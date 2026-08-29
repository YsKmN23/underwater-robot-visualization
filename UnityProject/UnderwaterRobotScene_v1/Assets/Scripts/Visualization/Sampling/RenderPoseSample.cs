using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Transforms;

namespace UnderwaterRobotScene.Visualization.Sampling
{
    public enum RenderSampleMode
    {
        None = 0,
        Exact = 1,
        Interpolated = 2,
        HeldLatest = 3
    }

    public enum RenderSampleFailureReason
    {
        None = 0,
        InvalidRequest = 1,
        InvalidPolicy = 2,
        NoData = 3,
        EpochUnavailable = 4,
        BeforeHistory = 5,
        SingleSampleHoldDisabled = 6,
        AfterLatestRejected = 7,
        HoldWindowExceeded = 8,
        Stale = 9,
        SourceFaulted = 10,
        SourceUnavailable = 11,
        GapTooLarge = 12,
        InvalidHistory = 13,
        ConversionFailed = 14,
        InterpolationFailed = 15,
        LocalClockRegression = 16
    }

    public readonly struct RenderPoseSample
    {
        private RenderPoseSample(
            bool succeeded,
            RenderSampleMode mode,
            RenderSampleFailureReason failureReason,
            string message,
            ConversionError conversionError,
            string profileId,
            string sourceId,
            ulong sourceEpoch,
            string vehicleId,
            double targetSourceTimeSeconds,
            double beforeSourceTimeSeconds,
            double afterSourceTimeSeconds,
            ulong beforeSequenceNumber,
            ulong afterSequenceNumber,
            double interpolationAlpha,
            bool hasSourceHealth,
            double localDataAgeSeconds,
            SourceHealth sourceHealth,
            Vector3d position,
            Quaterniond orientation)
        {
            Succeeded = succeeded;
            Mode = mode;
            FailureReason = failureReason;
            Message = message ?? string.Empty;
            ConversionError = conversionError;
            ProfileId = profileId ?? string.Empty;
            SourceId = sourceId ?? string.Empty;
            SourceEpoch = sourceEpoch;
            VehicleId = vehicleId ?? string.Empty;
            TargetSourceTimeSeconds = targetSourceTimeSeconds;
            BeforeSourceTimeSeconds = beforeSourceTimeSeconds;
            AfterSourceTimeSeconds = afterSourceTimeSeconds;
            BeforeSequenceNumber = beforeSequenceNumber;
            AfterSequenceNumber = afterSequenceNumber;
            InterpolationAlpha = interpolationAlpha;
            HasSourceHealth = hasSourceHealth;
            LocalDataAgeSeconds = localDataAgeSeconds;
            SourceHealth = sourceHealth;
            Position = position;
            Orientation = orientation;
        }

        public bool Succeeded { get; }
        public RenderSampleMode Mode { get; }
        public RenderSampleFailureReason FailureReason { get; }
        public string Message { get; }
        public ConversionError ConversionError { get; }
        public string ProfileId { get; }
        public string SourceId { get; }
        public ulong SourceEpoch { get; }
        public string VehicleId { get; }
        public double TargetSourceTimeSeconds { get; }
        public double BeforeSourceTimeSeconds { get; }
        public double AfterSourceTimeSeconds { get; }
        public ulong BeforeSequenceNumber { get; }
        public ulong AfterSequenceNumber { get; }
        public double InterpolationAlpha { get; }
        public bool HasSourceHealth { get; }
        public double LocalDataAgeSeconds { get; }
        public SourceHealth SourceHealth { get; }
        public Vector3d Position { get; }
        public Quaterniond Orientation { get; }

        internal static RenderPoseSample Success(
            RenderSampleMode mode,
            in RenderSampleRequest request,
            in VehicleSnapshot snapshot,
            in ReceivedVehicleState before,
            in ReceivedVehicleState after,
            double interpolationAlpha,
            Vector3d position,
            Quaterniond orientation)
        {
            return new RenderPoseSample(
                true,
                mode,
                RenderSampleFailureReason.None,
                string.Empty,
                ConversionError.None,
                request.TransformProfile.ProfileId,
                request.SourceId,
                request.SourceEpoch,
                request.VehicleId,
                request.TargetSourceTimeSeconds,
                before.State.SourceTimestampSeconds,
                after.State.SourceTimestampSeconds,
                before.State.SequenceNumber,
                after.State.SequenceNumber,
                interpolationAlpha,
                true,
                snapshot.AgeSeconds,
                snapshot.Health,
                position,
                orientation);
        }

        internal static RenderPoseSample Failure(
            RenderSampleFailureReason reason,
            string message,
            in RenderSampleRequest request,
            ConversionError conversionError = default)
        {
            return new RenderPoseSample(
                false,
                RenderSampleMode.None,
                reason,
                message,
                conversionError,
                request.TransformProfile.ProfileId,
                request.SourceId,
                request.SourceEpoch,
                request.VehicleId,
                request.TargetSourceTimeSeconds,
                0.0,
                0.0,
                0UL,
                0UL,
                0.0,
                false,
                0.0,
                default,
                default,
                default);
        }

        internal static RenderPoseSample FailureWithSnapshot(
            RenderSampleFailureReason reason,
            string message,
            in RenderSampleRequest request,
            in VehicleSnapshot snapshot)
        {
            return new RenderPoseSample(
                false,
                RenderSampleMode.None,
                reason,
                message,
                ConversionError.None,
                request.TransformProfile.ProfileId,
                request.SourceId,
                request.SourceEpoch,
                request.VehicleId,
                request.TargetSourceTimeSeconds,
                0.0,
                0.0,
                0UL,
                0UL,
                0.0,
                true,
                snapshot.AgeSeconds,
                snapshot.Health,
                default,
                default);
        }
    }
}
