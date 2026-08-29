namespace UnderwaterRobotScene.Visualization.Interpolation
{
    public enum AuvPoseInterpolationResult
    {
        NoSamples = 0,
        HoldOnlySample,
        HoldOldest,
        HoldExactSample,
        Interpolated,
        HoldLatest,
        InvalidEndpoint,
        InvalidTargetTime,
        BufferNotInitialized,
        InvalidBufferState
    }
}
