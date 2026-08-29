namespace UnderwaterRobotScene.Visualization.Buffering
{
    public enum VehiclePoseBufferPushResult
    {
        Accepted = 0,
        InvalidState,
        NonFiniteValue,
        FloatRangeOverflow,
        DuplicateSequence,
        OutOfOrderSequence,
        DuplicateTimestamp,
        TimestampRegression,
        InvalidReceiveTime,
        BufferNotInitialized
    }
}
