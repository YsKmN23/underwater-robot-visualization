using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.LocalTesting;
using UnityEditor;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    public static class UsvDiagnosticMotionM2BVerifier
    {
        private const double CycleSeconds = 14.0;
        private const double InitialMotionStart = 0.75;
        private const double FirstMotionEnd = 6.25;
        private const double SecondMotionStart = 7.25;
        private const double SecondMotionEnd = 12.75;
        private const double DifferenceSeconds = 0.001;
        private const double PoseTolerance = 1e-8;
        private const double RotationToleranceRadians = 1e-7;
        private const double BoundaryLinearSpeedTolerance = 1e-4;
        private const double BoundaryAngularSpeedTolerance = 1e-4;
        private const string GeneratorPath =
            "Assets/Scripts/Visualization/Data/LocalTesting/DeterministicUsvDiagnosticTrajectory.cs";

        private static readonly LocalTestVehicle Vehicle = new LocalTestVehicle(
            "USV-01",
            VehicleType.Usv,
            new Vector3d(0.15, 0.18, 2.05),
            WorldFrame.UnityWorld,
            BodyFrame.UnityBody);

        [MenuItem("Tools/Underwater Demo/M2-B/Verify USV Diagnostic Motion")]
        public static void RunFromMenu()
        {
            Execute();
        }

        public static void RunBatch()
        {
            try
            {
                Execute();
                Debug.Log("M2B_USV_DIAGNOSTIC_STATIC_VALIDATION_PASS");
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "M2B_USV_DIAGNOSTIC_STATIC_VALIDATION_FAIL | " +
                    exception.Message);
                throw;
            }
        }

        private static void Execute()
        {
            var checks = new List<string>();
            var generator = new DeterministicUsvDiagnosticTrajectory();

            TestDeterminismAndDataSemantics(generator);
            checks.Add("Determinism, identity, frames, timestamps, sequences and ValidFields");
            TestPeriodAndPositiveModulo(generator);
            checks.Add("Explicit 14-second period and positive modulo");
            TestHolds(generator);
            checks.Add("Initial, middle and final multi-sample holds");
            TestHorizontalYawOnlyMotion(generator);
            checks.Add("Two opposite XZ turns, constant Y and yaw-only orientation");
            TestTangentAlignment(generator);
            checks.Add("Business forward aligned with both path tangents");
            TestClosedPosesAndBoundaries(generator);
            checks.Add("Closed poses, segment continuity and loop continuity");
            TestBoundaryVelocities(generator);
            checks.Add("Near-zero linear and angular speed at every boundary");
            TestFiniteSweep(generator);
            checks.Add("Finite normalized quaternion and pose sweep");
            TestGeneratorDependencyBoundary();
            checks.Add("Pure-data generator dependency boundary");

            WriteReport(checks);
        }

        private static void TestDeterminismAndDataSemantics(
            DeterministicUsvDiagnosticTrajectory generator)
        {
            const ulong sequence = 123456789UL;
            const double timestamp = 2.125;
            VehicleState first = generator.Evaluate(Vehicle, sequence, timestamp);
            VehicleState repeated = generator.Evaluate(Vehicle, sequence, timestamp);
            Require(first.Equals(repeated),
                "Identical generator input did not produce identical state.");

            double[] times = { -0.25, 0.0, 0.5, 2.125, 6.5, 9.0, 13.5, 28.25 };
            for (int index = 0; index < times.Length; index++)
            {
                ulong currentSequence = (ulong)(index * 101 + 7);
                VehicleState state =
                    generator.Evaluate(Vehicle, currentSequence, times[index]);
                Require(state.VehicleId == Vehicle.VehicleId &&
                        state.VehicleType == Vehicle.VehicleType &&
                        state.SourceTimestampSeconds.Equals(times[index]) &&
                        state.SequenceNumber == currentSequence &&
                        state.WorldFrame == Vehicle.WorldFrame &&
                        state.BodyFrame == Vehicle.BodyFrame,
                    "Generator changed identity, time, sequence, or frame semantics.");
                Require(state.ValidFields ==
                        (VehicleStateFields.Position | VehicleStateFields.Orientation),
                    "Generator expanded or reduced Position|Orientation ValidFields.");
                Require(state.LinearVelocity.Equals(Vector3d.Zero) &&
                        state.AngularVelocity.Equals(Vector3d.Zero) &&
                        state.LinearAcceleration.Equals(Vector3d.Zero),
                    "Generator fabricated velocity or acceleration values.");
                Require(state.IsStructurallyValid,
                    "Generator returned a structurally invalid state.");
            }
        }

        private static void TestPeriodAndPositiveModulo(
            DeterministicUsvDiagnosticTrajectory generator)
        {
            double[] times = { -28.25, -14.0, -0.25, 0.0, 0.25, 2.5, 6.5, 9.5, 13.75 };
            foreach (double time in times)
            {
                RequirePoseNear(
                    generator.Evaluate(Vehicle, 1UL, time),
                    generator.Evaluate(Vehicle, 2UL, time + CycleSeconds),
                    PoseTolerance,
                    RotationToleranceRadians,
                    "Generator pose is not periodic over 14 seconds at t=" +
                    time.ToString("R", CultureInfo.InvariantCulture) + ".");
            }

            RequirePoseNear(
                generator.Evaluate(Vehicle, 3UL, -0.25),
                generator.Evaluate(Vehicle, 4UL, 13.75),
                PoseTolerance,
                RotationToleranceRadians,
                "Negative source time was not mapped with positive modulo.");
            RequirePoseNear(
                generator.Evaluate(Vehicle, 5UL, 14.25),
                generator.Evaluate(Vehicle, 6UL, 0.25),
                PoseTolerance,
                RotationToleranceRadians,
                "Time beyond one cycle was not reduced with positive modulo.");
        }

        private static void TestHolds(
            DeterministicUsvDiagnosticTrajectory generator)
        {
            AssertPoseZeroAt(generator, new[] { 0.0, 0.2, 0.5, 0.749 });
            AssertPoseZeroAt(generator, new[] { 6.25, 6.5, 7.0, 7.249 });
            AssertPoseZeroAt(generator, new[] { 12.75, 13.0, 13.5, 13.999 });
        }

        private static void TestHorizontalYawOnlyMotion(
            DeterministicUsvDiagnosticTrajectory generator)
        {
            double minX = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double minZ = double.PositiveInfinity;
            double maxZ = double.NegativeInfinity;
            double firstMaxDistance = 0.0;
            double secondMaxDistance = 0.0;

            for (int index = 0; index <= 280; index++)
            {
                double time = index * 0.05;
                VehicleState state = generator.Evaluate(Vehicle, (ulong)index, time);
                Require(Math.Abs(state.Position.Y - Vehicle.PositionOffset.Y) <= 1e-12,
                    "USV business Y changed at t=" +
                    time.ToString("R", CultureInfo.InvariantCulture) + ".");
                Require(Math.Abs(state.Orientation.X) <= 1e-12 &&
                        Math.Abs(state.Orientation.Z) <= 1e-12,
                    "USV orientation contains business pitch or roll at t=" +
                    time.ToString("R", CultureInfo.InvariantCulture) + ".");

                minX = Math.Min(minX, state.Position.X);
                maxX = Math.Max(maxX, state.Position.X);
                minZ = Math.Min(minZ, state.Position.Z);
                maxZ = Math.Max(maxZ, state.Position.Z);
                double distance = PositionDistance(
                    state.Position,
                    Vehicle.PositionOffset);
                if (time > InitialMotionStart && time < FirstMotionEnd)
                {
                    firstMaxDistance = Math.Max(firstMaxDistance, distance);
                }
                if (time > SecondMotionStart && time < SecondMotionEnd)
                {
                    secondMaxDistance = Math.Max(secondMaxDistance, distance);
                }
            }

            Require(maxX - minX > 0.4 && maxZ - minZ > 0.4,
                "USV trajectory did not exercise both X and Z.");
            Require(firstMaxDistance > 0.25 && secondMaxDistance > 0.25,
                "One or both closed-turn segments did not move.");

            VehicleState firstTurn =
                generator.Evaluate(Vehicle, 1UL, 2.25);
            VehicleState secondTurn =
                generator.Evaluate(Vehicle, 2UL, 8.75);
            Require(firstTurn.Position.X > Vehicle.PositionOffset.X &&
                    secondTurn.Position.X < Vehicle.PositionOffset.X,
                "The two turn paths are not mirrored across the origin.");
            Require(SignedYawRadians(firstTurn.Orientation) > 0.1 &&
                    SignedYawRadians(secondTurn.Orientation) < -0.1,
                "The two closed turns do not use opposite yaw directions.");
        }

        private static void TestTangentAlignment(
            DeterministicUsvDiagnosticTrajectory generator)
        {
            AssertTangentAlignment(generator, 2.75);
            AssertTangentAlignment(generator, 9.25);
        }

        private static void AssertTangentAlignment(
            DeterministicUsvDiagnosticTrajectory generator,
            double time)
        {
            VehicleState before =
                generator.Evaluate(Vehicle, 1UL, time - DifferenceSeconds);
            VehicleState center =
                generator.Evaluate(Vehicle, 2UL, time);
            VehicleState after =
                generator.Evaluate(Vehicle, 3UL, time + DifferenceSeconds);
            Vector3d tangent = Normalize(new Vector3d(
                after.Position.X - before.Position.X,
                0.0,
                after.Position.Z - before.Position.Z));
            Vector3d forward = Normalize(Forward(center.Orientation));
            double dot = tangent.X * forward.X +
                         tangent.Y * forward.Y +
                         tangent.Z * forward.Z;
            Require(dot > 0.9999,
                "USV forward is not aligned with path tangent at t=" +
                time.ToString("R", CultureInfo.InvariantCulture) +
                "; dot=" + dot.ToString("R", CultureInfo.InvariantCulture) + ".");
        }

        private static void TestClosedPosesAndBoundaries(
            DeterministicUsvDiagnosticTrajectory generator)
        {
            AssertPoseZeroAt(generator, new[] { 0.0, FirstMotionEnd, SecondMotionEnd, CycleSeconds });

            double[] boundaries =
            {
                0.0,
                InitialMotionStart,
                FirstMotionEnd,
                SecondMotionStart,
                SecondMotionEnd,
                CycleSeconds
            };
            foreach (double boundary in boundaries)
            {
                VehicleState before =
                    generator.Evaluate(Vehicle, 1UL, boundary - DifferenceSeconds);
                VehicleState at =
                    generator.Evaluate(Vehicle, 2UL, boundary);
                VehicleState after =
                    generator.Evaluate(Vehicle, 3UL, boundary + DifferenceSeconds);
                RequirePoseNear(
                    before,
                    at,
                    1e-7,
                    1e-6,
                    "Pose is discontinuous immediately before boundary t=" +
                    boundary.ToString("R", CultureInfo.InvariantCulture) + ".");
                RequirePoseNear(
                    at,
                    after,
                    1e-7,
                    1e-6,
                    "Pose is discontinuous immediately after boundary t=" +
                    boundary.ToString("R", CultureInfo.InvariantCulture) + ".");
            }

            RequirePoseNear(
                generator.Evaluate(Vehicle, 1UL, CycleSeconds - DifferenceSeconds),
                generator.Evaluate(Vehicle, 2UL, DifferenceSeconds),
                PoseTolerance,
                RotationToleranceRadians,
                "14-to-0 loop boundary is discontinuous.");
        }

        private static void TestBoundaryVelocities(
            DeterministicUsvDiagnosticTrajectory generator)
        {
            double[] boundaries =
            {
                0.0,
                InitialMotionStart,
                FirstMotionEnd,
                SecondMotionStart,
                SecondMotionEnd,
                CycleSeconds
            };
            foreach (double boundary in boundaries)
            {
                VehicleState before =
                    generator.Evaluate(Vehicle, 1UL, boundary - DifferenceSeconds);
                VehicleState after =
                    generator.Evaluate(Vehicle, 2UL, boundary + DifferenceSeconds);
                double linearSpeed =
                    PositionDistance(before.Position, after.Position) /
                    (2.0 * DifferenceSeconds);
                double angularSpeed =
                    RotationAngleRadians(before.Orientation, after.Orientation) /
                    (2.0 * DifferenceSeconds);
                Require(linearSpeed <= BoundaryLinearSpeedTolerance,
                    "Boundary linear speed is not near zero at t=" +
                    boundary.ToString("R", CultureInfo.InvariantCulture) +
                    "; speed=" +
                    linearSpeed.ToString("R", CultureInfo.InvariantCulture) + ".");
                Require(angularSpeed <= BoundaryAngularSpeedTolerance,
                    "Boundary angular speed is not near zero at t=" +
                    boundary.ToString("R", CultureInfo.InvariantCulture) +
                    "; speed=" +
                    angularSpeed.ToString("R", CultureInfo.InvariantCulture) + ".");
            }
        }

        private static void TestFiniteSweep(
            DeterministicUsvDiagnosticTrajectory generator)
        {
            for (int index = -280; index <= 560; index++)
            {
                double time = index * 0.05;
                VehicleState state = generator.Evaluate(Vehicle, (ulong)(index + 280), time);
                Require(state.Position.IsFinite &&
                        state.Orientation.IsFinite &&
                        state.Orientation.TryNormalize(out Quaterniond normalized) &&
                        Math.Abs(normalized.MagnitudeSquared - 1.0) <= 1e-12,
                    "Non-finite or non-normalizable pose at t=" +
                    time.ToString("R", CultureInfo.InvariantCulture) + ".");
            }
        }

        private static void TestGeneratorDependencyBoundary()
        {
            string source = File.ReadAllText(GeneratorPath);
            Require(source.IndexOf("using UnityEngine", StringComparison.Ordinal) < 0 &&
                    source.IndexOf("modelAlignment", StringComparison.OrdinalIgnoreCase) < 0 &&
                    source.IndexOf("VehiclePoseDriver", StringComparison.Ordinal) < 0 &&
                    source.IndexOf("VehicleStateStore", StringComparison.Ordinal) < 0 &&
                    source.IndexOf("Transform", StringComparison.Ordinal) < 0,
                "USV generator crossed the pure-data or model-alignment boundary.");
        }

        private static void AssertPoseZeroAt(
            DeterministicUsvDiagnosticTrajectory generator,
            IEnumerable<double> times)
        {
            foreach (double time in times)
            {
                VehicleState state = generator.Evaluate(Vehicle, 0UL, time);
                Require(PositionDistance(state.Position, Vehicle.PositionOffset) <=
                        PoseTolerance &&
                        RotationAngleRadians(
                            state.Orientation,
                            Quaterniond.Identity) <= RotationToleranceRadians,
                    "Expected Pose 0 at t=" +
                    time.ToString("R", CultureInfo.InvariantCulture) + ".");
            }
        }

        private static void RequirePoseNear(
            VehicleState left,
            VehicleState right,
            double positionTolerance,
            double rotationTolerance,
            string message)
        {
            Require(PositionDistance(left.Position, right.Position) <=
                    positionTolerance &&
                    RotationAngleRadians(left.Orientation, right.Orientation) <=
                    rotationTolerance,
                message);
        }

        private static double PositionDistance(Vector3d left, Vector3d right)
        {
            double x = left.X - right.X;
            double y = left.Y - right.Y;
            double z = left.Z - right.Z;
            return Math.Sqrt(x * x + y * y + z * z);
        }

        private static double RotationAngleRadians(
            Quaterniond left,
            Quaterniond right)
        {
            Require(left.TryNormalize(out Quaterniond normalizedLeft),
                "Cannot compare a non-normalizable left quaternion.");
            Require(right.TryNormalize(out Quaterniond normalizedRight),
                "Cannot compare a non-normalizable right quaternion.");
            double dot = Math.Abs(
                normalizedLeft.X * normalizedRight.X +
                normalizedLeft.Y * normalizedRight.Y +
                normalizedLeft.Z * normalizedRight.Z +
                normalizedLeft.W * normalizedRight.W);
            return 2.0 * Math.Acos(Math.Min(1.0, dot));
        }

        private static double SignedYawRadians(Quaterniond orientation)
        {
            Require(orientation.TryNormalize(out Quaterniond q),
                "Cannot extract yaw from non-normalizable quaternion.");
            double sinYaw = 2.0 * (q.W * q.Y + q.X * q.Z);
            double cosYaw = 1.0 - 2.0 * (q.Y * q.Y + q.X * q.X);
            return Math.Atan2(sinYaw, cosYaw);
        }

        private static Vector3d Forward(Quaterniond orientation)
        {
            Require(orientation.TryNormalize(out Quaterniond q),
                "Cannot rotate forward with non-normalizable quaternion.");
            return new Vector3d(
                2.0 * (q.X * q.Z + q.W * q.Y),
                2.0 * (q.Y * q.Z - q.W * q.X),
                1.0 - 2.0 * (q.X * q.X + q.Y * q.Y));
        }

        private static Vector3d Normalize(Vector3d value)
        {
            double magnitude = Math.Sqrt(
                value.X * value.X +
                value.Y * value.Y +
                value.Z * value.Z);
            Require(IsFinite(magnitude) && magnitude > 1e-12,
                "Cannot normalize a zero or non-finite vector.");
            return new Vector3d(
                value.X / magnitude,
                value.Y / magnitude,
                value.Z / magnitude);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static void WriteReport(IReadOnlyCollection<string> checks)
        {
            string outputDirectory = EvidenceDirectory();
            Directory.CreateDirectory(outputDirectory);
            var markdown = new StringBuilder();
            markdown.AppendLine("# M2-B USV Diagnostic Motion Static Verification");
            markdown.AppendLine();
            markdown.AppendLine("- Status: `M2B_USV_DIAGNOSTIC_STATIC_VALIDATION_PASS`");
            markdown.AppendLine("- Cycle: `14.0 seconds`");
            markdown.AppendLine("- Finite-difference epsilon: `0.001 seconds`");
            markdown.AppendLine("- Linear boundary speed tolerance: `0.0001 units/s`");
            markdown.AppendLine("- Angular boundary speed tolerance: `0.0001 rad/s`");
            markdown.AppendLine("- Checks: `" + checks.Count + "/" + checks.Count + "`");
            markdown.AppendLine();
            foreach (string check in checks)
            {
                markdown.AppendLine("- PASS — " + check);
            }
            File.WriteAllText(
                Path.Combine(outputDirectory, "m2b_usv_static_report.md"),
                markdown.ToString(),
                new UTF8Encoding(false));
        }

        private static string EvidenceDirectory()
        {
            string configured =
                Environment.GetEnvironmentVariable("M2B_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.GetFullPath(configured);
            }
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(
                Path.Combine(projectRoot, "..", "..", "M2B_Validation"));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
