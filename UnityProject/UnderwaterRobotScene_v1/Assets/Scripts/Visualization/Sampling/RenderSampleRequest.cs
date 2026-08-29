using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Transforms;

namespace UnderwaterRobotScene.Visualization.Sampling
{
    public readonly struct RenderSampleRequest
    {
        public RenderSampleRequest(
            string sourceId,
            ulong sourceEpoch,
            string vehicleId,
            double targetSourceTimeSeconds,
            double localMonotonicNowSeconds,
            DataSourceStatus sourceStatus,
            CoordinateTransformProfile transformProfile,
            RenderSamplingPolicy policy)
        {
            SourceId = sourceId;
            SourceEpoch = sourceEpoch;
            VehicleId = vehicleId;
            TargetSourceTimeSeconds = targetSourceTimeSeconds;
            LocalMonotonicNowSeconds = localMonotonicNowSeconds;
            SourceStatus = sourceStatus;
            TransformProfile = transformProfile;
            Policy = policy;
        }

        public string SourceId { get; }
        public ulong SourceEpoch { get; }
        public string VehicleId { get; }
        public double TargetSourceTimeSeconds { get; }
        public double LocalMonotonicNowSeconds { get; }
        public DataSourceStatus SourceStatus { get; }
        public CoordinateTransformProfile TransformProfile { get; }
        public RenderSamplingPolicy Policy { get; }
    }
}
