using System;

namespace UnderwaterRobotScene.Visualization.Data.LocalTesting
{
    public sealed class DefaultDeterministicVehicleStateGenerator : IDeterministicVehicleStateGenerator
    {
        private readonly double sampleIntervalSeconds;

        public DefaultDeterministicVehicleStateGenerator(double sampleIntervalSeconds)
        {
            if (double.IsNaN(sampleIntervalSeconds) ||
                double.IsInfinity(sampleIntervalSeconds) ||
                sampleIntervalSeconds <= 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sampleIntervalSeconds),
                    "Sample interval must be finite and greater than zero.");
            }

            this.sampleIntervalSeconds = sampleIntervalSeconds;
        }

        public VehicleState Evaluate(
            LocalTestVehicle vehicle,
            ulong sampleIndex,
            double sourceTimestampSeconds)
        {
            double step = sampleIndex;
            var position = new Vector3d(
                vehicle.PositionOffset.X + step,
                vehicle.PositionOffset.Y + step * 2.0,
                vehicle.PositionOffset.Z - step);
            var linearVelocity = new Vector3d(
                1.0 / sampleIntervalSeconds,
                2.0 / sampleIntervalSeconds,
                -1.0 / sampleIntervalSeconds);

            return new VehicleState(
                vehicle.VehicleId,
                vehicle.VehicleType,
                sourceTimestampSeconds,
                sampleIndex,
                position,
                Quaterniond.Identity,
                linearVelocity,
                Vector3d.Zero,
                Vector3d.Zero,
                VehicleStateFields.Position |
                VehicleStateFields.Orientation |
                VehicleStateFields.LinearVelocity,
                vehicle.WorldFrame,
                vehicle.BodyFrame);
        }
    }
}
