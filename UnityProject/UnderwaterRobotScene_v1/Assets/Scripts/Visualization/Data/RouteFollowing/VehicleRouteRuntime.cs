using System;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Data.RouteFollowing
{
    public enum VehicleRouteExecutionState
    {
        Running = 0,
        Paused = 1,
        Completed = 2,
        Hold = 3
    }

    public readonly struct VehicleRoutePose
    {
        public VehicleRoutePose(Vector3d position, Quaterniond orientation)
        {
            Position = position;
            Orientation = orientation;
        }

        public Vector3d Position { get; }
        public Quaterniond Orientation { get; }
    }

    public sealed class VehicleRouteRuntime
    {
        private ActiveRouteSnapshot snapshot;
        private double distanceAlongRoute;
        private VehicleRouteExecutionState state;
        private ulong routeEpoch;
        private bool hasHoldPose;
        private VehicleRoutePose holdPose;
        private bool hasContinuityPose;
        private VehicleRoutePose continuityPose;
        private bool hasResumeBridge;
        private VehicleRoutePose resumeBridgeStart;
        private VehicleRoutePose resumeBridgeEnd;
        private double resumeBridgeDistance;
        private double resumeBridgeProgress;
        private Quaterniond rovActivationHeadingSeed;

        public VehicleRouteRuntime(
            ActiveRouteSnapshot activeSnapshot,
            double cruiseSpeedMetersPerSecond)
        {
            snapshot = activeSnapshot ??
                throw new ArgumentNullException(nameof(activeSnapshot));
            if (!Numeric.IsFinite(cruiseSpeedMetersPerSecond) ||
                cruiseSpeedMetersPerSecond <= 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cruiseSpeedMetersPerSecond));
            }

            CruiseSpeedMetersPerSecond = cruiseSpeedMetersPerSecond;
            rovActivationHeadingSeed = ResolveActivationHeadingSeed(snapshot);
            routeEpoch = 1UL;
            state = VehicleRouteExecutionState.Running;
        }

        public ActiveRouteSnapshot ActiveSnapshot => snapshot;
        public double CruiseSpeedMetersPerSecond { get; }
        public double DistanceAlongRoute => distanceAlongRoute;
        public double Progress01 => snapshot.TotalLength <= 0.0
            ? 0.0
            : distanceAlongRoute / snapshot.TotalLength;
        public VehicleRouteExecutionState State => state;
        public ulong RouteVersion => snapshot.RouteVersion;
        public ulong RouteEpoch => routeEpoch;

        public void Advance(double fixedSampleIntervalSeconds)
        {
            if (!Numeric.IsFinite(fixedSampleIntervalSeconds) ||
                fixedSampleIntervalSeconds < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fixedSampleIntervalSeconds));
            }

            if (hasResumeBridge)
            {
                if (state != VehicleRouteExecutionState.Running)
                {
                    return;
                }
                resumeBridgeProgress = Math.Min(
                    resumeBridgeDistance,
                    resumeBridgeProgress +
                    CruiseSpeedMetersPerSecond * fixedSampleIntervalSeconds);
                if (resumeBridgeProgress >= resumeBridgeDistance)
                {
                    hasResumeBridge = false;
                }
                return;
            }

            if (state != VehicleRouteExecutionState.Running)
            {
                return;
            }

            distanceAlongRoute = Math.Min(
                snapshot.TotalLength,
                distanceAlongRoute +
                CruiseSpeedMetersPerSecond * fixedSampleIntervalSeconds);
            if (distanceAlongRoute > 0.0)
            {
                hasContinuityPose = false;
            }
            if (distanceAlongRoute >= snapshot.TotalLength)
            {
                distanceAlongRoute = snapshot.TotalLength;
                state = VehicleRouteExecutionState.Completed;
            }
        }

        public VehicleRoutePose SampleCurrentPose()
        {
            if (hasHoldPose)
            {
                return holdPose;
            }
            if (hasResumeBridge)
            {
                double t = resumeBridgeDistance <= 0.0
                    ? 1.0
                    : resumeBridgeProgress / resumeBridgeDistance;
                return InterpolatePose(
                    in resumeBridgeStart,
                    in resumeBridgeEnd,
                    t);
            }
            if (hasContinuityPose && distanceAlongRoute <= 0.0)
            {
                return continuityPose;
            }

            return SampleRoutePose();
        }

        public bool BeginResumeBridge(in VehicleRoutePose acceptedPose)
        {
            if (!TryNormalizeAcceptedPose(in acceptedPose,
                    out VehicleRoutePose normalizedAcceptedPose))
            {
                return false;
            }

            VehicleRoutePose routePose = SampleRoutePose();
            double x = routePose.Position.X - normalizedAcceptedPose.Position.X;
            double y = routePose.Position.Y - normalizedAcceptedPose.Position.Y;
            double z = routePose.Position.Z - normalizedAcceptedPose.Position.Z;
            double distance = Math.Sqrt(x * x + y * y + z * z);
            resumeBridgeStart = normalizedAcceptedPose;
            resumeBridgeEnd = routePose;
            resumeBridgeDistance = distance;
            resumeBridgeProgress = 0.0;
            hasResumeBridge = distance > 1e-6;
            hasHoldPose = false;
            hasContinuityPose = false;
            return true;
        }

        private VehicleRoutePose SampleRoutePose()
        {

            int segment = FindSegment(distanceAlongRoute);
            double startDistance = snapshot.GetCumulativeLength(segment);
            double endDistance = snapshot.GetCumulativeLength(segment + 1);
            double span = endDistance - startDistance;
            double t = span <= 0.0
                ? 0.0
                : (distanceAlongRoute - startDistance) / span;
            Vector3d start = snapshot.GetWaypoint(segment);
            Vector3d end = snapshot.GetWaypoint(segment + 1);
            var position = new Vector3d(
                start.X + (end.X - start.X) * t,
                start.Y + (end.Y - start.Y) * t,
                start.Z + (end.Z - start.Z) * t);
            return new VehicleRoutePose(
                position,
                ResolveSegmentOrientation(
                    snapshot, segment, rovActivationHeadingSeed));
        }

        public bool Pause()
        {
            if (state != VehicleRouteExecutionState.Running)
                return false;
            state = VehicleRouteExecutionState.Paused;
            return true;
        }

        public bool Resume()
        {
            if (state != VehicleRouteExecutionState.Paused &&
                state != VehicleRouteExecutionState.Hold)
                return false;
            hasHoldPose = false;
            state = distanceAlongRoute >= snapshot.TotalLength
                ? VehicleRouteExecutionState.Completed
                : VehicleRouteExecutionState.Running;
            return true;
        }

        public void Restart()
        {
            distanceAlongRoute = 0.0;
            hasHoldPose = false;
            hasContinuityPose = false;
            hasResumeBridge = false;
            routeEpoch++;
            state = VehicleRouteExecutionState.Running;
        }

        public void Complete()
        {
            distanceAlongRoute = snapshot.TotalLength;
            hasHoldPose = false;
            hasContinuityPose = false;
            hasResumeBridge = false;
            state = VehicleRouteExecutionState.Completed;
        }

        public bool EnterHold(in VehicleRoutePose acceptedPose)
        {
            if (!TryNormalizeAcceptedPose(in acceptedPose,
                    out VehicleRoutePose normalizedAcceptedPose) ||
                state == VehicleRouteExecutionState.Hold)
            {
                return false;
            }

            holdPose = normalizedAcceptedPose;
            hasHoldPose = true;
            hasContinuityPose = false;
            hasResumeBridge = false;
            state = VehicleRouteExecutionState.Hold;
            return true;
        }

        public void Activate(ActiveRouteSnapshot nextSnapshot)
        {
            if (nextSnapshot == null)
                throw new ArgumentNullException(nameof(nextSnapshot));
            if (!string.Equals(nextSnapshot.VehicleId, snapshot.VehicleId,
                    StringComparison.Ordinal) ||
                nextSnapshot.VehicleType != snapshot.VehicleType ||
                nextSnapshot.RouteVersion <= snapshot.RouteVersion)
            {
                throw new ArgumentException(
                    "A replacement route must target the same vehicle and have a newer version.",
                    nameof(nextSnapshot));
            }

            VehicleRoutePose activationPose = SampleCurrentPose();
            snapshot = nextSnapshot;
            rovActivationHeadingSeed = snapshot.OrientationPolicy ==
                VehicleRouteOrientationPolicy.RovLevelYaw
                ? BuildLevelYaw(activationPose.Orientation)
                : Quaterniond.Identity;
            Restart();
        }

        public bool TryActivateWhenNotRunning(
            ActiveRouteSnapshot nextSnapshot,
            out string error)
        {
            if (state == VehicleRouteExecutionState.Running)
            {
                error = "Apply is unavailable while the route is running. Pause or complete it first.";
                return false;
            }

            try
            {
                Activate(nextSnapshot);
                error = string.Empty;
                return true;
            }
            catch (ArgumentException exception)
            {
                error = exception.Message;
                return false;
            }
        }

        internal bool TryValidateRunningReplacement(
            ActiveRouteSnapshot nextSnapshot,
            in VehicleRoutePose acceptedPose,
            out string error)
        {
            if (state != VehicleRouteExecutionState.Running)
            {
                error = "Atomic replanning requires a running route.";
                return false;
            }
            if (!acceptedPose.Position.IsFinite || !acceptedPose.Orientation.IsUsable)
            {
                error = "The Driver accepted pose is unavailable or invalid.";
                return false;
            }
            if (!TryValidateReplacement(nextSnapshot, out error))
            {
                return false;
            }

            Vector3d start = nextSnapshot.GetWaypoint(0);
            double x = start.X - acceptedPose.Position.X;
            double y = start.Y - acceptedPose.Position.Y;
            double z = start.Z - acceptedPose.Position.Z;
            if (x * x + y * y + z * z > 1e-10)
            {
                error = "The replacement route must begin at the Driver accepted position.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        internal void CommitRunningReplacement(
            ActiveRouteSnapshot nextSnapshot,
            in VehicleRoutePose acceptedPose)
        {
            VehicleRoutePose normalizedAcceptedPose = acceptedPose;
            if (!TryNormalizeAcceptedPose(in acceptedPose,
                    out normalizedAcceptedPose))
            {
                throw new InvalidOperationException(
                    "The accepted replacement pose is invalid for its route policy.");
            }
            snapshot = nextSnapshot;
            rovActivationHeadingSeed = snapshot.OrientationPolicy ==
                VehicleRouteOrientationPolicy.RovLevelYaw
                ? normalizedAcceptedPose.Orientation
                : Quaterniond.Identity;
            distanceAlongRoute = 0.0;
            routeEpoch++;
            hasHoldPose = false;
            continuityPose = normalizedAcceptedPose;
            hasContinuityPose = true;
            hasResumeBridge = false;
            state = VehicleRouteExecutionState.Running;
        }

        private static VehicleRoutePose InterpolatePose(
            in VehicleRoutePose start,
            in VehicleRoutePose end,
            double t)
        {
            double clamped = Math.Max(0.0, Math.Min(1.0, t));
            var position = new Vector3d(
                start.Position.X +
                (end.Position.X - start.Position.X) * clamped,
                start.Position.Y +
                (end.Position.Y - start.Position.Y) * clamped,
                start.Position.Z +
                (end.Position.Z - start.Position.Z) * clamped);
            var startRotation = new Quaternion(
                (float)start.Orientation.X,
                (float)start.Orientation.Y,
                (float)start.Orientation.Z,
                (float)start.Orientation.W);
            var endRotation = new Quaternion(
                (float)end.Orientation.X,
                (float)end.Orientation.Y,
                (float)end.Orientation.Z,
                (float)end.Orientation.W);
            Quaternion rotation = Quaternion.Slerp(
                startRotation,
                endRotation,
                (float)clamped);
            return new VehicleRoutePose(
                position,
                new Quaterniond(
                    rotation.x,
                    rotation.y,
                    rotation.z,
                    rotation.w));
        }

        private int FindSegment(double distance)
        {
            int lastSegment = snapshot.WaypointCount - 2;
            if (distance >= snapshot.TotalLength)
                return lastSegment;
            int low = 0;
            int high = lastSegment;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (snapshot.GetCumulativeLength(middle + 1) <= distance)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }

        private bool TryValidateReplacement(
            ActiveRouteSnapshot nextSnapshot,
            out string error)
        {
            if (nextSnapshot == null)
            {
                error = "A replacement route is required.";
                return false;
            }
            if (!string.Equals(nextSnapshot.VehicleId, snapshot.VehicleId,
                    StringComparison.Ordinal) ||
                nextSnapshot.VehicleType != snapshot.VehicleType ||
                nextSnapshot.RouteVersion <= snapshot.RouteVersion)
            {
                error = "A replacement route must target the same vehicle and have a newer version.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        internal bool TryNormalizeAcceptedPose(
            in VehicleRoutePose acceptedPose,
            out VehicleRoutePose normalizedPose)
        {
            normalizedPose = default;
            if (!acceptedPose.Position.IsFinite ||
                !acceptedPose.Orientation.IsUsable)
            {
                return false;
            }

            Quaterniond orientation = acceptedPose.Orientation;
            if (snapshot.OrientationPolicy ==
                VehicleRouteOrientationPolicy.RovLevelYaw)
            {
                try
                {
                    orientation = BuildLevelYaw(orientation);
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
            else if (!orientation.TryNormalize(out orientation))
            {
                return false;
            }

            normalizedPose = new VehicleRoutePose(
                acceptedPose.Position, orientation);
            return true;
        }

        public static Quaterniond ResolveSegmentOrientation(
            ActiveRouteSnapshot route,
            int segment)
        {
            if (route == null)
                throw new ArgumentNullException(nameof(route));
            Quaterniond seed = ResolveActivationHeadingSeed(route);
            return ResolveSegmentOrientation(route, segment, seed);
        }

        private static Quaterniond ResolveSegmentOrientation(
            ActiveRouteSnapshot route,
            int segment,
            Quaterniond activationHeadingSeed)
        {
            if (segment < 0 || segment >= route.WaypointCount - 1)
                throw new ArgumentOutOfRangeException(nameof(segment));
            Vector3d start = route.GetWaypoint(segment);
            Vector3d end = route.GetWaypoint(segment + 1);
            if (route.OrientationPolicy !=
                    VehicleRouteOrientationPolicy.RovLevelYaw ||
                HasHorizontalDirection(start, end))
            {
                return BuildOrientation(
                    start, end, route.OrientationPolicy);
            }

            for (int previous = segment - 1; previous >= 0; previous--)
            {
                Vector3d previousStart = route.GetWaypoint(previous);
                Vector3d previousEnd = route.GetWaypoint(previous + 1);
                if (HasHorizontalDirection(previousStart, previousEnd))
                {
                    return BuildOrientation(
                        previousStart, previousEnd,
                        VehicleRouteOrientationPolicy.RovLevelYaw);
                }
            }
            return activationHeadingSeed;
        }

        private static Quaterniond ResolveActivationHeadingSeed(
            ActiveRouteSnapshot route)
        {
            if (route.OrientationPolicy !=
                VehicleRouteOrientationPolicy.RovLevelYaw)
            {
                return Quaterniond.Identity;
            }
            if (route.HasActivationHeadingSeed)
            {
                return BuildLevelYaw(route.ActivationHeadingSeed);
            }

            Vector3d start = route.GetWaypoint(0);
            Vector3d end = route.GetWaypoint(1);
            if (HasHorizontalDirection(start, end))
            {
                return BuildOrientation(
                    start, end,
                    VehicleRouteOrientationPolicy.RovLevelYaw);
            }
            throw new InvalidOperationException(
                "A vertical-first ROV route requires an activation heading seed.");
        }

        private static bool HasHorizontalDirection(Vector3d start, Vector3d end)
        {
            double x = end.X - start.X;
            double z = end.Z - start.Z;
            double squared = x * x + z * z;
            return Numeric.IsFinite(squared) && squared > 1e-12;
        }

        private static Quaterniond BuildLevelYaw(Quaterniond orientation)
        {
            if (!orientation.TryNormalize(out Quaterniond normalized))
            {
                throw new InvalidOperationException(
                    "The ROV activation heading seed is unusable.");
            }
            var rotation = new Quaternion(
                (float)normalized.X,
                (float)normalized.Y,
                (float)normalized.Z,
                (float)normalized.W);
            Vector3 forward = rotation * Vector3.forward;
            forward.y = 0f;
            if (!IsFinite(forward.sqrMagnitude) ||
                forward.sqrMagnitude <= 1e-12f)
            {
                throw new InvalidOperationException(
                    "The ROV activation heading seed has no horizontal forward direction.");
            }
            forward.Normalize();
            Quaternion level = Quaternion.LookRotation(forward, Vector3.up);
            return new Quaterniond(level.x, level.y, level.z, level.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static Quaterniond BuildOrientation(
            Vector3d start,
            Vector3d end,
            VehicleRouteOrientationPolicy policy)
        {
            if (!start.IsFinite || !end.IsFinite)
            {
                throw new InvalidOperationException(
                    "Cannot build route orientation from non-finite geometry.");
            }
            var tangent = new Vector3(
                (float)(end.X - start.X),
                (float)(end.Y - start.Y),
                (float)(end.Z - start.Z));
            if (policy != VehicleRouteOrientationPolicy.AuvThreeDimensional)
                tangent.y = 0f;
            float squaredMagnitude = tangent.sqrMagnitude;
            if (float.IsNaN(squaredMagnitude) ||
                float.IsInfinity(squaredMagnitude) ||
                squaredMagnitude <= 1e-12f)
            {
                throw new InvalidOperationException(
                    "Cannot build route orientation from a zero or non-finite tangent.");
            }
            tangent.Normalize();
            if (Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) >= 1f - 1e-6f)
            {
                throw new InvalidOperationException(
                    "Cannot build route orientation from a forward/up-collinear tangent.");
            }
            Quaternion value = Quaternion.LookRotation(tangent, Vector3.up);
            var orientation = new Quaterniond(
                value.x, value.y, value.z, value.w);
            if (!orientation.IsUsable)
            {
                throw new InvalidOperationException(
                    "Route orientation construction produced an invalid quaternion.");
            }
            return orientation;
        }
    }
}
