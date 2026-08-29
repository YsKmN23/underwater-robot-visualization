using System;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    public static class EnvE3CInteractiveRouteEditingVerifier
    {
        public static void RunBatch()
        {
            VerifyDraftDeepCopyLifecycleAndIsolation();
            VerifyAuvHeightResolutionLifecycle();
            VerifyPointerGestureRegression();
            VerifyTransactionalActivation();
            VerifyInputArbitration();
            VerifyProjectionPolicies();
            Debug.Log("ENV_E3C_INTERACTIVE_ROUTE_EDITING_VERIFICATION_PASS");
        }

        private static void VerifyAuvHeightResolutionLifecycle()
        {
            ActiveRouteSnapshot reentryActive = Build(
                "A-height-reentry", VehicleType.Auv, 10,
                VehicleRouteOrientationPolicy.AuvThreeDimensional,
                new Vector3d(0, -1.35, 0),
                new Vector3d(1, -2.00, 2));
            var reentryDraft = new DraftRouteSession();
            reentryDraft.Begin(reentryActive);
            reentryDraft.ExitPreservingDraft();
            Require(RouteEditHeightResolver.TryResolve(
                    VehicleSelectionKind.Auv, reentryDraft, reentryActive, -9f,
                    out RouteEditHeightResolution reentry) &&
                    reentry.Source == RouteEditHeightSource.PreviousWaypoint &&
                    Near(reentry.WorldY, -2.00),
                "Preserved AUV Draft re-entry did not use its final waypoint Y.");

            ActiveRouteSnapshot active = Build(
                "A-height", VehicleType.Auv, 11,
                VehicleRouteOrientationPolicy.AuvThreeDimensional,
                new Vector3d(0, -1.35, 0),
                new Vector3d(1, -2.00, 2),
                new Vector3d(2, -1.50, 4));
            var draft = new DraftRouteSession();
            draft.Begin(active);
            draft.ExitPreservingDraft();

            draft.Select(1);
            Require(RouteEditHeightResolver.TryResolve(
                    VehicleSelectionKind.Auv, draft, active, -9f,
                    out RouteEditHeightResolution selected) &&
                    selected.Source == RouteEditHeightSource.SelectedWaypoint &&
                    selected.SourceWaypointIndex == 1 && Near(selected.WorldY, -2.00),
                "Selected non-final AUV waypoint did not override the height source.");
            int appendIndex = draft.Waypoints.Count;
            string selectedFeedback = RouteEditHeightResolver.FormatAuvFeedback(
                in selected, draft.Waypoints.Count);
            Require(selectedFeedback.Contains("Selected waypoint 2") &&
                    selectedFeedback.Contains("appends to route end"),
                "Selected non-final AUV UI feedback did not explain append-to-end semantics.");
            Require(draft.Add(new Vector3d(10, selected.WorldY, 10)) &&
                    draft.SelectedWaypointIndex == appendIndex &&
                    Near(draft.Waypoints[appendIndex].Y, -2.00),
                "AUV append did not add at the route end or select the new point.");
            Require(RouteEditHeightResolver.TryResolve(
                    VehicleSelectionKind.Auv, draft, active, -9f,
                    out RouteEditHeightResolution consecutive) &&
                    consecutive.Source == RouteEditHeightSource.SelectedWaypoint &&
                    Near(consecutive.WorldY, -2.00),
                "Consecutive AUV append did not inherit the newly selected point.");
            int secondAppendIndex = draft.Waypoints.Count;
            Require(draft.Add(new Vector3d(11, consecutive.WorldY, 11)) &&
                    draft.SelectedWaypointIndex == secondAppendIndex &&
                    Near(draft.Waypoints[secondAppendIndex].Y, -2.00),
                "Second consecutive AUV append changed height unexpectedly.");

            draft.Select(1);
            draft.MoveSelected(
                VehicleRouteEditingController.AdjustAuvWaypointDepth(
                    draft.Waypoints[1], 0.25));
            draft.MoveSelected(
                VehicleRouteEditingController.AdjustAuvWaypointDepth(
                    draft.Waypoints[1], 0.25));
            Require(RouteEditHeightResolver.TryResolve(
                    VehicleSelectionKind.Auv, draft, active, -9f,
                    out RouteEditHeightResolution pageUp) &&
                    Near(pageUp.WorldY, -1.50),
                "PageUp height was not inherited by the next AUV append.");
            Require(draft.Add(new Vector3d(12, pageUp.WorldY, 12)) &&
                    Near(draft.Waypoints[draft.Waypoints.Count - 1].Y, -1.50),
                "PageUp append used the wrong AUV height.");

            draft.Select(1);
            draft.MoveSelected(
                VehicleRouteEditingController.AdjustAuvWaypointDepth(
                    draft.Waypoints[1], -0.25));
            draft.MoveSelected(
                VehicleRouteEditingController.AdjustAuvWaypointDepth(
                    draft.Waypoints[1], -0.25));
            Require(RouteEditHeightResolver.TryResolve(
                    VehicleSelectionKind.Auv, draft, active, -9f,
                    out RouteEditHeightResolution pageDown) &&
                    Near(pageDown.WorldY, -2.00),
                "PageDown height was not inherited by the next AUV append.");
            Require(draft.Add(new Vector3d(13, pageDown.WorldY, 13)) &&
                    Near(draft.Waypoints[draft.Waypoints.Count - 1].Y, -2.00),
                "PageDown append used the wrong AUV height.");

            var deleteDraft = new DraftRouteSession();
            deleteDraft.Begin(active);
            deleteDraft.Select(2);
            Require(deleteDraft.DeleteSelected() &&
                    deleteDraft.SelectedWaypointIndex == 1,
                "Deleting the final AUV waypoint did not select the surviving final point.");
            Require(RouteEditHeightResolver.TryResolve(
                    VehicleSelectionKind.Auv, deleteDraft, active, -9f,
                    out RouteEditHeightResolution deleteFinal) &&
                    deleteFinal.Source == RouteEditHeightSource.SelectedWaypoint &&
                    Near(deleteFinal.WorldY, -2.00),
                "AUV append after deleting the final point used the wrong height.");

            var deleteOnlyDraft = new DraftRouteSession();
            deleteOnlyDraft.Begin(active);
            deleteOnlyDraft.Clear();
            Require(deleteOnlyDraft.Add(new Vector3d(5, -4, 5)) &&
                    deleteOnlyDraft.DeleteSelected() &&
                    deleteOnlyDraft.Waypoints.Count == 0,
                "Deleting the only AUV Draft point did not leave an empty Draft.");
            Require(RouteEditHeightResolver.TryResolve(
                    VehicleSelectionKind.Auv, deleteOnlyDraft, active, -9f,
                    out RouteEditHeightResolution deleteOnly) &&
                    deleteOnly.Source == RouteEditHeightSource.ActiveRoute &&
                    Near(deleteOnly.WorldY, -1.35),
                "Empty AUV Draft did not fall back to ActiveRoute[0].Y.");

            var clearDraft = new DraftRouteSession();
            clearDraft.Begin(active);
            clearDraft.Clear();
            Require(RouteEditHeightResolver.TryResolve(
                    VehicleSelectionKind.Auv, clearDraft, active, -9f,
                    out RouteEditHeightResolution clear) &&
                    clear.Source == RouteEditHeightSource.ActiveRoute &&
                    Near(clear.WorldY, -1.35),
                "Cleared AUV Draft did not use ActiveRoute[0].Y.");

            var cancelDraft = new DraftRouteSession();
            cancelDraft.Begin(active);
            cancelDraft.Cancel(active);
            cancelDraft.Begin(active);
            Require(RouteEditHeightResolver.TryResolve(
                    VehicleSelectionKind.Auv, cancelDraft, active, -9f,
                    out RouteEditHeightResolution cancelReentry) &&
                    cancelReentry.Source == RouteEditHeightSource.PreviousWaypoint &&
                    Near(cancelReentry.WorldY, -1.50),
                "Cancel/re-entry did not continue from the copied Active Route final Y.");

            var appliedDraft = new DraftRouteSession();
            appliedDraft.Begin(active);
            appliedDraft.AcceptAppliedSnapshot(active);
            Require(RouteEditHeightResolver.TryResolve(
                    VehicleSelectionKind.Auv, appliedDraft, active, -9f,
                    out RouteEditHeightResolution applied) &&
                    applied.Source == RouteEditHeightSource.PreviousWaypoint &&
                    Near(applied.WorldY, -1.50),
                "Post-Apply AUV editing did not retain the Active Route final height.");

            ActiveRouteSnapshot rov = Build(
                "R-height", VehicleType.Rov, 12,
                VehicleRouteOrientationPolicy.RovLevelYaw,
                new Vector3d(0, -3.00, 0),
                new Vector3d(1, -4.00, 2));
            var rovDraft = new DraftRouteSession();
            rovDraft.Begin(rov);
            rovDraft.ExitPreservingDraft();
            Require(RouteEditHeightResolver.TryResolve(
                    VehicleSelectionKind.Rov, rovDraft, rov, -9f,
                    out RouteEditHeightResolution rovResolution) &&
                    rovResolution.Source == RouteEditHeightSource.PreviousWaypoint &&
                    Near(rovResolution.WorldY, -4.00),
                "ROV preserved Draft did not inherit its final waypoint height.");

            ActiveRouteSnapshot usv = Build(
                "U-height", VehicleType.Usv, 13,
                VehicleRouteOrientationPolicy.UsvSurfaceYaw,
                new Vector3d(0, 0.18, 0),
                new Vector3d(1, 0.42, 2));
            var usvDraft = new DraftRouteSession();
            usvDraft.Begin(usv);
            usvDraft.ExitPreservingDraft();
            Require(RouteEditHeightResolver.TryResolve(
                    VehicleSelectionKind.Usv, usvDraft, usv, 0.18f,
                    out RouteEditHeightResolution usvResolution) &&
                    usvResolution.Source == RouteEditHeightSource.ActiveRoute &&
                    Near(usvResolution.WorldY, 0.18),
                "USV height resolution changed from its surface route plane.");

            RouteEditorPanelLayout layout = RouteEditorPanelLayout.Calculate(
                1280, 720, true, 32f, 32f);
            Require(layout.HeightSourceRect.height > 0f &&
                    layout.HeightSourceRect.y < layout.HelpRect.y,
                "Vertical height-source feedback did not receive a dedicated compact panel row.");
        }

        private static void VerifyPointerGestureRegression()
        {
            var gesture = new RoutePointerGesture();
            Vector3 original = new Vector3(3f, -2f, 4f);
            gesture.Begin(
                new Vector2(100f, 100f),
                original,
                true,
                new Vector3(3f, 0f, 4f));
            Require(!gesture.TryGetDragTarget(
                    new Vector2(100f, 100f), true,
                    new Vector3(3f, 0f, 4f), true, out _),
                "AUV 0px click moved a waypoint.");
            for (int pixels = 1; pixels <= 5; pixels++)
            {
                Require(!gesture.TryGetDragTarget(
                        new Vector2(100f + pixels, 100f), true,
                        new Vector3(3f + pixels, 0f, 4f), true, out _),
                    "AUV pointer jitter below 6px started a drag.");
            }
            Require(gesture.TryGetDragTarget(
                    new Vector2(106f, 100f), true,
                    new Vector3(9f, 0f, 10f), true,
                    out Vector3 moved) &&
                    Near(moved.x, 9f) && Near(moved.z, 10f) &&
                    Near(moved.y, original.y),
                "AUV pointer drag did not start at 6px or preserve Y.");
        }

        private static void VerifyDraftDeepCopyLifecycleAndIsolation()
        {
            ActiveRouteSnapshot auv = Build(
                "A", VehicleType.Auv, 4,
                VehicleRouteOrientationPolicy.AuvThreeDimensional,
                new Vector3d(0, -2, 0), new Vector3d(1, -3, 2));
            ActiveRouteSnapshot rov = Build(
                "R", VehicleType.Rov, 7,
                VehicleRouteOrientationPolicy.RovLevelYaw,
                new Vector3d(0, -3, 0), new Vector3d(2, -3, 2));
            var auvDraft = new DraftRouteSession();
            var rovDraft = new DraftRouteSession();
            auvDraft.Begin(auv);
            rovDraft.Begin(rov);
            Require(auvDraft.Waypoints.Count == auv.WaypointCount &&
                    !ReferenceEquals(auvDraft.Waypoints, auv.Waypoints),
                "Active Route was not deep-copied into Draft.");
            auvDraft.Select(0);
            auvDraft.MoveSelected(new Vector3d(9, -5, 9));
            Require(auv.GetWaypoint(0).Equals(new Vector3d(0, -2, 0)),
                "Draft mutation changed the immutable Active Route.");
            Require(rovDraft.Waypoints[0].Equals(rov.GetWaypoint(0)),
                "AUV editing leaked into the ROV Draft.");

            rovDraft.Clear();
            Require(rovDraft.Waypoints.Count == 0 &&
                    auvDraft.Waypoints.Count == 2 &&
                    rov.WaypointCount == 2,
                "Clear did not remain isolated to the current Draft.");
            Require(!rovDraft.TryValidate(rov, 1.0, out _),
                "An empty Draft passed route validation.");
            rovDraft.Cancel(rov);
            Require(!rovDraft.HasDraft && !rovDraft.IsEditing &&
                    rov.RouteVersion == 7,
                "Cancel changed Active Route state or retained an editing session.");
            rovDraft.Begin(rov);
            Require(rovDraft.Waypoints.Count == 2,
                "A new session did not recopy the current Active Route.");
        }

        private static void VerifyTransactionalActivation()
        {
            ActiveRouteSnapshot active = Build(
                "A", VehicleType.Auv, 1,
                VehicleRouteOrientationPolicy.AuvThreeDimensional,
                new Vector3d(0, 0, 0), new Vector3d(0, 0, 2));
            ActiveRouteSnapshot replacement = Build(
                "A", VehicleType.Auv, 2,
                VehicleRouteOrientationPolicy.AuvThreeDimensional,
                new Vector3d(2, -1, 2), new Vector3d(4, -2, 4));
            var runtime = new VehicleRouteRuntime(active, 1.0);
            runtime.Advance(0.5);
            double progress = runtime.DistanceAlongRoute;
            ulong routeEpoch = runtime.RouteEpoch;
            Require(!runtime.TryActivateWhenNotRunning(replacement, out string runningError) &&
                    !string.IsNullOrEmpty(runningError) &&
                    runtime.ActiveSnapshot == active &&
                    runtime.RouteVersion == 1 &&
                    runtime.RouteEpoch == routeEpoch &&
                    Near(runtime.DistanceAlongRoute, progress),
                "Running Apply was not rejected without side effects.");

            Require(runtime.Pause(), "Verifier could not pause the route.");
            var source = new RouteFollowingSource(
                "editing-source", 0.1, runtime,
                WorldFrame.UnityWorld, BodyFrame.UnityBody);
            source.Start(new AcceptingSink());
            ulong sourceEpoch = source.SourceEpoch;
            Require(source.TryActivateWhenNotRunning(replacement, out string error), error);
            Require(runtime.ActiveSnapshot == replacement &&
                    runtime.RouteVersion == 2 &&
                    runtime.RouteEpoch == routeEpoch + 1 &&
                    runtime.State == VehicleRouteExecutionState.Running &&
                    Near(runtime.DistanceAlongRoute, 0.0) &&
                    source.SourceEpoch == sourceEpoch + 1,
                "Paused Apply did not publish and restart with coherent epochs.");
            source.Dispose();
        }

        private static void VerifyInputArbitration()
        {
            object owner = new object();
            object stranger = new object();
            RouteEditingInputContext.SetRouteEditorActive(owner, true);
            Require(RouteEditingInputContext.IsRouteEditorActive &&
                    !RouteEditingInputContext.SelectionMayConsumePrimaryPointer(),
                "Edit Mode did not gate Selection primary input.");
            RouteEditingInputContext.SetRouteEditorActive(stranger, false);
            Require(RouteEditingInputContext.IsRouteEditorActive,
                "A non-owner cleared the input arbitration gate.");
            RouteEditingInputContext.SetRouteEditorActive(owner, false);
            Require(!RouteEditingInputContext.IsRouteEditorActive &&
                    RouteEditingInputContext.SelectionMayConsumePrimaryPointer(),
                "Normal Mode did not restore Selection input.");
        }

        private static void VerifyProjectionPolicies()
        {
            var ray = new Ray(
                new Vector3(-1f, 7f, 2f),
                new Vector3(0.2f, -1f, 0.35f).normalized);
            VerifyRouteEditorProjection(
                VehicleSelectionKind.Auv, ray, -2f, "AUV");
            VerifyRouteEditorProjection(
                VehicleSelectionKind.Rov, ray, 2f, "ROV");
            VerifyRouteEditorProjection(
                VehicleSelectionKind.Usv, ray, 0.18f, "USV");
            Require(!VehicleRouteProjection.TryProjectRouteEditorPointer(
                    VehicleSelectionKind.None, ray, 0f, out _),
                "An unsupported vehicle kind received a route-editor projection.");
            Require(!VehicleRouteProjection.TryProjectRouteEditorPointer(
                    VehicleSelectionKind.Rov,
                    new Ray(Vector3.zero, Vector3.right), 2f, out _),
                "A route-plane-parallel ROV pointer ray was accepted.");
        }

        private static void VerifyRouteEditorProjection(
            VehicleSelectionKind kind,
            Ray ray,
            float routeY,
            string label)
        {
            float distance = (routeY - ray.origin.y) / ray.direction.y;
            Vector3 expected = ray.GetPoint(distance);
            Require(VehicleRouteProjection.TryProjectRouteEditorPointer(
                    kind, ray, routeY, out Vector3 projected) &&
                    (projected - expected).sqrMagnitude <= 0.00000001f &&
                    Near(projected.y, routeY),
                label + " route-editor projection left the original pointer ray.");
        }

        private static ActiveRouteSnapshot Build(
            string id,
            VehicleType type,
            ulong version,
            VehicleRouteOrientationPolicy policy,
            params Vector3d[] points)
        {
            Require(ActiveRouteSnapshotBuilder.TryBuild(
                    id, type, "route-" + id, version, points, policy, 0.0,
                    out ActiveRouteSnapshot snapshot, out string error), error);
            return snapshot;
        }

        private static bool Near(double a, double b)
        {
            return Math.Abs(a - b) <= 1e-5;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class AcceptingSink : IStateSink
        {
            public PublishResult Publish(in ReceivedVehicleState sample)
            {
                return PublishResult.Accepted;
            }
        }
    }
}
