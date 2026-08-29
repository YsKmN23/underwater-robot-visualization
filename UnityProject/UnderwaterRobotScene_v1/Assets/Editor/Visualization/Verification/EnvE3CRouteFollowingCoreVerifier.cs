using System;
using System.Collections.Generic;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.LocalTesting;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Constraints;
using UnderwaterRobotScene.Visualization.Sampling;
using UnderwaterRobotScene.Visualization.Transforms;
using UnityEditor;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    public sealed class EnvE3CRouteHoldConstraint : MonoBehaviour,
        IUnityPoseConstraint
    {
        public UnityPoseConstraintResult Constrain(
            in UnityPoseConstraintRequest request)
        {
            return new UnityPoseConstraintResult(
                UnityPoseConstraintDecision.HoldCurrent,
                request.Position,
                request.Rotation,
                "VerifierHold");
        }

        public void ResetObservation()
        {
        }
    }

    public sealed class EnvE3CRouteSafetyStub : MonoBehaviour,
        IUnityPoseConstraint,
        IRouteSafetyValidator
    {
        public UnityPoseConstraintResult Constrain(
            in UnityPoseConstraintRequest request)
        {
            return new UnityPoseConstraintResult(
                UnityPoseConstraintDecision.Apply,
                request.Position,
                request.Rotation,
                string.Empty);
        }

        public bool TryValidateRoute(
            ActiveRouteSnapshot candidate,
            in CoordinateTransformProfile transformProfile,
            out string error)
        {
            error = string.Empty;
            return candidate != null && candidate.VehicleType == VehicleType.Auv;
        }

        public void ResetObservation()
        {
        }
    }

    public static class EnvE3CRouteFollowingCoreVerifier
    {
        public static void RunBatch()
        {
            VerifySnapshotAndSampling();
            VerifyExecutionStateAndEpochs();
            VerifyThreeVehicleIsolationAndPolicies();
            VerifyStoreSamplerConversionDriverIntegration();
            Debug.Log("ENV_E3C_ROUTE_FOLLOWING_CORE_VERIFICATION_PASS");
        }

        public static void RunOrientationDefensiveGuardsBatch()
        {
            VerifyNonFiniteOrientationGuard();
            VerifyZeroTangentOrientationGuard();
            VerifyCollinearOrientationGuard();
            VerifyInvalidQuaternionInputGuard();
            VerifyNormalOrientationRegression();
            Debug.Log("ENV_E3D_ORIENTATION_DEFENSIVE_GUARDS_PASS");
        }

        private static void VerifyNonFiniteOrientationGuard()
        {
            RequireInvalidOperation(
                () => VehicleRouteRuntime.BuildOrientation(
                    Vector3d.Zero,
                    new Vector3d(double.NaN, 0.0, 1.0),
                    VehicleRouteOrientationPolicy.AuvThreeDimensional),
                "Cannot build route orientation from non-finite geometry.");
            RequireInvalidOperation(
                () => VehicleRouteRuntime.BuildOrientation(
                    new Vector3d(-double.MaxValue, 0.0, 0.0),
                    new Vector3d(double.MaxValue, 0.0, 0.0),
                    VehicleRouteOrientationPolicy.AuvThreeDimensional),
                "Cannot build route orientation from a zero or non-finite tangent.");
            Debug.Log(
                "ENV_E3D_ORIENTATION_NONFINITE_PASS " +
                "inputs=NaN-endpoint-and-finite-double-overflow-tangent " +
                "observed=expected-invalid-operation-exceptions");
        }

        private static void VerifyZeroTangentOrientationGuard()
        {
            var point = new Vector3d(4.0, -2.0, 7.0);
            RequireInvalidOperation(
                () => VehicleRouteRuntime.BuildOrientation(
                    point, point,
                    VehicleRouteOrientationPolicy.AuvThreeDimensional),
                "Cannot build route orientation from a zero or non-finite tangent.");
            Debug.Log(
                "ENV_E3D_ORIENTATION_ZERO_TANGENT_PASS " +
                "input=start-equals-end-(4,-2,7) " +
                "observed=expected-invalid-operation-exception");
        }

        private static void VerifyCollinearOrientationGuard()
        {
            RequireInvalidOperation(
                () => VehicleRouteRuntime.BuildOrientation(
                    Vector3d.Zero,
                    new Vector3d(0.0, 2.0, 0.0),
                    VehicleRouteOrientationPolicy.AuvThreeDimensional),
                "Cannot build route orientation from a forward/up-collinear tangent.");
            Debug.Log(
                "ENV_E3D_ORIENTATION_COLLINEARITY_PASS " +
                "input=vertical-auv-tangent-(0,2,0) " +
                "observed=expected-invalid-operation-exception");
        }

        private static void VerifyInvalidQuaternionInputGuard()
        {
            ActiveRouteSnapshot snapshot = Build(
                "AUV-ORIENTATION-GUARD", VehicleType.Auv, 1UL,
                VehicleRouteOrientationPolicy.AuvThreeDimensional,
                Vector3d.Zero, new Vector3d(0.0, 0.0, 2.0));
            var runtime = new VehicleRouteRuntime(snapshot, 1.0);
            ulong routeEpoch = runtime.RouteEpoch;
            var zeroQuaternionPose = new VehicleRoutePose(
                Vector3d.Zero, default);
            var nonFiniteQuaternionPose = new VehicleRoutePose(
                Vector3d.Zero,
                new Quaterniond(double.NaN, 0.0, 0.0, 1.0));

            Require(!runtime.EnterHold(in zeroQuaternionPose),
                "VehicleRouteRuntime accepted a zero quaternion Hold pose.");
            Require(!runtime.EnterHold(in nonFiniteQuaternionPose),
                "VehicleRouteRuntime accepted a non-finite quaternion Hold pose.");
            Require(runtime.State == VehicleRouteExecutionState.Running &&
                    runtime.RouteEpoch == routeEpoch,
                "Rejected quaternion input changed route runtime state.");
            VehicleRoutePose sampled = runtime.SampleCurrentPose();
            Require(sampled.Orientation.IsUsable,
                "Rejected quaternion input poisoned subsequent route orientation.");
            Debug.Log(
                "ENV_E3D_ORIENTATION_INVALID_QUATERNION_PASS " +
                "inputs=zero-and-NaN-hold-quaternions " +
                "observed=EnterHold-false,state-Running,routeEpoch-unchanged," +
                "subsequent-BuildOrientation-usable");
        }

        private static void VerifyNormalOrientationRegression()
        {
            var start = new Vector3d(1.0, 2.0, 3.0);
            var end = new Vector3d(5.0, 4.0, 7.0);
            Quaterniond orientation = VehicleRouteRuntime.BuildOrientation(
                start, end,
                VehicleRouteOrientationPolicy.AuvThreeDimensional);
            Require(orientation.IsUsable,
                "Legal route tangent produced an unusable orientation.");
            Require(orientation.TryNormalize(out Quaterniond normalized),
                "Legal route orientation could not be normalized.");

            Vector3 expectedForward = new Vector3(4f, 2f, 4f).normalized;
            Vector3 actualForward = Forward(normalized).normalized;
            Require(Vector3.Dot(expectedForward, actualForward) >= 0.99999f,
                "Legal route orientation does not follow its route tangent.");
            Require(Near(normalized.MagnitudeSquared, 1.0),
                "Legal route orientation is not normalized.");
            Debug.Log(
                "ENV_E3D_ORIENTATION_NORMAL_PASS " +
                "input=start-(1,2,3),end-(5,4,7) " +
                "observed=usable-normalized-tangent-aligned-orientation");
        }

        private static void VerifySnapshotAndSampling()
        {
            bool invalid = ActiveRouteSnapshotBuilder.TryBuild(
                "AUV-01", VehicleType.Auv, "invalid", 1UL,
                new[]
                {
                    new Vector3d(0, 0, 0),
                    new Vector3d(double.NaN, 0, 1)
                },
                VehicleRouteOrientationPolicy.AuvThreeDimensional,
                0.0, out _, out _);
            Require(!invalid, "Non-finite route point was accepted.");
            bool insufficient = ActiveRouteSnapshotBuilder.TryBuild(
                "AUV-01", VehicleType.Auv, "insufficient", 1UL,
                new[]
                {
                    new Vector3d(1, 2, 3),
                    new Vector3d(1, 2, 3)
                },
                VehicleRouteOrientationPolicy.AuvThreeDimensional,
                0.0, out _, out _);
            Require(!insufficient,
                "A route with fewer than two compressed points was accepted.");

            ActiveRouteSnapshot snapshot = Build(
                "AUV-01", VehicleType.Auv, 7UL,
                VehicleRouteOrientationPolicy.AuvThreeDimensional,
                new Vector3d(0, 0, 0),
                new Vector3d(0, 0, 0),
                new Vector3d(3, 0, 0),
                new Vector3d(4, 4, 0));
            Require(snapshot.WaypointCount == 3,
                "Consecutive duplicate route point was not compressed.");
            Require(snapshot.RouteVersion == 7UL,
                "Active snapshot routeVersion was not preserved.");
            Require(Near(snapshot.GetCumulativeLength(1), 3.0) &&
                    Near(snapshot.TotalLength, 3.0 + Math.Sqrt(17.0)),
                "Cumulative route length is incorrect.");

            var runtime = new VehicleRouteRuntime(snapshot, 1.0);
            runtime.Advance(3.0);
            VehicleRoutePose exact = runtime.SampleCurrentPose();
            Require(Near(exact.Position.X, 3.0) &&
                    Near(exact.Position.Y, 0.0),
                "Exact segment endpoint sampling failed.");
            runtime.Advance(2.0);
            VehicleRoutePose second = runtime.SampleCurrentPose();
            Require(Near(
                        second.Position.X,
                        3.0 + 2.0 / Math.Sqrt(17.0)) &&
                    Near(
                        second.Position.Y,
                        8.0 / Math.Sqrt(17.0)),
                "Multi-segment distance sampling failed.");
        }

        private static void VerifyExecutionStateAndEpochs()
        {
            ActiveRouteSnapshot snapshot = Build(
                "AUV-01", VehicleType.Auv, 1UL,
                VehicleRouteOrientationPolicy.AuvThreeDimensional,
                new Vector3d(0, 0, 0), new Vector3d(0, 0, 2));
            var runtime = new VehicleRouteRuntime(snapshot, 1.0);
            runtime.Advance(0.5);
            Require(Near(runtime.DistanceAlongRoute, 0.5),
                "Fixed-speed advancement failed.");
            ulong epoch = runtime.RouteEpoch;
            Require(runtime.Pause(), "Pause failed.");
            runtime.Advance(10.0);
            Require(Near(runtime.DistanceAlongRoute, 0.5) &&
                    runtime.RouteEpoch == epoch,
                "Pause advanced progress or changed route epoch.");
            Require(runtime.Resume(), "Resume failed.");
            runtime.Advance(10.0);
            Require(runtime.State == VehicleRouteExecutionState.Completed &&
                    Near(runtime.DistanceAlongRoute, snapshot.TotalLength),
                "Completion did not clamp to the exact endpoint.");
            Vector3d endpoint = runtime.SampleCurrentPose().Position;
            Require(endpoint.Equals(runtime.SampleCurrentPose().Position),
                "Completed endpoint is unstable.");
            runtime.Restart();
            Require(runtime.RouteEpoch == epoch + 1UL &&
                    runtime.State == VehicleRouteExecutionState.Running &&
                    Near(runtime.DistanceAlongRoute, 0.0),
                "Restart semantics are incorrect.");

            var sink = new RecordingSink();
            var source = new RouteFollowingSource(
                "route-source", 0.1, runtime,
                WorldFrame.UnityWorld, BodyFrame.UnityBody);
            source.Start(sink);
            source.Step(1.0);
            Require(sink.Last.State.SequenceNumber == 0UL &&
                    sink.Last.SourceEpoch == 1UL,
                "Initial sequence/source epoch is incorrect.");
            source.Step(1.1);
            Require(sink.Last.State.SequenceNumber == 1UL,
                "Sequence did not increment within a source epoch.");
            source.RestartExecution();
            source.Step(1.2);
            Require(sink.Last.SourceEpoch == 2UL &&
                    sink.Last.State.SequenceNumber == 0UL,
                "Restart did not switch source epoch and reset sequence.");
            Require(runtime.RouteEpoch == epoch + 2UL,
                "Restart did not increment route epoch.");
            source.Dispose();
        }

        private static void VerifyThreeVehicleIsolationAndPolicies()
        {
            var auv = new VehicleRouteRuntime(Build(
                "A", VehicleType.Auv, 1,
                VehicleRouteOrientationPolicy.AuvThreeDimensional,
                new Vector3d(0, 0, 0), new Vector3d(0, -2, 2)), 1.0);
            var rov = new VehicleRouteRuntime(Build(
                "R", VehicleType.Rov, 1,
                VehicleRouteOrientationPolicy.RovLevelYaw,
                new Vector3d(0, -2, 0), new Vector3d(1, -2, 1)), 1.0);
            var usv = new VehicleRouteRuntime(Build(
                "U", VehicleType.Usv, 1,
                VehicleRouteOrientationPolicy.UsvSurfaceYaw,
                new Vector3d(0, 0.18, 0), new Vector3d(1, 0.18, 1)), 1.0);
            auv.Pause();
            rov.Advance(0.5);
            usv.Advance(0.25);
            Require(Near(auv.DistanceAlongRoute, 0.0) &&
                    Near(rov.DistanceAlongRoute, 0.5) &&
                    Near(usv.DistanceAlongRoute, 0.25),
                "Per-vehicle route state is not isolated.");

            Vector3 auvForward = Forward(auv.SampleCurrentPose().Orientation);
            Vector3 rovForward = Forward(rov.SampleCurrentPose().Orientation);
            Vector3 usvForward = Forward(usv.SampleCurrentPose().Orientation);
            Require(auvForward.y < -0.1f,
                "AUV orientation did not follow route depth.");
            Require(Mathf.Abs(rovForward.y) < 0.0001f &&
                    Mathf.Abs(usvForward.y) < 0.0001f,
                "ROV/USV policies introduced pitch.");
            Require(Near(usv.SampleCurrentPose().Position.Y, 0.18),
                "USV route left its neutral surface plane.");
        }

        private static void VerifyStoreSamplerConversionDriverIntegration()
        {
            GameObject root = new GameObject("E3C_Verifier_Root");
            GameObject hostObject = new GameObject("E3C_Verifier_Host");
            GameObject driverObject = new GameObject("E3C_Verifier_Driver");
            try
            {
                var configuration =
                    hostObject.AddComponent<VehiclePoseIntegrationConfiguration>();
                configuration.ConfigureLocalTest(
                    "e3c-verifier", "AUV-VERIFY", VehicleType.Auv,
                    DeterministicVehicleStateGeneratorKind.Default,
                    new Vector3(0f, -1f, 0f),
                    0.1f, 64, 0.75f, 8, false,
                    0f, 0.25f, 0.25f, 0.000001f,
                    AfterLatestBehavior.HoldLatest, true);
                var profile =
                    driverObject.AddComponent<VehiclePoseProfileConfiguration>();
                profile.Configure(
                    "E3C_VERIFY_UNITY", CoordinateProfilePreset.UnityNative,
                    1f, AttitudeDirection.BodyToWorld,
                    SignedSemanticAxis.NegativeZ,
                    SignedSemanticAxis.PositiveY,
                    SignedSemanticAxis.PositiveX,
                    new Vector3(0f, -90f, 0f));
                var authority = root.AddComponent<VehiclePoseControlAuthority>();
                authority.Mode = VehiclePoseControlMode.PublicData;
                var host = hostObject.AddComponent<VehicleDataRuntimeHost>();
                host.Configure(configuration, profile);
                host.ConfigureSourceMode(VehicleRuntimeSourceMode.RouteFollowing);
                var driver = driverObject.AddComponent<VehiclePoseDriver>();
                var safetyStub = driverObject.AddComponent<EnvE3CRouteSafetyStub>();
                driver.Configure(host, configuration, profile, authority,
                    root.transform, safetyStub);
                host.InitializeForDiagnostics(10.0);
                host.TickForDiagnostics(10.2);
                Require(driver.TrySampleAndApply(10.2),
                    "Route source -> Store -> Sampler -> Conversion -> Driver integration failed.");
                Require(driver.HasFreshAppliedPose &&
                        driver.LastAppliedSourceEpoch > 0UL &&
                        root.transform.position.z > 0f,
                    "Driver did not apply a fresh route pose to the Movement Root.");

                ulong routeEpochBeforeHold = host.RouteEpoch;
                Require(host.TryGetActiveEpoch(out ulong sourceEpochBeforeHold),
                    "Integration route source epoch was unavailable.");
                var hold = driverObject.AddComponent<
                    EnvE3CRouteHoldConstraint>();
                driver.ConfigurePoseConstraint(hold);
                host.TickForDiagnostics(10.3);
                Require(!driver.TrySampleAndApply(10.3) &&
                        host.RouteExecutionState ==
                        VehicleRouteExecutionState.Hold &&
                        host.RouteEpoch == routeEpochBeforeHold &&
                        host.TryGetActiveEpoch(out ulong sourceEpochAfterHold) &&
                        sourceEpochAfterHold != sourceEpochBeforeHold,
                    "Driver constraint Hold feedback did not freeze the route and switch SourceEpoch.");
                host.ShutdownForDiagnostics();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(driverObject);
                UnityEngine.Object.DestroyImmediate(hostObject);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static ActiveRouteSnapshot Build(
            string id,
            VehicleType type,
            ulong version,
            VehicleRouteOrientationPolicy policy,
            params Vector3d[] points)
        {
            Require(ActiveRouteSnapshotBuilder.TryBuild(
                    id, type, "route-" + id, version, points, policy, 0.0,
                    out ActiveRouteSnapshot snapshot, out string error),
                error);
            return snapshot;
        }

        private static Vector3 Forward(Quaterniond value)
        {
            return new Quaternion(
                (float)value.X, (float)value.Y,
                (float)value.Z, (float)value.W) * Vector3.forward;
        }

        private static bool Near(double a, double b)
        {
            return Math.Abs(a - b) <= 1e-6;
        }

        private static void RequireInvalidOperation(
            Action action,
            string expectedMessage)
        {
            try
            {
                action();
            }
            catch (InvalidOperationException exception)
            {
                Require(string.Equals(
                        exception.Message, expectedMessage,
                        StringComparison.Ordinal),
                    "Unexpected orientation guard exception: " +
                    exception.Message);
                return;
            }

            throw new InvalidOperationException(
                "Expected orientation guard exception was not thrown: " +
                expectedMessage);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private sealed class RecordingSink : IStateSink
        {
            public ReceivedVehicleState Last { get; private set; }

            public PublishResult Publish(in ReceivedVehicleState sample)
            {
                Last = sample;
                return PublishResult.Accepted;
            }
        }
    }
}
