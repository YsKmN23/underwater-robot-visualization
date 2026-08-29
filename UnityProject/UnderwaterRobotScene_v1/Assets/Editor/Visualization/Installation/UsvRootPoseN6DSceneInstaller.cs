using System;
using System.Linq;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.LocalTesting;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Usv;
using UnderwaterRobotScene.Visualization.Sampling;
using UnderwaterRobotScene.Visualization.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    public static class UsvRootPoseN6DSceneInstaller
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string UsvName = "USV_Blue_Surface";
        private const string UsvModelName = "USV_FineModel_V1_Imported";
        private const string VisualRootName = "USV_SurfaceVisualRoot";
        private const string HostName = "USV_PublicData_RuntimeHost";
        private const string DriverName = "USV_PublicPoseDriver";

        [MenuItem("Tools/Underwater Demo/N6-D/Install USV Root Pose Integration")]
        public static void InstallFromMenu()
        {
            Install();
        }

        public static void RunBatch()
        {
            bool changed = Install();
            Debug.Log(
                "N6D_USV_SCENE_INSTALL_COMPLETE | changed=" +
                (changed ? "true" : "false") +
                " | " + ScenePath);
        }

        public static bool Install()
        {
            return Install(false);
        }

        public static bool InstallForCanonicalPostBuildChain()
        {
            return InstallIntoLoadedScene(RequireComposableScene(), true);
        }

        private static bool Install(bool allowCanonicalPostBuildComponents)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Require(!scene.isDirty, "N6-D scene was dirty before installation.");
            bool changed = InstallIntoLoadedScene(
                scene,
                allowCanonicalPostBuildComponents);
            if (changed)
            {
                Require(EditorSceneManager.SaveScene(scene),
                    "Failed to save the N6-D scene integration.");
                AssetDatabase.SaveAssets();
            }
            else
            {
                Require(!scene.isDirty,
                    "Idempotent N6-D install unexpectedly dirtied the Scene.");
            }

            return changed;
        }

        private static bool InstallIntoLoadedScene(
            Scene scene,
            bool allowCanonicalPostBuildComponents)
        {
            GameObject usv = FindUniqueRoot(scene, UsvName);
            Transform model = RequireUniqueDescendant(usv.transform, UsvModelName);
            ValidateCompatibleHierarchy(usv.transform, model);
            Require(usv.GetComponents<Component>().All(component =>
                    component is Transform ||
                    component is VehiclePoseControlAuthority ||
                    (allowCanonicalPostBuildComponents &&
                     component is UsvActuatorVisualCoordinator)),
                "USV movement root contains an unexpected component.");

            bool changed = false;
            VehiclePoseControlAuthority authority =
                usv.GetComponent<VehiclePoseControlAuthority>();
            if (authority == null)
            {
                authority = usv.AddComponent<VehiclePoseControlAuthority>();
                changed = true;
            }
            if (!authority.PublicDataOwnsControl)
            {
                authority.Mode = VehiclePoseControlMode.PublicData;
                changed = true;
            }

            GameObject hostObject = FindOrCreateRoot(scene, HostName, ref changed);
            VehicleDataRuntimeHost host = hostObject.GetComponent<VehicleDataRuntimeHost>();
            if (host == null)
            {
                host = hostObject.AddComponent<VehicleDataRuntimeHost>();
                changed = true;
            }
            VehiclePoseIntegrationConfiguration integrationConfiguration =
                hostObject.GetComponent<VehiclePoseIntegrationConfiguration>();
            if (integrationConfiguration == null)
            {
                integrationConfiguration =
                    hostObject.AddComponent<VehiclePoseIntegrationConfiguration>();
                changed = true;
            }
            Require(
                hostObject.GetComponents<VehiclePoseIntegrationConfiguration>().Length == 1,
                "USV Host contains duplicate Integration Configuration components.");
            if (!ConfigurationMatches(integrationConfiguration, usv.transform.position))
            {
                integrationConfiguration.ConfigureLocalTest(
                    "local-test-usv-n6d",
                    "USV-01",
                    VehicleType.Usv,
                    DeterministicVehicleStateGeneratorKind.UsvDiagnosticTrajectory,
                    usv.transform.position,
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

            GameObject driverObject = FindOrCreateRoot(scene, DriverName, ref changed);
            VehiclePoseProfileConfiguration profile =
                driverObject.GetComponent<VehiclePoseProfileConfiguration>();
            if (profile == null)
            {
                profile = driverObject.AddComponent<VehiclePoseProfileConfiguration>();
                changed = true;
            }
            if (!ProfileMatches(profile))
            {
                profile.Configure(
                    "USV_LOCAL_TEST_UNITY_Y_PLUS_90",
                    CoordinateProfilePreset.UnityNative,
                    1f,
                    AttitudeDirection.BodyToWorld,
                    SignedSemanticAxis.PositiveZ,
                    SignedSemanticAxis.PositiveY,
                    SignedSemanticAxis.NegativeX,
                    new Vector3(0f, 90f, 0f));
                changed = true;
            }

            if (!ReferenceEquals(host.IntegrationConfiguration, integrationConfiguration) ||
                !ReferenceEquals(host.ProfileConfiguration, profile))
            {
                host.Configure(integrationConfiguration, profile);
                changed = true;
            }

            VehiclePoseDriver driver = driverObject.GetComponent<VehiclePoseDriver>();
            if (driver == null)
            {
                driver = driverObject.AddComponent<VehiclePoseDriver>();
                changed = true;
            }
            if (!ReferenceEquals(driver.RuntimeHost, host) ||
                !ReferenceEquals(
                    driver.IntegrationConfiguration,
                    integrationConfiguration) ||
                !ReferenceEquals(driver.ProfileConfiguration, profile) ||
                !ReferenceEquals(driver.ControlAuthority, authority) ||
                driver.TargetRoot != usv.transform)
            {
                driver.Configure(
                    host,
                    integrationConfiguration,
                    profile,
                    authority,
                    usv.transform);
                changed = true;
            }

            DemoMotionController[] demos =
                UnityEngine.Object.FindObjectsByType<DemoMotionController>(
                        FindObjectsInactive.Include)
                    .Where(item => item.gameObject.scene == scene)
                    .ToArray();
            Require(demos.Length == 1, "Expected exactly one DemoMotionController.");
            DemoMotionController demo = demos[0];
            if (!ReferenceEquals(demo.usvControlAuthority, authority))
            {
                demo.usvControlAuthority = authority;
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(authority);
                EditorUtility.SetDirty(host);
                EditorUtility.SetDirty(integrationConfiguration);
                EditorUtility.SetDirty(profile);
                EditorUtility.SetDirty(driver);
                EditorUtility.SetDirty(demo);
                EditorSceneManager.MarkSceneDirty(scene);
            }

            ValidateCompatibleHierarchy(usv.transform, model);
            Require(authority.PublicDataOwnsControl,
                "USV authority was not set to PublicData.");
            Require(driver.TargetRoot == usv.transform,
                "USV Driver target is not the movement root.");
            Require(ReferenceEquals(host.IntegrationConfiguration, integrationConfiguration) &&
                    ReferenceEquals(driver.IntegrationConfiguration, integrationConfiguration),
                "USV Host and Driver do not share one USV Integration Configuration.");
            Require(ReferenceEquals(demo.usvControlAuthority, authority),
                "Demo controller does not share the USV authority.");
            return changed;
        }

        private static Scene RequireComposableScene()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                "The N6-D canonical installer cannot run in Play Mode.");
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded,
                "The authoritative N6-D Scene is not loaded.");
            Require(string.Equals(scene.path, ScenePath, StringComparison.Ordinal),
                "The active Scene is not the authoritative N6-D Scene.");
            Require(SceneManager.sceneCount == 1,
                "The authoritative N6-D Scene must be uniquely loaded.");
            return scene;
        }

        private static void ValidateCompatibleHierarchy(Transform usv, Transform model)
        {
            if (model.parent == usv)
            {
                Require(usv.childCount == 1,
                    "Legacy USV root must contain only the imported model.");
                return;
            }

            Transform visualRoot = model.parent;
            Require(visualRoot != null &&
                    visualRoot.parent == usv &&
                    string.Equals(
                        visualRoot.name,
                        VisualRootName,
                        StringComparison.Ordinal) &&
                    usv.childCount == 1 &&
                    visualRoot.childCount == 1 &&
                    Near(visualRoot.localPosition, Vector3.zero, 0.000001f) &&
                    Near(visualRoot.localRotation, Quaternion.identity, 0.000001f) &&
                    Near(visualRoot.localScale, Vector3.one, 0.000001f),
                "USV hierarchy is neither legacy nor canonical.");
        }

        private static bool ConfigurationMatches(
            VehiclePoseIntegrationConfiguration value,
            Vector3 origin)
        {
            return value.SourceId == "local-test-usv-n6d" &&
                   value.VehicleId == "USV-01" &&
                   value.VehicleType == VehicleType.Usv &&
                   value.GeneratorKind ==
                       DeterministicVehicleStateGeneratorKind.UsvDiagnosticTrajectory &&
                   Near(value.TestOrigin, origin, 0.000001f) &&
                   Math.Abs(value.SampleIntervalSeconds - 0.1) <= 0.0000001 &&
                   value.StoreCapacity == 64 &&
                   Math.Abs(value.StaleTimeoutSeconds - 0.75) <= 0.0000001 &&
                   value.MaxCatchUpStepsPerFrame == 8 &&
                   value.AutoStart &&
                   Math.Abs(value.RenderDelaySeconds - 0.15) <= 0.0000001 &&
                   Math.Abs(value.MaxInterpolationGapSeconds - 0.25) <= 0.0000001 &&
                   Math.Abs(value.MaxHoldSourceTimeSeconds - 0.25) <= 0.0000001 &&
                   Math.Abs(value.ExactTimeToleranceSeconds - 0.000001) <= 0.00000001 &&
                   value.AfterLatestBehavior == AfterLatestBehavior.HoldLatest &&
                   value.AllowSingleSampleHold;
        }

        private static bool ProfileMatches(VehiclePoseProfileConfiguration value)
        {
            return value.ProfileId == "USV_LOCAL_TEST_UNITY_Y_PLUS_90" &&
                   value.Preset == CoordinateProfilePreset.UnityNative &&
                   Math.Abs(value.PositionScale - 1f) <= 0.000001f &&
                   value.AttitudeDirection == AttitudeDirection.BodyToWorld &&
                   value.ModelRight == SignedSemanticAxis.PositiveZ &&
                   value.ModelUp == SignedSemanticAxis.PositiveY &&
                   value.ModelForward == SignedSemanticAxis.NegativeX &&
                   Near(
                       value.ModelAlignmentEulerDegrees,
                       new Vector3(0f, 90f, 0f),
                       0.000001f);
        }

        private static GameObject FindUniqueRoot(Scene scene, string name)
        {
            GameObject[] matches = scene.GetRootGameObjects()
                .Where(root => string.Equals(root.name, name, StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1,
                "Expected exactly one root named " + name + ".");
            return matches[0];
        }

        private static GameObject FindOrCreateRoot(
            Scene scene,
            string name,
            ref bool changed)
        {
            GameObject[] matches = scene.GetRootGameObjects()
                .Where(root => string.Equals(root.name, name, StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length <= 1, "Duplicate scene roots named " + name + ".");
            if (matches.Length == 1)
            {
                return matches[0];
            }

            var created = new GameObject(name);
            SceneManager.MoveGameObjectToScene(created, scene);
            changed = true;
            return created;
        }

        private static Transform RequireUniqueDescendant(Transform root, string name)
        {
            Transform[] matches = root.GetComponentsInChildren<Transform>(true)
                .Where(item => string.Equals(item.name, name, StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1,
                "Expected exactly one descendant named " + name + ".");
            return matches[0];
        }

        private static bool Near(Vector3 left, Vector3 right, float tolerance)
        {
            return (left - right).sqrMagnitude <= tolerance * tolerance;
        }

        private static bool Near(Quaternion left, Quaternion right, float tolerance)
        {
            return Mathf.Abs(Quaternion.Dot(left.normalized, right.normalized)) >=
                   1f - tolerance;
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
