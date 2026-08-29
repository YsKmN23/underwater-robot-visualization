using System;
using System.Collections.Generic;

namespace UnderwaterRobotScene.Visualization.Data
{
    public sealed class VehicleStateStore : IStateSink, IDisposable
    {
        private readonly object gate = new object();
        private readonly VehicleStateStorePolicy policy;
        private readonly Dictionary<VehicleKey, VehicleChannel> channels =
            new Dictionary<VehicleKey, VehicleChannel>();
        private readonly Dictionary<string, ulong> activeEpochBySource =
            new Dictionary<string, ulong>(StringComparer.Ordinal);
        private ulong acceptedSamples;
        private ulong invalidSamples;
        private ulong retiredEpochSamples;
        private ulong epochTransitions;
        private bool disposed;

        public VehicleStateStore(VehicleStateStorePolicy policy)
        {
            if (policy.CapacityPerVehicle < 2)
            {
                throw new ArgumentException("Store policy capacity must be at least two.", nameof(policy));
            }

            this.policy = policy;
        }

        public PublishResult Publish(in ReceivedVehicleState sample)
        {
            lock (gate)
            {
                if (disposed)
                {
                    return PublishResult.StoreDisposed;
                }

                if (!sample.IsStructurallyValid)
                {
                    invalidSamples++;
                    return PublishResult.InvalidSample;
                }

                if (activeEpochBySource.TryGetValue(sample.SourceId, out ulong activeEpoch))
                {
                    if (sample.SourceEpoch < activeEpoch)
                    {
                        retiredEpochSamples++;
                        return PublishResult.RetiredEpoch;
                    }

                    if (sample.SourceEpoch > activeEpoch)
                    {
                        RetireSourceEpochs(sample.SourceId, sample.SourceEpoch);
                        activeEpochBySource[sample.SourceId] = sample.SourceEpoch;
                        epochTransitions++;
                    }
                }
                else
                {
                    activeEpochBySource.Add(sample.SourceId, sample.SourceEpoch);
                }

                var key = new VehicleKey(sample.SourceId, sample.SourceEpoch, sample.State.VehicleId);
                if (!channels.TryGetValue(key, out VehicleChannel channel))
                {
                    channel = new VehicleChannel(policy.CapacityPerVehicle);
                    channels.Add(key, channel);
                }

                if (channel.Count > 0)
                {
                    ReceivedVehicleState latest = channel.Latest;
                    ulong sequence = sample.State.SequenceNumber;
                    ulong latestSequence = latest.State.SequenceNumber;

                    if (policy.RejectDuplicateSequence && sequence == latestSequence)
                    {
                        if (latest.State.Equals(sample.State))
                        {
                            channel.DuplicateSamples++;
                            return PublishResult.DuplicateSequence;
                        }

                        channel.ConflictingDuplicateSamples++;
                        return PublishResult.ConflictingDuplicate;
                    }

                    if (policy.RejectOutOfOrderSequence && sequence < latestSequence)
                    {
                        channel.OutOfOrderSamples++;
                        return PublishResult.OutOfOrderSequence;
                    }

                    if (policy.RequireIncreasingTimestamp &&
                        sample.State.SourceTimestampSeconds <= latest.State.SourceTimestampSeconds)
                    {
                        channel.NonIncreasingTimestampSamples++;
                        return PublishResult.NonIncreasingTimestamp;
                    }

                    if (policy.RequireMonotonicReceiveTime &&
                        sample.ReceivedAtMonotonicSeconds < latest.ReceivedAtMonotonicSeconds)
                    {
                        channel.LocalClockRegressionSamples++;
                        return PublishResult.LocalClockRegression;
                    }

                    bool isDiscontinuity =
                        sample.State.SourceTimestampSeconds - latest.State.SourceTimestampSeconds >
                        policy.TimestampDiscontinuityThresholdSeconds;
                    if (isDiscontinuity)
                    {
                        channel.ClearHistory();
                        channel.DiscontinuityResets++;
                    }
                    else if (sequence > latestSequence && latestSequence != ulong.MaxValue)
                    {
                        ulong nextExpected = latestSequence + 1UL;
                        if (sequence > nextExpected)
                        {
                            channel.MissingSequenceCount += sequence - nextExpected;
                        }
                    }
                }

                channel.Append(sample);
                channel.AcceptedSamples++;
                acceptedSamples++;
                return PublishResult.Accepted;
            }
        }

        public bool TryReadLatest(
            string sourceId,
            ulong sourceEpoch,
            string vehicleId,
            out ReceivedVehicleState sample)
        {
            lock (gate)
            {
                if (!disposed &&
                    TryGetChannel(sourceId, sourceEpoch, vehicleId, out VehicleChannel channel) &&
                    channel.Count > 0)
                {
                    sample = channel.Latest;
                    return true;
                }

                sample = default;
                return false;
            }
        }

        public bool TryGetActiveEpoch(string sourceId, out ulong sourceEpoch)
        {
            lock (gate)
            {
                if (!disposed &&
                    sourceId != null &&
                    activeEpochBySource.TryGetValue(sourceId, out sourceEpoch))
                {
                    return true;
                }

                sourceEpoch = default;
                return false;
            }
        }

        public bool TryReadWindow(
            string sourceId,
            ulong sourceEpoch,
            string vehicleId,
            out VehicleStateWindow window)
        {
            lock (gate)
            {
                if (!disposed &&
                    TryGetChannel(sourceId, sourceEpoch, vehicleId, out VehicleChannel channel) &&
                    channel.Count > 0)
                {
                    window = new VehicleStateWindow(channel.CopyInLogicalOrder());
                    return true;
                }

                window = null;
                return false;
            }
        }

        public bool TryReadSnapshot(
            string sourceId,
            ulong sourceEpoch,
            string vehicleId,
            double evaluatedAtMonotonicSeconds,
            out VehicleSnapshot snapshot)
        {
            if (!Numeric.IsFinite(evaluatedAtMonotonicSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(evaluatedAtMonotonicSeconds),
                    "Snapshot time must be finite.");
            }

            lock (gate)
            {
                if (!disposed &&
                    TryGetChannel(sourceId, sourceEpoch, vehicleId, out VehicleChannel channel) &&
                    channel.Count > 0)
                {
                    ReceivedVehicleState latest = channel.Latest;
                    if (evaluatedAtMonotonicSeconds < latest.ReceivedAtMonotonicSeconds)
                    {
                        throw new ArgumentOutOfRangeException(nameof(evaluatedAtMonotonicSeconds),
                            "Snapshot time must not precede the latest local receive time.");
                    }

                    double age = evaluatedAtMonotonicSeconds - latest.ReceivedAtMonotonicSeconds;
                    SourceHealth health = age > policy.TimeoutSeconds
                        ? SourceHealth.TimedOut
                        : SourceHealth.Healthy;
                    snapshot = new VehicleSnapshot(
                        new VehicleStateWindow(channel.CopyInLogicalOrder()),
                        evaluatedAtMonotonicSeconds,
                        age,
                        health);
                    return true;
                }

                snapshot = default;
                return false;
            }
        }

        public VehicleStateStoreStatistics GetStatistics()
        {
            lock (gate)
            {
                return new VehicleStateStoreStatistics(
                    acceptedSamples,
                    invalidSamples,
                    retiredEpochSamples,
                    epochTransitions);
            }
        }

        public bool TryGetChannelStatistics(
            string sourceId,
            ulong sourceEpoch,
            string vehicleId,
            out VehicleStateChannelStatistics statistics)
        {
            lock (gate)
            {
                if (!disposed && TryGetChannel(sourceId, sourceEpoch, vehicleId, out VehicleChannel channel))
                {
                    statistics = channel.GetStatistics();
                    return true;
                }

                statistics = default;
                return false;
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                if (!disposed)
                {
                    channels.Clear();
                    activeEpochBySource.Clear();
                    acceptedSamples = 0UL;
                    invalidSamples = 0UL;
                    retiredEpochSamples = 0UL;
                    epochTransitions = 0UL;
                }
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

                channels.Clear();
                activeEpochBySource.Clear();
                disposed = true;
            }
        }

        private void RetireSourceEpochs(string sourceId, ulong newEpoch)
        {
            var retiredKeys = new List<VehicleKey>();
            foreach (VehicleKey key in channels.Keys)
            {
                if (key.SourceEpoch != newEpoch &&
                    string.Equals(key.SourceId, sourceId, StringComparison.Ordinal))
                {
                    retiredKeys.Add(key);
                }
            }

            foreach (VehicleKey key in retiredKeys)
            {
                channels.Remove(key);
            }
        }

        private bool TryGetChannel(
            string sourceId,
            ulong sourceEpoch,
            string vehicleId,
            out VehicleChannel channel)
        {
            if (sourceId == null || vehicleId == null)
            {
                channel = null;
                return false;
            }

            return channels.TryGetValue(new VehicleKey(sourceId, sourceEpoch, vehicleId), out channel);
        }

        private readonly struct VehicleKey : IEquatable<VehicleKey>
        {
            public VehicleKey(string sourceId, ulong sourceEpoch, string vehicleId)
            {
                SourceId = sourceId;
                SourceEpoch = sourceEpoch;
                VehicleId = vehicleId;
            }

            public string SourceId { get; }
            public ulong SourceEpoch { get; }
            public string VehicleId { get; }

            public bool Equals(VehicleKey other)
            {
                return SourceEpoch == other.SourceEpoch &&
                       string.Equals(SourceId, other.SourceId, StringComparison.Ordinal) &&
                       string.Equals(VehicleId, other.VehicleId, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is VehicleKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = StringComparer.Ordinal.GetHashCode(SourceId);
                    hash = (hash * 397) ^ SourceEpoch.GetHashCode();
                    return (hash * 397) ^ StringComparer.Ordinal.GetHashCode(VehicleId);
                }
            }
        }

        private sealed class VehicleChannel
        {
            private readonly ReceivedVehicleState[] samples;
            private int nextIndex;

            public VehicleChannel(int capacity)
            {
                samples = new ReceivedVehicleState[capacity];
            }

            public int Count { get; private set; }
            public ulong AcceptedSamples { get; set; }
            public ulong InvalidSamples { get; set; }
            public ulong DuplicateSamples { get; set; }
            public ulong ConflictingDuplicateSamples { get; set; }
            public ulong OutOfOrderSamples { get; set; }
            public ulong NonIncreasingTimestampSamples { get; set; }
            public ulong LocalClockRegressionSamples { get; set; }
            public ulong MissingSequenceCount { get; set; }
            public ulong DiscontinuityResets { get; set; }

            public ReceivedVehicleState Latest
            {
                get
                {
                    if (Count == 0) throw new InvalidOperationException("The vehicle channel is empty.");
                    int index = nextIndex == 0 ? samples.Length - 1 : nextIndex - 1;
                    return samples[index];
                }
            }

            public void Append(ReceivedVehicleState sample)
            {
                samples[nextIndex] = sample;
                nextIndex = (nextIndex + 1) % samples.Length;
                if (Count < samples.Length)
                {
                    Count++;
                }
            }

            public void ClearHistory()
            {
                Array.Clear(samples, 0, samples.Length);
                nextIndex = 0;
                Count = 0;
            }

            public ReceivedVehicleState[] CopyInLogicalOrder()
            {
                var copy = new ReceivedVehicleState[Count];
                int first = (nextIndex - Count + samples.Length) % samples.Length;
                for (int index = 0; index < Count; index++)
                {
                    copy[index] = samples[(first + index) % samples.Length];
                }

                return copy;
            }

            public VehicleStateChannelStatistics GetStatistics()
            {
                return new VehicleStateChannelStatistics(
                    AcceptedSamples,
                    InvalidSamples,
                    DuplicateSamples,
                    ConflictingDuplicateSamples,
                    OutOfOrderSamples,
                    NonIncreasingTimestampSamples,
                    LocalClockRegressionSamples,
                    MissingSequenceCount,
                    DiscontinuityResets);
            }
        }
    }
}
