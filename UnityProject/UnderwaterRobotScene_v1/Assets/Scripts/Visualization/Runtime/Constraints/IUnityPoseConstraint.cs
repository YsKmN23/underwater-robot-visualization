using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime.Constraints
{
    public enum UnityPoseConstraintDecision
    {
        NotEvaluated = 0,
        Apply = 1,
        HoldCurrent = 2
    }

    public readonly struct UnityPoseConstraintRequest
    {
        public UnityPoseConstraintRequest(
            Vector3 position,
            Quaternion rotation,
            ulong sourceEpoch)
        {
            Position = position;
            Rotation = rotation;
            SourceEpoch = sourceEpoch;
        }

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public ulong SourceEpoch { get; }
    }

    public readonly struct UnityPoseConstraintResult
    {
        public UnityPoseConstraintResult(
            UnityPoseConstraintDecision decision,
            Vector3 position,
            Quaternion rotation,
            string reason)
        {
            Decision = decision;
            Position = position;
            Rotation = rotation;
            Reason = reason ?? string.Empty;
        }

        public UnityPoseConstraintDecision Decision { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public string Reason { get; }
    }

    public interface IUnityPoseConstraint
    {
        UnityPoseConstraintResult Constrain(
            in UnityPoseConstraintRequest request);

        void ResetObservation();
    }
}
