using System;
using System.Collections.Generic;
using System.IO;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    public static class RovVerticalMotionBatch2Verifier
    {
        private const string ReportArgument =
            "-rovVerticalBatch2VerifierReportPath";

        [Serializable]
        private sealed class CaseResult
        {
            public string name;
            public string status;
            public string detail;
        }

        [Serializable]
        private sealed class Report
        {
            public string schema;
            public string status;
            public string unityVersion;
            public int caseCount;
            public int passedCaseCount;
            public CaseResult[] cases;
        }

        public static void RunBatch()
        {
            string reportPath = RequireExternalCreateNewPath();
            var cases = new List<KeyValuePair<string, Func<string>>>
            {
                Case("01 Pure vertical build policy split", VerifyVerticalBuildPolicy),
                Case("02 Vertical yaw continuity", VerifyVerticalYawContinuity),
                Case("03 All-vertical cold activation seed", VerifyAllVerticalSeed),
                Case("04 Atomic XYZ and running apply", VerifyAtomicVerticalReplan),
                Case("05 ROV three-dimensional velocity", VerifyVelocityPolicies),
                Case("06 Lifecycle and route epochs", VerifyLifecycle),
                Case("07 Resume bridge accepted heading", VerifyResumeBridge),
                Case("08 AUV and USV policy regression", VerifySharedPolicies)
            };
            var results = new List<CaseResult>();
            int passed = 0;
            foreach (KeyValuePair<string, Func<string>> item in cases)
            {
                try
                {
                    results.Add(new CaseResult
                    {
                        name = item.Key,
                        status = "PASS",
                        detail = item.Value()
                    });
                    passed++;
                }
                catch (Exception exception)
                {
                    results.Add(new CaseResult
                    {
                        name = item.Key,
                        status = "FAIL",
                        detail = exception.GetType().Name + ": " + exception.Message
                    });
                }
            }

            bool success = passed == cases.Count;
            var report = new Report
            {
                schema = "ROV-VerticalMotion-Batch2-Verifier-v1",
                status = success
                    ? "ROV_VERTICAL_MOTION_BATCH2_ROUTE_VERTICAL_RUNTIME_SEMANTICS_PASS"
                    : "ROV_VERTICAL_MOTION_BATCH2_ROUTE_VERTICAL_RUNTIME_SEMANTICS_FAIL",
                unityVersion = Application.unityVersion,
                caseCount = cases.Count,
                passedCaseCount = passed,
                cases = results.ToArray()
            };
            File.WriteAllText(reportPath,
                JsonUtility.ToJson(report, true) + Environment.NewLine);
            if (!success)
                throw new InvalidOperationException(report.status + " | " +
                    passed + "/" + cases.Count + " cases passed.");
            Debug.Log(report.status + " | " + passed + "/" +
                cases.Count + " cases passed.");
        }

        private static string VerifyVerticalBuildPolicy()
        {
            Quaterniond seed = Yaw(31f);
            ActiveRouteSnapshot ascent = BuildRov("ASCENT", 1UL, seed,
                new Vector3d(0, -4, 0), new Vector3d(0, 3, 0));
            ActiveRouteSnapshot descent = BuildRov("DESCENT", 1UL, seed,
                new Vector3d(0, 3, 0), new Vector3d(0, -4, 0));
            Require(ActiveRouteSnapshotBuilder.TryBuild(
                    "ROV-BUILDER-ONLY", VehicleType.Rov, "BUILDER-ONLY", 1UL,
                    new[] { Vector3d.Zero, new Vector3d(0, 1, 0) },
                    VehicleRouteOrientationPolicy.RovLevelYaw, 0.0,
                    out ActiveRouteSnapshot builderOnly, out string builderError),
                builderError);
            Require(Near(ascent.TotalLength, 7.0) && Near(descent.TotalLength, 7.0),
                "ROV pure vertical ascent/descent length was not retained.");
            Require(!builderOnly.HasActivationHeadingSeed,
                "The builder invented a heading seed when none was supplied.");
            Require(!ActiveRouteSnapshotBuilder.TryBuild(
                    "ROV-ZERO", VehicleType.Rov, "ZERO", 1UL,
                    new[] { Vector3d.Zero, Vector3d.Zero },
                    VehicleRouteOrientationPolicy.RovLevelYaw, 0.0,
                    out _, out _),
                "A true zero-length ROV route was accepted.");
            Require(!ActiveRouteSnapshotBuilder.TryBuild(
                    "USV-VERTICAL", VehicleType.Usv, "USV-VERTICAL", 1UL,
                    new[] { Vector3d.Zero, new Vector3d(0, 2, 0) },
                    VehicleRouteOrientationPolicy.UsvSurfaceYaw, 0.0,
                    out _, out _),
                "USV pure vertical route unexpectedly became legal.");
            return "ROV ascent/descent pass; zero-length and USV vertical remain rejected.";
        }

        private static string VerifyVerticalYawContinuity()
        {
            ActiveRouteSnapshot route = BuildRov("MIDDLE-FINAL", 1UL, Yaw(71f),
                new Vector3d(0, 0, 0),
                new Vector3d(2, 0, 0),
                new Vector3d(2, 3, 0),
                new Vector3d(2, 5, 0));
            var runtime = new VehicleRouteRuntime(route, 1.0);
            runtime.Advance(2.25);
            Require(ForwardDot(runtime.SampleCurrentPose().Orientation,
                        Vector3.right) > 0.99999f,
                "Middle vertical segment did not inherit the previous +X heading.");
            runtime.Advance(2.7);
            Require(ForwardDot(runtime.SampleCurrentPose().Orientation,
                        Vector3.right) > 0.99999f,
                "Final vertical segment changed heading or looked ahead.");

            ActiveRouteSnapshot first = BuildRov("FIRST", 1UL, Yaw(47f),
                new Vector3d(0, 0, 0),
                new Vector3d(0, 2, 0),
                new Vector3d(0, 2, -2));
            var firstRuntime = new VehicleRouteRuntime(first, 1.0);
            Require(SameYaw(firstRuntime.SampleCurrentPose().Orientation, Yaw(47f)),
                "Vertical-first route did not use its activation seed.");
            firstRuntime.Advance(1.999);
            Require(SameYaw(firstRuntime.SampleCurrentPose().Orientation, Yaw(47f)),
                "Vertical-first route pre-read the following horizontal segment.");
            firstRuntime.Advance(0.001);
            Require(ForwardDot(firstRuntime.SampleCurrentPose().Orientation,
                        Vector3.back) > 0.99999f,
                "Vertical-to-horizontal heading did not change at the segment boundary.");
            return "First/middle/final vertical headings are deterministic and never pre-read future segments.";
        }

        private static string VerifyAllVerticalSeed()
        {
            Quaterniond seed = Yaw(-63f);
            ActiveRouteSnapshot route = BuildRov("ALL-VERTICAL", 1UL, seed,
                new Vector3d(4, -5, 8),
                new Vector3d(4, -1, 8),
                new Vector3d(4, -7, 8));
            var runtime = new VehicleRouteRuntime(route, 2.0);
            Require(SameYaw(runtime.SampleCurrentPose().Orientation, seed),
                "All-vertical route did not start from the composition seed.");
            runtime.Advance(3.0);
            Require(SameYaw(runtime.SampleCurrentPose().Orientation, seed),
                "All-vertical route did not retain a stable seed heading.");
            runtime.Restart();
            Require(SameYaw(runtime.SampleCurrentPose().Orientation, seed),
                "Restart did not deterministically restore the activation heading.");
            return "All-vertical and Restart retain the explicit composition-time yaw seed.";
        }

        private static string VerifyAtomicVerticalReplan()
        {
            ActiveRouteSnapshot active = BuildRov("ATOMIC", 4UL, Yaw(0f),
                Vector3d.Zero, new Vector3d(0, 0, 6));
            var runtime = new VehicleRouteRuntime(active, 1.0);
            using (var store = Store())
            using (var source = new RouteFollowingSource(
                       "batch2-atomic", 0.1, runtime,
                       WorldFrame.UnityWorld, BodyFrame.UnityBody))
            {
                source.Start(store);
                Require(source.Step(0.0) == 1, "Atomic harness did not start.");
                var accepted = new VehicleRoutePose(
                    runtime.SampleCurrentPose().Position, Yaw(38f));
                Require(AtomicRouteReplanCandidate.TryBuild(
                        active,
                        new[]
                        {
                            new Vector3d(0, 2, 0.1),
                            new Vector3d(0, 2, 3)
                        },
                        in accepted, 0.5,
                        out ActiveRouteSnapshot candidate, out string error), error);
                Require(candidate.WaypointCount == 3 &&
                        Near(candidate.GetWaypoint(1).Y, 2.0),
                    "ROV XYZ-distinct vertical waypoint was compressed away.");
                ulong routeEpoch = runtime.RouteEpoch;
                ulong sourceEpoch = source.SourceEpoch;
                Require(source.TryPublishRunningReplan(
                        candidate, in accepted, 0.5, out error), error);
                Require(runtime.RouteEpoch == routeEpoch + 1UL &&
                        source.SourceEpoch == sourceEpoch + 1UL &&
                        Near(runtime.SampleCurrentPose().Position, accepted.Position) &&
                        SameYaw(runtime.SampleCurrentPose().Orientation, accepted.Orientation),
                    "Running Apply lost epoch or accepted-pose continuity.");
                Require(store.TryReadLatest(source.SourceId, source.SourceEpoch,
                        active.VehicleId, out ReceivedVehicleState first) &&
                        Near(first.State.Position, accepted.Position) &&
                        SameYaw(first.State.Orientation, accepted.Orientation),
                    "New SourceEpoch did not begin at the accepted pose.");
                source.Step(0.6);
                Require(runtime.DistanceAlongRoute > 0.0,
                    "Running Apply did not advance from its vertical connection segment.");
            }
            return "ROV XYZ distinctness retains vertical points; Running Apply preserves accepted pose and epochs.";
        }

        private static string VerifyVelocityPolicies()
        {
            Vector3d diagonal = FirstVelocity(BuildRov("VEL-DIAGONAL", 1UL,
                Yaw(0f), Vector3d.Zero, new Vector3d(3, 4, 0)), 2.0);
            Require(diagonal.Y > 0.0 && Near(Magnitude(diagonal), 2.0),
                "ROV diagonal velocity lost Y or route speed magnitude.");
            Vector3d ascent = FirstVelocity(BuildRov("VEL-UP", 1UL,
                Yaw(12f), Vector3d.Zero, new Vector3d(0, 4, 0)), 2.0);
            Vector3d descent = FirstVelocity(BuildRov("VEL-DOWN", 1UL,
                Yaw(12f), Vector3d.Zero, new Vector3d(0, -4, 0)), 2.0);
            Require(Near(ascent.X, 0.0) && Near(ascent.Z, 0.0) &&
                    ascent.Y > 0.0 && Near(Magnitude(ascent), 2.0) &&
                    descent.Y < 0.0 && Near(Magnitude(descent), 2.0),
                "ROV pure vertical velocity direction or magnitude is wrong.");
            return "ROV diagonal/ascent/descent velocities are full XYZ at configured resultant speed.";
        }

        private static string VerifyLifecycle()
        {
            ActiveRouteSnapshot route = BuildRov("LIFECYCLE", 1UL, Yaw(24f),
                Vector3d.Zero, new Vector3d(0, 3, 0));
            var runtime = new VehicleRouteRuntime(route, 1.0);
            runtime.Advance(0.5);
            double distance = runtime.DistanceAlongRoute;
            Require(runtime.Pause(), "Pause failed.");
            runtime.Advance(1.0);
            Require(Near(runtime.DistanceAlongRoute, distance) && runtime.Resume(),
                "Pause advanced or Resume failed.");
            var hold = new VehicleRoutePose(
                new Vector3d(0, 0.5, 0), Yaw(24f));
            Require(runtime.EnterHold(in hold), "Hold failed.");
            runtime.Advance(1.0);
            Require(Near(runtime.SampleCurrentPose().Position, hold.Position) &&
                    runtime.Resume(),
                "Hold moved or recovery failed.");
            ulong epoch = runtime.RouteEpoch;
            runtime.Restart();
            Require(runtime.RouteEpoch == epoch + 1UL &&
                    Near(runtime.DistanceAlongRoute, 0.0) &&
                    SameYaw(runtime.SampleCurrentPose().Orientation, Yaw(24f)),
                "Restart epoch/progress/yaw contract drifted.");
            runtime.Complete();
            Require(runtime.State == VehicleRouteExecutionState.Completed &&
                    Near(runtime.DistanceAlongRoute, route.TotalLength),
                "Complete contract drifted.");
            return "Pause/Resume/Hold/recovery/Restart/Complete preserve progress, yaw, and RouteEpoch semantics.";
        }

        private static string VerifyResumeBridge()
        {
            ActiveRouteSnapshot route = BuildRov("BRIDGE", 1UL, Yaw(8f),
                new Vector3d(0, 0, 0), new Vector3d(0, 4, 0));
            var runtime = new VehicleRouteRuntime(route, 1.0);
            var accepted = new VehicleRoutePose(
                new Vector3d(-2, 0, 0), Yaw(81f));
            Require(runtime.BeginResumeBridge(in accepted),
                "Resume bridge rejected a valid accepted pose.");
            VehicleRoutePose first = runtime.SampleCurrentPose();
            Require(Near(first.Position, accepted.Position) &&
                    SameYaw(first.Orientation, accepted.Orientation),
                "Resume bridge did not begin at accepted pose/yaw.");
            runtime.Advance(0.5);
            Require(runtime.SampleCurrentPose().Position.X > accepted.Position.X,
                "Resume bridge did not advance without a pose flashback.");
            return "Source-transition bridge begins at the accepted pose and its deterministic level yaw.";
        }

        private static string VerifySharedPolicies()
        {
            ActiveRouteSnapshot auv = Build(
                "AUV", VehicleType.Auv,
                VehicleRouteOrientationPolicy.AuvThreeDimensional,
                Vector3d.Zero, new Vector3d(3, 4, 0));
            Quaterniond auvOrientation =
                new VehicleRouteRuntime(auv, 1.0).SampleCurrentPose().Orientation;
            Require(Math.Abs(Forward(auvOrientation).y) > 0.5f,
                "AUV route-derived pitch was flattened.");
            Vector3d auvVelocity = FirstVelocity(auv, 2.0);
            Require(auvVelocity.Y > 0.0 && Near(Magnitude(auvVelocity), 2.0),
                "AUV full-3D velocity changed.");

            ActiveRouteSnapshot usv = Build(
                "USV", VehicleType.Usv,
                VehicleRouteOrientationPolicy.UsvSurfaceYaw,
                Vector3d.Zero, new Vector3d(3, 0, 4));
            Vector3d usvVelocity = FirstVelocity(usv, 2.0);
            Require(Near(usvVelocity.Y, 0.0) &&
                    Near(Magnitude(usvVelocity), 2.0),
                "USV surface velocity changed.");
            return "AUV keeps pitch/full-3D velocity; USV keeps horizontal route/orientation/velocity semantics.";
        }

        private static ActiveRouteSnapshot BuildRov(
            string id, ulong version, Quaterniond seed,
            params Vector3d[] points)
        {
            Require(ActiveRouteSnapshotBuilder.TryBuild(
                    "ROV-" + id, VehicleType.Rov, id, version, points,
                    VehicleRouteOrientationPolicy.RovLevelYaw, 0.0, seed,
                    out ActiveRouteSnapshot snapshot, out string error), error);
            return snapshot;
        }

        private static ActiveRouteSnapshot Build(
            string id, VehicleType type,
            VehicleRouteOrientationPolicy policy,
            params Vector3d[] points)
        {
            Require(ActiveRouteSnapshotBuilder.TryBuild(
                    id, type, id, 1UL, points, policy, 0.0,
                    out ActiveRouteSnapshot snapshot, out string error), error);
            return snapshot;
        }

        private static Vector3d FirstVelocity(
            ActiveRouteSnapshot route, double speed)
        {
            var runtime = new VehicleRouteRuntime(route, speed);
            using (var store = Store())
            using (var source = new RouteFollowingSource(
                       "velocity-" + route.VehicleId, 0.1, runtime,
                       WorldFrame.UnityWorld, BodyFrame.UnityBody))
            {
                source.Start(store);
                Require(source.Step(0.0) == 1, "Velocity sample was rejected.");
                Require(store.TryReadLatest(source.SourceId, source.SourceEpoch,
                        route.VehicleId, out ReceivedVehicleState state),
                    "Velocity sample was not readable.");
                return state.State.LinearVelocity;
            }
        }

        private static VehicleStateStore Store()
        {
            return new VehicleStateStore(new VehicleStateStorePolicy(
                capacityPerVehicle: 16,
                timeoutSeconds: 2.0));
        }

        private static Quaterniond Yaw(float degrees)
        {
            Quaternion value = Quaternion.Euler(0f, degrees, 0f);
            return new Quaterniond(value.x, value.y, value.z, value.w);
        }

        private static Vector3 Forward(Quaterniond orientation)
        {
            return new Quaternion(
                (float)orientation.X, (float)orientation.Y,
                (float)orientation.Z, (float)orientation.W) * Vector3.forward;
        }

        private static float ForwardDot(Quaterniond orientation, Vector3 expected)
        {
            return Vector3.Dot(Forward(orientation).normalized, expected.normalized);
        }

        private static bool SameYaw(Quaterniond a, Quaterniond b)
        {
            Vector3 af = Forward(a);
            Vector3 bf = Forward(b);
            af.y = 0f;
            bf.y = 0f;
            return Vector3.Dot(af.normalized, bf.normalized) > 0.99999f;
        }

        private static double Magnitude(Vector3d value)
        {
            return Math.Sqrt(value.X * value.X +
                value.Y * value.Y + value.Z * value.Z);
        }

        private static bool Near(Vector3d a, Vector3d b)
        {
            return Near(a.X, b.X) && Near(a.Y, b.Y) && Near(a.Z, b.Z);
        }

        private static bool Near(double a, double b)
        {
            return Math.Abs(a - b) <= 1e-6;
        }

        private static KeyValuePair<string, Func<string>> Case(
            string name, Func<string> body)
        {
            return new KeyValuePair<string, Func<string>>(name, body);
        }

        private static string RequireExternalCreateNewPath()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int index = 0; index < args.Length - 1; index++)
            {
                if (!string.Equals(args[index], ReportArgument,
                        StringComparison.Ordinal))
                    continue;
                string path = Path.GetFullPath(args[index + 1]);
                Require(!File.Exists(path), "Verifier report path already exists.");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                return path;
            }
            throw new InvalidOperationException(ReportArgument + " is required.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
