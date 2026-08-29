using System;
using System.Linq;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.LocalTesting;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Rov;
using UnderwaterRobotScene.Visualization.Sampling;
using UnderwaterRobotScene.Visualization.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    public static class RovRootPoseN6BSceneInstaller
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string RovName = "ROV_Box_Seabed";
        private const string RovModelName = "ROV_FineModel_V1_Imported";
        private const string HostName = "ROV_PublicData_RuntimeHost";
        private const string DriverName = "ROV_PublicPoseDriver";

        [MenuItem("Tools/Underwater Demo/N6-B/Install ROV Root Pose Integration")]
        public static void InstallFromMenu()
        {
            Install(false);
        }

        public static void RunBatch()
        {
            bool changed = Install(false);
            Debug.Log(
                "N6B_ROV_SCENE_INSTALL_COMPLETE | changed=" +
                (changed ? "true" : "false") +
                " | " + ScenePath);
        }

        public static bool InstallForCanonicalSceneRebuild()
        {
            return InstallIntoLoadedScene(RequireComposableScene(), true);
        }

        private static bool Install(bool allowCanonicalPostBuildComponents)
        {
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            Require(scene.IsValid() && scene.isLoaded,
                "The authoritative N6-B scene could not be loaded.");
            Require(!scene.isDirty,
                "The N6-B scene must be clean before installation.");
            bool changed = InstallIntoLoadedScene(
                scene,
                allowCanonicalPostBuildComponents);
            if (changed)
            {
                Require(EditorSceneManager.SaveScene(scene),
                    "Failed to save the N6-B scene integration.");
                Require(!scene.isDirty,
                    "Saved N6-B scene remained dirty.");
            }
            else
            {
                Require(!scene.isDirty,
                    "Idempotent N6-B installation unexpectedly dirtied the scene.");
            }

            return changed;
        }

        private static bool InstallIntoLoadedScene(
            Scene scene,
            bool allowCanonicalPostBuildComponents)
        {
            GameObject rov = FindUniqueRoot(scene, RovName);
            Require(rov.transform.childCount == 1 &&
                    string.Equals(
                        rov.transform.GetChild(0).name,
                        RovModelName,
                        StringComparison.Ordinal),
                "ROV movement root must contain only the imported ROV model.");

            Component[] rootComponents = rov.GetComponents<Component>();
            Require(rootComponents.All(component =>
                    component is Transform ||
                    component is VehiclePoseControlAuthority ||
                    allowCanonicalPostBuildComponents &&
                    component is RovThrusterVisualCoordinator),
                "ROV movement root contains an unexpected component.");
            VehiclePoseControlAuthority[] authorities =
                rov.GetComponents<VehiclePoseControlAuthority>();
            Require(authorities.Length <= 1,
                "ROV movement root contains duplicate control Authorities.");
            RovThrusterVisualCoordinator[] coordinators =
                rov.GetComponents<RovThrusterVisualCoordinator>();
            Require(
                allowCanonicalPostBuildComponents
                    ? coordinators.Length <= 1
                    : coordinators.Length == 0,
                allowCanonicalPostBuildComponents
                    ? "ROV movement root contains duplicate visual thruster Coordinators."
                    : "ROV movement root contains an unexpected component.");

            GameObject hostObject = FindOptionalRoot(scene, HostName);
            if (hostObject != null)
            {
                RequireAllowedComponents(
                    hostObject,
                    typeof(VehicleDataRuntimeHost),
                    typeof(VehiclePoseIntegrationConfiguration));
                Require(
                    hostObject.GetComponents<VehicleDataRuntimeHost>().Length <= 1,
                    "ROV Host contains duplicate Runtime Host components.");
                Require(
                    hostObject.GetComponents<VehiclePoseIntegrationConfiguration>()
                        .Length <= 1,
                    "ROV Host contains duplicate Integration Configuration components.");
            }

            GameObject driverObject = FindOptionalRoot(scene, DriverName);
            if (driverObject != null)
            {
                RequireAllowedComponents(
                    driverObject,
                    typeof(VehiclePoseProfileConfiguration),
                    typeof(VehiclePoseDriver));
                Require(
                    driverObject.GetComponents<VehiclePoseProfileConfiguration>()
                        .Length <= 1,
                    "ROV Driver root contains duplicate Profile components.");
                Require(
                    driverObject.GetComponents<VehiclePoseDriver>().Length <= 1,
                    "ROV Driver root contains duplicate Driver components.");
            }

            DemoMotionController[] demos =
                UnityEngine.Object.FindObjectsByType<DemoMotionController>(
                        FindObjectsInactive.Include)
                    .Where(item => item.gameObject.scene == scene)
                    .ToArray();
            Require(demos.Length == 1,
                "Expected exactly one DemoMotionController in the N6-B scene.");
            DemoMotionController demo = demos[0];

            bool changed = false;
            VehiclePoseControlAuthority authority = authorities.Length == 0
                ? AddComponent<VehiclePoseControlAuthority>(rov, ref changed)
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
            if (!ConfigurationMatches(configuration, rov.transform.position))
            {
                configuration.ConfigureLocalTest(
                    "local-test-rov-n6b",
                    "ROV-01",
                    VehicleType.Rov,
                    DeterministicVehicleStateGeneratorKind.RovDiagnosticTrajectory,
                    rov.transform.position,
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
                    "ROV_LOCAL_TEST_UNITY_Y_MINUS_90",
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
                !ReferenceEquals(driver.TargetRoot, rov.transform))
            {
                driver.Configure(
                    host,
                    configuration,
                    profile,
                    authority,
                    rov.transform);
                changed = true;
            }

            if (!ReferenceEquals(demo.rovControlAuthority, authority))
            {
                demo.rovControlAuthority = authority;
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
                "ROV authority was not set to PublicData.");
            Require(driver.TargetRoot == rov.transform,
                "ROV Driver target is not the movement root.");
            Require(ReferenceEquals(host.IntegrationConfiguration, configuration) &&
                    ReferenceEquals(driver.IntegrationConfiguration, configuration),
                "ROV Host and Driver do not share one ROV Integration Configuration.");
            Require(ReferenceEquals(demo.rovControlAuthority, authority),
                "Demo controller does not share the ROV authority.");
            return changed;
        }

        private static Scene RequireComposableScene()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                "The N6-B canonical installer cannot run in Play Mode.");
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded,
                "The authoritative N6-B scene is not loaded.");
            Require(string.Equals(scene.path, ScenePath, StringComparison.Ordinal),
                "The active Scene is not the authoritative N6-B scene.");
            Require(SceneManager.sceneCount == 1,
                "The authoritative N6-B scene must be uniquely loaded.");
            return scene;
        }

        private static bool ConfigurationMatches(
            VehiclePoseIntegrationConfiguration value,
            Vector3 expectedOrigin)
        {
            return value.SourceId == "local-test-rov-n6b" &&
                   value.VehicleId == "ROV-01" &&
                   value.VehicleType == VehicleType.Rov &&
                   value.GeneratorKind ==
                   DeterministicVehicleStateGeneratorKind.RovDiagnosticTrajectory &&
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
            return value.ProfileId == "ROV_LOCAL_TEST_UNITY_Y_MINUS_90" &&
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
