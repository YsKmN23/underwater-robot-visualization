using System;
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

namespace UnderwaterRobotScene.EditorTools
{
    [InitializeOnLoad]
    public static class UsvRootPoseN6DPlayModeVerifier
    {
        private const string ActiveKey = "N6D.UsvPlayMode.Active";
        private const string PassedKey = "N6D.UsvPlayMode.Passed";
        private const string DetailKey = "N6D.UsvPlayMode.Detail";
        private const string BatchKey = "N6D.UsvPlayMode.Batch";
        private const string BeforeStopKey = "N6D.UsvPlayMode.BeforeStop";
        private const string AfterStaleKey = "N6D.UsvPlayMode.AfterStale";
        private const string AfterRestartKey = "N6D.UsvPlayMode.AfterRestart";
        private const string FirstNewSampleKey = "N6D.UsvPlayMode.FirstNewSample";
        private const string FinalRecoveryKey = "N6D.UsvPlayMode.FinalRecovery";
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";

        private enum Phase
        {
            WaitHealthy,
            ObserveMotion,
            WaitForHoldCapture,
            WaitForStale,
            WaitForRestart,
            WaitForDemo,
            WaitForPublic,
            WaitWhileDisabled,
            WaitForFinalRecovery
        }

        private static bool subscribed;
        private static bool referencesBound;
        private static bool spinnerRotationObserved;
        private static int errorCount;
        private static double enteredAt;
        private static double phaseStartedAt;
        private static Phase phase;

        private static Transform usv;
        private static Transform model;
        private static Transform rudder;
        private static Transform auv;
        private static Transform rov;
        private static VehiclePoseDriver driver;
        private static VehicleDataRuntimeHost host;
        private static VehiclePoseIntegrationConfiguration configuration;
        private static VehiclePoseProfileConfiguration profile;
        private static VehiclePoseControlAuthority authority;
        private static DemoMotionController demo;
        private static PropellerSpinner[] spinners;
        private static Vector3 modelLocalPosition;
        private static Quaternion modelLocalRotation;
        private static Vector3 modelLocalScale;
        private static Vector3 rudderLocalPosition;
        private static Quaternion rudderLocalRotation;
        private static Vector3 rudderLocalScale;
        private static Vector3[] spinnerLocalPositions;
        private static Quaternion[] spinnerInitialRotations;
        private static Vector3[] spinnerLocalScales;
        private static Vector3 usvMotionBaseline;
        private static Vector3 auvBaseline;
        private static Vector3 rovBaseline;
        private static Vector3 staleHoldPosition;
        private static Vector3 demoBaseline;
        private static Vector3 disabledBaseline;
        private static ulong initialEpoch;

        static UsvRootPoseN6DPlayModeVerifier()
        {
            if (SessionState.GetBool(ActiveKey, false))
            {
                Subscribe();
            }
        }

        [MenuItem("Tools/Underwater Demo/N6-D/Run USV Play Mode Verification")]
        public static void RunFromMenu()
        {
            Begin(false);
        }

        public static void RunBatch()
        {
            Begin(true);
        }

        private static void Begin(bool batch)
        {
            if (SessionState.GetBool(ActiveKey, false))
            {
                throw new InvalidOperationException(
                    "N6-D USV Play Mode verification is already active.");
            }

            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(PassedKey, false);
            SessionState.SetString(DetailKey, "Verification did not complete.");
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetString(BeforeStopKey, "not-captured");
            SessionState.SetString(AfterStaleKey, "not-captured");
            SessionState.SetString(AfterRestartKey, "not-captured");
            SessionState.SetString(FirstNewSampleKey, "not-captured");
            SessionState.SetString(FinalRecoveryKey, "not-captured");
            Subscribe();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        private static void Subscribe()
        {
            if (subscribed)
            {
                return;
            }

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;
            Application.logMessageReceived += OnLog;
            subscribed = true;
        }

        private static void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= OnEditorUpdate;
            Application.logMessageReceived -= OnLog;
            subscribed = false;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(ActiveKey, false))
            {
                return;
            }

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                enteredAt = Time.realtimeSinceStartupAsDouble;
                phaseStartedAt = enteredAt;
                phase = Phase.WaitHealthy;
                referencesBound = false;
                spinnerRotationObserved = false;
                errorCount = 0;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                Finish();
            }
        }

        private static void OnEditorUpdate()
        {
            if (!SessionState.GetBool(ActiveKey, false) ||
                !EditorApplication.isPlaying)
            {
                return;
            }

            try
            {
                if (!referencesBound)
                {
                    BindReferences();
                    ValidatePlayModeConversionCases();
                    referencesBound = true;
                }

                ObserveSpinnerRotation();
                double now = Time.realtimeSinceStartupAsDouble;
                double elapsed = now - phaseStartedAt;
                switch (phase)
                {
                    case Phase.WaitHealthy:
                        if (IsHealthy())
                        {
                            CaptureInitialHealthyState();
                            Advance(Phase.ObserveMotion, now);
                        }
                        else if (now - enteredAt > 4.0)
                        {
                            throw new InvalidOperationException(
                                "USV Driver did not become healthy within four seconds.");
                        }
                        break;

                    case Phase.ObserveMotion:
                        if (elapsed >= 1.4)
                        {
                            ValidateLiveMotion();
                            SessionState.SetString(
                                BeforeStopKey,
                                CaptureRecoveryState("before-stop", now));
                            host.StopSource();
                            Advance(Phase.WaitForHoldCapture, now);
                        }
                        break;

                    case Phase.WaitForHoldCapture:
                        if (elapsed >= 0.35)
                        {
                            staleHoldPosition = usv.position;
                            Advance(Phase.WaitForStale, now);
                        }
                        break;

                    case Phase.WaitForStale:
                        if (elapsed >= 0.65)
                        {
                            ValidateStaleHold();
                            SessionState.SetString(
                                AfterStaleKey,
                                CaptureRecoveryState("after-stale", now));
                            host.RestartSource();
                            SessionState.SetString(
                                AfterRestartKey,
                                CaptureRecoveryState("immediately-after-restart", now));
                            Advance(Phase.WaitForRestart, now);
                        }
                        break;

                    case Phase.WaitForRestart:
                        string recoveryState =
                            CaptureRecoveryState("restart-wait", now);
                        SessionState.SetString(FinalRecoveryKey, recoveryState);
                        CaptureFirstNewSample(now);
                        if (elapsed >= 0.65 && IsHealthy())
                        {
                            ValidateEpochRecovery();
                            authority.Mode = VehiclePoseControlMode.Demo;
                            demoBaseline = usv.position;
                            Advance(Phase.WaitForDemo, now);
                        }
                        else if (elapsed > 3.0)
                        {
                            throw new InvalidOperationException(
                                "USV Driver did not recover after epoch restart. " +
                                RecoveryEvidence(recoveryState));
                        }
                        break;

                    case Phase.WaitForDemo:
                        if (elapsed >= 1.1)
                        {
                            ValidateDemoOwnership();
                            authority.Mode = VehiclePoseControlMode.PublicData;
                            Advance(Phase.WaitForPublic, now);
                        }
                        break;

                    case Phase.WaitForPublic:
                        if (elapsed >= 0.5 && IsHealthy())
                        {
                            ValidatePublicOwnership();
                            driver.enabled = false;
                            disabledBaseline = usv.position;
                            Advance(Phase.WaitWhileDisabled, now);
                        }
                        break;

                    case Phase.WaitWhileDisabled:
                        if (elapsed >= 0.4)
                        {
                            ValidateDisabledHold();
                            driver.enabled = true;
                            Advance(Phase.WaitForFinalRecovery, now);
                        }
                        break;

                    case Phase.WaitForFinalRecovery:
                        if (elapsed >= 0.5 && IsHealthy())
                        {
                            ValidateFinal();
                            SessionState.SetBool(PassedKey, true);
                            SessionState.SetString(
                                DetailKey,
                                "USV public root motion, explicit position/attitude axes, interpolation, stale hold, epoch recovery, " +
                                "authority switching, Driver disable, two local spinners, fixed rudder and AUV/ROV regressions passed.");
                            EditorApplication.ExitPlaymode();
                        }
                        else if (elapsed > 3.0)
                        {
                            throw new InvalidOperationException(
                                "USV Driver did not recover after being re-enabled.");
                        }
                        break;
                }
            }
            catch (Exception exception)
            {
                SessionState.SetBool(PassedKey, false);
                SessionState.SetString(
                    DetailKey,
                    exception.GetType().Name + ": " + exception.Message);
                EditorApplication.ExitPlaymode();
            }
        }

        private static void BindReferences()
        {
            usv = RequireGameObject("USV_Blue_Surface").transform;
            model = RequireDescendant(usv, "USV_FineModel_V1_Imported");
            rudder = RequireDescendant(usv, "USV_Rudder_Main");
            auv = RequireGameObject("AUV_Yellow_Underwater").transform;
            rov = RequireGameObject("ROV_Box_Seabed").transform;
            driver = RequireGameObject("USV_PublicPoseDriver")
                .GetComponent<VehiclePoseDriver>();
            host = driver == null ? null : driver.RuntimeHost;
            configuration = driver == null ? null : driver.IntegrationConfiguration;
            profile = driver == null ? null : driver.ProfileConfiguration;
            authority = usv.GetComponent<VehiclePoseControlAuthority>();
            demo = UnityEngine.Object.FindAnyObjectByType<DemoMotionController>();
            spinners = usv.GetComponentsInChildren<PropellerSpinner>(true);

            Require(driver != null &&
                    host != null &&
                    configuration != null &&
                    profile != null &&
                    authority != null &&
                    demo != null &&
                    spinners.Length == 2,
                "Required N6-D runtime components are missing.");
            Require(ReferenceEquals(host.IntegrationConfiguration, configuration) &&
                    ReferenceEquals(driver.IntegrationConfiguration, configuration) &&
                    ReferenceEquals(driver.ControlAuthority, authority) &&
                    ReferenceEquals(demo.usvControlAuthority, authority) &&
                    configuration.GeneratorKind ==
                    DeterministicVehicleStateGeneratorKind.UsvDiagnosticTrajectory,
                "USV Host, Driver, Demo and diagnostic configuration bindings differ.");
        }

        private static bool IsHealthy()
        {
            return authority.PublicDataOwnsControl &&
                   driver.enabled &&
                   driver.OwnsControl &&
                   host.SourceMode == VehicleRuntimeSourceMode.RouteFollowing &&
                   !demo.DrivesUsv &&
                   driver.LastFailureReason == RenderSampleFailureReason.None &&
                   (driver.LastSampleMode == RenderSampleMode.Exact ||
                    driver.LastSampleMode == RenderSampleMode.Interpolated ||
                    driver.LastSampleMode == RenderSampleMode.HeldLatest);
        }

        private static void CaptureInitialHealthyState()
        {
            Require(
                Vector3.Dot(usv.TransformDirection(Vector3.left), Vector3.forward) > 0.999f &&
                Vector3.Dot(usv.TransformDirection(Vector3.up), Vector3.up) > 0.999f &&
                Vector3.Dot(usv.TransformDirection(Vector3.forward), Vector3.right) > 0.999f,
                "Zero-pose USV visual axes are not Forward +Z, Up +Y, Right +X.");
            Require((usv.position - configuration.TestOrigin).sqrMagnitude < 1e-5f,
                "USV identity-hold position differs from its diagnostic origin.");
            Require(host.TryGetActiveEpoch(out initialEpoch),
                "USV Host has no active initial epoch.");

            modelLocalPosition = model.localPosition;
            modelLocalRotation = model.localRotation;
            modelLocalScale = model.localScale;
            rudderLocalPosition = rudder.localPosition;
            rudderLocalRotation = rudder.localRotation;
            rudderLocalScale = rudder.localScale;
            spinnerLocalPositions = spinners.Select(item => item.transform.localPosition).ToArray();
            spinnerInitialRotations = spinners.Select(item => item.transform.localRotation).ToArray();
            spinnerLocalScales = spinners.Select(item => item.transform.localScale).ToArray();
            usvMotionBaseline = usv.position;
            auvBaseline = auv.position;
            rovBaseline = rov.position;
        }

        private static void ObserveSpinnerRotation()
        {
            if (spinnerInitialRotations == null)
            {
                return;
            }

            for (int index = 0; index < spinners.Length; index++)
            {
                if (!Near(
                        spinners[index].transform.localRotation,
                        spinnerInitialRotations[index],
                        1e-4f))
                {
                    spinnerRotationObserved = true;
                    return;
                }
            }
        }

        private static void ValidateLiveMotion()
        {
            Require(errorCount == 0,
                "Runtime Console reported an error before live validation.");
            Require(IsHealthy(),
                "USV Driver lost PublicData ownership during live motion.");
            Require((usv.position - usvMotionBaseline).sqrMagnitude > 1e-5f,
                "USV diagnostic trajectory did not move the root.");
            Require(driver.LastSampleMode == RenderSampleMode.Interpolated ||
                    driver.LastSampleMode == RenderSampleMode.Exact,
                "USV did not observe an exact or interpolated live sample.");
            Require((auv.position - auvBaseline).sqrMagnitude > 1e-6f,
                "AUV public pose stopped while USV was active.");
            Require((rov.position - rovBaseline).sqrMagnitude > 1e-6f,
                "ROV public pose stopped while USV was active.");
            ValidateProtectedLocals();
        }

        private static void ValidateStaleHold()
        {
            Require(host.SourceStatus == DataSourceStatus.Stopped,
                "USV source did not enter Stopped state.");
            Require(driver.LastFailureReason == RenderSampleFailureReason.Stale,
                "USV Driver did not report stale after the configured timeout.");
            Require(Near(usv.position, staleHoldPosition, 1e-5f),
                "USV root changed while stale data was held.");
            Require(authority.PublicDataOwnsControl && !demo.DrivesUsv,
                "Demo wrote USV while stale PublicData authority was active.");
        }

        private static void ValidateEpochRecovery()
        {
            Require(host.TryGetActiveEpoch(out ulong recoveredEpoch) &&
                    recoveredEpoch != initialEpoch,
                "USV source epoch did not change after restart.");
            Require(IsHealthy(), "USV Driver did not recover after epoch restart.");
        }

        private static void ValidateDemoOwnership()
        {
            Require(authority.DemoOwnsControl &&
                    !demo.DrivesUsv &&
                    driver.OwnsControl &&
                    host.SourceMode == VehicleRuntimeSourceMode.LocalDiagnostic &&
                    host.TryGetActiveEpoch(out ulong epoch) &&
                    driver.LastAppliedSourceEpoch == epoch,
                "Demo mode did not retain unique USV root ownership in VehiclePoseDriver.");
            Require((usv.position - demoBaseline).sqrMagnitude > 1e-8f,
                "USV LocalDiagnostic Demo motion did not run through the Driver.");
            Require(usv.position.sqrMagnitude > 0.1f,
                "Authority switch reset USV to the world origin.");
        }

        private static void ValidatePublicOwnership()
        {
            Require(IsHealthy(),
                "PublicData mode did not return unique USV ownership to VehiclePoseDriver.");
        }

        private static void ValidateDisabledHold()
        {
            Require(!driver.enabled &&
                    !driver.OwnsControl &&
                    authority.PublicDataOwnsControl &&
                    !demo.DrivesUsv,
                "Disabled Driver ownership state is incorrect.");
            Require(Near(usv.position, disabledBaseline, 1e-5f),
                "USV root changed while the PublicData Driver was disabled.");
        }

        private static void ValidateFinal()
        {
            Require(errorCount == 0,
                "Captured " + errorCount + " runtime Console errors.");
            Require(IsHealthy(), "USV Driver did not finish healthy.");
            Require(spinners.All(item => item.enabled),
                "A USV PropellerSpinner was disabled.");
            ValidateProtectedLocals();
            ValidateSiblingVehicle("AUV_Yellow_Underwater", "AUV_PublicPoseDriver", demo.DrivesAuv);
            ValidateSiblingVehicle("ROV_Box_Seabed", "ROV_PublicPoseDriver", demo.DrivesRov);
        }

        private static void ValidateSiblingVehicle(
            string rootName,
            string driverName,
            bool demoDrives)
        {
            Transform root = RequireGameObject(rootName).transform;
            VehiclePoseControlAuthority siblingAuthority =
                root.GetComponent<VehiclePoseControlAuthority>();
            VehiclePoseDriver siblingDriver =
                RequireGameObject(driverName).GetComponent<VehiclePoseDriver>();
            Require(siblingAuthority != null &&
                    siblingAuthority.PublicDataOwnsControl &&
                    siblingDriver != null &&
                    siblingDriver.OwnsControl &&
                    siblingDriver.LastFailureReason == RenderSampleFailureReason.None &&
                    !demoDrives,
                rootName + " PublicData behavior regressed.");
        }

        private static void ValidateProtectedLocals()
        {
            Require(Near(model.localPosition, modelLocalPosition, 1e-6f) &&
                    Near(model.localRotation, modelLocalRotation, 1e-5f) &&
                    Near(model.localScale, modelLocalScale, 1e-6f),
                "USV Driver changed the imported model local Transform.");
            Require(Near(rudder.localPosition, rudderLocalPosition, 1e-6f) &&
                    Near(rudder.localRotation, rudderLocalRotation, 1e-5f) &&
                    Near(rudder.localScale, rudderLocalScale, 1e-6f),
                "USV Driver changed the main rudder local Transform.");
            for (int index = 0; index < spinners.Length; index++)
            {
                Require(
                    Near(spinners[index].transform.localPosition,
                        spinnerLocalPositions[index], 1e-6f) &&
                    Near(spinners[index].transform.localScale,
                        spinnerLocalScales[index], 1e-6f),
                    "USV Driver changed a spinner local position or scale.");
            }
        }

        private static void ValidatePlayModeConversionCases()
        {
            Require(profile.TryBuildProfile(
                    out CoordinateTransformProfile transformProfile,
                    out string profileError),
                "USV Profile failed to build in Play Mode: " + profileError);
            Vector3[] positions =
            {
                Vector3.right,
                Vector3.up,
                Vector3.forward,
                new Vector3(-1f, -2f, -3f)
            };
            foreach (Vector3 position in positions)
            {
                Require(VehiclePoseConverter.TryConvert(
                        State(position, Quaternion.identity),
                        transformProfile,
                        out ConvertedVehiclePose converted,
                        out ConversionError error),
                    "USV Play Mode position conversion failed: " + error.Message);
                Require(UnityPoseAdapter.TryConvert(
                        converted.Position,
                        converted.Orientation,
                        out Vector3 unityPosition,
                        out _),
                    "USV Play Mode adapter rejected a position.");
                Require(Near(position, unityPosition, 1e-6f),
                    "USV Play Mode East/Up/North position mapping changed.");
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
                        State(Vector3.zero, target),
                        transformProfile,
                        out ConvertedVehiclePose converted,
                        out ConversionError error),
                    "USV Play Mode attitude conversion failed: " + error.Message);
                Require(UnityPoseAdapter.TryConvert(
                        converted.Position,
                        converted.Orientation,
                        out _,
                        out Quaternion rootRotation),
                    "USV Play Mode adapter rejected an attitude.");
                Require(
                    Vector3.Dot(rootRotation * Vector3.left,
                        target * Vector3.forward) > 0.999999f &&
                    Vector3.Dot(rootRotation * Vector3.up,
                        target * Vector3.up) > 0.999999f &&
                    Vector3.Dot(rootRotation * Vector3.forward,
                        target * Vector3.right) > 0.999999f,
                    "USV Play Mode identity/yaw/pitch/roll/combined axes changed.");
            }
        }

        private static VehicleState State(Vector3 position, Quaternion orientation)
        {
            Quaternion normalized = orientation.normalized;
            return new VehicleState(
                "USV-01",
                VehicleType.Usv,
                0UL,
                0UL,
                new Vector3d(position.x, position.y, position.z),
                new Quaterniond(normalized.x, normalized.y, normalized.z, normalized.w),
                Vector3d.Zero,
                Vector3d.Zero,
                Vector3d.Zero,
                VehicleStateFields.Position | VehicleStateFields.Orientation,
                WorldFrame.UnityWorld,
                BodyFrame.UnityBody);
        }

        private static void Advance(Phase next, double now)
        {
            phase = next;
            phaseStartedAt = now;
        }

        private static void CaptureFirstNewSample(double now)
        {
            if (!string.Equals(
                    SessionState.GetString(FirstNewSampleKey, "not-captured"),
                    "not-captured",
                    StringComparison.Ordinal) ||
                !host.TryGetActiveEpoch(out ulong epoch) ||
                epoch == initialEpoch ||
                !host.TryGetLatestSourceTimestamp(out _))
            {
                return;
            }

            SessionState.SetString(
                FirstNewSampleKey,
                CaptureRecoveryState("first-new-sample", now));
        }

        private static string CaptureRecoveryState(string label, double now)
        {
            bool hasEpoch = host.TryGetActiveEpoch(out ulong epoch);
            bool hasLatestTimestamp =
                host.TryGetLatestSourceTimestamp(out double latestTimestamp);
            ReceivedVehicleState latest = default;
            bool hasStoreLatest = hasEpoch && host.Store != null &&
                host.Store.TryReadLatest(
                    host.SourceId,
                    epoch,
                    host.VehicleId,
                    out latest);
            bool sampleModeAllowed =
                driver.LastSampleMode == RenderSampleMode.Exact ||
                driver.LastSampleMode == RenderSampleMode.Interpolated ||
                driver.LastSampleMode == RenderSampleMode.HeldLatest;

            var text = new StringBuilder();
            text.Append(label);
            text.Append(" now=").Append(Number(now));
            text.Append(" initialized=").Append(host.IsInitialized);
            text.Append(" sourceMode=").Append(host.SourceMode);
            text.Append(" sourceStatus=").Append(host.SourceStatus);
            text.Append(" hasEpoch=").Append(hasEpoch);
            text.Append(" epoch=").Append(hasEpoch ? epoch.ToString(CultureInfo.InvariantCulture) : "unavailable");
            text.Append(" hasLatestTimestamp=").Append(hasLatestTimestamp);
            text.Append(" latestTimestamp=").Append(hasLatestTimestamp ? Number(latestTimestamp) : "unavailable");
            text.Append(" hasStoreLatest=").Append(hasStoreLatest);
            if (hasStoreLatest)
            {
                text.Append(" storeEpoch=").Append(latest.SourceEpoch);
                text.Append(" storeSequence=").Append(latest.State.SequenceNumber);
                text.Append(" storeSourceTime=").Append(Number(latest.State.SourceTimestampSeconds));
                text.Append(" storeReceivedAt=").Append(Number(latest.ReceivedAtMonotonicSeconds));
            }
            text.Append(" authorityPublic=").Append(authority.PublicDataOwnsControl);
            text.Append(" driverEnabled=").Append(driver.enabled);
            text.Append(" driverActive=").Append(driver.isActiveAndEnabled);
            text.Append(" driverOwns=").Append(driver.OwnsControl);
            text.Append(" demoDrivesUsv=").Append(demo.DrivesUsv);
            text.Append(" sampleMode=").Append(driver.LastSampleMode);
            text.Append(" sampleModeAllowed=").Append(sampleModeAllowed);
            text.Append(" failure=").Append(driver.LastFailureReason);
            text.Append(" failureMessage=").Append(driver.LastFailureMessage);
            text.Append(" hasAppliedPose=").Append(driver.HasAppliedPose);
            text.Append(" hasFreshAppliedPose=").Append(driver.HasFreshAppliedPose);
            text.Append(" appliedEpoch=").Append(driver.LastAppliedSourceEpoch);
            text.Append(" dataAge=").Append(Number(driver.LastDataAgeSeconds));
            text.Append(" lastSuccess=").Append(Number(driver.LastSuccessfulMonotonicSeconds));
            text.Append(" healthy=").Append(IsHealthy());
            text.Append(" root=").Append(Vector(usv.position));
            return text.ToString();
        }

        private static string RecoveryEvidence(string finalState)
        {
            return "beforeStop={" +
                   SessionState.GetString(BeforeStopKey, "not-captured") +
                   "} afterStale={" +
                   SessionState.GetString(AfterStaleKey, "not-captured") +
                   "} immediatelyAfterRestart={" +
                   SessionState.GetString(AfterRestartKey, "not-captured") +
                   "} firstNewSample={" +
                   SessionState.GetString(FirstNewSampleKey, "not-captured") +
                   "} final={" + finalState + "}";
        }

        private static string Number(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Vector(Vector3 value)
        {
            return "(" + Number(value.x) + "," + Number(value.y) + "," +
                   Number(value.z) + ")";
        }

        private static void Finish()
        {
            bool passed = SessionState.GetBool(PassedKey, false);
            string detail = SessionState.GetString(DetailKey, "No result.");
            string outputDirectory = EvidenceDirectory();
            Directory.CreateDirectory(outputDirectory);
            string timestamp = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
            string status = passed
                ? "N6D_USV_PLAY_MODE_VALIDATION_PASS"
                : "N6D_USV_PLAY_MODE_VALIDATION_FAIL";
            string beforeStop = SessionState.GetString(BeforeStopKey, "not-captured");
            string afterStale = SessionState.GetString(AfterStaleKey, "not-captured");
            string afterRestart = SessionState.GetString(AfterRestartKey, "not-captured");
            string firstNewSample =
                SessionState.GetString(FirstNewSampleKey, "not-captured");
            string finalRecovery =
                SessionState.GetString(FinalRecoveryKey, "not-captured");
            string json =
                "{\n" +
                "  \"status\": \"" + status + "\",\n" +
                "  \"generatedAtIso8601\": \"" + Escape(timestamp) + "\",\n" +
                "  \"passed\": " + (passed ? "true" : "false") + ",\n" +
                "  \"detail\": \"" + Escape(detail) + "\",\n" +
                "  \"stateBeforeStop\": \"" + Escape(beforeStop) + "\",\n" +
                "  \"stateAfterStale\": \"" + Escape(afterStale) + "\",\n" +
                "  \"stateImmediatelyAfterRestart\": \"" + Escape(afterRestart) + "\",\n" +
                "  \"firstNewSampleAfterRestart\": \"" + Escape(firstNewSample) + "\",\n" +
                "  \"finalRecoveryState\": \"" + Escape(finalRecovery) + "\"\n" +
                "}\n";
            File.WriteAllText(
                Path.Combine(outputDirectory, "n6d_usv_playmode_report.json"),
                json,
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(outputDirectory, "n6d_usv_playmode_report.md"),
                "# N6-D USV Play Mode Verification\n\n" +
                "- Status: `" + status + "`\n" +
                "- Detail: " + detail + "\n" +
                "- State before Stop: `" + beforeStop + "`\n" +
                "- State after Stale: `" + afterStale + "`\n" +
                "- State immediately after Restart: `" + afterRestart + "`\n" +
                "- First new sample after Restart: `" + firstNewSample + "`\n" +
                "- Final recovery state: `" + finalRecovery + "`\n",
                new UTF8Encoding(false));

            bool batch = SessionState.GetBool(BatchKey, false);
            SessionState.SetBool(ActiveKey, false);
            SessionState.SetBool(BatchKey, false);
            Unsubscribe();
            Debug.Log(status + " | " + detail);
            if (batch)
            {
                EditorApplication.Exit(passed ? 0 : 1);
            }
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

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error ||
                type == LogType.Exception ||
                type == LogType.Assert)
            {
                errorCount++;
            }
        }

        private static GameObject RequireGameObject(string name)
        {
            GameObject value = GameObject.Find(name);
            Require(value != null, "Missing GameObject " + name + ".");
            return value;
        }

        private static Transform RequireDescendant(Transform root, string name)
        {
            Transform[] matches = root.GetComponentsInChildren<Transform>(true)
                .Where(item => string.Equals(item.name, name, StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1, "Expected one descendant " + name + ".");
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

        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
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
