using System;
using System.Collections.Generic;
using System.Threading;
using UnderwaterRobotScene.Visualization.Data.LocalTesting;

namespace UnderwaterRobotScene.Visualization.Data
{
    public readonly struct LocalTestVehicle
    {
        public LocalTestVehicle(string vehicleId, VehicleType vehicleType, Vector3d positionOffset)
            : this(vehicleId, vehicleType, positionOffset, WorldFrame.Unknown, BodyFrame.Unknown)
        {
        }

        public LocalTestVehicle(
            string vehicleId,
            VehicleType vehicleType,
            Vector3d positionOffset,
            WorldFrame worldFrame,
            BodyFrame bodyFrame)
        {
            if (string.IsNullOrWhiteSpace(vehicleId))
            {
                throw new ArgumentException("Vehicle ID must not be empty.", nameof(vehicleId));
            }

            if (!positionOffset.IsFinite)
            {
                throw new ArgumentException("Position offset must be finite.", nameof(positionOffset));
            }

            VehicleId = vehicleId;
            VehicleType = vehicleType;
            PositionOffset = positionOffset;
            WorldFrame = worldFrame;
            BodyFrame = bodyFrame;
        }

        public string VehicleId { get; }
        public VehicleType VehicleType { get; }
        public Vector3d PositionOffset { get; }
        public WorldFrame WorldFrame { get; }
        public BodyFrame BodyFrame { get; }
    }

    public delegate VehicleState LocalTestStateEvaluator(
        LocalTestVehicle vehicle,
        ulong sampleIndex,
        double sourceTimestampSeconds);

    public sealed class LocalTestSourceConfig
    {
        private readonly LocalTestVehicle[] vehicles;

        public LocalTestSourceConfig(string sourceId, double sampleIntervalSeconds, LocalTestVehicle[] vehicles)
            : this(
                sourceId,
                sampleIntervalSeconds,
                vehicles,
                new DefaultDeterministicVehicleStateGenerator(sampleIntervalSeconds))
        {
        }

        public LocalTestSourceConfig(
            string sourceId,
            double sampleIntervalSeconds,
            LocalTestVehicle[] vehicles,
            LocalTestStateEvaluator stateEvaluator)
            : this(
                sourceId,
                sampleIntervalSeconds,
                vehicles,
                stateEvaluator == null
                    ? (IDeterministicVehicleStateGenerator)
                        new DefaultDeterministicVehicleStateGenerator(sampleIntervalSeconds)
                    : new DelegateStateGenerator(stateEvaluator))
        {
            StateEvaluator = stateEvaluator;
        }

        public LocalTestSourceConfig(
            string sourceId,
            double sampleIntervalSeconds,
            LocalTestVehicle[] vehicles,
            IDeterministicVehicleStateGenerator stateGenerator)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                throw new ArgumentException("Source ID must not be empty.", nameof(sourceId));
            }

            if (!Numeric.IsFinite(sampleIntervalSeconds) || sampleIntervalSeconds <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleIntervalSeconds),
                    "Sample interval must be finite and greater than zero.");
            }

            if (vehicles == null || vehicles.Length == 0)
            {
                throw new ArgumentException("At least one test vehicle is required.", nameof(vehicles));
            }

            if (stateGenerator == null)
            {
                throw new ArgumentNullException(nameof(stateGenerator));
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            this.vehicles = new LocalTestVehicle[vehicles.Length];
            for (int index = 0; index < vehicles.Length; index++)
            {
                LocalTestVehicle vehicle = vehicles[index];
                if (string.IsNullOrWhiteSpace(vehicle.VehicleId) || !vehicle.PositionOffset.IsFinite)
                {
                    throw new ArgumentException("Every test vehicle must be valid.", nameof(vehicles));
                }

                if (!ids.Add(vehicle.VehicleId))
                {
                    throw new ArgumentException("Test vehicle IDs must be unique.", nameof(vehicles));
                }

                this.vehicles[index] = vehicle;
            }

            SourceId = sourceId;
            SampleIntervalSeconds = sampleIntervalSeconds;
            StateGenerator = stateGenerator;
        }

        public string SourceId { get; }
        public double SampleIntervalSeconds { get; }
        public LocalTestStateEvaluator StateEvaluator { get; }
        public IDeterministicVehicleStateGenerator StateGenerator { get; }
        public int VehicleCount => vehicles.Length;

        public LocalTestVehicle GetVehicle(int index)
        {
            return vehicles[index];
        }

        private sealed class DelegateStateGenerator : IDeterministicVehicleStateGenerator
        {
            private readonly LocalTestStateEvaluator evaluator;

            public DelegateStateGenerator(LocalTestStateEvaluator evaluator)
            {
                this.evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
            }

            public VehicleState Evaluate(
                LocalTestVehicle vehicle,
                ulong sampleIndex,
                double sourceTimestampSeconds)
            {
                return evaluator(vehicle, sampleIndex, sourceTimestampSeconds);
            }
        }
    }

    public sealed class LocalTestSource : IManualStepDataSource
    {
        private readonly object gate = new object();
        private readonly LocalTestSourceConfig config;
        private IStateSink sink;
        private DataSourceStatus status;
        private DataSourceError lastError;
        private ulong sourceEpoch;
        private ulong sampleIndex;
        private ulong startCount;
        private ulong stopCount;
        private ulong attemptedSamples;
        private ulong publishedSamples;
        private ulong rejectedSamples;
        private ulong faultCount;
        private double lastPublishedAt;
        private bool stepInProgress;
        private int stepThreadId;
        private bool stopRequested;
        private bool disposed;

        public LocalTestSource(LocalTestSourceConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            status = DataSourceStatus.Stopped;
            lastError = DataSourceError.None;
        }

        public string SourceId => config.SourceId;

        public DataSourceStatus Status
        {
            get
            {
                lock (gate)
                {
                    return status;
                }
            }
        }

        public bool IsRunning => Status == DataSourceStatus.Running;

        public DataSourceError LastError
        {
            get
            {
                lock (gate)
                {
                    return lastError;
                }
            }
        }

        public void Start(IStateSink stateSink)
        {
            StartCore(stateSink, false, 0UL);
        }

        public void StartAtEpoch(IStateSink stateSink, ulong explicitSourceEpoch)
        {
            if (explicitSourceEpoch == 0UL)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(explicitSourceEpoch),
                    "An explicit SourceEpoch must be greater than zero.");
            }

            StartCore(stateSink, true, explicitSourceEpoch);
        }

        private void StartCore(
            IStateSink stateSink,
            bool hasExplicitSourceEpoch,
            ulong explicitSourceEpoch)
        {
            if (stateSink == null) throw new ArgumentNullException(nameof(stateSink));

            lock (gate)
            {
                ThrowIfDisposed();
                if (status == DataSourceStatus.Running)
                {
                    if (!ReferenceEquals(sink, stateSink))
                    {
                        throw new InvalidOperationException("A running source cannot switch sinks.");
                    }

                    return;
                }

                status = DataSourceStatus.Starting;
                sink = stateSink;
                sourceEpoch = hasExplicitSourceEpoch
                    ? explicitSourceEpoch
                    : checked(sourceEpoch + 1UL);
                sampleIndex = 0UL;
                startCount++;
                lastError = DataSourceError.None;
                status = DataSourceStatus.Running;
            }
        }

        public void Stop()
        {
            lock (gate)
            {
                if (disposed || status == DataSourceStatus.Stopped)
                {
                    return;
                }

                if (stepInProgress)
                {
                    status = DataSourceStatus.Stopping;
                    stopRequested = true;
                    if (Thread.CurrentThread.ManagedThreadId == stepThreadId)
                    {
                        return;
                    }

                    while (stepInProgress)
                    {
                        Monitor.Wait(gate);
                    }

                    return;
                }

                status = DataSourceStatus.Stopping;
                sink = null;
                stopCount++;
                status = DataSourceStatus.Stopped;
            }
        }

        public int Step()
        {
            return StepCore(false, 0.0);
        }

        public int Step(double receivedAtMonotonicSeconds)
        {
            if (!Numeric.IsFinite(receivedAtMonotonicSeconds) || receivedAtMonotonicSeconds < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(receivedAtMonotonicSeconds),
                    "Local receive time must be finite and non-negative.");
            }

            return StepCore(true, receivedAtMonotonicSeconds);
        }

        private int StepCore(bool hasExplicitReceiveTime, double explicitReceiveTime)
        {
            IStateSink target;
            ulong epoch;
            ulong index;
            lock (gate)
            {
                if (disposed || status != DataSourceStatus.Running)
                {
                    return 0;
                }

                if (stepInProgress)
                {
                    throw new InvalidOperationException("Only one manual Step may run at a time.");
                }

                stepInProgress = true;
                stepThreadId = Thread.CurrentThread.ManagedThreadId;
                target = sink;
                epoch = sourceEpoch;
                index = sampleIndex;
                sampleIndex++;
            }

            int accepted = 0;
            ulong attempted = 0UL;
            ulong rejected = 0UL;
            Exception failure = null;
            double timestamp = index * config.SampleIntervalSeconds;
            double receivedAt = hasExplicitReceiveTime ? explicitReceiveTime : timestamp;

            try
            {
                for (int vehicleIndex = 0; vehicleIndex < config.VehicleCount; vehicleIndex++)
                {
                    lock (gate)
                    {
                        if (status != DataSourceStatus.Running)
                        {
                            break;
                        }
                    }

                    LocalTestVehicle vehicle = config.GetVehicle(vehicleIndex);
                    var sample = new ReceivedVehicleState(
                        Evaluate(vehicle, index, timestamp),
                        SourceId,
                        epoch,
                        receivedAt,
                        SequenceKind.Synthetic,
                        DecodeQualityFlags.None);

                    attempted++;
                    try
                    {
                        PublishResult result = target.Publish(in sample);
                        if (result == PublishResult.Accepted)
                        {
                            accepted++;
                        }
                        else
                        {
                            rejected++;
                        }
                    }
                    catch (Exception exception)
                    {
                        rejected++;
                        failure = exception;
                        break;
                    }
                }
            }
            finally
            {
                lock (gate)
                {
                    attemptedSamples += attempted;
                    publishedSamples += (ulong)accepted;
                    rejectedSamples += rejected;
                    if (accepted > 0)
                    {
                        lastPublishedAt = receivedAt;
                    }

                    stepInProgress = false;
                    stepThreadId = 0;

                    if (disposed)
                    {
                        status = DataSourceStatus.Disposed;
                    }
                    else if (failure != null)
                    {
                        sink = null;
                        stopRequested = false;
                        faultCount++;
                        lastError = new DataSourceError(
                            "SINK_PUBLISH_FAILED",
                            failure.GetType().Name + ": " + failure.Message);
                        status = DataSourceStatus.Faulted;
                    }
                    else if (stopRequested || status == DataSourceStatus.Stopping)
                    {
                        sink = null;
                        stopRequested = false;
                        stopCount++;
                        status = DataSourceStatus.Stopped;
                    }

                    Monitor.PulseAll(gate);
                }
            }

            return accepted;
        }

        public DataSourceStatistics GetStatistics()
        {
            lock (gate)
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
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                if (stepInProgress)
                {
                    disposed = true;
                    status = DataSourceStatus.Disposed;
                    sink = null;
                    if (Thread.CurrentThread.ManagedThreadId != stepThreadId)
                    {
                        while (stepInProgress)
                        {
                            Monitor.Wait(gate);
                        }
                    }

                    return;
                }

                disposed = true;
                sink = null;
                status = DataSourceStatus.Disposed;
            }
        }

        private VehicleState Evaluate(LocalTestVehicle vehicle, ulong index, double timestamp)
        {
            return config.StateGenerator.Evaluate(vehicle, index, timestamp);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(LocalTestSource));
            }
        }
    }
}
