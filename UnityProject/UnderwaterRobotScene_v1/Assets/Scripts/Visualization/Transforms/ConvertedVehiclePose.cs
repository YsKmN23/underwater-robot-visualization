using System;
using UnderwaterRobotScene.Visualization.Data;

namespace UnderwaterRobotScene.Visualization.Transforms
{
    public enum ConversionFailureReason
    {
        None = 0,
        InvalidProfileId = 1,
        UnknownWorldFrame = 2,
        UnknownBodyFrame = 3,
        InvalidWorldBasis = 4,
        InvalidBodyBasis = 5,
        BasisHandednessMismatch = 6,
        InvalidPositionScale = 7,
        UnknownAttitudeDirection = 8,
        InvalidModelAlignment = 9,
        InvalidInputState = 10,
        MissingPoseFields = 11,
        FrameMismatch = 12,
        InvalidOrientation = 13,
        NonFiniteResult = 14
    }

    public readonly struct ConversionError : IEquatable<ConversionError>
    {
        public ConversionError(ConversionFailureReason reason, string message)
        {
            Reason = reason;
            Message = message ?? string.Empty;
        }

        public static ConversionError None => new ConversionError(ConversionFailureReason.None, string.Empty);

        public ConversionFailureReason Reason { get; }
        public string Message { get; }
        public bool IsNone => Reason == ConversionFailureReason.None;

        public bool Equals(ConversionError other)
        {
            return Reason == other.Reason &&
                   string.Equals(Message, other.Message, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is ConversionError other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Reason * 397) ^ StringComparer.Ordinal.GetHashCode(Message ?? string.Empty);
            }
        }
    }

    public readonly struct ConvertedVehiclePose
    {
        internal ConvertedVehiclePose(
            string profileId,
            string vehicleId,
            double sourceTimestampSeconds,
            ulong sequenceNumber,
            Vector3d position,
            Quaterniond orientation)
        {
            ProfileId = profileId;
            VehicleId = vehicleId;
            SourceTimestampSeconds = sourceTimestampSeconds;
            SequenceNumber = sequenceNumber;
            Position = position;
            Orientation = orientation;
            Succeeded = true;
        }

        public string ProfileId { get; }
        public string VehicleId { get; }
        public double SourceTimestampSeconds { get; }
        public ulong SequenceNumber { get; }
        public Vector3d Position { get; }
        public Quaterniond Orientation { get; }
        public bool Succeeded { get; }
    }
}
