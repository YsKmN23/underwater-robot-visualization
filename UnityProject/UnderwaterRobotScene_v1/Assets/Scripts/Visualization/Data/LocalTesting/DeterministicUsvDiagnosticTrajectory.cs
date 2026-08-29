using System;

namespace UnderwaterRobotScene.Visualization.Data.LocalTesting
{
    /// <summary>
    /// N6-D USV diagnostic integration data.
    /// This is not production surface-vessel motion behavior.
    /// </summary>
    public sealed class DeterministicUsvDiagnosticTrajectory :
        IDeterministicVehicleStateGenerator
    {
        public const double CycleDurationSeconds = 14.0;
        public const double InitialHoldEndSeconds = 0.75;
        public const double FirstTurnEndSeconds = 6.25;
        public const double MiddleHoldEndSeconds = 7.25;
        public const double SecondTurnEndSeconds = 12.75;
        public const double TurnRadius = 0.30;

        public VehicleState Evaluate(
            LocalTestVehicle vehicle,
            ulong sampleIndex,
            double sourceTimestampSeconds)
        {
            double cycleTime = PositiveModulo(
                sourceTimestampSeconds,
                CycleDurationSeconds);
            Vector3d position = vehicle.PositionOffset;
            Quaterniond orientation = Quaterniond.Identity;

            if (cycleTime >= InitialHoldEndSeconds &&
                cycleTime < FirstTurnEndSeconds)
            {
                EvaluateTurn(
                    vehicle.PositionOffset,
                    cycleTime,
                    InitialHoldEndSeconds,
                    FirstTurnEndSeconds - InitialHoldEndSeconds,
                    1.0,
                    out position,
                    out orientation);
            }
            else if (cycleTime >= MiddleHoldEndSeconds &&
                     cycleTime < SecondTurnEndSeconds)
            {
                EvaluateTurn(
                    vehicle.PositionOffset,
                    cycleTime,
                    MiddleHoldEndSeconds,
                    SecondTurnEndSeconds - MiddleHoldEndSeconds,
                    -1.0,
                    out position,
                    out orientation);
            }

            return new VehicleState(
                vehicle.VehicleId,
                vehicle.VehicleType,
                sourceTimestampSeconds,
                sampleIndex,
                position,
                orientation,
                Vector3d.Zero,
                Vector3d.Zero,
                Vector3d.Zero,
                VehicleStateFields.Position | VehicleStateFields.Orientation,
                vehicle.WorldFrame,
                vehicle.BodyFrame);
        }

        private static void EvaluateTurn(
            Vector3d origin,
            double cycleTime,
            double segmentStart,
            double segmentDuration,
            double direction,
            out Vector3d position,
            out Quaterniond orientation)
        {
            double u = Clamp01((cycleTime - segmentStart) / segmentDuration);
            double eased = SmootherStep(u);
            double theta = 2.0 * Math.PI * eased;
            double xOffset = direction * TurnRadius * (1.0 - Math.Cos(theta));
            double zOffset = TurnRadius * Math.Sin(theta);
            double yaw = direction * theta;

            position = new Vector3d(
                origin.X + xOffset,
                origin.Y,
                origin.Z + zOffset);
            orientation = YawQuaternion(yaw);
        }

        private static Quaterniond YawQuaternion(double yawRadians)
        {
            double halfYaw = yawRadians * 0.5;
            var orientation = new Quaterniond(
                0.0,
                Math.Sin(halfYaw),
                0.0,
                Math.Cos(halfYaw));
            if (!orientation.TryNormalize(out Quaterniond normalized))
            {
                throw new InvalidOperationException(
                    "USV diagnostic yaw produced an invalid quaternion.");
            }
            return normalized;
        }

        private static double SmootherStep(double value)
        {
            double squared = value * value;
            double cubed = squared * value;
            return cubed * (value * (value * 6.0 - 15.0) + 10.0);
        }

        private static double Clamp01(double value)
        {
            return Math.Max(0.0, Math.Min(1.0, value));
        }

        private static double PositiveModulo(double value, double modulus)
        {
            double remainder = value % modulus;
            return remainder < 0.0 ? remainder + modulus : remainder;
        }
    }
}
