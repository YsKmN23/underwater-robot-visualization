using System;
using UnderwaterRobotScene.Visualization.Data;

namespace UnderwaterRobotScene.Visualization.Transforms
{
    public static class QuaternionContinuity
    {
        public static bool TryAlignHemisphere(
            in Quaterniond reference,
            in Quaterniond candidate,
            out Quaterniond aligned,
            out ConversionError error)
        {
            if (!reference.TryNormalize(out Quaterniond normalizedReference))
            {
                aligned = default;
                error = new ConversionError(
                    ConversionFailureReason.InvalidOrientation,
                    "Reference quaternion is not usable.");
                return false;
            }

            if (!candidate.TryNormalize(out Quaterniond normalizedCandidate))
            {
                aligned = default;
                error = new ConversionError(
                    ConversionFailureReason.InvalidOrientation,
                    "Candidate quaternion is not usable.");
                return false;
            }

            aligned = QuaternionMath3d.Dot(normalizedReference, normalizedCandidate) < 0.0
                ? QuaternionMath3d.Negate(normalizedCandidate)
                : normalizedCandidate;
            error = ConversionError.None;
            return true;
        }

        public static double ShortestAngleRadians(Quaterniond left, Quaterniond right)
        {
            if (!left.TryNormalize(out Quaterniond normalizedLeft) ||
                !right.TryNormalize(out Quaterniond normalizedRight))
            {
                throw new ArgumentException("Both quaternions must be usable.");
            }

            double absoluteDot = Math.Abs(QuaternionMath3d.Dot(normalizedLeft, normalizedRight));
            absoluteDot = Math.Max(-1.0, Math.Min(1.0, absoluteDot));
            return 2.0 * Math.Acos(absoluteDot);
        }
    }
}
