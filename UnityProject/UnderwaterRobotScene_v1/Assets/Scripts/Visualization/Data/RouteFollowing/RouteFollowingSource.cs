using System;

namespace UnderwaterRobotScene.Visualization.Data.RouteFollowing
{
    public sealed class RouteFollowingSource : IManualStepDataSource
    {
        private readonly string sourceId;
        private readonly double sampleIntervalSeconds;
        private readonly WorldFrame worldFrame;
        private readonly BodyFrame bodyFrame;
        private IStateSink sink;
        private DataSourceStatus status;
        private DataSourceError lastError;
        private ulong sourceEpoch;
        private ulong sequence;
        private ulong startCount;
        private ulong stopCount;
        private ulong attemptedSamples;
        private ulong publishedSamples;
        private ulong rejectedSamples;
        private ulong faultCount;
        private double lastPublishedAt;
        private bool disposed;

        public RouteFollowingSource(
            string configuredSourceId,
            double configuredSampleIntervalSeconds,
            VehicleRouteRuntime runtime,
            WorldFrame configuredWorldFrame,
            BodyFrame configuredBodyFrame)
        {
            if (string.IsNullOrWhiteSpace(configuredSourceId))
                throw new ArgumentException("Source ID is required.", nameof(configuredSourceId));
            if (!Numeric.IsFinite(configuredSampleIntervalSeconds) ||
                configuredSampleIntervalSeconds <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(configuredSampleIntervalSeconds));
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            if (configuredWorldFrame == WorldFrame.Unknown ||
                configuredBodyFrame == BodyFrame.Unknown)
                throw new ArgumentException("Explicit source frames are required.");

            sourceId = configuredSourceId;
            sampleIntervalSeconds = configuredSampleIntervalSeconds;
            worldFrame = configuredWorldFrame;
            bodyFrame = configuredBodyFrame;
            status = DataSourceStatus.Stopped;
            lastError = DataSourceError.None;
        }

        public string SourceId => sourceId;
        public DataSourceStatus Status => status;
        public bool IsRunning => status == DataSourceStatus.Running;
        public DataSourceError LastError => lastError;
        public VehicleRouteRuntime Runtime { get; }
        public ulong SourceEpoch => sourceEpoch;
        public ulong NextSequenceNumber => sequence;

        public void Start(IStateSink stateSink)
        {
            StartCore(stateSink, false, 0UL);
        }

        public void StartAtEpoch(IStateSink stateSink, ulong explicitSourceEpoch)
        {
            if (explicitSourceEpoch == 0UL)
                throw new ArgumentOutOfRangeException(
                    nameof(explicitSourceEpoch),
                    "An explicit SourceEpoch must be greater than zero.");
            StartCore(stateSink, true, explicitSourceEpoch);
        }

        private void StartCore(
            IStateSink stateSink,
            bool hasExplicitSourceEpoch,
            ulong explicitSourceEpoch)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(RouteFollowingSource));
            if (stateSink == null)
                throw new ArgumentNullException(nameof(stateSink));
            if (IsRunning)
            {
                if (!ReferenceEquals(sink, stateSink))
                    throw new InvalidOperationException("A running source cannot switch sinks.");
                return;
            }

            sink = stateSink;
            sourceEpoch = hasExplicitSourceEpoch
                ? explicitSourceEpoch
                : checked(sourceEpoch + 1UL);
            sequence = 0UL;
            startCount++;
            lastError = DataSourceError.None;
            status = DataSourceStatus.Running;
        }

        public void Stop()
        {
            if (disposed || status == DataSourceStatus.Stopped)
                return;
            sink = null;
            stopCount++;
            status = DataSourceStatus.Stopped;
        }

        public int Step(double receivedAtMonotonicSeconds)
        {
            if (!Numeric.IsFinite(receivedAtMonotonicSeconds) ||
                receivedAtMonotonicSeconds < 0.0)
                throw new ArgumentOutOfRangeException(nameof(receivedAtMonotonicSeconds));
            if (!IsRunning)
                return 0;

            ulong currentSequence = sequence++;
            double sourceTimestamp = currentSequence * sampleIntervalSeconds;
            VehicleRoutePose pose = Runtime.SampleCurrentPose();
            Vector3d velocity = VelocityForCurrentState();
            var state = new VehicleState(
                Runtime.ActiveSnapshot.VehicleId,
                Runtime.ActiveSnapshot.VehicleType,
                sourceTimestamp,
                currentSequence,
                pose.Position,
                pose.Orientation,
                velocity,
                Vector3d.Zero,
                Vector3d.Zero,
                VehicleStateFields.Position |
                VehicleStateFields.Orientation |
                VehicleStateFields.LinearVelocity,
                worldFrame,
                bodyFrame);
            var received = new ReceivedVehicleState(
                state,
                sourceId,
                sourceEpoch,
                receivedAtMonotonicSeconds,
                SequenceKind.Synthetic,
                DecodeQualityFlags.None);

            attemptedSamples++;
            try
            {
                PublishResult result = sink.Publish(in received);
                if (result == PublishResult.Accepted)
                {
                    publishedSamples++;
                    lastPublishedAt = receivedAtMonotonicSeconds;
                    Runtime.Advance(sampleIntervalSeconds);
                    return 1;
                }

                rejectedSamples++;
                return 0;
            }
            catch (Exception exception)
            {
                rejectedSamples++;
                faultCount++;
                lastError = new DataSourceError(
                    "SINK_PUBLISH_FAILED",
                    exception.GetType().Name + ": " + exception.Message);
                sink = null;
                status = DataSourceStatus.Faulted;
                return 0;
            }
        }

        public void RestartExecution()
        {
            Runtime.Restart();
            SwitchEpoch();
        }

        public bool TryActivateWhenNotRunning(
            ActiveRouteSnapshot snapshot,
            out string error)
        {
            if (!Runtime.TryActivateWhenNotRunning(snapshot, out error))
                return false;
            SwitchEpoch();
            return true;
        }

        public bool TryPublishRunningReplan(
            ActiveRouteSnapshot snapshot,
            in VehicleRoutePose acceptedPose,
            double receivedAtMonotonicSeconds,
            out string error)
        {
            if (!Numeric.IsFinite(receivedAtMonotonicSeconds) ||
                receivedAtMonotonicSeconds < 0.0)
            {
                error = "Replan publication time must be finite and non-negative.";
                return false;
            }
            if (!IsRunning || sink == null)
            {
                error = "The route source must be running to publish an atomic replan.";
                return false;
            }
            if (!Runtime.TryValidateRunningReplacement(
                    snapshot, in acceptedPose, out error))
            {
                return false;
            }
            if (sourceEpoch == ulong.MaxValue)
            {
                error = "Source epoch is exhausted.";
                return false;
            }

            ulong nextEpoch = sourceEpoch + 1UL;
            if (!Runtime.TryNormalizeAcceptedPose(
                    in acceptedPose,
                    out VehicleRoutePose normalizedAcceptedPose))
            {
                error = "The Driver accepted pose is invalid for the route orientation policy.";
                return false;
            }
            VehicleState firstState = BuildState(
                snapshot, in normalizedAcceptedPose, 0.0, 0UL, Vector3d.Zero);
            var firstReceived = new ReceivedVehicleState(
                firstState,
                sourceId,
                nextEpoch,
                receivedAtMonotonicSeconds,
                SequenceKind.Synthetic,
                DecodeQualityFlags.None);

            attemptedSamples++;
            PublishResult result;
            try
            {
                result = sink.Publish(in firstReceived);
            }
            catch (Exception exception)
            {
                rejectedSamples++;
                error = "Atomic replan publication failed: " +
                    exception.GetType().Name + ": " + exception.Message;
                return false;
            }
            if (result != PublishResult.Accepted)
            {
                rejectedSamples++;
                error = "Atomic replan publication was rejected by the Store: " + result + ".";
                return false;
            }

            Runtime.CommitRunningReplacement(snapshot, in acceptedPose);
            sourceEpoch = nextEpoch;
            sequence = 1UL;
            publishedSamples++;
            lastPublishedAt = receivedAtMonotonicSeconds;
            lastError = DataSourceError.None;
            error = string.Empty;
            return true;
        }

        public bool EnterConstraintHold(in VehicleRoutePose acceptedPose)
        {
            if (!Runtime.EnterHold(in acceptedPose))
                return false;
            SwitchEpoch();
            return true;
        }

        public void SwitchEpoch()
        {
            if (!IsRunning)
                return;
            sourceEpoch++;
            sequence = 0UL;
        }

        public DataSourceStatistics GetStatistics()
        {
            return new DataSourceStatistics(
                startCount,
                stopCount,
                attemptedSamples,
                publishedSamples,
                rejectedSamples,
                faultCount,
                lastPublishedAt);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            Stop();
            disposed = true;
            status = DataSourceStatus.Disposed;
        }

        private Vector3d VelocityForCurrentState()
        {
            if (Runtime.State != VehicleRouteExecutionState.Running)
                return Vector3d.Zero;
            VehicleRoutePose current = Runtime.SampleCurrentPose();
            double probe = Math.Min(
                Runtime.ActiveSnapshot.TotalLength,
                Runtime.DistanceAlongRoute + 0.001);
            double original = Runtime.DistanceAlongRoute;
            if (probe <= original)
                return Vector3d.Zero;

            int segment = 0;
            while (segment + 1 < Runtime.ActiveSnapshot.WaypointCount - 1 &&
                   Runtime.ActiveSnapshot.GetCumulativeLength(segment + 1) <= original)
                segment++;
            Vector3d a = Runtime.ActiveSnapshot.GetWaypoint(segment);
            Vector3d b = Runtime.ActiveSnapshot.GetWaypoint(segment + 1);
            double x = b.X - a.X;
            double y = b.Y - a.Y;
            double z = b.Z - a.Z;
            if (Runtime.ActiveSnapshot.OrientationPolicy ==
                VehicleRouteOrientationPolicy.UsvSurfaceYaw)
                y = 0.0;
            double length = Math.Sqrt(x * x + y * y + z * z);
            if (length <= 0.0 || !current.Position.IsFinite)
                return Vector3d.Zero;
            double speed = Runtime.CruiseSpeedMetersPerSecond / length;
            return new Vector3d(x * speed, y * speed, z * speed);
        }

        private VehicleState BuildState(
            ActiveRouteSnapshot snapshot,
            in VehicleRoutePose pose,
            double sourceTimestamp,
            ulong stateSequence,
            Vector3d velocity)
        {
            return new VehicleState(
                snapshot.VehicleId,
                snapshot.VehicleType,
                sourceTimestamp,
                stateSequence,
                pose.Position,
                pose.Orientation,
                velocity,
                Vector3d.Zero,
                Vector3d.Zero,
                VehicleStateFields.Position |
                VehicleStateFields.Orientation |
                VehicleStateFields.LinearVelocity,
                worldFrame,
                bodyFrame);
        }
    }
}
