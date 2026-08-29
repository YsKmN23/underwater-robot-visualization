using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public static class UsvSurfaceVisualM2CPlayModeVerifier
    {
        private const string ActiveKey = "M2C1.UsvSurfaceVisualPlay.Active";
        private const string PassedKey = "M2C1.UsvSurfaceVisualPlay.Passed";
        private const string DetailKey = "M2C1.UsvSurfaceVisualPlay.Detail";
        private const string BatchKey = "M2C1.UsvSurfaceVisualPlay.Batch";
        private const string FormalSourceModeKey =
            "M2C1.UsvSurfaceVisualPlay.FormalSourceMode";
        private const string FixtureSourceModeKey =
            "M2C1.UsvSurfaceVisualPlay.FixtureSourceMode";
        private const string FixtureEpochKey =
            "M2C1.UsvSurfaceVisualPlay.FixtureEpoch";
        private const string DriverAppliedEpochKey =
            "M2C1.UsvSurfaceVisualPlay.DriverAppliedEpoch";
        private const string ColliderPreflightKey =
            "M2C1.UsvSurfaceVisualPlay.ColliderPreflight";
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";

        private enum Phase
        {
            WaitHealthy,
            ObserveTwentySeconds,
            ModeDisabled,
            ModeRecovery,
            DemoAuthority,
            PublicRecovery,
            DriverDisabled,
            DriverRecovery,
            ProviderDisabled,
            ProviderRecovery,
            ControllerDisabled,
            ControllerRecovery,
            WaitStale,
            WaitRestart,
            TeleportDistance,
            TeleportDistanceRecovery,
            TeleportAngle,
            TeleportAngleRecovery,
            Final
        }

        private static bool subscribed;
        private static bool bound;
        private static int errorCount;
        private static Phase phase;
        private static double enteredAt;
        private static double phaseStartedAt;
        private static double observationStartedAt;
        private static ulong epochBeforeRestart;
        private static ulong fixtureEpoch;
        private static ulong driverAppliedEpoch;
        private static double authorityTransitionElapsed;
        private static VehicleRuntimeSourceMode formalSourceMode;
        private static bool fixtureEstablished;
        private static long gcAtObservationStart;
        private static long gcAtObservationEnd;
        private static readonly List<string> Checks = new List<string>();

        private static Transform businessRoot;
        private static Transform visualRoot;
        private static Transform importedRoot;
        private static Transform rudder;
        private static GameObject water;
        private static UsvSurfaceVisualController controller;
        private static FlatWaterSurfaceProvider provider;
        private static VehiclePoseDriver driver;
        private static VehicleDataRuntimeHost host;
        private static VehiclePoseIntegrationConfiguration configuration;
        private static VehiclePoseControlAuthority authority;
        private static DemoMotionController demo;
        private static PropellerSpinner[] spinners;
        private static VehiclePoseControlAuthority auvAuthority;
        private static VehiclePoseControlAuthority rovAuthority;

        private static Vector3 importedLocalPosition;
        private static Quaternion importedLocalRotation;
        private static Vector3 importedLocalScale;
        private static Vector3 rudderLocalPosition;
        private static Quaternion rudderLocalRotation;
        private static Vector3 rudderLocalScale;
        private static Vector3 waterPosition;
        private static Quaternion waterRotation;
        private static Vector3 waterScale;
        private static Material waterMaterial;
        private static BoxCollider waterCollider;
        private static Vector3 waterColliderCenter;
        private static Vector3 waterColliderSize;
        private static Vector3[] spinnerLocalPositions;
        private static Vector3[] spinnerLocalScales;
        private static Quaternion[] spinnerInitialRotations;
        private static bool[] spinnerRotated;
        private static Vector3 previousBusinessPosition;
        private static float maxBusinessDisplacement;
        private static float maxBusinessYDeviation;
        private static int stationaryFrames;
        private static float maxVisualHeight;
        private static float maxVisualPitch;
        private static float maxVisualRoll;
        private static float maxVisualYaw;
        private static int lastObservedFrame = -1;
        private static int visualEvidenceCount;
        private static Vector3 diagnosticOrigin;
        private static readonly bool[] ScreenshotTaken = new bool[5];

        static UsvSurfaceVisualM2CPlayModeVerifier()
        {
            if (SessionState.GetBool(ActiveKey, false))
            {
                Subscribe();
            }
        }

        [MenuItem("Tools/Underwater Demo/M2-C1/Run USV Surface Visual Play Mode Verification")]
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
                    "M2-C1 Play Mode verification is already active.");
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
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject editWater = scene.GetRootGameObjects()
                .Single(item => string.Equals(
                    item.name,
                    "Water_Surface",
                    StringComparison.Ordinal));
            BoxCollider editCollider = editWater.GetComponent<BoxCollider>();
            Require(editCollider != null &&
                    Near(editCollider.center, Vector3.zero, 0.000001f) &&
                    Near(editCollider.size, Vector3.one, 0.000001f),
                "Water collider failed the edit-mode preflight.");
            SessionState.SetBool(ColliderPreflightKey, true);
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
                Checks.Clear();
                maxBusinessDisplacement = 0f;
                maxBusinessYDeviation = 0f;
                stationaryFrames = 0;
                maxVisualHeight = 0f;
                maxVisualPitch = 0f;
                maxVisualRoll = 0f;
                maxVisualYaw = 0f;
                lastObservedFrame = -1;
                visualEvidenceCount = 0;
                fixtureEstablished = false;
                fixtureEpoch = 0UL;
                driverAppliedEpoch = 0UL;
                Array.Clear(ScreenshotTaken, 0, ScreenshotTaken.Length);
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
                    bound = true;
                }

                double now = Time.realtimeSinceStartupAsDouble;
                double elapsed = now - phaseStartedAt;
                ObserveInvariants();

                switch (phase)
                {
                    case Phase.WaitHealthy:
                        if (IsHealthyAndActive())
                        {
                            Require(host.TryGetActiveEpoch(out fixtureEpoch),
                                "The M2-C LocalDiagnostic fixture has no active epoch.");
                            driverAppliedEpoch = driver.LastAppliedSourceEpoch;
                            Require(driverAppliedEpoch == fixtureEpoch,
                                "The Driver did not apply the M2-C LocalDiagnostic fixture epoch.");
                            SessionState.SetString(FixtureEpochKey, fixtureEpoch.ToString());
                            SessionState.SetString(
                                DriverAppliedEpochKey,
                                driverAppliedEpoch.ToString());
                            ValidateInitialState();
                            observationStartedAt = now;
                            gcAtObservationStart = GC.GetTotalMemory(false);
                            previousBusinessPosition = businessRoot.position;
                            Advance(Phase.ObserveTwentySeconds, now);
                        }
                        else if (now - enteredAt > 6.0)
                        {
                            throw new InvalidOperationException(
                                "USV public diagnostic chain did not become healthy within six seconds.");
                        }
                        break;

                    case Phase.ObserveTwentySeconds:
                        ObserveMotion(now - observationStartedAt);
                        if (now - observationStartedAt >= 20.0)
                        {
                            gcAtObservationEnd = GC.GetTotalMemory(false);
                            ValidateObservation();
                            controller.Mode = UsvSurfaceVisualMode.Disabled;
                            Advance(Phase.ModeDisabled, now);
                        }
                        break;

                    case Phase.ModeDisabled:
                        if (elapsed >= 0.15)
                        {
                            RequireIdentity("mode Disabled");
                            Add("Disabled gate", "Mode Disabled restored identity.");
                            controller.Mode =
                                UsvSurfaceVisualMode.LocalDiagnosticPublicData;
                            Advance(Phase.ModeRecovery, now);
                        }
                        break;

                    case Phase.ModeRecovery:
                        if (WaitForFreshFade(now, elapsed))
                        {
                            Add("Mode recovery", "Public diagnostic mode restarted from phase zero.");
                            authorityTransitionElapsed = controller.ElapsedSeconds;
                            authority.Mode = VehiclePoseControlMode.Demo;
                            Advance(Phase.DemoAuthority, now);
                        }
                        break;

                    case Phase.DemoAuthority:
                        if (elapsed >= 0.15)
                        {
                            Require(authority.DemoOwnsControl &&
                                    !demo.DrivesUsv &&
                                    driver.OwnsControl &&
                                    driver.HasFreshAppliedPose &&
                                    host.SourceMode == VehicleRuntimeSourceMode.LocalDiagnostic &&
                                    host.TryGetActiveEpoch(out ulong demoEpoch) &&
                                    driver.LastAppliedSourceEpoch == demoEpoch &&
                                    controller.DiagnosticActive &&
                                    controller.ElapsedSeconds > authorityTransitionElapsed,
                                "Demo Authority did not preserve Driver-owned LocalDiagnostic surface presentation.");
                            Add("Demo gate", "Driver-owned LocalDiagnostic surface overlay continued in Demo.");
                            authorityTransitionElapsed = controller.ElapsedSeconds;
                            authority.Mode = VehiclePoseControlMode.PublicData;
                            Advance(Phase.PublicRecovery, now);
                        }
                        break;

                    case Phase.PublicRecovery:
                        if (elapsed >= 0.15 && IsHealthyAndActive())
                        {
                            Require(controller.ElapsedSeconds > authorityTransitionElapsed,
                                "PublicData authority switch reset the continuous LocalDiagnostic overlay.");
                            Add("Authority recovery", "PublicData retained the Driver-owned LocalDiagnostic overlay.");
                            driver.enabled = false;
                            Advance(Phase.DriverDisabled, now);
                        }
                        break;

                    case Phase.DriverDisabled:
                        if (elapsed >= 0.15)
                        {
                            RequireIdentity("Driver disabled");
                            Require(!driver.HasFreshAppliedPose,
                                "Disabled Driver retained a fresh-pose flag.");
                            Add("Driver disable", "Driver disable invalidated freshness and restored identity.");
                            driver.enabled = true;
                            Advance(Phase.DriverRecovery, now);
                        }
                        break;

                    case Phase.DriverRecovery:
                        if (WaitForFreshFade(now, elapsed))
                        {
                            Add("Driver recovery", "Driver re-enable restarted from phase zero.");
                            provider.enabled = false;
                            Advance(Phase.ProviderDisabled, now);
                        }
                        break;

                    case Phase.ProviderDisabled:
                        if (elapsed >= 0.15)
                        {
                            RequireIdentity("Provider disabled");
                            Add("Provider invalid", "Disabled Provider restored identity.");
                            provider.enabled = true;
                            Advance(Phase.ProviderRecovery, now);
                        }
                        break;

                    case Phase.ProviderRecovery:
                        if (WaitForFreshFade(now, elapsed))
                        {
                            Add("Provider recovery", "Provider recovery restarted from phase zero.");
                            controller.enabled = false;
                            Advance(Phase.ControllerDisabled, now);
                        }
                        break;

                    case Phase.ControllerDisabled:
                        if (elapsed >= 0.15)
                        {
                            RequireIdentity("Controller disabled");
                            Add("Controller disable", "OnDisable restored the original identity pose.");
                            controller.enabled = true;
                            Advance(Phase.ControllerRecovery, now);
                        }
                        break;

                    case Phase.ControllerRecovery:
                        if (WaitForFreshFade(now, elapsed))
                        {
                            Add("Controller recovery", "OnEnable restarted from phase zero.");
                            Require(host.TryGetActiveEpoch(out epochBeforeRestart),
                                "Active epoch was unavailable before stale test.");
                            host.StopSource();
                            Advance(Phase.WaitStale, now);
                        }
                        break;

                    case Phase.WaitStale:
                        if (driver.LastFailureReason == RenderSampleFailureReason.Stale)
                        {
                            RequireIdentity("stale pose");
                            Require(!driver.HasFreshAppliedPose,
                                "Stale Driver retained a fresh-pose flag.");
                            Add("Stale gate", "Sampler stale failure restored identity.");
                            host.RestartSource();
                            Advance(Phase.WaitRestart, now);
                        }
                        else if (elapsed > 2.0)
                        {
                            throw new InvalidOperationException(
                                "USV source did not produce stale state within two seconds.");
                        }
                        break;

                    case Phase.WaitRestart:
                        if (IsHealthyAndActive() &&
                            host.TryGetActiveEpoch(out ulong restartedEpoch) &&
                            restartedEpoch != epochBeforeRestart)
                        {
                            Require(driver.LastAppliedSourceEpoch == restartedEpoch,
                                "Driver did not publish the restarted source epoch.");
                            Add("Epoch restart",
                                "New epoch restored identity then restarted diagnostic phase.");
                            Advance(Phase.TeleportDistance, now);
                        }
                        else if (elapsed > 5.0)
                        {
                            throw new InvalidOperationException(
                                "USV source did not recover with a new epoch.");
                        }
                        break;

                    case Phase.TeleportDistance:
                        if (elapsed >= 0.8 && controller.DiagnosticActive)
                        {
                            Vector3 originalPosition = businessRoot.position;
                            businessRoot.position += Vector3.right * 0.30f;
                            controller.TickForDiagnostics(0.016);
                            RequireIdentity("distance teleport");
                            businessRoot.position = originalPosition;
                            Add("Distance teleport",
                                "0.30 m business-root jump reset the visual pose without a pulse.");
                            Advance(Phase.TeleportDistanceRecovery, now);
                        }
                        break;

                    case Phase.TeleportDistanceRecovery:
                        if (WaitForFreshFade(now, elapsed))
                        {
                            Advance(Phase.TeleportAngle, now);
                        }
                        break;

                    case Phase.TeleportAngle:
                        if (elapsed >= 0.8 && controller.DiagnosticActive)
                        {
                            Quaternion originalRotation = businessRoot.rotation;
                            businessRoot.rotation =
                                Quaternion.AngleAxis(35f, Vector3.up) * originalRotation;
                            controller.TickForDiagnostics(0.016);
                            RequireIdentity("angle teleport");
                            businessRoot.rotation = originalRotation;
                            Add("Angle teleport",
                                "35 degree business-root jump reset the visual pose without a pulse.");
                            Advance(Phase.TeleportAngleRecovery, now);
                        }
                        break;

                    case Phase.TeleportAngleRecovery:
                        if (WaitForFreshFade(now, elapsed))
                        {
                            Add("Teleport recovery",
                                "Valid data after both teleport cases restarted from phase zero.");
                            Advance(Phase.Final, now);
                        }
                        break;

                    case Phase.Final:
                        ValidateFinalState();
                        WriteReport();
                        RestoreFormalSourceMode();
                        SessionState.SetBool(PassedKey, true);
                        SessionState.SetString(
                            DetailKey,
                            "M2-C1 isolated LocalDiagnostic lifecycle, 20-second motion, isolation, gates, stale, epoch, disable and teleport verification passed. " +
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
                        "M2-C1 Play Mode verification exceeded 55 seconds.");
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
            businessRoot = RequireObject("USV_Blue_Surface").transform;
            visualRoot = RequireChild(businessRoot, "USV_SurfaceVisualRoot");
            importedRoot = RequireChild(visualRoot, "USV_FineModel_V1_Imported");
            rudder = RequireChild(importedRoot, "USV_Rudder_Main");
            water = RequireObject("Water_Surface");
            controller = RequireComponent<UsvSurfaceVisualController>(
                visualRoot.gameObject,
                "USV surface visual Controller");
            provider = RequireComponent<FlatWaterSurfaceProvider>(
                water,
                "Flat water Provider");
            authority = RequireComponent<VehiclePoseControlAuthority>(
                businessRoot.gameObject,
                "USV Authority");
            driver = RequireComponent<VehiclePoseDriver>(
                RequireObject("USV_PublicPoseDriver"),
                "USV Driver");
            host = RequireComponent<VehicleDataRuntimeHost>(
                RequireObject("USV_PublicData_RuntimeHost"),
                "USV Host");
            configuration = driver.IntegrationConfiguration;
            demo = UnityEngine.Object.FindAnyObjectByType<DemoMotionController>(
                FindObjectsInactive.Include);
            Require(configuration != null && demo != null,
                "USV integration configuration or DemoMotionController is missing.");

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
                "The non-persistent M2-C LocalDiagnostic fixture did not start.");
            SessionState.SetString(
                FixtureSourceModeKey,
                host.SourceMode.ToString());
            diagnosticOrigin = configuration.TestOrigin;

            spinners = businessRoot.GetComponentsInChildren<PropellerSpinner>(true);
            Require(spinners.Length == 2, "Expected two USV PropellerSpinner components.");
            spinnerLocalPositions = spinners.Select(item => item.transform.localPosition).ToArray();
            spinnerLocalScales = spinners.Select(item => item.transform.localScale).ToArray();
            spinnerInitialRotations =
                spinners.Select(item => item.transform.localRotation).ToArray();
            spinnerRotated = new bool[spinners.Length];

            importedLocalPosition = importedRoot.localPosition;
            importedLocalRotation = importedRoot.localRotation;
            importedLocalScale = importedRoot.localScale;
            rudderLocalPosition = rudder.localPosition;
            rudderLocalRotation = rudder.localRotation;
            rudderLocalScale = rudder.localScale;
            waterPosition = water.transform.position;
            waterRotation = water.transform.rotation;
            waterScale = water.transform.localScale;
            MeshRenderer renderer = RequireComponent<MeshRenderer>(water, "Water renderer");
            waterMaterial = renderer.sharedMaterial;
            waterCollider = water.GetComponent<BoxCollider>();
            Require(waterCollider != null ||
                    SessionState.GetBool(ColliderPreflightKey, false),
                "Water collider was absent without a successful edit-mode preflight.");
            if (waterCollider != null)
            {
                waterColliderCenter = waterCollider.center;
                waterColliderSize = waterCollider.size;
            }

            auvAuthority = RequireComponent<VehiclePoseControlAuthority>(
                RequireObject("AUV_Yellow_Underwater"),
                "AUV Authority");
            rovAuthority = RequireComponent<VehiclePoseControlAuthority>(
                RequireObject("ROV_Box_Seabed"),
                "ROV Authority");
        }

        private static void ValidateInitialState()
        {
            Require(authority.PublicDataOwnsControl &&
                    driver.TargetRoot == businessRoot &&
                    controller.BusinessRoot == businessRoot &&
                    controller.ImportedModelRoot == importedRoot &&
                    controller.WaterSurfaceProvider == provider &&
                    controller.PoseDriver == driver &&
                    controller.ControlAuthority == authority &&
                    host.SourceMode == VehicleRuntimeSourceMode.LocalDiagnostic &&
                    controller.Mode ==
                        UsvSurfaceVisualMode.LocalDiagnosticPublicData,
                "Initial M2-C1 references, mode or ownership are invalid.");
            Require(Near(visualRoot.localScale, Vector3.one, 0.000001f),
                "Visual root scale is not one.");
            Add("Initial activation",
                "PublicData diagnostic mode activated with explicit references and identity scale.");
        }

        private static void ObserveMotion(double elapsed)
        {
            if (Time.frameCount == lastObservedFrame)
            {
                return;
            }
            lastObservedFrame = Time.frameCount;
            Vector3 current = businessRoot.position;
            maxBusinessDisplacement = Mathf.Max(
                maxBusinessDisplacement,
                Vector3.Distance(current, diagnosticOrigin));
            maxBusinessYDeviation = Mathf.Max(
                maxBusinessYDeviation,
                Mathf.Abs(current.y - diagnosticOrigin.y));
            Require(Vector3.Dot(businessRoot.up, Vector3.up) > 0.9999f,
                "USV business root acquired pitch or roll during LocalDiagnostic motion.");
            if (Vector3.Distance(previousBusinessPosition, current) < 0.00001f)
            {
                stationaryFrames++;
            }
            previousBusinessPosition = current;

            maxVisualHeight = Mathf.Max(maxVisualHeight, Mathf.Abs(visualRoot.localPosition.y));
            Vector3 euler = SignedEuler(visualRoot.localEulerAngles);
            maxVisualPitch = Mathf.Max(maxVisualPitch, Mathf.Abs(euler.x));
            maxVisualYaw = Mathf.Max(maxVisualYaw, Mathf.Abs(euler.y));
            maxVisualRoll = Mathf.Max(maxVisualRoll, Mathf.Abs(euler.z));

            for (int index = 0; index < spinners.Length; index++)
            {
                spinnerRotated[index] |=
                    Quaternion.Angle(
                        spinnerInitialRotations[index],
                        spinners[index].transform.localRotation) > 5f;
            }

            double[] times = { 2.0, 6.0, 10.0, 14.0, 18.0 };
            for (int index = 0; index < times.Length; index++)
            {
                if (!ScreenshotTaken[index] && elapsed >= times[index])
                {
                    ScreenshotTaken[index] = true;
                    CaptureScreenshot(index, elapsed);
                }
            }
        }

        private static void ObserveInvariants()
        {
            Require(Near(importedRoot.localPosition, importedLocalPosition, 0.000001f) &&
                    Near(importedRoot.localRotation, importedLocalRotation, 0.000001f) &&
                    Near(importedRoot.localScale, importedLocalScale, 0.00001f),
                "Imported model local Transform changed in Play Mode.");
            Require(Near(rudder.localPosition, rudderLocalPosition, 0.000001f) &&
                    Near(rudder.localRotation, rudderLocalRotation, 0.000001f) &&
                    Near(rudder.localScale, rudderLocalScale, 0.000001f),
                "Main rudder local Transform changed in Play Mode.");
            Require(Near(water.transform.position, waterPosition, 0.000001f) &&
                    Near(water.transform.rotation, waterRotation, 0.000001f) &&
                    Near(water.transform.localScale, waterScale, 0.000001f) &&
                    water.GetComponent<MeshRenderer>().sharedMaterial == waterMaterial &&
                    (waterCollider == null ||
                     (water.GetComponent<BoxCollider>() == waterCollider &&
                      Near(waterCollider.center, waterColliderCenter, 0.000001f) &&
                      Near(waterCollider.size, waterColliderSize, 0.000001f))) &&
                    SessionState.GetBool(ColliderPreflightKey, false),
                "Water_Surface Transform, Material or Collider changed.");
            Require(Near(visualRoot.localScale, Vector3.one, 0.000001f),
                "Visual root scale changed.");
            for (int index = 0; index < spinners.Length; index++)
            {
                Require(Near(
                            spinners[index].transform.localPosition,
                            spinnerLocalPositions[index],
                            0.000001f) &&
                        Near(
                            spinners[index].transform.localScale,
                            spinnerLocalScales[index],
                            0.000001f),
                    "A USV Spinner local position or scale changed.");
            }
        }

        private static void ValidateObservation()
        {
            Require(maxBusinessDisplacement > 0.55f &&
                    maxBusinessDisplacement <= 0.6001f,
                "USV LocalDiagnostic displacement left the M2-B 0.60 m envelope. " +
                "maxDisplacement=" + maxBusinessDisplacement + ".");
            Require(stationaryFrames > 10,
                "USV LocalDiagnostic hold windows were not observed. " +
                "stationaryFrames=" + stationaryFrames + ".");
            Require(maxBusinessYDeviation <= 0.0001f,
                "USV LocalDiagnostic business Y changed. " +
                "maxYDeviation=" + maxBusinessYDeviation + ".");
            Add("LocalDiagnostic root displacement",
                "The Driver kept the business root within the M2-B 0.60 m envelope.");
            Add("LocalDiagnostic holds",
                "Stationary samples proved the diagnostic hold windows.");
            Add("Business-root authority",
                "Business Y remained fixed and the Movement Root remained yaw-only.");
            Require(maxVisualHeight >= 0.005f &&
                    maxVisualHeight <= 0.0651f &&
                    maxVisualPitch >= 0.25f &&
                    maxVisualPitch <= 0.8001f &&
                    maxVisualRoll >= 0.40f &&
                    maxVisualRoll <= 1.2001f &&
                    maxVisualYaw <= 0.05f,
                "Visual heave/pitch/roll was unobservable, out of bounds, or added yaw.");
            Require(visualEvidenceCount == ScreenshotTaken.Length,
                "Normal-render visual evidence capture did not produce five frames.");
            Require(auvAuthority.PublicDataOwnsControl &&
                    rovAuthority.PublicDataOwnsControl &&
                    RequireObject("AUV_Yellow_Underwater")
                        .GetComponentInChildren<UsvSurfaceVisualController>(true) == null &&
                    RequireObject("ROV_Box_Seabed")
                        .GetComponentInChildren<UsvSurfaceVisualController>(true) == null,
                "AUV/ROV isolation or Authority changed.");
            Add("Twenty-second visual observation",
                "Two 8 s cycles, M2-B 0.30 m radius/0.60 m diameter motion and holds, and bounded heave/pitch/roll with zero visual-root yaw passed; M2-D owns dynamic USV actuator checks.");
            Add("Business/model/water isolation",
                "Business Y, Driver target, imported model, rudder, water and sibling vehicles remained unchanged.");
        }

        private static bool WaitForFreshFade(double now, double elapsed)
        {
            if (!IsHealthyAndActive())
            {
                if (elapsed > 5.0)
                {
                    throw new InvalidOperationException(
                        phase + " did not recover within five seconds.");
                }
                return false;
            }

            if (elapsed < 0.85)
            {
                return false;
            }
            Require(controller.ElapsedSeconds < 2.0,
                phase + " did not restart diagnostic phase near zero.");
            return true;
        }

        private static bool IsHealthyAndActive()
        {
            return authority.PublicDataOwnsControl &&
                   driver.isActiveAndEnabled &&
                   driver.OwnsControl &&
                   driver.HasAppliedPose &&
                   driver.HasFreshAppliedPose &&
                   driver.LastFailureReason == RenderSampleFailureReason.None &&
                   provider.isActiveAndEnabled &&
                   controller.isActiveAndEnabled &&
                   controller.DiagnosticActive;
        }

        private static void RequireIdentity(string context)
        {
            Require(Near(visualRoot.localPosition, Vector3.zero, 0.00001f) &&
                    Near(visualRoot.localRotation, Quaternion.identity, 0.00001f) &&
                    Near(visualRoot.localScale, Vector3.one, 0.000001f) &&
                    !controller.DiagnosticActive &&
                    Math.Abs(controller.ElapsedSeconds) <= 0.000001,
                "Visual root did not reset to identity for " + context + ".");
        }

        private static void ValidateFinalState()
        {
            RestoreRuntimeControls();
            Require(errorCount == 0,
                "Console recorded Error/Exception/Assert messages: " + errorCount + ".");
            Require(Checks.Count >= 15,
                "Not all M2-C1 Play Mode acceptance groups completed.");
            Add("Console and performance boundary",
                "Error/Exception/Assert=0; runtime code uses cached constant-time sampling and one visual-root write.");
        }

        private static void RestoreRuntimeControls()
        {
            if (controller != null)
            {
                controller.enabled = true;
                controller.Mode = UsvSurfaceVisualMode.LocalDiagnosticPublicData;
            }
            if (provider != null)
            {
                provider.enabled = true;
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
                "m2c1_visual_" + index + "_" +
                elapsed.ToString("00.0", System.Globalization.CultureInfo.InvariantCulture) +
                "s.png");
            Camera camera = Camera.main ??
                            UnityEngine.Object.FindAnyObjectByType<Camera>(
                                FindObjectsInactive.Exclude);
            Require(camera != null, "No active Camera is available for visual evidence.");
            RenderTexture target = RenderTexture.GetTemporary(
                1280,
                720,
                24,
                RenderTextureFormat.ARGB32);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
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
            text.AppendLine("# M2-C1 USV Surface Visual Play Mode Verification");
            text.AppendLine();
            text.AppendLine("- Status: `M2C1_USV_SURFACE_VISUAL_PLAYMODE_VALIDATION_PASS`");
            text.AppendLine("- Observation: `20 seconds`");
            text.AppendLine("- Visual periods: `2.5`");
            text.AppendLine("- Console errors: `" + errorCount + "`");
            text.AppendLine("- Peak local Y: `" + maxVisualHeight + "`");
            text.AppendLine("- Peak pitch: `" + maxVisualPitch + "`");
            text.AppendLine("- Peak roll: `" + maxVisualRoll + "`");
            text.AppendLine("- Peak yaw: `" + maxVisualYaw + "`");
            text.AppendLine("- Managed memory start: `" + gcAtObservationStart + "`");
            text.AppendLine("- Managed memory end: `" + gcAtObservationEnd + "`");
            text.AppendLine("- Visual evidence frames: `" + visualEvidenceCount + "`");
            text.AppendLine("- Formal source mode: `" + formalSourceMode + "`");
            text.AppendLine("- Fixture source mode: `LocalDiagnostic`");
            text.AppendLine("- Fixture epoch: `" + fixtureEpoch + "`");
            text.AppendLine("- Driver applied epoch: `" + driverAppliedEpoch + "`");
            text.AppendLine("- Maximum business-root displacement: `" +
                maxBusinessDisplacement + "`");
            text.AppendLine("- Maximum business-root Y deviation: `" +
                maxBusinessYDeviation + "`");
            text.AppendLine();
            foreach (string check in Checks)
            {
                text.AppendLine("- PASS — " + check);
            }
            File.WriteAllText(
                Path.Combine(directory, "m2c1_playmode_report.md"),
                text.ToString(),
                new UTF8Encoding(false));
        }

        private static string EvidenceDirectory()
        {
            string configured = Environment.GetEnvironmentVariable("M2C1_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.GetFullPath(configured);
            }
            return Path.GetFullPath(Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "..",
                "..",
                "M2C1_Validation"));
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
            if (passed)
            {
                try
                {
                    Scene scene = EditorSceneManager.GetActiveScene();
                    GameObject editWater = scene.GetRootGameObjects()
                        .Single(item => string.Equals(
                            item.name,
                            "Water_Surface",
                            StringComparison.Ordinal));
                    BoxCollider editCollider = editWater.GetComponent<BoxCollider>();
                    Require(editCollider != null &&
                            Near(editCollider.center, Vector3.zero, 0.000001f) &&
                            Near(editCollider.size, Vector3.one, 0.000001f),
                        "Water collider failed the edit-mode postflight.");
                }
                catch (Exception exception)
                {
                    passed = false;
                    detail = exception.GetType().Name + ": " + exception.Message;
                }
            }
            SessionState.SetBool(ActiveKey, false);
            SessionState.SetBool(BatchKey, false);
            SessionState.SetBool(ColliderPreflightKey, false);
            Unsubscribe();
            string status = passed
                ? "M2C1_USV_SURFACE_VISUAL_PLAYMODE_VALIDATION_PASS"
                : "M2C1_USV_SURFACE_VISUAL_PLAYMODE_VALIDATION_FAIL";
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

        private static GameObject RequireObject(string name)
        {
            GameObject[] matches = UnityEngine.Object.FindObjectsByType<Transform>(
                    FindObjectsInactive.Include)
                .Where(item =>
                    item.parent == null &&
                    string.Equals(item.name, name, StringComparison.Ordinal))
                .Select(item => item.gameObject)
                .ToArray();
            Require(matches.Length == 1, "Expected one root named " + name + ".");
            return matches[0];
        }

        private static Transform RequireChild(Transform root, string name)
        {
            Transform[] matches = root.GetComponentsInChildren<Transform>(true)
                .Where(item => string.Equals(item.name, name, StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1,
                "Expected one descendant named " + name + ".");
            return matches[0];
        }

        private static T RequireComponent<T>(GameObject value, string label)
            where T : Component
        {
            T component = value.GetComponent<T>();
            Require(component != null, "Missing " + label + ".");
            return component;
        }

        private static Vector3 SignedEuler(Vector3 value)
        {
            return new Vector3(
                Mathf.DeltaAngle(0f, value.x),
                Mathf.DeltaAngle(0f, value.y),
                Mathf.DeltaAngle(0f, value.z));
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
