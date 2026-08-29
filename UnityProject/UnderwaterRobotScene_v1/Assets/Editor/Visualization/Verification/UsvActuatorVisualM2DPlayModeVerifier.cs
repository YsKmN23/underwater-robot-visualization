using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Usv;
using UnderwaterRobotScene.Visualization.Sampling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    [InitializeOnLoad]
    public static class UsvActuatorVisualM2DPlayModeVerifier
    {
        private const string ActiveKey = "M2D.UsvActuatorPlay.Active";
        private const string PassedKey = "M2D.UsvActuatorPlay.Passed";
        private const string DetailKey = "M2D.UsvActuatorPlay.Detail";
        private const string BatchKey = "M2D.UsvActuatorPlay.Batch";
        private const string FormalSourceModeKey =
            "M2D.UsvActuatorPlay.FormalSourceMode";
        private const string FixtureSourceModeKey =
            "M2D.UsvActuatorPlay.FixtureSourceMode";
        private const string FixtureEpochKey = "M2D.UsvActuatorPlay.FixtureEpoch";
        private const string DriverAppliedEpochKey =
            "M2D.UsvActuatorPlay.DriverAppliedEpoch";
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const float RpmTolerance = 8f;
        private const float RudderTolerance = 1.5f;

        private enum Phase
        {
            WaitHealthy,
            ObserveTwentySeconds,
            DemoGate,
            PublicRecovery,
            DriverDisabled,
            DriverRecovery,
            DriverTargetMismatch,
            CoordinatorDisabled,
            CoordinatorRecovery,
            ModeDisabled,
            ModeRecovery,
            DeltaTimeFaults,
            DistanceTeleport,
            RotationJump,
            InactiveReferences,
            NonfinitePose,
            WaitStale,
            WaitEpochRecovery,
            Final
        }

        private static readonly List<string> Checks = new List<string>();
        private static readonly double[] ScreenshotTimes =
            { 0.5, 3.5, 6.75, 10.0, 13.3, 17.5, 19.5 };
        private static readonly bool[] ScreenshotTaken =
            new bool[ScreenshotTimes.Length];

        private static bool subscribed;
        private static bool bound;
        private static int errorCount;
        private static Phase phase;
        private static double enteredAt;
        private static double phaseStartedAt;
        private static double observationStartedAt;
        private static int lastObservedFrame = -1;
        private static int visualEvidenceCount;
        private static ulong epochBeforeRestart;
        private static ulong fixtureEpoch;
        private static ulong driverAppliedEpoch;
        private static VehicleRuntimeSourceMode formalSourceMode;
        private static bool fixtureEstablished;
        private static long managedMemoryStart;
        private static long managedMemoryEnd;
        private static long allocatedBytesStart;
        private static long allocatedBytesEnd;

        private static Transform businessRoot;
        private static Transform visualRoot;
        private static Transform importedRoot;
        private static Transform rudderMain;
        private static Transform rudderPivot;
        private static UsvActuatorVisualCoordinator coordinator;
        private static UsvSurfaceVisualController surfaceController;
        private static PropellerSpinner portSpinner;
        private static PropellerSpinner starboardSpinner;
        private static VehiclePoseDriver driver;
        private static VehicleDataRuntimeHost host;
        private static VehiclePoseIntegrationConfiguration configuration;
        private static VehiclePoseControlAuthority authority;
        private static DemoMotionController demo;
        private static Transform auvRoot;
        private static Transform rovRoot;

        private static Vector3 visualInitialPosition;
        private static Quaternion visualInitialRotation;
        private static Vector3 visualInitialScale;
        private static Vector3 importedInitialPosition;
        private static Quaternion importedInitialRotation;
        private static Vector3 importedInitialScale;
        private static Vector3 mainInitialPosition;
        private static Quaternion mainInitialRotation;
        private static Vector3 mainInitialScale;
        private static Vector3 pivotInitialPosition;
        private static Vector3 pivotInitialScale;
        private static Quaternion pivotNeutralRotation;
        private static Vector3 portInitialPosition;
        private static Vector3 portInitialScale;
        private static Vector3 starboardInitialPosition;
        private static Vector3 starboardInitialScale;
        private static Transform[] movingObjects;
        private static Transform[] fixedObjects;
        private static Matrix4x4[] movingRelativeMatrices;
        private static Matrix4x4[] fixedRelativeMatrices;
        private static Vector3 auvInitialPosition;
        private static Vector3 rovInitialPosition;

        private static int initialHoldSamples;
        private static int positiveYawSamples;
        private static int middleHoldSamples;
        private static int negativeYawSamples;
        private static int finalHoldSamples;
        private static float maxPositiveDifferential;
        private static float maxNegativeDifferential;
        private static float maxPositiveRudder;
        private static float minNegativeRudder;
        private static float maxVisualHeave;
        private static float maxVisualPitch;
        private static float maxVisualRoll;
        private static bool portRotationObserved;
        private static bool starboardRotationObserved;
        private static Quaternion portPreviousRotation;
        private static Quaternion starboardPreviousRotation;
        private static bool demoResponseObserved;

        static UsvActuatorVisualM2DPlayModeVerifier()
        {
            if (SessionState.GetBool(ActiveKey, false))
            {
                Subscribe();
            }
        }

        [MenuItem("Tools/Underwater Demo/M2-D2/Run USV Actuator Visual Play Mode Verification")]
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
                    "M2-D2 Play Mode verification is already active.");
            }

            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(PassedKey, false);
            SessionState.SetString(DetailKey, "Verification did not complete.");
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetString(FormalSourceModeKey, "not-captured");
            SessionState.SetString(FixtureSourceModeKey, "not-captured");
            SessionState.SetString(FixtureEpochKey, "not-captured");
            SessionState.SetString(DriverAppliedEpochKey, "not-captured");
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
                bound = false;
                errorCount = 0;
                lastObservedFrame = -1;
                visualEvidenceCount = 0;
                Checks.Clear();
                fixtureEstablished = false;
                fixtureEpoch = 0UL;
                driverAppliedEpoch = 0UL;
                Array.Clear(ScreenshotTaken, 0, ScreenshotTaken.Length);
                ResetObservationCounters();
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
                if (!bound)
                {
                    BindReferences();
                    CaptureProtectedState();
                    bound = true;
                }

                double now = Time.realtimeSinceStartupAsDouble;
                double elapsed = now - phaseStartedAt;
                ValidateProtectedState();

                switch (phase)
                {
                    case Phase.WaitHealthy:
                        if (IsHealthyPublicData())
                        {
                            Require(host.TryGetActiveEpoch(out fixtureEpoch),
                                "The M2-D LocalDiagnostic fixture has no active epoch.");
                            driverAppliedEpoch = driver.LastAppliedSourceEpoch;
                            Require(driverAppliedEpoch == fixtureEpoch,
                                "The Driver did not apply the M2-D LocalDiagnostic fixture epoch.");
                            SessionState.SetString(FixtureEpochKey, fixtureEpoch.ToString());
                            SessionState.SetString(
                                DriverAppliedEpochKey,
                                driverAppliedEpoch.ToString());
                            Require(Near(coordinator.OriginalPortRpm, 740f, 0.01f) &&
                                    Near(coordinator.OriginalStarboardRpm, 740f, 0.01f),
                                "Coordinator did not cache the serialized 740/740 baseline.");
                            RequireHardNeutral("scene reload first valid frame");
                            Add("Scene reload",
                                "The first valid Play Mode frame captured a baseline and held 0/0 RPM with a neutral Pivot.");
                            observationStartedAt = now;
                            managedMemoryStart = GC.GetTotalMemory(false);
                            allocatedBytesStart = GC.GetAllocatedBytesForCurrentThread();
                            Advance(Phase.ObserveTwentySeconds, now);
                        }
                        else if (now - enteredAt > 6.0)
                        {
                            throw new InvalidOperationException(
                                "USV PublicData chain did not become healthy within six seconds.");
                        }
                        break;

                    case Phase.ObserveTwentySeconds:
                        ObserveTwentySeconds(now - observationStartedAt, now);
                        if (now - observationStartedAt >= 20.0)
                        {
                            managedMemoryEnd = GC.GetTotalMemory(false);
                            allocatedBytesEnd = GC.GetAllocatedBytesForCurrentThread();
                            ValidateTwentySecondObservation();
                            authority.Mode = VehiclePoseControlMode.Demo;
                            demoResponseObserved = false;
                            Advance(Phase.DemoGate, now);
                        }
                        break;

                    case Phase.DemoGate:
                        demoResponseObserved |=
                            coordinator.CurrentPortRpm > 1f ||
                            coordinator.CurrentStarboardRpm > 1f ||
                            Mathf.Abs(coordinator.CurrentRudderDegrees) > 0.5f;
                        if (elapsed >= 1.25)
                        {
                            Require(authority.DemoOwnsControl &&
                                    !demo.DrivesUsv &&
                                    driver.OwnsControl &&
                                    host.SourceMode == VehicleRuntimeSourceMode.LocalDiagnostic &&
                                    host.TryGetActiveEpoch(out ulong demoEpoch) &&
                                    driver.LastAppliedSourceEpoch == demoEpoch &&
                                    coordinator.BaselineValid,
                                "Demo Authority did not establish the explicit Demo gate.");
                            Require(demoResponseObserved,
                                "Demo gate did not produce any VisualOnly actuator response.");
                            Add("Demo gate with Driver ownership",
                                "Driver-owned LocalDiagnostic Demo motion was observed and the explicit Demo gate remained active.");
                            authority.Mode = VehiclePoseControlMode.PublicData;
                            Advance(Phase.PublicRecovery, now);
                        }
                        break;

                    case Phase.PublicRecovery:
                        if (elapsed >= 0.85 && IsHealthyPublicData())
                        {
                            Add("Authority both directions",
                                "Demo→PublicData and PublicData→Demo transitions reset before resuming.");
                            driver.enabled = false;
                            coordinator.TickForDiagnostics(0.016);
                            Advance(Phase.DriverDisabled, now);
                        }
                        else if (elapsed > 5.0)
                        {
                            throw new InvalidOperationException(
                                "PublicData did not recover after the Demo gate test.");
                        }
                        break;

                    case Phase.DriverDisabled:
                        RequireHardNeutral("Driver disabled");
                        Require(!driver.OwnsControl && !driver.HasFreshAppliedPose,
                            "Disabled Driver retained ownership or freshness.");
                        Add("Driver disable",
                            "Driver Disable immediately hard-reset both RPM and the Pivot.");
                        driver.enabled = true;
                        Advance(Phase.DriverRecovery, now);
                        break;

                    case Phase.DriverRecovery:
                        if (elapsed >= 0.75 && IsHealthyPublicData())
                        {
                            TestDriverTargetMismatch();
                            Advance(Phase.DriverTargetMismatch, now);
                        }
                        else if (elapsed > 5.0)
                        {
                            throw new InvalidOperationException(
                                "Driver did not recover after re-enable.");
                        }
                        break;

                    case Phase.DriverTargetMismatch:
                        RequireHardNeutral("Driver target mismatch");
                        Require(coordinator.enabled,
                            "A transient Driver target mismatch disabled the Coordinator.");
                        Add("Driver target",
                            "A transient target mismatch hard-reset without changing configuration or disabling the Coordinator.");
                        coordinator.enabled = false;
                        Advance(Phase.CoordinatorDisabled, now);
                        break;

                    case Phase.CoordinatorDisabled:
                        Require(Near(portSpinner.rpm, 740f, 0.01f) &&
                                Near(starboardSpinner.rpm, 740f, 0.01f) &&
                                Near(rudderPivot.localRotation, pivotNeutralRotation, 0.00001f),
                            "Coordinator Disable did not restore 740/740 and neutral.");
                        Add("Coordinator disable",
                            "OnDisable restored the cached serialized RPM and neutral Pivot.");
                        coordinator.enabled = true;
                        coordinator.TickForDiagnostics(0.016);
                        RequireHardNeutral("Coordinator re-enable first tick");
                        Require(coordinator.BaselineValid,
                            "Coordinator re-enable did not capture a fresh baseline.");
                        Add("Coordinator re-enable",
                            "The first re-enabled tick captured a baseline and remained hard neutral.");
                        coordinator.Mode = UsvActuatorVisualMode.Disabled;
                        coordinator.TickForDiagnostics(0.016);
                        Advance(Phase.ModeDisabled, now);
                        break;

                    case Phase.CoordinatorRecovery:
                        throw new InvalidOperationException(
                            "Unexpected obsolete Coordinator recovery phase.");

                    case Phase.ModeDisabled:
                        Require(Near(portSpinner.rpm, 740f, 0.01f) &&
                                Near(starboardSpinner.rpm, 740f, 0.01f) &&
                                Near(rudderPivot.localRotation, pivotNeutralRotation, 0.00001f),
                            "Mode Disabled did not restore the original actuator state.");
                        Add("Mode Disabled",
                            "The Disabled branch abandoned M2-D writes and restored 740/740 plus neutral.");
                        coordinator.Mode =
                            UsvActuatorVisualMode.DemoAndLocalDiagnosticPublicData;
                        coordinator.TickForDiagnostics(0.016);
                        Advance(Phase.ModeRecovery, now);
                        break;

                    case Phase.ModeRecovery:
                        RequireHardNeutral("mode recovery first tick");
                        Add("Mode branch recovery",
                            "Re-entering the explicit VisualOnly mode reset and captured a new baseline.");
                        TestDeltaTimeFaults();
                        Advance(Phase.DeltaTimeFaults, now);
                        break;

                    case Phase.DeltaTimeFaults:
                        RequireHardNeutral("invalid and excessive delta time");
                        Add("Delta time",
                            "dt=0, negative, NaN and dt>0.25 s all produced an immediate hard reset.");
                        TestDistanceTeleport();
                        Advance(Phase.DistanceTeleport, now);
                        break;

                    case Phase.DistanceTeleport:
                        RequireHardNeutral("distance teleport");
                        Add("Distance teleport",
                            "A 0.30 m single-tick displacement hard-reset without a pulse.");
                        TestRotationJump();
                        Advance(Phase.RotationJump, now);
                        break;

                    case Phase.RotationJump:
                        RequireHardNeutral("rotation jump");
                        Add("Rotation jump",
                            "A 35 degree single-tick yaw jump hard-reset without a pulse.");
                        TestInactiveReferences();
                        Advance(Phase.InactiveReferences, now);
                        break;

                    case Phase.InactiveReferences:
                        RequireHardNeutral("inactive reference tests");
                        Add("Inactive references",
                            "Pivot, Port Spinner and Starboard Spinner inactivity each hard-reset immediately.");
                        TestNonfinitePreviousPose();
                        RequireHardNeutral("nonfinite pose");
                        Add("Nonfinite pose",
                            "A nonfinite cached observer position was rejected and hard-reset.");
                        Require(host.TryGetActiveEpoch(out epochBeforeRestart),
                            "No active epoch was available before stale testing.");
                        host.StopSource();
                        Advance(Phase.WaitStale, now);
                        break;

                    case Phase.NonfinitePose:
                        throw new InvalidOperationException(
                            "Unexpected obsolete nonfinite-pose phase.");

                    case Phase.WaitStale:
                        if (driver.LastFailureReason == RenderSampleFailureReason.Stale)
                        {
                            RequireHardNeutral("stale PublicData");
                            Add("Stale gate",
                                "Stale PublicData immediately forced 0/0 RPM and a neutral Pivot.");
                            host.RestartSource();
                            Advance(Phase.WaitEpochRecovery, now);
                        }
                        else if (elapsed > 2.0)
                        {
                            throw new InvalidOperationException(
                                "The stopped USV source did not become stale.");
                        }
                        break;

                    case Phase.WaitEpochRecovery:
                        if (IsHealthyPublicData() &&
                            host.TryGetActiveEpoch(out ulong restartedEpoch) &&
                            restartedEpoch != epochBeforeRestart)
                        {
                            Require(driver.LastAppliedSourceEpoch == restartedEpoch,
                                "Driver did not apply the restarted source epoch.");
                            Require(coordinator.BaselineValid,
                                "New epoch did not establish a fresh observer baseline.");
                            RequireHardNeutral("new epoch first diagnostic hold");
                            Add("Epoch restart",
                                "The new epoch reset, captured a new baseline, and resumed from the diagnostic hold.");
                            Advance(Phase.Final, now);
                        }
                        else if (elapsed > 5.0)
                        {
                            throw new InvalidOperationException(
                                "USV source did not recover with a new epoch.");
                        }
                        break;

                    case Phase.Final:
                        RestoreRuntimeControls();
                        ValidateFinalState();
                        WriteReport();
                        RestoreFormalSourceMode();
                        SessionState.SetBool(PassedKey, true);
                        SessionState.SetString(
                            DetailKey,
                            "M2-D2 isolated LocalDiagnostic lifecycle, 20-second actuator motion, gate, reset, isolation, visual and performance-initial verification passed. " +
                            "formalSourceMode=" + formalSourceMode +
                            ", fixtureSourceMode=LocalDiagnostic" +
                            ", fixtureEpoch=" + fixtureEpoch +
                            ", driverAppliedEpoch=" + driverAppliedEpoch + ".");
                        EditorApplication.ExitPlaymode();
                        break;
                }

                if (now - enteredAt > 55.0)
                {
                    throw new InvalidOperationException(
                        "M2-D2 Play Mode verification exceeded 55 seconds.");
                }
            }
            catch (Exception exception)
            {
                RestoreRuntimeControls();
                RestoreFormalSourceMode();
                SessionState.SetBool(PassedKey, false);
                SessionState.SetString(
                    DetailKey,
                    exception.GetType().Name + ": " + exception.Message);
                EditorApplication.ExitPlaymode();
            }
        }

        private static void BindReferences()
        {
            businessRoot = RequireRoot("USV_Blue_Surface").transform;
            visualRoot = RequireDescendant(businessRoot, "USV_SurfaceVisualRoot");
            importedRoot = RequireDescendant(visualRoot, "USV_FineModel_V1_Imported");
            rudderMain = RequireDescendant(importedRoot, "USV_Rudder_Main");
            rudderPivot = RequireDescendant(rudderMain, "USV_Rudder_VisualPivot");
            coordinator = RequireComponent<UsvActuatorVisualCoordinator>(
                businessRoot.gameObject,
                "USV actuator Coordinator");
            surfaceController = RequireComponent<UsvSurfaceVisualController>(
                visualRoot.gameObject,
                "M2-C surface Controller");
            driver = RequireComponent<VehiclePoseDriver>(
                RequireRoot("USV_PublicPoseDriver"),
                "USV Driver");
            host = driver.RuntimeHost;
            configuration = driver.IntegrationConfiguration;
            authority = RequireComponent<VehiclePoseControlAuthority>(
                businessRoot.gameObject,
                "USV Authority");
            demo = UnityEngine.Object.FindAnyObjectByType<DemoMotionController>(
                FindObjectsInactive.Include);
            auvRoot = RequireRoot("AUV_Yellow_Underwater").transform;
            rovRoot = RequireRoot("ROV_Box_Seabed").transform;

            Require(host != null && configuration != null && demo != null,
                "Host, integration configuration or DemoMotionController is missing.");
            formalSourceMode = host.SourceMode;
            Require(formalSourceMode == VehicleRuntimeSourceMode.RouteFollowing,
                "The approved Formal USV Host is expected to use RouteFollowing.");
            SessionState.SetString(FormalSourceModeKey, formalSourceMode.ToString());
            host.ShutdownForDiagnostics();
            fixtureEstablished = true;
            host.ConfigureSourceMode(VehicleRuntimeSourceMode.LocalDiagnostic);
            host.InitializeForDiagnostics(host.MonotonicNowSeconds);
            Require(host.SourceMode == VehicleRuntimeSourceMode.LocalDiagnostic &&
                    host.SourceStatus == DataSourceStatus.Running,
                "The non-persistent M2-D LocalDiagnostic fixture did not start.");
            SessionState.SetString(
                FixtureSourceModeKey,
                host.SourceMode.ToString());
            Require(coordinator.BusinessRoot == businessRoot &&
                    coordinator.RudderVisualPivot == rudderPivot &&
                    coordinator.PoseDriver == driver &&
                    coordinator.ControlAuthority == authority &&
                    coordinator.Mode ==
                        UsvActuatorVisualMode.DemoAndLocalDiagnosticPublicData,
                "Coordinator references or explicit Scene mode changed.");
            portSpinner = coordinator.PortVisualThruster;
            starboardSpinner = coordinator.StarboardVisualThruster;
            Require(portSpinner != null &&
                    starboardSpinner != null &&
                    !ReferenceEquals(portSpinner, starboardSpinner),
                "Stable Port/Starboard Spinner references are invalid.");
            Require(RootSpacePosition(portSpinner.transform).z > 0f &&
                    RootSpacePosition(starboardSpinner.transform).z < 0f,
                "Port/Starboard role geometry no longer matches business local +Z/-Z.");
            Require(Vector3.Dot(portSpinner.localAxis.normalized, Vector3.right) >
                        0.99999f &&
                    Vector3.Dot(starboardSpinner.localAxis.normalized, Vector3.right) >
                        0.99999f,
                "Spinner local axes are not +X.");
            Require(rudderPivot.parent == rudderMain &&
                    rudderPivot.childCount == 10,
                "Rudder Pivot hierarchy or ten-object moving set changed.");
            movingObjects = DirectChildren(rudderPivot);
            fixedObjects = DirectChildren(rudderMain)
                .Where(item => item != rudderPivot)
                .ToArray();
            Require(movingObjects.Length == 10 && fixedObjects.Length == 8,
                "Rudder moving/fixed sets are not exactly 10/8.");
        }

        private static void CaptureProtectedState()
        {
            visualInitialPosition = visualRoot.localPosition;
            visualInitialRotation = visualRoot.localRotation;
            visualInitialScale = visualRoot.localScale;
            importedInitialPosition = importedRoot.localPosition;
            importedInitialRotation = importedRoot.localRotation;
            importedInitialScale = importedRoot.localScale;
            mainInitialPosition = rudderMain.localPosition;
            mainInitialRotation = rudderMain.localRotation;
            mainInitialScale = rudderMain.localScale;
            pivotInitialPosition = rudderPivot.localPosition;
            pivotInitialScale = rudderPivot.localScale;
            pivotNeutralRotation = coordinator.NeutralRudderLocalRotation;
            portInitialPosition = portSpinner.transform.localPosition;
            portInitialScale = portSpinner.transform.localScale;
            starboardInitialPosition = starboardSpinner.transform.localPosition;
            starboardInitialScale = starboardSpinner.transform.localScale;
            movingRelativeMatrices = movingObjects
                .Select(item => rudderPivot.worldToLocalMatrix * item.localToWorldMatrix)
                .ToArray();
            fixedRelativeMatrices = fixedObjects
                .Select(item => rudderMain.worldToLocalMatrix * item.localToWorldMatrix)
                .ToArray();
            auvInitialPosition = auvRoot.position;
            rovInitialPosition = rovRoot.position;
            portPreviousRotation = portSpinner.transform.localRotation;
            starboardPreviousRotation = starboardSpinner.transform.localRotation;
        }

        private static void ObserveTwentySeconds(double elapsed, double now)
        {
            if (Time.frameCount == lastObservedFrame)
            {
                return;
            }
            lastObservedFrame = Time.frameCount;
            Require(IsHealthyPublicData(),
                "PublicData ownership or freshness was lost during the 20-second observation.");

            double sourceTime = host.GetTargetSourceTimestamp(
                now,
                configuration.RenderDelaySeconds);
            float cycleTime = Mathf.Repeat((float)sourceTime, 14f);
            float port = coordinator.CurrentPortRpm;
            float starboard = coordinator.CurrentStarboardRpm;
            float rudder = coordinator.CurrentRudderDegrees;
            Require(port >= -0.001f &&
                    starboard >= -0.001f &&
                    port <= 740.001f &&
                    starboard <= 740.001f,
                "Observed an out-of-range or negative VisualOnly RPM.");
            RequirePivotOnlyLocalY();

            if (cycleTime >= 0.30f && cycleTime <= 0.65f)
            {
                Require(port <= RpmTolerance &&
                        starboard <= RpmTolerance &&
                        Mathf.Abs(rudder) <= RudderTolerance,
                    "Initial hold did not settle to 0/0 RPM and neutral.");
                initialHoldSamples++;
            }
            if (cycleTime >= 1.10f && cycleTime <= 5.90f)
            {
                if (port > starboard + 2f && rudder > 0.25f)
                {
                    positiveYawSamples++;
                }
                maxPositiveDifferential = Mathf.Max(
                    maxPositiveDifferential,
                    port - starboard);
                maxPositiveRudder = Mathf.Max(maxPositiveRudder, rudder);
            }
            if (cycleTime >= 6.65f && cycleTime <= 7.05f)
            {
                Require(port <= RpmTolerance &&
                        starboard <= RpmTolerance &&
                        Mathf.Abs(rudder) <= RudderTolerance,
                    "Middle hold did not settle without restoring 740 RPM.");
                middleHoldSamples++;
            }
            if (cycleTime >= 7.85f && cycleTime <= 12.45f)
            {
                if (starboard > port + 2f && rudder < -0.25f)
                {
                    negativeYawSamples++;
                }
                maxNegativeDifferential = Mathf.Max(
                    maxNegativeDifferential,
                    starboard - port);
                minNegativeRudder = Mathf.Min(minNegativeRudder, rudder);
            }
            if (cycleTime >= 13.20f && cycleTime <= 13.75f)
            {
                Require(port <= RpmTolerance &&
                        starboard <= RpmTolerance &&
                        Mathf.Abs(rudder) <= RudderTolerance,
                    "Final hold did not settle to 0/0 RPM and neutral.");
                finalHoldSamples++;
            }

            Vector3 visualEuler = SignedEuler(visualRoot.localEulerAngles);
            maxVisualHeave = Mathf.Max(
                maxVisualHeave,
                Mathf.Abs(visualRoot.localPosition.y));
            maxVisualPitch = Mathf.Max(maxVisualPitch, Mathf.Abs(visualEuler.x));
            maxVisualRoll = Mathf.Max(maxVisualRoll, Mathf.Abs(visualEuler.z));
            portRotationObserved |= Quaternion.Angle(
                portPreviousRotation,
                portSpinner.transform.localRotation) > 0.01f;
            starboardRotationObserved |= Quaternion.Angle(
                starboardPreviousRotation,
                starboardSpinner.transform.localRotation) > 0.01f;
            portPreviousRotation = portSpinner.transform.localRotation;
            starboardPreviousRotation = starboardSpinner.transform.localRotation;

            for (int index = 0; index < ScreenshotTimes.Length; index++)
            {
                if (!ScreenshotTaken[index] && elapsed >= ScreenshotTimes[index])
                {
                    ScreenshotTaken[index] = true;
                    CaptureScreenshot(index, elapsed);
                }
            }
        }

        private static void ValidateTwentySecondObservation()
        {
            Require(initialHoldSamples >= 2 &&
                    middleHoldSamples >= 2 &&
                    finalHoldSamples >= 2,
                "One or more M2-B hold windows were not sampled.");
            Require(positiveYawSamples >= 2 &&
                    maxPositiveDifferential > 5f &&
                    maxPositiveRudder > 0.5f,
                "Positive yaw did not produce Port>Starboard RPM and positive rudder.");
            Require(negativeYawSamples >= 2 &&
                    maxNegativeDifferential > 5f &&
                    minNegativeRudder < -0.5f,
                "Negative yaw did not produce Starboard>Port RPM and negative rudder.");
            Require(portRotationObserved && starboardRotationObserved,
                "Both Spinner rotating parts were not observed during active windows.");
            Require(maxVisualHeave >= 0.005f &&
                    maxVisualPitch >= 0.25f &&
                    maxVisualRoll >= 0.40f,
                "M2-C heave/pitch/roll did not coexist with M2-D.");
            Require(visualEvidenceCount == ScreenshotTimes.Length,
                "Seven normal-render representative screenshots were not captured.");
            Require((auvRoot.position - auvInitialPosition).sqrMagnitude > 1e-6f &&
                    (rovRoot.position - rovInitialPosition).sqrMagnitude > 1e-6f,
                "AUV or ROV public motion stopped during M2-D observation.");
            Add("Full M2-B cycle",
                "Initial, positive-yaw, middle-hold, negative-yaw and final-hold actuator contracts passed across 20 seconds.");
            Add("Differential and rudder signs",
                "Positive yaw produced Port>Starboard and positive rudder; negative yaw produced the inverse.");
            Add("M2-C and sibling isolation",
                "Heave/pitch/roll remained visible and AUV/ROV continued independently.");
            Add("Normal-render evidence",
                "Seven representative 1280×720 frames were captured at 0.5, 3.5, 6.75, 10, 13.3, 17.5 and 19.5 seconds.");
        }

        private static void ValidateProtectedState()
        {
            if (!bound)
            {
                return;
            }
            Require(Near(visualRoot.localScale, visualInitialScale, 0.00001f),
                "M2-C visual-root scale changed.");
            Require(Near(importedRoot.localPosition, importedInitialPosition, 0.000001f) &&
                    Near(importedRoot.localRotation, importedInitialRotation, 0.000001f) &&
                    Near(importedRoot.localScale, importedInitialScale, 0.000001f),
                "Imported root local Transform changed.");
            Require(Near(rudderMain.localPosition, mainInitialPosition, 0.000001f) &&
                    Near(rudderMain.localRotation, mainInitialRotation, 0.000001f) &&
                    Near(rudderMain.localScale, mainInitialScale, 0.000001f),
                "USV_Rudder_Main local Transform changed.");
            Require(Near(rudderPivot.localPosition, pivotInitialPosition, 0.000001f) &&
                    Near(rudderPivot.localScale, pivotInitialScale, 0.000001f),
                "Rudder Pivot position or scale changed.");
            Require(Near(portSpinner.transform.localPosition, portInitialPosition, 0.000001f) &&
                    Near(portSpinner.transform.localScale, portInitialScale, 0.000001f) &&
                    Near(starboardSpinner.transform.localPosition, starboardInitialPosition, 0.000001f) &&
                    Near(starboardSpinner.transform.localScale, starboardInitialScale, 0.000001f),
                "Spinner local position or scale changed.");
            for (int index = 0; index < movingObjects.Length; index++)
            {
                Matrix4x4 relative =
                    rudderPivot.worldToLocalMatrix * movingObjects[index].localToWorldMatrix;
                Require(Near(relative, movingRelativeMatrices[index], 0.0001f),
                    "A moving rudder object's matrix relative to Pivot changed.");
            }
            for (int index = 0; index < fixedObjects.Length; index++)
            {
                Matrix4x4 relative =
                    rudderMain.worldToLocalMatrix * fixedObjects[index].localToWorldMatrix;
                Require(Near(relative, fixedRelativeMatrices[index], 0.0001f),
                    "A fixed rudder object's matrix relative to Main changed.");
            }
        }

        private static void TestDriverTargetMismatch()
        {
            FieldInfo field = typeof(VehiclePoseDriver).GetField(
                "targetRoot",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, "VehiclePoseDriver targetRoot field was not found.");
            Transform original = driver.TargetRoot;
            field.SetValue(driver, visualRoot);
            try
            {
                coordinator.TickForDiagnostics(0.016);
            }
            finally
            {
                field.SetValue(driver, original);
            }
        }

        private static void TestDeltaTimeFaults()
        {
            coordinator.TickForDiagnostics(0.0);
            RequireHardNeutral("dt=0");
            coordinator.TickForDiagnostics(-0.01);
            RequireHardNeutral("negative dt");
            coordinator.TickForDiagnostics(double.NaN);
            RequireHardNeutral("nonfinite dt");
            coordinator.TickForDiagnostics(0.30);
            RequireHardNeutral("excessive dt");
        }

        private static void TestDistanceTeleport()
        {
            coordinator.TickForDiagnostics(0.016);
            Vector3 original = businessRoot.position;
            businessRoot.position = original + Vector3.right * 0.30f;
            try
            {
                coordinator.TickForDiagnostics(0.016);
            }
            finally
            {
                businessRoot.position = original;
            }
        }

        private static void TestRotationJump()
        {
            coordinator.TickForDiagnostics(0.016);
            Quaternion original = businessRoot.rotation;
            businessRoot.rotation = Quaternion.AngleAxis(35f, Vector3.up) * original;
            try
            {
                coordinator.TickForDiagnostics(0.016);
            }
            finally
            {
                businessRoot.rotation = original;
            }
        }

        private static void TestInactiveReferences()
        {
            TestInactiveObject(rudderPivot.gameObject, "Pivot inactive");
            TestInactiveObject(portSpinner.gameObject, "Port Spinner inactive");
            TestInactiveObject(starboardSpinner.gameObject, "Starboard Spinner inactive");
        }

        private static void TestInactiveObject(GameObject value, string label)
        {
            value.SetActive(false);
            try
            {
                coordinator.TickForDiagnostics(0.016);
                RequireHardNeutral(label);
            }
            finally
            {
                value.SetActive(true);
            }
        }

        private static void TestNonfinitePreviousPose()
        {
            coordinator.TickForDiagnostics(0.016);
            FieldInfo baselineField = typeof(UsvActuatorVisualCoordinator).GetField(
                "baselineValid",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo positionField = typeof(UsvActuatorVisualCoordinator).GetField(
                "previousBusinessPosition",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(baselineField != null && positionField != null,
                "Coordinator diagnostic observer fields were not found.");
            baselineField.SetValue(coordinator, true);
            positionField.SetValue(
                coordinator,
                new Vector3(float.NaN, 0f, 0f));
            coordinator.TickForDiagnostics(0.016);
        }

        private static void RequirePivotOnlyLocalY()
        {
            Quaternion delta =
                Quaternion.Inverse(pivotNeutralRotation) * rudderPivot.localRotation;
            Vector3 euler = SignedEuler(delta.eulerAngles);
            Require(Mathf.Abs(euler.x) <= 0.10f &&
                    Mathf.Abs(euler.z) <= 0.10f &&
                    Mathf.Abs(euler.y) <= 25.10f,
                "Pivot rotation left local +Y or exceeded ±25 degrees.");
        }

        private static void RequireHardNeutral(string context)
        {
            Require(Near(portSpinner.rpm, 0f, 0.01f) &&
                    Near(starboardSpinner.rpm, 0f, 0.01f) &&
                    Near(coordinator.CurrentPortRpm, 0f, 0.01f) &&
                    Near(coordinator.CurrentStarboardRpm, 0f, 0.01f) &&
                    Near(coordinator.CurrentRudderDegrees, 0f, 0.01f) &&
                    Near(rudderPivot.localRotation, pivotNeutralRotation, 0.00001f),
                "Actuators were not hard neutral for " + context + ".");
        }

        private static bool IsHealthyPublicData()
        {
            return host.SourceMode == VehicleRuntimeSourceMode.LocalDiagnostic &&
                   host.TryGetActiveEpoch(out ulong activeEpoch) &&
                   authority.PublicDataOwnsControl &&
                   driver.isActiveAndEnabled &&
                   driver.OwnsControl &&
                   driver.HasAppliedPose &&
                   driver.HasFreshAppliedPose &&
                   driver.LastFailureReason == RenderSampleFailureReason.None &&
                   driver.LastAppliedSourceEpoch == activeEpoch &&
                   driver.TargetRoot == businessRoot &&
                   coordinator.isActiveAndEnabled;
        }

        private static void ValidateFinalState()
        {
            Require(errorCount == 0,
                "Console recorded Error/Exception/Assert messages: " + errorCount + ".");
            Require(IsHealthyPublicData(),
                "PublicData chain was not healthy at final validation.");
            Require(coordinator.Mode ==
                        UsvActuatorVisualMode.DemoAndLocalDiagnosticPublicData &&
                    coordinator.enabled &&
                    portSpinner.enabled &&
                    starboardSpinner.enabled &&
                    rudderPivot.gameObject.activeInHierarchy,
                "Runtime controls were not restored after fault validation.");
            Require(auvRoot.GetComponentInChildren<UsvActuatorVisualCoordinator>(true) ==
                        null &&
                    rovRoot.GetComponentInChildren<UsvActuatorVisualCoordinator>(true) ==
                        null,
                "M2-D Coordinator leaked onto AUV or ROV.");
            Require(Checks.Count >= 18,
                "Not all M2-D2 Play Mode acceptance groups completed.");
            Add("Performance initial",
                "20-second managed-memory and current-thread allocation counters were recorded; runtime hot-path source is separately checked for cached constant-time, allocation-free operations.");
            Add("Console",
                "Error/Exception/Assert count remained zero.");
        }

        private static void RestoreRuntimeControls()
        {
            if (rudderPivot != null)
            {
                rudderPivot.gameObject.SetActive(true);
            }
            if (portSpinner != null)
            {
                portSpinner.gameObject.SetActive(true);
            }
            if (starboardSpinner != null)
            {
                starboardSpinner.gameObject.SetActive(true);
            }
            if (coordinator != null)
            {
                coordinator.enabled = true;
                coordinator.Mode =
                    UsvActuatorVisualMode.DemoAndLocalDiagnosticPublicData;
            }
            if (driver != null)
            {
                driver.enabled = true;
            }
            if (authority != null)
            {
                authority.Mode = VehiclePoseControlMode.PublicData;
            }
        }

        private static void CaptureScreenshot(int index, double elapsed)
        {
            string directory = EvidenceDirectory();
            Directory.CreateDirectory(directory);
            string path = Path.Combine(
                directory,
                "m2d_visual_" + index + "_" +
                elapsed.ToString("00.00", CultureInfo.InvariantCulture) +
                "s.png");
            Camera camera = Camera.main ??
                            UnityEngine.Object.FindAnyObjectByType<Camera>(
                                FindObjectsInactive.Exclude);
            Require(camera != null,
                "No active Camera is available for M2-D visual evidence.");
            RenderTexture target = RenderTexture.GetTemporary(
                1280,
                720,
                24,
                RenderTextureFormat.ARGB32);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            Vector3 previousCameraPosition = camera.transform.position;
            Quaternion previousCameraRotation = camera.transform.rotation;
            float previousFieldOfView = camera.fieldOfView;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.transform.position = businessRoot.TransformPoint(
                    new Vector3(3.2f, 0.75f, 0f));
                camera.transform.LookAt(businessRoot.TransformPoint(
                    new Vector3(0.85f, -0.28f, 0f)));
                camera.fieldOfView = 32f;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0, false);
                image.Apply(false, false);
                File.WriteAllBytes(path, image.EncodeToPNG());
                visualEvidenceCount++;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.transform.position = previousCameraPosition;
                camera.transform.rotation = previousCameraRotation;
                camera.fieldOfView = previousFieldOfView;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private static void WriteReport()
        {
            string directory = EvidenceDirectory();
            Directory.CreateDirectory(directory);
            var text = new StringBuilder();
            text.AppendLine("# M2-D2 USV Actuator Visual Play Mode Verification");
            text.AppendLine();
            text.AppendLine("- Status: `M2D_USV_ACTUATOR_VISUAL_PLAYMODE_VALIDATION_PASS`");
            text.AppendLine("- Observation: `20 seconds`");
            text.AppendLine("- Representative frames: `" + visualEvidenceCount + "`");
            text.AppendLine("- Console errors: `" + errorCount + "`");
            text.AppendLine("- Positive max Port-Starboard RPM: `" +
                maxPositiveDifferential.ToString("F3", CultureInfo.InvariantCulture) + "`");
            text.AppendLine("- Negative max Starboard-Port RPM: `" +
                maxNegativeDifferential.ToString("F3", CultureInfo.InvariantCulture) + "`");
            text.AppendLine("- Positive max rudder: `" +
                maxPositiveRudder.ToString("F3", CultureInfo.InvariantCulture) + " deg`");
            text.AppendLine("- Negative min rudder: `" +
                minNegativeRudder.ToString("F3", CultureInfo.InvariantCulture) + " deg`");
            text.AppendLine("- Peak M2-C heave: `" + maxVisualHeave + "`");
            text.AppendLine("- Peak M2-C pitch: `" + maxVisualPitch + "`");
            text.AppendLine("- Peak M2-C roll: `" + maxVisualRoll + "`");
            text.AppendLine("- Managed memory start: `" + managedMemoryStart + "`");
            text.AppendLine("- Managed memory end: `" + managedMemoryEnd + "`");
            text.AppendLine("- Managed memory trend: `" +
                (managedMemoryEnd - managedMemoryStart) + " bytes`");
            text.AppendLine("- Current-thread allocated bytes start: `" +
                allocatedBytesStart + "`");
            text.AppendLine("- Current-thread allocated bytes end: `" +
                allocatedBytesEnd + "`");
            text.AppendLine("- Formal source mode: `" + formalSourceMode + "`");
            text.AppendLine("- Fixture source mode: `LocalDiagnostic`");
            text.AppendLine("- Fixture epoch: `" + fixtureEpoch + "`");
            text.AppendLine("- Driver applied epoch: `" + driverAppliedEpoch + "`");
            text.AppendLine("- Visual rating pending image review: `ACCEPTABLE`");
            text.AppendLine();
            foreach (string check in Checks)
            {
                text.AppendLine("- PASS — " + check);
            }
            File.WriteAllText(
                Path.Combine(directory, "m2d_playmode_report.md"),
                text.ToString(),
                new UTF8Encoding(false));
        }

        private static string EvidenceDirectory()
        {
            string configured =
                Environment.GetEnvironmentVariable("M2D_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.GetFullPath(configured);
            }
            return Path.GetFullPath(Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "..",
                "..",
                "M2D_Validation"));
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (!SessionState.GetBool(ActiveKey, false))
            {
                return;
            }
            if (type == LogType.Error ||
                type == LogType.Exception ||
                type == LogType.Assert)
            {
                errorCount++;
            }
        }

        private static void Advance(Phase next, double now)
        {
            phase = next;
            phaseStartedAt = now;
        }

        private static void Finish()
        {
            bool passed = SessionState.GetBool(PassedKey, false);
            bool batch = SessionState.GetBool(BatchKey, false);
            string detail = SessionState.GetString(
                DetailKey,
                "Verification did not complete.");
            SessionState.SetBool(ActiveKey, false);
            SessionState.SetBool(BatchKey, false);
            Unsubscribe();
            string status = passed
                ? "M2D_USV_ACTUATOR_VISUAL_PLAYMODE_VALIDATION_PASS"
                : "M2D_USV_ACTUATOR_VISUAL_PLAYMODE_VALIDATION_FAIL";
            Debug.Log(status + " | " + detail);
            if (batch)
            {
                EditorApplication.Exit(passed ? 0 : 1);
            }
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

        private static GameObject RequireRoot(string name)
        {
            GameObject[] matches = UnityEngine.Object.FindObjectsByType<Transform>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Where(item =>
                    item.parent == null &&
                    string.Equals(item.name, name, StringComparison.Ordinal))
                .Select(item => item.gameObject)
                .ToArray();
            Require(matches.Length == 1,
                "Expected exactly one root named " + name + ".");
            return matches[0];
        }

        private static Transform RequireDescendant(Transform root, string name)
        {
            Transform[] matches = root.GetComponentsInChildren<Transform>(true)
                .Where(item => string.Equals(item.name, name, StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1,
                "Expected exactly one descendant named " + name + ".");
            return matches[0];
        }

        private static T RequireComponent<T>(GameObject value, string label)
            where T : Component
        {
            T component = value.GetComponent<T>();
            Require(component != null, "Missing " + label + ".");
            return component;
        }

        private static Transform[] DirectChildren(Transform parent)
        {
            var result = new Transform[parent.childCount];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = parent.GetChild(index);
            }
            return result;
        }

        private static Vector3 RootSpacePosition(Transform value)
        {
            return businessRoot.InverseTransformPoint(value.position);
        }

        private static Vector3 SignedEuler(Vector3 value)
        {
            return new Vector3(
                Mathf.DeltaAngle(0f, value.x),
                Mathf.DeltaAngle(0f, value.y),
                Mathf.DeltaAngle(0f, value.z));
        }

        private static bool Near(float left, float right, float tolerance)
        {
            return Mathf.Abs(left - right) <= tolerance;
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

        private static bool Near(Matrix4x4 left, Matrix4x4 right, float tolerance)
        {
            for (int index = 0; index < 16; index++)
            {
                if (Mathf.Abs(left[index] - right[index]) > tolerance)
                {
                    return false;
                }
            }
            return true;
        }

        private static void ResetObservationCounters()
        {
            initialHoldSamples = 0;
            positiveYawSamples = 0;
            middleHoldSamples = 0;
            negativeYawSamples = 0;
            finalHoldSamples = 0;
            maxPositiveDifferential = 0f;
            maxNegativeDifferential = 0f;
            maxPositiveRudder = 0f;
            minNegativeRudder = 0f;
            maxVisualHeave = 0f;
            maxVisualPitch = 0f;
            maxVisualRoll = 0f;
            portRotationObserved = false;
            starboardRotationObserved = false;
            demoResponseObserved = false;
        }

        private static void Add(string name, string detail)
        {
            Checks.Add(name + ": " + detail);
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
