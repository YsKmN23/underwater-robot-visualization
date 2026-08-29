using UnderwaterRobotScene.Visualization.State;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Driving
{
    public static class AuvMvpSingleAxisPoseMath
    {
        public const double AngleZeroEpsilonDegrees = 1e-9;

        public static AuvPoseApplyResult TryCalculate(
            VehiclePoseState state,
            Vector3 baselinePosition,
            Quaternion baselineRotation,
            out Vector3 targetPosition,
            out Quaternion targetRotation)
        {
            targetPosition = baselinePosition;
            targetRotation = baselineRotation;

            if (!IsFinite(state.x) || !IsFinite(state.y) || !IsFinite(state.z) ||
                !IsFinite(state.roll) || !IsFinite(state.pitch) || !IsFinite(state.yaw))
            {
                return AuvPoseApplyResult.NonFiniteValue;
            }

            if (!CanRepresentFloat(state.x) || !CanRepresentFloat(state.y) || !CanRepresentFloat(state.z))
            {
                return AuvPoseApplyResult.FloatRangeOverflow;
            }

            double roll = NormalizeDegrees(state.roll);
            double pitch = NormalizeDegrees(state.pitch);
            double yaw = NormalizeDegrees(state.yaw);
            if (!CanRepresentFloat(roll) || !CanRepresentFloat(pitch) || !CanRepresentFloat(yaw))
            {
                return AuvPoseApplyResult.FloatRangeOverflow;
            }

            bool hasRoll = !IsZeroAngle(roll);
            bool hasPitch = !IsZeroAngle(pitch);
            bool hasYaw = !IsZeroAngle(yaw);
            int activeRotationAxes = (hasRoll ? 1 : 0) + (hasPitch ? 1 : 0) + (hasYaw ? 1 : 0);
            if (activeRotationAxes > 1) return AuvPoseApplyResult.MultipleRotationAxes;

            var localOffset = new Vector3((float)state.x, (float)state.z, (float)state.y);
            targetPosition = baselinePosition + baselineRotation * localOffset;
            if (!IsFinite(targetPosition))
            {
                targetPosition = baselinePosition;
                return AuvPoseApplyResult.FloatRangeOverflow;
            }

            if (hasRoll)
            {
                targetRotation = baselineRotation * Quaternion.AngleAxis((float)roll, Vector3.right);
            }
            else if (hasPitch)
            {
                targetRotation = baselineRotation * Quaternion.AngleAxis((float)pitch, Vector3.forward);
            }
            else if (hasYaw)
            {
                targetRotation = baselineRotation * Quaternion.AngleAxis((float)yaw, Vector3.up);
            }

            if (!IsFinite(targetRotation))
            {
                targetPosition = baselinePosition;
                targetRotation = baselineRotation;
                return AuvPoseApplyResult.FloatRangeOverflow;
            }

            return AuvPoseApplyResult.Applied;
        }

        public static double NormalizeDegrees(double angleDegrees)
        {
            double normalized = angleDegrees % 360.0;
            if (normalized > 180.0) normalized -= 360.0;
            if (normalized <= -180.0) normalized += 360.0;
            return normalized;
        }

        private static bool IsZeroAngle(double angleDegrees)
        {
            return System.Math.Abs(angleDegrees) <= AngleZeroEpsilonDegrees;
        }

        private static bool CanRepresentFloat(double value)
        {
            if (!IsFinite(value) || value > float.MaxValue || value < -float.MaxValue) return false;
            float converted = (float)value;
            return !float.IsNaN(converted) && !float.IsInfinity(converted);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z) &&
                   !float.IsNaN(value.w) && !float.IsInfinity(value.w);
        }
    }
}
