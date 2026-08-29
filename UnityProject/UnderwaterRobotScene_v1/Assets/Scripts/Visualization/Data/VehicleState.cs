using System;

namespace UnderwaterRobotScene.Visualization.Data
{
    public enum VehicleType
    {
        Unknown = 0,
        Auv = 1,
        Rov = 2,
        Usv = 3
    }

    public enum WorldFrame
    {
        Unknown = 0,
        Ned = 1,
        Enu = 2,
        UnityWorld = 3
    }

    public enum BodyFrame
    {
        Unknown = 0,
        Frd = 1,
        Flu = 2,
        UnityBody = 3
    }

    [Flags]
    public enum VehicleStateFields : uint
    {
        None = 0,
        Position = 1U << 0,
        Orientation = 1U << 1,
        LinearVelocity = 1U << 2,
        AngularVelocity = 1U << 3,
        LinearAcceleration = 1U << 4
    }

    public readonly struct Vector3d : IEquatable<Vector3d>
    {
        public Vector3d(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vector3d Zero => new Vector3d(0.0, 0.0, 0.0);

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public bool IsFinite => Numeric.IsFinite(X) && Numeric.IsFinite(Y) && Numeric.IsFinite(Z);

        public bool Equals(Vector3d other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        }

        public override bool Equals(object obj)
        {
            return obj is Vector3d other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                return (hash * 397) ^ Z.GetHashCode();
            }
        }
    }

    public readonly struct Quaterniond : IEquatable<Quaterniond>
    {
        public Quaterniond(double x, double y, double z, double w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public static Quaterniond Identity => new Quaterniond(0.0, 0.0, 0.0, 1.0);

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double W { get; }

        public bool IsFinite =>
            Numeric.IsFinite(X) && Numeric.IsFinite(Y) && Numeric.IsFinite(Z) && Numeric.IsFinite(W);

        public double MagnitudeSquared => X * X + Y * Y + Z * Z + W * W;

        public bool IsUsable => IsFinite && Numeric.IsFinite(MagnitudeSquared) && MagnitudeSquared > 1e-12;

        public bool TryNormalize(out Quaterniond normalized)
        {
            if (!IsUsable)
            {
                normalized = default;
                return false;
            }

            double inverseMagnitude = 1.0 / Math.Sqrt(MagnitudeSquared);
            normalized = new Quaterniond(
                X * inverseMagnitude,
                Y * inverseMagnitude,
                Z * inverseMagnitude,
                W * inverseMagnitude);
            return true;
        }

        public bool Equals(Quaterniond other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);
        }

        public override bool Equals(object obj)
        {
            return obj is Quaterniond other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                hash = (hash * 397) ^ Z.GetHashCode();
                return (hash * 397) ^ W.GetHashCode();
            }
        }
    }

    public readonly struct VehicleState : IEquatable<VehicleState>
    {
        public VehicleState(
            string vehicleId,
            VehicleType vehicleType,
            double sourceTimestampSeconds,
            ulong sequenceNumber,
            Vector3d position,
            Quaterniond orientation,
            Vector3d linearVelocity,
            Vector3d angularVelocity,
            Vector3d linearAcceleration,
            VehicleStateFields validFields,
            WorldFrame worldFrame,
            BodyFrame bodyFrame)
        {
            VehicleId = vehicleId;
            VehicleType = vehicleType;
            SourceTimestampSeconds = sourceTimestampSeconds;
            SequenceNumber = sequenceNumber;
            Position = position;
            Orientation = (validFields & VehicleStateFields.Orientation) != 0 &&
                          orientation.TryNormalize(out Quaterniond normalizedOrientation)
                ? normalizedOrientation
                : orientation;
            LinearVelocity = linearVelocity;
            AngularVelocity = angularVelocity;
            LinearAcceleration = linearAcceleration;
            ValidFields = validFields;
            WorldFrame = worldFrame;
            BodyFrame = bodyFrame;
        }

        public string VehicleId { get; }
        public VehicleType VehicleType { get; }
        public double SourceTimestampSeconds { get; }
        public ulong SequenceNumber { get; }
        public Vector3d Position { get; }
        public Quaterniond Orientation { get; }
        public Vector3d LinearVelocity { get; }
        public Vector3d AngularVelocity { get; }
        public Vector3d LinearAcceleration { get; }
        public VehicleStateFields ValidFields { get; }
        public WorldFrame WorldFrame { get; }
        public BodyFrame BodyFrame { get; }

        public bool IsStructurallyValid
        {
            get
            {
                if (string.IsNullOrWhiteSpace(VehicleId) || !Numeric.IsFinite(SourceTimestampSeconds))
                {
                    return false;
                }

                if ((ValidFields & VehicleStateFields.Position) != 0 && !Position.IsFinite)
                {
                    return false;
                }

                if ((ValidFields & VehicleStateFields.Orientation) != 0 && !Orientation.IsUsable)
                {
                    return false;
                }

                if ((ValidFields & VehicleStateFields.LinearVelocity) != 0 && !LinearVelocity.IsFinite)
                {
                    return false;
                }

                if ((ValidFields & VehicleStateFields.AngularVelocity) != 0 && !AngularVelocity.IsFinite)
                {
                    return false;
                }

                return (ValidFields & VehicleStateFields.LinearAcceleration) == 0 || LinearAcceleration.IsFinite;
            }
        }

        public bool Equals(VehicleState other)
        {
            return string.Equals(VehicleId, other.VehicleId, StringComparison.Ordinal) &&
                   VehicleType == other.VehicleType &&
                   SourceTimestampSeconds.Equals(other.SourceTimestampSeconds) &&
                   SequenceNumber == other.SequenceNumber &&
                   Position.Equals(other.Position) &&
                   Orientation.Equals(other.Orientation) &&
                   LinearVelocity.Equals(other.LinearVelocity) &&
                   AngularVelocity.Equals(other.AngularVelocity) &&
                   LinearAcceleration.Equals(other.LinearAcceleration) &&
                   ValidFields == other.ValidFields &&
                   WorldFrame == other.WorldFrame &&
                   BodyFrame == other.BodyFrame;
        }

        public override bool Equals(object obj)
        {
            return obj is VehicleState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = VehicleId == null ? 0 : StringComparer.Ordinal.GetHashCode(VehicleId);
                hash = (hash * 397) ^ (int)VehicleType;
                hash = (hash * 397) ^ SourceTimestampSeconds.GetHashCode();
                hash = (hash * 397) ^ SequenceNumber.GetHashCode();
                hash = (hash * 397) ^ Position.GetHashCode();
                hash = (hash * 397) ^ Orientation.GetHashCode();
                hash = (hash * 397) ^ LinearVelocity.GetHashCode();
                hash = (hash * 397) ^ AngularVelocity.GetHashCode();
                hash = (hash * 397) ^ LinearAcceleration.GetHashCode();
                hash = (hash * 397) ^ (int)ValidFields;
                hash = (hash * 397) ^ (int)WorldFrame;
                return (hash * 397) ^ (int)BodyFrame;
            }
        }
    }

    internal static class Numeric
    {
        public static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
