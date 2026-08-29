using System;
using UnderwaterRobotScene.Visualization.Data;

namespace UnderwaterRobotScene.Visualization.Transforms
{
    public static class VehiclePoseConverter
    {
        public static bool TryConvert(
            in VehicleState input,
            in CoordinateTransformProfile profile,
            out ConvertedVehiclePose pose,
            out ConversionError error)
        {
            pose = default;

            if (string.IsNullOrWhiteSpace(profile.ProfileId))
            {
                return Fail(
                    ConversionFailureReason.InvalidProfileId,
                    "Profile ID must be explicit.",
                    out error);
            }

            if (!IsSupportedWorldFrame(profile.SourceWorldFrame))
            {
                return Fail(
                    ConversionFailureReason.UnknownWorldFrame,
                    "Profile source world frame is unknown.",
                    out error);
            }

            if (!IsSupportedBodyFrame(profile.SourceBodyFrame))
            {
                return Fail(
                    ConversionFailureReason.UnknownBodyFrame,
                    "Profile source body frame is unknown.",
                    out error);
            }

            if (!profile.WorldBasis.IsValid)
            {
                return Fail(
                    ConversionFailureReason.InvalidWorldBasis,
                    "World basis must contain three finite orthonormal signed axes.",
                    out error);
            }

            if (!profile.BodyBasis.IsValid)
            {
                return Fail(
                    ConversionFailureReason.InvalidBodyBasis,
                    "Body basis must contain three finite orthonormal signed axes.",
                    out error);
            }

            if (profile.WorldBasis.Handedness != profile.BodyBasis.Handedness)
            {
                return Fail(
                    ConversionFailureReason.BasisHandednessMismatch,
                    "World and body basis mappings must have matching handedness.",
                    out error);
            }

            if (!IsFinite(profile.PositionScale) || profile.PositionScale <= 0.0)
            {
                return Fail(
                    ConversionFailureReason.InvalidPositionScale,
                    "Position scale must be finite and greater than zero.",
                    out error);
            }

            if (profile.AttitudeDirection != AttitudeDirection.BodyToWorld &&
                profile.AttitudeDirection != AttitudeDirection.WorldToBody)
            {
                return Fail(
                    ConversionFailureReason.UnknownAttitudeDirection,
                    "Attitude direction must be BodyToWorld or WorldToBody.",
                    out error);
            }

            if (!profile.ModelAlignment.TryNormalize(out Quaterniond modelAlignment))
            {
                return Fail(
                    ConversionFailureReason.InvalidModelAlignment,
                    "Model alignment quaternion is not usable.",
                    out error);
            }

            if (!input.IsStructurallyValid)
            {
                return Fail(
                    ConversionFailureReason.InvalidInputState,
                    "Input VehicleState is not structurally valid.",
                    out error);
            }

            VehicleStateFields required = VehicleStateFields.Position | VehicleStateFields.Orientation;
            if ((input.ValidFields & required) != required)
            {
                return Fail(
                    ConversionFailureReason.MissingPoseFields,
                    "Input must mark Position and Orientation as valid.",
                    out error);
            }

            if (!IsSupportedWorldFrame(input.WorldFrame))
            {
                return Fail(
                    ConversionFailureReason.UnknownWorldFrame,
                    "Input world frame is unknown.",
                    out error);
            }

            if (!IsSupportedBodyFrame(input.BodyFrame))
            {
                return Fail(
                    ConversionFailureReason.UnknownBodyFrame,
                    "Input body frame is unknown.",
                    out error);
            }

            if (input.WorldFrame != profile.SourceWorldFrame || input.BodyFrame != profile.SourceBodyFrame)
            {
                return Fail(
                    ConversionFailureReason.FrameMismatch,
                    "Input frames do not match the explicit transform profile.",
                    out error);
            }

            if (!input.Orientation.TryNormalize(out Quaterniond sourceOrientation))
            {
                return Fail(
                    ConversionFailureReason.InvalidOrientation,
                    "Input orientation quaternion is not usable.",
                    out error);
            }

            if (profile.AttitudeDirection == AttitudeDirection.WorldToBody)
            {
                sourceOrientation = QuaternionMath3d.Conjugate(sourceOrientation);
            }

            Vector3d position = VectorMath3d.Scale(
                profile.WorldBasis.Transform(input.Position),
                profile.PositionScale);
            if (!position.IsFinite)
            {
                return Fail(
                    ConversionFailureReason.NonFiniteResult,
                    "Position conversion produced a non-finite result.",
                    out error);
            }

            Matrix3d targetRotation = Matrix3d.Multiply(
                Matrix3d.Multiply(
                    profile.WorldBasis.ToMatrix(),
                    QuaternionMath3d.ToMatrix(sourceOrientation)),
                profile.BodyBasis.ToMatrix().Transpose());
            if (!QuaternionMath3d.TryFromMatrix(targetRotation, out Quaterniond targetOrientation))
            {
                return Fail(
                    ConversionFailureReason.NonFiniteResult,
                    "Attitude basis conversion did not produce a proper rotation.",
                    out error);
            }

            Quaterniond alignedOrientation = QuaternionMath3d.Multiply(targetOrientation, modelAlignment);
            if (!alignedOrientation.TryNormalize(out alignedOrientation))
            {
                return Fail(
                    ConversionFailureReason.NonFiniteResult,
                    "Model alignment produced an invalid orientation.",
                    out error);
            }

            pose = new ConvertedVehiclePose(
                profile.ProfileId,
                input.VehicleId,
                input.SourceTimestampSeconds,
                input.SequenceNumber,
                position,
                alignedOrientation);
            error = ConversionError.None;
            return true;
        }

        private static bool Fail(
            ConversionFailureReason reason,
            string message,
            out ConversionError error)
        {
            error = new ConversionError(reason, message);
            return false;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsSupportedWorldFrame(WorldFrame frame)
        {
            return frame == WorldFrame.Ned ||
                   frame == WorldFrame.Enu ||
                   frame == WorldFrame.UnityWorld;
        }

        private static bool IsSupportedBodyFrame(BodyFrame frame)
        {
            return frame == BodyFrame.Frd ||
                   frame == BodyFrame.Flu ||
                   frame == BodyFrame.UnityBody;
        }
    }
}
