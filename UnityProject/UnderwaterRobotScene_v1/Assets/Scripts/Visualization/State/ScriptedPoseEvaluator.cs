using System;

namespace UnderwaterRobotScene.Visualization.State
{
    public static class ScriptedPoseEvaluator
    {
        public static VehiclePoseState Evaluate(
            ScriptedPoseMode mode,
            long sampleIndex,
            double sampleRateHz,
            double positionAmplitude,
            double angleAmplitudeDegrees,
            double periodSeconds,
            bool valid)
        {
            if (sampleIndex < 0) throw new ArgumentOutOfRangeException(nameof(sampleIndex));
            if (!IsFinitePositive(sampleRateHz)) throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
            if (!IsFiniteNonNegative(positionAmplitude)) throw new ArgumentOutOfRangeException(nameof(positionAmplitude));
            if (!IsFiniteNonNegative(angleAmplitudeDegrees)) throw new ArgumentOutOfRangeException(nameof(angleAmplitudeDegrees));
            if (!IsFinitePositive(periodSeconds)) throw new ArgumentOutOfRangeException(nameof(periodSeconds));

            double timestampSeconds = sampleIndex / sampleRateHz;
            double phase = 2.0 * Math.PI * timestampSeconds / periodSeconds;
            double signedPulse = 0.5 * (1.0 - Math.Cos(phase));
            double position = positionAmplitude * signedPulse;
            double angle = angleAmplitudeDegrees * signedPulse;
            double x = 0.0;
            double y = 0.0;
            double z = 0.0;
            double roll = 0.0;
            double pitch = 0.0;
            double yaw = 0.0;

            switch (mode)
            {
                case ScriptedPoseMode.XPositive: x = position; break;
                case ScriptedPoseMode.XNegative: x = -position; break;
                case ScriptedPoseMode.YPositive: y = position; break;
                case ScriptedPoseMode.YNegative: y = -position; break;
                case ScriptedPoseMode.ZPositive: z = position; break;
                case ScriptedPoseMode.ZNegative: z = -position; break;
                case ScriptedPoseMode.RollPositive: roll = angle; break;
                case ScriptedPoseMode.RollNegative: roll = -angle; break;
                case ScriptedPoseMode.PitchPositive: pitch = angle; break;
                case ScriptedPoseMode.PitchNegative: pitch = -angle; break;
                case ScriptedPoseMode.YawPositive: yaw = angle; break;
                case ScriptedPoseMode.YawNegative: yaw = -angle; break;
                case ScriptedPoseMode.Combined6DoF:
                    x = positionAmplitude * Math.Sin(phase);
                    y = positionAmplitude * Math.Sin(phase + Math.PI / 3.0);
                    z = positionAmplitude * Math.Sin(phase + 2.0 * Math.PI / 3.0);
                    roll = angleAmplitudeDegrees * Math.Sin(phase + Math.PI / 6.0);
                    pitch = angleAmplitudeDegrees * Math.Sin(phase + Math.PI / 2.0);
                    yaw = angleAmplitudeDegrees * Math.Sin(phase + 5.0 * Math.PI / 6.0);
                    break;
                case ScriptedPoseMode.Static:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }

            return new VehiclePoseState(
                timestampSeconds,
                checked((ulong)sampleIndex),
                valid,
                x,
                y,
                z,
                roll,
                pitch,
                yaw);
        }

        private static bool IsFinitePositive(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0.0;
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0.0;
        }
    }
}
