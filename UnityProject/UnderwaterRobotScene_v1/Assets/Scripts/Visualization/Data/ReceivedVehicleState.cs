using System;

namespace UnderwaterRobotScene.Visualization.Data
{
    public enum SequenceKind
    {
        Protocol = 0,
        Synthetic = 1
    }

    [Flags]
    public enum DecodeQualityFlags : uint
    {
        None = 0,
        Warning = 1U << 0,
        RecoveredValue = 1U << 1
    }

    public readonly struct ReceivedVehicleState : IEquatable<ReceivedVehicleState>
    {
        public ReceivedVehicleState(
            VehicleState state,
            string sourceId,
            ulong sourceEpoch,
            double receivedAtMonotonicSeconds,
            SequenceKind sequenceKind,
            DecodeQualityFlags decodeQuality)
        {
            State = state;
            SourceId = sourceId;
            SourceEpoch = sourceEpoch;
            ReceivedAtMonotonicSeconds = receivedAtMonotonicSeconds;
            SequenceKind = sequenceKind;
            DecodeQuality = decodeQuality;
        }

        public VehicleState State { get; }
        public string SourceId { get; }
        public ulong SourceEpoch { get; }
        public double ReceivedAtMonotonicSeconds { get; }
        public SequenceKind SequenceKind { get; }
        public DecodeQualityFlags DecodeQuality { get; }

        public bool IsStructurallyValid =>
            State.IsStructurallyValid &&
            !string.IsNullOrWhiteSpace(SourceId) &&
            Numeric.IsFinite(ReceivedAtMonotonicSeconds) &&
            ReceivedAtMonotonicSeconds >= 0.0;

        public bool Equals(ReceivedVehicleState other)
        {
            return State.Equals(other.State) &&
                   string.Equals(SourceId, other.SourceId, StringComparison.Ordinal) &&
                   SourceEpoch == other.SourceEpoch &&
                   ReceivedAtMonotonicSeconds.Equals(other.ReceivedAtMonotonicSeconds) &&
                   SequenceKind == other.SequenceKind &&
                   DecodeQuality == other.DecodeQuality;
        }

        public override bool Equals(object obj)
        {
            return obj is ReceivedVehicleState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = State.GetHashCode();
                hash = (hash * 397) ^ (SourceId == null ? 0 : StringComparer.Ordinal.GetHashCode(SourceId));
                hash = (hash * 397) ^ SourceEpoch.GetHashCode();
                hash = (hash * 397) ^ ReceivedAtMonotonicSeconds.GetHashCode();
                hash = (hash * 397) ^ (int)SequenceKind;
                return (hash * 397) ^ (int)DecodeQuality;
            }
        }
    }
}
