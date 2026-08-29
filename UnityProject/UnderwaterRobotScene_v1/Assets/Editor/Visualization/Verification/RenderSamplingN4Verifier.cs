using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Sampling;
using UnderwaterRobotScene.Visualization.Transforms;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnderwaterRobotScene.EditorTools
{
    public static class RenderSamplingN4Verifier
    {
        private const double Tolerance = 1e-8;

        private static readonly string[] ProductionAssetPaths =
        {
            "Assets/Scripts/Visualization/Sampling/RenderSamplingPolicy.cs",
            "Assets/Scripts/Visualization/Sampling/RenderSampleRequest.cs",
            "Assets/Scripts/Visualization/Sampling/RenderPoseSample.cs",
            "Assets/Scripts/Visualization/Sampling/PoseInterpolation.cs",
            "Assets/Scripts/Visualization/Sampling/VehicleRenderSampler.cs"
        };

#if UNITY_EDITOR
        [MenuItem("Tools/Underwater Demo/Verify Render Sampling Core N4")]
        public static void RunFromMenu()
        {
            int exitCode = RunVerification(Console.WriteLine);
            if (exitCode != 0)
            {
                throw new InvalidOperationException("N4 verification failed.");
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
                new VerificationCase("Policy, request, no-data, and epoch outcomes", VerifyPolicyAndUnavailableCases),
                new VerificationCase("Single-sample exact and bounded hold behavior", VerifySingleSampleBehavior),
                new VerificationCase("Exact oldest, middle, and latest selection", VerifyExactSelection),
                new VerificationCase("Position interpolation and coordinate profiles", VerifyPositionInterpolation),
                new VerificationCase("Shortest-path normalized quaternion interpolation", VerifyQuaternionInterpolation),
                new VerificationCase("Gap, rolling history, and duplicate-time safety", VerifyGapAndHistoryBoundaries),
                new VerificationCase("Epoch transitions and source-time discontinuities", VerifyEpochAndDiscontinuity),
                new VerificationCase("Stale, hold, fault, and recovery outcomes", VerifyHealthAndLifecycle),
                new VerificationCase("N3 conversion failures propagate unchanged", VerifyConversionFailurePropagation),
                new VerificationCase("Multi-source and multi-vehicle isolation", VerifyIsolation),
                new VerificationCase("Concurrent sampling reads stable immutable windows", VerifyConcurrentSampling),
                new VerificationCase("N4 pure C# dependency boundary", VerifyDependencyBoundary)
            };

            int passed = 0;
            foreach (VerificationCase test in tests)
            {
                try
                {
                    test.Body();
                    passed++;
                    writeLine("[PASS] " + test.Name);
                }
                catch (Exception exception)
                {
                    writeLine("[FAIL] " + test.Name + " | " + exception.Message);
                }
            }

            writeLine("N4 verification: " + passed + "/" + tests.Length + " groups passed.");
            return passed == tests.Length ? 0 : 1;
        }

        private static void VerifyPolicyAndUnavailableCases()
        {
            CoordinateTransformProfile profile = UnityProfile();
            RenderSamplingPolicy valid = Policy();
            Require(valid.TryValidate(out string validationError), validationError);

            var invalidPolicies = new[]
            {
                new RenderSamplingPolicy(double.NaN, 1.0, 0.0, AfterLatestBehavior.HoldLatest, true),
                new RenderSamplingPolicy(double.PositiveInfinity, 1.0, 0.0, AfterLatestBehavior.HoldLatest, true),
                new RenderSamplingPolicy(0.0, 1.0, 0.0, AfterLatestBehavior.HoldLatest, true),
                new RenderSamplingPolicy(1.0, -1.0, 0.0, AfterLatestBehavior.HoldLatest, true),
                new RenderSamplingPolicy(1.0, double.PositiveInfinity, 0.0, AfterLatestBehavior.HoldLatest, true),
                new RenderSamplingPolicy(1.0, 1.0, -0.1, AfterLatestBehavior.HoldLatest, true),
                new RenderSamplingPolicy(1.0, 1.0, double.NaN, AfterLatestBehavior.HoldLatest, true),
                new RenderSamplingPolicy(1.0, 1.0, 0.0, (AfterLatestBehavior)99, true)
            };
            foreach (RenderSamplingPolicy invalid in invalidPolicies)
            {
                Require(!invalid.TryValidate(out validationError), "Invalid policy was accepted.");
                using (VehicleStateStore store = MakeStore())
                {
                    RenderPoseSample result = VehicleRenderSampler.Sample(
                        store,
                        Request("SRC", 1UL, "AUV", 0.0, 0.0, profile, invalid));
                    RequireFailure(result, RenderSampleFailureReason.InvalidPolicy);
                }
            }

            using (VehicleStateStore store = MakeStore())
            {
                RequireFailure(
                    VehicleRenderSampler.Sample(store, Request("", 1UL, "AUV", 0.0, 0.0, profile, valid)),
                    RenderSampleFailureReason.InvalidRequest);
                RequireFailure(
                    VehicleRenderSampler.Sample(store, Request("SRC", 1UL, "", 0.0, 0.0, profile, valid)),
                    RenderSampleFailureReason.InvalidRequest);
                RequireFailure(
                    VehicleRenderSampler.Sample(store, Request("SRC", 1UL, "AUV", double.NaN, 0.0, profile, valid)),
                    RenderSampleFailureReason.InvalidRequest);
                RequireFailure(
                    VehicleRenderSampler.Sample(store, Request("SRC", 1UL, "AUV", 0.0, double.PositiveInfinity, profile, valid)),
                    RenderSampleFailureReason.InvalidRequest);
                RequireFailure(
                    VehicleRenderSampler.Sample(
                        store,
                        Request("SRC", 1UL, "AUV", 0.0, 0.0, profile, valid, (DataSourceStatus)99)),
                    RenderSampleFailureReason.InvalidRequest);
                RequireFailure(
                    VehicleRenderSampler.Sample(store, Request("SRC", 1UL, "AUV", 0.0, 0.0, profile, valid)),
                    RenderSampleFailureReason.NoData);

                Publish(store, "SRC", 3UL, "OTHER", 0.0, 0.0, Vector3d.Zero, Quaterniond.Identity, 1UL);
                RequireFailure(
                    VehicleRenderSampler.Sample(store, Request("SRC", 3UL, "AUV", 0.0, 0.0, profile, valid)),
                    RenderSampleFailureReason.NoData);
                RequireFailure(
                    VehicleRenderSampler.Sample(store, Request("SRC", 2UL, "OTHER", 0.0, 0.0, profile, valid)),
                    RenderSampleFailureReason.EpochUnavailable);
            }
        }

        private static void VerifySingleSampleBehavior()
        {
            using (VehicleStateStore store = MakeStore())
            {
                Publish(store, "SRC", 1UL, "AUV", 10.0, 20.0, new Vector3d(2.0, 3.0, 4.0), Quaterniond.Identity, 1UL);

                RenderPoseSample exact = VehicleRenderSampler.Sample(
                    store,
                    Request("SRC", 1UL, "AUV", 10.0, 20.1, UnityProfile(), Policy()));
                RequireSuccess(exact, RenderSampleMode.Exact);
                RequireVector(exact.Position, new Vector3d(2.0, 3.0, 4.0), "single exact");

                RenderPoseSample before = VehicleRenderSampler.Sample(
                    store,
                    Request("SRC", 1UL, "AUV", 9.0, 20.1, UnityProfile(), Policy()));
                RequireFailure(before, RenderSampleFailureReason.BeforeHistory);

                RenderSamplingPolicy noSingleHold = new RenderSamplingPolicy(
                    2.0, 1.0, 1e-9, AfterLatestBehavior.HoldLatest, false);
                RequireFailure(
                    VehicleRenderSampler.Sample(
                        store,
                        Request("SRC", 1UL, "AUV", 10.5, 20.1, UnityProfile(), noSingleHold)),
                    RenderSampleFailureReason.SingleSampleHoldDisabled);

                RenderPoseSample held = VehicleRenderSampler.Sample(
                    store,
                    Request("SRC", 1UL, "AUV", 10.5, 20.1, UnityProfile(), Policy()));
                RequireSuccess(held, RenderSampleMode.HeldLatest);
                RequireNear(held.BeforeSourceTimeSeconds, 10.0, "held source time");
                RequireNear(held.TargetSourceTimeSeconds, 10.5, "held target time");

                RequireFailure(
                    VehicleRenderSampler.Sample(
                        store,
                        Request("SRC", 1UL, "AUV", 11.000001, 20.1, UnityProfile(), Policy())),
                    RenderSampleFailureReason.HoldWindowExceeded);
            }
        }

        private static void VerifyExactSelection()
        {
            using (VehicleStateStore store = MakeStore())
            {
                PublishLinearSeries(store, "SRC", 1UL, "AUV", new[] { 2.0, 4.0, 6.0 });
                foreach (double time in new[] { 2.0, 4.0, 6.0 })
                {
                    RenderPoseSample result = VehicleRenderSampler.Sample(
                        store,
                        Request("SRC", 1UL, "AUV", time, 10.0, UnityProfile(), Policy()));
                    RequireSuccess(result, RenderSampleMode.Exact);
                    RequireNear(result.BeforeSourceTimeSeconds, time, "exact before metadata");
                    RequireNear(result.AfterSourceTimeSeconds, time, "exact after metadata");
                    RequireNear(result.InterpolationAlpha, 0.0, "exact alpha");
                    RequireNear(result.Position.X, time, "exact position");
                }

                RenderSamplingPolicy tolerant = new RenderSamplingPolicy(
                    4.0, 1.0, 0.01, AfterLatestBehavior.HoldLatest, true);
                RenderPoseSample withinTolerance = VehicleRenderSampler.Sample(
                    store,
                    Request("SRC", 1UL, "AUV", 4.005, 10.0, UnityProfile(), tolerant));
                RequireSuccess(withinTolerance, RenderSampleMode.Exact);
                RequireNear(withinTolerance.Position.X, 4.0, "tolerant exact was direct conversion");
            }
        }

        private static void VerifyPositionInterpolation()
        {
            using (VehicleStateStore store = MakeStore())
            {
                Publish(store, "SRC", 1UL, "AUV", -2.0, 5.0, new Vector3d(-10.0, 20.0, 1e9), Quaterniond.Identity, 1UL);
                Publish(store, "SRC", 1UL, "AUV", 2.0, 6.0, new Vector3d(30.0, -20.0, -1e9), Quaterniond.Identity, 2UL);

                RequirePositionAt(store, -2.0, new Vector3d(-10.0, 20.0, 1e9), RenderSampleMode.Exact);
                RequirePositionAt(store, 2.0, new Vector3d(30.0, -20.0, -1e9), RenderSampleMode.Exact);
                RequirePositionAt(store, 0.0, new Vector3d(10.0, 0.0, 0.0), RenderSampleMode.Interpolated);
                RequirePositionAt(store, -1.0, new Vector3d(0.0, 10.0, 5e8), RenderSampleMode.Interpolated);
            }

            VerifyProfilePositionInterpolation(
                CoordinateTransformProfiles.NedFrdToUnity(
                    "NED_N4", 2.0, AttitudeDirection.BodyToWorld, Quaterniond.Identity),
                new Vector3d(1.0, 2.0, 3.0),
                new Vector3d(5.0, 6.0, 7.0),
                new Vector3d(8.0, -10.0, 6.0));
            VerifyProfilePositionInterpolation(
                CoordinateTransformProfiles.EnuFluToUnity(
                    "ENU_N4", 1.0, AttitudeDirection.BodyToWorld, Quaterniond.Identity),
                new Vector3d(-4.0, 2.0, 6.0),
                new Vector3d(8.0, -2.0, 10.0),
                new Vector3d(2.0, 8.0, 0.0));
        }

        private static void VerifyQuaternionInterpolation()
        {
            Quaterniond identity = Quaterniond.Identity;
            Quaterniond ninety = QuaternionMath3d.FromAxisAngleRadians(new Vector3d(0.0, 1.0, 0.0), Math.PI * 0.5);
            Quaterniond midpoint = SampleOrientation(identity, ninety, 0.5);
            Quaterniond expected45 = QuaternionMath3d.FromAxisAngleRadians(new Vector3d(0.0, 1.0, 0.0), Math.PI * 0.25);
            RequireQuaternion(midpoint, expected45, "identity-to-90 midpoint");

            Quaterniond q359 = QuaternionMath3d.FromAxisAngleRadians(
                new Vector3d(0.0, 1.0, 0.0), DegreesToRadians(359.0));
            Quaterniond q1 = QuaternionMath3d.FromAxisAngleRadians(
                new Vector3d(0.0, 1.0, 0.0), DegreesToRadians(1.0));
            Quaterniond wrapMid = SampleOrientation(q359, q1, 0.5);
            RequireQuaternion(wrapMid, Quaterniond.Identity, "359-to-1 shortest path");

            Quaterniond arbitrary = QuaternionMath3d.FromAxisAngleRadians(
                new Vector3d(1.0, 2.0, -3.0), 1.1);
            RequireQuaternion(
                SampleOrientation(arbitrary, QuaternionMath3d.Negate(arbitrary), 0.37),
                arbitrary,
                "q/-q continuity");

            Quaterniond near = QuaternionMath3d.FromAxisAngleRadians(
                new Vector3d(0.0, 0.0, 1.0), 1e-7);
            Quaterniond nearMid = SampleOrientation(identity, near, 0.5);
            Require(nearMid.IsUsable, "Near-identical interpolation produced unusable quaternion.");
            RequireNear(nearMid.MagnitudeSquared, 1.0, "Near-identical quaternion unit length");

            Quaterniond combinedA = QuaternionMath3d.Multiply(
                QuaternionMath3d.FromAxisAngleRadians(new Vector3d(1.0, 0.0, 0.0), 0.4),
                QuaternionMath3d.FromAxisAngleRadians(new Vector3d(0.0, 1.0, 0.0), -0.2));
            Quaterniond combinedB = QuaternionMath3d.Multiply(
                QuaternionMath3d.FromAxisAngleRadians(new Vector3d(0.0, 0.0, 1.0), 1.2),
                QuaternionMath3d.FromAxisAngleRadians(new Vector3d(1.0, 0.0, 0.0), -0.6));
            Quaterniond combinedMid = SampleOrientation(combinedA, combinedB, 0.42);
            RequireNear(combinedMid.MagnitudeSquared, 1.0, "Combined interpolation unit length");
            Require(QuaternionMath3d.Dot(combinedA, combinedMid) >= -Tolerance,
                "Combined interpolation left the shortest-path hemisphere.");

            Require(PoseInterpolation.TrySlerp(identity, ninety, 0.0, out Quaterniond atZero),
                "Slerp alpha zero failed.");
            Require(PoseInterpolation.TrySlerp(identity, ninety, 1.0, out Quaterniond atOne),
                "Slerp alpha one failed.");
            RequireQuaternion(atZero, identity, "Slerp alpha zero");
            RequireQuaternion(atOne, ninety, "Slerp alpha one");
            Require(!PoseInterpolation.TrySlerp(identity, ninety, -0.1, out _),
                "Negative alpha was accepted.");
            Require(!PoseInterpolation.TrySlerp(identity, ninety, double.NaN, out _),
                "NaN alpha was accepted.");
        }

        private static void VerifyGapAndHistoryBoundaries()
        {
            using (VehicleStateStore store = MakeStore(capacity: 3, requireIncreasingTimestamp: false))
            {
                Publish(store, "SRC", 1UL, "AUV", 0.0, 0.0, Vector3d.Zero, Quaterniond.Identity, 1UL);
                Publish(store, "SRC", 1UL, "AUV", 2.0, 0.1, new Vector3d(2.0, 0.0, 0.0), Quaterniond.Identity, 2UL);

                RenderSamplingPolicy exactBoundary = new RenderSamplingPolicy(
                    2.0, 1.0, 0.0, AfterLatestBehavior.HoldLatest, true);
                RequireSuccess(
                    VehicleRenderSampler.Sample(
                        store,
                        Request("SRC", 1UL, "AUV", 1.0, 0.2, UnityProfile(), exactBoundary)),
                    RenderSampleMode.Interpolated);
                RenderSamplingPolicy belowBoundary = new RenderSamplingPolicy(
                    1.999999, 1.0, 0.0, AfterLatestBehavior.HoldLatest, true);
                RequireFailure(
                    VehicleRenderSampler.Sample(
                        store,
                        Request("SRC", 1UL, "AUV", 1.0, 0.2, UnityProfile(), belowBoundary)),
                    RenderSampleFailureReason.GapTooLarge);

                Publish(store, "SRC", 1UL, "AUV", 3.0, 0.2, new Vector3d(3.0, 0.0, 0.0), Quaterniond.Identity, 3UL);
                Publish(store, "SRC", 1UL, "AUV", 4.0, 0.3, new Vector3d(4.0, 0.0, 0.0), Quaterniond.Identity, 4UL);
                RequireFailure(
                    VehicleRenderSampler.Sample(
                        store,
                        Request("SRC", 1UL, "AUV", 0.5, 0.4, UnityProfile(), Policy())),
                    RenderSampleFailureReason.BeforeHistory);

                Publish(store, "SRC", 1UL, "AUV", 4.0, 0.4, new Vector3d(40.0, 0.0, 0.0), Quaterniond.Identity, 5UL);
                RequireFailure(
                    VehicleRenderSampler.Sample(
                        store,
                        Request("SRC", 1UL, "AUV", 4.0, 0.5, UnityProfile(), Policy())),
                    RenderSampleFailureReason.InvalidHistory);
            }
        }

        private static void VerifyEpochAndDiscontinuity()
        {
            using (VehicleStateStore store = MakeStore(discontinuityThreshold: 5.0))
            {
                Publish(store, "SRC", 1UL, "AUV", 0.0, 0.0, Vector3d.Zero, Quaterniond.Identity, 1UL);
                Publish(store, "SRC", 1UL, "AUV", 1.0, 0.1, new Vector3d(1.0, 0.0, 0.0), Quaterniond.Identity, 2UL);
                Publish(store, "SRC", 1UL, "AUV", 100.0, 0.2, new Vector3d(100.0, 0.0, 0.0), Quaterniond.Identity, 3UL);
                RequireFailure(
                    VehicleRenderSampler.Sample(
                        store,
                        Request("SRC", 1UL, "AUV", 1.0, 0.3, UnityProfile(), Policy())),
                    RenderSampleFailureReason.BeforeHistory);
                RequireSuccess(
                    VehicleRenderSampler.Sample(
                        store,
                        Request("SRC", 1UL, "AUV", 100.0, 0.3, UnityProfile(), Policy())),
                    RenderSampleMode.Exact);

                Publish(store, "SRC", 2UL, "AUV", 0.0, 0.4, new Vector3d(200.0, 0.0, 0.0), Quaterniond.Identity, 1UL);
                RequireFailure(
                    VehicleRenderSampler.Sample(
                        store,
                        Request("SRC", 1UL, "AUV", 100.0, 0.5, UnityProfile(), Policy())),
                    RenderSampleFailureReason.EpochUnavailable);
                RenderPoseSample newEpoch = VehicleRenderSampler.Sample(
                    store,
                    Request("SRC", 2UL, "AUV", 0.0, 0.5, UnityProfile(), Policy()));
                RequireSuccess(newEpoch, RenderSampleMode.Exact);
                RequireNear(newEpoch.Position.X, 200.0, "new epoch position");
            }
        }

        private static void VerifyHealthAndLifecycle()
        {
            using (VehicleStateStore store = MakeStore(timeout: 0.5))
            {
                Publish(store, "SRC", 1UL, "AUV", 10.0, 20.0, Vector3d.Zero, Quaterniond.Identity, 1UL);
                RenderSamplingPolicy rejectStale = Policy();
                RenderPoseSample timeoutBoundary = VehicleRenderSampler.Sample(
                    store,
                    Request("SRC", 1UL, "AUV", 10.0, 20.5, UnityProfile(), rejectStale));
                RequireSuccess(timeoutBoundary, RenderSampleMode.Exact);
                RenderPoseSample stale = VehicleRenderSampler.Sample(
                    store,
                    Request("SRC", 1UL, "AUV", 10.0, 20.500001, UnityProfile(), rejectStale));
                RequireFailure(stale, RenderSampleFailureReason.Stale);
                Require(stale.HasSourceHealth, "Stale result did not expose health metadata.");
                Require(stale.SourceHealth == SourceHealth.TimedOut, "Stale result did not report TimedOut.");
                RequireNear(stale.LocalDataAgeSeconds, 0.500001, "Stale local age");

                RenderSamplingPolicy rejectAfter = new RenderSamplingPolicy(
                    2.0, 1.0, 0.0, AfterLatestBehavior.Reject, true);
                RequireFailure(
                    VehicleRenderSampler.Sample(
                        store,
                        Request("SRC", 1UL, "AUV", 10.1, 20.1, UnityProfile(), rejectAfter)),
                    RenderSampleFailureReason.AfterLatestRejected);

                RequireFailure(
                    VehicleRenderSampler.Sample(
                        store,
                        Request(
                            "SRC", 1UL, "AUV", 10.0, 20.1, UnityProfile(), Policy(),
                            DataSourceStatus.Faulted)),
                    RenderSampleFailureReason.SourceFaulted);
                RequireFailure(
                    VehicleRenderSampler.Sample(
                        store,
                        Request(
                            "SRC", 1UL, "AUV", 10.0, 20.1, UnityProfile(), Policy(),
                            DataSourceStatus.Disposed)),
                    RenderSampleFailureReason.SourceUnavailable);

                Publish(store, "SRC", 1UL, "AUV", 11.0, 22.0, new Vector3d(11.0, 0.0, 0.0), Quaterniond.Identity, 2UL);
                RenderPoseSample recovered = VehicleRenderSampler.Sample(
                    store,
                    Request("SRC", 1UL, "AUV", 11.0, 22.1, UnityProfile(), Policy()));
                RequireSuccess(recovered, RenderSampleMode.Exact);
                Require(recovered.SourceHealth == SourceHealth.Healthy, "Recovery did not return healthy.");

                RequireFailure(
                    VehicleRenderSampler.Sample(
                        store,
                        Request("SRC", 1UL, "AUV", 11.0, 21.9, UnityProfile(), Policy())),
                    RenderSampleFailureReason.LocalClockRegression);
            }
        }

        private static void VerifyConversionFailurePropagation()
        {
            using (VehicleStateStore store = MakeStore())
            {
                Publish(store, "SRC", 1UL, "AUV", 0.0, 0.0, Vector3d.Zero, Quaterniond.Identity, 1UL);
                CoordinateTransformProfile badProfile = CoordinateTransformProfiles.UnityNative(
                    "", 1.0, AttitudeDirection.BodyToWorld, Quaterniond.Identity);
                RenderPoseSample result = VehicleRenderSampler.Sample(
                    store,
                    Request("SRC", 1UL, "AUV", 0.0, 0.1, badProfile, Policy()));
                RequireFailure(result, RenderSampleFailureReason.ConversionFailed);
                Require(result.ConversionError.Reason == ConversionFailureReason.InvalidProfileId,
                    "N3 conversion failure reason was not propagated.");
                Require(!string.IsNullOrWhiteSpace(result.ConversionError.Message),
                    "N3 conversion failure message was not propagated.");
            }
        }

        private static void VerifyIsolation()
        {
            using (VehicleStateStore store = MakeStore())
            {
                Publish(store, "A", 1UL, "V1", 0.0, 0.0, new Vector3d(1.0, 0.0, 0.0), Quaterniond.Identity, 1UL);
                Publish(store, "A", 1UL, "V2", 0.0, 0.0, new Vector3d(2.0, 0.0, 0.0), Quaterniond.Identity, 1UL);
                Publish(store, "B", 7UL, "V1", 0.0, 0.0, new Vector3d(3.0, 0.0, 0.0), Quaterniond.Identity, 1UL);

                RequireNear(SampleExactX(store, "A", 1UL, "V1"), 1.0, "A/V1 isolation");
                RequireNear(SampleExactX(store, "A", 1UL, "V2"), 2.0, "A/V2 isolation");
                RequireNear(SampleExactX(store, "B", 7UL, "V1"), 3.0, "B/V1 isolation");
                RequireFailure(
                    VehicleRenderSampler.Sample(
                        store,
                        Request("B", 7UL, "V2", 0.0, 0.1, UnityProfile(), Policy())),
                    RenderSampleFailureReason.NoData);
            }
        }

        private static void VerifyConcurrentSampling()
        {
            using (VehicleStateStore store = MakeStore(capacity: 32, timeout: 10000.0))
            {
                var errors = new List<Exception>();
                object errorGate = new object();
                var start = new ManualResetEventSlim(false);
                int observed = 0;

                Task writer = Task.Run(() =>
                {
                    start.Wait();
                    try
                    {
                        for (ulong sequence = 1UL; sequence <= 500UL; sequence++)
                        {
                            double value = sequence;
                            Publish(
                                store,
                                "SRC",
                                1UL,
                                "AUV",
                                value,
                                value,
                                new Vector3d(value, value * 2.0, -value),
                                Quaterniond.Identity,
                                sequence);
                        }
                    }
                    catch (Exception exception)
                    {
                        lock (errorGate) errors.Add(exception);
                    }
                });

                Task reader = Task.Run(() =>
                {
                    start.Wait();
                    try
                    {
                        RenderSamplingPolicy holdFar = new RenderSamplingPolicy(
                            10.0, 10000.0, 0.0, AfterLatestBehavior.HoldLatest, true);
                        for (int index = 0; index < 1000; index++)
                        {
                            RenderPoseSample result = VehicleRenderSampler.Sample(
                                store,
                                Request("SRC", 1UL, "AUV", 1000.0, 1000.0, UnityProfile(), holdFar));
                            if (!result.Succeeded)
                            {
                                Require(result.FailureReason == RenderSampleFailureReason.NoData,
                                    "Concurrent sampling returned " + result.FailureReason + ".");
                                continue;
                            }

                            Require(result.Mode == RenderSampleMode.HeldLatest, "Concurrent sample was not held latest.");
                            RequireNear(result.Position.Y, result.Position.X * 2.0, "Concurrent position Y coherence");
                            RequireNear(result.Position.Z, -result.Position.X, "Concurrent position Z coherence");
                            Interlocked.Increment(ref observed);
                        }
                    }
                    catch (Exception exception)
                    {
                        lock (errorGate) errors.Add(exception);
                    }
                });

                start.Set();
                Require(Task.WaitAll(new[] { writer, reader }, TimeSpan.FromSeconds(10.0)),
                    "Concurrent sampling tasks timed out.");
                Require(errors.Count == 0, errors.Count == 0 ? string.Empty : errors[0].Message);
                Require(observed > 0, "Concurrent reader never observed a sample.");

                Require(store.TryReadWindow("SRC", 1UL, "AUV", out VehicleStateWindow before),
                    "Final concurrent window missing.");
                ReceivedVehicleState[] detached = before.ToArray();
                Publish(
                    store,
                    "SRC",
                    1UL,
                    "AUV",
                    501.0,
                    501.0,
                    new Vector3d(501.0, 1002.0, -501.0),
                    Quaterniond.Identity,
                    501UL);
                Require(detached[detached.Length - 1].State.SourceTimestampSeconds == 500.0,
                    "Previously returned history mutated after a publish.");
            }
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
                "UnityEngine.Time",
                "DateTime",
                "Stopwatch",
                "Thread.Sleep",
                "UdpClient",
                "System.Net",
                "MemoryMappedFile",
                "DemoMotionController",
                "SceneManager",
                "MechanicalArm"
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

        private static void VerifyProfilePositionInterpolation(
            CoordinateTransformProfile profile,
            Vector3d first,
            Vector3d second,
            Vector3d expectedMidpoint)
        {
            using (VehicleStateStore store = MakeStore())
            {
                Publish(
                    store, "SRC", 1UL, "AUV", 0.0, 0.0, first, Quaterniond.Identity, 1UL,
                    profile.SourceWorldFrame, profile.SourceBodyFrame);
                Publish(
                    store, "SRC", 1UL, "AUV", 2.0, 0.1, second, Quaterniond.Identity, 2UL,
                    profile.SourceWorldFrame, profile.SourceBodyFrame);
                RenderPoseSample result = VehicleRenderSampler.Sample(
                    store,
                    Request("SRC", 1UL, "AUV", 1.0, 0.2, profile, Policy()));
                RequireSuccess(result, RenderSampleMode.Interpolated);
                RequireVector(result.Position, expectedMidpoint, profile.ProfileId + " midpoint");
            }
        }

        private static Quaterniond SampleOrientation(Quaterniond first, Quaterniond second, double alpha)
        {
            using (VehicleStateStore store = MakeStore())
            {
                Publish(store, "SRC", 1UL, "AUV", 0.0, 0.0, Vector3d.Zero, first, 1UL);
                Publish(store, "SRC", 1UL, "AUV", 1.0, 0.1, Vector3d.Zero, second, 2UL);
                RenderPoseSample result = VehicleRenderSampler.Sample(
                    store,
                    Request("SRC", 1UL, "AUV", alpha, 0.2, UnityProfile(), Policy()));
                RequireSuccess(result, alpha == 0.0 || alpha == 1.0
                    ? RenderSampleMode.Exact
                    : RenderSampleMode.Interpolated);
                RequireNear(result.Orientation.MagnitudeSquared, 1.0, "Sampled orientation unit length");
                return result.Orientation;
            }
        }

        private static void RequirePositionAt(
            VehicleStateStore store,
            double sourceTime,
            Vector3d expected,
            RenderSampleMode expectedMode)
        {
            RenderPoseSample result = VehicleRenderSampler.Sample(
                store,
                Request("SRC", 1UL, "AUV", sourceTime, 6.1, UnityProfile(), Policy(maxGap: 5.0)));
            RequireSuccess(result, expectedMode);
            RequireVector(result.Position, expected, "position at " + sourceTime);
        }

        private static double SampleExactX(
            VehicleStateStore store,
            string sourceId,
            ulong sourceEpoch,
            string vehicleId)
        {
            RenderPoseSample result = VehicleRenderSampler.Sample(
                store,
                Request(sourceId, sourceEpoch, vehicleId, 0.0, 0.1, UnityProfile(), Policy()));
            RequireSuccess(result, RenderSampleMode.Exact);
            return result.Position.X;
        }

        private static VehicleStateStore MakeStore(
            int capacity = 8,
            double timeout = 100.0,
            double discontinuityThreshold = double.PositiveInfinity,
            bool requireIncreasingTimestamp = true)
        {
            return new VehicleStateStore(
                new VehicleStateStorePolicy(
                    capacity,
                    requireIncreasingTimestamp: requireIncreasingTimestamp,
                    timeoutSeconds: timeout,
                    timestampDiscontinuityThresholdSeconds: discontinuityThreshold));
        }

        private static CoordinateTransformProfile UnityProfile()
        {
            return CoordinateTransformProfiles.UnityNative(
                "UNITY_N4",
                1.0,
                AttitudeDirection.BodyToWorld,
                Quaterniond.Identity);
        }

        private static RenderSamplingPolicy Policy(double maxGap = 4.0)
        {
            return new RenderSamplingPolicy(
                maxGap,
                1.0,
                1e-9,
                AfterLatestBehavior.HoldLatest,
                true);
        }

        private static RenderSampleRequest Request(
            string sourceId,
            ulong sourceEpoch,
            string vehicleId,
            double targetSourceTime,
            double localNow,
            CoordinateTransformProfile profile,
            RenderSamplingPolicy policy,
            DataSourceStatus sourceStatus = DataSourceStatus.Running)
        {
            return new RenderSampleRequest(
                sourceId,
                sourceEpoch,
                vehicleId,
                targetSourceTime,
                localNow,
                sourceStatus,
                profile,
                policy);
        }

        private static void PublishLinearSeries(
            VehicleStateStore store,
            string sourceId,
            ulong sourceEpoch,
            string vehicleId,
            double[] sourceTimes)
        {
            for (int index = 0; index < sourceTimes.Length; index++)
            {
                double time = sourceTimes[index];
                Publish(
                    store,
                    sourceId,
                    sourceEpoch,
                    vehicleId,
                    time,
                    index,
                    new Vector3d(time, 0.0, 0.0),
                    Quaterniond.Identity,
                    (ulong)(index + 1));
            }
        }

        private static void Publish(
            VehicleStateStore store,
            string sourceId,
            ulong sourceEpoch,
            string vehicleId,
            double sourceTime,
            double receivedAt,
            Vector3d position,
            Quaterniond orientation,
            ulong sequence,
            WorldFrame worldFrame = WorldFrame.UnityWorld,
            BodyFrame bodyFrame = BodyFrame.UnityBody)
        {
            var state = new VehicleState(
                vehicleId,
                VehicleType.Auv,
                sourceTime,
                sequence,
                position,
                orientation,
                Vector3d.Zero,
                Vector3d.Zero,
                Vector3d.Zero,
                VehicleStateFields.Position | VehicleStateFields.Orientation,
                worldFrame,
                bodyFrame);
            var received = new ReceivedVehicleState(
                state,
                sourceId,
                sourceEpoch,
                receivedAt,
                SequenceKind.Protocol,
                DecodeQualityFlags.None);
            PublishResult result = store.Publish(received);
            Require(result == PublishResult.Accepted, "Publish failed: " + result + ".");
        }

        private static void RequireSuccess(RenderPoseSample sample, RenderSampleMode expectedMode)
        {
            Require(sample.Succeeded,
                "Sampling failed: " + sample.FailureReason + " | " + sample.Message);
            Require(sample.Mode == expectedMode,
                "Expected mode " + expectedMode + " but was " + sample.Mode + ".");
            Require(sample.FailureReason == RenderSampleFailureReason.None,
                "Successful sample carried failure reason " + sample.FailureReason + ".");
            Require(sample.ConversionError.IsNone, "Successful sample carried a conversion error.");
        }

        private static void RequireFailure(RenderPoseSample sample, RenderSampleFailureReason expected)
        {
            Require(!sample.Succeeded, "Sampling unexpectedly succeeded as " + sample.Mode + ".");
            Require(sample.Mode == RenderSampleMode.None, "Failed sample carried mode " + sample.Mode + ".");
            Require(sample.FailureReason == expected,
                "Expected failure " + expected + " but was " + sample.FailureReason + ".");
            Require(!string.IsNullOrWhiteSpace(sample.Message), "Failure did not include a message.");
        }

        private static void RequireVector(Vector3d actual, Vector3d expected, string label)
        {
            RequireNear(actual.X, expected.X, label + " X");
            RequireNear(actual.Y, expected.Y, label + " Y");
            RequireNear(actual.Z, expected.Z, label + " Z");
        }

        private static void RequireQuaternion(Quaterniond actual, Quaterniond expected, string label)
        {
            Require(QuaternionMath3d.RepresentsSameRotation(actual, expected, Tolerance),
                label + " differs; dot=" + QuaternionMath3d.Dot(actual, expected) + ".");
        }

        private static void RequireNear(
            double actual,
            double expected,
            string label,
            double tolerance = Tolerance)
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
