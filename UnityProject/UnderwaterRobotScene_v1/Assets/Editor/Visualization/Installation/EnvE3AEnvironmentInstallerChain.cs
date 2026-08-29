using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    internal enum EnvE3AEnvironmentInstallMode
    {
        MutationAllowed,
        RequireNoOp
    }

    internal sealed class EnvE3AEnvironmentInstallerChainException :
        InvalidOperationException
    {
        private readonly EnvE3AEnvironmentInvocationTraceEntry[] trace;

        internal EnvE3AEnvironmentInstallerChainException(
            string failureStage,
            string message,
            IEnumerable<EnvE3AEnvironmentInvocationTraceEntry>
                invocationTrace,
            Exception innerException = null)
            : base(message, innerException)
        {
            FailureStage = failureStage;
            trace = invocationTrace?
                .Select(entry => entry.Copy())
                .ToArray() ??
                Array.Empty<EnvE3AEnvironmentInvocationTraceEntry>();
        }

        internal string FailureStage { get; }

        internal EnvE3AEnvironmentInvocationTraceEntry[]
            CopyActualInvocationTrace()
        {
            return trace.Select(entry => entry.Copy()).ToArray();
        }
    }

    internal sealed class EnvE3AEnvironmentInvocationTraceEntry
    {
        internal EnvE3AEnvironmentInvocationTraceEntry(
            int ordinal,
            string stageName,
            string declaringType,
            string methodName,
            bool completed,
            bool changed,
            int sceneSaveCallbackCount)
        {
            Ordinal = ordinal;
            StageName = stageName;
            DeclaringType = declaringType;
            MethodName = methodName;
            Completed = completed;
            Changed = changed;
            SceneSaveCallbackCount = sceneSaveCallbackCount;
        }

        internal int Ordinal { get; }
        internal string StageName { get; }
        internal string DeclaringType { get; }
        internal string MethodName { get; }
        internal bool Completed { get; }
        internal bool Changed { get; }
        internal int SceneSaveCallbackCount { get; }
        internal string MethodIdentity =>
            DeclaringType + "." + MethodName;

        internal EnvE3AEnvironmentInvocationTraceEntry Copy()
        {
            return new EnvE3AEnvironmentInvocationTraceEntry(
                Ordinal,
                StageName,
                DeclaringType,
                MethodName,
                Completed,
                Changed,
                SceneSaveCallbackCount);
        }
    }

    internal sealed class EnvE3AEnvironmentInstallResult
    {
        internal bool AnyChanged;
        internal int LegacyRemovedCount;
        internal string[] LegacyRemovedPaths;
        internal string ConfigurationSha256;
        internal string ContactMeshSha256;
        internal string FarMeshSha256;
        internal EnvE3AEnvironmentInvocationTraceEntry[] InvocationTrace;
    }

    internal sealed class EnvE3APostVehicleLayoutResult
    {
        internal bool Changed;
        internal bool StatusPanelMoved;
        internal EnvE3AEnvironmentInvocationTraceEntry InvocationTrace;
    }

    internal static class EnvE3AEnvironmentInstallerChain
    {
        private sealed class SceneSaveObserver : IDisposable
        {
            internal int Count { get; private set; }

            internal SceneSaveObserver()
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

        private sealed class LegacyRemovalResult
        {
            internal bool Changed;
            internal string[] RemovedPaths;
        }

        private static readonly string[] InvocationOrder =
        {
            "LegacyRemoval",
            "EnvE2ASceneInstaller",
            "EnvE2BSceneInstaller",
            "EnvE2CSceneInstaller",
            "EnvE2DPreVehicle"
        };
        private const string PostVehicleLayoutStage =
            "EnvE2DPostVehicleLayout";

        private static readonly string[] LegacyRootNames =
        {
            "Water_Backdrop",
            "Water_Left_Wall",
            "Water_Right_Wall",
            "Seabed_Ridge_Back",
            "Seabed_Ridge_Front",
            "Seabed_Rock_Left",
            "Seabed_Rock_Right"
        };

        private static readonly Func<LegacyRemovalResult> LegacyIdentity =
            RemoveExactLegacyRoots;
        private static readonly
            Func<Scene, EnvE2AConfiguration, EnvE2AInstallResult>
            E2AIdentity = EnvE2ASceneInstaller.InstallIntoLoadedScene;
        private static readonly
            Func<Scene, EnvE2BConfiguration, EnvE2BInstallResult>
            E2BIdentity = EnvE2BSceneInstaller.InstallIntoLoadedScene;
        private static readonly
            Func<Scene, EnvE2CProfile, EnvE2CInstallResult>
            E2CIdentity = EnvE2CSceneInstaller.Apply;
        private static readonly Func<Scene, EnvE2DInstallResult>
            E2DIdentity = EnvE2DSceneInstaller.ApplyPreVehicle;
        private static readonly Func<Scene, EnvE2DInstallResult>
            E2DPostIdentity = EnvE2DSceneInstaller.ApplyPostVehicleLayout;

        private static EnvE3AEnvironmentInvocationTraceEntry[] lastTrace =
            Array.Empty<EnvE3AEnvironmentInvocationTraceEntry>();
        private static string verifierFaultStage = string.Empty;
        private static string verifierFaultKind = string.Empty;

        internal static EnvE3AEnvironmentInstallResult Execute(
            EnvE3AEnvironmentInstallMode mode)
        {
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded,
                "INITIAL_SCENE_STATE",
                "E3A environment chain requires one active loaded Scene.");
            if (mode == EnvE3AEnvironmentInstallMode.RequireNoOp &&
                scene.isDirty)
            {
                throw new EnvE3AEnvironmentInstallerChainException(
                    "INITIAL_SCENE_STATE",
                    "RequireNoOp requires a clean Scene at entry.",
                    Array.Empty<EnvE3AEnvironmentInvocationTraceEntry>());
            }

            EnvE3AContinuousSeabedConfiguration authority =
                EnvE3AContinuousSeabedConfiguration.CreateApproved();
            var trace =
                new List<EnvE3AEnvironmentInvocationTraceEntry>();
            LegacyRemovalResult legacy = null;
            EnvE2AInstallResult e2a = null;
            EnvE2BInstallResult e2b = null;
            using (var saveObserver = new SceneSaveObserver())
            {
                try
                {
                    legacy = Invoke(
                        InvocationOrder[0],
                        RemoveExactLegacyRoots,
                        LegacyIdentity.Method,
                        result => result.Changed,
                        trace,
                        saveObserver);
                    EnforceStagePolicy(mode, scene, trace, saveObserver);

                    EnvE2AConfiguration nearConfiguration =
                        EnvE2AConfiguration.CreateApproved();
                    e2a = Invoke(
                        InvocationOrder[1],
                        () => EnvE2ASceneInstaller.InstallIntoLoadedScene(
                            scene,
                            nearConfiguration),
                        E2AIdentity.Method,
                        result => result.Changed,
                        trace,
                        saveObserver);
                    EnforceStagePolicy(mode, scene, trace, saveObserver);

                    EnvE2BConfiguration farConfiguration =
                        EnvE2BConfiguration.CreateApproved();
                    e2b = Invoke(
                        InvocationOrder[2],
                        () => EnvE2BSceneInstaller.InstallIntoLoadedScene(
                            scene,
                            farConfiguration),
                        E2BIdentity.Method,
                        result => result.Changed,
                        trace,
                        saveObserver);
                    EnforceStagePolicy(mode, scene, trace, saveObserver);

                    Invoke(
                        InvocationOrder[3],
                        () => EnvE2CSceneInstaller.Apply(
                            scene,
                            EnvE2CProfile.CreateApproved()),
                        E2CIdentity.Method,
                        result => result.Changed,
                        trace,
                        saveObserver);
                    EnforceStagePolicy(mode, scene, trace, saveObserver);

                    Invoke(
                        InvocationOrder[4],
                        () => EnvE2DSceneInstaller.ApplyPreVehicle(scene),
                        E2DIdentity.Method,
                        result => result.Changed,
                        trace,
                        saveObserver);
                    EnforceStagePolicy(mode, scene, trace, saveObserver);
                }
                catch (EnvE3AEnvironmentInstallerChainException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    string stage = trace.Count == 0
                        ? "INITIAL_SCENE_STATE"
                        : trace[trace.Count - 1].StageName;
                    throw new EnvE3AEnvironmentInstallerChainException(
                        stage,
                        "E3A environment stage failed: " + stage +
                        " | " + exception.Message,
                        trace,
                        exception);
                }
                finally
                {
                    lastTrace = trace.Select(entry => entry.Copy()).ToArray();
                }
            }

            ValidateCompletedTrace(trace);
            return new EnvE3AEnvironmentInstallResult
            {
                AnyChanged = trace.Any(entry => entry.Changed),
                LegacyRemovedCount = legacy.RemovedPaths.Length,
                LegacyRemovedPaths = legacy.RemovedPaths.ToArray(),
                ConfigurationSha256 = authority.Sha256(),
                ContactMeshSha256 = e2a.TerrainMeshSha256,
                FarMeshSha256 = e2b.MeshSha256,
                InvocationTrace = trace.Select(entry => entry.Copy()).ToArray()
            };
        }

        internal static EnvE3APostVehicleLayoutResult
            ExecutePostVehicleLayout(EnvE3AEnvironmentInstallMode mode)
        {
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded,
                "INITIAL_SCENE_STATE",
                "E3A post-vehicle layout requires one active loaded Scene.");
            if (mode == EnvE3AEnvironmentInstallMode.RequireNoOp &&
                scene.isDirty)
            {
                throw new EnvE3AEnvironmentInstallerChainException(
                    "INITIAL_SCENE_STATE",
                    "RequireNoOp requires a clean Scene at entry.",
                    Array.Empty<EnvE3AEnvironmentInvocationTraceEntry>());
            }

            var trace =
                new List<EnvE3AEnvironmentInvocationTraceEntry>();
            EnvE2DInstallResult result = null;
            using (var saveObserver = new SceneSaveObserver())
            {
                try
                {
                    result = Invoke(
                        PostVehicleLayoutStage,
                        () => EnvE2DSceneInstaller.ApplyPostVehicleLayout(scene),
                        E2DPostIdentity.Method,
                        value => value.Changed,
                        trace,
                        saveObserver);
                    EnforceStagePolicy(mode, scene, trace, saveObserver);
                }
                catch (EnvE3AEnvironmentInstallerChainException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new EnvE3AEnvironmentInstallerChainException(
                        PostVehicleLayoutStage,
                        "E3A post-vehicle layout failed: " +
                        exception.Message,
                        trace,
                        exception);
                }
            }

            Require(trace.Count == 1 && trace[0].Completed &&
                    string.Equals(trace[0].StageName,
                        PostVehicleLayoutStage,
                        StringComparison.Ordinal) &&
                    trace[0].SceneSaveCallbackCount == 0,
                "ACTUAL_CALL_TRACE",
                "E3A post-vehicle layout trace is invalid.");
            return new EnvE3APostVehicleLayoutResult
            {
                Changed = result.Changed,
                StatusPanelMoved = result.StatusPanelMoved,
                InvocationTrace = trace[0].Copy()
            };
        }

        internal static string[] CopyInvocationOrder()
        {
            return InvocationOrder.ToArray();
        }

        internal static string PostVehicleLayoutStageName =>
            PostVehicleLayoutStage;

        internal static EnvE3AEnvironmentInvocationTraceEntry[]
            CopyLastActualInvocationTrace()
        {
            return lastTrace.Select(entry => entry.Copy()).ToArray();
        }

        internal static void ConfigureVerifierFaultInjection(
            string stageName,
            string faultKind)
        {
            Require(string.IsNullOrEmpty(verifierFaultStage) &&
                    (InvocationOrder.Contains(
                         stageName,
                         StringComparer.Ordinal) ||
                     string.Equals(stageName,
                         PostVehicleLayoutStage,
                         StringComparison.Ordinal)) &&
                    new[] { "Changed", "Dirty", "SaveCallback" }.Contains(
                        faultKind,
                        StringComparer.Ordinal),
                "VERIFIER_FAULT_INJECTION",
                "Invalid or overlapping environment verifier fault.");
            verifierFaultStage = stageName;
            verifierFaultKind = faultKind;
        }

        private static LegacyRemovalResult RemoveExactLegacyRoots()
        {
            Scene scene = SceneManager.GetActiveScene();
            var removed = new List<string>();
            foreach (string name in LegacyRootNames)
            {
                GameObject[] matches = scene.GetRootGameObjects()
                    .Where(root => string.Equals(
                        root.name,
                        name,
                        StringComparison.Ordinal))
                    .ToArray();
                if (matches.Length > 1)
                {
                    throw new InvalidOperationException(
                        "Duplicate exact legacy root /" + name + ".");
                }
                if (matches.Length == 0)
                {
                    continue;
                }
                UnityEngine.Object.DestroyImmediate(matches[0]);
                removed.Add("/" + name);
            }
            if (removed.Count > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }
            return new LegacyRemovalResult
            {
                Changed = removed.Count > 0,
                RemovedPaths = removed.ToArray()
            };
        }

        private static T Invoke<T>(
            string stageName,
            Func<T> invocation,
            MethodInfo identity,
            Func<T, bool> changedSelector,
            ICollection<EnvE3AEnvironmentInvocationTraceEntry> trace,
            SceneSaveObserver saveObserver)
        {
            int ordinal = trace.Count + 1;
            int savesBefore = saveObserver.Count;
            bool completed = false;
            bool changed = false;
            try
            {
                T result = invocation();
                changed = changedSelector(result);
                ApplyVerifierFaultInjection(stageName, ref changed);
                completed = true;
                return result;
            }
            finally
            {
                trace.Add(new EnvE3AEnvironmentInvocationTraceEntry(
                    ordinal,
                    stageName,
                    identity.DeclaringType?.FullName ?? string.Empty,
                    identity.Name,
                    completed,
                    changed,
                    saveObserver.Count - savesBefore));
            }
        }

        private static void ApplyVerifierFaultInjection(
            string stageName,
            ref bool changed)
        {
            if (!string.Equals(
                    verifierFaultStage,
                    stageName,
                    StringComparison.Ordinal))
            {
                return;
            }
            string kind = verifierFaultKind;
            verifierFaultStage = string.Empty;
            verifierFaultKind = string.Empty;
            if (string.Equals(kind, "Changed", StringComparison.Ordinal))
            {
                changed = true;
                return;
            }
            Scene scene = SceneManager.GetActiveScene();
            if (string.Equals(kind, "Dirty", StringComparison.Ordinal))
            {
                EditorSceneManager.MarkSceneDirty(scene);
                return;
            }
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new InvalidOperationException(
                    "Verifier save-callback injection failed to save Scene.");
            }
        }

        private static void EnforceStagePolicy(
            EnvE3AEnvironmentInstallMode mode,
            Scene scene,
            IReadOnlyList<EnvE3AEnvironmentInvocationTraceEntry> trace,
            SceneSaveObserver saveObserver)
        {
            EnvE3AEnvironmentInvocationTraceEntry stage =
                trace[trace.Count - 1];
            if (stage.SceneSaveCallbackCount != 0 ||
                saveObserver.Count != 0)
            {
                throw new EnvE3AEnvironmentInstallerChainException(
                    stage.StageName,
                    "E3A environment stage attempted to save the Scene: " +
                    stage.StageName + ".",
                    trace);
            }
            if (mode == EnvE3AEnvironmentInstallMode.RequireNoOp &&
                (stage.Changed || scene.isDirty))
            {
                throw new EnvE3AEnvironmentInstallerChainException(
                    stage.StageName,
                    "RequireNoOp observed mutation/dirty state at stage " +
                    stage.StageName + ".",
                    trace);
            }
        }

        private static void ValidateCompletedTrace(
            IReadOnlyList<EnvE3AEnvironmentInvocationTraceEntry> trace)
        {
            Require(trace.Count == InvocationOrder.Length,
                "ACTUAL_CALL_TRACE",
                "Environment chain did not complete exactly five stages.");
            for (int index = 0; index < InvocationOrder.Length; index++)
            {
                EnvE3AEnvironmentInvocationTraceEntry entry = trace[index];
                Require(entry.Ordinal == index + 1 && entry.Completed &&
                        string.Equals(entry.StageName,
                            InvocationOrder[index],
                            StringComparison.Ordinal) &&
                        entry.SceneSaveCallbackCount == 0,
                    "ACTUAL_CALL_TRACE",
                    "Environment trace is invalid at " +
                    InvocationOrder[index] + ".");
            }
            Require(trace.Select(entry => entry.MethodIdentity)
                    .Distinct(StringComparer.Ordinal).Count() ==
                    InvocationOrder.Length,
                "ACTUAL_CALL_TRACE",
                "Environment trace method identities are not unique.");
        }

        private static void Require(
            bool condition,
            string stage,
            string message)
        {
            if (!condition)
            {
                throw new EnvE3AEnvironmentInstallerChainException(
                    stage,
                    message,
                    Array.Empty<EnvE3AEnvironmentInvocationTraceEntry>());
            }
        }
    }
}
