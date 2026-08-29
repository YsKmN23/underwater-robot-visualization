using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
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
    public static class AuvPublicPoseN5Verifier
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string AuvName = "AUV_Yellow_Underwater";
        private const string ModelName = "AUV_FineModel_V1_Imported";
        private const string TailName = "Tail_Propeller_RotatingPart";
        private const string HostName = "AUV_PublicData_RuntimeHost";
        private const string DriverName = "AUV_PublicPoseDriver";

        [Serializable]
        private sealed class CheckRecord
        {
            public string name;
            public bool passed;
            public string detail;
        }

        [Serializable]
        private sealed class VerificationReport
        {
            public string status;
            public string generatedAtIso8601;
            public string scenePath;
            public string sceneHashBefore;
            public string sceneHashAfter;
            public bool sceneHashUnchanged;
            public bool finalSceneDirty;
            public string sourceId;
            public string vehicleId;
            public string profileId;
            public string modelSemanticAxes;
            public string modelAlignment;
            public string multiplicationOrder;
            public string monotonicClockDomain;
            public string[] observedSampleModes;
            public string[] observedFailures;
            public ulong epochBeforeRestart;
            public ulong epochAfterRestart;
            public bool propellerSpinnerEnabled;
            public bool modelLocalTransformPreserved;
            public bool tailLocalTransformPreserved;
            public bool rootScalePreserved;
            public bool rovUsvReferencesPreserved;
            public int capturedErrorCount;
            public CheckRecord[] checks;
        }

        [MenuItem("Tools/AUV Pose MVP/N5/Run Public Pose Verification")]
        public static void RunFromMenu()
        {
            Execute();
        }

        public static void RunBatch()
        {
            Execute();
            Debug.Log("N5_VERIFICATION_COMPLETE");
        }

        private static void Execute()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string sceneFullPath = Path.Combine(projectRoot, ScenePath);
            string outputDirectory = Path.GetFullPath(
                Path.Combine(projectRoot, "..", "..", "N5_Validation"));
            Directory.CreateDirectory(outputDirectory);

            var checks = new List<CheckRecord>();
            var modes = new HashSet<string>(StringComparer.Ordinal);
            var failures = new HashSet<string>(StringComparer.Ordinal);
            int capturedErrors = 0;
            Application.LogCallback logCallback = (condition, stackTrace, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    capturedErrors++;
                }
            };
            Application.logMessageReceived += logCallback;

            string hashBefore = Sha256(sceneFullPath);
            ulong epochBeforeRestart = 0UL;
            ulong epochAfterRestart = 0UL;
            bool spinnerEnabled = false;
            bool modelLocalPreserved = false;
            bool tailLocalPreserved = false;
            bool rootScalePreserved = false;
            bool rovUsvPreserved = false;
            VehiclePoseIntegrationConfiguration integrationConfiguration = null;
            VehiclePoseProfileConfiguration profile = null;

            try
            {
                TestUnityPoseAdapter(checks);
                TestQuaternionContinuity(checks);
                TestSamplerFailureSurface(checks, failures);

                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                GameObject auv = FindUniqueRoot(scene, AuvName);
                GameObject hostObject = FindUniqueRoot(scene, HostName);
                GameObject driverObject = FindUniqueRoot(scene, DriverName);
                Transform model = FindUniqueDescendant(auv.transform, ModelName);
                Transform tail = FindUniqueDescendant(auv.transform, TailName);
                PropellerSpinner spinner = tail.GetComponent<PropellerSpinner>();
                VehiclePoseControlAuthority authority =
                    RequireComponent<VehiclePoseControlAuthority>(auv, "AUV control authority");
                VehicleDataRuntimeHost host =
                    RequireComponent<VehicleDataRuntimeHost>(hostObject, "runtime host");
                integrationConfiguration =
                    RequireComponent<VehiclePoseIntegrationConfiguration>(
                        hostObject,
                        "integration configuration");
                profile = RequireComponent<VehiclePoseProfileConfiguration>(driverObject, "profile");
                VehiclePoseDriver driver =
                    RequireComponent<VehiclePoseDriver>(driverObject, "driver");
                DemoMotionController[] demos =
                    UnityEngine.Object.FindObjectsByType<DemoMotionController>(
                        FindObjectsInactive.Include);
                Require(demos.Length == 1, "Expected one DemoMotionController.");
                DemoMotionController demo = demos[0];

                AddCheck(checks, "Scene bindings",
                    driver.TargetRoot == auv.transform &&
                    ReferenceEquals(host.IntegrationConfiguration, integrationConfiguration) &&
                    ReferenceEquals(driver.IntegrationConfiguration, integrationConfiguration) &&
                    ReferenceEquals(host.ProfileConfiguration, profile) &&
                    ReferenceEquals(driver.ProfileConfiguration, profile) &&
                    ReferenceEquals(driver.RuntimeHost, host) &&
                    ReferenceEquals(driver.ControlAuthority, authority) &&
                    ReferenceEquals(demo.auvControlAuthority, authority) &&
                    authority.PublicDataOwnsControl,
                    "Host and Driver share one integration configuration and profile; AUV root and authority are explicit.");
                VehiclePoseIntegrationConfiguration[] sceneConfigurations =
                    UnityEngine.Object.FindObjectsByType<VehiclePoseIntegrationConfiguration>(
                            FindObjectsInactive.Include)
                        .Where(item => item.gameObject.scene == scene)
                        .ToArray();
                VehiclePoseIntegrationConfiguration[] auvConfigurations =
                    sceneConfigurations
                        .Where(item => item.VehicleType == VehicleType.Auv &&
                                       string.Equals(
                                           item.VehicleId,
                                           integrationConfiguration.VehicleId,
                                           StringComparison.Ordinal))
                        .ToArray();
                AddCheck(checks, "Unique AUV integration configuration",
                    auvConfigurations.Length == 1 &&
                    ReferenceEquals(auvConfigurations[0], integrationConfiguration) &&
                    integrationConfiguration.TryValidate(out _),
                    "The authoritative scene contains one uniquely bound, valid AUV N5 Integration Configuration and may contain independent vehicle configurations.");
                TestRuntimeArchitectureBoundary(checks);
                TestEditorMenuItems(checks);
                AddCheck(checks, "ROV/USV references",
                    demo.rov != null && demo.usv != null,
                    "Demo controller retains explicit ROV and USV references.");
                rovUsvPreserved = demo.rov != null && demo.usv != null;

                Require(profile.TryBuildProfile(
                    out CoordinateTransformProfile transformProfile,
                    out string profileError), profileError);
                AddCheck(checks, "AUV profile semantics",
                    profile.ModelRight == SignedSemanticAxis.NegativeZ &&
                    profile.ModelUp == SignedSemanticAxis.PositiveY &&
                    profile.ModelForward == SignedSemanticAxis.PositiveX &&
                    Near(profile.ModelAlignmentEulerDegrees, new Vector3(0f, -90f, 0f), 1e-5f),
                    "Right=-Z, Up=+Y, Forward=+X; model alignment is Unity Y -90 degrees.");
                TestZeroPoseAxes(transformProfile, checks);
                TestN5PositionAxes(transformProfile, checks);
                TestN5AttitudeAxes(transformProfile, checks);
                TestDeterministicAuvTrajectory(
                    integrationConfiguration,
                    transformProfile,
                    checks);

                Vector3 rootPosition = auv.transform.position;
                Quaternion rootRotation = auv.transform.rotation;
                Vector3 rootScale = auv.transform.localScale;
                PoseRecord modelLocal = PoseRecord.Capture(model);
                PoseRecord tailLocal = PoseRecord.Capture(tail);
                Vector3 rovPosition = demo.rov.position;
                Quaternion rovRotation = demo.rov.rotation;
                Vector3 usvPosition = demo.usv.position;
                Quaternion usvRotation = demo.usv.rotation;
                spinnerEnabled = spinner != null && spinner.enabled;
                Require(spinnerEnabled, "Tail propeller spinner is missing or disabled.");

                try
                {
                    const double epochStart = 100.0;
                    double interval = integrationConfiguration.SampleIntervalSeconds;
                    double latestPublishLocal = epochStart + interval * 3.0;
                    double interpolatedLocal = latestPublishLocal;
                    double heldLocal = latestPublishLocal +
                                       integrationConfiguration.RenderDelaySeconds +
                                       integrationConfiguration.MaxHoldSourceTimeSeconds * 0.6;
                    double holdFailureLocal = latestPublishLocal +
                                              integrationConfiguration.RenderDelaySeconds +
                                              integrationConfiguration.MaxHoldSourceTimeSeconds +
                                              0.05;
                    double staleLocal = latestPublishLocal +
                                        integrationConfiguration.StaleTimeoutSeconds +
                                        0.05;
                    double restartLocal = staleLocal + interval;

                    host.InitializeForDiagnostics(epochStart);
                    host.TickForDiagnostics(epochStart);
                    Require(host.TryGetActiveEpoch(out epochBeforeRestart),
                        "Initial source epoch was not published.");
                    Require(driver.TrySampleAndApply(epochStart), "Initial exact sample did not apply.");
                    modes.Add(driver.LastSampleMode.ToString());
                    AddCheck(checks, "Exact runtime sample",
                        driver.LastSampleMode == RenderSampleMode.Exact,
                        "Epoch start applies source timestamp 0 exactly.");
                    AddCheck(checks, "Zero-pose visual axes",
                        Vector3.Dot(auv.transform.TransformDirection(Vector3.right), Vector3.forward) > 0.9999f &&
                        Vector3.Dot(auv.transform.TransformDirection(Vector3.up), Vector3.up) > 0.9999f &&
                        Vector3.Dot(auv.transform.TransformDirection(Vector3.back), Vector3.right) > 0.9999f,
                        "Model +X faces world +Z, +Y faces +Y, and model -Z faces world +X.");

                    host.TickForDiagnostics(epochStart + interval);
                    host.TickForDiagnostics(epochStart + interval * 2.0);
                    host.TickForDiagnostics(latestPublishLocal);
                    Require(driver.TrySampleAndApply(interpolatedLocal), "Interpolated sample did not apply.");
                    modes.Add(driver.LastSampleMode.ToString());
                    AddCheck(checks, "Interpolated runtime sample",
                        driver.LastSampleMode == RenderSampleMode.Interpolated,
                        "The configured render delay selects a valid source bracket.");
                    AddCheck(checks, "PublicData ownership",
                        driver.OwnsControl && !demo.DrivesAuv,
                        "Driver owns AUV and DemoMotionController does not write it.");

                    authority.Mode = VehiclePoseControlMode.Demo;
                    Vector3 beforeDemoDriverAttempt = auv.transform.position;
                    bool wroteWithoutOwnership = driver.TrySampleAndApply(interpolatedLocal + 0.01);
                    AddCheck(checks, "Demo ownership",
                        demo.DrivesAuv &&
                        !wroteWithoutOwnership &&
                        Near(auv.transform.position, beforeDemoDriverAttempt, 1e-6f),
                        "Demo mode blocks the public driver without resetting the AUV.");

                    authority.Mode = VehiclePoseControlMode.PublicData;
                    driver.enabled = false;
                    Vector3 beforeDisabledAttempt = auv.transform.position;
                    bool wroteWhileDisabled = driver.TrySampleAndApply(interpolatedLocal + 0.02);
                    AddCheck(checks, "Disabled driver",
                        !wroteWhileDisabled &&
                        Near(auv.transform.position, beforeDisabledAttempt, 1e-6f),
                        "A disabled driver cannot write the movement root.");
                    driver.enabled = true;

                    host.StopSource();
                    Require(driver.TrySampleAndApply(heldLocal), "Bounded latest hold did not apply.");
                    modes.Add(driver.LastSampleMode.ToString());
                    AddCheck(checks, "HeldLatest runtime sample",
                        driver.LastSampleMode == RenderSampleMode.HeldLatest,
                        "Stopped source holds the latest pose inside the configured source-time window.");

                    Vector3 beforeGapFailure = auv.transform.position;
                    Require(!driver.TrySampleAndApply(holdFailureLocal), "Hold-window failure was expected.");
                    failures.Add(driver.LastFailureReason.ToString());
                    AddCheck(checks, "Bounded hold failure",
                        driver.LastFailureReason == RenderSampleFailureReason.HoldWindowExceeded &&
                        Near(auv.transform.position, beforeGapFailure, 1e-6f),
                        "Hold window expiry preserves the last successful pose.");

                    Vector3 beforeStale = auv.transform.position;
                    Require(!driver.TrySampleAndApply(staleLocal), "Stale failure was expected.");
                    failures.Add(driver.LastFailureReason.ToString());
                    AddCheck(checks, "Stale failure",
                        driver.LastFailureReason == RenderSampleFailureReason.Stale &&
                        Near(auv.transform.position, beforeStale, 1e-6f),
                        "Stale data does not reset or overwrite the AUV.");

                    host.RestartSourceForDiagnostics(restartLocal);
                    host.TickForDiagnostics(restartLocal);
                    Require(host.TryGetActiveEpoch(out epochAfterRestart),
                        "Restarted source epoch was not published.");
                    Require(epochAfterRestart > epochBeforeRestart, "Source epoch did not advance.");
                    Require(driver.TrySampleAndApply(restartLocal), "Driver did not recover after source restart.");
                    modes.Add(driver.LastSampleMode.ToString());
                    AddCheck(checks, "Source restart and recovery",
                        epochAfterRestart > epochBeforeRestart &&
                        driver.LastSampleMode == RenderSampleMode.Exact,
                        "Restart publishes a new epoch and recovers with an exact timestamp-0 sample.");

                    modelLocalPreserved = modelLocal.EqualsCurrent(model, 1e-6f);
                    tailLocalPreserved = tailLocal.EqualsCurrent(tail, 1e-6f);
                    rootScalePreserved = Near(auv.transform.localScale, rootScale, 1e-6f);
                    AddCheck(checks, "Model child protection", modelLocalPreserved,
                        "AUV_FineModel_V1_Imported local Transform is unchanged.");
                    AddCheck(checks, "Tail animation protection",
                        tailLocalPreserved && spinner.enabled,
                        "Tail propeller local Transform and PropellerSpinner enabled state are unchanged.");
                    AddCheck(checks, "Root scale protection", rootScalePreserved,
                        "Driver writes only position and rotation.");
                    AddCheck(checks, "ROV/USV untouched",
                        Near(demo.rov.position, rovPosition, 1e-6f) &&
                        Near(demo.rov.rotation, rovRotation, 1e-5f) &&
                        Near(demo.usv.position, usvPosition, 1e-6f) &&
                        Near(demo.usv.rotation, usvRotation, 1e-5f),
                        "Public AUV verification does not write ROV or USV.");
                }
                finally
                {
                    host.ShutdownForDiagnostics();
                    authority.Mode = VehiclePoseControlMode.PublicData;
                    driver.enabled = true;
                    auv.transform.SetPositionAndRotation(rootPosition, rootRotation);
                    auv.transform.localScale = rootScale;
                }

                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Scene finalScene = SceneManager.GetActiveScene();
                Require(!finalScene.isDirty, "Reloaded authoritative scene is unexpectedly dirty.");
            }
            finally
            {
                Application.logMessageReceived -= logCallback;
            }

            string hashAfter = Sha256(sceneFullPath);
            AddCheck(checks, "Scene verification is non-destructive",
                string.Equals(hashBefore, hashAfter, StringComparison.OrdinalIgnoreCase),
                "Scene SHA-256 is unchanged across runtime diagnostics.");
            AddCheck(checks, "No captured Console errors",
                capturedErrors == 0,
                "Verifier captured " + capturedErrors + " Error/Exception/Assert messages.");

            var report = new VerificationReport
            {
                status = checks.All(check => check.passed)
                    ? "N5_AUV_PUBLIC_POSE_DRIVER_INTEGRATION_COMPLETE"
                    : "N5_VERIFICATION_FAILED",
                generatedAtIso8601 = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
                scenePath = ScenePath,
                sceneHashBefore = hashBefore,
                sceneHashAfter = hashAfter,
                sceneHashUnchanged = string.Equals(hashBefore, hashAfter, StringComparison.OrdinalIgnoreCase),
                finalSceneDirty = SceneManager.GetActiveScene().isDirty,
                sourceId = integrationConfiguration.SourceId,
                vehicleId = integrationConfiguration.VehicleId,
                profileId = profile.ProfileId,
                modelSemanticAxes = "Right=-Z, Up=+Y, Forward=+X",
                modelAlignment = "Unity Y -90 degrees; quaternion approximately (0,-0.7071068,0,0.7071068)",
                multiplicationOrder = "q_output = q_target * q_modelAlignment",
                monotonicClockDomain =
                    "Time.realtimeSinceStartupAsDouble for host now, receive time, and RenderSampleRequest local now.",
                observedSampleModes = modes.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                observedFailures = failures.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                epochBeforeRestart = epochBeforeRestart,
                epochAfterRestart = epochAfterRestart,
                propellerSpinnerEnabled = spinnerEnabled,
                modelLocalTransformPreserved = modelLocalPreserved,
                tailLocalTransformPreserved = tailLocalPreserved,
                rootScalePreserved = rootScalePreserved,
                rovUsvReferencesPreserved = rovUsvPreserved,
                capturedErrorCount = capturedErrors,
                checks = checks.ToArray()
            };

            string jsonPath = Path.Combine(outputDirectory, "n5_auv_public_pose_report.json");
            string markdownPath = Path.Combine(outputDirectory, "n5_auv_public_pose_report.md");
            File.WriteAllText(jsonPath, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            File.WriteAllText(markdownPath, BuildMarkdown(report), new UTF8Encoding(false));

            Require(report.status == "N5_AUV_PUBLIC_POSE_DRIVER_INTEGRATION_COMPLETE",
                "One or more N5 verification checks failed. See " + markdownPath);
        }

        private static void TestRuntimeArchitectureBoundary(List<CheckRecord> checks)
        {
            const BindingFlags InstanceFields =
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            const BindingFlags AllMethods =
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.NonPublic | BindingFlags.Public;

            Type host = typeof(VehicleDataRuntimeHost);
            Type driver = typeof(VehiclePoseDriver);
            Type profile = typeof(VehiclePoseProfileConfiguration);
            string[] retiredHostFields =
            {
                "sourceId",
                "vehicleId",
                "testOrigin",
                "sampleIntervalSeconds",
                "storeCapacity",
                "staleTimeoutSeconds",
                "maxCatchUpStepsPerFrame",
                "autoStart"
            };
            string[] retiredDriverFields =
            {
                "sourceId",
                "vehicleId",
                "renderDelaySeconds",
                "maxInterpolationGapSeconds",
                "maxHoldSourceTimeSeconds",
                "exactTimeToleranceSeconds",
                "afterLatestBehavior",
                "allowSingleSampleHold"
            };

            bool passed =
                host.GetMethod("EvaluateN5Trajectory", AllMethods) == null &&
                host.GetMethod("ConfigureForN5Auv", AllMethods) == null &&
                driver.GetMethod("ConfigureForN5Auv", AllMethods) == null &&
                profile.GetMethod("ConfigureForN5Auv", AllMethods) == null &&
                retiredHostFields.All(name => host.GetField(name, InstanceFields) == null) &&
                retiredDriverFields.All(name => driver.GetField(name, InstanceFields) == null);
            AddCheck(
                checks,
                "Runtime architecture boundary",
                passed,
                "Host has no AUV trajectory/config entry; Host and Driver have no independently serialized identity or timing policy fields.");
        }

        private static void TestEditorMenuItems(List<CheckRecord> checks)
        {
            Type[] publicEditorTools =
            {
                typeof(VehicleDataLayerN2Verifier),
                typeof(CoordinateAttitudeN3Verifier),
                typeof(RenderSamplingN4Verifier),
                typeof(AuvPublicPoseN5Verifier),
                typeof(AuvPublicPoseN5PlayModeVerifier),
                typeof(AuvPublicPoseN5SceneInstaller),
                typeof(AuvHandednessN5ADiagnostic)
            };
            bool passed = publicEditorTools.All(type =>
                type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Any(method => method.GetCustomAttributes(typeof(MenuItem), false).Length > 0));
            AddCheck(
                checks,
                "Public Editor MenuItems",
                passed,
                "All seven public Verification, Installation and Diagnostics tools retain a discoverable MenuItem.");
        }

        private static void TestDeterministicAuvTrajectory(
            VehiclePoseIntegrationConfiguration configuration,
            CoordinateTransformProfile profile,
            List<CheckRecord> checks)
        {
            IDeterministicVehicleStateGenerator generator =
                configuration.CreateStateGenerator();
            var vehicle = new LocalTestVehicle(
                configuration.VehicleId,
                configuration.VehicleType,
                new Vector3d(
                    configuration.TestOrigin.x,
                    configuration.TestOrigin.y,
                    configuration.TestOrigin.z),
                profile.SourceWorldFrame,
                profile.SourceBodyFrame);

            const ulong SampleIndex = 7UL;
            const double SourceTimestamp = 0.7;
            VehicleState first = generator.Evaluate(vehicle, SampleIndex, SourceTimestamp);
            VehicleState second = generator.Evaluate(vehicle, SampleIndex, SourceTimestamp);
            var expectedPosition = new Vector3d(
                vehicle.PositionOffset.X + Math.Sin(SourceTimestamp * 0.45) * 0.8,
                vehicle.PositionOffset.Y + Math.Sin(SourceTimestamp * 0.31) * 0.25,
                vehicle.PositionOffset.Z + (Math.Cos(SourceTimestamp * 0.37) - 1.0) * 0.6);
            Quaternion expectedUnity = Quaternion.Euler(
                (float)(Math.Sin(SourceTimestamp * 0.41) * 8.0),
                (float)(Math.Sin(SourceTimestamp * 0.25) * 25.0),
                (float)(Math.Sin(SourceTimestamp * 0.33) * 10.0));

            bool passed =
                configuration.GeneratorKind ==
                DeterministicVehicleStateGeneratorKind.AuvIntegrationTrajectory &&
                generator is DeterministicAuvIntegrationTrajectory &&
                first.Equals(second) &&
                first.Position.Equals(expectedPosition) &&
                first.VehicleId == configuration.VehicleId &&
                first.WorldFrame == profile.SourceWorldFrame &&
                first.BodyFrame == profile.SourceBodyFrame &&
                QuaternionMath3d.RepresentsSameRotation(
                    first.Orientation,
                    ToQuaterniond(expectedUnity),
                    1e-10);
            AddCheck(
                checks,
                "Deterministic AUV integration trajectory",
                passed,
                "The selected pure C# generator preserves the N5 position/Euler trajectory without model alignment.");
        }

        private static void TestUnityPoseAdapter(List<CheckRecord> checks)
        {
            bool valid = UnityPoseAdapter.TryConvert(
                new Vector3d(1.25, -2.5, 3.75),
                new Quaterniond(0.0, 0.0, 0.0, 2.0),
                out Vector3 position,
                out Quaternion orientation);
            AddCheck(checks, "Unity Pose Adapter component order",
                valid &&
                Near(position, new Vector3(1.25f, -2.5f, 3.75f), 1e-6f) &&
                Near(orientation, Quaternion.identity, 1e-6f),
                "Position XYZ and quaternion XYZW pass through without axis exchange; quaternion is normalized.");

            bool rejectsNan = !UnityPoseAdapter.TryConvert(
                new Vector3d(double.NaN, 0.0, 0.0),
                Quaterniond.Identity,
                out _,
                out _);
            bool rejectsInfinity = !UnityPoseAdapter.TryConvert(
                new Vector3d(double.MaxValue, 0.0, 0.0),
                Quaterniond.Identity,
                out _,
                out _);
            bool rejectsZeroQuaternion = !UnityPoseAdapter.TryConvert(
                Vector3d.Zero,
                new Quaterniond(0.0, 0.0, 0.0, 0.0),
                out _,
                out _);
            AddCheck(checks, "Unity Pose Adapter invalid values",
                rejectsNan && rejectsInfinity && rejectsZeroQuaternion,
                "NaN, float-overflowing Infinity and unusable quaternions are rejected.");
        }

        private static void TestQuaternionContinuity(List<CheckRecord> checks)
        {
            Quaterniond q = QuaternionMath3d.FromAxisAngleRadians(
                new Vector3d(0.0, 1.0, 0.0),
                0.75);
            Quaterniond negative = QuaternionMath3d.Negate(q);
            Require(PoseInterpolation.TrySlerp(q, negative, 0.5, out Quaterniond sameRotation),
                "q/-q interpolation failed.");
            bool qSignContinuous = QuaternionMath3d.RepresentsSameRotation(q, sameRotation, 1e-10);

            Quaterniond from359 = QuaternionMath3d.FromAxisAngleRadians(
                new Vector3d(0.0, 1.0, 0.0),
                359.0 * Math.PI / 180.0);
            Quaterniond to1 = QuaternionMath3d.FromAxisAngleRadians(
                new Vector3d(0.0, 1.0, 0.0),
                1.0 * Math.PI / 180.0);
            Require(PoseInterpolation.TrySlerp(from359, to1, 0.5, out Quaterniond midpoint),
                "359-to-1 interpolation failed.");
            Vector3d midpointForward = QuaternionMath3d.Rotate(midpoint, new Vector3d(0.0, 0.0, 1.0));
            bool wrapContinuous = midpointForward.Z > 0.9999;
            AddCheck(checks, "Quaternion continuity",
                qSignContinuous && wrapContinuous,
                "q/-q and the 359-to-1 degree boundary follow the shortest continuous rotation.");
        }

        private static void TestZeroPoseAxes(
            CoordinateTransformProfile profile,
            List<CheckRecord> checks)
        {
            VehicleState zero = State(
                "AUV-01",
                0.0,
                0UL,
                Vector3d.Zero,
                Quaterniond.Identity,
                WorldFrame.UnityWorld,
                BodyFrame.UnityBody);
            Require(VehiclePoseConverter.TryConvert(
                zero,
                profile,
                out ConvertedVehiclePose converted,
                out ConversionError error), error.Message);
            Vector3d forward = QuaternionMath3d.Rotate(
                converted.Orientation,
                new Vector3d(1.0, 0.0, 0.0));
            Vector3d up = QuaternionMath3d.Rotate(
                converted.Orientation,
                new Vector3d(0.0, 1.0, 0.0));
            Vector3d right = QuaternionMath3d.Rotate(
                converted.Orientation,
                new Vector3d(0.0, 0.0, -1.0));
            AddCheck(checks, "N3 model-alignment multiplication",
                Dot(forward, new Vector3d(0.0, 0.0, 1.0)) > 0.999999 &&
                Dot(up, new Vector3d(0.0, 1.0, 0.0)) > 0.999999 &&
                Dot(right, new Vector3d(1.0, 0.0, 0.0)) > 0.999999,
                "Right multiplication maps model Forward +X, Up +Y and Right -Z to target +Z,+Y,+X.");
        }

        private static void TestN5PositionAxes(
            CoordinateTransformProfile profile,
            List<CheckRecord> checks)
        {
            Vector3d[] values =
            {
                Vector3d.Zero,
                new Vector3d(1.0, 0.0, 0.0),
                new Vector3d(0.0, 1.0, 0.0),
                new Vector3d(0.0, 0.0, 1.0),
                new Vector3d(-2.5, 3.25, -4.75)
            };
            bool passed = true;
            for (int index = 0; index < values.Length; index++)
            {
                VehicleState state = State(
                    "AUV-01",
                    index,
                    (ulong)index,
                    values[index],
                    Quaterniond.Identity,
                    WorldFrame.UnityWorld,
                    BodyFrame.UnityBody);
                if (!VehiclePoseConverter.TryConvert(
                        state,
                        profile,
                        out ConvertedVehiclePose converted,
                        out _) ||
                    Dot(converted.Position, values[index]) <
                    Math.Sqrt(Dot(converted.Position, converted.Position) *
                              Dot(values[index], values[index])) - 1e-9 &&
                    Dot(values[index], values[index]) > 1e-12)
                {
                    passed = false;
                    break;
                }

                passed &= Math.Abs(converted.Position.X - values[index].X) <= 1e-9 &&
                          Math.Abs(converted.Position.Y - values[index].Y) <= 1e-9 &&
                          Math.Abs(converted.Position.Z - values[index].Z) <= 1e-9;
            }

            AddCheck(checks, "N5 position axes and scale",
                passed,
                "Unity-native N5 test profile maps East/+X, Up/+Y and North/+Z directly, including zero, negative and combined values at scale 1.");
        }

        private static void TestN5AttitudeAxes(
            CoordinateTransformProfile profile,
            List<CheckRecord> checks)
        {
            Quaternion[] targetRotations =
            {
                Quaternion.Euler(0f, 30f, 0f),
                Quaternion.Euler(20f, 0f, 0f),
                Quaternion.Euler(0f, 0f, 15f),
                Quaternion.Euler(12f, 27f, -9f)
            };
            bool passed = true;
            for (int index = 0; index < targetRotations.Length; index++)
            {
                Quaterniond target = ToQuaterniond(targetRotations[index]);
                VehicleState state = State(
                    "AUV-01",
                    index,
                    (ulong)index,
                    Vector3d.Zero,
                    target,
                    WorldFrame.UnityWorld,
                    BodyFrame.UnityBody);
                if (!VehiclePoseConverter.TryConvert(
                        state,
                        profile,
                        out ConvertedVehiclePose converted,
                        out _))
                {
                    passed = false;
                    break;
                }

                Vector3d actualForward = QuaternionMath3d.Rotate(
                    converted.Orientation,
                    new Vector3d(1.0, 0.0, 0.0));
                Vector3d actualUp = QuaternionMath3d.Rotate(
                    converted.Orientation,
                    new Vector3d(0.0, 1.0, 0.0));
                Vector3d actualRight = QuaternionMath3d.Rotate(
                    converted.Orientation,
                    new Vector3d(0.0, 0.0, -1.0));
                Vector3d expectedForward = QuaternionMath3d.Rotate(
                    target,
                    new Vector3d(0.0, 0.0, 1.0));
                Vector3d expectedUp = QuaternionMath3d.Rotate(
                    target,
                    new Vector3d(0.0, 1.0, 0.0));
                Vector3d expectedRight = QuaternionMath3d.Rotate(
                    target,
                    new Vector3d(1.0, 0.0, 0.0));
                passed &= Dot(actualForward, expectedForward) > 0.999999 &&
                          Dot(actualUp, expectedUp) > 0.999999 &&
                          Dot(actualRight, expectedRight) > 0.999999;
            }

            AddCheck(checks, "N5 yaw, pitch, roll and combined attitude",
                passed,
                "Positive yaw, pitch, roll and a combined target rotation preserve the actual model forward, up and starboard directions after the single Y -90 model alignment.");
        }

        private static void TestSamplerFailureSurface(
            List<CheckRecord> checks,
            HashSet<string> failures)
        {
            CoordinateTransformProfile profile = CoordinateTransformProfiles.UnityNative(
                "N5_FAILURE_SURFACE",
                1.0,
                AttitudeDirection.BodyToWorld,
                Quaterniond.Identity);
            var policy = new RenderSamplingPolicy(
                0.2,
                0.25,
                1e-9,
                AfterLatestBehavior.HoldLatest,
                true);

            using (var empty = new VehicleStateStore(new VehicleStateStorePolicy(8, timeoutSeconds: 10.0)))
            {
                RenderPoseSample noData = VehicleRenderSampler.Sample(
                    empty,
                    Request(1UL, 0.0, 10.0, DataSourceStatus.Running, profile, policy));
                failures.Add(noData.FailureReason.ToString());
                AddCheck(checks, "NoData surface",
                    noData.FailureReason == RenderSampleFailureReason.NoData,
                    "Empty store reports NoData.");
            }

            using (var store = new VehicleStateStore(new VehicleStateStorePolicy(8, timeoutSeconds: 10.0)))
            {
                Publish(store, State(
                    "AUV-01", 0.0, 0UL, Vector3d.Zero, Quaterniond.Identity,
                    WorldFrame.UnityWorld, BodyFrame.UnityBody), 1UL, 20.0);
                RenderPoseSample epoch = VehicleRenderSampler.Sample(
                    store,
                    Request(2UL, 0.0, 20.0, DataSourceStatus.Running, profile, policy));
                RenderPoseSample faulted = VehicleRenderSampler.Sample(
                    store,
                    Request(1UL, 0.0, 20.0, DataSourceStatus.Faulted, profile, policy));
                failures.Add(epoch.FailureReason.ToString());
                failures.Add(faulted.FailureReason.ToString());
                AddCheck(checks, "EpochUnavailable and SourceFaulted surface",
                    epoch.FailureReason == RenderSampleFailureReason.EpochUnavailable &&
                    faulted.FailureReason == RenderSampleFailureReason.SourceFaulted,
                    "Sampler exposes epoch mismatch and source fault distinctly.");
            }

            using (var gapStore = new VehicleStateStore(new VehicleStateStorePolicy(8, timeoutSeconds: 10.0)))
            {
                Publish(gapStore, State(
                    "AUV-01", 0.0, 0UL, Vector3d.Zero, Quaterniond.Identity,
                    WorldFrame.UnityWorld, BodyFrame.UnityBody), 1UL, 30.0);
                Publish(gapStore, State(
                    "AUV-01", 1.0, 1UL, new Vector3d(1.0, 0.0, 0.0), Quaterniond.Identity,
                    WorldFrame.UnityWorld, BodyFrame.UnityBody), 1UL, 30.1);
                RenderPoseSample gap = VehicleRenderSampler.Sample(
                    gapStore,
                    Request(1UL, 0.5, 30.1, DataSourceStatus.Running, profile, policy));
                failures.Add(gap.FailureReason.ToString());
                AddCheck(checks, "GapTooLarge surface",
                    gap.FailureReason == RenderSampleFailureReason.GapTooLarge,
                    "A one-second bracket is rejected by the 0.2-second interpolation policy.");
            }

            using (var conversionStore = new VehicleStateStore(
                       new VehicleStateStorePolicy(8, timeoutSeconds: 10.0)))
            {
                Publish(conversionStore, State(
                    "AUV-01", 0.0, 0UL, Vector3d.Zero, Quaterniond.Identity,
                    WorldFrame.Unknown, BodyFrame.Unknown), 1UL, 40.0);
                RenderPoseSample conversion = VehicleRenderSampler.Sample(
                    conversionStore,
                    Request(1UL, 0.0, 40.0, DataSourceStatus.Running, profile, policy));
                failures.Add(conversion.FailureReason.ToString());
                AddCheck(checks, "ConversionFailed surface",
                    conversion.FailureReason == RenderSampleFailureReason.ConversionFailed,
                    "Frame mismatch/unknown input is rejected before Unity Transform write.");
            }
        }

        private static RenderSampleRequest Request(
            ulong epoch,
            double target,
            double localNow,
            DataSourceStatus status,
            CoordinateTransformProfile profile,
            RenderSamplingPolicy policy)
        {
            return new RenderSampleRequest(
                "local-test-n5",
                epoch,
                "AUV-01",
                target,
                localNow,
                status,
                profile,
                policy);
        }

        private static VehicleState State(
            string vehicleId,
            double timestamp,
            ulong sequence,
            Vector3d position,
            Quaterniond orientation,
            WorldFrame world,
            BodyFrame body)
        {
            return new VehicleState(
                vehicleId,
                VehicleType.Auv,
                timestamp,
                sequence,
                position,
                orientation,
                Vector3d.Zero,
                Vector3d.Zero,
                Vector3d.Zero,
                VehicleStateFields.Position | VehicleStateFields.Orientation,
                world,
                body);
        }

        private static void Publish(
            VehicleStateStore store,
            VehicleState state,
            ulong epoch,
            double receivedAt)
        {
            var received = new ReceivedVehicleState(
                state,
                "local-test-n5",
                epoch,
                receivedAt,
                SequenceKind.Synthetic,
                DecodeQualityFlags.None);
            Require(store.Publish(received) == PublishResult.Accepted, "Test state publish failed.");
        }

        private static T RequireComponent<T>(GameObject gameObject, string label)
            where T : Component
        {
            T component = gameObject.GetComponent<T>();
            Require(component != null, "Missing " + label + " on " + gameObject.name + ".");
            return component;
        }

        private static GameObject FindUniqueRoot(Scene scene, string name)
        {
            GameObject[] matches = scene.GetRootGameObjects()
                .Where(root => string.Equals(root.name, name, StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1, "Expected one scene root named " + name + ".");
            return matches[0];
        }

        private static Transform FindUniqueDescendant(Transform root, string name)
        {
            Transform[] matches = root.GetComponentsInChildren<Transform>(true)
                .Where(item => string.Equals(item.name, name, StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1, "Expected one descendant named " + name + ".");
            return matches[0];
        }

        private static void AddCheck(
            List<CheckRecord> checks,
            string name,
            bool passed,
            string detail)
        {
            checks.Add(new CheckRecord
            {
                name = name,
                passed = passed,
                detail = detail
            });
        }

        private static string BuildMarkdown(VerificationReport report)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# N5 AUV Public Pose Driver Verification");
            builder.AppendLine();
            builder.AppendLine("- Status: `" + report.status + "`");
            builder.AppendLine("- Scene hash unchanged: " + report.sceneHashUnchanged);
            builder.AppendLine("- Final scene dirty: " + report.finalSceneDirty);
            builder.AppendLine("- Source / vehicle: `" + report.sourceId + "` / `" + report.vehicleId + "`");
            builder.AppendLine("- Profile: `" + report.profileId + "`");
            builder.AppendLine("- Semantic axes: " + report.modelSemanticAxes);
            builder.AppendLine("- Model alignment: " + report.modelAlignment);
            builder.AppendLine("- Multiplication: `" + report.multiplicationOrder + "`");
            builder.AppendLine("- Clock: " + report.monotonicClockDomain);
            builder.AppendLine("- Modes: " + string.Join(", ", report.observedSampleModes));
            builder.AppendLine("- Failures: " + string.Join(", ", report.observedFailures));
            builder.AppendLine("- Epoch restart: " + report.epochBeforeRestart + " -> " + report.epochAfterRestart);
            builder.AppendLine();
            builder.AppendLine("## Checks");
            builder.AppendLine();
            builder.AppendLine("| Check | Result | Detail |");
            builder.AppendLine("|---|---:|---|");
            foreach (CheckRecord check in report.checks)
            {
                builder.AppendLine("| " + check.name + " | " +
                                   (check.passed ? "PASS" : "FAIL") + " | " +
                                   check.detail.Replace("|", "\\|") + " |");
            }

            return builder.ToString();
        }

        private static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private static double Dot(Vector3d left, Vector3d right)
        {
            return left.X * right.X + left.Y * right.Y + left.Z * right.Z;
        }

        private static Quaterniond ToQuaterniond(Quaternion value)
        {
            return new Quaterniond(value.x, value.y, value.z, value.w);
        }

        private static bool Near(Vector3 left, Vector3 right, float tolerance)
        {
            return (left - right).sqrMagnitude <= tolerance * tolerance;
        }

        private static bool Near(Quaternion left, Quaternion right, float tolerance)
        {
            return Mathf.Abs(Quaternion.Dot(left.normalized, right.normalized)) >= 1f - tolerance;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private readonly struct PoseRecord
        {
            private readonly Vector3 position;
            private readonly Quaternion rotation;
            private readonly Vector3 scale;

            private PoseRecord(Vector3 position, Quaternion rotation, Vector3 scale)
            {
                this.position = position;
                this.rotation = rotation;
                this.scale = scale;
            }

            public static PoseRecord Capture(Transform transform)
            {
                return new PoseRecord(
                    transform.localPosition,
                    transform.localRotation,
                    transform.localScale);
            }

            public bool EqualsCurrent(Transform transform, float tolerance)
            {
                return Near(transform.localPosition, position, tolerance) &&
                       Near(transform.localRotation, rotation, tolerance) &&
                       Near(transform.localScale, scale, tolerance);
            }
        }
    }
}
