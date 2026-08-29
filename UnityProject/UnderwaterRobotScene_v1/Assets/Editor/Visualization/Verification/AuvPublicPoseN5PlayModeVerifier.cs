using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnderwaterRobotScene.Visualization.Data.LocalTesting;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Sampling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    [InitializeOnLoad]
    public static class AuvPublicPoseN5PlayModeVerifier
    {
        private const string ActiveKey = "N5.PlayModeVerifier.Active";
        private const string PassedKey = "N5.PlayModeVerifier.Passed";
        private const string DetailKey = "N5.PlayModeVerifier.Detail";
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";

        private static bool subscribed;
        private static bool baselineCaptured;
        private static bool propellerRotationObserved;
        private static double enteredPlayAt;
        private static double baselineCapturedAt;
        private static int errorCount;
        private static Transform auv;
        private static Transform model;
        private static Transform tail;
        private static Transform rov;
        private static Transform usv;
        private static VehiclePoseDriver driver;
        private static VehicleDataRuntimeHost host;
        private static VehiclePoseIntegrationConfiguration integrationConfiguration;
        private static VehiclePoseControlAuthority authority;
        private static DemoMotionController demo;
        private static PropellerSpinner spinner;
        private static Vector3 auvBaseline;
        private static Vector3 modelLocalPosition;
        private static Quaternion modelLocalRotation;
        private static Vector3 modelLocalScale;
        private static Vector3 tailLocalPosition;
        private static Quaternion tailLocalRotation;
        private static Vector3 tailLocalScale;
        private static Vector3 rovBaseline;
        private static Vector3 usvBaseline;

        static AuvPublicPoseN5PlayModeVerifier()
        {
            if (SessionState.GetBool(ActiveKey, false))
            {
                Subscribe();
            }
        }

        [MenuItem("Tools/AUV Pose MVP/N5/Run Play Mode Verification")]
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
                throw new InvalidOperationException("N5 Play Mode verification is already active.");
            }

            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(PassedKey, false);
            SessionState.SetString(DetailKey, "Verification did not complete.");
            SessionState.SetBool("N5.PlayModeVerifier.Batch", batch);
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
                enteredPlayAt = Time.realtimeSinceStartupAsDouble;
                baselineCaptured = false;
                errorCount = 0;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                Finish();
            }
        }

        private static void OnEditorUpdate()
        {
            if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying)
            {
                return;
            }

            try
            {
                double elapsed = Time.realtimeSinceStartupAsDouble - enteredPlayAt;
                if (!baselineCaptured && elapsed >= 0.2)
                {
                    baselineCaptured = TryCaptureBaseline();
                    if (baselineCaptured)
                    {
                        baselineCapturedAt = Time.realtimeSinceStartupAsDouble;
                    }
                    else if (elapsed >= 4.0)
                    {
                        throw new InvalidOperationException(
                            "Driver did not reach a healthy sampling state before the Play Mode baseline timeout.");
                    }
                }

                if (baselineCaptured &&
                    !Near(tail.localRotation, tailLocalRotation, 1e-5f))
                {
                    propellerRotationObserved = true;
                }

                if (baselineCaptured &&
                    Time.realtimeSinceStartupAsDouble - baselineCapturedAt >= 1.4)
                {
                    ValidateFinal();
                    EditorApplication.ExitPlaymode();
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

        private static bool TryCaptureBaseline()
        {
            auv = RequireGameObject("AUV_Yellow_Underwater").transform;
            model = RequireDescendant(auv, "AUV_FineModel_V1_Imported");
            tail = RequireDescendant(auv, "Tail_Propeller_RotatingPart");
            driver = RequireGameObject("AUV_PublicPoseDriver").GetComponent<VehiclePoseDriver>();
            host = driver == null ? null : driver.RuntimeHost;
            integrationConfiguration = driver == null ? null : driver.IntegrationConfiguration;
            authority = auv.GetComponent<VehiclePoseControlAuthority>();
            demo = UnityEngine.Object.FindAnyObjectByType<DemoMotionController>();
            spinner = tail.GetComponent<PropellerSpinner>();
            Require(driver != null &&
                    host != null &&
                    integrationConfiguration != null &&
                    authority != null &&
                    demo != null &&
                    spinner != null,
                "Required N5 runtime components are missing.");
            Require(ReferenceEquals(host.IntegrationConfiguration, integrationConfiguration) &&
                    ReferenceEquals(driver.IntegrationConfiguration, integrationConfiguration) &&
                    integrationConfiguration.TryValidate(out _) &&
                    integrationConfiguration.GeneratorKind ==
                    DeterministicVehicleStateGeneratorKind.AuvIntegrationTrajectory,
                "Host and Driver do not share the valid N5 Integration Configuration.");
            rov = demo.rov;
            usv = demo.usv;
            Require(rov != null && usv != null, "ROV/USV Demo references are missing.");
            if (!authority.PublicDataOwnsControl ||
                !driver.OwnsControl ||
                demo.DrivesAuv ||
                driver.LastFailureReason != RenderSampleFailureReason.None)
            {
                return false;
            }
            Require(
                Vector3.Dot(auv.TransformDirection(Vector3.right), Vector3.forward) > 0.995f &&
                Vector3.Dot(auv.TransformDirection(Vector3.up), Vector3.up) > 0.995f &&
                Vector3.Dot(auv.TransformDirection(Vector3.back), Vector3.right) > 0.995f,
                "Initial AUV model axes do not match Forward +Z, Up +Y, Right +X.");

            auvBaseline = auv.position;
            modelLocalPosition = model.localPosition;
            modelLocalRotation = model.localRotation;
            modelLocalScale = model.localScale;
            tailLocalPosition = tail.localPosition;
            tailLocalRotation = tail.localRotation;
            tailLocalScale = tail.localScale;
            propellerRotationObserved = false;
            rovBaseline = rov.position;
            usvBaseline = usv.position;
            return true;
        }

        private static void ValidateFinal()
        {
            Require(errorCount == 0, "Captured " + errorCount + " runtime Console errors.");
            Require(driver.OwnsControl && !demo.DrivesAuv,
                "AUV writer ownership changed during Play Mode.");
            Require(driver.LastFailureReason == RenderSampleFailureReason.None,
                "Driver ended with " + driver.LastFailureReason + ".");
            Require(
                driver.LastSampleMode == RenderSampleMode.Interpolated ||
                driver.LastSampleMode == RenderSampleMode.Exact,
                "Unexpected final sample mode " + driver.LastSampleMode + ".");
            Require((auv.position - auvBaseline).sqrMagnitude > 1e-6f,
                "LocalTestSource did not move the AUV.");
            Require(Near(model.localPosition, modelLocalPosition, 1e-6f) &&
                    Near(model.localRotation, modelLocalRotation, 1e-5f) &&
                    Near(model.localScale, modelLocalScale, 1e-6f),
                "Driver changed the imported model local Transform.");
            Require(Near(tail.localPosition, tailLocalPosition, 1e-6f) &&
                    Near(tail.localScale, tailLocalScale, 1e-6f),
                "Driver changed tail propeller local position or scale.");
            Require(propellerRotationObserved && spinner.enabled,
                "PropellerSpinner did not continue local rotation animation.");
            Require((rov.position - rovBaseline).sqrMagnitude > 1e-8f &&
                    (usv.position - usvBaseline).sqrMagnitude > 1e-8f,
                "ROV or USV Demo motion did not continue.");

            SessionState.SetBool(PassedKey, true);
            SessionState.SetString(
                DetailKey,
                "PublicData ownership, live AUV motion, interpolation, model-child protection, " +
                "propeller local animation, and ROV/USV Demo motion passed.");
        }

        private static void Finish()
        {
            bool passed = SessionState.GetBool(PassedKey, false);
            string detail = SessionState.GetString(DetailKey, "No result.");
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outputDirectory = Path.GetFullPath(
                Path.Combine(projectRoot, "..", "..", "N5_Validation"));
            Directory.CreateDirectory(outputDirectory);
            string timestamp = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
            string json =
                "{\n" +
                "  \"status\": \"" +
                (passed ? "N5_PLAY_MODE_VALIDATION_PASS" : "N5_PLAY_MODE_VALIDATION_FAIL") +
                "\",\n" +
                "  \"generatedAtIso8601\": \"" + Escape(timestamp) + "\",\n" +
                "  \"passed\": " + (passed ? "true" : "false") + ",\n" +
                "  \"detail\": \"" + Escape(detail) + "\"\n" +
                "}\n";
            File.WriteAllText(
                Path.Combine(outputDirectory, "n5_playmode_report.json"),
                json,
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(outputDirectory, "n5_playmode_report.md"),
                "# N5 Play Mode Verification\n\n" +
                "- Status: `" +
                (passed ? "N5_PLAY_MODE_VALIDATION_PASS" : "N5_PLAY_MODE_VALIDATION_FAIL") +
                "`\n- Detail: " + detail + "\n",
                new UTF8Encoding(false));

            bool batch = SessionState.GetBool("N5.PlayModeVerifier.Batch", false);
            SessionState.SetBool(ActiveKey, false);
            SessionState.SetBool("N5.PlayModeVerifier.Batch", false);
            Unsubscribe();
            Debug.Log((passed ? "N5_PLAY_MODE_VALIDATION_PASS" : "N5_PLAY_MODE_VALIDATION_FAIL") +
                      " | " + detail);
            if (batch)
            {
                EditorApplication.Exit(passed ? 0 : 1);
            }
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                errorCount++;
            }
        }

        private static GameObject RequireGameObject(string name)
        {
            GameObject found = GameObject.Find(name);
            Require(found != null, "Missing GameObject " + name + ".");
            return found;
        }

        private static Transform RequireDescendant(Transform root, string name)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            Transform found = null;
            foreach (Transform item in transforms)
            {
                if (!string.Equals(item.name, name, StringComparison.Ordinal))
                {
                    continue;
                }

                Require(found == null, "Duplicate descendant " + name + ".");
                found = item;
            }

            Require(found != null, "Missing descendant " + name + ".");
            return found;
        }

        private static bool Near(Vector3 left, Vector3 right, float tolerance)
        {
            return (left - right).sqrMagnitude <= tolerance * tolerance;
        }

        private static bool Near(Quaternion left, Quaternion right, float tolerance)
        {
            return Mathf.Abs(Quaternion.Dot(left.normalized, right.normalized)) >= 1f - tolerance;
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
