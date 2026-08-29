using System;

namespace UnderwaterRobotScene.Visualization.Runtime.Usv
{
    public readonly struct UsvDiagnosticVisualMotionSample
    {
        public UsvDiagnosticVisualMotionSample(
            float heightOffsetMeters,
            float pitchDegrees,
            float rollDegrees)
        {
            HeightOffsetMeters = heightOffsetMeters;
            PitchDegrees = pitchDegrees;
            RollDegrees = rollDegrees;
        }

        public float HeightOffsetMeters { get; }
        public float PitchDegrees { get; }
        public float RollDegrees { get; }
    }

    public static class DeterministicUsvDiagnosticVisualMotion
    {
        private const double TwoPi = Math.PI * 2.0;

        public static bool TryEvaluate(
            double elapsedSeconds,
            float periodSeconds,
            float heaveAmplitudeMeters,
            float pitchAmplitudeDegrees,
            float rollAmplitudeDegrees,
            float activationFadeSeconds,
            out UsvDiagnosticVisualMotionSample sample)
        {
            sample = default;
            if (!IsFinite(elapsedSeconds) ||
                elapsedSeconds < 0.0 ||
                !IsFinite(periodSeconds) ||
                periodSeconds <= 0f ||
                !IsFinite(heaveAmplitudeMeters) ||
                heaveAmplitudeMeters < 0f ||
                !IsFinite(pitchAmplitudeDegrees) ||
                pitchAmplitudeDegrees < 0f ||
                !IsFinite(rollAmplitudeDegrees) ||
                rollAmplitudeDegrees < 0f ||
                !IsFinite(activationFadeSeconds) ||
                activationFadeSeconds <= 0f)
            {
                return false;
            }

            double phase =
                TwoPi * PositiveModulo(elapsedSeconds, periodSeconds) / periodSeconds;
            double fadeU = Math.Min(1.0, elapsedSeconds / activationFadeSeconds);
            double envelope = fadeU * fadeU * fadeU *
                              (fadeU * (fadeU * 6.0 - 15.0) + 10.0);
            double height = heaveAmplitudeMeters * Math.Sin(phase) * envelope;
            double pitch = pitchAmplitudeDegrees *
                           Math.Sin(phase + (TwoPi / 3.0)) * envelope;
            double roll = rollAmplitudeDegrees *
                          Math.Sin(phase + ((TwoPi * 2.0) / 3.0)) * envelope;
            if (!IsFinite(height) || !IsFinite(pitch) || !IsFinite(roll))
            {
                return false;
            }

            sample = new UsvDiagnosticVisualMotionSample(
                (float)height,
                (float)pitch,
                (float)roll);
            return true;
        }

        private static double PositiveModulo(double value, double modulus)
        {
            double result = value % modulus;
            return result < 0.0 ? result + modulus : result;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
