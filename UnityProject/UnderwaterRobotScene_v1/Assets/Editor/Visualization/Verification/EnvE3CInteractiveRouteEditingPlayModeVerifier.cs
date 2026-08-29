using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    [InitializeOnLoad]
    public static class EnvE3CInteractiveRouteEditingPlayModeVerifier
    {
        private const string ActiveKey = "E3C.RouteEditing.Active";
        private const string BatchKey = "E3C.RouteEditing.Batch";
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";

        private enum Phase
        {
            WaitHealthy,
            RejectedDraftPreservation,
            InvalidReject,
            PausedApply,
            ObserveAppliedRoute
        }

        private static bool subscribed;
        private static Phase phase;
        private static double phaseStarted;
        private static VehicleDataRuntimeHost auv;
        private static VehicleDataRuntimeHost rov;
        private static VehicleDataRuntimeHost usv;
        private static VehicleRouteEditingController editor;
        private static VehicleSelectionCameraController selection;
        private static VehicleTrajectoryVisualizationController trajectory;
        private static Transform auvRoot;
        private static Vector3 auvPositionAfterApply;
        private static int actualPointsBeforeApply;

        static EnvE3CInteractiveRouteEditingPlayModeVerifier()
        {
            if (SessionState.GetBool(ActiveKey, false)) Subscribe();
        }

        public static void RunBatch()
        {
            if (SessionState.GetBool(ActiveKey, false))
                throw new InvalidOperationException("E3C route-editing verification is already active.");
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
                if (now - phaseStarted > 15.0)
                    throw new InvalidOperationException("Timed out in Batch 2 phase " + phase + ".");
                switch (phase)
                {
                    case Phase.WaitHealthy:
                        if (!TryBind() || !Healthy(auv) || !Healthy(rov) || !Healthy(usv)) return;
                        Require(auvRoot != null && editor != null && selection != null && trajectory != null,
                            "Batch 2 runtime controllers did not bootstrap.");
                        rov.RestartRoute();
                        Advance(Phase.RejectedDraftPreservation, now);
                        break;

                    case Phase.RejectedDraftPreservation:
                        VerifyRejectedApplyPreservesDraft();
                        Advance(Phase.InvalidReject, now);
                        break;

                    case Phase.InvalidReject:
                        if (now - phaseStarted < 0.2) return;
                        ulong version = rov.RouteVersion;
                        ulong routeEpoch = rov.RouteEpoch;
                        rov.TryGetActiveEpoch(out ulong sourceEpoch);
                        double progress = rov.RouteProgress01;
                        var invalidDraft = new List<Vector3d>
                        {
                            new Vector3d(double.NaN, 0.0, 0.0)
                        };
                        Require(!rov.TryApplyDraftRoute(invalidDraft, out string runningError) &&
                                !string.IsNullOrEmpty(runningError) &&
                                rov.RouteVersion == version && rov.RouteEpoch == routeEpoch &&
                                Near(rov.RouteProgress01, progress) &&
                                rov.TryGetActiveEpoch(out ulong sourceAfterReject) &&
                                sourceAfterReject == sourceEpoch,
                            "Invalid Running Apply changed route/version/epoch/progress.");
                        auv.RestartRoute();
                        Require(auv.PauseRoute(), "Could not establish Paused Apply precondition.");
                        Advance(Phase.PausedApply, now);
                        break;

                    case Phase.PausedApply:
                        ulong auvVersion = auv.RouteVersion;
                        ulong auvRouteEpoch = auv.RouteEpoch;
                        auv.TryGetActiveEpoch(out ulong auvSourceEpoch);
                        ulong rovVersion = rov.RouteVersion;
                        ulong usvVersion = usv.RouteVersion;
                        actualPointsBeforeApply = ActualPointCount(trajectory, VehicleSelectionKind.Auv);
                        List<Vector3d> auvDraft = Copy(auv.ActiveRouteSnapshot);
                        Vector3d auvLast = auvDraft[auvDraft.Count - 1];
                        auvDraft[auvDraft.Count - 1] =
                            new Vector3d(auvLast.X + 0.75, auvLast.Y - 0.25, auvLast.Z + 0.5);
                        Require(auv.TryApplyDraftRoute(auvDraft, out string applyError), applyError);
                        Require(auv.RouteVersion == auvVersion + 1 &&
                                auv.RouteEpoch == auvRouteEpoch + 1 &&
                                auv.RouteExecutionState == VehicleRouteExecutionState.Running &&
                                auv.TryGetActiveEpoch(out ulong auvSourceAfter) &&
                                auvSourceAfter == auvSourceEpoch + 1 &&
                                rov.RouteVersion == rovVersion && usv.RouteVersion == usvVersion,
                            "Paused Apply did not update only the selected vehicle coherently.");

                        selection.SelectVehicle(VehicleSelectionKind.Auv);
                        Require(editor.EnterEditMode(VehicleSelectionKind.Auv) &&
                                RouteEditingInputContext.IsRouteEditorActive,
                            "AUV Route Edit Mode did not acquire primary input.");
                        selection.SelectVehicle(VehicleSelectionKind.Rov);
                        Require(!RouteEditingInputContext.IsRouteEditorActive,
                            "Switching to an independent non-editing ROV session retained AUV input ownership.");
                        selection.SelectVehicle(VehicleSelectionKind.Auv);
                        Require(RouteEditingInputContext.IsRouteEditorActive,
                            "Returning to AUV did not preserve its independent edit session.");
                        selection.SelectVehicle(VehicleSelectionKind.Rov);
                        auvPositionAfterApply = auvRoot.position;
                        Advance(Phase.ObserveAppliedRoute, now);
                        break;

                    case Phase.ObserveAppliedRoute:
                        if (now - phaseStarted < 1.0) return;
                        Require(Vector3.Distance(auvRoot.position, auvPositionAfterApply) > 0.02f,
                            "Applied AUV route did not execute through the live pose chain.");
                        Require(ActualPointCount(trajectory, VehicleSelectionKind.Auv) >= actualPointsBeforeApply,
                            "Apply cleared the existing AUV Actual Track.");
                        Debug.Log("ENV_E3C_INTERACTIVE_ROUTE_EDITING_PLAY_MODE_PASS");
                        Finish(0);
                        break;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("ENV_E3C_INTERACTIVE_ROUTE_EDITING_PLAY_MODE_FAIL | " + exception.Message);
                Finish(1);
            }
        }

        private static bool TryBind()
        {
            VehicleDataRuntimeHost[] hosts = UnityEngine.Object.FindObjectsByType<VehicleDataRuntimeHost>(
                FindObjectsSortMode.None);
            if (hosts.Length != 3) return false;
            auv = hosts.Single(value => value.IntegrationConfiguration.VehicleType == VehicleType.Auv);
            rov = hosts.Single(value => value.IntegrationConfiguration.VehicleType == VehicleType.Rov);
            usv = hosts.Single(value => value.IntegrationConfiguration.VehicleType == VehicleType.Usv);
            editor = VehicleRouteEditingController.InputOwner;
            selection = UnityEngine.Object.FindFirstObjectByType<VehicleSelectionCameraController>();
            trajectory = UnityEngine.Object.FindFirstObjectByType<VehicleTrajectoryVisualizationController>();
            VehiclePoseDriver driver = UnityEngine.Object.FindObjectsByType<VehiclePoseDriver>(
                FindObjectsSortMode.None).Single(value => ReferenceEquals(value.RuntimeHost, auv));
            auvRoot = driver.TargetRoot;
            Require(UnityEngine.Object.FindObjectsByType<VehicleRouteEditingController>(
                    FindObjectsSortMode.None).Length == 1,
                "Exactly one Route Editing Controller must arbitrate input.");
            return editor != null && selection != null && trajectory != null;
        }

        private static bool Healthy(VehicleDataRuntimeHost host)
        {
            return host != null && host.IsInitialized &&
                host.SourceStatus == DataSourceStatus.Running && host.ActiveRouteSnapshot != null;
        }

        private static void VerifyRejectedApplyPreservesDraft()
        {
            selection.SelectVehicle(VehicleSelectionKind.Auv);
            Require(editor.EnterEditMode(VehicleSelectionKind.Auv) &&
                    RouteEditingInputContext.IsRouteEditorActive &&
                    ReferenceEquals(VehicleRouteEditingController.InputOwner,
                        editor),
                "AUV Route Edit Mode did not establish the real controller session.");

            object stateBefore = GetCurrentEditState();
            DraftRouteSession draftBefore = GetEditStateField<
                DraftRouteSession>(stateBefore, "Draft");
            Require(draftBefore != null && draftBefore.IsEditing &&
                    draftBefore.HasDraft &&
                    GetEditStateField<VehicleSelectionKind>(
                        stateBefore, "Kind") == VehicleSelectionKind.Auv &&
                    ReferenceEquals(GetEditStateField<VehicleDataRuntimeHost>(
                        stateBefore, "Host"), auv),
                "The real AUV Draft session was not observable after entering edit mode.");

            Vector3d[] unsafeWaypoints =
            {
                new Vector3d(-1.85, -1.35, -1.65),
                new Vector3d(-1.85, -1.35, 1.35),
                new Vector3d(0.15, -2.35, 4.35),
                new Vector3d(1.15, -3.35, 7.35),
                new Vector3d(-1.85, -2.35, 10.35)
            };
            Require(draftBefore.Waypoints.Count == unsafeWaypoints.Length,
                "The current AUV Active Route no longer matches the audited " +
                "five-waypoint unsafe-route fixture.");
            for (int index = 0; index < unsafeWaypoints.Length; index++)
            {
                Require(draftBefore.Select(index) &&
                        draftBefore.MoveSelected(unsafeWaypoints[index]),
                    "Could not construct the audited unsafe AUV Draft at index " +
                    index + ".");
            }
            Require(draftBefore.TryValidate(
                    auv.ActiveRouteSnapshot,
                    auv.MonotonicNowSeconds,
                    out string draftError),
                "The audited unsafe AUV Draft was structurally invalid before " +
                "the real safety Apply path: " + draftError);

            var waypointsBefore = new List<Vector3d>(draftBefore.Waypoints);
            int selectedBefore = draftBefore.SelectedWaypointIndex;
            ulong draftBaseVersionBefore = draftBefore.BaseRouteVersion;
            ulong activeVersionBefore = auv.RouteVersion;
            ulong routeEpochBefore = auv.RouteEpoch;
            Require(auv.TryGetActiveEpoch(out ulong sourceEpochBefore),
                "Rejected Apply precondition has no SourceEpoch.");
            VehicleRouteExecutionState? executionStateBefore =
                auv.RouteExecutionState;
            double progressBefore = auv.RouteProgress01;

            MethodInfo applyCurrent = typeof(VehicleRouteEditingController)
                .GetMethod("ApplyCurrent",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Require(applyCurrent != null,
                "The real VehicleRouteEditingController Apply path was not found.");
            applyCurrent.Invoke(editor, new[] { stateBefore });

            object stateAfter = GetCurrentEditState();
            DraftRouteSession draftAfter = GetEditStateField<
                DraftRouteSession>(stateAfter, "Draft");
            Require(editor.LastApplyOutcome.StartsWith(
                        "Apply rejected;", StringComparison.Ordinal) &&
                    editor.LastApplyOutcome.Contains(
                        "Active v" + activeVersionBefore + " unchanged.") &&
                    (editor.LastApplyOutcome.Contains("Connection segment") ||
                     editor.LastApplyOutcome.Contains("Draft segment")),
                "The real controller Apply path did not report rejection.");
            Require(auv.RouteVersion == activeVersionBefore &&
                    auv.RouteEpoch == routeEpochBefore &&
                    auv.TryGetActiveEpoch(out ulong sourceEpochAfter) &&
                    sourceEpochAfter == sourceEpochBefore &&
                    auv.RouteExecutionState == executionStateBefore &&
                    Near(auv.RouteProgress01, progressBefore),
                "Rejected controller Apply changed Active version, epochs, or runtime state.");
            Require(ReferenceEquals(stateAfter, stateBefore) &&
                    ReferenceEquals(draftAfter, draftBefore) &&
                    draftAfter.IsEditing && draftAfter.HasDraft &&
                    editor.SelectedVehicle == VehicleSelectionKind.Auv &&
                    ReferenceEquals(VehicleRouteEditingController.InputOwner,
                        editor) &&
                    RouteEditingInputContext.IsRouteEditorActive &&
                    GetEditStateField<VehicleSelectionKind>(
                        stateAfter, "Kind") == VehicleSelectionKind.Auv &&
                    ReferenceEquals(GetEditStateField<VehicleDataRuntimeHost>(
                        stateAfter, "Host"), auv),
                "Rejected Apply replaced the Draft session or changed its vehicle/edit owner.");
            Require(draftAfter.SelectedWaypointIndex == selectedBefore &&
                    draftAfter.BaseRouteVersion == draftBaseVersionBefore &&
                    draftAfter.Waypoints.Count == waypointsBefore.Count,
                "Rejected Apply changed Draft selection, base version, or waypoint count.");
            for (int index = 0; index < waypointsBefore.Count; index++)
                Require(SamePoint(draftAfter.Waypoints[index],
                        waypointsBefore[index]),
                    "Rejected Apply changed Draft waypoint sequence at index " +
                    index + ".");
        }

        private static object GetCurrentEditState()
        {
            PropertyInfo property = typeof(VehicleRouteEditingController)
                .GetProperty("CurrentState",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            object state = property == null ? null : property.GetValue(editor);
            Require(state != null,
                "The real controller CurrentState was not available.");
            return state;
        }

        private static T GetEditStateField<T>(object state, string name)
        {
            FieldInfo field = state.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.Public |
                      BindingFlags.NonPublic);
            Require(field != null,
                "The real controller edit-state field was not available: " +
                name + ".");
            return (T)field.GetValue(state);
        }

        private static bool SamePoint(Vector3d a, Vector3d b)
        {
            return a.X.Equals(b.X) &&
                a.Y.Equals(b.Y) &&
                a.Z.Equals(b.Z);
        }

        private static List<Vector3d> Copy(ActiveRouteSnapshot snapshot)
        {
            var result = new List<Vector3d>();
            for (int index = 0; index < snapshot.WaypointCount; index++)
                result.Add(snapshot.GetWaypoint(index));
            return result;
        }

        private static int ActualPointCount(
            VehicleTrajectoryVisualizationController controller,
            VehicleSelectionKind kind)
        {
            FieldInfo statesField = controller.GetType().GetField(
                "traceStates", BindingFlags.Instance | BindingFlags.NonPublic);
            IDictionary states = statesField == null ? null : statesField.GetValue(controller) as IDictionary;
            if (states == null || !states.Contains(kind)) return 0;
            object state = states[kind];
            FieldInfo countField = state.GetType().GetField(
                "TotalPointCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return countField == null ? 0 : (int)countField.GetValue(state);
        }

        private static bool Near(double a, double b) { return Math.Abs(a - b) <= 1e-8; }
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
    }
}
