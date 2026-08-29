using System;

namespace UnderwaterRobotScene.Visualization.Data
{
    public enum PublishResult
    {
        Accepted = 0,
        InvalidSample = 1,
        DuplicateSequence = 2,
        ConflictingDuplicate = 3,
        OutOfOrderSequence = 4,
        NonIncreasingTimestamp = 5,
        LocalClockRegression = 6,
        RetiredEpoch = 7,
        StoreDisposed = 8
    }

    public readonly struct VehicleStateStorePolicy
    {
        public VehicleStateStorePolicy(
            int capacityPerVehicle,
            bool rejectDuplicateSequence = true,
            bool rejectOutOfOrderSequence = true,
            bool requireIncreasingTimestamp = true,
            bool requireMonotonicReceiveTime = true,
            double timeoutSeconds = 0.5,
            double timestampDiscontinuityThresholdSeconds = double.PositiveInfinity)
        {
            if (capacityPerVehicle < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(capacityPerVehicle), "Capacity must be at least two.");
            }

            if (!Numeric.IsFinite(timeoutSeconds) || timeoutSeconds <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds),
                    "Timeout must be finite and greater than zero.");
            }

            if ((!Numeric.IsFinite(timestampDiscontinuityThresholdSeconds) &&
                 !double.IsPositiveInfinity(timestampDiscontinuityThresholdSeconds)) ||
                timestampDiscontinuityThresholdSeconds <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(timestampDiscontinuityThresholdSeconds),
                    "Discontinuity threshold must be positive or positive infinity.");
            }

            CapacityPerVehicle = capacityPerVehicle;
            RejectDuplicateSequence = rejectDuplicateSequence;
            RejectOutOfOrderSequence = rejectOutOfOrderSequence;
            RequireIncreasingTimestamp = requireIncreasingTimestamp;
            RequireMonotonicReceiveTime = requireMonotonicReceiveTime;
            TimeoutSeconds = timeoutSeconds;
            TimestampDiscontinuityThresholdSeconds = timestampDiscontinuityThresholdSeconds;
        }

        public int CapacityPerVehicle { get; }
        public bool RejectDuplicateSequence { get; }
        public bool RejectOutOfOrderSequence { get; }
        public bool RequireIncreasingTimestamp { get; }
        public bool RequireMonotonicReceiveTime { get; }
        public double TimeoutSeconds { get; }
        public double TimestampDiscontinuityThresholdSeconds { get; }
    }

    public readonly struct VehicleStateChannelStatistics
    {
        public VehicleStateChannelStatistics(
            ulong acceptedSamples,
            ulong invalidSamples,
            ulong duplicateSamples,
            ulong conflictingDuplicateSamples,
            ulong outOfOrderSamples,
            ulong nonIncreasingTimestampSamples,
            ulong localClockRegressionSamples,
            ulong missingSequenceCount,
            ulong discontinuityResets)
        {
            AcceptedSamples = acceptedSamples;
            InvalidSamples = invalidSamples;
            DuplicateSamples = duplicateSamples;
            ConflictingDuplicateSamples = conflictingDuplicateSamples;
            OutOfOrderSamples = outOfOrderSamples;
            NonIncreasingTimestampSamples = nonIncreasingTimestampSamples;
            LocalClockRegressionSamples = localClockRegressionSamples;
            MissingSequenceCount = missingSequenceCount;
            DiscontinuityResets = discontinuityResets;
        }

        public ulong AcceptedSamples { get; }
        public ulong InvalidSamples { get; }
        public ulong DuplicateSamples { get; }
        public ulong ConflictingDuplicateSamples { get; }
        public ulong OutOfOrderSamples { get; }
        public ulong NonIncreasingTimestampSamples { get; }
        public ulong LocalClockRegressionSamples { get; }
        public ulong MissingSequenceCount { get; }
        public ulong DiscontinuityResets { get; }
    }

    public readonly struct VehicleStateStoreStatistics
    {
        public VehicleStateStoreStatistics(
            ulong acceptedSamples,
            ulong invalidSamples,
            ulong retiredEpochSamples,
            ulong epochTransitions)
        {
            AcceptedSamples = acceptedSamples;
            InvalidSamples = invalidSamples;
            RetiredEpochSamples = retiredEpochSamples;
            EpochTransitions = epochTransitions;
        }

        public ulong AcceptedSamples { get; }
        public ulong InvalidSamples { get; }
        public ulong RetiredEpochSamples { get; }
        public ulong EpochTransitions { get; }
    }

    public enum SourceHealth
    {
        Healthy = 0,
        TimedOut = 1
    }

    public readonly struct VehicleSnapshot
    {
        internal VehicleSnapshot(
            VehicleStateWindow window,
            double evaluatedAtMonotonicSeconds,
            double ageSeconds,
            SourceHealth health)
        {
            Window = window ?? throw new ArgumentNullException(nameof(window));
            EvaluatedAtMonotonicSeconds = evaluatedAtMonotonicSeconds;
            AgeSeconds = ageSeconds;
            Health = health;
        }

        public VehicleStateWindow Window { get; }
        public ReceivedVehicleState Latest => Window.Latest;
        public double EvaluatedAtMonotonicSeconds { get; }
        public double AgeSeconds { get; }
        public SourceHealth Health { get; }
        public bool IsTimedOut => Health == SourceHealth.TimedOut;
    }

    public sealed class VehicleStateWindow
    {
        private readonly ReceivedVehicleState[] samples;

        internal VehicleStateWindow(ReceivedVehicleState[] samples)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            this.samples = (ReceivedVehicleState[])samples.Clone();
        }

        public int Count => samples.Length;

        public ReceivedVehicleState this[int index] => samples[index];

        public ReceivedVehicleState Latest
        {
            get
            {
                if (samples.Length == 0) throw new InvalidOperationException("The state window is empty.");
                return samples[samples.Length - 1];
            }
        }

        public ReceivedVehicleState[] ToArray()
        {
            return (ReceivedVehicleState[])samples.Clone();
        }
    }
}
