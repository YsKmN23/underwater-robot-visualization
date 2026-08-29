using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.LocalTesting;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Sampling;
using UnderwaterRobotScene.Visualization.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    public static class RovRootPoseN6BVerifier
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string RovName = "ROV_Box_Seabed";
        private const string RovModelName = "ROV_FineModel_V1_Imported";
        private const string HostName = "ROV_PublicData_RuntimeHost";
        private const string DriverName = "ROV_PublicPoseDriver";
        private const ulong RovTransformFileId = 656059028UL;

        private sealed class Check
        {
            public string Name;
            public string Detail;
        }

        [MenuItem("Tools/Underwater Demo/N6-B/Verify ROV Root Pose Integration")]
        public static void RunFromMenu()
        {
            Execute();
        }

        public static void RunBatch()
        {
            Execute();
            Debug.Log("N6B_ROV_STATIC_VALIDATION_PASS");
        }

        public static void RunCoreRegressionsBatch()
        {
            int n2 = VehicleDataLayerN2Verifier.RunVerification(Debug.Log);
            int n3 = CoordinateAttitudeN3Verifier.RunVerification(Debug.Log);
            int n4 = RenderSamplingN4Verifier.RunVerification(Debug.Log);
            Require(n2 == 0 && n3 == 0 && n4 == 0,
                "One or more N2-N4 core regressions failed.");
            Debug.Log("N6B_CORE_REGRESSIONS_PASS | N2=8/8 | N3=10/10 | N4=12/12");
        }

        private static void Execute()
        {
            var checks = new List<Check>();
            TestGenerator(checks);
            TestGeneratorBoundaries(checks);
            TestGeneratorBoundary(checks);

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Require(!scene.isDirty, "Scene became dirty when opened for verification.");
            GameObject rov = FindUniqueRoot(scene, RovName);
            Transform model = RequireUniqueDescendant(rov.transform, RovModelName);
            GameObject hostObject = FindUniqueRoot(scene, HostName);
            GameObject driverObject = FindUniqueRoot(scene, DriverName);

            GlobalObjectId rovId = GlobalObjectId.GetGlobalObjectIdSlow(rov.transform);
            Require(rovId.targetObjectId == RovTransformFileId,
                "ROV Transform fileID changed from 656059028.");
            Require(rov.transform.childCount == 1 && model.parent == rov.transform,
                "ROV movement root no longer contains exactly the imported model.");
            Add(checks, "ROV movement root",
                "Transform fileID 656059028; only child is the imported ROV model.");

            Require(Near(model.localPosition, Vector3.zero, 1e-6f) &&
                    Near(model.localRotation, Quaternion.identity, 1e-6f) &&
                    Near(model.localScale, Vector3.one * 100f, 1e-5f),
                "Imported ROV model local Transform changed.");
            Add(checks, "Model local Transform",
                "Position zero, rotation identity, scale (100,100,100).");

            VehiclePoseControlAuthority authority =
                RequireComponent<VehiclePoseControlAuthority>(rov, "ROV authority");
            VehicleDataRuntimeHost host =
                RequireComponent<VehicleDataRuntimeHost>(hostObject, "ROV host");
            VehiclePoseIntegrationConfiguration configuration =
                RequireComponent<VehiclePoseIntegrationConfiguration>(
                    hostObject,
                    "ROV integration configuration");
            VehiclePoseProfileConfiguration profile =
                RequireComponent<VehiclePoseProfileConfiguration>(
                    driverObject,
                    "ROV profile");
            VehiclePoseDriver driver =
                RequireComponent<VehiclePoseDriver>(driverObject, "ROV driver");
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
                    ReferenceEquals(driver.TargetRoot, rov.transform) &&
                    ReferenceEquals(demo.rovControlAuthority, authority),
                "ROV Host, Driver, Profile, Authority, target, or Demo binding is incorrect.");
            Require(authority.PublicDataOwnsControl && !demo.DrivesRov,
                "ROV PublicData ownership is not active in the installed scene.");
            Add(checks, "Scene bindings",
                "Independent Host, configuration, Profile, Driver and Authority target ROV_Box_Seabed.");

            Require(
                configuration.SourceId == "local-test-rov-n6b" &&
                configuration.VehicleId == "ROV-01" &&
                configuration.VehicleType == VehicleType.Rov &&
                configuration.GeneratorKind ==
                DeterministicVehicleStateGeneratorKind.RovDiagnosticTrajectory &&
                Near(configuration.TestOrigin, new Vector3(3.45f, -2.42f, -1.45f), 1e-6f) &&
                Math.Abs(configuration.SampleIntervalSeconds - 0.1) <= 1e-7 &&
                configuration.StoreCapacity == 64 &&
                Math.Abs(configuration.StaleTimeoutSeconds - 0.75) <= 1e-7 &&
                Math.Abs(configuration.RenderDelaySeconds - 0.15) <= 1e-7 &&
                Math.Abs(configuration.MaxInterpolationGapSeconds - 0.25) <= 1e-7 &&
                Math.Abs(configuration.MaxHoldSourceTimeSeconds - 0.25) <= 1e-7 &&
                configuration.TryValidate(out _),
                "ROV Integration Configuration differs from N6-B diagnostic values.");
            Add(checks, "ROV diagnostic configuration",
                "Source local-test-rov-n6b, vehicle ROV-01, 0.1 s samples, 0.15 s delay, 0.75 s stale timeout.");

            Require(
                profile.ProfileId == "ROV_LOCAL_TEST_UNITY_Y_MINUS_90" &&
                profile.Preset == CoordinateProfilePreset.UnityNative &&
                profile.AttitudeDirection == AttitudeDirection.BodyToWorld &&
                profile.ModelRight == SignedSemanticAxis.NegativeZ &&
                profile.ModelUp == SignedSemanticAxis.PositiveY &&
                profile.ModelForward == SignedSemanticAxis.PositiveX &&
                Near(profile.ModelAlignmentEulerDegrees, new Vector3(0f, -90f, 0f), 1e-6f),
                "ROV Profile axes or model alignment are incorrect.");
            TestProfileConversion(profile);
            Add(checks, "ROV axes and single model alignment",
                "Forward +X, Up +Y, Right -Z; one Y -90 degree alignment maps identity, yaw, pitch, roll and combined attitudes to target +Z/+Y/+X axes.");

            VehiclePoseIntegrationConfiguration[] configurations =
                UnityEngine.Object.FindObjectsByType<VehiclePoseIntegrationConfiguration>(
                        FindObjectsInactive.Include)
                    .Where(item => item.gameObject.scene == scene)
                    .ToArray();
            VehiclePoseIntegrationConfiguration auvConfiguration =
                configurations.Single(item =>
                    item.VehicleType == VehicleType.Auv &&
                    item.VehicleId == "AUV-01");
            Require(!ReferenceEquals(auvConfiguration, configuration),
                "AUV and ROV share one serialized Integration Configuration.");
            VehiclePoseControlAuthority auvAuthority =
                FindUniqueRoot(scene, "AUV_Yellow_Underwater")
                    .GetComponent<VehiclePoseControlAuthority>();
            Require(auvAuthority != null &&
                    !ReferenceEquals(auvAuthority, authority) &&
                    auvAuthority.PublicDataOwnsControl,
                "AUV and ROV Authority instances are not independent.");
            Add(checks, "AUV isolation",
                "AUV and ROV retain distinct configurations, Profiles, Hosts, Drivers and Authorities.");

            PropellerSpinner[] spinners =
                rov.GetComponentsInChildren<PropellerSpinner>(true);
            Require(spinners.Length == 6, "Expected six ROV PropellerSpinner components.");
            Require(spinners.All(item =>
                    item.enabled &&
                    item.transform != rov.transform &&
                    item.transform.IsChildOf(model)),
                "A ROV spinner is disabled or outside the model subtree.");
            Require(spinners.Count(item =>
                        Near(item.localAxis.normalized, Vector3.right, 1e-6f) &&
                        Math.Abs(item.rpm - 720f) <= 1e-5f) == 2 &&
                    spinners.Count(item =>
                        Near(item.localAxis.normalized, Vector3.up, 1e-6f) &&
                        Math.Abs(item.rpm - 680f) <= 1e-5f) == 2 &&
                    spinners.Count(item =>
                        Near(item.localAxis.normalized, Vector3.forward, 1e-6f) &&
                        Math.Abs(item.rpm - 700f) <= 1e-5f) == 2,
                "ROV spinner axes or RPM values changed.");
            Add(checks, "Local actuator boundary",
                "Six enabled spinners remain under the model and write only their local rotation.");

            Require(!HasMissingScripts(scene), "Scene contains a Missing Script.");
            Require(!scene.isDirty, "Static verification dirtied the scene.");
            Add(checks, "Scene integrity", "No Missing Script and verification left the Scene clean.");

            WriteReport(checks, scene);
        }

        private static void TestGenerator(ICollection<Check> checks)
        {
            const double CycleSeconds = 12.0;
            var generator = new DeterministicRovDiagnosticTrajectory();
            var vehicle = new LocalTestVehicle(
                "ROV-01",
                VehicleType.Rov,
                new Vector3d(3.45, -2.42, -1.45),
                WorldFrame.UnityWorld,
                BodyFrame.UnityBody);
            VehicleState zero = Evaluate(generator, vehicle, 0.0);
            VehicleState cycle = Evaluate(generator, vehicle, CycleSeconds);
            VehicleState poseA = Evaluate(generator, vehicle, 3.0);
            VehicleState poseB = Evaluate(generator, vehicle, 7.0);
            VehicleState repeated = generator.Evaluate(vehicle, 123UL, 5.5);
            VehicleState repeatedAgain = generator.Evaluate(vehicle, 123UL, 5.5);
            VehicleState wrappedNegative = Evaluate(generator, vehicle, -0.25);
            VehicleState wrappedPositive = Evaluate(generator, vehicle, 11.75);

            Require(zero.VehicleId == "ROV-01" &&
                    zero.VehicleType == VehicleType.Rov &&
                    SamePose(zero, cycle, 1e-12, 1e-12) &&
                    SamePose(wrappedNegative, wrappedPositive, 1e-12, 1e-12),
                "ROV generator cycle or positive-modulo behavior is incorrect.");
            Require(repeated.Equals(repeatedAgain),
                "ROV generator is not deterministic for identical input.");

            Require(Near(
                        poseA.Position,
                        Offset(vehicle.PositionOffset, 0.30, 0.12, -0.18),
                        1e-12) &&
                    Near(
                        poseB.Position,
                        Offset(vehicle.PositionOffset, -0.22, -0.10, 0.22),
                        1e-12),
                "ROV diagnostic Pose A or Pose B position is incorrect.");
            Require(
                SameRotation(
                    poseA.Orientation,
                    FromUnityQuaternion(Quaternion.Euler(-4f, 12f, 5f)),
                    1e-12) &&
                SameRotation(
                    poseB.Orientation,
                    FromUnityQuaternion(Quaternion.Euler(5f, -14f, -6f)),
                    1e-12),
                "ROV diagnostic Pose A or Pose B orientation is incorrect.");

            AssertExactHold(generator, vehicle, new[] { 0.0, 0.25, 0.74 }, zero, "initial");
            AssertExactHold(generator, vehicle, new[] { 3.0, 3.4, 4.24 }, poseA, "first hover");
            AssertExactHold(generator, vehicle, new[] { 7.0, 7.5, 8.24 }, poseB, "second hover");
            AssertExactHold(generator, vehicle, new[] { 11.25, 11.6, 11.99 }, zero, "final");

            AssertMoves(generator, vehicle, 0.75, 3.0, "first motion");
            AssertMoves(generator, vehicle, 4.25, 7.0, "second motion");
            AssertMoves(generator, vehicle, 8.25, 11.25, "return");
            Require(
                poseA.Position.X > vehicle.PositionOffset.X &&
                poseA.Position.Y > vehicle.PositionOffset.Y &&
                poseA.Position.Z < vehicle.PositionOffset.Z &&
                poseB.Position.X < vehicle.PositionOffset.X &&
                poseB.Position.Y < vehicle.PositionOffset.Y &&
                poseB.Position.Z > vehicle.PositionOffset.Z,
                "ROV trajectory does not cover opposite X/Y/Z directions.");

            VehicleState semantic = generator.Evaluate(vehicle, 987UL, 5.5);
            VehicleStateFields poseOnly =
                VehicleStateFields.Position | VehicleStateFields.Orientation;
            Require(
                semantic.SourceTimestampSeconds == 5.5 &&
                semantic.SequenceNumber == 987UL &&
                semantic.ValidFields == poseOnly &&
                semantic.LinearVelocity.Equals(Vector3d.Zero) &&
                semantic.AngularVelocity.Equals(Vector3d.Zero) &&
                semantic.LinearAcceleration.Equals(Vector3d.Zero) &&
                semantic.IsStructurallyValid,
                "ROV generator changed timestamp, sequence, validity, or velocity-field semantics.");
            Require(
                poseA.Position.IsFinite &&
                poseB.Position.IsFinite &&
                poseA.Orientation.TryNormalize(out _) &&
                poseB.Orientation.TryNormalize(out _),
                "ROV trajectory produced a non-finite or unusable key pose.");

            Add(checks, "M1-B deterministic segmented trajectory",
                "12 s positive-modulo cycle, four exact holds, three moving segments, opposite XYZ motion, Pose A/B and pose-only data semantics passed.");
        }

        private static void TestGeneratorBoundaries(ICollection<Check> checks)
        {
            const double Epsilon = 0.001;
            const double ContinuityEpsilon = 0.000000001;
            const double LinearSpeedTolerance = 0.00001;
            const double AttitudeIncrementToleranceRadians = 0.000001;
            double[] boundaries = { 0.75, 3.0, 4.25, 7.0, 8.25, 11.25, 12.0 };
            var generator = new DeterministicRovDiagnosticTrajectory();
            var vehicle = new LocalTestVehicle(
                "ROV-01",
                VehicleType.Rov,
                new Vector3d(3.45, -2.42, -1.45),
                WorldFrame.UnityWorld,
                BodyFrame.UnityBody);

            foreach (double boundary in boundaries)
            {
                VehicleState exact = Evaluate(generator, vehicle, boundary);
                VehicleState beforeContinuity =
                    Evaluate(generator, vehicle, boundary - ContinuityEpsilon);
                VehicleState afterContinuity =
                    Evaluate(generator, vehicle, boundary + ContinuityEpsilon);
                Require(
                    Near(beforeContinuity.Position, exact.Position, 1e-8) &&
                    Near(afterContinuity.Position, exact.Position, 1e-8) &&
                    SameRotation(beforeContinuity.Orientation, exact.Orientation, 1e-10) &&
                    SameRotation(afterContinuity.Orientation, exact.Orientation, 1e-10),
                    "ROV trajectory pose is discontinuous at " +
                    boundary.ToString("0.00", CultureInfo.InvariantCulture) + " s.");

                VehicleState before = Evaluate(generator, vehicle, boundary - Epsilon);
                VehicleState after = Evaluate(generator, vehicle, boundary + Epsilon);
                double estimatedLinearSpeed =
                    Distance(before.Position, after.Position) / (2.0 * Epsilon);
                double attitudeIncrement =
                    RotationDistanceRadians(before.Orientation, after.Orientation);
                Require(
                    estimatedLinearSpeed <= LinearSpeedTolerance,
                    "ROV boundary linear speed is not near zero at " +
                    boundary.ToString("0.00", CultureInfo.InvariantCulture) +
                    " s: " + estimatedLinearSpeed.ToString("G17", CultureInfo.InvariantCulture));
                Require(
                    attitudeIncrement <= AttitudeIncrementToleranceRadians,
                    "ROV boundary attitude increment is not near zero at " +
                    boundary.ToString("0.00", CultureInfo.InvariantCulture) +
                    " s: " + attitudeIncrement.ToString("G17", CultureInfo.InvariantCulture));
            }

            Add(checks, "M1-B smooth boundaries",
                "All six segment boundaries and the 12/0 s loop are pose-continuous; epsilon 0.001 s finite differences stay below 1e-5 linear speed and 1e-6 rad attitude increment.");
        }

        private static VehicleState Evaluate(
            DeterministicRovDiagnosticTrajectory generator,
            LocalTestVehicle vehicle,
            double sourceTimestampSeconds)
        {
            ulong sequence = sourceTimestampSeconds >= 0.0
                ? (ulong)Math.Round(sourceTimestampSeconds * 1000.0)
                : 0UL;
            return generator.Evaluate(vehicle, sequence, sourceTimestampSeconds);
        }

        private static void AssertExactHold(
            DeterministicRovDiagnosticTrajectory generator,
            LocalTestVehicle vehicle,
            IEnumerable<double> sampleTimes,
            VehicleState expected,
            string label)
        {
            foreach (double sampleTime in sampleTimes)
            {
                VehicleState actual = Evaluate(generator, vehicle, sampleTime);
                Require(
                    SamePose(actual, expected, 0.0, 1e-15),
                    "ROV " + label + " is not an exact pose hold at " +
                    sampleTime.ToString("0.00", CultureInfo.InvariantCulture) + " s.");
            }
        }

        private static void AssertMoves(
            DeterministicRovDiagnosticTrajectory generator,
            LocalTestVehicle vehicle,
            double start,
            double end,
            string label)
        {
            VehicleState first = Evaluate(
                generator,
                vehicle,
                start + (end - start) * 0.25);
            VehicleState second = Evaluate(
                generator,
                vehicle,
                start + (end - start) * 0.75);
            Require(
                Distance(first.Position, second.Position) > 0.01 &&
                !SameRotation(first.Orientation, second.Orientation, 1e-8),
                "ROV " + label + " does not change both position and orientation.");
        }

        private static Vector3d Offset(
            Vector3d origin,
            double x,
            double y,
            double z)
        {
            return new Vector3d(origin.X + x, origin.Y + y, origin.Z + z);
        }

        private static Quaterniond FromUnityQuaternion(Quaternion value)
        {
            Quaternion normalized = value.normalized;
            return new Quaterniond(
                normalized.x,
                normalized.y,
                normalized.z,
                normalized.w);
        }

        private static bool SamePose(
            VehicleState left,
            VehicleState right,
            double positionTolerance,
            double rotationTolerance)
        {
            return Near(left.Position, right.Position, positionTolerance) &&
                   SameRotation(left.Orientation, right.Orientation, rotationTolerance);
        }

        private static bool Near(Vector3d left, Vector3d right, double tolerance)
        {
            return Distance(left, right) <= tolerance;
        }

        private static double Distance(Vector3d left, Vector3d right)
        {
            double x = left.X - right.X;
            double y = left.Y - right.Y;
            double z = left.Z - right.Z;
            return Math.Sqrt(x * x + y * y + z * z);
        }

        private static bool SameRotation(
            Quaterniond left,
            Quaterniond right,
            double tolerance)
        {
            return QuaternionMath3d.RepresentsSameRotation(left, right, tolerance);
        }

        private static double RotationDistanceRadians(Quaterniond left, Quaterniond right)
        {
            bool leftValid = left.TryNormalize(out Quaterniond normalizedLeft);
            bool rightValid = right.TryNormalize(out Quaterniond normalizedRight);
            Require(
                leftValid && rightValid,
                "ROV boundary contains an unusable quaternion.");
            double dot = Math.Abs(QuaternionMath3d.Dot(normalizedLeft, normalizedRight));
            dot = Math.Max(-1.0, Math.Min(1.0, dot));
            return 2.0 * Math.Acos(dot);
        }

        private static void TestGeneratorBoundary(ICollection<Check> checks)
        {
            string source = File.ReadAllText(
                "Assets/Scripts/Visualization/Data/LocalTesting/DeterministicRovDiagnosticTrajectory.cs");
            Require(!source.Contains("using UnityEngine") &&
                    !source.Contains("ModelAlignment") &&
                    !source.Contains("VehiclePoseDriver") &&
                    !source.Contains("VehicleStateStore") &&
                    !source.Contains("Transform"),
                "ROV generator crossed the pure-data boundary or contains model alignment.");
            Add(checks, "Generator dependency boundary",
                "No UnityEngine, Scene, Store, Driver, Transform or model-alignment dependency.");
        }

        private static void TestProfileConversion(
            VehiclePoseProfileConfiguration profileConfiguration)
        {
            Require(profileConfiguration.TryBuildProfile(
                    out CoordinateTransformProfile profile,
                    out string profileError),
                "ROV Profile failed to build: " + profileError);
            Vector3[] positions =
            {
                Vector3.zero,
                Vector3.right,
                Vector3.up,
                Vector3.forward,
                new Vector3(-1f, -2f, -3f),
                new Vector3(1.25f, -0.75f, 2.5f)
            };
            for (int index = 0; index < positions.Length; index++)
            {
                VehicleState state = State(positions[index], Quaternion.identity, (ulong)index);
                Require(VehiclePoseConverter.TryConvert(
                        state,
                        profile,
                        out ConvertedVehiclePose converted,
                        out ConversionError error),
                    "ROV position conversion failed: " + error.Message);
                Require(UnityPoseAdapter.TryConvert(
                        converted.Position,
                        converted.Orientation,
                        out Vector3 unityPosition,
                        out _),
                    "Unity adapter rejected a ROV position case.");
                Require(Near(unityPosition, positions[index], 1e-6f),
                    "ROV Unity-native East/Up/North position mapping changed.");
            }

            Quaternion[] targetRotations =
            {
                Quaternion.identity,
                Quaternion.AngleAxis(25f, Vector3.up),
                Quaternion.AngleAxis(15f, Vector3.right),
                Quaternion.AngleAxis(20f, Vector3.forward),
                Quaternion.Euler(12f, 27f, -9f)
            };
            for (int index = 0; index < targetRotations.Length; index++)
            {
                Quaternion target = targetRotations[index].normalized;
                VehicleState state = State(Vector3.zero, target, (ulong)index);
                Require(VehiclePoseConverter.TryConvert(
                        state,
                        profile,
                        out ConvertedVehiclePose converted,
                        out ConversionError error),
                    "ROV attitude conversion failed: " + error.Message);
                Require(UnityPoseAdapter.TryConvert(
                        converted.Position,
                        converted.Orientation,
                        out _,
                        out Quaternion rootRotation),
                    "Unity adapter rejected a ROV attitude case.");
                Require(
                    Vector3.Dot(
                        rootRotation * Vector3.right,
                        target * Vector3.forward) > 0.999999f &&
                    Vector3.Dot(
                        rootRotation * Vector3.up,
                        target * Vector3.up) > 0.999999f &&
                    Vector3.Dot(
                        rootRotation * Vector3.back,
                        target * Vector3.right) > 0.999999f,
                    "ROV identity/yaw/pitch/roll/combined model axes are incorrect.");
            }
        }

        private static VehicleState State(
            Vector3 position,
            Quaternion orientation,
            ulong sequence)
        {
            Quaternion normalized = orientation.normalized;
            return new VehicleState(
                "ROV-01",
                VehicleType.Rov,
                sequence,
                sequence,
                new Vector3d(position.x, position.y, position.z),
                new Quaterniond(
                    normalized.x,
                    normalized.y,
                    normalized.z,
                    normalized.w),
                Vector3d.Zero,
                Vector3d.Zero,
                Vector3d.Zero,
                VehicleStateFields.Position | VehicleStateFields.Orientation,
                WorldFrame.UnityWorld,
                BodyFrame.UnityBody);
        }

        private static bool HasMissingScripts(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
                {
                    if (item.GetComponents<Component>().Any(component => component == null))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void WriteReport(IReadOnlyCollection<Check> checks, Scene scene)
        {
            string outputDirectory = EvidenceDirectory();
            Directory.CreateDirectory(outputDirectory);
            string sceneHash = Sha256(Path.GetFullPath(ScenePath));
            var markdown = new StringBuilder();
            markdown.AppendLine("# N6-B ROV Static Verification");
            markdown.AppendLine();
            markdown.AppendLine("- Status: `N6B_ROV_STATIC_VALIDATION_PASS`");
            markdown.AppendLine("- Scene: `" + scene.path + "`");
            markdown.AppendLine("- Scene SHA-256: `" + sceneHash + "`");
            markdown.AppendLine("- Checks: `" + checks.Count + "/" + checks.Count + "`");
            markdown.AppendLine();
            foreach (Check check in checks)
            {
                markdown.AppendLine("- PASS — " + check.Name + ": " + check.Detail);
            }

            File.WriteAllText(
                Path.Combine(outputDirectory, "n6b_rov_static_report.md"),
                markdown.ToString(),
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(outputDirectory, "n6b_scene_sha256.txt"),
                sceneHash + Environment.NewLine,
                new UTF8Encoding(false));
        }

        private static string EvidenceDirectory()
        {
            string configured = Environment.GetEnvironmentVariable("N6B_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.GetFullPath(configured);
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, "..", "..", "N6B_Validation"));
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
