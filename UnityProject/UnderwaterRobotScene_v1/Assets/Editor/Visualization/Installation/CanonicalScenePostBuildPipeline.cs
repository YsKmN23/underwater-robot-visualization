using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    internal enum CanonicalScenePostBuildMode
    {
        MutationAllowed,
        RequireNoOp
    }

    internal sealed class CanonicalScenePostBuildPipelineException :
        InvalidOperationException
    {
        internal CanonicalScenePostBuildPipelineException(
            string failureStage,
            string message,
            IEnumerable<CanonicalScenePostBuildInvocationTraceEntry>
                actualInvocationTrace = null)
            : base(message)
        {
            FailureStage = failureStage;
            this.actualInvocationTrace = actualInvocationTrace?
                .Select(entry => entry.Copy())
                .ToArray() ??
                Array.Empty<CanonicalScenePostBuildInvocationTraceEntry>();
        }

        private readonly CanonicalScenePostBuildInvocationTraceEntry[]
            actualInvocationTrace;
        internal string FailureStage { get; }

        internal CanonicalScenePostBuildInvocationTraceEntry[]
            CopyActualInvocationTrace()
        {
            return actualInvocationTrace
                .Select(entry => entry.Copy())
                .ToArray();
        }
    }

    internal sealed class CanonicalScenePostBuildInvocationTraceEntry
    {
        internal CanonicalScenePostBuildInvocationTraceEntry(
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

        internal CanonicalScenePostBuildInvocationTraceEntry Copy()
        {
            return new CanonicalScenePostBuildInvocationTraceEntry(
                Ordinal,
                StageName,
                DeclaringType,
                MethodName,
                Completed,
                Changed,
                SceneSaveCallbackCount);
        }
    }

    internal sealed class CanonicalScenePostBuildPipelineResult
    {
        private readonly CanonicalScenePostBuildInvocationTraceEntry[]
            actualInvocationTrace;
        private readonly string[] invocationOrder;
        private readonly UsvPostBuildInvocationTraceEntry[]
            usvNestedInvocationTrace;
        private readonly PresentationParityInvocationTraceEntry[]
            presentationNestedInvocationTrace;

        internal CanonicalScenePostBuildPipelineResult(
            CanonicalScenePostBuildMode mode,
            IEnumerable<CanonicalScenePostBuildInvocationTraceEntry> trace,
            int sceneSaveCallbackCount,
            IEnumerable<UsvPostBuildInvocationTraceEntry> usvNestedTrace,
            int usvChainSceneSaveCallbackCount,
            IEnumerable<PresentationParityInvocationTraceEntry>
                presentationNestedTrace,
            int presentationChainSceneSaveCallbackCount)
        {
            Mode = mode;
            actualInvocationTrace = trace
                .Select(entry => entry.Copy())
                .ToArray();
            invocationOrder = actualInvocationTrace
                .Select(entry => entry.StageName)
                .ToArray();
            usvNestedInvocationTrace = usvNestedTrace
                .Select(entry => entry.Copy())
                .ToArray();
            presentationNestedInvocationTrace = presentationNestedTrace
                .Select(entry => entry.Copy())
                .ToArray();
            AuvChanged = actualInvocationTrace[0].Changed;
            RovChanged = actualInvocationTrace[1].Changed;
            RovM1CChanged = actualInvocationTrace[2].Changed;
            UsvChanged = actualInvocationTrace[3].Changed;
            PresentationChanged = actualInvocationTrace[4].Changed;
            V1StatusPanelChanged = actualInvocationTrace[5].Changed;
            AnyChanged = actualInvocationTrace.Any(entry => entry.Changed);
            SceneSaved = false;
            TargetWriteAttempted = false;
            SceneSaveCallbackCount = sceneSaveCallbackCount;
            UsvChainSceneSaveCallbackCount =
                usvChainSceneSaveCallbackCount;
            PresentationChainSceneSaveCallbackCount =
                presentationChainSceneSaveCallbackCount;
            FailureStage = string.Empty;
        }

        internal CanonicalScenePostBuildMode Mode { get; }
        internal bool AuvChanged { get; }
        internal bool RovChanged { get; }
        internal bool RovM1CChanged { get; }
        internal bool UsvChanged { get; }
        internal bool PresentationChanged { get; }
        internal bool V1StatusPanelChanged { get; }
        internal bool AnyChanged { get; }
        internal bool SceneSaved { get; }
        internal bool TargetWriteAttempted { get; }
        internal int SceneSaveCallbackCount { get; }
        internal int UsvChainSceneSaveCallbackCount { get; }
        internal int PresentationChainSceneSaveCallbackCount { get; }
        internal string FailureStage { get; }

        internal string[] CopyInvocationOrder()
        {
            return invocationOrder.ToArray();
        }

        internal CanonicalScenePostBuildInvocationTraceEntry[]
            CopyActualInvocationTrace()
        {
            return actualInvocationTrace
                .Select(entry => entry.Copy())
                .ToArray();
        }

        internal UsvPostBuildInvocationTraceEntry[]
            CopyUsvNestedInvocationTrace()
        {
            return usvNestedInvocationTrace
                .Select(entry => entry.Copy())
                .ToArray();
        }

        internal PresentationParityInvocationTraceEntry[]
            CopyPresentationNestedInvocationTrace()
        {
            return presentationNestedInvocationTrace
                .Select(entry => entry.Copy())
                .ToArray();
        }
    }

    internal static class CanonicalScenePostBuildPipeline
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

        private static CanonicalScenePostBuildInvocationTraceEntry[]
            lastActualInvocationTrace =
                Array.Empty<CanonicalScenePostBuildInvocationTraceEntry>();
        private static int lastSceneSaveCallbackCount;

        private static readonly string[] InvocationOrder =
        {
            "AuvPublicPoseN5SceneInstaller",
            "RovRootPoseN6BSceneInstaller",
            "RovThrusterVisualM1CSceneInstaller",
            "UsvPostBuildInstallerChain",
            "CanonicalModelPresentationParityInstallerChain",
            "VehicleStatusPanelV1SceneInstaller"
        };

        private static readonly string[] ExpectedMethodIdentities =
        {
            typeof(AuvPublicPoseN5SceneInstaller).FullName + "." +
            nameof(AuvPublicPoseN5SceneInstaller
                .InstallForCanonicalSceneRebuild),
            typeof(RovRootPoseN6BSceneInstaller).FullName + "." +
            nameof(RovRootPoseN6BSceneInstaller
                .InstallForCanonicalSceneRebuild),
            typeof(RovThrusterVisualM1CSceneInstaller).FullName + "." +
            nameof(RovThrusterVisualM1CSceneInstaller
                .InstallForCanonicalSceneRebuild),
            typeof(UsvPostBuildInstallerChain).FullName + "." +
            nameof(UsvPostBuildInstallerChain
                .InstallCanonicalUsvPostBuildChain),
            typeof(CanonicalModelPresentationParityInstallerChain).FullName +
            "." +
            nameof(CanonicalModelPresentationParityInstallerChain
                .InstallCanonicalModelPresentationParity),
            typeof(VehicleStatusPanelV1SceneInstaller).FullName + "." +
            nameof(VehicleStatusPanelV1SceneInstaller
                .InstallForCanonicalSceneRebuild)
        };

        private static readonly Func<bool> AuvInvocation =
            () => AuvPublicPoseN5SceneInstaller
                .InstallForCanonicalSceneRebuild();
        private static readonly Func<bool> AuvIdentity =
            AuvPublicPoseN5SceneInstaller.InstallForCanonicalSceneRebuild;
        private static readonly Func<bool> RovInvocation =
            () => RovRootPoseN6BSceneInstaller
                .InstallForCanonicalSceneRebuild();
        private static readonly Func<bool> RovIdentity =
            RovRootPoseN6BSceneInstaller.InstallForCanonicalSceneRebuild;
        private static readonly Func<bool> RovM1CInvocation =
            () => RovThrusterVisualM1CSceneInstaller
                .InstallForCanonicalSceneRebuild();
        private static readonly Func<bool> RovM1CIdentity =
            RovThrusterVisualM1CSceneInstaller
                .InstallForCanonicalSceneRebuild;
        private static readonly Func<UsvPostBuildInstallerChainResult>
            UsvInvocation =
                () => UsvPostBuildInstallerChain
                    .InstallCanonicalUsvPostBuildChain();
        private static readonly Func<UsvPostBuildInstallerChainResult>
            UsvIdentity =
                UsvPostBuildInstallerChain
                    .InstallCanonicalUsvPostBuildChain;
        private static readonly
            Func<CanonicalModelPresentationParityResult>
            PresentationInvocation =
                () => CanonicalModelPresentationParityInstallerChain
                    .InstallCanonicalModelPresentationParity();
        private static readonly
            Func<CanonicalModelPresentationParityResult>
            PresentationIdentity =
                CanonicalModelPresentationParityInstallerChain
                    .InstallCanonicalModelPresentationParity;
        private static readonly Func<bool> V1StatusPanelInvocation =
            () => VehicleStatusPanelV1SceneInstaller
                .InstallForCanonicalSceneRebuild();
        private static readonly Func<bool> V1StatusPanelIdentity =
            VehicleStatusPanelV1SceneInstaller
                .InstallForCanonicalSceneRebuild;

        internal static string[] CopyInvocationOrder()
        {
            return InvocationOrder.ToArray();
        }

        internal static CanonicalScenePostBuildInvocationTraceEntry[]
            CopyLastActualInvocationTrace()
        {
            return lastActualInvocationTrace
                .Select(entry => entry.Copy())
                .ToArray();
        }

        internal static int LastSceneSaveCallbackCount()
        {
            return lastSceneSaveCallbackCount;
        }

        internal static CanonicalScenePostBuildPipelineResult Execute(
            CanonicalScenePostBuildMode mode)
        {
            var trace =
                new List<CanonicalScenePostBuildInvocationTraceEntry>();
            UsvPostBuildInstallerChainResult usv = null;
            CanonicalModelPresentationParityResult presentation = null;
            using (var saveObserver = new SceneSaveObserver())
            {
                try
                {
                    Invoke(
                        InvocationOrder[0],
                        AuvInvocation,
                        AuvIdentity,
                        changed => changed,
                        trace,
                        saveObserver);
                    Invoke(
                        InvocationOrder[1],
                        RovInvocation,
                        RovIdentity,
                        changed => changed,
                        trace,
                        saveObserver);
                    Invoke(
                        InvocationOrder[2],
                        RovM1CInvocation,
                        RovM1CIdentity,
                        changed => changed,
                        trace,
                        saveObserver);

                    usv = Invoke(
                        InvocationOrder[3],
                        UsvInvocation,
                        UsvIdentity,
                        result => result.AnyChanged,
                        trace,
                        saveObserver);
                    Require(usv.Success,
                        "USV chain returned an unsuccessful result.");

                    presentation = Invoke(
                        InvocationOrder[4],
                        PresentationInvocation,
                        PresentationIdentity,
                        result => result.AnyChanged,
                        trace,
                        saveObserver);
                    Require(presentation.Success,
                        "Presentation chain returned an unsuccessful result.");

                    Invoke(
                        InvocationOrder[5],
                        V1StatusPanelInvocation,
                        V1StatusPanelIdentity,
                        changed => changed,
                        trace,
                        saveObserver);
                }
                finally
                {
                    lastActualInvocationTrace = trace
                        .Select(entry => entry.Copy())
                        .ToArray();
                    lastSceneSaveCallbackCount = saveObserver.Count;
                }
            }

            ValidateCompletedTrace(trace);
            ValidateUsvNestedTrace(usv);
            ValidatePresentationNestedTrace(presentation);
            var result = new CanonicalScenePostBuildPipelineResult(
                mode,
                trace,
                lastSceneSaveCallbackCount,
                usv.InvocationTrace,
                usv.SceneSaveCallbackCount,
                presentation.InvocationTrace,
                presentation.SceneSaveCallbackCount);
            ValidateResultProvenance(result);
            if (mode == CanonicalScenePostBuildMode.RequireNoOp &&
                result.AnyChanged)
            {
                throw new CanonicalScenePostBuildPipelineException(
                    "POST_BUILD_NO_OP",
                    "Canonical post-build validation was not a complete " +
                    "no-op.",
                    trace);
            }

            return result;
        }

        private static T Invoke<T>(
            string stageName,
            Func<T> invocation,
            Func<T> identity,
            Func<T, bool> changedSelector,
            ICollection<CanonicalScenePostBuildInvocationTraceEntry> trace,
            SceneSaveObserver saveObserver)
        {
            int ordinal = trace.Count + 1;
            int savesBefore = saveObserver.Count;
            T returnedResult = default;
            bool completed = false;
            bool changed = false;
            try
            {
                returnedResult = invocation();
                changed = changedSelector(returnedResult);
                completed = true;
                return returnedResult;
            }
            finally
            {
                trace.Add(new CanonicalScenePostBuildInvocationTraceEntry(
                    ordinal,
                    stageName,
                    identity.Method.DeclaringType?.FullName ??
                    string.Empty,
                    identity.Method.Name,
                    completed,
                    changed,
                    saveObserver.Count - savesBefore));
            }
        }

        private static void ValidateCompletedTrace(
            IReadOnlyList<CanonicalScenePostBuildInvocationTraceEntry> trace)
        {
            Require(trace.Count == InvocationOrder.Length,
                "Post-build runtime trace did not complete exactly six " +
                "installer calls.");
            Require(trace.Select(entry => entry.MethodIdentity)
                    .Distinct(StringComparer.Ordinal)
                    .Count() == ExpectedMethodIdentities.Length,
                "Post-build runtime trace contains a duplicate installer " +
                "method identity.");

            for (int index = 0; index < InvocationOrder.Length; index++)
            {
                CanonicalScenePostBuildInvocationTraceEntry entry =
                    trace[index];
                Require(entry.Completed,
                    "Post-build runtime trace contains an incomplete call.");
                Require(entry.Ordinal == index + 1,
                    "Post-build runtime trace ordinal is incorrect.");
                Require(string.Equals(
                        entry.StageName,
                        InvocationOrder[index],
                        StringComparison.Ordinal),
                    "Post-build runtime trace stage order is incorrect.");
                Require(string.Equals(
                        entry.MethodIdentity,
                        ExpectedMethodIdentities[index],
                        StringComparison.Ordinal),
                    "Post-build runtime trace method identity is incorrect.");
                Require(entry.SceneSaveCallbackCount == 0,
                    "A post-build stage observed a Scene save.");
            }
        }

        private static void ValidateUsvNestedTrace(
            UsvPostBuildInstallerChainResult result)
        {
            Require(result != null && result.Success,
                "The USV nested result is unavailable or unsuccessful.");
            UsvPostBuildInvocationTraceEntry[] trace =
                result.InvocationTrace;
            string[] expected =
            {
                typeof(UsvRootPoseN6DSceneInstaller).FullName + "." +
                nameof(UsvRootPoseN6DSceneInstaller
                    .InstallForCanonicalPostBuildChain),
                typeof(UsvSurfaceVisualM2CSceneInstaller).FullName + "." +
                nameof(UsvSurfaceVisualM2CSceneInstaller
                    .InstallForCanonicalPostBuildChain),
                typeof(UsvActuatorVisualM2DSceneInstaller).FullName + "." +
                nameof(UsvActuatorVisualM2DSceneInstaller
                    .InstallForCanonicalPostBuildChain)
            };
            bool[] changed =
            {
                result.N6DChanged,
                result.M2CChanged,
                result.M2DChanged
            };
            Require(trace.Length == expected.Length,
                "USV nested runtime trace did not complete exactly three calls.");
            for (int index = 0; index < expected.Length; index++)
            {
                UsvPostBuildInvocationTraceEntry entry = trace[index];
                Require(entry.Completed && entry.Ordinal == index + 1,
                    "USV nested runtime trace completion or ordinal is incorrect.");
                Require(string.Equals(
                        entry.DeclaringType + "." + entry.MethodName,
                        expected[index],
                        StringComparison.Ordinal),
                    "USV nested runtime trace method identity is incorrect.");
                Require(entry.Changed == changed[index],
                    "USV nested changed provenance is incorrect.");
                Require(entry.SceneSaveCallbackCount == 0,
                    "A USV nested call observed a Scene save.");
            }

            Require(result.AnyChanged == changed.Any(value => value),
                "USV chain aggregate changed provenance is incorrect.");
            Require(!result.SceneSaved &&
                    result.SceneSaveCallbackCount == 0,
                "USV chain must be zero-write.");
        }

        private static void ValidatePresentationNestedTrace(
            CanonicalModelPresentationParityResult result)
        {
            Require(result != null && result.Success,
                "The presentation nested result is unavailable or unsuccessful.");
            PresentationParityInvocationTraceEntry[] trace =
                result.InvocationTrace;
            string[] expected =
            {
                typeof(AuvModelPresentationParityInstaller).FullName + "." +
                nameof(AuvModelPresentationParityInstaller
                    .InstallForCanonicalSceneRebuild),
                typeof(RovModelPresentationParityInstaller).FullName + "." +
                nameof(RovModelPresentationParityInstaller
                    .InstallForCanonicalSceneRebuild),
                typeof(UsvModelPresentationParityInstaller).FullName + "." +
                nameof(UsvModelPresentationParityInstaller
                    .InstallForCanonicalSceneRebuild)
            };
            bool[] changed =
            {
                result.AuvChanged,
                result.RovChanged,
                result.UsvChanged
            };
            Require(trace.Length == expected.Length,
                "Presentation nested runtime trace did not complete exactly " +
                "three calls.");
            for (int index = 0; index < expected.Length; index++)
            {
                PresentationParityInvocationTraceEntry entry = trace[index];
                Require(entry.Completed && entry.Ordinal == index + 1,
                    "Presentation nested runtime trace completion or ordinal " +
                    "is incorrect.");
                Require(string.Equals(
                        entry.DeclaringType + "." + entry.MethodName,
                        expected[index],
                        StringComparison.Ordinal),
                    "Presentation nested runtime trace method identity is incorrect.");
                Require(entry.Changed == changed[index],
                    "Presentation nested changed provenance is incorrect.");
                Require(entry.SceneSaveCallbackCount == 0,
                    "A presentation nested call observed a Scene save.");
            }

            Require(result.AnyChanged == changed.Any(value => value),
                "Presentation chain aggregate changed provenance is incorrect.");
            Require(!result.SceneSaved &&
                    result.SceneSaveCallbackCount == 0,
                "Presentation chain must be zero-write.");
        }

        private static void ValidateResultProvenance(
            CanonicalScenePostBuildPipelineResult result)
        {
            CanonicalScenePostBuildInvocationTraceEntry[] trace =
                result.CopyActualInvocationTrace();
            bool[] returnedChanged =
            {
                result.AuvChanged,
                result.RovChanged,
                result.RovM1CChanged,
                result.UsvChanged,
                result.PresentationChanged,
                result.V1StatusPanelChanged
            };
            for (int index = 0; index < trace.Length; index++)
            {
                Require(returnedChanged[index] == trace[index].Changed,
                    "Post-build result changed provenance is incorrect.");
            }

            Require(
                result.AnyChanged ==
                trace.Any(entry => entry.Changed),
                "Post-build aggregate changed provenance is incorrect.");
            Require(!result.SceneSaved && !result.TargetWriteAttempted,
                "Post-build pipeline must not own Scene or target writes.");
            Require(trace.All(entry =>
                    entry.SceneSaveCallbackCount == 0) &&
                    result.SceneSaveCallbackCount == 0 &&
                    result.UsvChainSceneSaveCallbackCount == 0 &&
                    result.PresentationChainSceneSaveCallbackCount == 0,
                "Post-build save callback provenance is not zero-write.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new CanonicalScenePostBuildPipelineException(
                    "ACTUAL_CALL_TRACE",
                    message);
            }
        }
    }
}
