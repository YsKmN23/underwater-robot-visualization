namespace UnderwaterRobotScene.Visualization.Data.LocalTesting
{
    public enum DeterministicVehicleStateGeneratorKind
    {
        Default = 0,
        AuvIntegrationTrajectory = 1,
        RovDiagnosticTrajectory = 2,
        UsvDiagnosticTrajectory = 3
    }

    public interface IDeterministicVehicleStateGenerator
    {
        VehicleState Evaluate(
            LocalTestVehicle vehicle,
            ulong sampleIndex,
            double sourceTimestampSeconds);
    }
}
