using System;
using System.Collections.Generic;

namespace UnderwaterRobotScene.Visualization.Data.RouteFollowing
{
    public enum VehicleRouteOrientationPolicy
    {
        AuvThreeDimensional = 1,
        RovLevelYaw = 2,
        UsvSurfaceYaw = 3
    }

    public sealed class ActiveRouteSnapshot
    {
        private readonly Vector3d[] waypoints;
        private readonly double[] cumulativeLengths;

        internal ActiveRouteSnapshot(
            string vehicleId,
            VehicleType vehicleType,
            string routeId,
            ulong routeVersion,
            Vector3d[] points,
            double[] lengths,
            VehicleRouteOrientationPolicy orientationPolicy,
            double publishedAtMonotonicSeconds,
            bool hasActivationHeadingSeed,
            Quaterniond activationHeadingSeed)
        {
            VehicleId = vehicleId;
            VehicleType = vehicleType;
            RouteId = routeId;
            RouteVersion = routeVersion;
            waypoints = points;
            cumulativeLengths = lengths;
            OrientationPolicy = orientationPolicy;
            PublishedAtMonotonicSeconds = publishedAtMonotonicSeconds;
            HasActivationHeadingSeed = hasActivationHeadingSeed;
            ActivationHeadingSeed = activationHeadingSeed;
        }

        public string VehicleId { get; }
        public VehicleType VehicleType { get; }
        public string RouteId { get; }
        public ulong RouteVersion { get; }
        public VehicleRouteOrientationPolicy OrientationPolicy { get; }
        public double PublishedAtMonotonicSeconds { get; }
        public bool HasActivationHeadingSeed { get; }
        public Quaterniond ActivationHeadingSeed { get; }
        public int WaypointCount => waypoints.Length;
        public double TotalLength => cumulativeLengths[cumulativeLengths.Length - 1];
        public IReadOnlyList<Vector3d> Waypoints => waypoints;
        public IReadOnlyList<double> CumulativeLengths => cumulativeLengths;

        public Vector3d GetWaypoint(int index) => waypoints[index];
        public double GetCumulativeLength(int index) => cumulativeLengths[index];
    }

    public static class ActiveRouteSnapshotBuilder
    {
        private const double DuplicatePointEpsilonSquared = 1e-12;

        public static bool TryBuild(
            string vehicleId,
            VehicleType vehicleType,
            string routeId,
            ulong routeVersion,
            IEnumerable<Vector3d> worldWaypoints,
            VehicleRouteOrientationPolicy orientationPolicy,
            double publishedAtMonotonicSeconds,
            out ActiveRouteSnapshot snapshot,
            out string error)
        {
            return TryBuildCore(
                vehicleId, vehicleType, routeId, routeVersion,
                worldWaypoints, orientationPolicy,
                publishedAtMonotonicSeconds, false, default,
                out snapshot, out error);
        }

        public static bool TryBuild(
            string vehicleId,
            VehicleType vehicleType,
            string routeId,
            ulong routeVersion,
            IEnumerable<Vector3d> worldWaypoints,
            VehicleRouteOrientationPolicy orientationPolicy,
            double publishedAtMonotonicSeconds,
            Quaterniond activationHeadingSeed,
            out ActiveRouteSnapshot snapshot,
            out string error)
        {
            return TryBuildCore(
                vehicleId, vehicleType, routeId, routeVersion,
                worldWaypoints, orientationPolicy,
                publishedAtMonotonicSeconds, true, activationHeadingSeed,
                out snapshot, out error);
        }

        private static bool TryBuildCore(
            string vehicleId,
            VehicleType vehicleType,
            string routeId,
            ulong routeVersion,
            IEnumerable<Vector3d> worldWaypoints,
            VehicleRouteOrientationPolicy orientationPolicy,
            double publishedAtMonotonicSeconds,
            bool hasActivationHeadingSeed,
            Quaterniond activationHeadingSeed,
            out ActiveRouteSnapshot snapshot,
            out string error)
        {
            snapshot = null;
            if (string.IsNullOrWhiteSpace(vehicleId) ||
                string.IsNullOrWhiteSpace(routeId))
            {
                error = "Vehicle ID and route ID must be explicit.";
                return false;
            }

            if (hasActivationHeadingSeed && !activationHeadingSeed.IsUsable)
            {
                error = "The route activation heading seed must be a usable quaternion.";
                return false;
            }

            if (vehicleType == VehicleType.Unknown || routeVersion == 0UL ||
                !Numeric.IsFinite(publishedAtMonotonicSeconds) ||
                publishedAtMonotonicSeconds < 0.0 ||
                !PolicyMatches(vehicleType, orientationPolicy))
            {
                error = "Route identity, publication time, or vehicle policy is invalid.";
                return false;
            }

            if (worldWaypoints == null)
            {
                error = "Route waypoints are required.";
                return false;
            }

            var compressed = new List<Vector3d>();
            foreach (Vector3d point in worldWaypoints)
            {
                if (!point.IsFinite)
                {
                    error = "Every route waypoint must be finite.";
                    return false;
                }

                if (compressed.Count == 0 ||
                    DistanceSquared(compressed[compressed.Count - 1], point) >
                    DuplicatePointEpsilonSquared)
                {
                    compressed.Add(point);
                }
            }

            if (compressed.Count < 2)
            {
                error = "A route must contain at least two distinct waypoints.";
                return false;
            }

            var cumulative = new double[compressed.Count];
            for (int index = 1; index < compressed.Count; index++)
            {
                Vector3d previous = compressed[index - 1];
                Vector3d current = compressed[index];
                double horizontalSquared =
                    (current.X - previous.X) * (current.X - previous.X) +
                    (current.Z - previous.Z) * (current.Z - previous.Z);
                if (orientationPolicy ==
                        VehicleRouteOrientationPolicy.UsvSurfaceYaw &&
                    horizontalSquared <= DuplicatePointEpsilonSquared)
                {
                    error = "USV route segments require a horizontal direction.";
                    return false;
                }

                double length = Math.Sqrt(DistanceSquared(previous, current));
                cumulative[index] = cumulative[index - 1] + length;
            }

            if (!Numeric.IsFinite(cumulative[cumulative.Length - 1]) ||
                cumulative[cumulative.Length - 1] <= 0.0)
            {
                error = "Route total length must be finite and non-zero.";
                return false;
            }

            snapshot = new ActiveRouteSnapshot(
                vehicleId,
                vehicleType,
                routeId,
                routeVersion,
                compressed.ToArray(),
                cumulative,
                orientationPolicy,
                publishedAtMonotonicSeconds,
                hasActivationHeadingSeed,
                activationHeadingSeed);
            error = string.Empty;
            return true;
        }

        private static bool PolicyMatches(
            VehicleType vehicleType,
            VehicleRouteOrientationPolicy policy)
        {
            return (vehicleType == VehicleType.Auv &&
                    policy == VehicleRouteOrientationPolicy.AuvThreeDimensional) ||
                (vehicleType == VehicleType.Rov &&
                 policy == VehicleRouteOrientationPolicy.RovLevelYaw) ||
                (vehicleType == VehicleType.Usv &&
                 policy == VehicleRouteOrientationPolicy.UsvSurfaceYaw);
        }

        private static double DistanceSquared(Vector3d a, Vector3d b)
        {
            double x = b.X - a.X;
            double y = b.Y - a.Y;
            double z = b.Z - a.Z;
            return x * x + y * y + z * z;
        }
    }
}
