using UnderwaterRobotScene.Visualization.Data;

namespace UnderwaterRobotScene.Visualization.Transforms
{
    public enum AttitudeDirection
    {
        Unknown = 0,
        BodyToWorld = 1,
        WorldToBody = 2
    }

    public readonly struct CoordinateTransformProfile
    {
        public CoordinateTransformProfile(
            string profileId,
            WorldFrame sourceWorldFrame,
            BodyFrame sourceBodyFrame,
            AxisBasis3d worldBasis,
            AxisBasis3d bodyBasis,
            double positionScale,
            AttitudeDirection attitudeDirection,
            Quaterniond modelAlignment)
        {
            ProfileId = profileId;
            SourceWorldFrame = sourceWorldFrame;
            SourceBodyFrame = sourceBodyFrame;
            WorldBasis = worldBasis;
            BodyBasis = bodyBasis;
            PositionScale = positionScale;
            AttitudeDirection = attitudeDirection;
            ModelAlignment = modelAlignment;
        }

        public string ProfileId { get; }
        public WorldFrame SourceWorldFrame { get; }
        public BodyFrame SourceBodyFrame { get; }
        public AxisBasis3d WorldBasis { get; }
        public AxisBasis3d BodyBasis { get; }
        public double PositionScale { get; }
        public AttitudeDirection AttitudeDirection { get; }
        public Quaterniond ModelAlignment { get; }
    }

    public static class CoordinateTransformProfiles
    {
        public static AxisBasis3d NedWorldBasis => new AxisBasis3d(
            new Vector3d(0.0, 0.0, 1.0),
            new Vector3d(1.0, 0.0, 0.0),
            new Vector3d(0.0, -1.0, 0.0));

        public static AxisBasis3d EnuWorldBasis => new AxisBasis3d(
            new Vector3d(1.0, 0.0, 0.0),
            new Vector3d(0.0, 0.0, 1.0),
            new Vector3d(0.0, 1.0, 0.0));

        public static AxisBasis3d FrdBodyBasis => new AxisBasis3d(
            new Vector3d(0.0, 0.0, 1.0),
            new Vector3d(1.0, 0.0, 0.0),
            new Vector3d(0.0, -1.0, 0.0));

        public static AxisBasis3d FluBodyBasis => new AxisBasis3d(
            new Vector3d(0.0, 0.0, 1.0),
            new Vector3d(-1.0, 0.0, 0.0),
            new Vector3d(0.0, 1.0, 0.0));

        public static CoordinateTransformProfile UnityNative(
            string profileId,
            double positionScale,
            AttitudeDirection attitudeDirection,
            Quaterniond modelAlignment)
        {
            return new CoordinateTransformProfile(
                profileId,
                WorldFrame.UnityWorld,
                BodyFrame.UnityBody,
                AxisBasis3d.Identity,
                AxisBasis3d.Identity,
                positionScale,
                attitudeDirection,
                modelAlignment);
        }

        public static CoordinateTransformProfile NedFrdToUnity(
            string profileId,
            double positionScale,
            AttitudeDirection attitudeDirection,
            Quaterniond modelAlignment)
        {
            return new CoordinateTransformProfile(
                profileId,
                WorldFrame.Ned,
                BodyFrame.Frd,
                NedWorldBasis,
                FrdBodyBasis,
                positionScale,
                attitudeDirection,
                modelAlignment);
        }

        public static CoordinateTransformProfile EnuFluToUnity(
            string profileId,
            double positionScale,
            AttitudeDirection attitudeDirection,
            Quaterniond modelAlignment)
        {
            return new CoordinateTransformProfile(
                profileId,
                WorldFrame.Enu,
                BodyFrame.Flu,
                EnuWorldBasis,
                FluBodyBasis,
                positionScale,
                attitudeDirection,
                modelAlignment);
        }
    }
}
