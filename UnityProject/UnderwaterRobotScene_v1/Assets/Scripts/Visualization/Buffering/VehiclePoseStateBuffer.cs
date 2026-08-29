using System;
using UnderwaterRobotScene.Visualization.State;

namespace UnderwaterRobotScene.Visualization.Buffering
{
    public sealed class VehiclePoseStateBuffer
    {
        private struct Entry
        {
            public VehiclePoseState state;
            public double localReceiveTimeSeconds;
        }

        private readonly int _requestedCapacity;
        private readonly int _capacity;
        private readonly Entry[] _entries;
        private int _head;
        private int _count;
        private bool _hasAcceptedState;
        private ulong _lastAcceptedSequenceId;
        private double _lastAcceptedTimestampSeconds;
        private double _lastAcceptedLocalReceiveTimeSeconds;
        private ulong _skippedSequenceCount;
        private ulong _acceptedCount;
        private ulong _rejectedCount;
        private ulong _invalidStateRejectCount;
        private ulong _nonFiniteRejectCount;
        private ulong _floatOverflowRejectCount;
        private ulong _duplicateSequenceRejectCount;
        private ulong _outOfOrderSequenceRejectCount;
        private ulong _duplicateTimestampRejectCount;
        private ulong _timestampRegressionRejectCount;
        private ulong _invalidReceiveTimeRejectCount;
        private ulong _bufferNotInitializedRejectCount;

        public VehiclePoseStateBuffer(int requestedCapacity = VehiclePoseBufferPolicy.DefaultCapacity)
        {
            _requestedCapacity = requestedCapacity;
            if (requestedCapacity < VehiclePoseBufferPolicy.MinimumValidCapacity)
            {
                _capacity = 0;
                _entries = null;
                return;
            }

            _capacity = requestedCapacity;
            _entries = new Entry[requestedCapacity];
        }

        public int RequestedCapacity => _requestedCapacity;
        public int Capacity => _capacity;
        public int Count => _count;
        public bool IsInitialized => _entries != null;
        public bool HasAcceptedState => _hasAcceptedState;
        public ulong SkippedSequenceCount => _skippedSequenceCount;
        public ulong AcceptedCount => _acceptedCount;
        public ulong RejectedCount => _rejectedCount;

        public VehiclePoseBufferPushResult Push(VehiclePoseState state, double localReceiveTimeSeconds)
        {
            if (!IsInitialized) return Reject(VehiclePoseBufferPushResult.BufferNotInitialized);
            if (!state.valid) return Reject(VehiclePoseBufferPushResult.InvalidState);
            if (!VehiclePoseBufferPolicy.IsFinite(state.timestampSeconds) ||
                !VehiclePoseBufferPolicy.IsFinite(state.x) ||
                !VehiclePoseBufferPolicy.IsFinite(state.y) ||
                !VehiclePoseBufferPolicy.IsFinite(state.z) ||
                !VehiclePoseBufferPolicy.IsFinite(state.roll) ||
                !VehiclePoseBufferPolicy.IsFinite(state.pitch) ||
                !VehiclePoseBufferPolicy.IsFinite(state.yaw))
            {
                return Reject(VehiclePoseBufferPushResult.NonFiniteValue);
            }

            if (!VehiclePoseBufferPolicy.IsFloatRepresentable(state.x) ||
                !VehiclePoseBufferPolicy.IsFloatRepresentable(state.y) ||
                !VehiclePoseBufferPolicy.IsFloatRepresentable(state.z))
            {
                return Reject(VehiclePoseBufferPushResult.FloatRangeOverflow);
            }

            if (!VehiclePoseBufferPolicy.IsFinite(localReceiveTimeSeconds) || localReceiveTimeSeconds < 0.0 ||
                (_hasAcceptedState && localReceiveTimeSeconds < _lastAcceptedLocalReceiveTimeSeconds))
            {
                return Reject(VehiclePoseBufferPushResult.InvalidReceiveTime);
            }

            if (_hasAcceptedState)
            {
                if (state.sequenceId == _lastAcceptedSequenceId) return Reject(VehiclePoseBufferPushResult.DuplicateSequence);
                if (state.sequenceId < _lastAcceptedSequenceId) return Reject(VehiclePoseBufferPushResult.OutOfOrderSequence);
                if (state.timestampSeconds == _lastAcceptedTimestampSeconds) return Reject(VehiclePoseBufferPushResult.DuplicateTimestamp);
                if (state.timestampSeconds < _lastAcceptedTimestampSeconds) return Reject(VehiclePoseBufferPushResult.TimestampRegression);
            }

            int writeIndex;
            if (_count < _capacity)
            {
                writeIndex = (_head + _count) % _capacity;
                _count++;
            }
            else
            {
                writeIndex = _head;
                _head = (_head + 1) % _capacity;
            }

            _entries[writeIndex].state = state;
            _entries[writeIndex].localReceiveTimeSeconds = localReceiveTimeSeconds;
            if (_hasAcceptedState && state.sequenceId > _lastAcceptedSequenceId + 1UL)
            {
                AddSaturated(ref _skippedSequenceCount, state.sequenceId - _lastAcceptedSequenceId - 1UL);
            }

            _hasAcceptedState = true;
            _lastAcceptedSequenceId = state.sequenceId;
            _lastAcceptedTimestampSeconds = state.timestampSeconds;
            _lastAcceptedLocalReceiveTimeSeconds = localReceiveTimeSeconds;
            IncrementSaturated(ref _acceptedCount);
            return VehiclePoseBufferPushResult.Accepted;
        }

        public void Clear()
        {
            if (_entries != null) Array.Clear(_entries, 0, _entries.Length);
            _head = 0;
            _count = 0;
            _hasAcceptedState = false;
            _lastAcceptedSequenceId = 0UL;
            _lastAcceptedTimestampSeconds = 0.0;
            _lastAcceptedLocalReceiveTimeSeconds = 0.0;
            _skippedSequenceCount = 0UL;
            _acceptedCount = 0UL;
            _rejectedCount = 0UL;
            _invalidStateRejectCount = 0UL;
            _nonFiniteRejectCount = 0UL;
            _floatOverflowRejectCount = 0UL;
            _duplicateSequenceRejectCount = 0UL;
            _outOfOrderSequenceRejectCount = 0UL;
            _duplicateTimestampRejectCount = 0UL;
            _timestampRegressionRejectCount = 0UL;
            _invalidReceiveTimeRejectCount = 0UL;
            _bufferNotInitializedRejectCount = 0UL;
        }

        public bool TryGetOldest(out VehiclePoseState state, out double localReceiveTimeSeconds)
        {
            return TryGetAtLogicalIndex(0, out state, out localReceiveTimeSeconds);
        }

        public bool TryGetLatest(out VehiclePoseState state, out double localReceiveTimeSeconds)
        {
            return TryGetAtLogicalIndex(_count - 1, out state, out localReceiveTimeSeconds);
        }

        public bool TryGetAtLogicalIndex(int index, out VehiclePoseState state, out double localReceiveTimeSeconds)
        {
            if (index < 0 || index >= _count || _entries == null)
            {
                state = default;
                localReceiveTimeSeconds = 0.0;
                return false;
            }

            int physicalIndex = (_head + index) % _capacity;
            state = _entries[physicalIndex].state;
            localReceiveTimeSeconds = _entries[physicalIndex].localReceiveTimeSeconds;
            return true;
        }

        public bool TryGetBracket(double targetTimestampSeconds, out VehiclePoseState stateA, out VehiclePoseState stateB)
        {
            stateA = default;
            stateB = default;
            if (!VehiclePoseBufferPolicy.IsFinite(targetTimestampSeconds) || _count < 2) return false;
            for (int index = 0; index < _count - 1; index++)
            {
                int first = (_head + index) % _capacity;
                int second = (_head + index + 1) % _capacity;
                double timeA = _entries[first].state.timestampSeconds;
                double timeB = _entries[second].state.timestampSeconds;
                if (timeA < targetTimestampSeconds && targetTimestampSeconds < timeB)
                {
                    stateA = _entries[first].state;
                    stateB = _entries[second].state;
                    return true;
                }
            }

            return false;
        }

        public bool TryGetLastAcceptedTiming(out ulong sequenceId, out double timestampSeconds, out double localReceiveTimeSeconds)
        {
            if (!_hasAcceptedState)
            {
                sequenceId = 0UL;
                timestampSeconds = 0.0;
                localReceiveTimeSeconds = 0.0;
                return false;
            }

            sequenceId = _lastAcceptedSequenceId;
            timestampSeconds = _lastAcceptedTimestampSeconds;
            localReceiveTimeSeconds = _lastAcceptedLocalReceiveTimeSeconds;
            return true;
        }

        public bool TryGetDataAge(double currentLocalTimeSeconds, out double dataAgeSeconds)
        {
            dataAgeSeconds = 0.0;
            if (!_hasAcceptedState || !VehiclePoseBufferPolicy.IsFinite(currentLocalTimeSeconds) || currentLocalTimeSeconds < 0.0 ||
                currentLocalTimeSeconds < _lastAcceptedLocalReceiveTimeSeconds)
            {
                return false;
            }

            dataAgeSeconds = currentLocalTimeSeconds - _lastAcceptedLocalReceiveTimeSeconds;
            return true;
        }

        public bool TryIsStale(double currentLocalTimeSeconds, double staleTimeoutSeconds, out bool isStale)
        {
            isStale = false;
            if (!VehiclePoseBufferPolicy.IsFinite(staleTimeoutSeconds) || staleTimeoutSeconds < 0.0 ||
                !TryGetDataAge(currentLocalTimeSeconds, out double dataAgeSeconds))
            {
                return false;
            }

            isStale = dataAgeSeconds > staleTimeoutSeconds;
            return true;
        }

        public ulong GetRejectCount(VehiclePoseBufferPushResult result)
        {
            switch (result)
            {
                case VehiclePoseBufferPushResult.InvalidState: return _invalidStateRejectCount;
                case VehiclePoseBufferPushResult.NonFiniteValue: return _nonFiniteRejectCount;
                case VehiclePoseBufferPushResult.FloatRangeOverflow: return _floatOverflowRejectCount;
                case VehiclePoseBufferPushResult.DuplicateSequence: return _duplicateSequenceRejectCount;
                case VehiclePoseBufferPushResult.OutOfOrderSequence: return _outOfOrderSequenceRejectCount;
                case VehiclePoseBufferPushResult.DuplicateTimestamp: return _duplicateTimestampRejectCount;
                case VehiclePoseBufferPushResult.TimestampRegression: return _timestampRegressionRejectCount;
                case VehiclePoseBufferPushResult.InvalidReceiveTime: return _invalidReceiveTimeRejectCount;
                case VehiclePoseBufferPushResult.BufferNotInitialized: return _bufferNotInitializedRejectCount;
                default: return 0UL;
            }
        }

        private VehiclePoseBufferPushResult Reject(VehiclePoseBufferPushResult result)
        {
            IncrementSaturated(ref _rejectedCount);
            switch (result)
            {
                case VehiclePoseBufferPushResult.InvalidState: IncrementSaturated(ref _invalidStateRejectCount); break;
                case VehiclePoseBufferPushResult.NonFiniteValue: IncrementSaturated(ref _nonFiniteRejectCount); break;
                case VehiclePoseBufferPushResult.FloatRangeOverflow: IncrementSaturated(ref _floatOverflowRejectCount); break;
                case VehiclePoseBufferPushResult.DuplicateSequence: IncrementSaturated(ref _duplicateSequenceRejectCount); break;
                case VehiclePoseBufferPushResult.OutOfOrderSequence: IncrementSaturated(ref _outOfOrderSequenceRejectCount); break;
                case VehiclePoseBufferPushResult.DuplicateTimestamp: IncrementSaturated(ref _duplicateTimestampRejectCount); break;
                case VehiclePoseBufferPushResult.TimestampRegression: IncrementSaturated(ref _timestampRegressionRejectCount); break;
                case VehiclePoseBufferPushResult.InvalidReceiveTime: IncrementSaturated(ref _invalidReceiveTimeRejectCount); break;
                case VehiclePoseBufferPushResult.BufferNotInitialized: IncrementSaturated(ref _bufferNotInitializedRejectCount); break;
            }
            return result;
        }

        private static void IncrementSaturated(ref ulong value)
        {
            if (value != ulong.MaxValue) value++;
        }

        private static void AddSaturated(ref ulong value, ulong addend)
        {
            value = ulong.MaxValue - value < addend ? ulong.MaxValue : value + addend;
        }
    }
}
