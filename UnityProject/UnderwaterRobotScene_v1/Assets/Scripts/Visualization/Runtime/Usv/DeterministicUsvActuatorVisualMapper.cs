using System;

namespace UnderwaterRobotScene.Visualization.Runtime.Usv
{
    public readonly struct UsvActuatorVisualConfig
    {
        public readonly float SpeedDeadbandMetersPerSecond;
        public readonly float SpeedFullScaleMetersPerSecond;
        public readonly float YawDeadbandDegreesPerSecond;
        public readonly float YawFullScaleDegreesPerSecond;
        public readonly float MinVisibleRpm;
        public readonly float CruiseRpm;
        public readonly float MaxVisualRpm;
        public readonly float MaxDifferentialRpm;
        public readonly float LowSpeedOffMetersPerSecond;
        public readonly float LowSpeedFullMetersPerSecond;
        public readonly float MaxVisualRudderDegrees;

        public UsvActuatorVisualConfig(
            float speedDeadbandMetersPerSecond,
            float speedFullScaleMetersPerSecond,
            float yawDeadbandDegreesPerSecond,
            float yawFullScaleDegreesPerSecond,
            float minVisibleRpm,
            float cruiseRpm,
            float maxVisualRpm,
            float maxDifferentialRpm,
            float lowSpeedOffMetersPerSecond,
            float lowSpeedFullMetersPerSecond,
            float maxVisualRudderDegrees)
        {
            SpeedDeadbandMetersPerSecond = speedDeadbandMetersPerSecond;
            SpeedFullScaleMetersPerSecond = speedFullScaleMetersPerSecond;
            YawDeadbandDegreesPerSecond = yawDeadbandDegreesPerSecond;
            YawFullScaleDegreesPerSecond = yawFullScaleDegreesPerSecond;
            MinVisibleRpm = minVisibleRpm;
            CruiseRpm = cruiseRpm;
            MaxVisualRpm = maxVisualRpm;
            MaxDifferentialRpm = maxDifferentialRpm;
            LowSpeedOffMetersPerSecond = lowSpeedOffMetersPerSecond;
            LowSpeedFullMetersPerSecond = lowSpeedFullMetersPerSecond;
            MaxVisualRudderDegrees = maxVisualRudderDegrees;
        }
    }

    public readonly struct UsvActuatorVisualTargets
    {
        public readonly float PortRpm;
        public readonly float StarboardRpm;
        public readonly float RudderDegrees;

        public UsvActuatorVisualTargets(
            float portRpm,
            float starboardRpm,
            float rudderDegrees)
        {
            PortRpm = portRpm;
            StarboardRpm = starboardRpm;
            RudderDegrees = rudderDegrees;
        }
    }

    public static class DeterministicUsvActuatorVisualMapper
    {
        public static bool TryMap(
            float forwardSpeedMetersPerSecond,
            float yawRateDegreesPerSecond,
            in UsvActuatorVisualConfig config,
            out UsvActuatorVisualTargets targets)
        {
            targets = default;
            if (!IsFinite(forwardSpeedMetersPerSecond) ||
                !IsFinite(yawRateDegreesPerSecond) ||
                !IsValid(config))
            {
                return false;
            }

            float speed = Math.Abs(forwardSpeedMetersPerSecond);
            float speedActivity = Activity(
                speed,
                config.SpeedDeadbandMetersPerSecond,
                config.SpeedFullScaleMetersPerSecond);
            float yawActivityMagnitude = Activity(
                Math.Abs(yawRateDegreesPerSecond),
                config.YawDeadbandDegreesPerSecond,
                config.YawFullScaleDegreesPerSecond);
            float signedYawActivity = Math.Sign(yawRateDegreesPerSecond) *
                                      yawActivityMagnitude;
            float speedGate = Activity(
                speed,
                config.LowSpeedOffMetersPerSecond,
                config.LowSpeedFullMetersPerSecond);

            float baseRpm = speedActivity == 0f
                ? 0f
                : Lerp(config.MinVisibleRpm, config.CruiseRpm, speedActivity);
            float differential = config.MaxDifferentialRpm *
                                 signedYawActivity *
                                 speedGate;
            float portRpm = Clamp(
                baseRpm + differential,
                0f,
                config.MaxVisualRpm);
            float starboardRpm = Clamp(
                baseRpm - differential,
                0f,
                config.MaxVisualRpm);
            float rudderDegrees = config.MaxVisualRudderDegrees *
                                  signedYawActivity *
                                  speedGate;
            targets = new UsvActuatorVisualTargets(
                portRpm,
                starboardRpm,
                rudderDegrees);
            return true;
        }

        private static bool IsValid(in UsvActuatorVisualConfig config)
        {
            return IsFinite(config.SpeedDeadbandMetersPerSecond) &&
                   IsFinite(config.SpeedFullScaleMetersPerSecond) &&
                   IsFinite(config.YawDeadbandDegreesPerSecond) &&
                   IsFinite(config.YawFullScaleDegreesPerSecond) &&
                   IsFinite(config.MinVisibleRpm) &&
                   IsFinite(config.CruiseRpm) &&
                   IsFinite(config.MaxVisualRpm) &&
                   IsFinite(config.MaxDifferentialRpm) &&
                   IsFinite(config.LowSpeedOffMetersPerSecond) &&
                   IsFinite(config.LowSpeedFullMetersPerSecond) &&
                   IsFinite(config.MaxVisualRudderDegrees) &&
                   config.SpeedDeadbandMetersPerSecond >= 0f &&
                   config.SpeedFullScaleMetersPerSecond >
                       config.SpeedDeadbandMetersPerSecond &&
                   config.YawDeadbandDegreesPerSecond >= 0f &&
                   config.YawFullScaleDegreesPerSecond >
                       config.YawDeadbandDegreesPerSecond &&
                   config.MinVisibleRpm >= 0f &&
                   config.CruiseRpm >= config.MinVisibleRpm &&
                   config.MaxVisualRpm >= config.CruiseRpm &&
                   config.MaxDifferentialRpm >= 0f &&
                   config.LowSpeedOffMetersPerSecond >= 0f &&
                   config.LowSpeedFullMetersPerSecond >
                       config.LowSpeedOffMetersPerSecond &&
                   config.MaxVisualRudderDegrees >= 0f;
        }

        private static float Activity(float value, float off, float full)
        {
            if (value <= off)
            {
                return 0f;
            }
            if (value >= full)
            {
                return 1f;
            }
            float t = (value - off) / (full - off);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        private static float Lerp(float from, float to, float t)
        {
            return from + (to - from) * t;
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return value < minimum
                ? minimum
                : value > maximum
                    ? maximum
                    : value;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
