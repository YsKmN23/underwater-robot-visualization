namespace UnderwaterRobotScene.Visualization.State
{
    public readonly struct VehiclePoseState
    {
        public readonly double timestampSeconds;
        public readonly ulong sequenceId;
        public readonly bool valid;
        public readonly double x;
        public readonly double y;
        public readonly double z;
        public readonly double roll;
        public readonly double pitch;
        public readonly double yaw;

        public VehiclePoseState(
            double timestampSeconds,
            ulong sequenceId,
            bool valid,
            double x,
            double y,
            double z,
            double roll,
            double pitch,
            double yaw)
        {
            this.timestampSeconds = timestampSeconds;
            this.sequenceId = sequenceId;
            this.valid = valid;
            this.x = x;
            this.y = y;
            this.z = z;
            this.roll = roll;
            this.pitch = pitch;
            this.yaw = yaw;
        }
    }
}
