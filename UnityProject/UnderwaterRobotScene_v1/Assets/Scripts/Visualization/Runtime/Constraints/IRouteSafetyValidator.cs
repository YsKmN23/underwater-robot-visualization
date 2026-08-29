using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Transforms;

namespace UnderwaterRobotScene.Visualization.Runtime.Constraints
{
    public readonly struct RouteSafetyFailureDiagnostic
    {
        public RouteSafetyFailureDiagnostic(
            int segmentIndex,
            double percentage,
            string terrainState)
        {
            SegmentIndex = segmentIndex;
            Percentage = percentage;
            TerrainState = terrainState ?? string.Empty;
        }

        public int SegmentIndex { get; }
        public double Percentage { get; }
        public string TerrainState { get; }
        public bool HasFailure => SegmentIndex > 0;

        public static RouteSafetyFailureDiagnostic None =>
            new RouteSafetyFailureDiagnostic(0, 0.0, string.Empty);
    }

    public interface IRouteSafetyDiagnosticProvider
    {
        RouteSafetyFailureDiagnostic LastRouteSafetyFailure { get; }
    }

    public interface IRouteSafetyValidator
    {
        bool TryValidateRoute(
            ActiveRouteSnapshot candidate,
            in CoordinateTransformProfile transformProfile,
            out string error);
    }
}
