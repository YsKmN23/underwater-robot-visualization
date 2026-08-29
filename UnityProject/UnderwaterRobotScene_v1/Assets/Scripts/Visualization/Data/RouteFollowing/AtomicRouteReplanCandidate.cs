using System;
using System.Collections.Generic;

namespace UnderwaterRobotScene.Visualization.Data.RouteFollowing
{
    public static class AtomicRouteReplanCandidate
    {
        private const double DuplicateEpsilonSquared = 1e-10;

        public static bool TryBuild(
            ActiveRouteSnapshot active,
            IReadOnlyList<Vector3d> draftWaypoints,
            in VehicleRoutePose acceptedPose,
            double publishedAtMonotonicSeconds,
            out ActiveRouteSnapshot candidate,
            out string error)
        {
            candidate = null;
            if (active == null)
            {
                error = "An Active Route is required.";
                return false;
            }
            if (!acceptedPose.Position.IsFinite || !acceptedPose.Orientation.IsUsable)
            {
                error = "The Driver accepted pose is unavailable or invalid.";
                return false;
            }
            if (draftWaypoints == null)
            {
                error = "Draft waypoints are required.";
                return false;
            }

            var points = new List<Vector3d>(draftWaypoints.Count + 1);
            Vector3d start = ProjectForVehicle(active, acceptedPose.Position,
                acceptedPose.Position.Y);
            points.Add(start);
            for (int index = 0; index < draftWaypoints.Count; index++)
            {
                Vector3d point = ProjectForVehicle(
                    active, draftWaypoints[index], start.Y);
                if (!point.IsFinite)
                {
                    error = "Every Draft waypoint must be finite.";
                    return false;
                }
                if (!IsDistinctForPolicy(
                        points[points.Count - 1], point, active.OrientationPolicy))
                {
                    continue;
                }
                points.Add(point);
            }

            ulong nextVersion;
            try
            {
                nextVersion = checked(active.RouteVersion + 1UL);
            }
            catch (OverflowException)
            {
                error = "Route version is exhausted.";
                return false;
            }

            return ActiveRouteSnapshotBuilder.TryBuild(
                active.VehicleId,
                active.VehicleType,
                active.RouteId + "-REPLAN-" + nextVersion,
                nextVersion,
                points,
                active.OrientationPolicy,
                publishedAtMonotonicSeconds,
                acceptedPose.Orientation,
                out candidate,
                out error);
        }

        private static Vector3d ProjectForVehicle(
            ActiveRouteSnapshot active,
            Vector3d point,
            double acceptedHeight)
        {
            return active.OrientationPolicy ==
                   VehicleRouteOrientationPolicy.UsvSurfaceYaw
                ? new Vector3d(point.X, acceptedHeight, point.Z)
                : point;
        }

        private static bool IsDistinctForPolicy(
            Vector3d previous,
            Vector3d current,
            VehicleRouteOrientationPolicy policy)
        {
            double x = current.X - previous.X;
            double y = current.Y - previous.Y;
            double z = current.Z - previous.Z;
            return policy == VehicleRouteOrientationPolicy.UsvSurfaceYaw
                ? x * x + z * z > DuplicateEpsilonSquared
                : x * x + y * y + z * z > DuplicateEpsilonSquared;
        }
    }
}
