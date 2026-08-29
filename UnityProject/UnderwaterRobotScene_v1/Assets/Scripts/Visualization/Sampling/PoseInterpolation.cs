using System;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Transforms;

namespace UnderwaterRobotScene.Visualization.Sampling
{
    public static class PoseInterpolation
    {
        private const double NearlyParallelDot = 0.9995;

        public static bool TryLerpPosition(
            Vector3d before,
            Vector3d after,
            double alpha,
            out Vector3d position)
        {
            if (!before.IsFinite || !after.IsFinite || !IsUnitInterval(alpha))
            {
                position = default;
                return false;
            }

            position = new Vector3d(
                before.X + (after.X - before.X) * alpha,
                before.Y + (after.Y - before.Y) * alpha,
                before.Z + (after.Z - before.Z) * alpha);
            if (!position.IsFinite)
            {
                position = default;
                return false;
            }

            return true;
        }

        public static bool TrySlerp(
            Quaterniond before,
            Quaterniond after,
            double alpha,
            out Quaterniond orientation)
        {
            orientation = default;
            if (!IsUnitInterval(alpha) ||
                !before.TryNormalize(out Quaterniond first) ||
                !after.TryNormalize(out Quaterniond second))
            {
                return false;
            }

            double dot = QuaternionMath3d.Dot(first, second);
            if (dot < 0.0)
            {
                second = QuaternionMath3d.Negate(second);
                dot = -dot;
            }

            dot = Math.Max(0.0, Math.Min(1.0, dot));
            if (dot > NearlyParallelDot)
            {
                return TryNormalizedBlend(first, second, alpha, out orientation);
            }

            double angle = Math.Acos(dot);
            double sine = Math.Sin(angle);
            if (!IsFinite(sine) || Math.Abs(sine) <= 1e-12)
            {
                return TryNormalizedBlend(first, second, alpha, out orientation);
            }

            double beforeWeight = Math.Sin((1.0 - alpha) * angle) / sine;
            double afterWeight = Math.Sin(alpha * angle) / sine;
            var blended = new Quaterniond(
                beforeWeight * first.X + afterWeight * second.X,
                beforeWeight * first.Y + afterWeight * second.Y,
                beforeWeight * first.Z + afterWeight * second.Z,
                beforeWeight * first.W + afterWeight * second.W);
            return blended.TryNormalize(out orientation);
        }

        private static bool TryNormalizedBlend(
            Quaterniond before,
            Quaterniond after,
            double alpha,
            out Quaterniond orientation)
        {
            var blended = new Quaterniond(
                before.X + (after.X - before.X) * alpha,
                before.Y + (after.Y - before.Y) * alpha,
                before.Z + (after.Z - before.Z) * alpha,
                before.W + (after.W - before.W) * alpha);
            return blended.TryNormalize(out orientation);
        }

        private static bool IsUnitInterval(double value)
        {
            return IsFinite(value) && value >= 0.0 && value <= 1.0;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
