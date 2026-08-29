using System;

namespace UnderwaterRobotScene.Visualization.Driving
{
    internal sealed class AuvPosePlaybackCursor
    {
        public bool HasAnchor { get; private set; }
        public bool HasPreviousTarget { get; private set; }
        public double AnchorSourceTimestamp { get; private set; }
        public double AnchorLocalReceiveTime { get; private set; }
        public double PreviousTargetSourceTime { get; private set; }
        public double LastCandidateTarget { get; private set; }
        public double LastMonotonicTargetBeforeClamp { get; private set; }
        public double LastEffectiveTarget { get; private set; }

        public void Reset()
        {
            HasAnchor = false;
            HasPreviousTarget = false;
            AnchorSourceTimestamp = 0.0;
            AnchorLocalReceiveTime = 0.0;
            PreviousTargetSourceTime = 0.0;
            LastCandidateTarget = 0.0;
            LastMonotonicTargetBeforeClamp = 0.0;
            LastEffectiveTarget = 0.0;
        }

        public void SetAnchor(double sourceTimestamp, double localReceiveTime)
        {
            AnchorSourceTimestamp = sourceTimestamp;
            AnchorLocalReceiveTime = localReceiveTime;
            HasAnchor = true;
        }

        public bool TryCalculateTarget(
            double currentLocalTime,
            double interpolationDelay,
            double oldestSourceTimestamp,
            double latestSourceTimestamp,
            out double effectiveTarget)
        {
            effectiveTarget = 0.0;
            if (!HasAnchor || !IsFinite(currentLocalTime) || !IsFinite(interpolationDelay) || interpolationDelay < 0.0 ||
                !IsFinite(oldestSourceTimestamp) || !IsFinite(latestSourceTimestamp) || oldestSourceTimestamp > latestSourceTimestamp)
            {
                return false;
            }

            double localElapsed = Math.Max(0.0, currentLocalTime - AnchorLocalReceiveTime);
            double candidate = AnchorSourceTimestamp + localElapsed - interpolationDelay;
            if (!IsFinite(candidate)) return false;

            double monotonic = HasPreviousTarget ? Math.Max(PreviousTargetSourceTime, candidate) : candidate;
            double clamped = Math.Max(oldestSourceTimestamp, Math.Min(latestSourceTimestamp, monotonic));
            if (!IsFinite(monotonic) || !IsFinite(clamped)) return false;

            LastCandidateTarget = candidate;
            LastMonotonicTargetBeforeClamp = monotonic;
            LastEffectiveTarget = clamped;
            effectiveTarget = clamped;
            return true;
        }

        public void Commit(double effectiveTarget)
        {
            PreviousTargetSourceTime = effectiveTarget;
            HasPreviousTarget = true;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
