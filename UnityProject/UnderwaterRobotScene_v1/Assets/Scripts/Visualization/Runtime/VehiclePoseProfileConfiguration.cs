using System;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Transforms;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime
{
    public enum CoordinateProfilePreset
    {
        UnityNative = 0,
        NedFrdToUnity = 1,
        EnuFluToUnity = 2
    }

    public enum SignedSemanticAxis
    {
        PositiveX = 0,
        NegativeX = 1,
        PositiveY = 2,
        NegativeY = 3,
        PositiveZ = 4,
        NegativeZ = 5
    }

    [DisallowMultipleComponent]
    public sealed class VehiclePoseProfileConfiguration : MonoBehaviour
    {
        [Header("Explicit N3 profile")]
        [SerializeField] private string profileId = "UNCONFIGURED_PROFILE";
        [SerializeField] private CoordinateProfilePreset preset = CoordinateProfilePreset.UnityNative;
        [SerializeField] private float positionScale = 1f;
        [SerializeField] private AttitudeDirection attitudeDirection = AttitudeDirection.BodyToWorld;

        [Header("Model semantic axes (documentation and validation)")]
        [SerializeField] private SignedSemanticAxis modelRight = SignedSemanticAxis.PositiveX;
        [SerializeField] private SignedSemanticAxis modelUp = SignedSemanticAxis.PositiveY;
        [SerializeField] private SignedSemanticAxis modelForward = SignedSemanticAxis.PositiveZ;

        [Header("q_output = q_target * q_modelAlignment")]
        [SerializeField] private Vector3 modelAlignmentEulerDegrees = Vector3.zero;

        public string ProfileId => profileId;
        public CoordinateProfilePreset Preset => preset;
        public float PositionScale => positionScale;
        public AttitudeDirection AttitudeDirection => attitudeDirection;
        public WorldFrame SourceWorldFrame => SourceFrames(preset).world;
        public BodyFrame SourceBodyFrame => SourceFrames(preset).body;
        public SignedSemanticAxis ModelRight => modelRight;
        public SignedSemanticAxis ModelUp => modelUp;
        public SignedSemanticAxis ModelForward => modelForward;
        public Vector3 ModelAlignmentEulerDegrees => modelAlignmentEulerDegrees;

        public void Configure(
            string configuredProfileId,
            CoordinateProfilePreset configuredPreset,
            float configuredPositionScale,
            AttitudeDirection configuredAttitudeDirection,
            SignedSemanticAxis configuredModelRight,
            SignedSemanticAxis configuredModelUp,
            SignedSemanticAxis configuredModelForward,
            Vector3 configuredModelAlignmentEulerDegrees)
        {
            profileId = configuredProfileId;
            preset = configuredPreset;
            positionScale = configuredPositionScale;
            attitudeDirection = configuredAttitudeDirection;
            modelRight = configuredModelRight;
            modelUp = configuredModelUp;
            modelForward = configuredModelForward;
            modelAlignmentEulerDegrees = configuredModelAlignmentEulerDegrees;

            if (!TryBuildProfile(out _, out string error))
            {
                throw new ArgumentException(error, nameof(configuredProfileId));
            }
        }

        public bool TryBuildProfile(out CoordinateTransformProfile profile, out string error)
        {
            profile = default;
            if (string.IsNullOrWhiteSpace(profileId))
            {
                error = "Profile ID must be explicit.";
                return false;
            }

            if (!IsFinite(positionScale) || positionScale <= 0f)
            {
                error = "Position scale must be finite and positive.";
                return false;
            }

            var semanticBasis = new AxisBasis3d(
                ToVector(modelRight),
                ToVector(modelUp),
                ToVector(modelForward));
            if (!semanticBasis.IsValid || semanticBasis.Handedness != 1)
            {
                error = "Model semantic Right/Up/Forward axes must form a proper right-handed basis.";
                return false;
            }

            Quaternion unityAlignment = Quaternion.Euler(modelAlignmentEulerDegrees);
            var alignment = new Quaterniond(
                unityAlignment.x,
                unityAlignment.y,
                unityAlignment.z,
                unityAlignment.w);

            switch (preset)
            {
                case CoordinateProfilePreset.UnityNative:
                    profile = CoordinateTransformProfiles.UnityNative(
                        profileId,
                        positionScale,
                        attitudeDirection,
                        alignment);
                    break;
                case CoordinateProfilePreset.NedFrdToUnity:
                    profile = CoordinateTransformProfiles.NedFrdToUnity(
                        profileId,
                        positionScale,
                        attitudeDirection,
                        alignment);
                    break;
                case CoordinateProfilePreset.EnuFluToUnity:
                    profile = CoordinateTransformProfiles.EnuFluToUnity(
                        profileId,
                        positionScale,
                        attitudeDirection,
                        alignment);
                    break;
                default:
                    error = "Coordinate profile preset is unknown.";
                    return false;
            }

            error = string.Empty;
            return true;
        }

        private static Vector3d ToVector(SignedSemanticAxis axis)
        {
            switch (axis)
            {
                case SignedSemanticAxis.PositiveX: return new Vector3d(1.0, 0.0, 0.0);
                case SignedSemanticAxis.NegativeX: return new Vector3d(-1.0, 0.0, 0.0);
                case SignedSemanticAxis.PositiveY: return new Vector3d(0.0, 1.0, 0.0);
                case SignedSemanticAxis.NegativeY: return new Vector3d(0.0, -1.0, 0.0);
                case SignedSemanticAxis.PositiveZ: return new Vector3d(0.0, 0.0, 1.0);
                case SignedSemanticAxis.NegativeZ: return new Vector3d(0.0, 0.0, -1.0);
                default: throw new ArgumentOutOfRangeException(nameof(axis));
            }
        }

        private static (WorldFrame world, BodyFrame body) SourceFrames(
            CoordinateProfilePreset configuredPreset)
        {
            switch (configuredPreset)
            {
                case CoordinateProfilePreset.UnityNative:
                    return (WorldFrame.UnityWorld, BodyFrame.UnityBody);
                case CoordinateProfilePreset.NedFrdToUnity:
                    return (WorldFrame.Ned, BodyFrame.Frd);
                case CoordinateProfilePreset.EnuFluToUnity:
                    return (WorldFrame.Enu, BodyFrame.Flu);
                default:
                    return (WorldFrame.Unknown, BodyFrame.Unknown);
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
