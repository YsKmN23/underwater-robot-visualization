using System;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime.Constraints;
using UnderwaterRobotScene.Visualization.Sampling;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime.Monitoring
{
    public enum MonitoringDataHealth
    {
        Unavailable = 0,
        Disabled = 1,
        NoData = 2,
        Invalid = 3,
        Stale = 4,
        Fresh = 5
    }

    public readonly struct VehicleMonitorSnapshot
    {
        public VehicleMonitorSnapshot(
            VehicleType vehicleType,
            string vehicleId,
            MonitoringDataHealth health,
            VehicleRuntimeSourceMode sourceMode,
            DataSourceStatus sourceStatus,
            bool hasDataAge,
            double dataAgeSeconds,
            bool hasAppliedPose,
            Vector3 appliedPosition,
            Vector3 appliedEulerDegrees,
            bool hasLinearSpeed,
            double linearSpeedMetersPerSecond,
            bool hasSourceTimestamp,
            double sourceTimestampSeconds,
            RenderSampleMode sampleMode,
            ulong sequenceNumber,
            ulong sourceEpoch,
            string sourceId,
            WorldFrame worldFrame,
            BodyFrame bodyFrame,
            bool hasRoute,
            VehicleRouteExecutionState routeState,
            string routeId,
            ulong routeVersion,
            ulong routeEpoch,
            int waypointCount,
            double distanceAlongRoute,
            double totalRouteLength,
            double routeProgress01,
            double cruiseSpeedMetersPerSecond,
            bool hasSafetyConstraint,
            UnityPoseConstraintDecision safetyDecision,
            string safetyReason,
            RouteSafetyFailureDiagnostic routeRejection,
            string latestOutcome)
        {
            VehicleType = vehicleType;
            VehicleId = vehicleId ?? string.Empty;
            Health = health;
            SourceMode = sourceMode;
            SourceStatus = sourceStatus;
            HasDataAge = hasDataAge;
            DataAgeSeconds = dataAgeSeconds;
            HasAppliedPose = hasAppliedPose;
            AppliedPosition = appliedPosition;
            AppliedEulerDegrees = appliedEulerDegrees;
            HasLinearSpeed = hasLinearSpeed;
            LinearSpeedMetersPerSecond = linearSpeedMetersPerSecond;
            HasSourceTimestamp = hasSourceTimestamp;
            SourceTimestampSeconds = sourceTimestampSeconds;
            SampleMode = sampleMode;
            SequenceNumber = sequenceNumber;
            SourceEpoch = sourceEpoch;
            SourceId = sourceId ?? string.Empty;
            WorldFrame = worldFrame;
            BodyFrame = bodyFrame;
            HasRoute = hasRoute;
            RouteState = routeState;
            RouteId = routeId ?? string.Empty;
            RouteVersion = routeVersion;
            RouteEpoch = routeEpoch;
            WaypointCount = waypointCount;
            DistanceAlongRoute = distanceAlongRoute;
            TotalRouteLength = totalRouteLength;
            RouteProgress01 = routeProgress01;
            CruiseSpeedMetersPerSecond = cruiseSpeedMetersPerSecond;
            HasSafetyConstraint = hasSafetyConstraint;
            SafetyDecision = safetyDecision;
            SafetyReason = safetyReason ?? string.Empty;
            RouteRejection = routeRejection;
            LatestOutcome = latestOutcome ?? string.Empty;
        }

        public VehicleType VehicleType { get; }
        public string VehicleId { get; }
        public MonitoringDataHealth Health { get; }
        public VehicleRuntimeSourceMode SourceMode { get; }
        public DataSourceStatus SourceStatus { get; }
        public bool HasDataAge { get; }
        public double DataAgeSeconds { get; }
        public bool HasAppliedPose { get; }
        public Vector3 AppliedPosition { get; }
        public Vector3 AppliedEulerDegrees { get; }
        public bool HasLinearSpeed { get; }
        public double LinearSpeedMetersPerSecond { get; }
        public bool HasSourceTimestamp { get; }
        public double SourceTimestampSeconds { get; }
        public RenderSampleMode SampleMode { get; }
        public ulong SequenceNumber { get; }
        public ulong SourceEpoch { get; }
        public string SourceId { get; }
        public WorldFrame WorldFrame { get; }
        public BodyFrame BodyFrame { get; }
        public bool HasRoute { get; }
        public VehicleRouteExecutionState RouteState { get; }
        public string RouteId { get; }
        public ulong RouteVersion { get; }
        public ulong RouteEpoch { get; }
        public int WaypointCount { get; }
        public double DistanceAlongRoute { get; }
        public double TotalRouteLength { get; }
        public double RouteProgress01 { get; }
        public double CruiseSpeedMetersPerSecond { get; }
        public bool HasSafetyConstraint { get; }
        public UnityPoseConstraintDecision SafetyDecision { get; }
        public string SafetyReason { get; }
        public RouteSafetyFailureDiagnostic RouteRejection { get; }
        public string LatestOutcome { get; }
    }

    public readonly struct MonitoringFleetSnapshot
    {
        public MonitoringFleetSnapshot(
            VehicleMonitorSnapshot auv,
            VehicleMonitorSnapshot rov,
            VehicleMonitorSnapshot usv,
            VehicleSelectionKind selectedVehicle)
        {
            Auv = auv;
            Rov = rov;
            Usv = usv;
            SelectedVehicle = selectedVehicle;
        }

        public VehicleMonitorSnapshot Auv { get; }
        public VehicleMonitorSnapshot Rov { get; }
        public VehicleMonitorSnapshot Usv { get; }
        public VehicleSelectionKind SelectedVehicle { get; }

        public bool TryGetSelected(out VehicleMonitorSnapshot snapshot)
        {
            switch (SelectedVehicle)
            {
                case VehicleSelectionKind.Auv:
                    snapshot = Auv;
                    return true;
                case VehicleSelectionKind.Rov:
                    snapshot = Rov;
                    return true;
                case VehicleSelectionKind.Usv:
                    snapshot = Usv;
                    return true;
                default:
                    snapshot = default;
                    return false;
            }
        }
    }

    public static class VehicleMonitoringSnapshotBuilder
    {
        private static readonly VehicleStateFields RequiredPoseFields =
            VehicleStateFields.Position | VehicleStateFields.Orientation;

        public static VehicleMonitorSnapshot Capture(
            VehicleType expectedType,
            VehicleDataRuntimeHost host,
            VehiclePoseDriver driver,
            VehiclePoseControlAuthority authority,
            string latestOutcome,
            double? monotonicNowSeconds = null)
        {
            string vehicleId = host == null ? string.Empty : host.VehicleId;
            if (host == null || driver == null || authority == null ||
                host.IntegrationConfiguration == null ||
                host.IntegrationConfiguration.VehicleType != expectedType)
            {
                return Empty(expectedType, vehicleId,
                    MonitoringDataHealth.Unavailable, latestOutcome);
            }

            MonitoringDataHealth health = MonitoringDataHealth.NoData;
            DataSourceStatus sourceStatus = host.SourceStatus;
            bool hasAge = false;
            double age = 0.0;
            ReceivedVehicleState latest = default;
            bool hasLatest = false;
            ulong sourceEpoch = 0UL;

            if (sourceStatus == DataSourceStatus.Faulted)
            {
                health = MonitoringDataHealth.Invalid;
            }
            else if (!host.IsInitialized)
            {
                health = MonitoringDataHealth.NoData;
            }
            else if (sourceStatus == DataSourceStatus.Stopped ||
                     sourceStatus == DataSourceStatus.Stopping ||
                     sourceStatus == DataSourceStatus.Disposed ||
                     !driver.isActiveAndEnabled)
            {
                health = MonitoringDataHealth.Disabled;
            }
            else if (host.Store != null &&
                     host.TryGetActiveEpoch(out sourceEpoch))
            {
                try
                {
                    if (host.Store.TryReadSnapshot(
                            host.SourceId,
                            sourceEpoch,
                            host.VehicleId,
                            monotonicNowSeconds ?? host.MonotonicNowSeconds,
                            out VehicleSnapshot storeSnapshot))
                    {
                        latest = storeSnapshot.Latest;
                        hasLatest = true;
                        hasAge = true;
                        age = storeSnapshot.AgeSeconds;
                        bool requiredFields =
                            (latest.State.ValidFields & RequiredPoseFields) ==
                            RequiredPoseFields;
                        health = ClassifySnapshotHealth(
                            latest.IsStructurallyValid &&
                            latest.State.VehicleType == expectedType &&
                            requiredFields,
                            storeSnapshot.IsTimedOut);
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    health = MonitoringDataHealth.Invalid;
                }
            }

            bool hasAppliedPose = driver.TryGetLastAppliedPose(
                out Vector3 appliedPosition,
                out Quaternion appliedRotation,
                out ulong appliedEpoch) &&
                appliedEpoch == sourceEpoch;
            Vector3 euler = hasAppliedPose
                ? NormalizeEuler(appliedRotation.eulerAngles)
                : default;

            bool hasLinearSpeed = hasLatest && HasValidLinearVelocity(
                latest.State.ValidFields, latest.State.LinearVelocity);
            double speed = hasLinearSpeed
                ? Magnitude(latest.State.LinearVelocity)
                : 0.0;

            ActiveRouteSnapshot route = host.ActiveRouteSnapshot;
            bool hasRoute = route != null && host.RouteExecutionState.HasValue;
            VehicleRouteExecutionState routeState = hasRoute
                ? host.RouteExecutionState.Value
                : default;
            bool hasSafety = expectedType != VehicleType.Usv &&
                driver.HasPoseConstraint;
            string outcome = BuildLatestOutcome(
                latestOutcome, hasRoute, routeState, hasSafety,
                driver.LastPoseConstraintDecision,
                driver.LastPoseConstraintReason);

            return new VehicleMonitorSnapshot(
                expectedType,
                vehicleId,
                health,
                host.SourceMode,
                sourceStatus,
                hasAge,
                age,
                hasAppliedPose,
                appliedPosition,
                euler,
                hasLinearSpeed,
                speed,
                hasLatest,
                hasLatest ? latest.State.SourceTimestampSeconds : 0.0,
                driver.LastSampleMode,
                hasLatest ? latest.State.SequenceNumber : 0UL,
                sourceEpoch,
                host.SourceId,
                hasLatest ? latest.State.WorldFrame : WorldFrame.Unknown,
                hasLatest ? latest.State.BodyFrame : BodyFrame.Unknown,
                hasRoute,
                routeState,
                hasRoute ? route.RouteId : string.Empty,
                hasRoute ? route.RouteVersion : 0UL,
                hasRoute ? host.RouteEpoch : 0UL,
                hasRoute ? route.WaypointCount : 0,
                hasRoute ? host.RouteDistanceAlongRoute : 0.0,
                hasRoute ? route.TotalLength : 0.0,
                hasRoute ? host.RouteProgress01 : 0.0,
                hasRoute ? host.RouteCruiseSpeedMetersPerSecond : 0.0,
                hasSafety,
                hasSafety ? driver.LastPoseConstraintDecision :
                    UnityPoseConstraintDecision.NotEvaluated,
                hasSafety ? driver.LastPoseConstraintReason : string.Empty,
                expectedType == VehicleType.Auv
                    ? host.LastRouteSafetyFailure
                    : RouteSafetyFailureDiagnostic.None,
                outcome);
        }

        public static bool HasValidLinearVelocity(
            VehicleStateFields validFields,
            Vector3d linearVelocity)
        {
            return (validFields & VehicleStateFields.LinearVelocity) != 0 &&
                   linearVelocity.IsFinite;
        }

        public static MonitoringDataHealth ClassifySnapshotHealth(
            bool structurallyValidWithRequiredPose,
            bool timedOut)
        {
            if (!structurallyValidWithRequiredPose)
                return MonitoringDataHealth.Invalid;
            return timedOut
                ? MonitoringDataHealth.Stale
                : MonitoringDataHealth.Fresh;
        }

        private static VehicleMonitorSnapshot Empty(
            VehicleType type,
            string vehicleId,
            MonitoringDataHealth health,
            string latestOutcome)
        {
            return new VehicleMonitorSnapshot(
                type, vehicleId, health,
                VehicleRuntimeSourceMode.LocalDiagnostic,
                DataSourceStatus.Stopped,
                false, 0.0, false, default, default,
                false, 0.0, false, 0.0, RenderSampleMode.None,
                0UL, 0UL, string.Empty,
                WorldFrame.Unknown, BodyFrame.Unknown,
                false, default, string.Empty, 0UL, 0UL, 0,
                0.0, 0.0, 0.0, 0.0,
                false, UnityPoseConstraintDecision.NotEvaluated,
                string.Empty, RouteSafetyFailureDiagnostic.None,
                latestOutcome);
        }

        private static string BuildLatestOutcome(
            string supplied,
            bool hasRoute,
            VehicleRouteExecutionState routeState,
            bool hasSafety,
            UnityPoseConstraintDecision decision,
            string reason)
        {
            if (!string.IsNullOrWhiteSpace(supplied) &&
                !string.Equals(supplied, "No Apply recorded.",
                    StringComparison.Ordinal))
            {
                return supplied;
            }

            if (hasRoute && routeState == VehicleRouteExecutionState.Hold)
            {
                return string.IsNullOrWhiteSpace(reason)
                    ? "Route Hold"
                    : "Route Hold — " + reason;
            }

            if (hasSafety && decision == UnityPoseConstraintDecision.HoldCurrent)
            {
                return string.IsNullOrWhiteSpace(reason)
                    ? "Constraint Hold"
                    : "Constraint Hold — " + reason;
            }

            return hasRoute ? "Route " + routeState : "Route unavailable";
        }

        private static Vector3 NormalizeEuler(Vector3 euler)
        {
            return new Vector3(
                Mathf.DeltaAngle(0f, euler.x),
                Mathf.DeltaAngle(0f, euler.y),
                Mathf.DeltaAngle(0f, euler.z));
        }

        private static double Magnitude(Vector3d value)
        {
            return Math.Sqrt(value.X * value.X +
                             value.Y * value.Y +
                             value.Z * value.Z);
        }
    }
}
