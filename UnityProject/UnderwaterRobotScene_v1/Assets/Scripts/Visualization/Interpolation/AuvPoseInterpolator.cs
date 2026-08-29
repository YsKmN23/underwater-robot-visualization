using System;
using UnderwaterRobotScene.Visualization.Buffering;
using UnderwaterRobotScene.Visualization.Driving;
using UnderwaterRobotScene.Visualization.State;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Interpolation
{
    public static class AuvPoseInterpolator
    {
        public static AuvPoseInterpolationResult TrySample(
            VehiclePoseStateBuffer buffer,
            double targetTimestampSeconds,
            Vector3 baselinePosition,
            Quaternion baselineRotation,
            AuvMvpRotationInputMode rotationInputMode,
            out Vector3 targetPosition,
            out Quaternion targetRotation,
            out double interpolationT)
        {
            SetSafeOutputs(out targetPosition, out targetRotation, out interpolationT);
            if (buffer == null || !buffer.IsInitialized)
            {
                return AuvPoseInterpolationResult.BufferNotInitialized;
            }

            if (!IsFinite(targetTimestampSeconds))
            {
                return AuvPoseInterpolationResult.InvalidTargetTime;
            }

            if (buffer.Count == 0)
            {
                return AuvPoseInterpolationResult.NoSamples;
            }

            if (!buffer.TryGetOldest(out VehiclePoseState oldest, out _))
            {
                return AuvPoseInterpolationResult.InvalidBufferState;
            }

            if (buffer.Count == 1)
            {
                return Hold(oldest, AuvPoseInterpolationResult.HoldOnlySample, baselinePosition, baselineRotation,
                    rotationInputMode, out targetPosition, out targetRotation, out interpolationT);
            }

            if (!buffer.TryGetLatest(out VehiclePoseState latest, out _) ||
                !IsFinite(oldest.timestampSeconds) || !IsFinite(latest.timestampSeconds) ||
                oldest.timestampSeconds >= latest.timestampSeconds)
            {
                return AuvPoseInterpolationResult.InvalidBufferState;
            }

            if (targetTimestampSeconds <= oldest.timestampSeconds)
            {
                return Hold(oldest, AuvPoseInterpolationResult.HoldOldest, baselinePosition, baselineRotation,
                    rotationInputMode, out targetPosition, out targetRotation, out interpolationT);
            }

            if (targetTimestampSeconds >= latest.timestampSeconds)
            {
                return Hold(latest, AuvPoseInterpolationResult.HoldLatest, baselinePosition, baselineRotation,
                    rotationInputMode, out targetPosition, out targetRotation, out interpolationT);
            }

            VehiclePoseState previous = oldest;
            for (int index = 1; index < buffer.Count; index++)
            {
                if (!buffer.TryGetAtLogicalIndex(index, out VehiclePoseState current, out _) ||
                    !IsFinite(current.timestampSeconds) || current.timestampSeconds <= previous.timestampSeconds)
                {
                    return AuvPoseInterpolationResult.InvalidBufferState;
                }

                if (targetTimestampSeconds == current.timestampSeconds && index < buffer.Count - 1)
                {
                    return Hold(current, AuvPoseInterpolationResult.HoldExactSample, baselinePosition, baselineRotation,
                        rotationInputMode, out targetPosition, out targetRotation, out interpolationT);
                }

                if (previous.timestampSeconds < targetTimestampSeconds && targetTimestampSeconds < current.timestampSeconds)
                {
                    return Interpolate(previous, current, targetTimestampSeconds, baselinePosition, baselineRotation,
                        rotationInputMode, out targetPosition, out targetRotation, out interpolationT);
                }

                previous = current;
            }

            return AuvPoseInterpolationResult.InvalidBufferState;
        }

        internal static Quaternion InterpolateRotation(Quaternion a, Quaternion b, float t)
        {
            return Quaternion.Slerp(a, b, t);
        }

        internal static AuvPoseApplyResult TryConvertEndpoint(
            VehiclePoseState state,
            Vector3 baselinePosition,
            Quaternion baselineRotation,
            AuvMvpRotationInputMode rotationInputMode,
            out Vector3 targetPosition,
            out Quaternion targetRotation)
        {
            targetPosition = Vector3.zero;
            targetRotation = Quaternion.identity;
            if (!state.valid) return AuvPoseApplyResult.InvalidState;
            if (!IsFinite(baselinePosition) || !IsFinite(baselineRotation)) return AuvPoseApplyResult.NonFiniteValue;
            if (QuaternionNormSquared(baselineRotation) <= 0.0) return AuvPoseApplyResult.FloatRangeOverflow;

            AuvPoseApplyResult result = AuvMvpSingleAxisPoseMath.TryCalculate(
                state, baselinePosition, baselineRotation, out Vector3 calculatedPosition, out Quaternion calculatedRotation);
            if (result == AuvPoseApplyResult.MultipleRotationAxes &&
                rotationInputMode == AuvMvpRotationInputMode.CombinedEulerMvp)
            {
                result = AuvMvpCombinedPoseMath.TryCalculate(
                    state, baselinePosition, baselineRotation, out calculatedPosition, out calculatedRotation, out _, out _);
            }

            if (result != AuvPoseApplyResult.Applied || !IsFinite(calculatedPosition) || !IsFinite(calculatedRotation))
            {
                return result == AuvPoseApplyResult.Applied ? AuvPoseApplyResult.FloatRangeOverflow : result;
            }

            targetPosition = calculatedPosition;
            targetRotation = calculatedRotation;
            return AuvPoseApplyResult.Applied;
        }

        private static AuvPoseInterpolationResult Hold(
            VehiclePoseState state,
            AuvPoseInterpolationResult successResult,
            Vector3 baselinePosition,
            Quaternion baselineRotation,
            AuvMvpRotationInputMode rotationInputMode,
            out Vector3 targetPosition,
            out Quaternion targetRotation,
            out double interpolationT)
        {
            SetSafeOutputs(out targetPosition, out targetRotation, out interpolationT);
            AuvPoseApplyResult endpointResult = TryConvertEndpoint(
                state, baselinePosition, baselineRotation, rotationInputMode, out Vector3 position, out Quaternion rotation);
            if (endpointResult != AuvPoseApplyResult.Applied)
            {
                return AuvPoseInterpolationResult.InvalidEndpoint;
            }

            targetPosition = position;
            targetRotation = rotation;
            return successResult;
        }

        private static AuvPoseInterpolationResult Interpolate(
            VehiclePoseState stateA,
            VehiclePoseState stateB,
            double targetTimestampSeconds,
            Vector3 baselinePosition,
            Quaternion baselineRotation,
            AuvMvpRotationInputMode rotationInputMode,
            out Vector3 targetPosition,
            out Quaternion targetRotation,
            out double interpolationT)
        {
            SetSafeOutputs(out targetPosition, out targetRotation, out interpolationT);
            AuvPoseApplyResult resultA = TryConvertEndpoint(
                stateA, baselinePosition, baselineRotation, rotationInputMode, out Vector3 positionA, out Quaternion rotationA);
            AuvPoseApplyResult resultB = TryConvertEndpoint(
                stateB, baselinePosition, baselineRotation, rotationInputMode, out Vector3 positionB, out Quaternion rotationB);
            if (resultA != AuvPoseApplyResult.Applied || resultB != AuvPoseApplyResult.Applied)
            {
                return AuvPoseInterpolationResult.InvalidEndpoint;
            }

            double duration = stateB.timestampSeconds - stateA.timestampSeconds;
            if (!IsFinite(duration) || duration <= 0.0)
            {
                return AuvPoseInterpolationResult.InvalidBufferState;
            }

            double t = (targetTimestampSeconds - stateA.timestampSeconds) / duration;
            if (!IsFinite(t))
            {
                return AuvPoseInterpolationResult.InvalidBufferState;
            }

            t = Math.Max(0.0, Math.Min(1.0, t));
            if (!(t > 0.0 && t < 1.0))
            {
                return AuvPoseInterpolationResult.InvalidBufferState;
            }

            Vector3 interpolatedPosition = Vector3.Lerp(positionA, positionB, (float)t);
            Quaternion interpolatedRotation = InterpolateRotation(rotationA, rotationB, (float)t);
            if (!IsFinite(interpolatedPosition) || !IsFinite(interpolatedRotation))
            {
                return AuvPoseInterpolationResult.InvalidEndpoint;
            }

            targetPosition = interpolatedPosition;
            targetRotation = interpolatedRotation;
            interpolationT = t;
            return AuvPoseInterpolationResult.Interpolated;
        }

        private static void SetSafeOutputs(out Vector3 targetPosition, out Quaternion targetRotation, out double interpolationT)
        {
            targetPosition = Vector3.zero;
            targetRotation = Quaternion.identity;
            interpolationT = 0.0;
        }

        private static double QuaternionNormSquared(Quaternion value)
        {
            return value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
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
