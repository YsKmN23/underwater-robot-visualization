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
    public static class RovRootPoseN6BPlayModeVerifier
    {
        private const string ActiveKey = "N6B.RovPlayMode.Active";
        private const string PassedKey = "N6B.RovPlayMode.Passed";
        private const string DetailKey = "N6B.RovPlayMode.Detail";
        private const string BatchKey = "N6B.RovPlayMode.Batch";
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";

        private enum Phase
        {
            WaitHealthy,
            ObserveInitialHold,
            ObserveFirstMotion,
            CaptureFirstHover,
            ObserveFirstHover,
            ObserveSecondMotion,
            CaptureSecondHover,
            ObserveSecondHover,
            ObserveReturn,
            CaptureFinalHold,
            ObserveFinalHold,
            ObserveLoopBoundary,
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

        private static Transform rov;
        private static Transform model;
        private static Transform auv;
        private static Transform usv;
        private static VehiclePoseDriver driver;
        private static VehicleDataRuntimeHost host;
        private static VehiclePoseIntegrationConfiguration configuration;
        private static VehiclePoseControlAuthority authority;
        private static DemoMotionController demo;
        private static PropellerSpinner[] spinners;
        private static Vector3 modelLocalPosition;
        private static Quaternion modelLocalRotation;
        private static Vector3 modelLocalScale;
        private static Vector3[] spinnerLocalPositions;
        private static Quaternion[] spinnerInitialRotations;
        private static Vector3[] spinnerLocalScales;
        private static Vector3 rovMotionBaseline;
        private static Quaternion rovRotationBaseline;
        private static Vector3 firstHoverPosition;
        private static Quaternion firstHoverRotation;
        private static Vector3 secondHoverPosition;
        private static Quaternion secondHoverRotation;
        private static Vector3 finalHoldPosition;
        private static Quaternion finalHoldRotation;
        private static Vector3 auvBaseline;
        private static Vector3 usvBaseline;
        private static Vector3 staleHoldPosition;
        private static Vector3 demoBaseline;
        private static Vector3 disabledBaseline;
        private static ulong initialEpoch;

        static RovRootPoseN6BPlayModeVerifier()
        {
            if (SessionState.GetBool(ActiveKey, false))
            {
                Subscribe();
            }
        }

        [MenuItem("Tools/Underwater Demo/N6-B/Run ROV Play Mode Verification")]
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
                    "N6-B ROV Play Mode verification is already active.");
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
                    referencesBound = true;
                }

                ObserveSpinnerRotation();
                double now = Time.realtimeSinceStartupAsDouble;
                double elapsed = now - phaseStartedAt;
                double targetSourceTime = host == null
                    ? 0.0
                    : host.GetTargetSourceTimestamp(
                        now,
                        configuration.RenderDelaySeconds);

                switch (phase)
                {
                    case Phase.WaitHealthy:
                        if (IsHealthy())
                        {
                            CaptureInitialHealthyState();
                            Advance(Phase.ObserveInitialHold, now);
                        }
                        else if (now - enteredAt > 4.0)
                        {
                            throw new InvalidOperationException(
                                "ROV Driver did not become healthy within four seconds.");
                        }
                        break;

                    case Phase.ObserveInitialHold:
                        if (targetSourceTime >= 0.60)
                        {
                            ValidateInitialHold();
                            Advance(Phase.ObserveFirstMotion, now);
                        }
                        break;

                    case Phase.ObserveFirstMotion:
                        if (targetSourceTime >= 1.80)
                        {
                            ValidateFirstMotion();
                            Advance(Phase.CaptureFirstHover, now);
                        }
                        break;

                    case Phase.CaptureFirstHover:
                        if (targetSourceTime >= 3.15)
                        {
                            CaptureFirstHover();
                            Advance(Phase.ObserveFirstHover, now);
                        }
                        break;

                    case Phase.ObserveFirstHover:
                        if (targetSourceTime >= 4.05)
                        {
                            ValidateFirstHover();
                            Advance(Phase.ObserveSecondMotion, now);
                        }
                        break;

                    case Phase.ObserveSecondMotion:
                        if (targetSourceTime >= 5.60)
                        {
                            ValidateSecondMotion();
                            Advance(Phase.CaptureSecondHover, now);
                        }
                        break;

                    case Phase.CaptureSecondHover:
                        if (targetSourceTime >= 7.15)
                        {
                            CaptureSecondHover();
                            Advance(Phase.ObserveSecondHover, now);
                        }
                        break;

                    case Phase.ObserveSecondHover:
                        if (targetSourceTime >= 8.05)
                        {
                            ValidateSecondHover();
                            Advance(Phase.ObserveReturn, now);
                        }
                        break;

                    case Phase.ObserveReturn:
                        if (targetSourceTime >= 9.80)
                        {
                            ValidateReturn();
                            Advance(Phase.CaptureFinalHold, now);
                        }
                        break;

                    case Phase.CaptureFinalHold:
                        if (targetSourceTime >= 11.40)
                        {
                            CaptureFinalHold();
                            Advance(Phase.ObserveFinalHold, now);
                        }
                        break;

                    case Phase.ObserveFinalHold:
                        if (targetSourceTime >= 11.85)
                        {
                            ValidateFinalHold();
                            Advance(Phase.ObserveLoopBoundary, now);
                        }
                        break;

                    case Phase.ObserveLoopBoundary:
                        if (targetSourceTime >= 12.15)
                        {
                            ValidateLoopBoundary();
                            host.StopSource();
                            Advance(Phase.WaitForHoldCapture, now);
                        }
                        break;

                    case Phase.WaitForHoldCapture:
                        if (elapsed >= 0.35)
                        {
                            staleHoldPosition = rov.position;
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
                            ValidateEpochRecovery();
                            authority.Mode = VehiclePoseControlMode.Demo;
                            demoBaseline = rov.position;
                            Advance(Phase.WaitForDemo, now);
                        }
                        else if (elapsed > 3.0)
                        {
                            throw new InvalidOperationException(
                                "ROV Driver did not recover after epoch restart.");
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
                            ValidatePublicOwnership();
                            driver.enabled = false;
                            disabledBaseline = rov.position;
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
                                "M1-B initial/final holds, Pose A/B hovers, three smooth motion segments, " +
                                "12/0 cycle continuity, public root presentation, stale hold, epoch recovery, " +
                                "authority switching, Driver disable, six local spinners and AUV/USV isolation passed.");
                            EditorApplication.ExitPlaymode();
                        }
                        else if (elapsed > 3.0)
                        {
                            throw new InvalidOperationException(
                                "ROV Driver did not recover after being re-enabled.");
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
            driver = RequireGameObject("ROV_PublicPoseDriver")
                .GetComponent<VehiclePoseDriver>();
            host = driver == null ? null : driver.RuntimeHost;
            configuration = driver == null ? null : driver.IntegrationConfiguration;
            authority = rov.GetComponent<VehiclePoseControlAuthority>();
            demo = UnityEngine.Object.FindAnyObjectByType<DemoMotionController>();
            usv = demo == null ? null : demo.usv;
            spinners = rov.GetComponentsInChildren<PropellerSpinner>(true);

            Require(driver != null &&
                    host != null &&
                    configuration != null &&
                    authority != null &&
                    demo != null &&
                    usv != null &&
                    spinners.Length == 6,
                "Required N6-B runtime components are missing.");
            Require(ReferenceEquals(host.IntegrationConfiguration, configuration) &&
                    ReferenceEquals(driver.IntegrationConfiguration, configuration) &&
                    configuration.GeneratorKind ==
                    DeterministicVehicleStateGeneratorKind.RovDiagnosticTrajectory,
                "ROV Host and Driver do not share the diagnostic configuration.");
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

        private static void CaptureInitialHealthyState()
        {
            Require(
                Vector3.Dot(rov.TransformDirection(Vector3.right), Vector3.forward) > 0.999f &&
                Vector3.Dot(rov.TransformDirection(Vector3.up), Vector3.up) > 0.999f &&
                Vector3.Dot(rov.TransformDirection(Vector3.back), Vector3.right) > 0.999f,
                "Zero-pose ROV model axes are not Forward +Z, Up +Y, Right +X.");
            Require((rov.position - configuration.TestOrigin).sqrMagnitude < 1e-5f,
                "ROV initial identity-hold position differs from its diagnostic origin.");
            Require(host.TryGetActiveEpoch(out initialEpoch),
                "ROV Host has no active initial epoch.");

            modelLocalPosition = model.localPosition;
            modelLocalRotation = model.localRotation;
            modelLocalScale = model.localScale;
            spinnerLocalPositions = spinners.Select(item => item.transform.localPosition).ToArray();
            spinnerInitialRotations = spinners.Select(item => item.transform.localRotation).ToArray();
            spinnerLocalScales = spinners.Select(item => item.transform.localScale).ToArray();
            rovMotionBaseline = rov.position;
            rovRotationBaseline = rov.rotation;
            auvBaseline = auv.position;
            usvBaseline = usv.position;
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

        private static void ValidateInitialHold()
        {
            Require(IsHealthy(), "ROV Driver lost PublicData ownership during initial hold.");
            Require(
                Near(rov.position, rovMotionBaseline, 1e-5f) &&
                Near(rov.rotation, rovRotationBaseline, 1e-5f),
                "ROV root moved during the M1-B initial hold.");
            ValidateProtectedLocals();
        }

        private static void ValidateFirstMotion()
        {
            Require(errorCount == 0, "Runtime Console reported an error before live validation.");
            Require(IsHealthy(), "ROV Driver lost PublicData ownership during live motion.");
            Require((rov.position - rovMotionBaseline).sqrMagnitude > 1e-5f,
                "ROV first diagnostic segment did not move the root.");
            Require(!Near(rov.rotation, rovRotationBaseline, 1e-5f),
                "ROV first diagnostic segment did not change orientation.");
            Require(driver.LastSampleMode == RenderSampleMode.Interpolated ||
                    driver.LastSampleMode == RenderSampleMode.Exact,
                "ROV did not observe an exact or interpolated live sample.");
            Require(spinnerRotationObserved,
                "No ROV PropellerSpinner local rotation was observed.");
            Require((auv.position - auvBaseline).sqrMagnitude > 1e-6f,
                "AUV public pose stopped while ROV was active.");
            Require((usv.position - usvBaseline).sqrMagnitude > 1e-8f,
                "USV Demo motion stopped while ROV was active.");
            ValidateProtectedLocals();
        }

        private static void CaptureFirstHover()
        {
            Vector3 expected =
                configuration.TestOrigin + new Vector3(0.30f, 0.12f, -0.18f);
            Require(IsHealthy(), "ROV Driver was unhealthy at Pose A.");
            Require(Near(rov.position, expected, 0.002f),
                "ROV root did not reach M1-B Pose A.");
            Require(!Near(rov.rotation, rovRotationBaseline, 1e-5f),
                "ROV Pose A orientation remained at identity.");
            firstHoverPosition = rov.position;
            firstHoverRotation = rov.rotation;
        }

        private static void ValidateFirstHover()
        {
            Require(IsHealthy(), "ROV Driver was unhealthy during the first hover.");
            Require(
                Near(rov.position, firstHoverPosition, 1e-5f) &&
                Near(rov.rotation, firstHoverRotation, 1e-5f),
                "ROV root did not remain fixed during the first hover.");
            ValidateProtectedLocals();
        }

        private static void ValidateSecondMotion()
        {
            Require(IsHealthy(), "ROV Driver was unhealthy during the second motion.");
            Require((rov.position - firstHoverPosition).sqrMagnitude > 1e-4f,
                "ROV second diagnostic segment did not leave Pose A.");
            Require(!Near(rov.rotation, firstHoverRotation, 1e-5f),
                "ROV second diagnostic segment did not change orientation.");
        }

        private static void CaptureSecondHover()
        {
            Vector3 expected =
                configuration.TestOrigin + new Vector3(-0.22f, -0.10f, 0.22f);
            Require(IsHealthy(), "ROV Driver was unhealthy at Pose B.");
            Require(Near(rov.position, expected, 0.002f),
                "ROV root did not reach M1-B Pose B.");
            Require(!Near(rov.rotation, firstHoverRotation, 1e-5f),
                "ROV Pose B orientation did not differ from Pose A.");
            secondHoverPosition = rov.position;
            secondHoverRotation = rov.rotation;
        }

        private static void ValidateSecondHover()
        {
            Require(IsHealthy(), "ROV Driver was unhealthy during the second hover.");
            Require(
                Near(rov.position, secondHoverPosition, 1e-5f) &&
                Near(rov.rotation, secondHoverRotation, 1e-5f),
                "ROV root did not remain fixed during the second hover.");
            ValidateProtectedLocals();
        }

        private static void ValidateReturn()
        {
            Require(IsHealthy(), "ROV Driver was unhealthy during the return segment.");
            Require((rov.position - secondHoverPosition).sqrMagnitude > 1e-4f,
                "ROV return segment did not leave Pose B.");
            Require(
                (rov.position - configuration.TestOrigin).sqrMagnitude <
                (secondHoverPosition - configuration.TestOrigin).sqrMagnitude,
                "ROV return segment did not approach the diagnostic origin.");
            Require(!Near(rov.rotation, secondHoverRotation, 1e-5f),
                "ROV return segment did not change orientation.");
        }

        private static void CaptureFinalHold()
        {
            Require(IsHealthy(), "ROV Driver was unhealthy at the final hold.");
            Require(
                Near(rov.position, configuration.TestOrigin, 0.002f) &&
                Near(rov.rotation, rovRotationBaseline, 1e-5f),
                "ROV root did not return to its zero pose before the final hold.");
            finalHoldPosition = rov.position;
            finalHoldRotation = rov.rotation;
        }

        private static void ValidateFinalHold()
        {
            Require(IsHealthy(), "ROV Driver was unhealthy during the final hold.");
            Require(
                Near(rov.position, finalHoldPosition, 1e-5f) &&
                Near(rov.rotation, finalHoldRotation, 1e-5f),
                "ROV root moved during the final hold.");
        }

        private static void ValidateLoopBoundary()
        {
            Require(IsHealthy(), "ROV Driver was unhealthy across the cycle boundary.");
            Require(
                Near(rov.position, finalHoldPosition, 1e-5f) &&
                Near(rov.rotation, finalHoldRotation, 1e-5f),
                "ROV root jumped across the 12/0 second cycle boundary.");
            Require(spinnerRotationObserved,
                "ROV local propeller animation did not continue through the full cycle.");
            ValidateProtectedLocals();
        }

        private static void ValidateStaleHold()
        {
            Require(host.SourceStatus == DataSourceStatus.Stopped,
                "ROV source did not enter Stopped state.");
            Require(driver.LastFailureReason == RenderSampleFailureReason.Stale,
                "ROV Driver did not report stale after the configured timeout.");
            Require(Near(rov.position, staleHoldPosition, 1e-5f),
                "ROV root changed while stale data was held.");
            Require(authority.PublicDataOwnsControl && !demo.DrivesRov,
                "Demo wrote ROV while stale PublicData authority was active.");
        }

        private static void ValidateEpochRecovery()
        {
            Require(host.TryGetActiveEpoch(out ulong recoveredEpoch) &&
                    recoveredEpoch != initialEpoch,
                "ROV source epoch did not change after restart.");
            Require(IsHealthy(), "ROV Driver did not recover after epoch restart.");
        }

        private static void ValidateDemoOwnership()
        {
            Require(authority.DemoOwnsControl &&
                    demo.DrivesRov &&
                    !driver.OwnsControl,
                "Demo mode did not assign unique ROV ownership to DemoMotionController.");
            Require((rov.position - demoBaseline).sqrMagnitude > 1e-8f,
                "Original ROV Demo motion did not resume.");
            Require(rov.position.sqrMagnitude > 0.1f,
                "Authority switch reset ROV to the world origin.");
        }

        private static void ValidatePublicOwnership()
        {
            Require(IsHealthy(),
                "PublicData mode did not return unique ROV ownership to VehiclePoseDriver.");
        }

        private static void ValidateDisabledHold()
        {
            Require(!driver.enabled &&
                    !driver.OwnsControl &&
                    authority.PublicDataOwnsControl &&
                    !demo.DrivesRov,
                "Disabled Driver ownership state is incorrect.");
            Require(Near(rov.position, disabledBaseline, 1e-5f),
                "ROV root changed while the PublicData Driver was disabled.");
        }

        private static void ValidateFinal()
        {
            Require(errorCount == 0,
                "Captured " + errorCount + " runtime Console errors.");
            Require(IsHealthy(), "ROV Driver did not finish healthy.");
            Require(spinners.All(item => item.enabled),
                "A ROV PropellerSpinner was disabled.");
            Require(spinnerRotationObserved,
                "ROV local propeller animation did not continue.");
            ValidateProtectedLocals();
            VehiclePoseControlAuthority auvAuthority =
                auv.GetComponent<VehiclePoseControlAuthority>();
            VehiclePoseDriver auvDriver =
                RequireGameObject("AUV_PublicPoseDriver").GetComponent<VehiclePoseDriver>();
            Require(auvAuthority != null &&
                    auvAuthority.PublicDataOwnsControl &&
                    auvDriver != null &&
                    auvDriver.OwnsControl &&
                    auvDriver.LastFailureReason == RenderSampleFailureReason.None &&
                    !demo.DrivesAuv,
                "AUV N5 PublicData behavior regressed.");
            Require((usv.position - usvBaseline).sqrMagnitude > 1e-8f,
                "USV Demo behavior regressed.");
        }

        private static void ValidateProtectedLocals()
        {
            Require(Near(model.localPosition, modelLocalPosition, 1e-6f) &&
                    Near(model.localRotation, modelLocalRotation, 1e-5f) &&
                    Near(model.localScale, modelLocalScale, 1e-6f),
                "ROV Driver changed the imported model local Transform.");
            for (int index = 0; index < spinners.Length; index++)
            {
                Require(
                    Near(spinners[index].transform.localPosition, spinnerLocalPositions[index], 1e-6f) &&
                    Near(spinners[index].transform.localScale, spinnerLocalScales[index], 1e-6f),
                    "ROV Driver changed a spinner local position or scale.");
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
            string detail = SessionState.GetString(DetailKey, "No result.");
            string outputDirectory = EvidenceDirectory();
            Directory.CreateDirectory(outputDirectory);
            string timestamp = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
            string status = passed
                ? "N6B_ROV_PLAY_MODE_VALIDATION_PASS"
                : "N6B_ROV_PLAY_MODE_VALIDATION_FAIL";
            string json =
                "{\n" +
                "  \"status\": \"" + status + "\",\n" +
                "  \"generatedAtIso8601\": \"" + Escape(timestamp) + "\",\n" +
                "  \"passed\": " + (passed ? "true" : "false") + ",\n" +
                "  \"detail\": \"" + Escape(detail) + "\"\n" +
                "}\n";
            File.WriteAllText(
                Path.Combine(outputDirectory, "n6b_rov_playmode_report.json"),
                json,
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(outputDirectory, "n6b_rov_playmode_report.md"),
                "# N6-B ROV Play Mode Verification\n\n" +
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
            string configured = Environment.GetEnvironmentVariable("N6B_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.GetFullPath(configured);
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, "..", "..", "N6B_Validation"));
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
