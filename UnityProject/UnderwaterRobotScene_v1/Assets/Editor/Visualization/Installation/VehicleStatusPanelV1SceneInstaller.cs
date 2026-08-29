using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    [InitializeOnLoad]
    public static class VehicleStatusPanelV1SceneInstaller
    {
        private const string ScenePath =
            "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string ReportPathArgument =
            "-v1StatusPanelInstallerReportPath";
        private const string SmokeReportPathArgument =
            "-v1StatusPanelSmokeReportPath";
        private const string SmokeActiveKey =
            "V1.StatusPanel.Smoke.Active";
        private const string SmokePassedKey =
            "V1.StatusPanel.Smoke.Passed";
        private const string SmokeDetailKey =
            "V1.StatusPanel.Smoke.Detail";
        private const string SmokeTextKey =
            "V1.StatusPanel.Smoke.Text";
        private const string DataPanelName = "DataPanelText";
        private const string BoardName = "Scene_Status_Board";
        private const string AuvHostName = "AUV_PublicData_RuntimeHost";
        private const string RovHostName = "ROV_PublicData_RuntimeHost";
        private const string UsvHostName = "USV_PublicData_RuntimeHost";
        private const string AuvDriverName = "AUV_PublicPoseDriver";
        private const string RovDriverName = "ROV_PublicPoseDriver";
        private const string UsvDriverName = "USV_PublicPoseDriver";
        private const string AuvRootName = "AUV_Yellow_Underwater";
        private const string RovRootName = "ROV_Box_Seabed";
        private const string UsvRootName = "USV_Blue_Surface";
        private const string PreviewText =
            "VEHICLE STATUS\n" +
            "AUV AUV-01 | NO DATA | PUBLIC_DATA | P— | R—\n" +
            "ROV ROV-01 | NO DATA | PUBLIC_DATA | P— | R—\n" +
            "USV USV-01 | NO DATA | PUBLIC_DATA | P— | R—";

        [Serializable]
        private sealed class InstallerReport
        {
            public string SchemaVersion;
            public string Status;
            public bool Changed;
            public bool V1CoreChanged;
            public bool E2DPostLayoutChanged;
            public string ScenePath;
            public string SceneSha;
            public string CanonicalSemanticSha;
            public int GameObjectCount;
            public int ComponentCount;
            public int MissingReferenceCount;
        }

        private sealed class StandaloneInstallResult
        {
            internal bool V1CoreChanged;
            internal bool E2DPostLayoutChanged;
            internal bool Changed => V1CoreChanged || E2DPostLayoutChanged;
        }

        [Serializable]
        private sealed class SmokeReport
        {
            public string SchemaVersion;
            public string Status;
            public bool Passed;
            public string Detail;
            public string PanelText;
            public int BusinessErrorCount;
            public string UnityVersion;
            public string ScenePath;
        }

        private static bool smokeSubscribed;
        private static double smokeEnteredAt;
        private static int smokeBusinessErrors;

        static VehicleStatusPanelV1SceneInstaller()
        {
            if (SessionState.GetBool(SmokeActiveKey, false))
            {
                SubscribeSmoke();
            }
        }

        [MenuItem(
            "Tools/Underwater Demo/V1/Install Minimal Status Panel")]
        public static void InstallFromMenu()
        {
            StandaloneInstallResult result = Install();
            Debug.Log(
                "V1_STATUS_PANEL_INSTALL_COMPLETE | changed=" +
                result.Changed.ToString().ToLowerInvariant() +
                " | v1CoreChanged=" +
                result.V1CoreChanged.ToString().ToLowerInvariant() +
                " | e2dPostLayoutChanged=" +
                result.E2DPostLayoutChanged.ToString().ToLowerInvariant());
        }

        public static void RunBatch()
        {
            StandaloneInstallResult result = Install();
            CanonicalSemanticSignature signature =
                CanonicalSceneRebuildOrchestrator
                    .BuildCanonicalSemanticSignature();
            var report = new InstallerReport
            {
                SchemaVersion = "1.0",
                Status = "V1_STATUS_PANEL_INSTALL_COMPLETE",
                Changed = result.Changed,
                V1CoreChanged = result.V1CoreChanged,
                E2DPostLayoutChanged = result.E2DPostLayoutChanged,
                ScenePath = ScenePath,
                SceneSha = signature.SceneSha,
                CanonicalSemanticSha =
                    signature.CanonicalSemanticSha,
                GameObjectCount = signature.GameObjectCount,
                ComponentCount = signature.ComponentCount,
                MissingReferenceCount =
                    signature.MissingReferenceCount
            };
            WriteRequestedReport(report);
            Debug.Log(
                report.Status +
                " | changed=" +
                result.Changed.ToString().ToLowerInvariant() +
                " | v1CoreChanged=" +
                result.V1CoreChanged.ToString().ToLowerInvariant() +
                " | e2dPostLayoutChanged=" +
                result.E2DPostLayoutChanged.ToString().ToLowerInvariant() +
                " | scene=" +
                report.SceneSha +
                " | semantic=" +
                report.CanonicalSemanticSha +
                " | counts=" +
                report.GameObjectCount + "/" +
                report.ComponentCount + "/" +
                report.MissingReferenceCount);
        }

        public static void RunNormalSmokeBatch()
        {
            if (SessionState.GetBool(SmokeActiveKey, false))
            {
                throw new InvalidOperationException(
                    "V1 normal smoke is already active.");
            }

            SessionState.SetBool(SmokeActiveKey, true);
            SessionState.SetBool(SmokePassedKey, false);
            SessionState.SetString(
                SmokeDetailKey,
                "Smoke did not complete.");
            SessionState.SetString(SmokeTextKey, string.Empty);
            SubscribeSmoke();
            EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        public static bool InstallForCanonicalSceneRebuild()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                "The V1 status panel cannot be installed in Play Mode.");
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded,
                "The formal V1 Scene is not loaded.");
            Require(string.Equals(
                    scene.path,
                    ScenePath,
                    StringComparison.Ordinal),
                "The active Scene is not the formal V1 Scene.");
            Require(SceneManager.sceneCount == 1,
                "The formal V1 Scene must be uniquely loaded.");
            return InstallIntoLoadedCanonicalScene(scene);
        }

        private static StandaloneInstallResult Install()
        {
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            Require(scene.IsValid() && scene.isLoaded,
                "The formal V1 Scene could not be loaded.");
            Require(!scene.isDirty,
                "The formal V1 Scene must be clean before installation.");

            var result = new StandaloneInstallResult
            {
                V1CoreChanged = InstallForCanonicalSceneRebuild()
            };
            EnvE2DInstallResult postLayout =
                EnvE2DSceneInstaller.ApplyPostVehicleLayout(scene);
            result.E2DPostLayoutChanged = postLayout.Changed;
            if (result.Changed)
            {
                Require(EditorSceneManager.SaveScene(scene),
                    "Failed to save the V1 status panel Scene.");
                Require(!scene.isDirty,
                    "Saved V1 status panel Scene remained dirty.");
            }
            else
            {
                Require(!scene.isDirty,
                    "Idempotent V1 install unexpectedly dirtied the Scene.");
            }

            return result;
        }

        private static bool InstallIntoLoadedCanonicalScene(Scene scene)
        {
            GameObject panelObject = FindUniqueRoot(scene, DataPanelName);
            GameObject boardObject = FindUniqueRoot(scene, BoardName);
            TextMesh[] textMeshes = panelObject.GetComponents<TextMesh>();
            Require(textMeshes.Length == 1,
                "DataPanelText must contain exactly one TextMesh.");
            TextMesh target = textMeshes[0];
            CameraFacingText[] facingTexts =
                panelObject.GetComponents<CameraFacingText>();
            Require(facingTexts.Length == 1,
                "DataPanelText must contain exactly one CameraFacingText.");
            CameraFacingText facingText = facingTexts[0];
            MeshRenderer[] boardRenderers =
                boardObject.GetComponents<MeshRenderer>();
            Require(boardRenderers.Length == 1,
                "Scene_Status_Board must contain exactly one MeshRenderer.");
            MeshRenderer boardRenderer = boardRenderers[0];

            VehicleDataRuntimeHost auvHost =
                FindUniqueComponent<VehicleDataRuntimeHost>(
                    scene,
                    AuvHostName);
            VehicleDataRuntimeHost rovHost =
                FindUniqueComponent<VehicleDataRuntimeHost>(
                    scene,
                    RovHostName);
            VehicleDataRuntimeHost usvHost =
                FindUniqueComponent<VehicleDataRuntimeHost>(
                    scene,
                    UsvHostName);
            VehiclePoseDriver auvDriver =
                FindUniqueComponent<VehiclePoseDriver>(
                    scene,
                    AuvDriverName);
            VehiclePoseDriver rovDriver =
                FindUniqueComponent<VehiclePoseDriver>(
                    scene,
                    RovDriverName);
            VehiclePoseDriver usvDriver =
                FindUniqueComponent<VehiclePoseDriver>(
                    scene,
                    UsvDriverName);
            VehiclePoseControlAuthority auvAuthority =
                FindUniqueComponent<VehiclePoseControlAuthority>(
                    scene,
                    AuvRootName);
            VehiclePoseControlAuthority rovAuthority =
                FindUniqueComponent<VehiclePoseControlAuthority>(
                    scene,
                    RovRootName);
            VehiclePoseControlAuthority usvAuthority =
                FindUniqueComponent<VehiclePoseControlAuthority>(
                    scene,
                    UsvRootName);

            RequireDistinct(
                auvHost,
                rovHost,
                usvHost,
                "Runtime Hosts");
            RequireDistinct(
                auvDriver,
                rovDriver,
                usvDriver,
                "Pose Drivers");
            RequireDistinct(
                auvAuthority,
                rovAuthority,
                usvAuthority,
                "Authorities");
            RequireVehicleIdentity(
                auvHost,
                VehicleType.Auv,
                "AUV-01");
            RequireVehicleIdentity(
                rovHost,
                VehicleType.Rov,
                "ROV-01");
            RequireVehicleIdentity(
                usvHost,
                VehicleType.Usv,
                "USV-01");

            bool changed = false;
            VehicleStatusPanelPresenter[] presenters =
                panelObject.GetComponents<VehicleStatusPanelPresenter>();
            Require(presenters.Length <= 1,
                "DataPanelText contains duplicate V1 presenters.");
            VehicleStatusPanelPresenter presenter;
            if (presenters.Length == 0)
            {
                presenter =
                    panelObject.AddComponent<VehicleStatusPanelPresenter>();
                EditorUtility.SetDirty(presenter);
                changed = true;
            }
            else
            {
                presenter = presenters[0];
            }

            if (!presenter.MatchesConfiguration(
                    target,
                    auvHost,
                    auvDriver,
                    auvAuthority,
                    rovHost,
                    rovDriver,
                    rovAuthority,
                    usvHost,
                    usvDriver,
                    usvAuthority,
                    0.2f))
            {
                presenter.Configure(
                    target,
                    auvHost,
                    auvDriver,
                    auvAuthority,
                    rovHost,
                    rovDriver,
                    rovAuthority,
                    usvHost,
                    usvDriver,
                    usvAuthority,
                    0.2f);
                EditorUtility.SetDirty(presenter);
                changed = true;
            }

            if (!string.Equals(
                    target.text,
                    PreviewText,
                    StringComparison.Ordinal))
            {
                target.text = PreviewText;
                EditorUtility.SetDirty(target);
                changed = true;
            }

            if (!Approximately(
                    boardObject.transform.localPosition,
                    new Vector3(-1.60f, 1.15f, -3.20f)))
            {
                boardObject.transform.localPosition =
                    new Vector3(-1.60f, 1.15f, -3.20f);
                EditorUtility.SetDirty(boardObject.transform);
                changed = true;
            }

            if (!Approximately(
                    boardObject.transform.localScale,
                    new Vector3(2.80f, 0.80f, 0.04f)))
            {
                boardObject.transform.localScale =
                    new Vector3(2.80f, 0.80f, 0.04f);
                EditorUtility.SetDirty(boardObject.transform);
                changed = true;
            }

            if (target.fontSize != 36)
            {
                target.fontSize = 36;
                EditorUtility.SetDirty(target);
                changed = true;
            }

            if (Mathf.Abs(target.characterSize - 0.024f) >
                0.000001f)
            {
                target.characterSize = 0.024f;
                EditorUtility.SetDirty(target);
                changed = true;
            }

            if (Mathf.Abs(target.lineSpacing - 0.9f) >
                0.000001f)
            {
                target.lineSpacing = 0.9f;
                EditorUtility.SetDirty(target);
                changed = true;
            }

            if (target.anchor != TextAnchor.MiddleLeft)
            {
                target.anchor = TextAnchor.MiddleLeft;
                EditorUtility.SetDirty(target);
                changed = true;
            }

            if (target.alignment != TextAlignment.Left)
            {
                target.alignment = TextAlignment.Left;
                EditorUtility.SetDirty(target);
                changed = true;
            }

            if (facingText.Mode !=
                CameraFacingText.BillboardMode.ScreenParallel)
            {
                facingText.SetBillboardMode(
                    CameraFacingText.BillboardMode.ScreenParallel);
                EditorUtility.SetDirty(facingText);
                changed = true;
            }

            if (boardRenderer.enabled)
            {
                boardRenderer.enabled = false;
                EditorUtility.SetDirty(boardRenderer);
                changed = true;
            }

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }

            ValidateInstalledScene(
                scene,
                presenter,
                target,
                facingText,
                auvHost,
                auvDriver,
                auvAuthority,
                rovHost,
                rovDriver,
                rovAuthority,
                usvHost,
                usvDriver,
                usvAuthority);
            return changed;
        }

        private static void ValidateInstalledScene(
            Scene scene,
            VehicleStatusPanelPresenter presenter,
            TextMesh target,
            CameraFacingText facingText,
            VehicleDataRuntimeHost auvHost,
            VehiclePoseDriver auvDriver,
            VehiclePoseControlAuthority auvAuthority,
            VehicleDataRuntimeHost rovHost,
            VehiclePoseDriver rovDriver,
            VehiclePoseControlAuthority rovAuthority,
            VehicleDataRuntimeHost usvHost,
            VehiclePoseDriver usvDriver,
            VehiclePoseControlAuthority usvAuthority)
        {
            VehicleStatusPanelPresenter[] presenters =
                UnityEngine.Object
                    .FindObjectsByType<VehicleStatusPanelPresenter>(
                        FindObjectsInactive.Include)
                    .Where(item => item.gameObject.scene == scene)
                    .ToArray();
            Require(presenters.Length == 1 &&
                    ReferenceEquals(presenters[0], presenter),
                "The formal Scene must contain one V1 presenter.");
            Require(presenter.MatchesConfiguration(
                    target,
                    auvHost,
                    auvDriver,
                    auvAuthority,
                    rovHost,
                    rovDriver,
                    rovAuthority,
                    usvHost,
                    usvDriver,
                    usvAuthority,
                    0.2f),
                "V1 presenter bindings are incomplete or crossed.");
            Require(target.gameObject.name == DataPanelName,
                "V1 presenter target is not DataPanelText.");
            Require(target.GetComponents<VehicleStatusPanelPresenter>()
                    .Length == 1,
                "DataPanelText does not own exactly one V1 presenter.");
            Require(target.GetComponents<CameraFacingText>().Length == 1 &&
                    ReferenceEquals(
                        target.GetComponent<CameraFacingText>(),
                        facingText),
                "DataPanelText does not own exactly one CameraFacingText.");
            Require(facingText.Mode ==
                    CameraFacingText.BillboardMode.ScreenParallel,
                "DataPanelText does not use the approved screen-parallel billboard mode.");
            GameObject board = FindUniqueRoot(scene, BoardName);
            MeshRenderer[] boardRenderers =
                board.GetComponents<MeshRenderer>();
            Require(boardRenderers.Length == 1 &&
                    !boardRenderers[0].enabled,
                "Scene_Status_Board MeshRenderer must exist and be disabled.");
            Require(board.GetComponents<MeshFilter>().Length == 1,
                "Scene_Status_Board MeshFilter must be retained.");
            Require(board.GetComponents<Collider>().Length == 1,
                "Scene_Status_Board Collider must be retained.");
            Require(target.fontSize == 36 &&
                    Mathf.Abs(target.characterSize - 0.024f) <= 0.000001f &&
                    Mathf.Abs(target.lineSpacing - 0.9f) <= 0.000001f &&
                    target.anchor == TextAnchor.MiddleLeft &&
                    target.alignment == TextAlignment.Left,
                "DataPanelText typography is not the approved V1 layout.");
            Require(Approximately(
                    board.transform.localPosition,
                    new Vector3(-1.60f, 1.15f, -3.20f)) &&
                    Approximately(
                        board.transform.localScale,
                        new Vector3(2.80f, 0.80f, 0.04f)),
                "Scene_Status_Board is not the approved V1 layout.");
            Require(string.Equals(
                    target.text,
                    PreviewText,
                    StringComparison.Ordinal),
                "DataPanelText preview text is not canonical.");
        }

        private static bool Approximately(Vector3 actual, Vector3 expected)
        {
            return Mathf.Abs(actual.x - expected.x) <= 0.000001f &&
                   Mathf.Abs(actual.y - expected.y) <= 0.000001f &&
                   Mathf.Abs(actual.z - expected.z) <= 0.000001f;
        }

        private static T FindUniqueComponent<T>(
            Scene scene,
            string rootName)
            where T : Component
        {
            GameObject root = FindUniqueRoot(scene, rootName);
            T[] components = root.GetComponents<T>();
            Require(components.Length == 1,
                rootName + " must contain exactly one " +
                typeof(T).Name + ".");
            return components[0];
        }

        private static GameObject FindUniqueRoot(
            Scene scene,
            string name)
        {
            GameObject[] matches = scene.GetRootGameObjects()
                .Where(root => string.Equals(
                    root.name,
                    name,
                    StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1,
                "Expected exactly one Scene root named " + name + ".");
            return matches[0];
        }

        private static void RequireDistinct<T>(
            T first,
            T second,
            T third,
            string label)
            where T : UnityEngine.Object
        {
            Require(!ReferenceEquals(first, second) &&
                    !ReferenceEquals(first, third) &&
                    !ReferenceEquals(second, third),
                label + " must be unique per vehicle.");
        }

        private static void RequireVehicleIdentity(
            VehicleDataRuntimeHost host,
            VehicleType expectedType,
            string expectedId)
        {
            Require(host.IntegrationConfiguration != null,
                expectedId + " Integration Configuration is missing.");
            Require(host.IntegrationConfiguration.VehicleType ==
                    expectedType &&
                    string.Equals(
                        host.VehicleId,
                        expectedId,
                        StringComparison.Ordinal),
                expectedId + " Host identity is incorrect.");
        }

        private static void WriteRequestedReport(
            InstallerReport report)
        {
            string path = GetCommandLineArgument(ReportPathArgument);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string absolutePath = Path.GetFullPath(path);
            Directory.CreateDirectory(
                Path.GetDirectoryName(absolutePath));
            File.WriteAllText(
                absolutePath,
                JsonUtility.ToJson(report, true));
        }

        private static void SubscribeSmoke()
        {
            if (smokeSubscribed)
            {
                return;
            }

            EditorApplication.playModeStateChanged +=
                OnSmokePlayModeStateChanged;
            EditorApplication.update += OnSmokeEditorUpdate;
            Application.logMessageReceived += OnSmokeLog;
            smokeSubscribed = true;
        }

        private static void UnsubscribeSmoke()
        {
            if (!smokeSubscribed)
            {
                return;
            }

            EditorApplication.playModeStateChanged -=
                OnSmokePlayModeStateChanged;
            EditorApplication.update -= OnSmokeEditorUpdate;
            Application.logMessageReceived -= OnSmokeLog;
            smokeSubscribed = false;
        }

        private static void OnSmokePlayModeStateChanged(
            PlayModeStateChange state)
        {
            if (!SessionState.GetBool(SmokeActiveKey, false))
            {
                return;
            }

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                smokeEnteredAt = Time.realtimeSinceStartupAsDouble;
                smokeBusinessErrors = 0;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                FinishSmoke();
            }
        }

        private static void OnSmokeEditorUpdate()
        {
            if (!SessionState.GetBool(SmokeActiveKey, false) ||
                !EditorApplication.isPlaying)
            {
                return;
            }

            double elapsed =
                Time.realtimeSinceStartupAsDouble - smokeEnteredAt;
            if (elapsed < 0.5)
            {
                return;
            }

            try
            {
                VehicleStatusPanelPresenter[] presenters =
                    UnityEngine.Object
                        .FindObjectsByType<VehicleStatusPanelPresenter>(
                            FindObjectsInactive.Include);
                Require(presenters.Length == 1,
                    "Normal smoke expected exactly one V1 presenter.");
                VehicleStatusPanelPresenter presenter = presenters[0];
                presenter.RefreshNow();
                string panelText = presenter.LastRenderedText;
                if (!PanelIsNormal(panelText))
                {
                    if (elapsed < 5.0)
                    {
                        return;
                    }

                    throw new InvalidOperationException(
                        "The status panel did not reach three NORMAL rows " +
                        "within the smoke timeout. Text=" + panelText);
                }

                Require(smokeBusinessErrors == 0,
                    "Normal smoke captured " +
                    smokeBusinessErrors +
                    " business Console errors.");
                Require(presenter.TargetText != null &&
                        string.Equals(
                            presenter.TargetText.text,
                            panelText,
                            StringComparison.Ordinal),
                    "Presenter output is not bound to the visible TextMesh.");
                Require(
                    !ReferenceEquals(
                        presenter.AuvHost,
                        presenter.RovHost) &&
                    !ReferenceEquals(
                        presenter.AuvHost,
                        presenter.UsvHost) &&
                    !ReferenceEquals(
                        presenter.RovHost,
                        presenter.UsvHost),
                    "Smoke found crossed Runtime Host bindings.");

                SessionState.SetBool(SmokePassedKey, true);
                SessionState.SetString(
                    SmokeDetailKey,
                    "Three simultaneous NORMAL rows, explicit identities, " +
                    "PUBLIC_DATA authority, finite applied poses, and " +
                    "zero business Console errors passed.");
                SessionState.SetString(SmokeTextKey, panelText);
                EditorApplication.ExitPlaymode();
            }
            catch (Exception exception)
            {
                SessionState.SetBool(SmokePassedKey, false);
                SessionState.SetString(
                    SmokeDetailKey,
                    exception.GetType().Name + ": " + exception.Message);
                EditorApplication.ExitPlaymode();
            }
        }

        private static bool PanelIsNormal(string text)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   text.StartsWith(
                       "VEHICLE STATUS\n",
                       StringComparison.Ordinal) &&
                   text.Contains(
                       "AUV AUV-01 | NORMAL | PUBLIC_DATA | P(") &&
                   text.Contains(
                       "ROV ROV-01 | NORMAL | PUBLIC_DATA | P(") &&
                   text.Contains(
                       "USV USV-01 | NORMAL | PUBLIC_DATA | P(") &&
                   CountOccurrences(text, " | NORMAL | ") == 3 &&
                   CountOccurrences(text, " | PUBLIC_DATA | ") == 3 &&
                   CountOccurrences(text, "\n") == 3 &&
                   !text.Contains("P—") &&
                   !text.Contains("R—") &&
                   !text.Contains("NaN") &&
                   !text.Contains("Infinity");
        }

        private static int CountOccurrences(
            string text,
            string value)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(
                       value,
                       index,
                       StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static void OnSmokeLog(
            string condition,
            string stackTrace,
            LogType type)
        {
            bool isFailure =
                type == LogType.Error ||
                type == LogType.Exception ||
                type == LogType.Assert;
            bool isProjectFailure =
                (!string.IsNullOrEmpty(stackTrace) &&
                 (stackTrace.Contains("Assets/") ||
                  stackTrace.Contains("UnderwaterRobotScene"))) ||
                (!string.IsNullOrEmpty(condition) &&
                 condition.Contains("UnderwaterRobotScene"));
            if (isFailure && isProjectFailure)
            {
                smokeBusinessErrors++;
            }
        }

        private static void FinishSmoke()
        {
            bool passed =
                SessionState.GetBool(SmokePassedKey, false);
            string detail = SessionState.GetString(
                SmokeDetailKey,
                "No smoke result.");
            var report = new SmokeReport
            {
                SchemaVersion = "1.0",
                Status = passed
                    ? "V1_STATUS_PANEL_NORMAL_SMOKE_PASS"
                    : "V1_STATUS_PANEL_NORMAL_SMOKE_FAIL",
                Passed = passed,
                Detail = detail,
                PanelText = SessionState.GetString(
                    SmokeTextKey,
                    string.Empty),
                BusinessErrorCount = smokeBusinessErrors,
                UnityVersion = Application.unityVersion,
                ScenePath = ScenePath
            };
            WriteSmokeReport(report);
            SessionState.SetBool(SmokeActiveKey, false);
            SessionState.SetBool(SmokePassedKey, false);
            SessionState.SetString(SmokeDetailKey, string.Empty);
            SessionState.SetString(SmokeTextKey, string.Empty);
            UnsubscribeSmoke();
            Debug.Log(report.Status + " | " + detail);
            EditorApplication.Exit(passed ? 0 : 1);
        }

        private static void WriteSmokeReport(SmokeReport report)
        {
            string path =
                GetCommandLineArgument(SmokeReportPathArgument);
            Require(!string.IsNullOrWhiteSpace(path),
                "The V1 smoke report path is required.");
            string absolutePath = Path.GetFullPath(path);
            Directory.CreateDirectory(
                Path.GetDirectoryName(absolutePath));
            File.WriteAllText(
                absolutePath,
                JsonUtility.ToJson(report, true));
        }

        private static string GetCommandLineArgument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index + 1 < arguments.Length; index++)
            {
                if (string.Equals(
                        arguments[index],
                        name,
                        StringComparison.Ordinal))
                {
                    return arguments[index + 1];
                }
            }

            return string.Empty;
        }

        private static string SceneSha()
        {
            string path = Path.GetFullPath(Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                ScenePath));
            using SHA256 hash = SHA256.Create();
            return BitConverter.ToString(
                    hash.ComputeHash(File.ReadAllBytes(path)))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
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
