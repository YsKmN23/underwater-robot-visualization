using System;
using System.Linq;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.LocalTesting;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Auv;
using UnderwaterRobotScene.Visualization.Runtime.Terrain;
using UnderwaterRobotScene.Visualization.Sampling;
using UnderwaterRobotScene.Visualization.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    public static class AuvPublicPoseN5SceneInstaller
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string AuvName = "AUV_Yellow_Underwater";
        private const string HostName = "AUV_PublicData_RuntimeHost";
        private const string DriverName = "AUV_PublicPoseDriver";

        [MenuItem("Tools/AUV Pose MVP/N5/Install Public Pose Integration")]
        public static void InstallFromMenu()
        {
            Install();
        }

        public static void RunBatch()
        {
            bool changed = Install();
            Debug.Log(
                "N5_SCENE_INSTALL_COMPLETE | changed=" +
                (changed ? "true" : "false") +
                " | " + ScenePath);
        }

        public static bool InstallForCanonicalSceneRebuild()
        {
            return InstallIntoLoadedScene(RequireComposableScene());
        }

        private static bool Install()
        {
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            Require(scene.IsValid() && scene.isLoaded,
                "The authoritative N5 scene could not be loaded.");
            Require(!scene.isDirty,
                "The N5 scene must be clean before installation.");
            bool changed = InstallIntoLoadedScene(scene);
            if (changed)
            {
                Require(EditorSceneManager.SaveScene(scene),
                    "Failed to save the N5 scene integration.");
                Require(!scene.isDirty,
                    "Saved N5 scene remained dirty.");
            }
            else
            {
                Require(!scene.isDirty,
                    "Idempotent N5 installation unexpectedly dirtied the scene.");
            }

            return changed;
        }

        private static bool InstallIntoLoadedScene(Scene scene)
        {
            GameObject auv = FindUniqueRoot(scene, AuvName);
            VehiclePoseControlAuthority[] authorities =
                auv.GetComponents<VehiclePoseControlAuthority>();
            Require(authorities.Length <= 1,
                "AUV movement root contains duplicate control Authorities.");

            GameObject hostObject = FindOptionalRoot(scene, HostName);
            if (hostObject != null)
            {
                RequireAllowedComponents(
                    hostObject,
                    typeof(VehicleDataRuntimeHost),
                    typeof(VehiclePoseIntegrationConfiguration));
                Require(
                    hostObject.GetComponents<VehicleDataRuntimeHost>().Length <= 1,
                    "AUV Host contains duplicate Runtime Host components.");
                Require(
                    hostObject.GetComponents<VehiclePoseIntegrationConfiguration>()
                        .Length <= 1,
                    "AUV Host contains duplicate Integration Configuration components.");
            }

            GameObject driverObject = FindOptionalRoot(scene, DriverName);
            if (driverObject != null)
            {
                RequireAllowedComponents(
                    driverObject,
                    typeof(VehiclePoseProfileConfiguration),
                    typeof(VehiclePoseDriver),
                    typeof(TerrainSurfaceSampler),
                    typeof(AuvTerrainClearanceConstraint));
                Require(
                    driverObject.GetComponents<VehiclePoseProfileConfiguration>()
                        .Length <= 1,
                    "AUV Driver root contains duplicate Profile components.");
                Require(
                    driverObject.GetComponents<VehiclePoseDriver>().Length <= 1,
                    "AUV Driver root contains duplicate Driver components.");
            }

            DemoMotionController[] demos =
                UnityEngine.Object.FindObjectsByType<DemoMotionController>(
                        FindObjectsInactive.Include)
                    .Where(item => item.gameObject.scene == scene)
                    .ToArray();
            Require(demos.Length == 1,
                "Expected exactly one DemoMotionController in the N5 scene.");
            DemoMotionController demo = demos[0];

            bool changed = false;
            VehiclePoseControlAuthority authority = authorities.Length == 0
                ? AddComponent<VehiclePoseControlAuthority>(auv, ref changed)
                : authorities[0];
            if (authority.Mode != VehiclePoseControlMode.PublicData)
            {
                authority.Mode = VehiclePoseControlMode.PublicData;
                changed = true;
            }

            hostObject = FindOrCreateRoot(scene, HostName, ref changed);
            VehicleDataRuntimeHost host =
                GetOrAddComponent<VehicleDataRuntimeHost>(
                    hostObject,
                    ref changed);
            VehiclePoseIntegrationConfiguration configuration =
                GetOrAddComponent<VehiclePoseIntegrationConfiguration>(
                    hostObject,
                    ref changed);
            if (!ConfigurationMatches(configuration, auv.transform.position))
            {
                configuration.ConfigureLocalTest(
                    "local-test-n5",
                    "AUV-01",
                    VehicleType.Auv,
                    DeterministicVehicleStateGeneratorKind.AuvIntegrationTrajectory,
                    auv.transform.position,
                    0.1f,
                    64,
                    0.75f,
                    8,
                    true,
                    0.15f,
                    0.25f,
                    0.25f,
                    0.000001f,
                    AfterLatestBehavior.HoldLatest,
                    true);
                changed = true;
            }

            driverObject = FindOrCreateRoot(scene, DriverName, ref changed);
            VehiclePoseProfileConfiguration profile =
                GetOrAddComponent<VehiclePoseProfileConfiguration>(
                    driverObject,
                    ref changed);
            if (!ProfileMatches(profile))
            {
                profile.Configure(
                    "N5_LOCAL_TEST_UNITY_AUV_Y_MINUS_90",
                    CoordinateProfilePreset.UnityNative,
                    1f,
                    AttitudeDirection.BodyToWorld,
                    SignedSemanticAxis.NegativeZ,
                    SignedSemanticAxis.PositiveY,
                    SignedSemanticAxis.PositiveX,
                    new Vector3(0f, -90f, 0f));
                changed = true;
            }

            if (!ReferenceEquals(host.IntegrationConfiguration, configuration) ||
                !ReferenceEquals(host.ProfileConfiguration, profile))
            {
                host.Configure(configuration, profile);
                changed = true;
            }

            VehiclePoseDriver driver =
                GetOrAddComponent<VehiclePoseDriver>(driverObject, ref changed);
            if (!ReferenceEquals(driver.RuntimeHost, host) ||
                !ReferenceEquals(driver.IntegrationConfiguration, configuration) ||
                !ReferenceEquals(driver.ProfileConfiguration, profile) ||
                !ReferenceEquals(driver.ControlAuthority, authority) ||
                !ReferenceEquals(driver.TargetRoot, auv.transform))
            {
                driver.Configure(
                    host,
                    configuration,
                    profile,
                    authority,
                    auv.transform);
                changed = true;
            }

            if (!ReferenceEquals(demo.auvControlAuthority, authority))
            {
                demo.auvControlAuthority = authority;
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(authority);
                EditorUtility.SetDirty(host);
                EditorUtility.SetDirty(configuration);
                EditorUtility.SetDirty(profile);
                EditorUtility.SetDirty(driver);
                EditorUtility.SetDirty(demo);
                EditorSceneManager.MarkSceneDirty(scene);
            }

            Require(authority.PublicDataOwnsControl,
                "AUV authority was not set to PublicData.");
            Require(driver.TargetRoot == auv.transform,
                "Driver target is not the AUV movement root.");
            Require(ReferenceEquals(host.IntegrationConfiguration, configuration) &&
                    ReferenceEquals(driver.IntegrationConfiguration, configuration),
                "Host and Driver do not share one Integration Configuration.");
            Require(ReferenceEquals(demo.auvControlAuthority, authority),
                "Demo controller does not share the AUV authority.");
            return changed;
        }

        private static Scene RequireComposableScene()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                "The N5 canonical installer cannot run in Play Mode.");
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded,
                "The authoritative N5 scene is not loaded.");
            Require(string.Equals(scene.path, ScenePath, StringComparison.Ordinal),
                "The active Scene is not the authoritative N5 scene.");
            Require(SceneManager.sceneCount == 1,
                "The authoritative N5 scene must be uniquely loaded.");
            return scene;
        }

        private static bool ConfigurationMatches(
            VehiclePoseIntegrationConfiguration value,
            Vector3 expectedOrigin)
        {
            return value.SourceId == "local-test-n5" &&
                   value.VehicleId == "AUV-01" &&
                   value.VehicleType == VehicleType.Auv &&
                   value.GeneratorKind ==
                   DeterministicVehicleStateGeneratorKind
                       .AuvIntegrationTrajectory &&
                   Near(value.TestOrigin, expectedOrigin, 0.000001f) &&
                   Near(value.SampleIntervalSeconds, 0.1) &&
                   value.StoreCapacity == 64 &&
                   Near(value.StaleTimeoutSeconds, 0.75) &&
                   value.MaxCatchUpStepsPerFrame == 8 &&
                   value.AutoStart &&
                   Near(value.RenderDelaySeconds, 0.15) &&
                   Near(value.MaxInterpolationGapSeconds, 0.25) &&
                   Near(value.MaxHoldSourceTimeSeconds, 0.25) &&
                   Near(value.ExactTimeToleranceSeconds, 0.000001) &&
                   value.AfterLatestBehavior == AfterLatestBehavior.HoldLatest &&
                   value.AllowSingleSampleHold;
        }

        private static bool ProfileMatches(
            VehiclePoseProfileConfiguration value)
        {
            return value.ProfileId ==
                   "N5_LOCAL_TEST_UNITY_AUV_Y_MINUS_90" &&
                   value.Preset == CoordinateProfilePreset.UnityNative &&
                   Mathf.Abs(value.PositionScale - 1f) <= 0.0000001f &&
                   value.AttitudeDirection == AttitudeDirection.BodyToWorld &&
                   value.ModelRight == SignedSemanticAxis.NegativeZ &&
                   value.ModelUp == SignedSemanticAxis.PositiveY &&
                   value.ModelForward == SignedSemanticAxis.PositiveX &&
                   Near(
                       value.ModelAlignmentEulerDegrees,
                       new Vector3(0f, -90f, 0f),
                       0.000001f);
        }

        private static void RequireAllowedComponents(
            GameObject value,
            params Type[] allowed)
        {
            Component[] components = value.GetComponents<Component>();
            Require(components.All(component =>
                    component is Transform ||
                    component != null && allowed.Contains(component.GetType())),
                value.name + " contains an unexpected component.");
        }

        private static T AddComponent<T>(
            GameObject value,
            ref bool changed)
            where T : Component
        {
            changed = true;
            return value.AddComponent<T>();
        }

        private static T GetOrAddComponent<T>(
            GameObject value,
            ref bool changed)
            where T : Component
        {
            T[] matches = value.GetComponents<T>();
            Require(matches.Length <= 1,
                value.name + " contains duplicate " + typeof(T).Name + " components.");
            return matches.Length == 1
                ? matches[0]
                : AddComponent<T>(value, ref changed);
        }

        private static GameObject FindUniqueRoot(Scene scene, string name)
        {
            GameObject result = FindOptionalRoot(scene, name);
            Require(result != null,
                "Expected exactly one root named " + name + ".");
            return result;
        }

        private static GameObject FindOptionalRoot(Scene scene, string name)
        {
            GameObject[] matches = scene.GetRootGameObjects()
                .Where(root => string.Equals(
                    root.name,
                    name,
                    StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length <= 1,
                "Duplicate scene roots named " + name + ".");
            return matches.Length == 0 ? null : matches[0];
        }

        private static GameObject FindOrCreateRoot(
            Scene scene,
            string name,
            ref bool changed)
        {
            GameObject existing = FindOptionalRoot(scene, name);
            if (existing != null)
            {
                return existing;
            }

            var created = new GameObject(name);
            SceneManager.MoveGameObjectToScene(created, scene);
            changed = true;
            return created;
        }

        private static bool Near(Vector3 left, Vector3 right, float tolerance)
        {
            return (left - right).sqrMagnitude <= tolerance * tolerance;
        }

        private static bool Near(double left, double right)
        {
            return Math.Abs(left - right) <= 0.0000001;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
