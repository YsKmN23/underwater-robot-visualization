using System;
using System.Collections;
using UnderwaterRobotScene.Visualization.Runtime.Rov;
using UnderwaterRobotScene.Visualization.Runtime.Terrain;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.Visualization.Runtime
{
    public static class EnvE3DPlayerTerrainReadabilitySmokeVerifier
    {
        private const string LaunchArgument = "-envE3DPlayerSmoke";
        private const string FormalSceneName = "UnderwaterRobotDemo";
        private const string AuvRootName = "AUV_Yellow_Underwater";
        private const string MeshPassMarker =
            "ENV_E3D_PLAYER_ROV_CONTACT_MESH_READABLE_PASS";
        private const string AuthorityPassMarker =
            "ENV_E3D_PLAYER_TERRAIN_AUTHORITY_VALIDATION_PASS";
        private const string SamplePassMarker =
            "ENV_E3D_PLAYER_TERRAIN_SAMPLE_PASS";
        private const string MotionPassMarker =
            "ENV_E3D_PLAYER_AUV_MOTION_PASS";
        private const string NoFatalPassMarker =
            "ENV_E3D_PLAYER_NO_FATAL_ERROR_PASS";
        private const string FinalPassMarker =
            "ENV_E3D_PLAYER_TERRAIN_READABILITY_SMOKE_PASS";
        private const string FinalFailMarker =
            "ENV_E3D_PLAYER_TERRAIN_READABILITY_SMOKE_FAIL";
        private const double InitializationTimeoutSeconds = 15.0;
        private const double MotionObservationSeconds = 3.0;
        private const float MinimumMotionMeters = 0.02f;

        private static int fatalErrorCount;
        private static string firstFatalError = string.Empty;
        private static bool active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isEditor || !HasLaunchArgument())
                return;

            active = true;
            fatalErrorCount = 0;
            firstFatalError = string.Empty;
            Application.logMessageReceived += OnLogMessage;
            var verifierObject = new GameObject(
                "ENV_E3D_Player_Terrain_Readability_Smoke_Verifier");
            UnityEngine.Object.DontDestroyOnLoad(verifierObject);
            verifierObject.AddComponent<Runner>();
        }

        private static bool HasLaunchArgument()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length; index++)
            {
                if (string.Equals(
                        arguments[index], LaunchArgument,
                        StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static void OnLogMessage(
            string condition,
            string stackTrace,
            LogType type)
        {
            if (!active ||
                (type != LogType.Error &&
                 type != LogType.Exception &&
                 type != LogType.Assert))
                return;

            fatalErrorCount++;
            if (string.IsNullOrEmpty(firstFatalError))
            {
                firstFatalError = type + ": " + condition;
                if (!string.IsNullOrEmpty(stackTrace))
                    firstFatalError += " | " + stackTrace;
            }
        }

        private sealed class Runner : MonoBehaviour
        {
            private IEnumerator Start()
            {
                yield return RunSmoke();
            }

            private IEnumerator RunSmoke()
            {
                double deadline = Time.realtimeSinceStartupAsDouble +
                    InitializationTimeoutSeconds;
                RovTerrainContactConstraint rovConstraint = null;
                VehiclePoseDriver auvDriver = null;
                while (Time.realtimeSinceStartupAsDouble < deadline)
                {
                    if (fatalErrorCount > 0)
                    {
                        Fail("Fatal runtime log during initialization: " +
                            firstFatalError);
                        yield break;
                    }

                    Scene scene = SceneManager.GetActiveScene();
                    rovConstraint = FindRovConstraint();
                    auvDriver = FindAuvDriver();
                    if (scene.IsValid() && scene.isLoaded &&
                        string.Equals(scene.name, FormalSceneName,
                            StringComparison.Ordinal) &&
                        rovConstraint != null &&
                        rovConstraint.SurfaceSampler != null &&
                        auvDriver != null &&
                        auvDriver.TargetRoot != null &&
                        auvDriver.OwnsControl &&
                        auvDriver.HasFreshAppliedPose)
                    {
                        break;
                    }
                    yield return null;
                }

                if (rovConstraint == null ||
                    rovConstraint.SurfaceSampler == null)
                {
                    Fail("The real ROV contact terrain sampler was not ready.");
                    yield break;
                }
                if (auvDriver == null || auvDriver.TargetRoot == null ||
                    !auvDriver.OwnsControl ||
                    !auvDriver.HasFreshAppliedPose)
                {
                    Fail("The real AUV VehiclePoseDriver was not ready.");
                    yield break;
                }

                TerrainSurfaceSampler sampler =
                    rovConstraint.SurfaceSampler;
                MeshCollider collider = sampler.ContactTerrain;
                Mesh mesh = collider == null ? null : collider.sharedMesh;
                if (mesh == null || !mesh.isReadable)
                {
                    Fail("The serialized ROV contact mesh is missing or not " +
                        "CPU-readable in Player.");
                    yield break;
                }
                Debug.Log(MeshPassMarker +
                    " | mesh=" + mesh.name +
                    " | vertices=" + mesh.vertexCount);

                if (!sampler.TryValidateAuthority(
                        out TerrainAuthorityFailure authorityFailure) ||
                    authorityFailure != TerrainAuthorityFailure.None)
                {
                    Fail("TerrainAuthority validation failed: " +
                        authorityFailure + ".");
                    yield break;
                }
                Debug.Log(AuthorityPassMarker +
                    " | grid=" + sampler.AuthorityGridXCount + "x" +
                    sampler.AuthorityGridZCount +
                    " | spacing=" + sampler.AuthorityGridSpacing);

                Transform auvRoot = auvDriver.TargetRoot;
                Vector3 initialPosition = auvRoot.position;
                if (!sampler.TrySampleAtXZ(
                        initialPosition.x,
                        initialPosition.z,
                        out TerrainAuthoritySample terrainSample,
                        out TerrainAuthorityFailure sampleFailure))
                {
                    Fail("Runtime terrain sample at the AUV Movement Root " +
                        "failed: " + sampleFailure + ".");
                    yield break;
                }
                Debug.Log(SamplePassMarker +
                    " | queryXZ=(" + initialPosition.x + "," +
                    initialPosition.z + ")" +
                    " | point=" + terrainSample.WorldPoint +
                    " | triangle=" + terrainSample.TriangleIndex);

                double observationEnd = Time.realtimeSinceStartupAsDouble +
                    MotionObservationSeconds;
                float maximumDisplacement = 0f;
                while (Time.realtimeSinceStartupAsDouble < observationEnd)
                {
                    if (fatalErrorCount > 0)
                    {
                        Fail("Fatal runtime log during motion observation: " +
                            firstFatalError);
                        yield break;
                    }
                    maximumDisplacement = Mathf.Max(
                        maximumDisplacement,
                        Vector3.Distance(initialPosition, auvRoot.position));
                    yield return null;
                }

                if (maximumDisplacement < MinimumMotionMeters)
                {
                    Fail("The AUV business Movement Root displacement was " +
                        maximumDisplacement + "m; required at least " +
                        MinimumMotionMeters + "m.");
                    yield break;
                }
                Debug.Log(MotionPassMarker +
                    " | displacement=" + maximumDisplacement +
                    " | root=" + auvRoot.name);

                yield return null;
                if (fatalErrorCount > 0)
                {
                    Fail("Fatal runtime log before smoke completion: " +
                        firstFatalError);
                    yield break;
                }

                Debug.Log(NoFatalPassMarker + " | count=0");
                Debug.Log(FinalPassMarker);
                active = false;
                Application.logMessageReceived -= OnLogMessage;
                Application.Quit(0);
            }

            private static RovTerrainContactConstraint FindRovConstraint()
            {
                RovTerrainContactConstraint[] values =
                    UnityEngine.Object.FindObjectsByType<
                        RovTerrainContactConstraint>(
                        FindObjectsInactive.Include);
                for (int index = 0; index < values.Length; index++)
                {
                    if (values[index] != null &&
                        values[index].SurfaceSampler != null &&
                        values[index].SurfaceSampler.ContactTerrain != null)
                        return values[index];
                }
                return null;
            }

            private static VehiclePoseDriver FindAuvDriver()
            {
                VehiclePoseDriver[] values =
                    UnityEngine.Object.FindObjectsByType<VehiclePoseDriver>(
                        FindObjectsInactive.Include);
                for (int index = 0; index < values.Length; index++)
                {
                    VehiclePoseDriver value = values[index];
                    if (value != null && value.TargetRoot != null &&
                        string.Equals(
                            value.TargetRoot.name,
                            AuvRootName,
                            StringComparison.Ordinal))
                        return value;
                }
                return null;
            }

            private static void Fail(string reason)
            {
                active = false;
                Application.logMessageReceived -= OnLogMessage;
                Debug.LogError(FinalFailMarker + " | " + reason);
                Application.Quit(1);
            }
        }
    }
}
