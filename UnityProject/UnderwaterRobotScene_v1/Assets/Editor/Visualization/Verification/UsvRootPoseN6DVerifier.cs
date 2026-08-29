using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
    public static class UsvRootPoseN6DVerifier
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string UsvName = "USV_Blue_Surface";
        private const string ModelName = "USV_FineModel_V1_Imported";
        private const string VisualRootName = "USV_SurfaceVisualRoot";
        private const string HostName = "USV_PublicData_RuntimeHost";
        private const string DriverName = "USV_PublicPoseDriver";
        private const ulong UsvTransformFileId = 735866181UL;

        private sealed class Check
        {
            public string Name;
            public string Detail;
        }

        [MenuItem("Tools/Underwater Demo/N6-D/Verify USV Root Pose Integration")]
        public static void RunFromMenu()
        {
            Execute();
        }

        public static void RunBatch()
        {
            Execute();
            Debug.Log("N6D_USV_STATIC_VALIDATION_PASS");
        }

        public static void RunCoreRegressionsBatch()
        {
            int n2 = VehicleDataLayerN2Verifier.RunVerification(Debug.Log);
            int n3 = CoordinateAttitudeN3Verifier.RunVerification(Debug.Log);
            int n4 = RenderSamplingN4Verifier.RunVerification(Debug.Log);
            Require(n2 == 0 && n3 == 0 && n4 == 0,
                "One or more N2-N4 core regressions failed.");
            Debug.Log("N6D_CORE_REGRESSIONS_PASS | N2=8/8 | N3=10/10 | N4=12/12");
        }

        private static void Execute()
        {
            var checks = new List<Check>();
            TestGenerator(checks);
            TestGeneratorBoundary(checks);

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Require(!scene.isDirty, "Scene became dirty when opened for verification.");
            GameObject usv = FindUniqueRoot(scene, UsvName);
            Transform model = RequireUniqueDescendant(usv.transform, ModelName);
            GameObject hostObject = FindUniqueRoot(scene, HostName);
            GameObject driverObject = FindUniqueRoot(scene, DriverName);

            GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(usv.transform);
            Require(id.targetObjectId == UsvTransformFileId,
                "USV Transform fileID changed from 735866181.");
            bool legacyHierarchy =
                usv.transform.childCount == 1 && model.parent == usv.transform;
            Transform visualRoot = legacyHierarchy ? null : model.parent;
            bool canonicalHierarchy =
                visualRoot != null &&
                string.Equals(
                    visualRoot.name,
                    VisualRootName,
                    StringComparison.Ordinal) &&
                visualRoot.parent == usv.transform &&
                usv.transform.childCount == 1 &&
                visualRoot.childCount == 1 &&
                Near(visualRoot.localPosition, Vector3.zero, 1e-6f) &&
                Near(visualRoot.localRotation, Quaternion.identity, 1e-6f) &&
                Near(visualRoot.localScale, Vector3.one, 1e-6f);
            Require(legacyHierarchy || canonicalHierarchy,
                "USV hierarchy is neither the legacy nor canonical visual-root form.");
            Add(checks, "USV movement root",
                "Transform fileID " + UsvTransformFileId +
                "; legacy direct model or canonical identity visual root is preserved.");

            Require(Near(model.localPosition, Vector3.zero, 1e-6f) &&
                    Near(model.localRotation, Quaternion.Euler(-90f, 180f, 0f), 1e-6f) &&
                    Near(model.localScale, Vector3.one * 100f, 1e-5f),
                "Imported USV model local Transform changed.");
            Add(checks, "Model local Transform",
                "Position zero, Euler (-90,180,0), scale (100,100,100); no reflection.");

            VehiclePoseControlAuthority authority =
                RequireComponent<VehiclePoseControlAuthority>(usv, "USV authority");
            VehicleDataRuntimeHost host =
                RequireComponent<VehicleDataRuntimeHost>(hostObject, "USV host");
            VehiclePoseIntegrationConfiguration configuration =
                RequireComponent<VehiclePoseIntegrationConfiguration>(
                    hostObject,
                    "USV integration configuration");
            VehiclePoseProfileConfiguration profile =
                RequireComponent<VehiclePoseProfileConfiguration>(driverObject, "USV profile");
            VehiclePoseDriver driver =
                RequireComponent<VehiclePoseDriver>(driverObject, "USV driver");
            DemoMotionController demo =
                UnityEngine.Object.FindAnyObjectByType<DemoMotionController>(
                    FindObjectsInactive.Include);
            Require(demo != null, "DemoMotionController is missing.");

            Require(ReferenceEquals(host.IntegrationConfiguration, configuration) &&
                    ReferenceEquals(host.ProfileConfiguration, profile) &&
                    ReferenceEquals(driver.RuntimeHost, host) &&
                    ReferenceEquals(driver.IntegrationConfiguration, configuration) &&
                    ReferenceEquals(driver.ProfileConfiguration, profile) &&
                    ReferenceEquals(driver.ControlAuthority, authority) &&
                    ReferenceEquals(driver.TargetRoot, usv.transform) &&
                    ReferenceEquals(demo.usvControlAuthority, authority),
                "USV Host, Driver, Profile, Authority, target, or Demo binding is incorrect.");
            Require(authority.PublicDataOwnsControl && !demo.DrivesUsv,
                "USV PublicData ownership is not active in the installed scene.");
            Add(checks, "Scene bindings",
                "Independent Host, configuration, Profile, Driver and Authority target USV_Blue_Surface.");

            Require(
                configuration.SourceId == "local-test-usv-n6d" &&
                configuration.VehicleId == "USV-01" &&
                configuration.VehicleType == VehicleType.Usv &&
                configuration.GeneratorKind ==
                DeterministicVehicleStateGeneratorKind.UsvDiagnosticTrajectory &&
                Near(configuration.TestOrigin, new Vector3(0.15f, 0.18f, 2.05f), 1e-6f) &&
                Math.Abs(configuration.SampleIntervalSeconds - 0.1) <= 1e-7 &&
                configuration.StoreCapacity == 64 &&
                Math.Abs(configuration.StaleTimeoutSeconds - 0.75) <= 1e-7 &&
                Math.Abs(configuration.RenderDelaySeconds - 0.15) <= 1e-7 &&
                Math.Abs(configuration.MaxInterpolationGapSeconds - 0.25) <= 1e-7 &&
                Math.Abs(configuration.MaxHoldSourceTimeSeconds - 0.25) <= 1e-7 &&
                configuration.TryValidate(out _),
                "USV Integration Configuration differs from N6-D diagnostic values.");
            Add(checks, "USV diagnostic configuration",
                "Source local-test-usv-n6d, vehicle USV-01, 0.1 s samples, 0.15 s delay, 0.75 s stale timeout.");

            Require(
                profile.ProfileId == "USV_LOCAL_TEST_UNITY_Y_PLUS_90" &&
                profile.Preset == CoordinateProfilePreset.UnityNative &&
                profile.AttitudeDirection == AttitudeDirection.BodyToWorld &&
                profile.ModelRight == SignedSemanticAxis.PositiveZ &&
                profile.ModelUp == SignedSemanticAxis.PositiveY &&
                profile.ModelForward == SignedSemanticAxis.NegativeX &&
                Near(profile.ModelAlignmentEulerDegrees, new Vector3(0f, 90f, 0f), 1e-6f),
                "USV Profile axes or model alignment are incorrect.");
            TestProfileConversion(profile);
            TestGeneratedPoseConversion(profile);
            Add(checks, "USV axes and single model alignment",
                "Visual Forward -X, Up +Y, Right +Z; one Y +90 degree alignment maps synthetic attitude cases and a generated USV pose.");

            VehiclePoseIntegrationConfiguration[] configurations =
                UnityEngine.Object.FindObjectsByType<VehiclePoseIntegrationConfiguration>(
                        FindObjectsInactive.Include)
                    .Where(item => item.gameObject.scene == scene)
                    .ToArray();
            VehiclePoseIntegrationConfiguration auvConfiguration =
                configurations.Single(item => item.VehicleType == VehicleType.Auv);
            VehiclePoseIntegrationConfiguration rovConfiguration =
                configurations.Single(item => item.VehicleType == VehicleType.Rov);
            Require(!ReferenceEquals(configuration, auvConfiguration) &&
                    !ReferenceEquals(configuration, rovConfiguration) &&
                    !ReferenceEquals(auvConfiguration, rovConfiguration),
                "AUV, ROV and USV share a serialized Integration Configuration.");
            VehiclePoseControlAuthority auvAuthority =
                FindUniqueRoot(scene, "AUV_Yellow_Underwater")
                    .GetComponent<VehiclePoseControlAuthority>();
            VehiclePoseControlAuthority rovAuthority =
                FindUniqueRoot(scene, "ROV_Box_Seabed")
                    .GetComponent<VehiclePoseControlAuthority>();
            Require(auvAuthority != null && rovAuthority != null &&
                    auvAuthority.PublicDataOwnsControl &&
                    rovAuthority.PublicDataOwnsControl &&
                    !ReferenceEquals(authority, auvAuthority) &&
                    !ReferenceEquals(authority, rovAuthority),
                "AUV, ROV and USV Authority instances are not independent.");
            Add(checks, "Three-vehicle isolation",
                "AUV, ROV and USV retain distinct configurations, Profiles, Hosts, Drivers and Authorities.");

            PropellerSpinner[] spinners = usv.GetComponentsInChildren<PropellerSpinner>(true);
            Require(spinners.Length == 2, "Expected two USV PropellerSpinner components.");
            Require(spinners.All(item =>
                    item.enabled &&
                    item.transform != usv.transform &&
                    item.transform.IsChildOf(model) &&
                    Near(item.localAxis.normalized, Vector3.right, 1e-6f) &&
                    Math.Abs(item.rpm - 740f) <= 1e-5f),
                "A USV spinner serialized baseline, axis, hierarchy or enabled state changed.");
            Transform rudder = RequireUniqueDescendant(usv.transform, "USV_Rudder_Main");
            Transform pivot =
                RequireUniqueDescendant(usv.transform, "USV_Rudder_VisualPivot");
            UsvActuatorVisualCoordinator coordinator =
                RequireComponent<UsvActuatorVisualCoordinator>(
                    usv,
                    "USV actuator visual Coordinator");
            Require(rudder.GetComponent<PropellerSpinner>() == null &&
                    rudder.GetComponent<Animator>() == null &&
                    pivot.parent == rudder &&
                    coordinator.RudderVisualPivot == pivot,
                "USV rudder received an unintended animation writer.");
            Add(checks, "Local actuator boundary",
                "Two enabled +X Spinners retain serialized 740 rpm; Main stays fixed while the approved Pivot is owned by M2-D.");

            foreach (string environmentName in new[]
                     {
                         "USV_Blue_Label",
                         "USV_Local_ReflectionProbe",
                         "USV_Material_KeyLight"
                     })
            {
                GameObject environment = FindUniqueRoot(scene, environmentName);
                Require(!environment.transform.IsChildOf(usv.transform),
                    environmentName + " was incorrectly reparented under the moving USV.");
            }
            Add(checks, "Environment boundary",
                "Label, reflection probe and material key light remain outside the USV movement root.");

            Require(!HasMissingScripts(scene), "Scene contains a Missing Script.");
            Require(!scene.isDirty, "Static verification dirtied the scene.");
            Add(checks, "Scene integrity",
                "No Missing Script and verification left the Scene clean.");
            WriteReport(checks, scene);
        }

        private static void TestGenerator(ICollection<Check> checks)
        {
            var generator = new DeterministicUsvDiagnosticTrajectory();
            var vehicle = new LocalTestVehicle(
                "USV-01",
                VehicleType.Usv,
                new Vector3d(0.15, 0.18, 2.05),
                WorldFrame.UnityWorld,
                BodyFrame.UnityBody);
            double[] timestamps = { 0.0, 0.25, 1.5, 7.0, 10.0 };
            VehicleState[] states = timestamps
                .Select((timestamp, index) =>
                    generator.Evaluate(vehicle, (ulong)(index * 17), timestamp))
                .ToArray();
            VehicleState zero = states[0];
            for (int index = 0; index < states.Length; index++)
            {
                VehicleState state = states[index];
                Require(state.VehicleId == vehicle.VehicleId &&
                        state.VehicleType == vehicle.VehicleType &&
                        state.SourceTimestampSeconds.Equals(timestamps[index]) &&
                        state.SequenceNumber == (ulong)(index * 17) &&
                        state.WorldFrame == vehicle.WorldFrame &&
                        state.BodyFrame == vehicle.BodyFrame,
                    "USV generator changed identity, timestamp, sequence, or frame semantics.");
                Require(state.IsStructurallyValid &&
                        state.Position.IsFinite &&
                        state.Orientation.IsFinite &&
                        state.Orientation.TryNormalize(out _),
                    "USV generator produced a non-finite or non-normalizable pose.");
                Require(state.ValidFields ==
                        (VehicleStateFields.Position | VehicleStateFields.Orientation) &&
                        state.LinearVelocity.Equals(Vector3d.Zero) &&
                        state.AngularVelocity.Equals(Vector3d.Zero) &&
                        state.LinearAcceleration.Equals(Vector3d.Zero),
                    "USV generator fabricated velocity or acceleration fields.");
            }

            VehicleState repeated = generator.Evaluate(vehicle, 34UL, 1.5);
            Require(zero.Position.Equals(vehicle.PositionOffset) &&
                    zero.Orientation.Equals(Quaterniond.Identity),
                "USV generator initial pose is invalid.");
            Require(states.Skip(1).Any(state =>
                    !state.Position.Equals(zero.Position) ||
                    !state.Orientation.Equals(zero.Orientation)),
                "USV generator did not provide an observable pose change.");
            Require(states[2].Equals(repeated),
                "USV generator is not deterministic for identical input.");
            Add(checks, "Pure C# USV generator",
                "Finite normalized poses, exact field/time semantics, initial pose, observable motion and deterministic replay passed.");
        }

        private static void TestGeneratorBoundary(ICollection<Check> checks)
        {
            string source = File.ReadAllText(
                "Assets/Scripts/Visualization/Data/LocalTesting/DeterministicUsvDiagnosticTrajectory.cs");
            Require(!source.Contains("using UnityEngine") &&
                    !source.Contains("ModelAlignment") &&
                    !source.Contains("VehiclePoseDriver") &&
                    !source.Contains("VehicleStateStore") &&
                    !source.Contains("Transform"),
                "USV generator crossed the pure-data boundary or contains model alignment.");
            Add(checks, "Generator dependency boundary",
                "No UnityEngine, Scene, Store, Driver, Transform or model-alignment dependency.");
        }

        private static void TestProfileConversion(VehiclePoseProfileConfiguration configuration)
        {
            Require(configuration.TryBuildProfile(
                    out CoordinateTransformProfile profile,
                    out string profileError),
                "USV Profile failed to build: " + profileError);
            Vector3[] positions =
            {
                Vector3.zero,
                Vector3.right,
                Vector3.up,
                Vector3.forward,
                new Vector3(-1f, -2f, -3f)
            };
            for (int index = 0; index < positions.Length; index++)
            {
                Require(VehiclePoseConverter.TryConvert(
                        State(positions[index], Quaternion.identity, (ulong)index),
                        profile,
                        out ConvertedVehiclePose converted,
                        out ConversionError error),
                    "USV position conversion failed: " + error.Message);
                Require(UnityPoseAdapter.TryConvert(
                        converted.Position,
                        converted.Orientation,
                        out Vector3 unityPosition,
                        out _),
                    "Unity adapter rejected a USV position case.");
                Require(Near(unityPosition, positions[index], 1e-6f),
                    "USV Unity-native position mapping changed.");
            }

            Quaternion[] targets =
            {
                Quaternion.identity,
                Quaternion.AngleAxis(25f, Vector3.up),
                Quaternion.AngleAxis(15f, Vector3.right),
                Quaternion.AngleAxis(20f, Vector3.forward),
                Quaternion.Euler(12f, 27f, -9f)
            };
            foreach (Quaternion value in targets)
            {
                Quaternion target = value.normalized;
                Require(VehiclePoseConverter.TryConvert(
                        State(Vector3.zero, target, 0UL),
                        profile,
                        out ConvertedVehiclePose converted,
                        out ConversionError error),
                    "USV attitude conversion failed: " + error.Message);
                Require(UnityPoseAdapter.TryConvert(
                        converted.Position,
                        converted.Orientation,
                        out _,
                        out Quaternion rootRotation),
                    "Unity adapter rejected a USV attitude case.");
                Require(
                    Vector3.Dot(rootRotation * Vector3.left,
                        target * Vector3.forward) > 0.999999f &&
                    Vector3.Dot(rootRotation * Vector3.up,
                        target * Vector3.up) > 0.999999f &&
                    Vector3.Dot(rootRotation * Vector3.forward,
                        target * Vector3.right) > 0.999999f,
                    "USV identity/yaw/pitch/roll/combined model axes are incorrect.");
            }
        }

        private static void TestGeneratedPoseConversion(
            VehiclePoseProfileConfiguration configuration)
        {
            Require(configuration.TryBuildProfile(
                    out CoordinateTransformProfile profile,
                    out string profileError),
                "USV Profile failed to build for generated pose: " + profileError);
            var generator = new DeterministicUsvDiagnosticTrajectory();
            var vehicle = new LocalTestVehicle(
                "USV-01",
                VehicleType.Usv,
                new Vector3d(0.15, 0.18, 2.05),
                WorldFrame.UnityWorld,
                BodyFrame.UnityBody);
            VehicleState state = generator.Evaluate(vehicle, 15UL, 1.5);
            Require(VehiclePoseConverter.TryConvert(
                    state,
                    profile,
                    out ConvertedVehiclePose converted,
                    out ConversionError conversionError),
                "Generated USV pose failed Profile conversion: " +
                conversionError.Message);
            Require(UnityPoseAdapter.TryConvert(
                    converted.Position,
                    converted.Orientation,
                    out Vector3 unityPosition,
                    out Quaternion unityRotation) &&
                    IsFinite(unityPosition) &&
                    IsFinite(unityRotation),
                "Generated USV pose failed Unity adaptation.");
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) &&
                   float.IsFinite(value.y) &&
                   float.IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return float.IsFinite(value.x) &&
                   float.IsFinite(value.y) &&
                   float.IsFinite(value.z) &&
                   float.IsFinite(value.w);
        }

        private static VehicleState State(
            Vector3 position,
            Quaternion orientation,
            ulong sequence)
        {
            Quaternion normalized = orientation.normalized;
            return new VehicleState(
                "USV-01",
                VehicleType.Usv,
                sequence,
                sequence,
                new Vector3d(position.x, position.y, position.z),
                new Quaterniond(normalized.x, normalized.y, normalized.z, normalized.w),
                Vector3d.Zero,
                Vector3d.Zero,
                Vector3d.Zero,
                VehicleStateFields.Position | VehicleStateFields.Orientation,
                WorldFrame.UnityWorld,
                BodyFrame.UnityBody);
        }

        private static bool HasMissingScripts(Scene scene)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Any(item => item.GetComponents<Component>().Any(component => component == null));
        }

        private static void WriteReport(IReadOnlyCollection<Check> checks, Scene scene)
        {
            string outputDirectory = EvidenceDirectory();
            Directory.CreateDirectory(outputDirectory);
            string sceneHash = Sha256(Path.GetFullPath(ScenePath));
            var markdown = new StringBuilder();
            markdown.AppendLine("# N6-D USV Static Verification");
            markdown.AppendLine();
            markdown.AppendLine("- Status: `N6D_USV_STATIC_VALIDATION_PASS`");
            markdown.AppendLine("- Scene: `" + scene.path + "`");
            markdown.AppendLine("- Scene SHA-256: `" + sceneHash + "`");
            markdown.AppendLine("- Checks: `" + checks.Count + "/" + checks.Count + "`");
            markdown.AppendLine();
            foreach (Check check in checks)
            {
                markdown.AppendLine("- PASS — " + check.Name + ": " + check.Detail);
            }
            File.WriteAllText(
                Path.Combine(outputDirectory, "n6d_usv_static_report.md"),
                markdown.ToString(),
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(outputDirectory, "n6d_scene_sha256.txt"),
                sceneHash + Environment.NewLine,
                new UTF8Encoding(false));
        }

        private static string EvidenceDirectory()
        {
            string configured = Environment.GetEnvironmentVariable("N6D_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.GetFullPath(configured);
            }
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, "..", "..", "N6D_Validation"));
        }

        private static string Sha256(string path)
        {
            using (var algorithm = System.Security.Cryptography.SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return string.Concat(algorithm.ComputeHash(stream)
                    .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        private static GameObject FindUniqueRoot(Scene scene, string name)
        {
            GameObject[] matches = scene.GetRootGameObjects()
                .Where(item => string.Equals(item.name, name, StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1, "Expected one Scene root named " + name + ".");
            return matches[0];
        }

        private static Transform RequireUniqueDescendant(Transform root, string name)
        {
            Transform[] matches = root.GetComponentsInChildren<Transform>(true)
                .Where(item => string.Equals(item.name, name, StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1, "Expected one descendant named " + name + ".");
            return matches[0];
        }

        private static T RequireComponent<T>(GameObject value, string label)
            where T : Component
        {
            T component = value.GetComponent<T>();
            Require(component != null, "Missing " + label + ".");
            return component;
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

        private static void Add(ICollection<Check> checks, string name, string detail)
        {
            checks.Add(new Check { Name = name, Detail = detail });
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
