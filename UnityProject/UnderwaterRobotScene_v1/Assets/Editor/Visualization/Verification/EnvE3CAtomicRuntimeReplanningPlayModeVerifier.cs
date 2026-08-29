using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Constraints;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    [InitializeOnLoad]
    public static class EnvE3CAtomicRuntimeReplanningPlayModeVerifier
    {
        private const string ActiveKey = "E3C.AtomicReplan.Active";
        private const string BatchKey = "E3C.AtomicReplan.Batch";
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private enum Phase
        {
            WaitHealthy,
            ReplanAuv,
            ObserveAuv,
            ReplanRov,
            ObserveRov,
            ReplanUsv,
            ObserveUsv,
            InvalidFailure
        }

        private static bool subscribed;
        private static Phase phase;
        private static double phaseStarted;
        private static VehicleDataRuntimeHost auv;
        private static VehicleDataRuntimeHost rov;
        private static VehicleDataRuntimeHost usv;
        private static VehiclePoseDriver auvDriver;
        private static VehiclePoseDriver rovDriver;
        private static VehiclePoseDriver usvDriver;
        private static VehicleTrajectoryVisualizationController trajectory;
        private static Vector3 rootBefore;
        private static int trackBefore;
        private static ulong expectedSourceEpoch;
        private static ulong expectedRouteVersion;

        static EnvE3CAtomicRuntimeReplanningPlayModeVerifier()
        {
            if (SessionState.GetBool(ActiveKey, false)) Subscribe();
        }

        public static void RunBatch()
        {
            if (SessionState.GetBool(ActiveKey, false))
                throw new InvalidOperationException("E3C atomic replan verification is already active.");
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, true);
            Subscribe();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        private static void Subscribe()
        {
            if (subscribed) return;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnUpdate;
            subscribed = true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode ||
                !SessionState.GetBool(ActiveKey, false)) return;
            phase = Phase.WaitHealthy;
            phaseStarted = Time.realtimeSinceStartupAsDouble;
        }

        private static void OnUpdate()
        {
            if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying) return;
            try
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (now - phaseStarted > 20.0)
                    throw new InvalidOperationException("Timed out in Batch 3 phase " + phase + ".");
                switch (phase)
                {
                    case Phase.WaitHealthy:
                        if (!TryBind() || !Ready(auv, auvDriver) ||
                            !Ready(rov, rovDriver) || !Ready(usv, usvDriver)) return;
                        Advance(Phase.ReplanAuv, now);
                        break;
                    case Phase.ReplanAuv:
                        Replan(
                            auv, auvDriver, VehicleSelectionKind.Auv,
                            new Vector3d(2.5, 0.0, 2.0),
                            new Vector3d(5.0, 0.0, 4.5),
                            rov, usv);
                        Advance(Phase.ObserveAuv, now);
                        break;
                    case Phase.ObserveAuv:
                        if (now - phaseStarted < 0.8) return;
                        Observe(auv, auvDriver, VehicleSelectionKind.Auv, 2.0f);
                        Advance(Phase.ReplanRov, now);
                        break;
                    case Phase.ReplanRov:
                        if (!Ready(rov, rovDriver)) return;
                        Replan(
                            rov, rovDriver, VehicleSelectionKind.Rov,
                            new Vector3d(1.5, 0.0, 1.5),
                            new Vector3d(3.0, 0.0, 3.5),
                            auv, usv);
                        Advance(Phase.ObserveRov, now);
                        break;
                    case Phase.ObserveRov:
                        if (now - phaseStarted < 0.8) return;
                        Observe(rov, rovDriver, VehicleSelectionKind.Rov, 2.0f);
                        Require(rovDriver.HasPoseConstraint &&
                                rovDriver.LastPoseConstraintDecision !=
                                UnityPoseConstraintDecision.HoldCurrent,
                            "ROV atomic replan bypassed or entered terrain Hold.");
                        Require(Vector3.Dot(rovDriver.TargetRoot.up, Vector3.up) > 0.98f,
                            "ROV business root gained unexpected pitch/roll.");
                        Advance(Phase.ReplanUsv, now);
                        break;
                    case Phase.ReplanUsv:
                        if (!Ready(usv, usvDriver)) return;
                        Replan(
                            usv, usvDriver, VehicleSelectionKind.Usv,
                            new Vector3d(2.0, 20.0, 1.0),
                            new Vector3d(4.0, -20.0, 3.0),
                            auv, rov);
                        Advance(Phase.ObserveUsv, now);
                        break;
                    case Phase.ObserveUsv:
                        if (now - phaseStarted < 0.8) return;
                        Observe(usv, usvDriver, VehicleSelectionKind.Usv, 2.0f);
                        ActiveRouteSnapshot usvRoute = usv.ActiveRouteSnapshot;
                        double surfaceHeight = usvRoute.GetWaypoint(0).Y;
                        for (int index = 0; index < usvRoute.WaypointCount; index++)
                            Require(Math.Abs(usvRoute.GetWaypoint(index).Y - surfaceHeight) < 1e-5,
                                "USV Active Route retained visual-layer height.");
                        Require(Math.Abs(usvDriver.TargetRoot.position.y - (float)surfaceHeight) < 0.02f &&
                                Vector3.Dot(usvDriver.TargetRoot.up, Vector3.up) > 0.999f,
                            "USV business root did not remain water-height yaw-only.");
                        Advance(Phase.InvalidFailure, now);
                        break;
                    case Phase.InvalidFailure:
                        VerifyInvalidFailure();
                        Debug.Log("ENV_E3C_ATOMIC_RUNTIME_REPLANNING_PLAY_MODE_PASS");
                        Finish(0);
                        break;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("ENV_E3C_ATOMIC_RUNTIME_REPLANNING_PLAY_MODE_FAIL | " + exception.Message);
                Finish(1);
            }
        }

        private static void Replan(
            VehicleDataRuntimeHost host,
            VehiclePoseDriver driver,
            VehicleSelectionKind kind,
            Vector3d firstOffset,
            Vector3d secondOffset,
            VehicleDataRuntimeHost otherA,
            VehicleDataRuntimeHost otherB)
        {
            Require(host.RouteExecutionState == VehicleRouteExecutionState.Running,
                kind + " was not Running before atomic Apply.");
            Require(host.TryGetDriverAcceptedPose(
                    out VehicleRoutePose accepted, out ulong acceptedEpoch, out string poseError),
                poseError);
            Require(host.TryGetActiveEpoch(out ulong sourceEpoch) &&
                    acceptedEpoch == sourceEpoch,
                kind + " accepted pose was not from the active SourceEpoch.");
            RuntimeIdentity otherBeforeA = Capture(otherA);
            RuntimeIdentity otherBeforeB = Capture(otherB);
            ulong version = host.RouteVersion;
            ulong routeEpoch = host.RouteEpoch;
            rootBefore = driver.TargetRoot.position;
            trackBefore = ActualPointCount(trajectory, kind);
            var draft = new List<Vector3d>
            {
                Add(accepted.Position, firstOffset),
                Add(accepted.Position, secondOffset)
            };
            Require(host.TryApplyDraftRoute(draft, out string applyError), applyError);
            Require(host.RouteVersion == version + 1UL &&
                    host.RouteEpoch == routeEpoch + 1UL &&
                    host.TryGetActiveEpoch(out expectedSourceEpoch) &&
                    expectedSourceEpoch == sourceEpoch + 1UL &&
                    host.RouteExecutionState == VehicleRouteExecutionState.Running &&
                    host.RouteProgress01 <= 1e-9,
                kind + " route identities did not switch atomically.");
            expectedRouteVersion = host.RouteVersion;
            Require(Vector3.Distance(driver.TargetRoot.position, rootBefore) <= 1e-4f,
                kind + " Movement Root changed inside Apply instead of through Driver.");
            Require(Near(host.ActiveRouteSnapshot.GetWaypoint(0), accepted.Position),
                kind + " Active Route did not start at the accepted pose.");
            Require(Capture(otherA).Equals(otherBeforeA) &&
                    Capture(otherB).Equals(otherBeforeB),
                kind + " atomic Apply changed another vehicle in the same transaction.");
        }

        private static void Observe(
            VehicleDataRuntimeHost host,
            VehiclePoseDriver driver,
            VehicleSelectionKind kind,
            float continuityLimit)
        {
            Require(host.RouteVersion == expectedRouteVersion &&
                    host.TryGetActiveEpoch(out ulong epoch) &&
                    epoch == expectedSourceEpoch &&
                    driver.LastAppliedSourceEpoch == expectedSourceEpoch,
                kind + " Driver did not consume only the new SourceEpoch.");
            float displacement = Vector3.Distance(driver.TargetRoot.position, rootBefore);
            Require(displacement <= continuityLimit,
                kind + " teleported during the first replanning observation window.");
            Require(ActualPointCount(trajectory, kind) >= trackBefore,
                kind + " Actual Track was cleared by atomic replanning.");
            Require(ObservedRouteVersion(trajectory, kind) == expectedRouteVersion,
                kind + " Active Route renderer did not refresh to the new version.");
        }

        private static void VerifyInvalidFailure()
        {
            RuntimeIdentity before = Capture(auv);
            int pointsBefore = ActualPointCount(trajectory, VehicleSelectionKind.Auv);
            Vector3 positionBefore = auvDriver.TargetRoot.position;
            Require(!auv.TryApplyDraftRoute(
                    new[] { new Vector3d(double.NaN, 0.0, 0.0) },
                    out string error) && !string.IsNullOrWhiteSpace(error),
                "Invalid Running Draft was not rejected.");
            Require(Capture(auv).Equals(before) &&
                    ActualPointCount(trajectory, VehicleSelectionKind.Auv) == pointsBefore &&
                    Vector3.Distance(auvDriver.TargetRoot.position, positionBefore) <= 1e-4f,
                "Rejected Running Draft changed route state, Actual Track, or Movement Root.");
        }

        private static bool TryBind()
        {
            VehicleDataRuntimeHost[] hosts = UnityEngine.Object.FindObjectsByType<VehicleDataRuntimeHost>(
                FindObjectsSortMode.None);
            VehiclePoseDriver[] drivers = UnityEngine.Object.FindObjectsByType<VehiclePoseDriver>(
                FindObjectsSortMode.None);
            if (hosts.Length != 3 || drivers.Length != 3) return false;
            auv = hosts.Single(value => value.IntegrationConfiguration.VehicleType == VehicleType.Auv);
            rov = hosts.Single(value => value.IntegrationConfiguration.VehicleType == VehicleType.Rov);
            usv = hosts.Single(value => value.IntegrationConfiguration.VehicleType == VehicleType.Usv);
            auvDriver = drivers.Single(value => ReferenceEquals(value.RuntimeHost, auv));
            rovDriver = drivers.Single(value => ReferenceEquals(value.RuntimeHost, rov));
            usvDriver = drivers.Single(value => ReferenceEquals(value.RuntimeHost, usv));
            trajectory = UnityEngine.Object.FindFirstObjectByType<VehicleTrajectoryVisualizationController>();
            return trajectory != null;
        }

        private static bool Ready(VehicleDataRuntimeHost host, VehiclePoseDriver driver)
        {
            return host != null && driver != null && host.IsInitialized &&
                host.SourceStatus == DataSourceStatus.Running &&
                host.ActiveRouteSnapshot != null && driver.HasFreshAppliedPose &&
                host.TryGetActiveEpoch(out ulong epoch) &&
                driver.LastAppliedSourceEpoch == epoch;
        }

        private static RuntimeIdentity Capture(VehicleDataRuntimeHost host)
        {
            Require(host.TryGetActiveEpoch(out ulong sourceEpoch),
                "Runtime identity has no active SourceEpoch.");
            return new RuntimeIdentity(
                host.RouteVersion,
                host.RouteEpoch,
                sourceEpoch,
                host.RouteProgress01,
                host.RouteExecutionState.Value,
                host.Store.GetStatistics().AcceptedSamples);
        }

        private static int ActualPointCount(
            VehicleTrajectoryVisualizationController controller,
            VehicleSelectionKind kind)
        {
            object state = TraceState(controller, kind);
            if (state == null) return 0;
            FieldInfo field = state.GetType().GetField(
                "TotalPointCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field == null ? 0 : (int)field.GetValue(state);
        }

        private static ulong ObservedRouteVersion(
            VehicleTrajectoryVisualizationController controller,
            VehicleSelectionKind kind)
        {
            object state = TraceState(controller, kind);
            if (state == null) return 0UL;
            FieldInfo field = state.GetType().GetField(
                "ObservedRouteVersion", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field == null ? 0UL : (ulong)field.GetValue(state);
        }

        private static object TraceState(
            VehicleTrajectoryVisualizationController controller,
            VehicleSelectionKind kind)
        {
            FieldInfo statesField = controller.GetType().GetField(
                "traceStates", BindingFlags.Instance | BindingFlags.NonPublic);
            IDictionary states = statesField == null ? null : statesField.GetValue(controller) as IDictionary;
            return states != null && states.Contains(kind) ? states[kind] : null;
        }

        private static Vector3d Add(Vector3d a, Vector3d b)
        {
            return new Vector3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        private static bool Near(Vector3d a, Vector3d b)
        {
            return Math.Abs(a.X - b.X) <= 1e-5 &&
                Math.Abs(a.Y - b.Y) <= 1e-5 &&
                Math.Abs(a.Z - b.Z) <= 1e-5;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Advance(Phase next, double now)
        {
            phase = next;
            phaseStarted = now;
        }

        private static void Finish(int exitCode)
        {
            bool batch = SessionState.GetBool(BatchKey, false);
            SessionState.SetBool(ActiveKey, false);
            SessionState.SetBool(BatchKey, false);
            if (subscribed)
            {
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                EditorApplication.update -= OnUpdate;
                subscribed = false;
            }
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            if (batch) EditorApplication.Exit(exitCode);
        }

        private readonly struct RuntimeIdentity : IEquatable<RuntimeIdentity>
        {
            public RuntimeIdentity(
                ulong version,
                ulong routeEpoch,
                ulong sourceEpoch,
                double progress,
                VehicleRouteExecutionState state,
                ulong acceptedSamples)
            {
                Version = version;
                RouteEpoch = routeEpoch;
                SourceEpoch = sourceEpoch;
                Progress = progress;
                State = state;
                AcceptedSamples = acceptedSamples;
            }

            private ulong Version { get; }
            private ulong RouteEpoch { get; }
            private ulong SourceEpoch { get; }
            private double Progress { get; }
            private VehicleRouteExecutionState State { get; }
            private ulong AcceptedSamples { get; }

            public bool Equals(RuntimeIdentity other)
            {
                return Version == other.Version && RouteEpoch == other.RouteEpoch &&
                    SourceEpoch == other.SourceEpoch && Math.Abs(Progress - other.Progress) <= 1e-12 &&
                    State == other.State && AcceptedSamples == other.AcceptedSamples;
            }
        }
    }
}
