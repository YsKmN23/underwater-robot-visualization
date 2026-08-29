using System;
using System.Collections.Generic;
using System.IO;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Transforms;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnderwaterRobotScene.EditorTools
{
    public static class CoordinateAttitudeN3Verifier
    {
        private const double Tolerance = 1e-9;

        private static readonly string[] ProductionAssetPaths =
        {
            "Assets/Scripts/Visualization/Data/VehicleState.cs",
            "Assets/Scripts/Visualization/Transforms/AxisBasis3d.cs",
            "Assets/Scripts/Visualization/Transforms/QuaternionMath3d.cs",
            "Assets/Scripts/Visualization/Transforms/CoordinateTransformProfile.cs",
            "Assets/Scripts/Visualization/Transforms/ConvertedVehiclePose.cs",
            "Assets/Scripts/Visualization/Transforms/VehiclePoseConverter.cs",
            "Assets/Scripts/Visualization/Transforms/QuaternionContinuity.cs"
        };

#if UNITY_EDITOR
        [MenuItem("Tools/Underwater Demo/Verify Coordinate and Attitude Core N3")]
        public static void RunFromMenu()
        {
            int exitCode = RunVerification(Console.WriteLine);
            if (exitCode != 0)
            {
                throw new InvalidOperationException("Coordinate and attitude core N3 verification failed.");
            }
        }
#endif

        public static int Main(string[] args)
        {
            return RunVerification(Console.WriteLine);
        }

        public static int RunVerification(Action<string> writeLine)
        {
            if (writeLine == null) throw new ArgumentNullException(nameof(writeLine));

            var tests = new[]
            {
                new VerificationCase("Identity profile and explicit position scale", VerifyIdentity),
                new VerificationCase("NED basis vectors and combined position", VerifyNedPosition),
                new VerificationCase("ENU basis vectors and combined position", VerifyEnuPosition),
                new VerificationCase("Attitude basis conversion rotates actual directions", VerifyAttitudeBasis),
                new VerificationCase("Body-to-world and world-to-body agree", VerifyAttitudeDirection),
                new VerificationCase("Model alignment is a right-side local compensation", VerifyModelAlignment),
                new VerificationCase("Quaternion safety and normalization", VerifyQuaternionSafety),
                new VerificationCase("Quaternion hemisphere and 359-to-1 continuity", VerifyContinuity),
                new VerificationCase("Invalid and unsupported configurations are rejected", VerifyInvalidConfigurations),
                new VerificationCase("N3 public core dependency boundary", VerifyDependencyBoundary)
            };

            int passed = 0;
            foreach (VerificationCase test in tests)
            {
                try
                {
                    test.Body();
                    passed++;
                    writeLine("PASS | " + test.Name);
                }
                catch (Exception exception)
                {
                    writeLine("FAIL | " + test.Name + " | " + exception.GetType().Name + ": " + exception.Message);
                }
            }

            writeLine("N3 verifier summary: " + passed + "/" + tests.Length + " passed.");
            return passed == tests.Length ? 0 : 1;
        }

        private static void VerifyIdentity()
        {
            CoordinateTransformProfile profile = CoordinateTransformProfiles.UnityNative(
                "UNITY_NATIVE_X2",
                2.0,
                AttitudeDirection.BodyToWorld,
                Quaterniond.Identity);

            Quaterniond inputOrientation = QuaternionMath3d.FromAxisAngleRadians(
                new Vector3d(0.0, 1.0, 0.0),
                Math.PI / 2.0);
            VehicleState input = CreateState(
                new Vector3d(1.0, -2.0, 3.0),
                inputOrientation,
                WorldFrame.UnityWorld,
                BodyFrame.UnityBody);

            RequireConvert(input, profile, out ConvertedVehiclePose pose);
            RequireVector(pose.Position, new Vector3d(2.0, -4.0, 6.0), "Unity-native scaled position");
            RequireQuaternionEquivalent(pose.Orientation, inputOrientation, "Unity-native orientation");
            Require(pose.ProfileId == "UNITY_NATIVE_X2" && pose.VehicleId == input.VehicleId,
                "Converted identity metadata was not preserved.");
            Require(pose.SourceTimestampSeconds == input.SourceTimestampSeconds &&
                    pose.SequenceNumber == input.SequenceNumber, "Converted time identity was not preserved.");

            VehicleState zero = CreateState(
                Vector3d.Zero,
                Quaterniond.Identity,
                WorldFrame.UnityWorld,
                BodyFrame.UnityBody);
            RequireConvert(zero, profile, out pose);
            RequireVector(pose.Position, Vector3d.Zero, "Unity-native zero");
            RequireQuaternionEquivalent(pose.Orientation, Quaterniond.Identity, "Unity-native identity quaternion");
        }

        private static void VerifyNedPosition()
        {
            CoordinateTransformProfile profile = CoordinateTransformProfiles.NedFrdToUnity(
                "NED_FRD_X2",
                2.0,
                AttitudeDirection.BodyToWorld,
                Quaterniond.Identity);

            RequirePosition(profile, new Vector3d(1.0, 0.0, 0.0), new Vector3d(0.0, 0.0, 2.0), "NED North");
            RequirePosition(profile, new Vector3d(0.0, 1.0, 0.0), new Vector3d(2.0, 0.0, 0.0), "NED East");
            RequirePosition(profile, new Vector3d(0.0, 0.0, 1.0), new Vector3d(0.0, -2.0, 0.0), "NED Down");
            RequirePosition(profile, new Vector3d(2.0, 3.0, 4.0), new Vector3d(6.0, -8.0, 4.0), "NED combined");
        }

        private static void VerifyEnuPosition()
        {
            CoordinateTransformProfile profile = CoordinateTransformProfiles.EnuFluToUnity(
                "ENU_FLU_HALF",
                0.5,
                AttitudeDirection.BodyToWorld,
                Quaterniond.Identity);

            RequirePosition(profile, new Vector3d(1.0, 0.0, 0.0), new Vector3d(0.5, 0.0, 0.0), "ENU East");
            RequirePosition(profile, new Vector3d(0.0, 1.0, 0.0), new Vector3d(0.0, 0.0, 0.5), "ENU North");
            RequirePosition(profile, new Vector3d(0.0, 0.0, 1.0), new Vector3d(0.0, 0.5, 0.0), "ENU Up");
            RequirePosition(profile, new Vector3d(2.0, 4.0, 6.0), new Vector3d(1.0, 3.0, 2.0), "ENU combined");
        }

        private static void VerifyAttitudeBasis()
        {
            CoordinateTransformProfile profile = CoordinateTransformProfiles.NedFrdToUnity(
                "NED_ATTITUDE",
                1.0,
                AttitudeDirection.BodyToWorld,
                Quaterniond.Identity);

            RequireOrientation(profile, Quaterniond.Identity, Quaterniond.Identity, "NED identity");

            Quaterniond sourceUp = QuaternionMath3d.FromAxisAngleRadians(
                new Vector3d(0.0, 0.0, -1.0),
                Math.PI / 2.0);
            RequireRotatedDirection(
                profile,
                sourceUp,
                new Vector3d(0.0, 0.0, 1.0),
                new Vector3d(-1.0, 0.0, 0.0),
                "NED rotation around source Up");

            Quaterniond sourceForward = QuaternionMath3d.FromAxisAngleRadians(
                new Vector3d(1.0, 0.0, 0.0),
                Math.PI / 2.0);
            RequireRotatedDirection(
                profile,
                sourceForward,
                new Vector3d(1.0, 0.0, 0.0),
                new Vector3d(0.0, -1.0, 0.0),
                "NED rotation around source Forward");

            Quaterniond sourceRight = QuaternionMath3d.FromAxisAngleRadians(
                new Vector3d(0.0, 1.0, 0.0),
                Math.PI / 2.0);
            RequireRotatedDirection(
                profile,
                sourceRight,
                new Vector3d(0.0, 1.0, 0.0),
                new Vector3d(0.0, 0.0, -1.0),
                "NED rotation around source Right");

            Quaterniond combined = QuaternionMath3d.Multiply(sourceUp, sourceForward);
            VehicleState combinedState = CreateState(
                Vector3d.Zero,
                combined,
                WorldFrame.Ned,
                BodyFrame.Frd);
            RequireConvert(combinedState, profile, out ConvertedVehiclePose combinedPose);
            RequireVector(
                QuaternionMath3d.Rotate(combinedPose.Orientation, new Vector3d(1.0, 0.0, 0.0)),
                new Vector3d(0.0, -1.0, 0.0),
                "NED combined target Right");
            RequireVector(
                QuaternionMath3d.Rotate(combinedPose.Orientation, new Vector3d(0.0, 1.0, 0.0)),
                new Vector3d(0.0, 0.0, 1.0),
                "NED combined target Up");
            RequireVector(
                QuaternionMath3d.Rotate(combinedPose.Orientation, new Vector3d(0.0, 0.0, 1.0)),
                new Vector3d(-1.0, 0.0, 0.0),
                "NED combined target Forward");

            CoordinateTransformProfile enuProfile = CoordinateTransformProfiles.EnuFluToUnity(
                "ENU_ATTITUDE",
                1.0,
                AttitudeDirection.BodyToWorld,
                Quaterniond.Identity);
            Quaterniond expectedEnuIdentity = QuaternionMath3d.FromAxisAngleRadians(
                new Vector3d(0.0, 1.0, 0.0),
                Math.PI / 2.0);
            RequireOrientation(
                enuProfile,
                Quaterniond.Identity,
                expectedEnuIdentity,
                "ENU/FLU identity attitude basis");
        }

        private static void VerifyAttitudeDirection()
        {
            Quaterniond bodyToWorld = QuaternionMath3d.FromAxisAngleRadians(
                new Vector3d(1.0, 1.0, 0.0),
                0.73);
            Quaterniond worldToBody = QuaternionMath3d.Conjugate(bodyToWorld);

            CoordinateTransformProfile bodyToWorldProfile = CoordinateTransformProfiles.NedFrdToUnity(
                "NED_B2W",
                1.0,
                AttitudeDirection.BodyToWorld,
                Quaterniond.Identity);
            CoordinateTransformProfile worldToBodyProfile = CoordinateTransformProfiles.NedFrdToUnity(
                "NED_W2B",
                1.0,
                AttitudeDirection.WorldToBody,
                Quaterniond.Identity);

            VehicleState bodyToWorldState = CreateState(
                Vector3d.Zero,
                bodyToWorld,
                WorldFrame.Ned,
                BodyFrame.Frd);
            VehicleState worldToBodyState = CreateState(
                Vector3d.Zero,
                worldToBody,
                WorldFrame.Ned,
                BodyFrame.Frd);

            RequireConvert(bodyToWorldState, bodyToWorldProfile, out ConvertedVehiclePose fromBodyToWorld);
            RequireConvert(worldToBodyState, worldToBodyProfile, out ConvertedVehiclePose fromWorldToBody);
            RequireQuaternionEquivalent(
                fromBodyToWorld.Orientation,
                fromWorldToBody.Orientation,
                "Equivalent body/world direction semantics");
        }

        private static void VerifyModelAlignment()
        {
            Quaterniond worldAttitude = QuaternionMath3d.FromAxisAngleRadians(
                new Vector3d(1.0, 0.0, 0.0),
                Math.PI / 2.0);
            Quaterniond modelAlignment = QuaternionMath3d.FromAxisAngleRadians(
                new Vector3d(0.0, 1.0, 0.0),
                Math.PI / 2.0);

            CoordinateTransformProfile identityAlignment = CoordinateTransformProfiles.UnityNative(
                "IDENTITY_ALIGNMENT",
                1.0,
                AttitudeDirection.BodyToWorld,
                Quaterniond.Identity);
            CoordinateTransformProfile fixedAlignment = CoordinateTransformProfiles.UnityNative(
                "FIXED_ALIGNMENT",
                1.0,
                AttitudeDirection.BodyToWorld,
                modelAlignment);
            VehicleState state = CreateState(
                Vector3d.Zero,
                worldAttitude,
                WorldFrame.UnityWorld,
                BodyFrame.UnityBody);

            RequireConvert(state, identityAlignment, out ConvertedVehiclePose identityPose);
            RequireQuaternionEquivalent(identityPose.Orientation, worldAttitude, "Identity model alignment");

            RequireConvert(state, fixedAlignment, out ConvertedVehiclePose alignedPose);
            Vector3d modelLocalDirection = new Vector3d(0.0, 0.0, 1.0);
            Vector3d expected = QuaternionMath3d.Rotate(
                worldAttitude,
                QuaternionMath3d.Rotate(modelAlignment, modelLocalDirection));
            Vector3d actual = QuaternionMath3d.Rotate(alignedPose.Orientation, modelLocalDirection);
            RequireVector(actual, expected, "Right-side model alignment order");

            Quaterniond changedWorldAttitude = QuaternionMath3d.FromAxisAngleRadians(
                new Vector3d(0.0, 0.0, 1.0),
                Math.PI / 3.0);
            VehicleState changedState = CreateState(
                Vector3d.Zero,
                changedWorldAttitude,
                WorldFrame.UnityWorld,
                BodyFrame.UnityBody);
            RequireConvert(changedState, fixedAlignment, out ConvertedVehiclePose changedPose);
            expected = QuaternionMath3d.Rotate(
                changedWorldAttitude,
                QuaternionMath3d.Rotate(modelAlignment, modelLocalDirection));
            RequireVector(
                QuaternionMath3d.Rotate(changedPose.Orientation, modelLocalDirection),
                expected,
                "Model alignment remains local after world attitude changes");
        }

        private static void VerifyQuaternionSafety()
        {
            CoordinateTransformProfile profile = CoordinateTransformProfiles.UnityNative(
                "QUATERNION_SAFETY",
                1.0,
                AttitudeDirection.BodyToWorld,
                new Quaterniond(0.0, 0.0, 0.0, 2.0));
            VehicleState normalizedByState = CreateState(
                Vector3d.Zero,
                new Quaterniond(0.0, 0.0, 0.0, 4.0),
                WorldFrame.UnityWorld,
                BodyFrame.UnityBody);
            RequireConvert(normalizedByState, profile, out ConvertedVehiclePose pose);
            RequireNear(pose.Orientation.MagnitudeSquared, 1.0, "Converted quaternion unit length");

            Quaterniond orientation = QuaternionMath3d.FromAxisAngleRadians(
                new Vector3d(0.0, 1.0, 0.0),
                0.4);
            Quaterniond negated = QuaternionMath3d.Negate(orientation);
            Require(QuaternionMath3d.RepresentsSameRotation(orientation, negated, Tolerance),
                "q and -q were not recognized as the same rotation.");

            RequireConversionFailure(
                CreateStateWithRawInvalidOrientation(new Quaterniond(double.NaN, 0.0, 0.0, 1.0)),
                profile,
                ConversionFailureReason.InvalidInputState,
                "NaN quaternion");
            RequireConversionFailure(
                CreateStateWithRawInvalidOrientation(new Quaterniond(double.PositiveInfinity, 0.0, 0.0, 1.0)),
                profile,
                ConversionFailureReason.InvalidInputState,
                "Infinity quaternion");
            RequireConversionFailure(
                CreateStateWithRawInvalidOrientation(new Quaterniond(1e-9, 0.0, 0.0, 0.0)),
                profile,
                ConversionFailureReason.InvalidInputState,
                "Near-zero quaternion");
        }

        private static void VerifyContinuity()
        {
            Quaterniond reference = QuaternionMath3d.FromAxisAngleRadians(
                new Vector3d(0.0, 1.0, 0.0),
                DegreesToRadians(359.0));
            Quaterniond candidate = QuaternionMath3d.FromAxisAngleRadians(
                new Vector3d(0.0, 1.0, 0.0),
                DegreesToRadians(1.0));

            Require(QuaternionContinuity.TryAlignHemisphere(
                    reference,
                    candidate,
                    out Quaterniond aligned,
                    out ConversionError error),
                "Hemisphere alignment failed: " + error.Message);
            Require(QuaternionMath3d.Dot(reference, aligned) >= 0.0,
                "Aligned quaternion remained in the opposite hemisphere.");
            RequireQuaternionEquivalent(aligned, candidate, "Hemisphere alignment rotation preservation");

            double shortestAngle = QuaternionContinuity.ShortestAngleRadians(reference, candidate);
            RequireNear(shortestAngle, DegreesToRadians(2.0), "359-to-1 shortest attitude angle", 1e-8);

            Quaterniond oppositeSign = QuaternionMath3d.Negate(reference);
            Require(QuaternionContinuity.TryAlignHemisphere(
                    reference,
                    oppositeSign,
                    out Quaterniond sameHemisphere,
                    out error),
                "Opposite-sign alignment failed: " + error.Message);
            Require(QuaternionMath3d.Dot(reference, sameHemisphere) > 1.0 - Tolerance,
                "Opposite-sign quaternion did not align to the reference.");
            RequireQuaternionEquivalent(sameHemisphere, oppositeSign, "Opposite-sign rotation preservation");
        }

        private static void VerifyInvalidConfigurations()
        {
            VehicleState unityState = CreateState(
                Vector3d.Zero,
                Quaterniond.Identity,
                WorldFrame.UnityWorld,
                BodyFrame.UnityBody);

            var unknownFrame = new CoordinateTransformProfile(
                "UNKNOWN_FRAME",
                WorldFrame.Unknown,
                BodyFrame.UnityBody,
                AxisBasis3d.Identity,
                AxisBasis3d.Identity,
                1.0,
                AttitudeDirection.BodyToWorld,
                Quaterniond.Identity);
            RequireConversionFailure(unityState, unknownFrame, ConversionFailureReason.UnknownWorldFrame, "Unknown frame");

            var unsupportedWorldFrame = new CoordinateTransformProfile(
                "UNSUPPORTED_WORLD_FRAME",
                (WorldFrame)99,
                BodyFrame.UnityBody,
                AxisBasis3d.Identity,
                AxisBasis3d.Identity,
                1.0,
                AttitudeDirection.BodyToWorld,
                Quaterniond.Identity);
            VehicleState unsupportedWorldState = CreateState(
                Vector3d.Zero,
                Quaterniond.Identity,
                (WorldFrame)99,
                BodyFrame.UnityBody);
            RequireConversionFailure(
                unsupportedWorldState,
                unsupportedWorldFrame,
                ConversionFailureReason.UnknownWorldFrame,
                "Unsupported world frame value");

            var unsupportedBodyFrame = new CoordinateTransformProfile(
                "UNSUPPORTED_BODY_FRAME",
                WorldFrame.UnityWorld,
                (BodyFrame)99,
                AxisBasis3d.Identity,
                AxisBasis3d.Identity,
                1.0,
                AttitudeDirection.BodyToWorld,
                Quaterniond.Identity);
            VehicleState unsupportedBodyState = CreateState(
                Vector3d.Zero,
                Quaterniond.Identity,
                WorldFrame.UnityWorld,
                (BodyFrame)99);
            RequireConversionFailure(
                unsupportedBodyState,
                unsupportedBodyFrame,
                ConversionFailureReason.UnknownBodyFrame,
                "Unsupported body frame value");

            var incompleteBasis = new CoordinateTransformProfile(
                "INCOMPLETE",
                WorldFrame.UnityWorld,
                BodyFrame.UnityBody,
                default,
                AxisBasis3d.Identity,
                1.0,
                AttitudeDirection.BodyToWorld,
                Quaterniond.Identity);
            RequireConversionFailure(unityState, incompleteBasis, ConversionFailureReason.InvalidWorldBasis, "Incomplete basis");

            var duplicateAxis = new AxisBasis3d(
                new Vector3d(1.0, 0.0, 0.0),
                new Vector3d(1.0, 0.0, 0.0),
                new Vector3d(0.0, 0.0, 1.0));
            var duplicateProfile = new CoordinateTransformProfile(
                "DUPLICATE",
                WorldFrame.UnityWorld,
                BodyFrame.UnityBody,
                duplicateAxis,
                AxisBasis3d.Identity,
                1.0,
                AttitudeDirection.BodyToWorld,
                Quaterniond.Identity);
            RequireConversionFailure(unityState, duplicateProfile, ConversionFailureReason.InvalidWorldBasis, "Duplicate axis");

            var nonOrthogonal = new AxisBasis3d(
                new Vector3d(1.0, 0.0, 0.0),
                new Vector3d(Math.Sqrt(0.5), Math.Sqrt(0.5), 0.0),
                new Vector3d(0.0, 0.0, 1.0));
            var nonOrthogonalProfile = new CoordinateTransformProfile(
                "NON_ORTHOGONAL",
                WorldFrame.UnityWorld,
                BodyFrame.UnityBody,
                nonOrthogonal,
                AxisBasis3d.Identity,
                1.0,
                AttitudeDirection.BodyToWorld,
                Quaterniond.Identity);
            RequireConversionFailure(
                unityState,
                nonOrthogonalProfile,
                ConversionFailureReason.InvalidWorldBasis,
                "Non-orthogonal basis");

            foreach (double invalidScale in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity })
            {
                CoordinateTransformProfile invalidScaleProfile = CoordinateTransformProfiles.UnityNative(
                    "BAD_SCALE",
                    invalidScale,
                    AttitudeDirection.BodyToWorld,
                    Quaterniond.Identity);
                RequireConversionFailure(
                    unityState,
                    invalidScaleProfile,
                    ConversionFailureReason.InvalidPositionScale,
                    "Invalid scale " + invalidScale);
            }

            CoordinateTransformProfile unknownDirection = CoordinateTransformProfiles.UnityNative(
                "UNKNOWN_DIRECTION",
                1.0,
                (AttitudeDirection)99,
                Quaterniond.Identity);
            RequireConversionFailure(
                unityState,
                unknownDirection,
                ConversionFailureReason.UnknownAttitudeDirection,
                "Unknown attitude direction");

            CoordinateTransformProfile invalidAlignment = CoordinateTransformProfiles.UnityNative(
                "INVALID_ALIGNMENT",
                1.0,
                AttitudeDirection.BodyToWorld,
                new Quaterniond(0.0, 0.0, 0.0, 0.0));
            RequireConversionFailure(
                unityState,
                invalidAlignment,
                ConversionFailureReason.InvalidModelAlignment,
                "Invalid model alignment");

            var handednessMismatch = new CoordinateTransformProfile(
                "HANDEDNESS_MISMATCH",
                WorldFrame.Ned,
                BodyFrame.UnityBody,
                CoordinateTransformProfiles.NedWorldBasis,
                AxisBasis3d.Identity,
                1.0,
                AttitudeDirection.BodyToWorld,
                Quaterniond.Identity);
            VehicleState nedWithUnityBody = CreateState(
                Vector3d.Zero,
                Quaterniond.Identity,
                WorldFrame.Ned,
                BodyFrame.UnityBody);
            RequireConversionFailure(
                nedWithUnityBody,
                handednessMismatch,
                ConversionFailureReason.BasisHandednessMismatch,
                "Basis handedness mismatch");

            VehicleState unknownInputFrame = CreateState(
                Vector3d.Zero,
                Quaterniond.Identity,
                WorldFrame.Unknown,
                BodyFrame.UnityBody);
            CoordinateTransformProfile identity = CoordinateTransformProfiles.UnityNative(
                "IDENTITY",
                1.0,
                AttitudeDirection.BodyToWorld,
                Quaterniond.Identity);
            RequireConversionFailure(
                unknownInputFrame,
                identity,
                ConversionFailureReason.UnknownWorldFrame,
                "Unknown input frame");
        }

        private static void VerifyDependencyBoundary()
        {
            string[] forbiddenTokens =
            {
                "using UnityEngine",
                "UnityEngine.",
                "MonoBehaviour",
                "GameObject",
                "UnityEngine.Transform",
                "DemoMotionController",
                "AuvPose",
                "UdpClient",
                "System.Net.Sockets",
                "MemoryMappedFile",
                "SharedMemory",
                "MechanicalArm",
                "PerformanceRunner",
                "PerformancePublisher"
            };

            foreach (string relativePath in ProductionAssetPaths)
            {
                string path = Path.Combine(Environment.CurrentDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
                Require(File.Exists(path), "Production file missing: " + relativePath);
                string source = File.ReadAllText(path);
                foreach (string token in forbiddenTokens)
                {
                    Require(source.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0,
                        "Forbidden dependency token '" + token + "' found in " + relativePath + ".");
                }
            }
        }

        private static void RequirePosition(
            CoordinateTransformProfile profile,
            Vector3d inputPosition,
            Vector3d expectedPosition,
            string label)
        {
            VehicleState input = CreateState(
                inputPosition,
                Quaterniond.Identity,
                profile.SourceWorldFrame,
                profile.SourceBodyFrame);
            RequireConvert(input, profile, out ConvertedVehiclePose pose);
            RequireVector(pose.Position, expectedPosition, label);
        }

        private static void RequireOrientation(
            CoordinateTransformProfile profile,
            Quaterniond inputOrientation,
            Quaterniond expectedOrientation,
            string label)
        {
            VehicleState input = CreateState(
                Vector3d.Zero,
                inputOrientation,
                profile.SourceWorldFrame,
                profile.SourceBodyFrame);
            RequireConvert(input, profile, out ConvertedVehiclePose pose);
            RequireQuaternionEquivalent(pose.Orientation, expectedOrientation, label);
        }

        private static void RequireRotatedDirection(
            CoordinateTransformProfile profile,
            Quaterniond inputOrientation,
            Vector3d targetInputDirection,
            Vector3d expectedTargetDirection,
            string label)
        {
            VehicleState input = CreateState(
                Vector3d.Zero,
                inputOrientation,
                profile.SourceWorldFrame,
                profile.SourceBodyFrame);
            RequireConvert(input, profile, out ConvertedVehiclePose pose);
            RequireVector(
                QuaternionMath3d.Rotate(pose.Orientation, targetInputDirection),
                expectedTargetDirection,
                label);
        }

        private static void RequireConvert(
            VehicleState input,
            CoordinateTransformProfile profile,
            out ConvertedVehiclePose pose)
        {
            bool converted = VehiclePoseConverter.TryConvert(input, profile, out pose, out ConversionError error);
            Require(converted, "Conversion failed: " + error.Reason + " | " + error.Message);
            Require(error.IsNone, "Successful conversion returned an error.");
        }

        private static void RequireConversionFailure(
            VehicleState input,
            CoordinateTransformProfile profile,
            ConversionFailureReason expectedReason,
            string label)
        {
            bool converted = VehiclePoseConverter.TryConvert(
                input,
                profile,
                out ConvertedVehiclePose pose,
                out ConversionError error);
            Require(!converted, label + " unexpectedly converted.");
            Require(error.Reason == expectedReason,
                label + " returned " + error.Reason + " instead of " + expectedReason + ".");
            Require(!string.IsNullOrWhiteSpace(error.Message), label + " did not provide an error message.");
            Require(!pose.Succeeded, label + " returned a successful pose.");
        }

        private static VehicleState CreateState(
            Vector3d position,
            Quaterniond orientation,
            WorldFrame worldFrame,
            BodyFrame bodyFrame)
        {
            return new VehicleState(
                "AUV-N3-TEST",
                VehicleType.Auv,
                12.5,
                42UL,
                position,
                orientation,
                Vector3d.Zero,
                Vector3d.Zero,
                Vector3d.Zero,
                VehicleStateFields.Position | VehicleStateFields.Orientation,
                worldFrame,
                bodyFrame);
        }

        private static VehicleState CreateStateWithRawInvalidOrientation(Quaterniond orientation)
        {
            return CreateState(
                Vector3d.Zero,
                orientation,
                WorldFrame.UnityWorld,
                BodyFrame.UnityBody);
        }

        private static void RequireVector(Vector3d actual, Vector3d expected, string label)
        {
            RequireNear(actual.X, expected.X, label + " X");
            RequireNear(actual.Y, expected.Y, label + " Y");
            RequireNear(actual.Z, expected.Z, label + " Z");
        }

        private static void RequireQuaternionEquivalent(Quaterniond actual, Quaterniond expected, string label)
        {
            Require(
                QuaternionMath3d.RepresentsSameRotation(actual, expected, Tolerance),
                label + " differs. dot=" + QuaternionMath3d.Dot(actual, expected) + ".");
        }

        private static void RequireNear(double actual, double expected, string label, double tolerance = Tolerance)
        {
            Require(Math.Abs(actual - expected) <= tolerance,
                label + " expected " + expected + " but was " + actual + ".");
        }

        private static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private readonly struct VerificationCase
        {
            public VerificationCase(string name, Action body)
            {
                Name = name;
                Body = body;
            }

            public string Name { get; }
            public Action Body { get; }
        }
    }
}
