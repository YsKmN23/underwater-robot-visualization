namespace UnderwaterRobotScene.Visualization.State
{
    public interface IVehiclePoseSource
    {
        string SourceId { get; }
        PoseSourceStatus Status { get; }
        bool IsRunning { get; }

        void StartSource();
        void StopSource();
        void ResetSource();
        bool TryGetLatestState(out VehiclePoseState state);
    }
}
