namespace UnderwaterRobotScene.Visualization.Buffering
{
    public static class VehiclePoseBufferPolicy
    {
        public const string SequencePolicy = "STRICTLY_INCREASING_NO_WRAP_MVP_ONLY";
        public const string TimestampPolicy = "STRICTLY_INCREASING_NO_EPSILON_MVP_ONLY";
        public const int DefaultCapacity = 8;
        public const int MinimumValidCapacity = 2;

        public static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public static bool IsFloatRepresentable(double value)
        {
            if (!IsFinite(value) || value > float.MaxValue || value < -float.MaxValue) return false;
            float converted = (float)value;
            return !float.IsNaN(converted) && !float.IsInfinity(converted);
        }
    }
}
