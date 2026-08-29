using System;
using System.Collections.Generic;

namespace UnderwaterRobotScene.Visualization.Data.RouteFollowing
{
    public sealed class DraftRouteSession
    {
        private readonly List<Vector3d> waypoints = new List<Vector3d>();

        public bool IsEditing { get; private set; }
        public bool HasDraft { get; private set; }
        public int SelectedWaypointIndex { get; private set; } = -1;
        public ulong BaseRouteVersion { get; private set; }
        public string LastError { get; private set; } = string.Empty;
        public IReadOnlyList<Vector3d> Waypoints => waypoints;

        public void Begin(ActiveRouteSnapshot active)
        {
            if (active == null)
                throw new ArgumentNullException(nameof(active));
            if (!HasDraft)
                CopyFrom(active);
            IsEditing = true;
            LastError = string.Empty;
        }

        public void ExitPreservingDraft()
        {
            IsEditing = false;
            SelectedWaypointIndex = -1;
        }

        public void Cancel(ActiveRouteSnapshot active)
        {
            if (active == null)
                throw new ArgumentNullException(nameof(active));
            waypoints.Clear();
            SelectedWaypointIndex = -1;
            BaseRouteVersion = active.RouteVersion;
            HasDraft = false;
            IsEditing = false;
            LastError = string.Empty;
        }

        public void Clear()
        {
            waypoints.Clear();
            SelectedWaypointIndex = -1;
            HasDraft = true;
            LastError = "Draft needs at least two distinct waypoints.";
        }

        public bool Add(Vector3d point)
        {
            if (!point.IsFinite)
            {
                LastError = "A draft waypoint must be finite.";
                return false;
            }
            waypoints.Add(point);
            SelectedWaypointIndex = waypoints.Count - 1;
            HasDraft = true;
            LastError = string.Empty;
            return true;
        }

        public bool Select(int index)
        {
            if (index < 0 || index >= waypoints.Count)
                return false;
            SelectedWaypointIndex = index;
            return true;
        }

        public bool MoveSelected(Vector3d point)
        {
            if (SelectedWaypointIndex < 0 ||
                SelectedWaypointIndex >= waypoints.Count ||
                !point.IsFinite)
            {
                LastError = "Select a waypoint and provide a finite position.";
                return false;
            }
            waypoints[SelectedWaypointIndex] = point;
            HasDraft = true;
            LastError = string.Empty;
            return true;
        }

        public bool DeleteSelected()
        {
            if (SelectedWaypointIndex < 0 ||
                SelectedWaypointIndex >= waypoints.Count)
                return false;
            waypoints.RemoveAt(SelectedWaypointIndex);
            SelectedWaypointIndex = waypoints.Count == 0
                ? -1
                : Math.Min(SelectedWaypointIndex, waypoints.Count - 1);
            HasDraft = true;
            LastError = waypoints.Count < 2
                ? "Draft needs at least two distinct waypoints."
                : string.Empty;
            return true;
        }

        public bool TryValidate(
            ActiveRouteSnapshot active,
            double monotonicNowSeconds,
            out string error)
        {
            if (active == null)
            {
                error = "An Active Route is required.";
                LastError = error;
                return false;
            }
            if (BaseRouteVersion != active.RouteVersion)
            {
                error = "Draft is based on an older Active Route; cancel and reopen it.";
                LastError = error;
                return false;
            }
            bool valid = ActiveRouteSnapshotBuilder.TryBuild(
                active.VehicleId,
                active.VehicleType,
                active.RouteId + "-DRAFT",
                active.RouteVersion + 1UL,
                waypoints,
                active.OrientationPolicy,
                monotonicNowSeconds,
                out _,
                out error);
            LastError = error;
            return valid;
        }

        public void AcceptAppliedSnapshot(ActiveRouteSnapshot active)
        {
            if (active == null)
                throw new ArgumentNullException(nameof(active));
            CopyFrom(active);
            IsEditing = true;
            LastError = string.Empty;
        }

        private void CopyFrom(ActiveRouteSnapshot active)
        {
            waypoints.Clear();
            for (int index = 0; index < active.WaypointCount; index++)
                waypoints.Add(active.GetWaypoint(index));
            BaseRouteVersion = active.RouteVersion;
            SelectedWaypointIndex = -1;
            HasDraft = true;
        }
    }
}
