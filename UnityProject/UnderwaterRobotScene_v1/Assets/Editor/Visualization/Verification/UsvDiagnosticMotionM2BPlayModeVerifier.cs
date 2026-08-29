using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.LocalTesting;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Sampling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    [InitializeOnLoad]
    public static class UsvDiagnosticMotionM2BPlayModeVerifier
    {
        private const string ActiveKey = "M2B.UsvPlayMode.Active";
        private const string PassedKey = "M2B.UsvPlayMode.Passed";
        private const string DetailKey = "M2B.UsvPlayMode.Detail";
        private const string BatchKey = "M2B.UsvPlayMode.Batch";
        private const string FormalSourceModeKey = "M2B.UsvPlayMode.FormalSourceMode";
        private const string FixtureSourceModeKey = "M2B.UsvPlayMode.FixtureSourceMode";
        private const string FinalRuntimeStateKey = "M2B.UsvPlayMode.FinalRuntimeState";
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";

        private enum Phase
        {
            WaitHealthy,
            ObserveCycle,
            WaitForHoldCapture,
            WaitForStale,
            WaitForRestart,
            WaitWhileDisabled,
            WaitForReenabled,
            WaitForDemo,
            WaitForPublic
        }

        private static bool subscribed;
        private static bool referencesBound;
        private static bool fixtureEstablished;
        private static int errorCount;
        private static double enteredAt;
        private static double phaseStartedAt;
        private static Phase phase;

        private static Transform usv;
        private static Transform model;
        private static Transform rudder;
        private static Transform water;
        private static Transform auv;
        private static Transform rov;
        private static Renderer waterRenderer;
        private static Material waterMaterial;
        private static VehiclePoseDriver driver;
        private static VehicleDataRuntimeHost host;
        private static VehiclePoseIntegrationConfiguration configuration;
        private static VehiclePoseControlAuthority authority;
        private static DemoMotionController demo;
        private static PropellerSpinner[] spinners;

        private static Vector3 origin;
        private static Quaternion zeroRotation;
        private static Vector3 baselineForward;
        private static ulong initialEpoch;
        private static Vector3 auvBaseline;
        private static Vector3 rovBaseline;
        private static Vector3 modelLocalPosition;
        private static Quaternion modelLocalRotation;
        private static Vector3 modelLocalScale;
        private static Vector3 rudderLocalPosition;
        private static Quaternion rudderLocalRotation;
        private static Vector3 rudderLocalScale;
        private static Vector3 waterPosition;
        private static Quaternion waterRotation;
        private static Vector3 waterScale;
        private static Vector3[] spinnerLocalPositions;
        private static Quaternion[] spinnerInitialRotations;
        private static Vector3[] spinnerLocalScales;

        private static bool spinnerRotationObserved;
        private static int initialHoldSamples;
        private static int middleHoldSamples;
        private static int finalHoldSamples;
        private static int loopHoldSamples;
        private static bool firstTurnObserved;
        private static bool secondTurnObserved;
        private static bool firstXObserved;
        private static bool firstZObserved;
        private static bool secondXObserved;
        private static bool secondZObserved;
        private static bool firstYawCaptured;
        private static bool secondYawCaptured;
        private static float firstYawDegrees;
        private static float secondYawDegrees;
        private static bool firstReturnObserved;
        private static bool secondReturnObserved;
        private static bool loopObserved;
        private static bool hasPreviousCyclePose;
        private static double previousCycleTime;
        private static Vector3 previousCyclePosition;
        private static Quaternion previousCycleRotation;
        private static Vector3 staleHoldPosition;
        private static Quaternion staleHoldRotation;
        private static Vector3 disabledPosition;
        private static Quaternion disabledRotation;
        private static Vector3 demoBaseline;
        private static float waterRelationY;
        private static VehicleRuntimeSourceMode formalSourceMode;

        static UsvDiagnosticMotionM2BPlayModeVerifier()
        {
            if (SessionState.GetBool(ActiveKey, false))
            {
                Subscribe();
            }
        }

        [MenuItem("Tools/Underwater Demo/M2-B/Run USV Diagnostic Play Mode Verification")]
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
                    "M2-B USV Play Mode verification is already active.");
            }

            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(PassedKey, false);
            SessionState.SetString(DetailKey, "Verification did not complete.");
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetString(FormalSourceModeKey, "not-captured");
            SessionState.SetString(FixtureSourceModeKey, "not-captured");
            SessionState.SetString(FinalRuntimeStateKey, "not-captured");
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
                fixtureEstablished = false;
                errorCount = 0;
                ResetObservations();
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
                            CaptureInitialState();
                            Advance(Phase.ObserveCycle, now);
                        }
                        else if (now - enteredAt > 4.0)
                        {
                            throw new InvalidOperationException(
                                "USV Driver did not become healthy within four seconds.");
                        }
                        break;

                    case Phase.ObserveCycle:
                        ObserveCycle(now);
                        if (CurrentSourceTime(now) >= 14.35)
                        {
                            ValidateCycle();
                            host.StopSource();
                            Advance(Phase.WaitForHoldCapture, now);
                        }
                        else if (elapsed > 20.0)
                        {
                            throw new InvalidOperationException(
                                "Timed out while observing the 14-second USV cycle.");
                        }
                        break;

                    case Phase.WaitForHoldCapture:
                        if (elapsed >= 0.35)
                        {
                            staleHoldPosition = usv.position;
                            staleHoldRotation = usv.rotation;
                            Advance(Phase.WaitForStale, now);
                        }
                        break;

                    case Phase.WaitForStale:
                        if (elapsed >= 0.65)
                        {
                            ValidateStaleHold();
                            host.RestartSource();
                            Advance(Phase.WaitForRestart, now);
                        }
                        break;

                    case Phase.WaitForRestart:
                        if (elapsed >= 0.65 && IsHealthy())
                        {
                            ValidateRestart();
                            disabledPosition = usv.position;
                            disabledRotation = usv.rotation;
                            driver.enabled = false;
                            Advance(Phase.WaitWhileDisabled, now);
                        }
                        else if (elapsed > 3.0)
                        {
                            throw new InvalidOperationException(
                                "USV Driver did not recover after source restart.");
                        }
                        break;

                    case Phase.WaitWhileDisabled:
                        if (elapsed >= 0.4)
                        {
                            ValidateDisabledHold();
                            driver.enabled = true;
                            Advance(Phase.WaitForReenabled, now);
                        }
                        break;

                    case Phase.WaitForReenabled:
                        if (elapsed >= 0.5 && IsHealthy())
                        {
                            authority.Mode = VehiclePoseControlMode.Demo;
                            demoBaseline = usv.position;
                            Advance(Phase.WaitForDemo, now);
                        }
                        else if (elapsed > 3.0)
                        {
                            throw new InvalidOperationException(
                                "USV Driver did not recover after re-enable.");
                        }
                        break;

                    case Phase.WaitForDemo:
                        if (elapsed >= 0.6)
                        {
                            ValidateDemoOwnership();
                            authority.Mode = VehiclePoseControlMode.PublicData;
                            Advance(Phase.WaitForPublic, now);
                        }
                        break;

                    case Phase.WaitForPublic:
                        if (elapsed >= 0.5 && IsHealthy())
                        {
                            ValidateFinal();
                            SessionState.SetString(
                                FinalRuntimeStateKey,
                                CaptureRuntimeState("final-local-diagnostic"));
                            RestoreFormalSourceMode();
                            SessionState.SetBool(PassedKey, true);
                            SessionState.SetString(
                                DetailKey,
                                "An isolated non-persistent LocalDiagnostic fixture proved the full 14-second horizontal cycle, fixed Y, yaw-only root presentation, " +
                                "three holds, opposite closed turns, loop, stale/restart, Driver disable/re-enable, " +
                                "Authority, spinners, rudder, water and AUV/ROV isolation passed. " +
                                "Static water relation rootY-waterY=" +
                                waterRelationY.ToString("R", CultureInfo.InvariantCulture) + ".");
                            EditorApplication.ExitPlaymode();
                        }
                        else if (elapsed > 3.0)
                        {
                            throw new InvalidOperationException(
                                "PublicData ownership did not recover.");
                        }
                        break;
                }
            }
            catch (Exception exception)
            {
                string runtimeState = referencesBound
                    ? CaptureRuntimeState("failure")
                    : "references-not-bound";
                SessionState.SetString(FinalRuntimeStateKey, runtimeState);
                RestoreFormalSourceMode();
                SessionState.SetBool(PassedKey, false);
                SessionState.SetString(
                    DetailKey,
                    exception.GetType().Name + ": " + exception.Message +
                    " | " + runtimeState);
                EditorApplication.ExitPlaymode();
            }
        }

        private static void BindReferences()
        {
            usv = RequireGameObject("USV_Blue_Surface").transform;
            model = RequireDescendant(usv, "USV_FineModel_V1_Imported");
            rudder = RequireDescendant(usv, "USV_Rudder_Main");
            water = RequireGameObject("Water_Surface").transform;
            waterRenderer = water.GetComponent<Renderer>();
            auv = RequireGameObject("AUV_Yellow_Underwater").transform;
            rov = RequireGameObject("ROV_Box_Seabed").transform;
            driver = RequireGameObject("USV_PublicPoseDriver")
                .GetComponent<VehiclePoseDriver>();
            host = driver == null ? null : driver.RuntimeHost;
            configuration = driver == null ? null : driver.IntegrationConfiguration;
            authority = usv.GetComponent<VehiclePoseControlAuthority>();
            demo = UnityEngine.Object.FindAnyObjectByType<DemoMotionController>();
            spinners = usv.GetComponentsInChildren<PropellerSpinner>(true);

            Require(driver != null &&
                    host != null &&
                    configuration != null &&
                    authority != null &&
                    demo != null &&
                    waterRenderer != null &&
                    spinners.Length == 2,
                "Required M2-B runtime references are missing.");
            Require(ReferenceEquals(driver.TargetRoot, usv) &&
                    ReferenceEquals(driver.RuntimeHost, host) &&
                    ReferenceEquals(driver.ControlAuthority, authority) &&
                    ReferenceEquals(demo.usvControlAuthority, authority) &&
                    configuration.GeneratorKind ==
                    DeterministicVehicleStateGeneratorKind.UsvDiagnosticTrajectory,
                "USV Driver, Host, Authority, target, Demo or generator binding changed.");

            formalSourceMode = host.SourceMode;
            Require(formalSourceMode == VehicleRuntimeSourceMode.RouteFollowing,
                "The approved Formal USV Host is expected to use RouteFollowing.");
            SessionState.SetString(
                FormalSourceModeKey,
                formalSourceMode.ToString());
            host.ShutdownForDiagnostics();
            host.ConfigureSourceMode(VehicleRuntimeSourceMode.LocalDiagnostic);
            host.InitializeForDiagnostics(host.MonotonicNowSeconds);
            Require(host.SourceMode == VehicleRuntimeSourceMode.LocalDiagnostic &&
                    host.SourceStatus == DataSourceStatus.Running,
                "The non-persistent M2-B LocalDiagnostic fixture did not start.");
            fixtureEstablished = true;
            SessionState.SetString(
                FixtureSourceModeKey,
                host.SourceMode.ToString());
        }

        private static void CaptureInitialState()
        {
            origin = configuration.TestOrigin;
            Require(Near(usv.position, origin, 2e-3f),
                "USV did not begin at its configured diagnostic origin.");
            Require(Vector3.Dot(usv.up, Vector3.up) > 0.99999f,
                "USV zero pose contains pitch or roll.");
            zeroRotation = usv.rotation;
            baselineForward = (zeroRotation * Vector3.left).normalized;
            Require(host.TryGetActiveEpoch(out initialEpoch),
                "USV Host has no active epoch.");

            auvBaseline = auv.position;
            rovBaseline = rov.position;
            modelLocalPosition = model.localPosition;
            modelLocalRotation = model.localRotation;
            modelLocalScale = model.localScale;
            rudderLocalPosition = rudder.localPosition;
            rudderLocalRotation = rudder.localRotation;
            rudderLocalScale = rudder.localScale;
            waterPosition = water.position;
            waterRotation = water.rotation;
            waterScale = water.localScale;
            waterMaterial = waterRenderer.sharedMaterial;
            waterRelationY = origin.y - water.position.y;
            spinnerLocalPositions =
                spinners.Select(item => item.transform.localPosition).ToArray();
            spinnerInitialRotations =
                spinners.Select(item => item.transform.localRotation).ToArray();
            spinnerLocalScales =
                spinners.Select(item => item.transform.localScale).ToArray();
        }

        private static void ObserveCycle(double now)
        {
            Require(IsHealthy(),
                "USV Driver lost healthy PublicData ownership during cycle.");
            double sourceTime = CurrentSourceTime(now);
            Require(Mathf.Abs(usv.position.y - origin.y) <= 1e-4f,
                "USV root changed business Y during the diagnostic cycle.");
            Require(Vector3.Dot(usv.up, Vector3.up) > 0.9999f,
                "USV root acquired pitch or roll during the diagnostic cycle.");

            if (hasPreviousCyclePose)
            {
                double deltaTime = sourceTime - previousCycleTime;
                if (deltaTime > 1e-6 && deltaTime < 0.25)
                {
                    float distance = Vector3.Distance(
                        usv.position,
                        previousCyclePosition);
                    float angle = Quaternion.Angle(
                        usv.rotation,
                        previousCycleRotation);
                    Require(distance <= deltaTime * 1.0 + 0.005,
                        "USV root position jumped during continuous playback.");
                    Require(angle <= deltaTime * 180.0 + 2.0,
                        "USV yaw jumped during continuous playback.");
                }
            }
            previousCycleTime = sourceTime;
            previousCyclePosition = usv.position;
            previousCycleRotation = usv.rotation;
            hasPreviousCyclePose = true;

            Vector3 offset = usv.position - origin;
            if (sourceTime >= 0.10 && sourceTime <= 0.60)
            {
                Require(Near(usv.position, origin, 2e-3f) &&
                        Near(usv.rotation, zeroRotation, 1e-5f),
                    "Initial hold was not stable.");
                initialHoldSamples++;
            }
            if (sourceTime > 0.90 && sourceTime < 6.10)
            {
                firstTurnObserved |= offset.sqrMagnitude > 0.01f;
                firstXObserved |= Mathf.Abs(offset.x) > 0.10f;
                firstZObserved |= Mathf.Abs(offset.z) > 0.10f;
            }
            if (sourceTime >= 2.20 && sourceTime <= 2.35)
            {
                firstYawDegrees = BusinessYawDegrees();
                firstYawCaptured = true;
            }
            if (sourceTime >= 6.40 && sourceTime <= 7.00)
            {
                Require(Near(usv.position, origin, 2e-3f) &&
                        Near(usv.rotation, zeroRotation, 1e-5f),
                    "Middle hold was not stable after the first turn.");
                middleHoldSamples++;
                firstReturnObserved = true;
            }
            if (sourceTime > 7.40 && sourceTime < 12.60)
            {
                secondTurnObserved |= offset.sqrMagnitude > 0.01f;
                secondXObserved |= Mathf.Abs(offset.x) > 0.10f;
                secondZObserved |= Mathf.Abs(offset.z) > 0.10f;
            }
            if (sourceTime >= 8.70 && sourceTime <= 8.85)
            {
                secondYawDegrees = BusinessYawDegrees();
                secondYawCaptured = true;
            }
            if (sourceTime >= 13.00 && sourceTime <= 13.70)
            {
                Require(Near(usv.position, origin, 2e-3f) &&
                        Near(usv.rotation, zeroRotation, 1e-5f),
                    "Final hold was not stable after the second turn.");
                finalHoldSamples++;
                secondReturnObserved = true;
            }
            if (sourceTime >= 13.90 && sourceTime <= 14.30)
            {
                Require(Near(usv.position, origin, 2e-3f) &&
                        Near(usv.rotation, zeroRotation, 1e-5f),
                    "USV loop boundary changed position or rotation.");
                loopHoldSamples++;
                if (sourceTime > 14.10)
                {
                    loopObserved = true;
                }
            }

            ValidateProtectedObjects();
        }

        private static void ValidateCycle()
        {
            Require(errorCount == 0,
                "Runtime Console reported an error during cycle observation.");
            Require(initialHoldSamples >= 2 &&
                    middleHoldSamples >= 2 &&
                    finalHoldSamples >= 2 &&
                    loopHoldSamples >= 2,
                "One or more diagnostic hold windows were not observed.");
            Require(firstTurnObserved &&
                    firstXObserved &&
                    firstZObserved &&
                    firstReturnObserved,
                "First closed XZ turn was not fully observed.");
            Require(secondTurnObserved &&
                    secondXObserved &&
                    secondZObserved &&
                    secondReturnObserved,
                "Second closed XZ turn was not fully observed.");
            Require(firstYawCaptured &&
                    secondYawCaptured &&
                    Mathf.Abs(firstYawDegrees) > 5f &&
                    Mathf.Abs(secondYawDegrees) > 5f &&
                    Mathf.Sign(firstYawDegrees) != Mathf.Sign(secondYawDegrees),
                "Opposite continuous yaw directions were not observed.");
            Require(loopObserved,
                "The 14-to-0 loop boundary was not observed.");
            Require((auv.position - auvBaseline).sqrMagnitude > 1e-6f &&
                    (rov.position - rovBaseline).sqrMagnitude > 1e-6f,
                "AUV or ROV public motion stopped during USV observation.");
        }

        private static void ValidateStaleHold()
        {
            Require(host.SourceStatus == DataSourceStatus.Stopped,
                "USV source did not enter Stopped state.");
            Require(driver.LastFailureReason == RenderSampleFailureReason.Stale,
                "USV Driver did not report stale data.");
            Require(Near(usv.position, staleHoldPosition, 1e-5f) &&
                    Near(usv.rotation, staleHoldRotation, 1e-5f),
                "USV root changed while stale data was held.");
            Require(authority.PublicDataOwnsControl && !demo.DrivesUsv,
                "Demo wrote the root while stale PublicData authority was active.");
        }

        private static void ValidateRestart()
        {
            Require(host.TryGetActiveEpoch(out ulong recoveredEpoch) &&
                    recoveredEpoch != initialEpoch,
                "USV source epoch did not change after restart.");
            Require(IsHealthy(),
                "USV Driver did not recover after restart.");
            Require(Mathf.Abs(usv.position.y - origin.y) <= 1e-4f &&
                    Vector3.Dot(usv.up, Vector3.up) > 0.9999f,
                "Restarted USV pose violated fixed-Y or yaw-only presentation.");
        }

        private static void ValidateDisabledHold()
        {
            Require(!driver.enabled &&
                    !driver.OwnsControl &&
                    authority.PublicDataOwnsControl &&
                    !demo.DrivesUsv,
                "Disabled Driver ownership state is incorrect.");
            Require(Near(usv.position, disabledPosition, 1e-5f) &&
                    Near(usv.rotation, disabledRotation, 1e-5f),
                "USV root changed while the Driver was disabled.");
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
                "USV LocalDiagnostic motion did not continue through the Driver.");
            Require(usv.position.sqrMagnitude > 0.1f,
                "Authority switch reset USV to world origin.");
        }

        private static void ValidateFinal()
        {
            Require(errorCount == 0,
                "Captured " + errorCount + " runtime Console errors.");
            Require(IsHealthy(),
                "USV Driver did not finish healthy.");
            Require(spinners.All(item =>
                    item.enabled &&
                    Vector3.Dot(item.localAxis.normalized, Vector3.right) > 0.99999f),
                "USV Spinner enabled state or local +X axis changed.");
            ValidateProtectedObjects();
            ValidateSiblingVehicle(
                "AUV_Yellow_Underwater",
                "AUV_PublicPoseDriver",
                demo.DrivesAuv);
            ValidateSiblingVehicle(
                "ROV_Box_Seabed",
                "ROV_PublicPoseDriver",
                demo.DrivesRov);
        }

        private static void ValidateProtectedObjects()
        {
            Require(Near(model.localPosition, modelLocalPosition, 1e-6f) &&
                    Near(model.localRotation, modelLocalRotation, 1e-5f) &&
                    Near(model.localScale, modelLocalScale, 1e-6f),
                "USV imported model local Transform changed.");
            Require(Near(rudder.localPosition, rudderLocalPosition, 1e-6f) &&
                    Near(rudder.localRotation, rudderLocalRotation, 1e-5f) &&
                    Near(rudder.localScale, rudderLocalScale, 1e-6f),
                "USV main rudder local Transform changed.");
            Require(Near(water.position, waterPosition, 1e-6f) &&
                    Near(water.rotation, waterRotation, 1e-5f) &&
                    Near(water.localScale, waterScale, 1e-6f) &&
                    ReferenceEquals(waterRenderer.sharedMaterial, waterMaterial),
                "Static water Transform or material changed.");
            for (int index = 0; index < spinners.Length; index++)
            {
                Require(
                    Near(spinners[index].transform.localPosition,
                        spinnerLocalPositions[index], 1e-6f) &&
                    Near(spinners[index].transform.localScale,
                        spinnerLocalScales[index], 1e-6f),
                    "USV Spinner local position or scale changed.");
            }
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
                    siblingDriver.LastFailureReason ==
                    RenderSampleFailureReason.None &&
                    !demoDrives,
                rootName + " PublicData behavior regressed.");
        }

        private static bool IsHealthy()
        {
            return host.TryGetActiveEpoch(out ulong epoch) &&
                   authority.PublicDataOwnsControl &&
                   host.SourceMode == VehicleRuntimeSourceMode.LocalDiagnostic &&
                   driver.enabled &&
                   driver.OwnsControl &&
                   !demo.DrivesUsv &&
                   driver.HasFreshAppliedPose &&
                   driver.LastAppliedSourceEpoch == epoch &&
                   driver.LastFailureReason == RenderSampleFailureReason.None &&
                   (driver.LastSampleMode == RenderSampleMode.Exact ||
                    driver.LastSampleMode == RenderSampleMode.Interpolated ||
                    driver.LastSampleMode == RenderSampleMode.HeldLatest);
        }

        private static double CurrentSourceTime(double now)
        {
            return host.GetTargetSourceTimestamp(
                now,
                configuration.RenderDelaySeconds);
        }

        private static float BusinessYawDegrees()
        {
            Vector3 currentForward = (usv.rotation * Vector3.left).normalized;
            return Vector3.SignedAngle(
                baselineForward,
                currentForward,
                Vector3.up);
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

        private static void ResetObservations()
        {
            spinnerRotationObserved = false;
            initialHoldSamples = 0;
            middleHoldSamples = 0;
            finalHoldSamples = 0;
            loopHoldSamples = 0;
            firstTurnObserved = false;
            secondTurnObserved = false;
            firstXObserved = false;
            firstZObserved = false;
            secondXObserved = false;
            secondZObserved = false;
            firstYawCaptured = false;
            secondYawCaptured = false;
            firstYawDegrees = 0f;
            secondYawDegrees = 0f;
            firstReturnObserved = false;
            secondReturnObserved = false;
            loopObserved = false;
            hasPreviousCyclePose = false;
            previousCycleTime = 0.0;
            previousCyclePosition = Vector3.zero;
            previousCycleRotation = Quaternion.identity;
        }

        private static void Advance(Phase next, double now)
        {
            phase = next;
            phaseStartedAt = now;
        }

        private static string CaptureRuntimeState(string label)
        {
            ulong epoch = default;
            bool hasEpoch = host != null && host.TryGetActiveEpoch(out epoch);
            Vector3 currentOrigin = configuration == null
                ? Vector3.zero
                : configuration.TestOrigin;
            Vector3 position = usv == null ? Vector3.zero : usv.position;
            Quaternion rotation = usv == null ? Quaternion.identity : usv.rotation;
            return label +
                   " sourceMode=" + (host == null ? "unavailable" : host.SourceMode.ToString()) +
                   " sourceStatus=" + (host == null ? "unavailable" : host.SourceStatus.ToString()) +
                   " sourceEpoch=" + (hasEpoch ? epoch.ToString(CultureInfo.InvariantCulture) : "unavailable") +
                   " generatorKind=" + (configuration == null ? "unavailable" : configuration.GeneratorKind.ToString()) +
                   " rootPosition=" + Vector(position) +
                   " rootRotation=" + QuaternionText(rotation) +
                   " expectedOrigin=" + Vector(currentOrigin) +
                   " displacement=" +
                   Vector3.Distance(position, currentOrigin).ToString("R", CultureInfo.InvariantCulture) +
                   " sampleMode=" + (driver == null ? "unavailable" : driver.LastSampleMode.ToString()) +
                   " failure=" + (driver == null ? "unavailable" : driver.LastFailureReason.ToString()) +
                   " failureMessage=" + (driver == null ? "unavailable" : driver.LastFailureMessage) +
                   " driverOwns=" + (driver != null && driver.OwnsControl) +
                   " hasFreshAppliedPose=" + (driver != null && driver.HasFreshAppliedPose) +
                   " appliedEpoch=" + (driver == null ? 0UL : driver.LastAppliedSourceEpoch) +
                   " authorityPublic=" + (authority != null && authority.PublicDataOwnsControl) +
                   " demoDrivesUsv=" + (demo != null && demo.DrivesUsv);
        }

        private static void RestoreFormalSourceMode()
        {
            if (!fixtureEstablished || host == null)
            {
                return;
            }

            if (host.SourceMode != formalSourceMode)
            {
                host.ShutdownForDiagnostics();
                host.ConfigureSourceMode(formalSourceMode);
            }
            fixtureEstablished = false;
        }

        private static string Vector(Vector3 value)
        {
            return "(" + value.x.ToString("R", CultureInfo.InvariantCulture) +
                   "," + value.y.ToString("R", CultureInfo.InvariantCulture) +
                   "," + value.z.ToString("R", CultureInfo.InvariantCulture) + ")";
        }

        private static string QuaternionText(Quaternion value)
        {
            return "(" + value.x.ToString("R", CultureInfo.InvariantCulture) +
                   "," + value.y.ToString("R", CultureInfo.InvariantCulture) +
                   "," + value.z.ToString("R", CultureInfo.InvariantCulture) +
                   "," + value.w.ToString("R", CultureInfo.InvariantCulture) + ")";
        }

        private static void Finish()
        {
            bool passed = SessionState.GetBool(PassedKey, false);
            string detail = SessionState.GetString(DetailKey, "No result.");
            string outputDirectory = EvidenceDirectory();
            Directory.CreateDirectory(outputDirectory);
            string timestamp =
                DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
            string status = passed
                ? "M2B_USV_DIAGNOSTIC_PLAY_MODE_VALIDATION_PASS"
                : "M2B_USV_DIAGNOSTIC_PLAY_MODE_VALIDATION_FAIL";
            string formalMode =
                SessionState.GetString(FormalSourceModeKey, "not-captured");
            string fixtureMode =
                SessionState.GetString(FixtureSourceModeKey, "not-captured");
            string finalRuntimeState =
                SessionState.GetString(FinalRuntimeStateKey, "not-captured");
            string json =
                "{\n" +
                "  \"status\": \"" + status + "\",\n" +
                "  \"generatedAtIso8601\": \"" + Escape(timestamp) + "\",\n" +
                "  \"passed\": " + (passed ? "true" : "false") + ",\n" +
                "  \"detail\": \"" + Escape(detail) + "\",\n" +
                "  \"formalSourceMode\": \"" + Escape(formalMode) + "\",\n" +
                "  \"fixtureSourceMode\": \"" + Escape(fixtureMode) + "\",\n" +
                "  \"finalRuntimeState\": \"" + Escape(finalRuntimeState) + "\"\n" +
                "}\n";
            File.WriteAllText(
                Path.Combine(outputDirectory, "m2b_usv_playmode_report.json"),
                json,
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(outputDirectory, "m2b_usv_playmode_report.md"),
                "# M2-B USV Diagnostic Play Mode Verification\n\n" +
                "- Status: `" + status + "`\n" +
                "- Detail: " + detail + "\n" +
                "- Formal source mode: `" + formalMode + "`\n" +
                "- Fixture source mode: `" + fixtureMode + "`\n" +
                "- Final runtime state: `" + finalRuntimeState + "`\n",
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
            string configured =
                Environment.GetEnvironmentVariable("M2B_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.GetFullPath(configured);
            }
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(
                Path.Combine(projectRoot, "..", "..", "M2B_Validation"));
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
                .Where(item => string.Equals(
                    item.name,
                    name,
                    StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1,
                "Expected one descendant " + name + ".");
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
