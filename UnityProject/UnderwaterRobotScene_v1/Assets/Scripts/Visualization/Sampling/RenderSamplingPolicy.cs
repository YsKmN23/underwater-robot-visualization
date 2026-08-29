using System;

namespace UnderwaterRobotScene.Visualization.Sampling
{
    public enum AfterLatestBehavior
    {
        Unknown = 0,
        Reject = 1,
        HoldLatest = 2
    }

    public readonly struct RenderSamplingPolicy
    {
        public RenderSamplingPolicy(
            double maxInterpolationGapSeconds,
            double maxHoldSourceTimeSeconds,
            double exactTimeToleranceSeconds,
            AfterLatestBehavior afterLatestBehavior,
            bool allowSingleSampleHold)
        {
            MaxInterpolationGapSeconds = maxInterpolationGapSeconds;
            MaxHoldSourceTimeSeconds = maxHoldSourceTimeSeconds;
            ExactTimeToleranceSeconds = exactTimeToleranceSeconds;
            AfterLatestBehavior = afterLatestBehavior;
            AllowSingleSampleHold = allowSingleSampleHold;
        }

        public double MaxInterpolationGapSeconds { get; }
        public double MaxHoldSourceTimeSeconds { get; }
        public double ExactTimeToleranceSeconds { get; }
        public AfterLatestBehavior AfterLatestBehavior { get; }
        public bool AllowSingleSampleHold { get; }

        public bool TryValidate(out string error)
        {
            if (!IsFinite(MaxInterpolationGapSeconds) || MaxInterpolationGapSeconds <= 0.0)
            {
                error = "Maximum interpolation gap must be finite and greater than zero.";
                return false;
            }

            if (!IsFinite(MaxHoldSourceTimeSeconds) || MaxHoldSourceTimeSeconds < 0.0)
            {
                error = "Maximum latest-hold source-time window must be finite and non-negative.";
                return false;
            }

            if (!IsFinite(ExactTimeToleranceSeconds) || ExactTimeToleranceSeconds < 0.0)
            {
                error = "Exact-time tolerance must be finite and non-negative.";
                return false;
            }

            if (AfterLatestBehavior != AfterLatestBehavior.Reject &&
                AfterLatestBehavior != AfterLatestBehavior.HoldLatest)
            {
                error = "After-latest behavior must be explicitly Reject or HoldLatest.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
