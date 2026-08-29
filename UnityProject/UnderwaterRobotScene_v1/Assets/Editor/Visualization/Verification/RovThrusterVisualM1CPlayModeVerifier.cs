using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.LocalTesting;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Rov;
using UnderwaterRobotScene.Visualization.Runtime.Usv;
using UnderwaterRobotScene.Visualization.Sampling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    [InitializeOnLoad]
    public static class RovThrusterVisualM1CPlayModeVerifier
    {
        private const string ActiveKey = "M1C1.RovThrusterPlay.Active";
        private const string PassedKey = "M1C1.RovThrusterPlay.Passed";
        private const string DetailKey = "M1C1.RovThrusterPlay.Detail";
        private const string BatchKey = "M1C1.RovThrusterPlay.Batch";
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const float IdleTolerance = 2f;
        private const float PairTolerance = 0.001f;

        private enum Phase
        {
            WaitHealthy,
            ObserveInitial,
            ObserveFirstMotion,
            ObserveFirstHover,
            ObserveSecondMotion,
            ObserveSecondHover,
            ObserveReturn,
            ObserveFinalHold,
            ObserveLoop,
            WaitStale,
            WaitRestart,
            WaitDemo,
            WaitPublic,
            WaitDriverDisabled,
            WaitDriverRecovery,
            WaitDisableRestore,
            WaitFinal
        }

        private static bool subscribed;
        private static bool referencesBound;
        private static int errorCount;
        private static double enteredAt;
        private static double phaseStartedAt;
        private static float earlyTransitionPeak;
        private static Phase phase;

        private static Transform rov;
        private static Transform model;
        private static Transform auv;
        private static Transform usv;
        private static RovThrusterVisualCoordinator coordinator;
        private static VehiclePoseDriver driver;
        private static VehicleDataRuntimeHost host;
        private static VehiclePoseIntegrationConfiguration configuration;
        private static VehiclePoseControlAuthority authority;
        private static DemoMotionController demo;
        private static PropellerSpinner[] spinners;
        private static float[] originalRpm;
        private static Vector3[] spinnerLocalPositions;
        private static Vector3[] spinnerLocalScales;
        private static Quaternion[] spinnerInitialRotations;
        private static bool[] spinnerRotationObserved;
        private static Vector3 modelLocalPosition;
        private static Quaternion modelLocalRotation;
        private static Vector3 modelLocalScale;
        private static Vector3 rovLocalScale;
        private static Vector3 auvInitialPosition;
        private static Vector3 usvInitialPosition;
        private static VehiclePoseControlAuthority auvAuthority;
        private static VehiclePoseControlAuthority usvAuthority;
        private static VehiclePoseDriver auvDriver;
        private static VehiclePoseDriver usvDriver;
        private static PropellerSpinner[] auvSpinners;
        private static PropellerSpinner[] usvSpinners;
        private static float[] auvInitialRpm;
        private static float[] usvInitialRpm;
        private static Transform[] auvSpinnerParents;
        private static Transform[] usvSpinnerParents;
        private static Vector3[] auvSpinnerLocalPositions;
        private static Vector3[] auvSpinnerLocalScales;
        private static Vector3[] usvSpinnerLocalPositions;
        private static Vector3[] usvSpinnerLocalScales;
        private static UsvActuatorVisualCoordinator usvActuatorCoordinator;
        private static bool usvRpmVariationObserved;

        static RovThrusterVisualM1CPlayModeVerifier()
        {
            if (SessionState.GetBool(ActiveKey, false))
            {
                Subscribe();
            }
        }

        [MenuItem("Tools/Underwater Demo/M1-C1/Run ROV Visual Thruster Play Mode Verification")]
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
                    "M1-C1 Play Mode verification is already active.");
            }

            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(PassedKey, false);
            SessionState.SetString(DetailKey, "Verification did not complete.");
            SessionState.SetBool(BatchKey, batch);
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
                errorCount = 0;
                earlyTransitionPeak = 0f;
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

                double now = Time.realtimeSinceStartupAsDouble;
                double elapsed = now - phaseStartedAt;
                ObserveRuntime(now);
                double targetSourceTime = host.GetTargetSourceTimestamp(
                    now,
                    configuration.RenderDelaySeconds);

                switch (phase)
                {
                    case Phase.WaitHealthy:
                        if (IsHealthy())
                        {
                            ValidateInitialRuntimeState();
                            Advance(Phase.ObserveInitial, now);
                        }
                        else if (now - enteredAt > 5.0)
                        {
                            throw new InvalidOperationException(
                                "ROV public chain did not become healthy within five seconds.");
                        }
                        break;

                    case Phase.ObserveInitial:
                        if (targetSourceTime >= 0.60)
                        {
                            ValidateIdle("initial hold");
                            ValidateRootAt(configuration.TestOrigin, 0.002f, "initial hold");
                            Advance(Phase.ObserveFirstMotion, now);
                        }
                        break;

                    case Phase.ObserveFirstMotion:
                        if (targetSourceTime >= 1.80)
                        {
                            ValidateMovement("first motion");
                            Advance(Phase.ObserveFirstHover, now);
                        }
                        break;

                    case Phase.ObserveFirstHover:
                        if (targetSourceTime >= 4.05)
                        {
                            ValidateIdle("Pose A hover");
                            ValidateRootAt(
                                configuration.TestOrigin +
                                new Vector3(0.30f, 0.12f, -0.18f),
                                0.002f,
                                "Pose A hover");
                            Advance(Phase.ObserveSecondMotion, now);
                        }
                        break;

                    case Phase.ObserveSecondMotion:
                        if (targetSourceTime >= 5.60)
                        {
                            ValidateMovement("second motion");
                            Advance(Phase.ObserveSecondHover, now);
                        }
                        break;

                    case Phase.ObserveSecondHover:
                        if (targetSourceTime >= 8.05)
                        {
                            ValidateIdle("Pose B hover");
                            ValidateRootAt(
                                configuration.TestOrigin +
                                new Vector3(-0.22f, -0.10f, 0.22f),
                                0.002f,
                                "Pose B hover");
                            Advance(Phase.ObserveReturn, now);
                        }
                        break;

                    case Phase.ObserveReturn:
                        if (targetSourceTime >= 9.80)
                        {
                            ValidateMovement("return");
                            Advance(Phase.ObserveFinalHold, now);
                        }
                        break;

                    case Phase.ObserveFinalHold:
                        if (targetSourceTime >= 11.85)
                        {
                            ValidateIdle("final hold");
                            ValidateRootAt(configuration.TestOrigin, 0.002f, "final hold");
                            Advance(Phase.ObserveLoop, now);
                        }
                        break;

                    case Phase.ObserveLoop:
                        if (targetSourceTime >= 12.15)
                        {
                            ValidateIdle("12/0 cycle boundary");
                            Require(earlyTransitionPeak < 0.95f,
                                "12/0 cycle boundary produced a maximum-RPM pulse.");
                            host.StopSource();
                            Advance(Phase.WaitStale, now);
                        }
                        break;

                    case Phase.WaitStale:
                        if (elapsed >= 0.85)
                        {
                            Require(host.SourceStatus == DataSourceStatus.Stopped &&
                                    driver.LastFailureReason ==
                                    RenderSampleFailureReason.Stale,
                                "ROV public chain did not enter stale after Source Stop.");
                            ValidateIdle("stale hold");
                            host.RestartSource();
                            Advance(Phase.WaitRestart, now);
                        }
                        break;

                    case Phase.WaitRestart:
                        if (elapsed >= 0.70 && IsHealthy())
                        {
                            ValidatePairsAndBounds();
                            Require(earlyTransitionPeak < 0.95f,
                                "Epoch restart produced a maximum-RPM pulse.");
                            authority.Mode = VehiclePoseControlMode.Demo;
                            Advance(Phase.WaitDemo, now);
                        }
                        else if (elapsed > 3.0)
                        {
                            throw new InvalidOperationException(
                                "ROV public chain did not recover after restart.");
                        }
                        break;

                    case Phase.WaitDemo:
                        if (elapsed >= 0.60)
                        {
                            Require(authority.DemoOwnsControl &&
                                    demo.DrivesRov &&
                                    !driver.OwnsControl,
                                "Demo Authority did not exclusively own the ROV.");
                            Require(earlyTransitionPeak < 0.95f,
                                "PublicData-to-Demo switch produced a maximum-RPM pulse.");
                            ValidatePairsAndBounds();
                            authority.Mode = VehiclePoseControlMode.PublicData;
                            Advance(Phase.WaitPublic, now);
                        }
                        break;

                    case Phase.WaitPublic:
                        if (elapsed >= 0.50 && IsHealthy())
                        {
                            Require(earlyTransitionPeak < 0.95f,
                                "Demo-to-PublicData switch produced a maximum-RPM pulse.");
                            ValidatePairsAndBounds();
                            driver.enabled = false;
                            Advance(Phase.WaitDriverDisabled, now);
                        }
                        break;

                    case Phase.WaitDriverDisabled:
                        if (elapsed >= 0.45)
                        {
                            Require(!driver.enabled &&
                                    authority.PublicDataOwnsControl &&
                                    !demo.DrivesRov,
                                "Driver Disable changed Authority ownership.");
                            ValidateIdle("Driver Disable hold");
                            driver.enabled = true;
                            Advance(Phase.WaitDriverRecovery, now);
                        }
                        break;

                    case Phase.WaitDriverRecovery:
                        if (elapsed >= 0.70 && IsHealthy())
                        {
                            ValidateDiagnosticSafetyPaths();
                            coordinator.enabled = false;
                            Advance(Phase.WaitDisableRestore, now);
                        }
                        else if (elapsed > 3.0)
                        {
                            throw new InvalidOperationException(
                                "ROV Driver did not recover after re-enable.");
                        }
                        break;

                    case Phase.WaitDisableRestore:
                        if (elapsed >= 0.10)
                        {
                            ValidateOriginalRpmRestored();
                            Vector3 rootBeforeEnable = rov.position;
                            coordinator.enabled = true;
                            Require(coordinator.RuntimeInitialized &&
                                    coordinator.OriginalRpmCached &&
                                    !coordinator.HasPreviousPose,
                                "Coordinator re-enable did not reset runtime baseline.");
                            ValidateIdle("Coordinator re-enable");
                            Require(Near(rov.position, rootBeforeEnable, 0.00001f),
                                "Coordinator re-enable wrote the ROV root.");
                            Advance(Phase.WaitFinal, now);
                        }
                        break;

                    case Phase.WaitFinal:
                        if (elapsed >= 0.25)
                        {
                            ValidateFinal();
                            SessionState.SetBool(PassedKey, true);
                            SessionState.SetString(
                                DetailKey,
                                "M1-C1 group-level non-negative VISUAL_ONLY RPM, full-cycle " +
                                "motion/hover/stale/recovery/Authority/Disable safety, unique " +
                                "Spinner rotation ownership and AUV/USV isolation passed.");
                            EditorApplication.ExitPlaymode();
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
            rov = RequireGameObject("ROV_Box_Seabed").transform;
            model = RequireDescendant(rov, "ROV_FineModel_V1_Imported");
            auv = RequireGameObject("AUV_Yellow_Underwater").transform;
            usv = RequireGameObject("USV_Blue_Surface").transform;
            coordinator = rov.GetComponent<RovThrusterVisualCoordinator>();
            driver = RequireGameObject("ROV_PublicPoseDriver")
                .GetComponent<VehiclePoseDriver>();
            host = driver == null ? null : driver.RuntimeHost;
            configuration = driver == null ? null : driver.IntegrationConfiguration;
            authority = rov.GetComponent<VehiclePoseControlAuthority>();
            demo = UnityEngine.Object.FindAnyObjectByType<DemoMotionController>();
            auvAuthority = auv.GetComponent<VehiclePoseControlAuthority>();
            usvAuthority = usv.GetComponent<VehiclePoseControlAuthority>();
            auvDriver = RequireGameObject("AUV_PublicPoseDriver")
                .GetComponent<VehiclePoseDriver>();
            usvDriver = RequireGameObject("USV_PublicPoseDriver")
                .GetComponent<VehiclePoseDriver>();
            usvActuatorCoordinator = usv.GetComponent<UsvActuatorVisualCoordinator>();

            Require(coordinator != null &&
                    rov.GetComponents<RovThrusterVisualCoordinator>().Length == 1 &&
                    driver != null &&
                    host != null &&
                    configuration != null &&
                    authority != null &&
                    demo != null &&
                    auvAuthority != null &&
                    usvAuthority != null &&
                    auvDriver != null &&
                    usvDriver != null &&
                    usvActuatorCoordinator != null,
                "Required M1-C1 runtime references are missing.");
            ValidateOwnershipForDiagnostics(
                rov,
                auv,
                usv,
                coordinator,
                usvActuatorCoordinator);
            spinners = OrderedSpinners(coordinator);
            Require(spinners.All(item => item != null) &&
                    spinners.Distinct().Count() == 6,
                "M1-C1 six Spinner references are not unique.");
            originalRpm = new[] { 720f, 720f, 680f, 680f, 700f, 700f };
            spinnerLocalPositions = spinners.Select(item => item.transform.localPosition).ToArray();
            spinnerLocalScales = spinners.Select(item => item.transform.localScale).ToArray();
            spinnerInitialRotations = spinners.Select(item => item.transform.localRotation).ToArray();
            spinnerRotationObserved = new bool[spinners.Length];
            modelLocalPosition = model.localPosition;
            modelLocalRotation = model.localRotation;
            modelLocalScale = model.localScale;
            rovLocalScale = rov.localScale;
            auvInitialPosition = auv.position;
            usvInitialPosition = usv.position;
            auvSpinners = auv.GetComponentsInChildren<PropellerSpinner>(true);
            usvSpinners = usv.GetComponentsInChildren<PropellerSpinner>(true);
            auvInitialRpm = auvSpinners.Select(item => item.rpm).ToArray();
            usvInitialRpm = usvSpinners.Select(item => item.rpm).ToArray();
            auvSpinnerParents = auvSpinners.Select(item => item.transform.parent).ToArray();
            usvSpinnerParents = usvSpinners.Select(item => item.transform.parent).ToArray();
            auvSpinnerLocalPositions =
                auvSpinners.Select(item => item.transform.localPosition).ToArray();
            auvSpinnerLocalScales =
                auvSpinners.Select(item => item.transform.localScale).ToArray();
            usvSpinnerLocalPositions =
                usvSpinners.Select(item => item.transform.localPosition).ToArray();
            usvSpinnerLocalScales =
                usvSpinners.Select(item => item.transform.localScale).ToArray();
            usvRpmVariationObserved = false;
            ValidateUsvSpinnerContract();
            ValidateForeignVehicleStructure();
            Require(configuration.GeneratorKind ==
                    DeterministicVehicleStateGeneratorKind.RovDiagnosticTrajectory,
                "ROV is not using the M1-B diagnostic trajectory.");
        }

        private static void ValidateInitialRuntimeState()
        {
            Require(coordinator.enabled &&
                    coordinator.RuntimeInitialized &&
                    coordinator.OriginalRpmCached,
                "Coordinator did not initialize and cache original Scene RPM.");
            ValidateIdle("startup");
            ValidatePairsAndBounds();
            ValidateProtectedTransforms();
        }

        private static void ValidateMovement(string label)
        {
            Require(IsHealthy(), "ROV public chain was unhealthy during " + label + ".");
            ValidatePairsAndBounds();
            Require(spinners[0].rpm > IdleTolerance &&
                    spinners[2].rpm > IdleTolerance &&
                    spinners[4].rpm > IdleTolerance,
                "Surge, Heave and Sway groups did not all respond during " + label + ".");
            Require(spinnerRotationObserved.All(value => value),
                "Not all six ROV Spinners rotated during " + label + ".");
            ValidateProtectedTransforms();
        }

        private static void ValidateIdle(string label)
        {
            ValidatePairsAndBounds();
            Require(spinners.All(item => item.rpm <= IdleTolerance),
                "ROV visual RPM did not decay to idle during " + label + ".");
            ValidateProtectedTransforms();
        }

        private static void ValidatePairsAndBounds()
        {
            Require(Mathf.Abs(spinners[0].rpm - spinners[1].rpm) <= PairTolerance &&
                    Mathf.Abs(spinners[2].rpm - spinners[3].rpm) <= PairTolerance &&
                    Mathf.Abs(spinners[4].rpm - spinners[5].rpm) <= PairTolerance,
                "A ROV visual thruster group became differential.");
            Require(IsFiniteBounded(spinners[0].rpm, coordinator.SurgeMaxVisualRpm) &&
                    IsFiniteBounded(spinners[1].rpm, coordinator.SurgeMaxVisualRpm) &&
                    IsFiniteBounded(spinners[2].rpm, coordinator.HeaveMaxVisualRpm) &&
                    IsFiniteBounded(spinners[3].rpm, coordinator.HeaveMaxVisualRpm) &&
                    IsFiniteBounded(spinners[4].rpm, coordinator.SwayMaxVisualRpm) &&
                    IsFiniteBounded(spinners[5].rpm, coordinator.SwayMaxVisualRpm),
                "A ROV visual RPM was negative, non-finite, or above its VisualOnly maximum.");
        }

        private static void ValidateDiagnosticSafetyPaths()
        {
            Vector3 origin = rov.position;
            Quaternion rotation = rov.rotation;
            Require(coordinator.TryEvaluatePoseForDiagnostics(
                        origin,
                        rotation,
                        origin + Vector3.one,
                        rotation,
                        0.1f,
                        out Vector3 teleportTarget,
                        out bool discontinuity) &&
                    discontinuity &&
                    Near(teleportTarget, Vector3.zero, 0.00001f),
                "Explicit teleport diagnostics did not reset to idle.");
            Require(!coordinator.TryEvaluatePoseForDiagnostics(
                        origin,
                        rotation,
                        origin,
                        rotation,
                        float.NaN,
                        out Vector3 invalidTarget,
                        out _) &&
                    Near(invalidTarget, Vector3.zero, 0.00001f),
                "Invalid deltaTime diagnostics produced an unsafe target.");
            ValidatePairsAndBounds();
        }

        private static void ValidateOriginalRpmRestored()
        {
            for (int index = 0; index < spinners.Length; index++)
            {
                Require(Mathf.Abs(spinners[index].rpm - originalRpm[index]) <= 0.0001f,
                    "Coordinator Disable did not restore original Scene RPM.");
            }
        }

        private static void ValidateRootAt(
            Vector3 expected,
            float tolerance,
            string label)
        {
            Require(Near(rov.position, expected, tolerance),
                "ROV root presentation is incorrect during " + label + ".");
        }

        private static void ValidateProtectedTransforms()
        {
            Require(Near(rov.localScale, rovLocalScale, 0.000001f),
                "Coordinator changed the ROV root scale.");
            Require(Near(model.localPosition, modelLocalPosition, 0.000001f) &&
                    Near(model.localRotation, modelLocalRotation, 0.00001f) &&
                    Near(model.localScale, modelLocalScale, 0.000001f),
                "Coordinator changed the imported model local Transform.");
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
                    "Coordinator changed a RotatingPart local position or scale.");
            }
        }

        private static void ValidateFinal()
        {
            Require(errorCount == 0,
                "Captured " + errorCount + " Error/Exception/Assert messages.");
            Require(IsHealthy(), "ROV public chain did not finish healthy.");
            Require(coordinator.enabled && coordinator.RuntimeInitialized,
                "Coordinator did not finish enabled and initialized.");
            ValidatePairsAndBounds();
            ValidateProtectedTransforms();
            Require(spinnerRotationObserved.All(value => value),
                "Not all six ROV Spinners produced local rotation.");
            Require(auvAuthority.PublicDataOwnsControl &&
                    usvAuthority.PublicDataOwnsControl &&
                    auvDriver.OwnsControl &&
                    usvDriver.OwnsControl &&
                    (auv.position - auvInitialPosition).sqrMagnitude > 0.000001f &&
                    (usv.position - usvInitialPosition).sqrMagnitude > 0.000001f,
                "AUV or USV public root/Authority was affected by M1-C1.");
            Require(auv.GetComponent<RovThrusterVisualCoordinator>() == null &&
                    usv.GetComponent<RovThrusterVisualCoordinator>() == null,
                "M1-C1 Coordinator leaked to AUV or USV.");
            ValidateRovCoordinatorReferenceOwnership();
            ValidateAuvSpinnerContract();
            ValidateUsvSpinnerContract();
            ValidateForeignVehicleStructure();
            Require(usvRpmVariationObserved,
                "Authorized M2-D USV Spinner RPM did not vary during M1-C validation.");
        }

        private static void ObserveRuntime(double now)
        {
            ValidatePairsAndBounds();
            ValidateRovCoordinatorReferenceOwnership();
            ValidateUsvSpinnerContract();
            ValidateForeignVehicleStructure();
            for (int index = 0; index < spinners.Length; index++)
            {
                if (!Near(
                        spinners[index].transform.localRotation,
                        spinnerInitialRotations[index],
                        0.0001f))
                {
                    spinnerRotationObserved[index] = true;
                }
            }

            if (now - phaseStartedAt <= 0.15)
            {
                earlyTransitionPeak = Mathf.Max(
                    earlyTransitionPeak,
                    spinners[0].rpm / coordinator.SurgeMaxVisualRpm,
                    spinners[2].rpm / coordinator.HeaveMaxVisualRpm,
                    spinners[4].rpm / coordinator.SwayMaxVisualRpm);
            }
        }

        public static void ValidateOwnershipForDiagnostics(
            Transform rovRoot,
            Transform auvRoot,
            Transform usvRoot,
            RovThrusterVisualCoordinator rovValue,
            UsvActuatorVisualCoordinator usvValue)
        {
            Require(rovRoot != null && auvRoot != null && usvRoot != null,
                "ROV/AUV/USV ownership roots are required.");
            Require(rovValue != null &&
                    rovValue.transform == rovRoot &&
                    rovRoot.GetComponents<RovThrusterVisualCoordinator>().Length == 1,
                "ROV visual thruster Coordinator ownership is not unique on the ROV root.");

            PropellerSpinner[] rovReferences = OrderedSpinners(rovValue);
            PropellerSpinner[] rovHierarchy =
                rovRoot.GetComponentsInChildren<PropellerSpinner>(true);
            PropellerSpinner[] auvHierarchy =
                auvRoot.GetComponentsInChildren<PropellerSpinner>(true);
            PropellerSpinner[] usvHierarchy =
                usvRoot.GetComponentsInChildren<PropellerSpinner>(true);
            Require(!rovReferences.Intersect(auvHierarchy).Any(),
                "ROV Coordinator references an AUV Spinner.");
            Require(!rovReferences.Intersect(usvHierarchy).Any(),
                "ROV Coordinator references a USV Spinner.");
            Require(rovReferences.All(item => item != null) &&
                    rovReferences.Distinct().Count() == 6 &&
                    rovHierarchy.Length == 6 &&
                    rovReferences.All(rovHierarchy.Contains) &&
                    rovHierarchy.All(rovReferences.Contains) &&
                    rovReferences.All(item => item.transform.IsChildOf(rovRoot)),
                "ROV Coordinator must reference exactly six unique ROV Spinner descendants.");

            Require(usvValue != null &&
                    usvValue.transform == usvRoot &&
                    usvRoot.GetComponents<UsvActuatorVisualCoordinator>().Length == 1 &&
                    usvValue.enabled &&
                    usvValue.Mode ==
                        UsvActuatorVisualMode.DemoAndLocalDiagnosticPublicData,
                "USV Spinners do not have the authorized enabled M2-D Coordinator.");
            VehiclePoseControlAuthority expectedAuthority =
                usvRoot.GetComponent<VehiclePoseControlAuthority>();
            Require(usvValue.BusinessRoot == usvRoot &&
                    usvValue.PoseDriver != null &&
                    usvValue.ControlAuthority == expectedAuthority &&
                    usvValue.PoseDriver.TargetRoot == usvRoot &&
                    usvValue.PoseDriver.ControlAuthority == expectedAuthority,
                "USV M2-D Coordinator business-root, Driver or Authority ownership is invalid.");
            PropellerSpinner[] usvOwned =
            {
                usvValue.PortVisualThruster,
                usvValue.StarboardVisualThruster
            };
            Require(usvHierarchy.Length == 2 &&
                    usvOwned.All(item => item != null) &&
                    usvOwned.Distinct().Count() == 2 &&
                    usvOwned.All(usvHierarchy.Contains) &&
                    usvHierarchy.All(usvOwned.Contains),
                "USV M2-D Coordinator does not own exactly the two USV Spinners.");
        }

        private static void ValidateRovCoordinatorReferenceOwnership()
        {
            ValidateOwnershipForDiagnostics(
                rov,
                auv,
                usv,
                coordinator,
                usvActuatorCoordinator);
        }

        private static void ValidateAuvSpinnerContract()
        {
            Require(Enumerable.Range(0, auvSpinners.Length).All(index =>
                    Mathf.Abs(auvSpinners[index].rpm - auvInitialRpm[index]) <=
                    0.0001f),
                "AUV Spinner RPM changed without an authorized AUV writer.");
        }

        private static void ValidateUsvSpinnerContract()
        {
            Require(usvActuatorCoordinator != null &&
                    usvActuatorCoordinator.enabled &&
                    usvActuatorCoordinator.Mode ==
                        UsvActuatorVisualMode.DemoAndLocalDiagnosticPublicData,
                "USV Spinner RPM changed without the authorized M2-D Coordinator.");
            for (int index = 0; index < usvSpinners.Length; index++)
            {
                float value = usvSpinners[index].rpm;
                Require(!float.IsNaN(value) && !float.IsInfinity(value),
                    "USV Spinner RPM is non-finite.");
                Require(value >= 0f,
                    "USV Spinner RPM is negative.");
                Require(value <= usvActuatorCoordinator.MaxVisualRpm + 0.0001f,
                    "USV Spinner RPM is outside the M2-D visual range.");
                if (Mathf.Abs(value - usvInitialRpm[index]) > 0.0001f)
                {
                    usvRpmVariationObserved = true;
                }
            }
        }

        private static void ValidateForeignVehicleStructure()
        {
            Require(auv.GetComponentsInChildren<PropellerSpinner>(true).Length ==
                        auvSpinners.Length &&
                    usv.GetComponentsInChildren<PropellerSpinner>(true).Length ==
                        usvSpinners.Length,
                "Foreign vehicle Spinner hierarchy changed during M1-C validation.");
            for (int index = 0; index < auvSpinners.Length; index++)
            {
                Transform value = auvSpinners[index].transform;
                Require(value.parent == auvSpinnerParents[index] &&
                        Near(value.localPosition, auvSpinnerLocalPositions[index], 0.000001f) &&
                        Near(value.localScale, auvSpinnerLocalScales[index], 0.000001f),
                    "AUV Spinner hierarchy or local position/scale changed during M1-C validation.");
            }
            for (int index = 0; index < usvSpinners.Length; index++)
            {
                Transform value = usvSpinners[index].transform;
                Require(value.parent == usvSpinnerParents[index] &&
                        Near(value.localPosition, usvSpinnerLocalPositions[index], 0.000001f) &&
                        Near(value.localScale, usvSpinnerLocalScales[index], 0.000001f),
                    "USV Spinner hierarchy or local position/scale changed during M1-C validation.");
            }
        }

        private static bool IsHealthy()
        {
            return authority.PublicDataOwnsControl &&
                   driver.enabled &&
                   driver.OwnsControl &&
                   !demo.DrivesRov &&
                   driver.LastFailureReason == RenderSampleFailureReason.None &&
                   (driver.LastSampleMode == RenderSampleMode.Exact ||
                    driver.LastSampleMode == RenderSampleMode.Interpolated ||
                    driver.LastSampleMode == RenderSampleMode.HeldLatest);
        }

        private static void Advance(Phase next, double now)
        {
            phase = next;
            phaseStartedAt = now;
            earlyTransitionPeak = 0f;
        }

        private static PropellerSpinner[] OrderedSpinners(
            RovThrusterVisualCoordinator value)
        {
            return new[]
            {
                value.SurgeVisualRightSpinner,
                value.SurgeVisualLeftSpinner,
                value.HeaveVisualRightSpinner,
                value.HeaveVisualLeftSpinner,
                value.SwayFrontSpinner,
                value.SwayRearSpinner
            };
        }

        private static void Finish()
        {
            bool passed = SessionState.GetBool(PassedKey, false);
            string detail = SessionState.GetString(DetailKey, "No result.");
            string outputDirectory = EvidenceDirectory();
            Directory.CreateDirectory(outputDirectory);
            string status = passed
                ? "M1C_ROV_THRUSTER_PLAY_MODE_VALIDATION_PASS"
                : "M1C_ROV_THRUSTER_PLAY_MODE_VALIDATION_FAIL";
            string timestamp = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
            string json =
                "{\n" +
                "  \"status\": \"" + status + "\",\n" +
                "  \"generatedAtIso8601\": \"" + Escape(timestamp) + "\",\n" +
                "  \"passed\": " + (passed ? "true" : "false") + ",\n" +
                "  \"detail\": \"" + Escape(detail) + "\"\n" +
                "}\n";
            File.WriteAllText(
                Path.Combine(outputDirectory, "m1c1_rov_thruster_playmode_report.json"),
                json,
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(outputDirectory, "m1c1_rov_thruster_playmode_report.md"),
                "# M1-C1 ROV Visual Thruster Play Mode Verification\n\n" +
                "- Status: `" + status + "`\n" +
                "- Detail: " + detail + "\n",
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
            string configured = Environment.GetEnvironmentVariable("M1C1_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.GetFullPath(configured);
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, "..", "..", "M1C1_Validation"));
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
            Require(matches.Length == 1,
                "Expected exactly one descendant named " + name + ".");
            return matches[0];
        }

        private static bool IsFiniteBounded(float value, float maximum)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value) &&
                   value >= 0f &&
                   value <= maximum + 0.0001f;
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
