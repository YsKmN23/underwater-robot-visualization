using System;

namespace UnderwaterRobotScene.Visualization.Data.LocalTesting
{
    public sealed class DeterministicAuvIntegrationTrajectory : IDeterministicVehicleStateGenerator
    {
        public VehicleState Evaluate(
            LocalTestVehicle vehicle,
            ulong sampleIndex,
            double sourceTimestampSeconds)
        {
            double t = sourceTimestampSeconds;
            var position = new Vector3d(
                vehicle.PositionOffset.X + Math.Sin(t * 0.45) * 0.8,
                vehicle.PositionOffset.Y + Math.Sin(t * 0.31) * 0.25,
                vehicle.PositionOffset.Z + (Math.Cos(t * 0.37) - 1.0) * 0.6);

            double pitchDegrees = Math.Sin(t * 0.41) * 8.0;
            double yawDegrees = Math.Sin(t * 0.25) * 25.0;
            double rollDegrees = Math.Sin(t * 0.33) * 10.0;
            Quaterniond orientation = FromUnityEulerDegrees(
                pitchDegrees,
                yawDegrees,
                rollDegrees);

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

        private static Quaterniond FromUnityEulerDegrees(
            double xDegrees,
            double yDegrees,
            double zDegrees)
        {
            const double DegreesToRadians = Math.PI / 180.0;
            double xHalf = xDegrees * DegreesToRadians * 0.5;
            double yHalf = yDegrees * DegreesToRadians * 0.5;
            double zHalf = zDegrees * DegreesToRadians * 0.5;

            var xRotation = new Quaterniond(Math.Sin(xHalf), 0.0, 0.0, Math.Cos(xHalf));
            var yRotation = new Quaterniond(0.0, Math.Sin(yHalf), 0.0, Math.Cos(yHalf));
            var zRotation = new Quaterniond(0.0, 0.0, Math.Sin(zHalf), Math.Cos(zHalf));

            // Unity Quaternion.Euler applies Z, then X, then Y.
            Quaterniond result = Multiply(Multiply(yRotation, xRotation), zRotation);
            if (!result.TryNormalize(out Quaterniond normalized))
            {
                throw new InvalidOperationException("Deterministic Euler conversion produced an invalid quaternion.");
            }

            return normalized;
        }

        private static Quaterniond Multiply(Quaterniond left, Quaterniond right)
        {
            return new Quaterniond(
                left.W * right.X + left.X * right.W + left.Y * right.Z - left.Z * right.Y,
                left.W * right.Y - left.X * right.Z + left.Y * right.W + left.Z * right.X,
                left.W * right.Z + left.X * right.Y - left.Y * right.X + left.Z * right.W,
                left.W * right.W - left.X * right.X - left.Y * right.Y - left.Z * right.Z);
        }
    }
}
