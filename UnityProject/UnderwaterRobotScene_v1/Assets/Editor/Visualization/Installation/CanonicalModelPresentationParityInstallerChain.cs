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
    public sealed class PresentationParityInvocationTraceEntry
    {
        public string DeclaringType;
        public string MethodName;
        public int Ordinal;
        public bool Completed;
        public bool Changed;
        public int SceneSaveCallbackCount;

        internal PresentationParityInvocationTraceEntry Copy()
        {
            return new PresentationParityInvocationTraceEntry
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
    public sealed class CanonicalModelPresentationParityResult
    {
        public bool Success;
        public bool AuvChanged;
        public bool RovChanged;
        public bool UsvChanged;
        public bool AnyChanged;
        public bool SceneSaved;
        public string SceneShaBefore;
        public string SceneShaAfter;
        public string FailureStage;
        public string FailureMessage;
        public string[] InvocationOrder;
        public int SceneSaveCallbackCount;

        [SerializeField]
        private PresentationParityInvocationTraceEntry[] invocationTrace =
            Array.Empty<PresentationParityInvocationTraceEntry>();

        public PresentationParityInvocationTraceEntry[] InvocationTrace
        {
            get
            {
                return invocationTrace.Select(entry => entry.Copy()).ToArray();
            }
        }

        internal void SetInvocationTrace(
            PresentationParityInvocationTraceEntry[] entries)
        {
            invocationTrace = entries.Select(entry => entry.Copy()).ToArray();
        }
    }

    public static class CanonicalModelPresentationParityInstallerChain
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";

        internal static string FaultInjectionStage;
        internal static bool FaultInjectionRollbackFailure;
        private delegate bool ComposableInstaller();

        private sealed class InstallerStage
        {
            public readonly string FailureStage;
            public readonly string FaultInjectionStage;
            public readonly ComposableInstaller Install;

            public InstallerStage(
                string failureStage,
                string faultInjectionStage,
                ComposableInstaller install)
            {
                FailureStage = failureStage;
                FaultInjectionStage = faultInjectionStage;
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

        private static PresentationParityInvocationTraceEntry[]
            lastInvocationTrace =
                Array.Empty<PresentationParityInvocationTraceEntry>();
        private static int lastSceneSaveCallbackCount;

        [MenuItem("Tools/Underwater Demo/G3/Install Canonical Model Presentation Parity")]
        public static void InstallFromMenu()
        {
            CanonicalModelPresentationParityResult result =
                InstallWithLifecycle();
            if (!result.Success)
            {
                throw new InvalidOperationException(
                    result.FailureStage + ": " + result.FailureMessage);
            }

            Debug.Log(
                "G3 canonical model presentation parity changed=" +
                result.AnyChanged);
        }

        public static void RunCanonicalModelPresentationParityBatch()
        {
            CanonicalModelPresentationParityResult result =
                InstallWithLifecycle();
            if (!result.Success)
            {
                throw new InvalidOperationException(
                    "G3 presentation parity Chain failed at " +
                    result.FailureStage + ": " + result.FailureMessage);
            }

            Debug.Log(
                "M_GLOBAL_G3_CANONICAL_MODEL_PRESENTATION_PARITY_CHAIN_PASS" +
                " | auvChanged=" + result.AuvChanged +
                " | rovChanged=" + result.RovChanged +
                " | usvChanged=" + result.UsvChanged +
                " | anyChanged=" + result.AnyChanged +
                " | sceneSaved=" + result.SceneSaved +
                " | before=" + result.SceneShaBefore +
                " | after=" + result.SceneShaAfter +
                " | order=" + string.Join(">", result.InvocationOrder));
        }

        public static CanonicalModelPresentationParityResult
            InstallCanonicalModelPresentationParity()
        {
            RequireComposableScene();
            var result = new CanonicalModelPresentationParityResult
            {
                Success = false,
                SceneSaved = false,
                FailureStage = string.Empty,
                FailureMessage = string.Empty,
                InvocationOrder = Array.Empty<string>()
            };
            InstallerStage[] stages =
            {
                new InstallerStage(
                    "AuvModelPresentationParityInstaller",
                    "BeforeAuv",
                    AuvModelPresentationParityInstaller
                        .InstallForCanonicalSceneRebuild),
                new InstallerStage(
                    "RovModelPresentationParityInstaller",
                    "BeforeRov",
                    RovModelPresentationParityInstaller
                        .InstallForCanonicalSceneRebuild),
                new InstallerStage(
                    "UsvModelPresentationParityInstaller",
                    "BeforeUsv",
                    UsvModelPresentationParityInstaller
                        .InstallForCanonicalSceneRebuild)
            };
            var trace =
                new PresentationParityInvocationTraceEntry[stages.Length];
            try
            {
                using (var saveObserver = new SceneSaveObserver())
                {
                    try
                    {
                        for (int index = 0; index < stages.Length; index++)
                        {
                            InstallerStage stage = stages[index];
                            ThrowIfInjected(stage.FaultInjectionStage);
                            var entry =
                                new PresentationParityInvocationTraceEntry
                                {
                                    DeclaringType =
                                        stage.Install.Method.DeclaringType.FullName,
                                    MethodName = stage.Install.Method.Name,
                                    Ordinal = index + 1
                                };
                            trace[index] = entry;
                            result.FailureStage = stage.FailureStage;
                            int savesBefore = saveObserver.Count;
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
                ThrowIfInjected("AfterUsv");

                result.AuvChanged = trace[0].Changed;
                result.RovChanged = trace[1].Changed;
                result.UsvChanged = trace[2].Changed;
                result.AnyChanged =
                    result.AuvChanged ||
                    result.RovChanged ||
                    result.UsvChanged;
                result.SceneSaveCallbackCount =
                    lastSceneSaveCallbackCount;
                result.SetInvocationTrace(trace);
                result.InvocationOrder = trace
                    .Select(entry =>
                        entry.DeclaringType + "." + entry.MethodName)
                    .ToArray();
                Require(trace.All(entry =>
                        entry.Completed &&
                        entry.SceneSaveCallbackCount == 0),
                    "A presentation nested installer did not complete zero-write.");
                Require(result.SceneSaveCallbackCount == 0,
                    "The presentation chain observed a Scene save.");
                RequireComposableScene();
                result.Success = true;
                result.FailureStage = string.Empty;
                return result;
            }
            finally
            {
                FaultInjectionStage = null;
                FaultInjectionRollbackFailure = false;
            }
        }

        private static CanonicalModelPresentationParityResult
            InstallWithLifecycle()
        {
            RequireCleanPreflight();
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            RequireFormalScene();
            string absoluteScenePath = AbsoluteScenePath();
            byte[] beforeBytes = File.ReadAllBytes(absoluteScenePath);
            string beforeSha = Sha256(beforeBytes);
            bool injectRollbackFailure = FaultInjectionRollbackFailure;
            CanonicalModelPresentationParityResult result = null;
            try
            {
                result = InstallCanonicalModelPresentationParity();
                result.SceneShaBefore = beforeSha;
                if (result.AnyChanged)
                {
                    Require(EditorSceneManager.SaveScene(scene),
                        "Unity failed to save the presentation parity Scene.");
                    AssetDatabase.ImportAsset(
                        ScenePath,
                        ImportAssetOptions.ForceSynchronousImport |
                        ImportAssetOptions.ForceUpdate);
                    scene = EditorSceneManager.OpenScene(
                        ScenePath,
                        OpenSceneMode.Single);
                    RequireFormalScene();
                    CanonicalModelPresentationParityResult persisted =
                        InstallCanonicalModelPresentationParity();
                    Require(!persisted.AnyChanged,
                        "The persisted presentation parity chain is not an exact no-op.");
                }
                else
                {
                    Require(!scene.isDirty,
                        "A no-op presentation chain dirtied the formal Scene.");
                    Require(beforeBytes.SequenceEqual(
                            File.ReadAllBytes(absoluteScenePath)),
                        "A no-op presentation chain changed Scene bytes.");
                }

                result.SceneShaAfter =
                    Sha256(File.ReadAllBytes(absoluteScenePath));
                return result;
            }
            catch (Exception failure)
            {
                try
                {
                    RestoreBefore(
                        absoluteScenePath,
                        beforeBytes,
                        beforeSha,
                        injectRollbackFailure);
                }
                catch (Exception rollbackFailure)
                {
                    throw new InvalidOperationException(
                        "Presentation Chain failed and rollback also failed. " +
                        "Original failure: " + failure.Message +
                        " | Rollback failure: " + rollbackFailure.Message,
                        new AggregateException(failure, rollbackFailure));
                }

                throw;
            }
        }

        private static void ThrowIfInjected(string stage)
        {
            if (string.Equals(
                    FaultInjectionStage,
                    stage,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Injected G3 Chain failure at " + stage + ".");
            }
        }

        private static void RestoreBefore(
            string absoluteScenePath,
            byte[] beforeBytes,
            string beforeSha,
            bool injectRollbackFailure)
        {
            File.WriteAllBytes(absoluteScenePath, beforeBytes);
            AssetDatabase.ImportAsset(
                ScenePath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Require(string.Equals(
                    Sha256(File.ReadAllBytes(absoluteScenePath)),
                    beforeSha,
                    StringComparison.Ordinal),
                "Presentation Chain rollback did not restore the before SHA.");
            RequireFormalScene();
            if (injectRollbackFailure)
            {
                throw new InvalidOperationException(
                    "Injected rollback verification failure after byte restoration.");
            }
        }

        internal static PresentationParityInvocationTraceEntry[]
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

        private static void RequireCleanPreflight()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                "Presentation Chain refuses PlayMode.");
            Require(!EditorApplication.isCompiling,
                "Presentation Chain refuses to run while compiling.");
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                Require(!scene.isDirty,
                    "Refusing to replace a Scene with unsaved user changes: " +
                    scene.path);
            }
        }

        private static Scene RequireComposableScene()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                "Presentation Chain refuses PlayMode.");
            Require(!EditorApplication.isCompiling,
                "Presentation Chain refuses to run while compiling.");
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded,
                "The presentation parity formal Scene is not loaded.");
            Require(string.Equals(scene.path, ScenePath, StringComparison.Ordinal),
                "Presentation Chain may only run on the formal Scene.");
            Require(SceneManager.sceneCount == 1,
                "Presentation Chain requires the formal Scene to be uniquely loaded.");
            return scene;
        }

        private static Scene RequireFormalScene()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                "Presentation Chain refuses PlayMode.");
            Require(!EditorApplication.isCompiling,
                "Presentation Chain refuses to run while compiling.");
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded,
                "The presentation parity formal Scene is not loaded.");
            Require(string.Equals(scene.path, ScenePath, StringComparison.Ordinal),
                "Presentation Chain may only run on the formal Scene.");
            Require(SceneManager.sceneCount == 1,
                "Presentation Chain requires the formal Scene to be uniquely loaded.");
            Require(!scene.isDirty,
                "Presentation Chain refuses a dirty formal Scene.");
            return scene;
        }

        private static string AbsoluteScenePath()
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

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
