using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    [Serializable]
    public sealed class UsvPostBuildInvocationTraceEntry
    {
        public string DeclaringType;
        public string MethodName;
        public int Ordinal;
        public bool Completed;
        public bool Changed;
        public int SceneSaveCallbackCount;

        internal UsvPostBuildInvocationTraceEntry Copy()
        {
            return new UsvPostBuildInvocationTraceEntry
            {
                DeclaringType = DeclaringType,
                MethodName = MethodName,
                Ordinal = Ordinal,
                Completed = Completed,
                Changed = Changed,
                SceneSaveCallbackCount = SceneSaveCallbackCount
            };
        }
    }

    [Serializable]
    public sealed class UsvPostBuildInstallerChainResult
    {
        public bool Success;
        public bool N6DChanged;
        public bool M2CChanged;
        public bool M2DChanged;
        public bool AnyChanged;
        public bool SceneSaved;
        public string SceneShaBefore;
        public string SceneShaAfter;
        public string FinalActiveScenePath;
        public string FailureStage;
        public string FailureMessage;
        public string[] InvocationOrder;
        public int SceneSaveCallbackCount;

        [SerializeField]
        private UsvPostBuildInvocationTraceEntry[] invocationTrace =
            Array.Empty<UsvPostBuildInvocationTraceEntry>();

        public UsvPostBuildInvocationTraceEntry[] InvocationTrace
        {
            get
            {
                return invocationTrace.Select(entry => entry.Copy()).ToArray();
            }
        }

        internal void SetInvocationTrace(
            UsvPostBuildInvocationTraceEntry[] entries)
        {
            invocationTrace = entries.Select(entry => entry.Copy()).ToArray();
        }
    }

    public static class UsvPostBuildInstallerChain
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string ReportPathArgument = "-usvPostBuildReportPath";
        private delegate bool ComposableInstaller();

        private sealed class InstallerStage
        {
            public readonly string FailureStage;
            public readonly ComposableInstaller Install;

            public InstallerStage(
                string failureStage,
                ComposableInstaller install)
            {
                FailureStage = failureStage;
                Install = install;
            }
        }

        private sealed class SceneSaveObserver : IDisposable
        {
            public int Count { get; private set; }

            public SceneSaveObserver()
            {
                EditorSceneManager.sceneSaved += OnSceneSaved;
            }

            public void Dispose()
            {
                EditorSceneManager.sceneSaved -= OnSceneSaved;
            }

            private void OnSceneSaved(Scene scene)
            {
                Count++;
            }
        }

        private static UsvPostBuildInvocationTraceEntry[] lastInvocationTrace =
            Array.Empty<UsvPostBuildInvocationTraceEntry>();
        private static int lastSceneSaveCallbackCount;

        [MenuItem(
            "Tools/Underwater Demo/M2-CD/Run Canonical USV Post-Build Installer Chain")]
        public static void RunFromMenu()
        {
            UsvPostBuildInstallerChainResult result =
                RunAuthorizedCanonicalUsvPostBuildChainCore();
            if (!result.Success)
            {
                EditorUtility.DisplayDialog(
                    "Canonical USV Post-Build Installer Chain",
                    result.FailureStage + ": " + result.FailureMessage,
                    "OK");
                return;
            }

            Debug.Log(FormatCompletion(result));
        }

        public static void RunAuthorizedCanonicalUsvPostBuildChain()
        {
            UsvPostBuildInstallerChainResult result =
                RunAuthorizedCanonicalUsvPostBuildChainCore();
            WriteRequestedReport(result);
            if (!result.Success)
            {
                throw new InvalidOperationException(
                    result.FailureStage + ": " + result.FailureMessage);
            }

            Debug.Log(FormatCompletion(result));
        }

        public static UsvPostBuildInstallerChainResult
            InstallCanonicalUsvPostBuildChain()
        {
            ValidateCallablePreconditions();
            var result = new UsvPostBuildInstallerChainResult
            {
                Success = false,
                SceneSaved = false,
                FailureStage = "N6-D",
                FailureMessage = string.Empty
            };
            InstallerStage[] stages =
            {
                new InstallerStage(
                    "N6-D",
                    UsvRootPoseN6DSceneInstaller
                        .InstallForCanonicalPostBuildChain),
                new InstallerStage(
                    "M2-C",
                    UsvSurfaceVisualM2CSceneInstaller
                        .InstallForCanonicalPostBuildChain),
                new InstallerStage(
                    "M2-D",
                    UsvActuatorVisualM2DSceneInstaller
                        .InstallForCanonicalPostBuildChain)
            };
            var trace = new UsvPostBuildInvocationTraceEntry[stages.Length];
            using (var saveObserver = new SceneSaveObserver())
            {
                try
                {
                    for (int index = 0; index < stages.Length; index++)
                    {
                        InstallerStage stage = stages[index];
                        var entry = new UsvPostBuildInvocationTraceEntry
                        {
                            DeclaringType =
                                stage.Install.Method.DeclaringType.FullName,
                            MethodName = stage.Install.Method.Name,
                            Ordinal = index + 1
                        };
                        trace[index] = entry;
                        int savesBefore = saveObserver.Count;
                        result.FailureStage = stage.FailureStage;
                        try
                        {
                            bool changed = stage.Install();
                            entry.Changed = changed;
                            entry.Completed = true;
                        }
                        finally
                        {
                            entry.SceneSaveCallbackCount =
                                saveObserver.Count - savesBefore;
                        }
                    }
                }
                finally
                {
                    lastInvocationTrace = trace
                        .Where(entry => entry != null)
                        .Select(entry => entry.Copy())
                        .ToArray();
                    lastSceneSaveCallbackCount = saveObserver.Count;
                }
            }

            result.N6DChanged = trace[0].Changed;
            result.M2CChanged = trace[1].Changed;
            result.M2DChanged = trace[2].Changed;
            result.AnyChanged =
                result.N6DChanged ||
                result.M2CChanged ||
                result.M2DChanged;
            result.SceneSaveCallbackCount = lastSceneSaveCallbackCount;
            result.SetInvocationTrace(trace);
            result.InvocationOrder = trace
                .Select(entry =>
                    entry.DeclaringType + "." + entry.MethodName)
                .ToArray();
            Require(trace.All(entry =>
                    entry.Completed &&
                    entry.SceneSaveCallbackCount == 0),
                "A canonical USV nested installer did not complete zero-write.");
            Require(result.SceneSaveCallbackCount == 0,
                "The canonical USV chain observed a Scene save.");

            Scene activeScene = SceneManager.GetActiveScene();
            Require(IsFormalScene(activeScene),
                "The final active Scene is not the formal Scene.");
            result.FinalActiveScenePath = activeScene.path;
            result.Success = true;
            result.FailureStage = string.Empty;
            result.FailureMessage = string.Empty;
            return result;
        }

        private static UsvPostBuildInstallerChainResult
            RunAuthorizedCanonicalUsvPostBuildChainCore()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                "Cannot run the canonical chain in Play Mode.");
            Require(!EditorApplication.isCompiling,
                "Cannot run the canonical chain while scripts compile.");
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                Require(!scene.isDirty,
                    "Refusing to replace a Scene with unsaved user changes: " +
                    scene.path);
            }

            Scene formalScene =
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Require(IsFormalScene(formalScene) && !formalScene.isDirty,
                "Failed to open the clean formal Scene.");
            string sceneAbsolutePath = GetSceneAbsolutePath();
            byte[] sceneBackup = File.ReadAllBytes(sceneAbsolutePath);
            string sceneShaBefore = Sha256(sceneBackup);
            UsvPostBuildInstallerChainResult result = null;
            try
            {
                result = InstallCanonicalUsvPostBuildChain();
                result.SceneShaBefore = sceneShaBefore;
                if (result.AnyChanged)
                {
                    Require(EditorSceneManager.SaveScene(formalScene),
                        "Unity failed to save the canonical USV chain Scene.");
                    AssetDatabase.ImportAsset(
                        ScenePath,
                        ImportAssetOptions.ForceSynchronousImport |
                        ImportAssetOptions.ForceUpdate);
                    formalScene = EditorSceneManager.OpenScene(
                        ScenePath,
                        OpenSceneMode.Single);
                    Require(IsFormalScene(formalScene) && !formalScene.isDirty,
                        "The canonical USV chain did not reopen a clean formal Scene.");
                    UsvPostBuildInstallerChainResult persisted =
                        InstallCanonicalUsvPostBuildChain();
                    Require(!persisted.AnyChanged,
                        "The persisted canonical USV chain is not an exact no-op.");
                }
                else
                {
                    Require(!formalScene.isDirty,
                        "A no-op canonical USV chain dirtied the formal Scene.");
                    Require(sceneBackup.SequenceEqual(
                            File.ReadAllBytes(sceneAbsolutePath)),
                        "A no-op canonical USV chain changed Scene bytes.");
                }

                result.SceneShaAfter =
                    Sha256(File.ReadAllBytes(sceneAbsolutePath));
                result.FinalActiveScenePath =
                    SceneManager.GetActiveScene().path;
                return result;
            }
            catch (Exception failure)
            {
                var failed = result ?? new UsvPostBuildInstallerChainResult
                {
                    Success = false,
                    SceneSaved = false,
                    SceneShaBefore = sceneShaBefore,
                    FailureStage = FailureStageForLastTrace(),
                    FailureMessage = failure.ToString(),
                    InvocationOrder = LastInvocationOrder(),
                    SceneSaveCallbackCount = lastSceneSaveCallbackCount
                };
                failed.Success = false;
                failed.FailureMessage = failure.ToString();
                failed.SetInvocationTrace(GetLastInvocationTrace());
                try
                {
                    File.WriteAllBytes(sceneAbsolutePath, sceneBackup);
                    AssetDatabase.Refresh(
                        ImportAssetOptions.ForceSynchronousImport);
                    Scene restored = EditorSceneManager.OpenScene(
                        ScenePath,
                        OpenSceneMode.Single);
                    Require(IsFormalScene(restored) && !restored.isDirty,
                        "The formal Scene could not be reloaded after rollback.");
                    string restoredSha = Sha256(
                        File.ReadAllBytes(sceneAbsolutePath));
                    Require(string.Equals(
                            sceneShaBefore,
                            restoredSha,
                            StringComparison.Ordinal),
                        "Scene rollback did not restore the original SHA.");
                    failed.SceneShaAfter = restoredSha;
                    failed.FinalActiveScenePath = restored.path;
                }
                catch (Exception rollbackFailure)
                {
                    failed.FailureMessage +=
                        Environment.NewLine +
                        "Rollback failure: " +
                        rollbackFailure;
                }

                return failed;
            }
        }

        private static void ValidateCallablePreconditions()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                "Cannot call the canonical chain in Play Mode.");
            Require(!EditorApplication.isCompiling,
                "Cannot call the canonical chain while scripts compile.");

            Scene[] loadedScenes = Enumerable.Range(0, SceneManager.sceneCount)
                .Select(SceneManager.GetSceneAt)
                .Where(scene => scene.IsValid() && scene.isLoaded)
                .ToArray();
            Require(loadedScenes.Length == 1,
                "The canonical chain requires exactly one loaded Scene.");
            Scene activeScene = SceneManager.GetActiveScene();
            Require(IsFormalScene(activeScene),
                "The callable chain requires the formal Scene to be active.");
        }

        internal static UsvPostBuildInvocationTraceEntry[]
            GetLastInvocationTrace()
        {
            return lastInvocationTrace
                .Select(entry => entry.Copy())
                .ToArray();
        }

        internal static int GetLastSceneSaveCallbackCount()
        {
            return lastSceneSaveCallbackCount;
        }

        private static string[] LastInvocationOrder()
        {
            return GetLastInvocationTrace()
                .Select(entry =>
                    entry.DeclaringType + "." + entry.MethodName)
                .ToArray();
        }

        private static string FailureStageForLastTrace()
        {
            UsvPostBuildInvocationTraceEntry incomplete =
                GetLastInvocationTrace()
                    .FirstOrDefault(entry => !entry.Completed);
            return incomplete == null
                ? "postcondition"
                : "nested ordinal " + incomplete.Ordinal;
        }

        private static bool IsFormalScene(Scene scene)
        {
            return scene.IsValid() &&
                   scene.isLoaded &&
                   string.Equals(scene.path, ScenePath, StringComparison.Ordinal);
        }

        private static string GetSceneAbsolutePath()
        {
            return Path.GetFullPath(Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                ScenePath));
        }

        private static string Sha256(byte[] bytes)
        {
            using SHA256 hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(bytes))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        private static void WriteRequestedReport(
            UsvPostBuildInstallerChainResult result)
        {
            string path = GetCommandLineArgument(ReportPathArgument);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string absolutePath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllText(
                absolutePath,
                JsonUtility.ToJson(result, true));
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

        private static string FormatCompletion(
            UsvPostBuildInstallerChainResult result)
        {
            return
                "M2_CD_CANONICAL_USV_POST_BUILD_INSTALLER_CHAIN_COMPLETE" +
                " | n6dChanged=" + result.N6DChanged.ToString().ToLowerInvariant() +
                " | m2cChanged=" + result.M2CChanged.ToString().ToLowerInvariant() +
                " | m2dChanged=" + result.M2DChanged.ToString().ToLowerInvariant() +
                " | anyChanged=" + result.AnyChanged.ToString().ToLowerInvariant() +
                " | sceneSaved=" + result.SceneSaved.ToString().ToLowerInvariant() +
                " | sceneSha=" + result.SceneShaAfter;
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
