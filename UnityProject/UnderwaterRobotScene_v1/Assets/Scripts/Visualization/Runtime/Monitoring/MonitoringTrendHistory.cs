using System;

namespace UnderwaterRobotScene.Visualization.Runtime.Monitoring
{
    [Flags]
    public enum MonitoringTrendFields : byte
    {
        None = 0,
        VerticalPositionY = 1 << 0,
        Heading = 1 << 1,
        Pitch = 1 << 2,
        Roll = 1 << 3,
        LinearSpeed = 1 << 4
    }

    public readonly struct MonitoringTrendSample
    {
        public MonitoringTrendSample(
            double sourceTimestampSeconds,
            ulong sourceEpoch,
            ulong sequenceNumber,
            MonitoringTrendFields validFields,
            float verticalPositionY,
            float headingDegrees,
            float pitchDegrees,
            float rollDegrees,
            double linearSpeedMetersPerSecond,
            bool startsNewSegment)
        {
            SourceTimestampSeconds = sourceTimestampSeconds;
            SourceEpoch = sourceEpoch;
            SequenceNumber = sequenceNumber;
            ValidFields = validFields;
            VerticalPositionY = verticalPositionY;
            HeadingDegrees = headingDegrees;
            PitchDegrees = pitchDegrees;
            RollDegrees = rollDegrees;
            LinearSpeedMetersPerSecond = linearSpeedMetersPerSecond;
            StartsNewSegment = startsNewSegment;
        }

        public double SourceTimestampSeconds { get; }
        public ulong SourceEpoch { get; }
        public ulong SequenceNumber { get; }
        public MonitoringTrendFields ValidFields { get; }
        public float VerticalPositionY { get; }
        public float HeadingDegrees { get; }
        public float PitchDegrees { get; }
        public float RollDegrees { get; }
        public double LinearSpeedMetersPerSecond { get; }
        public bool StartsNewSegment { get; }

        public bool Has(MonitoringTrendFields field) =>
            (ValidFields & field) == field;
    }

    public sealed class MonitoringTrendSeries
    {
        private readonly MonitoringTrendSample[] samples;
        private int nextIndex;
        private bool hasIdentity;
        private ulong sourceEpoch;
        private ulong lastSequence;
        private bool segmentBreakPending = true;

        public MonitoringTrendSeries(int capacity)
        {
            if (capacity < 2)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            samples = new MonitoringTrendSample[capacity];
        }

        public int Capacity => samples.Length;
        public int Count { get; private set; }
        public ulong SourceEpoch => sourceEpoch;

        public MonitoringTrendSample this[int logicalIndex]
        {
            get
            {
                if (logicalIndex < 0 || logicalIndex >= Count)
                    throw new ArgumentOutOfRangeException(nameof(logicalIndex));
                int first = (nextIndex - Count + samples.Length) % samples.Length;
                return samples[(first + logicalIndex) % samples.Length];
            }
        }

        public bool Observe(in VehicleMonitorSnapshot snapshot)
        {
            if (!snapshot.HasSourceTimestamp ||
                snapshot.Health != MonitoringDataHealth.Fresh)
            {
                segmentBreakPending = true;
                return false;
            }

            if (!hasIdentity || sourceEpoch != snapshot.SourceEpoch)
            {
                Clear();
                hasIdentity = true;
                sourceEpoch = snapshot.SourceEpoch;
            }
            else if (snapshot.SequenceNumber == lastSequence)
            {
                return false;
            }
            else if (snapshot.SequenceNumber < lastSequence)
            {
                segmentBreakPending = true;
                return false;
            }

            MonitoringTrendFields fields = MonitoringTrendFields.None;
            if (snapshot.HasAppliedPose)
            {
                fields |= MonitoringTrendFields.VerticalPositionY |
                          MonitoringTrendFields.Heading |
                          MonitoringTrendFields.Pitch |
                          MonitoringTrendFields.Roll;
            }
            if (snapshot.HasLinearSpeed)
                fields |= MonitoringTrendFields.LinearSpeed;

            samples[nextIndex] = new MonitoringTrendSample(
                snapshot.SourceTimestampSeconds,
                snapshot.SourceEpoch,
                snapshot.SequenceNumber,
                fields,
                snapshot.AppliedPosition.y,
                snapshot.AppliedEulerDegrees.y,
                snapshot.AppliedEulerDegrees.x,
                snapshot.AppliedEulerDegrees.z,
                snapshot.LinearSpeedMetersPerSecond,
                segmentBreakPending || Count == 0);
            nextIndex = (nextIndex + 1) % samples.Length;
            if (Count < samples.Length)
                Count++;
            lastSequence = snapshot.SequenceNumber;
            segmentBreakPending = false;
            return true;
        }

        public void MarkGap()
        {
            segmentBreakPending = true;
        }

        public void Clear()
        {
            Array.Clear(samples, 0, samples.Length);
            nextIndex = 0;
            Count = 0;
            lastSequence = 0UL;
            segmentBreakPending = true;
        }
    }

    public sealed class MonitoringTrendHistory
    {
        public const int DefaultCapacityPerVehicle = 640;

        public MonitoringTrendHistory(int capacityPerVehicle = DefaultCapacityPerVehicle)
        {
            Auv = new MonitoringTrendSeries(capacityPerVehicle);
            Rov = new MonitoringTrendSeries(capacityPerVehicle);
            Usv = new MonitoringTrendSeries(capacityPerVehicle);
        }

        public MonitoringTrendSeries Auv { get; }
        public MonitoringTrendSeries Rov { get; }
        public MonitoringTrendSeries Usv { get; }

        public void Observe(in MonitoringFleetSnapshot fleet)
        {
            VehicleMonitorSnapshot auv = fleet.Auv;
            VehicleMonitorSnapshot rov = fleet.Rov;
            VehicleMonitorSnapshot usv = fleet.Usv;
            Auv.Observe(in auv);
            Rov.Observe(in rov);
            Usv.Observe(in usv);
        }

        public MonitoringTrendSeries GetSeries(VehicleSelectionKind kind)
        {
            switch (kind)
            {
                case VehicleSelectionKind.Auv: return Auv;
                case VehicleSelectionKind.Rov: return Rov;
                case VehicleSelectionKind.Usv: return Usv;
                default: return null;
            }
        }

        public static bool ShouldConnectAngles(
            in MonitoringTrendSample previous,
            in MonitoringTrendSample current,
            MonitoringTrendFields field,
            float previousValue,
            float currentValue)
        {
            return !current.StartsNewSegment &&
                   previous.SourceEpoch == current.SourceEpoch &&
                   previous.Has(field) && current.Has(field) &&
                   Math.Abs(currentValue - previousValue) <= 180f;
        }
    }
}
