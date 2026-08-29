using UnderwaterRobotScene.Visualization.State;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Driving
{
    public static class AuvMvpCombinedPoseMath
    {
        public const string RotationConventionId = "AUV_MVP_ACTIVE_INTRINSIC_ROLL_X_PITCH_Z_YAW_Y_V1";
        public const string ConventionStatus = "MVP_COMBINED_ROTATION_CONVENTION_ONLY";

        public static AuvPoseApplyResult TryCalculate(
            VehiclePoseState state,
            Vector3 baselinePosition,
            Quaternion baselineRotation,
            out Vector3 targetPosition,
            out Quaternion targetRotation,
            out Quaternion dynamicRotation,
            out int effectiveRotationAxisCount)
        {
            targetPosition = baselinePosition;
            targetRotation = baselineRotation;
            dynamicRotation = Quaternion.identity;
            effectiveRotationAxisCount = 0;

            if (!state.valid) return AuvPoseApplyResult.InvalidState;
            if (!IsFinite(state.x) || !IsFinite(state.y) || !IsFinite(state.z) ||
                !IsFinite(state.roll) || !IsFinite(state.pitch) || !IsFinite(state.yaw))
            {
                return AuvPoseApplyResult.NonFiniteValue;
            }

            double roll = AuvMvpSingleAxisPoseMath.NormalizeDegrees(state.roll);
            double pitch = AuvMvpSingleAxisPoseMath.NormalizeDegrees(state.pitch);
            double yaw = AuvMvpSingleAxisPoseMath.NormalizeDegrees(state.yaw);
            effectiveRotationAxisCount = ActiveAxis(roll) + ActiveAxis(pitch) + ActiveAxis(yaw);

            var positionOnlyState = new VehiclePoseState(
                state.timestampSeconds,
                state.sequenceId,
                true,
                state.x,
                state.y,
                state.z,
                0.0,
                0.0,
                0.0);
            AuvPoseApplyResult positionResult = AuvMvpSingleAxisPoseMath.TryCalculate(
                positionOnlyState,
                baselinePosition,
                baselineRotation,
                out targetPosition,
                out _);
            if (positionResult != AuvPoseApplyResult.Applied)
            {
                targetPosition = baselinePosition;
                return positionResult;
            }

            Quaternion qRoll = Quaternion.AngleAxis((float)roll, Vector3.right);
            Quaternion qPitch = Quaternion.AngleAxis((float)pitch, Vector3.forward);
            Quaternion qYaw = Quaternion.AngleAxis((float)yaw, Vector3.up);
            dynamicRotation = qRoll * qPitch * qYaw;
            targetRotation = baselineRotation * dynamicRotation;

            if (!IsFinite(dynamicRotation) || !IsFinite(targetRotation) ||
                !IsApproximatelyUnit(dynamicRotation) || !IsApproximatelyUnit(targetRotation))
            {
                targetPosition = baselinePosition;
                targetRotation = baselineRotation;
                dynamicRotation = Quaternion.identity;
                return AuvPoseApplyResult.FloatRangeOverflow;
            }

            return AuvPoseApplyResult.Applied;
        }

        private static int ActiveAxis(double normalizedAngleDegrees)
        {
            return System.Math.Abs(normalizedAngleDegrees) <= AuvMvpSingleAxisPoseMath.AngleZeroEpsilonDegrees ? 0 : 1;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFinite(Quaternion value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z) &&
                   !float.IsNaN(value.w) && !float.IsInfinity(value.w);
        }

        private static bool IsApproximatelyUnit(Quaternion value)
        {
            double normSquared = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
            return System.Math.Abs(normSquared - 1.0) <= 1e-4;
        }
    }
}
