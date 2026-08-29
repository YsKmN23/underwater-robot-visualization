using System;
using System.Collections.Generic;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Sampling;
using UnderwaterRobotScene.Visualization.Transforms;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    public static class EnvE3CAtomicRuntimeReplanningVerifier
    {
        public static void RunBatch()
        {
            VerifyAtomicSuccessAndOldBufferRetirement();
            VerifyFailureHasNoSideEffects();
            VerifyVehiclePolicies();
            VerifyThreeVehicleIsolation();
            Debug.Log("ENV_E3C_ATOMIC_RUNTIME_REPLANNING_VERIFICATION_PASS");
        }

        private static void VerifyAtomicSuccessAndOldBufferRetirement()
        {
            ActiveRouteSnapshot active = Build(
                "AUV-ATOMIC", VehicleType.Auv, 4UL,
                VehicleRouteOrientationPolicy.AuvThreeDimensional,
                new Vector3d(0, -2, 0), new Vector3d(0, -2, 8));
            var runtime = new VehicleRouteRuntime(active, 1.0);
            using (var store = Store())
            using (var source = new RouteFollowingSource(
                       "atomic-source", 0.1, runtime,
                       WorldFrame.UnityWorld, BodyFrame.UnityBody))
            {
                source.Start(store);
                Require(source.Step(0.0) == 1, "Initial route sample was not accepted.");
                ulong oldSourceEpoch = source.SourceEpoch;
                ulong oldRouteEpoch = runtime.RouteEpoch;
                VehicleRoutePose accepted = runtime.SampleCurrentPose();
                var draft = new List<Vector3d>
                {
                    new Vector3d(30, -6, 30),
                    new Vector3d(36, -7, 35)
                };
                Require(AtomicRouteReplanCandidate.TryBuild(
                        active, draft, in accepted, 0.5,
                        out ActiveRouteSnapshot candidate, out string buildError),
                    buildError);
                Require(candidate.RouteVersion == active.RouteVersion + 1UL &&
                        Near(candidate.GetWaypoint(0), accepted.Position) &&
                        Near(candidate.GetWaypoint(1), draft[0]),
                    "Candidate did not prepend the accepted position before the Draft.");

                Require(source.TryPublishRunningReplan(
                        candidate, in accepted, 0.5, out string publishError),
                    publishError);
                Require(ReferenceEquals(runtime.ActiveSnapshot, candidate) &&
                        runtime.RouteVersion == active.RouteVersion + 1UL &&
                        runtime.RouteEpoch == oldRouteEpoch + 1UL &&
                        source.SourceEpoch == oldSourceEpoch + 1UL &&
                        source.NextSequenceNumber == 1UL &&
                        Near(runtime.DistanceAlongRoute, 0.0) &&
                        runtime.State == VehicleRouteExecutionState.Running,
                    "Atomic publication did not switch route/version/epochs/sequence coherently.");
                VehicleRoutePose firstRuntimePose = runtime.SampleCurrentPose();
                Require(Near(firstRuntimePose.Position, accepted.Position) &&
                        Near(firstRuntimePose.Orientation, accepted.Orientation),
                    "Runtime first pose did not preserve the accepted pose.");

                Require(store.TryGetActiveEpoch(source.SourceId, out ulong activeEpoch) &&
                        activeEpoch == source.SourceEpoch,
                    "Store did not switch to the new SourceEpoch.");
                Require(!store.TryReadLatest(
                            source.SourceId, oldSourceEpoch, active.VehicleId, out _),
                    "Store retained a readable retired SourceEpoch buffer.");
                Require(store.TryReadLatest(
                            source.SourceId, source.SourceEpoch, active.VehicleId,
                            out ReceivedVehicleState first) &&
                        first.State.SequenceNumber == 0UL &&
                        Near(first.State.Position, accepted.Position) &&
                        Near(first.State.Orientation, accepted.Orientation),
                    "New SourceEpoch sequence zero was not the accepted pose.");

                RenderPoseSample oldSample = VehicleRenderSampler.Sample(
                    store,
                    Request(source.SourceId, oldSourceEpoch, active.VehicleId, 0.0, 0.5));
                Require(!oldSample.Succeeded &&
                        oldSample.FailureReason == RenderSampleFailureReason.EpochUnavailable,
                    "Sampler accepted a retired SourceEpoch.");
                RenderPoseSample newSample = VehicleRenderSampler.Sample(
                    store,
                    Request(source.SourceId, source.SourceEpoch, active.VehicleId, 0.0, 0.5));
                Require(newSample.Succeeded && Near(newSample.Position, accepted.Position),
                    "Sampler did not expose the new epoch accepted-pose sample.");

                Require(source.Step(0.6) == 1 && source.NextSequenceNumber == 2UL,
                    "New SourceEpoch sequence did not continue from one.");
                Require(runtime.DistanceAlongRoute > 0.0,
                    "Runtime did not advance from the connection start after the next tick.");
            }
        }

        private static void VerifyFailureHasNoSideEffects()
        {
            ActiveRouteSnapshot active = Build(
                "AUV-FAIL", VehicleType.Auv, 2UL,
                VehicleRouteOrientationPolicy.AuvThreeDimensional,
                new Vector3d(0, -1, 0), new Vector3d(0, -1, 4));
            var runtime = new VehicleRouteRuntime(active, 0.5);
            using (var store = Store())
            using (var source = new RouteFollowingSource(
                       "failure-source", 0.1, runtime,
                       WorldFrame.UnityWorld, BodyFrame.UnityBody))
            {
                source.Start(store);
                Require(source.Step(0.0) == 1, "Failure harness did not publish initial data.");
                ActiveRouteSnapshot beforeSnapshot = runtime.ActiveSnapshot;
                double beforeDistance = runtime.DistanceAlongRoute;
                ulong beforeRouteEpoch = runtime.RouteEpoch;
                ulong beforeSourceEpoch = source.SourceEpoch;
                ulong beforeSequence = source.NextSequenceNumber;
                VehicleStateStoreStatistics beforeStore = store.GetStatistics();
                VehicleRoutePose accepted = runtime.SampleCurrentPose();

                Require(!AtomicRouteReplanCandidate.TryBuild(
                        active,
                        new[] { new Vector3d(double.NaN, 0, 0) },
                        in accepted,
                        0.2,
                        out _,
                        out string invalidError) &&
                        !string.IsNullOrWhiteSpace(invalidError),
                    "Invalid Draft unexpectedly produced a candidate.");
                ActiveRouteSnapshot mismatched = Build(
                    "OTHER", VehicleType.Auv, 3UL,
                    VehicleRouteOrientationPolicy.AuvThreeDimensional,
                    accepted.Position, new Vector3d(2, -1, 2));
                Require(!source.TryPublishRunningReplan(
                        mismatched, in accepted, 0.2, out string rejectedError) &&
                        !string.IsNullOrWhiteSpace(rejectedError),
                    "Mismatched candidate unexpectedly published.");
                VehicleStateStoreStatistics afterStore = store.GetStatistics();
                Require(ReferenceEquals(runtime.ActiveSnapshot, beforeSnapshot) &&
                        Near(runtime.DistanceAlongRoute, beforeDistance) &&
                        runtime.RouteEpoch == beforeRouteEpoch &&
                        source.SourceEpoch == beforeSourceEpoch &&
                        source.NextSequenceNumber == beforeSequence &&
                        afterStore.AcceptedSamples == beforeStore.AcceptedSamples &&
                        afterStore.EpochTransitions == beforeStore.EpochTransitions,
                    "Failed publication changed route progress, identities, sequence, or Store.");
            }
        }

        private static void VerifyVehiclePolicies()
        {
            VehicleRoutePose usvAccepted = new VehicleRoutePose(
                new Vector3d(2, 0.35, 3), Quaterniond.Identity);
            ActiveRouteSnapshot usv = Build(
                "USV-POLICY", VehicleType.Usv, 1UL,
                VehicleRouteOrientationPolicy.UsvSurfaceYaw,
                new Vector3d(0, 0.35, 0), new Vector3d(4, 0.35, 0));
            Require(AtomicRouteReplanCandidate.TryBuild(
                    usv,
                    new[] { new Vector3d(7, 99, 8), new Vector3d(9, -99, 10) },
                    in usvAccepted,
                    1.0,
                    out ActiveRouteSnapshot usvCandidate,
                    out string usvError),
                usvError);
            for (int index = 0; index < usvCandidate.WaypointCount; index++)
            {
                Require(Near(usvCandidate.GetWaypoint(index).Y, usvAccepted.Position.Y),
                    "USV candidate retained non-business-root height.");
            }

            VehicleRoutePose rovAccepted = new VehicleRoutePose(
                new Vector3d(1, -3.25, 1), Quaterniond.Identity);
            ActiveRouteSnapshot rov = Build(
                "ROV-POLICY", VehicleType.Rov, 1UL,
                VehicleRouteOrientationPolicy.RovLevelYaw,
                new Vector3d(0, -3, 0), new Vector3d(4, -3, 0));
            Require(AtomicRouteReplanCandidate.TryBuild(
                    rov,
                    new[]
                    {
                        new Vector3d(1, -2, 1),
                        new Vector3d(5, -3, 4)
                    },
                    in rovAccepted,
                    1.0,
                    out ActiveRouteSnapshot rovCandidate,
                    out string rovError),
                rovError);
            Require(Near(rovCandidate.GetWaypoint(0), rovAccepted.Position) &&
                    rovCandidate.WaypointCount == 3 &&
                    Near(rovCandidate.GetWaypoint(1),
                        new Vector3d(1, -2, 1)),
                "ROV policy did not preserve accepted height and the vertical-only XYZ-distinct waypoint.");
        }

        private static void VerifyThreeVehicleIsolation()
        {
            using (var auv = new Harness("ISO-A", VehicleType.Auv,
                       VehicleRouteOrientationPolicy.AuvThreeDimensional, -2.0))
            using (var rov = new Harness("ISO-R", VehicleType.Rov,
                       VehicleRouteOrientationPolicy.RovLevelYaw, -3.0))
            using (var usv = new Harness("ISO-U", VehicleType.Usv,
                       VehicleRouteOrientationPolicy.UsvSurfaceYaw, 0.2))
            {
                ulong rovVersion = rov.Runtime.RouteVersion;
                ulong rovRouteEpoch = rov.Runtime.RouteEpoch;
                ulong rovSourceEpoch = rov.Source.SourceEpoch;
                double rovProgress = rov.Runtime.DistanceAlongRoute;
                ulong usvVersion = usv.Runtime.RouteVersion;
                ulong usvRouteEpoch = usv.Runtime.RouteEpoch;
                ulong usvSourceEpoch = usv.Source.SourceEpoch;
                double usvProgress = usv.Runtime.DistanceAlongRoute;

                VehicleRoutePose accepted = auv.Runtime.SampleCurrentPose();
                Require(AtomicRouteReplanCandidate.TryBuild(
                        auv.Runtime.ActiveSnapshot,
                        new[] { new Vector3d(8, -4, 8), new Vector3d(10, -5, 10) },
                        in accepted,
                        0.5,
                        out ActiveRouteSnapshot candidate,
                        out string buildError),
                    buildError);
                Require(auv.Source.TryPublishRunningReplan(
                        candidate, in accepted, 0.5, out string publishError),
                    publishError);
                Require(rov.Runtime.RouteVersion == rovVersion &&
                        rov.Runtime.RouteEpoch == rovRouteEpoch &&
                        rov.Source.SourceEpoch == rovSourceEpoch &&
                        Near(rov.Runtime.DistanceAlongRoute, rovProgress) &&
                        usv.Runtime.RouteVersion == usvVersion &&
                        usv.Runtime.RouteEpoch == usvRouteEpoch &&
                        usv.Source.SourceEpoch == usvSourceEpoch &&
                        Near(usv.Runtime.DistanceAlongRoute, usvProgress),
                    "Replanning one vehicle changed another vehicle runtime.");
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

        private static VehicleStateStore Store()
        {
            return new VehicleStateStore(new VehicleStateStorePolicy(
                capacityPerVehicle: 16,
                timeoutSeconds: 2.0));
        }

        private static RenderSampleRequest Request(
            string sourceId,
            ulong epoch,
            string vehicleId,
            double sourceTime,
            double localNow)
        {
            CoordinateTransformProfile profile = CoordinateTransformProfiles.UnityNative(
                "E3C_B3_VERIFY", 1.0,
                AttitudeDirection.BodyToWorld, Quaterniond.Identity);
            var policy = new RenderSamplingPolicy(
                1.0, 1.0, 1e-9, AfterLatestBehavior.HoldLatest, true);
            return new RenderSampleRequest(
                sourceId, epoch, vehicleId, sourceTime, localNow,
                DataSourceStatus.Running, profile, policy);
        }

        private static bool Near(Vector3d a, Vector3d b)
        {
            return Near(a.X, b.X) && Near(a.Y, b.Y) && Near(a.Z, b.Z);
        }

        private static bool Near(Quaterniond a, Quaterniond b)
        {
            double dot = Math.Abs(a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W);
            return Math.Abs(1.0 - dot) <= 1e-6;
        }

        private static bool Near(double a, double b)
        {
            return Math.Abs(a - b) <= 1e-6;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class Harness : IDisposable
        {
            public Harness(
                string id,
                VehicleType type,
                VehicleRouteOrientationPolicy policy,
                double height)
            {
                ActiveRouteSnapshot snapshot = Build(
                    id, type, 1UL, policy,
                    new Vector3d(0, height, 0), new Vector3d(0, height, 6));
                Runtime = new VehicleRouteRuntime(snapshot, 1.0);
                Store = EnvE3CAtomicRuntimeReplanningVerifier.Store();
                Source = new RouteFollowingSource(
                    "source-" + id, 0.1, Runtime,
                    WorldFrame.UnityWorld, BodyFrame.UnityBody);
                Source.Start(Store);
                Require(Source.Step(0.0) == 1, "Isolation harness did not start.");
            }

            public VehicleRouteRuntime Runtime { get; }
            public VehicleStateStore Store { get; }
            public RouteFollowingSource Source { get; }

            public void Dispose()
            {
                Source.Dispose();
                Store.Dispose();
            }
        }
    }
}
