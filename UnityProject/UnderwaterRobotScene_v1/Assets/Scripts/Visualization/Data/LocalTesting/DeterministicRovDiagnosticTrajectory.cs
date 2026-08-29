using System;
using UnderwaterRobotScene.Visualization.Sampling;

namespace UnderwaterRobotScene.Visualization.Data.LocalTesting
{
    /// <summary>
    /// N6-B ROV diagnostic integration data.
    /// Not production motion behavior.
    /// </summary>
    public sealed class DeterministicRovDiagnosticTrajectory :
        IDeterministicVehicleStateGenerator
    {
        public const double CycleDurationSeconds = 12.0;
        public const double InitialHoldEndSeconds = 0.75;
        public const double FirstMotionEndSeconds = 3.0;
        public const double FirstHoverEndSeconds = 4.25;
        public const double SecondMotionEndSeconds = 7.0;
        public const double SecondHoverEndSeconds = 8.25;
        public const double ReturnMotionEndSeconds = 11.25;

        private static readonly Vector3d PoseAOffset =
            new Vector3d(0.30, 0.12, -0.18);
        private static readonly Vector3d PoseBOffset =
            new Vector3d(-0.22, -0.10, 0.22);
        private static readonly Quaterniond PoseAOrientation =
            FromUnityEulerDegrees(-4.0, 12.0, 5.0);
        private static readonly Quaterniond PoseBOrientation =
            FromUnityEulerDegrees(5.0, -14.0, -6.0);

        public VehicleState Evaluate(
            LocalTestVehicle vehicle,
            ulong sampleIndex,
            double sourceTimestampSeconds)
        {
            if (double.IsNaN(sourceTimestampSeconds) ||
                double.IsInfinity(sourceTimestampSeconds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sourceTimestampSeconds),
                    "Source timestamp must be finite.");
            }

            double cycleTime = PositiveModulo(
                sourceTimestampSeconds,
                CycleDurationSeconds);
            Vector3d origin = vehicle.PositionOffset;
            Vector3d poseA = Add(origin, PoseAOffset);
            Vector3d poseB = Add(origin, PoseBOffset);
            Vector3d position;
            Quaterniond orientation;

            if (cycleTime < InitialHoldEndSeconds)
            {
                position = origin;
                orientation = Quaterniond.Identity;
            }
            else if (cycleTime < FirstMotionEndSeconds)
            {
                EvaluateSegment(
                    origin,
                    Quaterniond.Identity,
                    poseA,
                    PoseAOrientation,
                    cycleTime,
                    InitialHoldEndSeconds,
                    FirstMotionEndSeconds,
                    out position,
                    out orientation);
            }
            else if (cycleTime < FirstHoverEndSeconds)
            {
                position = poseA;
                orientation = PoseAOrientation;
            }
            else if (cycleTime < SecondMotionEndSeconds)
            {
                EvaluateSegment(
                    poseA,
                    PoseAOrientation,
                    poseB,
                    PoseBOrientation,
                    cycleTime,
                    FirstHoverEndSeconds,
                    SecondMotionEndSeconds,
                    out position,
                    out orientation);
            }
            else if (cycleTime < SecondHoverEndSeconds)
            {
                position = poseB;
                orientation = PoseBOrientation;
            }
            else if (cycleTime < ReturnMotionEndSeconds)
            {
                EvaluateSegment(
                    poseB,
                    PoseBOrientation,
                    origin,
                    Quaterniond.Identity,
                    cycleTime,
                    SecondHoverEndSeconds,
                    ReturnMotionEndSeconds,
                    out position,
                    out orientation);
            }
            else
            {
                position = origin;
                orientation = Quaterniond.Identity;
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

        private static void EvaluateSegment(
            Vector3d startPosition,
            Quaterniond startOrientation,
            Vector3d endPosition,
            Quaterniond endOrientation,
            double cycleTime,
            double segmentStart,
            double segmentEnd,
            out Vector3d position,
            out Quaterniond orientation)
        {
            double normalizedTime =
                Clamp01((cycleTime - segmentStart) / (segmentEnd - segmentStart));
            double interpolation = Clamp01(Smootherstep(normalizedTime));
            if (!PoseInterpolation.TryLerpPosition(
                    startPosition,
                    endPosition,
                    interpolation,
                    out position) ||
                !PoseInterpolation.TrySlerp(
                    startOrientation,
                    endOrientation,
                    interpolation,
                    out orientation))
            {
                throw new InvalidOperationException(
                    "ROV diagnostic segment interpolation failed.");
            }
        }

        private static double PositiveModulo(double value, double modulus)
        {
            double result = value % modulus;
            return result < 0.0 ? result + modulus : result;
        }

        private static double Clamp01(double value)
        {
            return Math.Max(0.0, Math.Min(1.0, value));
        }

        private static double Smootherstep(double value)
        {
            return value * value * value *
                   (value * (value * 6.0 - 15.0) + 10.0);
        }

        private static Vector3d Add(Vector3d left, Vector3d right)
        {
            return new Vector3d(
                left.X + right.X,
                left.Y + right.Y,
                left.Z + right.Z);
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

            var xRotation = new Quaterniond(
                Math.Sin(xHalf),
                0.0,
                0.0,
                Math.Cos(xHalf));
            var yRotation = new Quaterniond(
                0.0,
                Math.Sin(yHalf),
                0.0,
                Math.Cos(yHalf));
            var zRotation = new Quaterniond(
                0.0,
                0.0,
                Math.Sin(zHalf),
                Math.Cos(zHalf));

            // Matches Unity Quaternion.Euler: Z, then X, then Y.
            Quaterniond result = Multiply(
                Multiply(yRotation, xRotation),
                zRotation);
            if (!result.TryNormalize(out Quaterniond normalized))
            {
                throw new InvalidOperationException(
                    "ROV diagnostic Euler conversion produced an invalid quaternion.");
            }

            return normalized;
        }

        private static Quaterniond Multiply(Quaterniond left, Quaterniond right)
        {
            return new Quaterniond(
                left.W * right.X + left.X * right.W +
                left.Y * right.Z - left.Z * right.Y,
                left.W * right.Y - left.X * right.Z +
                left.Y * right.W + left.Z * right.X,
                left.W * right.Z + left.X * right.Y -
                left.Y * right.X + left.Z * right.W,
                left.W * right.W - left.X * right.X -
                left.Y * right.Y - left.Z * right.Z);
        }
    }
}
