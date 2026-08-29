using System;
using System.Runtime.CompilerServices;
using UnderwaterRobotScene.Visualization.Buffering;
using UnderwaterRobotScene.Visualization.Interpolation;
using UnderwaterRobotScene.Visualization.State;
using UnityEngine;

[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]

namespace UnderwaterRobotScene.Visualization.Driving
{
    public enum AuvPoseApplyResult
    {
        Applied = 0,
        NoState,
        InvalidState,
        NonFiniteValue,
        FloatRangeOverflow,
        MultipleRotationAxes,
        MissingSource,
        Reset
    }

    public enum AuvPoseDriverStepResult
    {
        ImmediateLatest = 0,
        BufferedApplied,
        BufferedNoSamples,
        BufferedPausedHold,
        BufferedAwaitingFreshSample,
        BufferedStaleHold,
        BufferedPoseRejected,
        BufferedInvalidLocalTime,
        BufferedInvalidDelay,
        BufferedInvalidStaleTimeout,
        BufferedInvalidCapacity,
        BufferedInterpolationRejected,
        MissingSource,
        Reset
    }

    public sealed class AuvPoseDriver : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour poseSourceBehaviour;
        [SerializeField] private AuvMvpRotationInputMode rotationInputMode = AuvMvpRotationInputMode.SingleAxisOnly;
        [SerializeField] private AuvPosePlaybackMode playbackMode = AuvPosePlaybackMode.ImmediateLatest;
        [SerializeField] private int bufferCapacity = VehiclePoseBufferPolicy.DefaultCapacity;
        [SerializeField] private double interpolationDelaySeconds = 1.0 / 60.0;
        [SerializeField] private double staleTimeoutSeconds = 0.5;

        private IVehiclePoseSource poseSource;
        private MonoBehaviour boundSourceBehaviour;
        private Vector3 baselinePosition;
        private Quaternion baselineRotation;
        private bool baselineCaptured;
        private VehiclePoseStateBuffer poseBuffer;
        private AuvPosePlaybackCursor playbackCursor;
        private bool bufferedSessionActive;
        private int effectiveBufferCapacity;
        private double effectiveInterpolationDelaySeconds;
        private double effectiveStaleTimeoutSeconds;
        private AuvPosePlaybackMode lastEffectivePlaybackMode = AuvPosePlaybackMode.ImmediateLatest;
        private AuvMvpRotationInputMode lastEffectiveRotationInputMode = AuvMvpRotationInputMode.SingleAxisOnly;
        private bool modesInitialized;
        private bool hasLastProcessedLocalTime;
        private double lastProcessedLocalTime;
        private bool awaitingFreshSampleAfterPause;
        private PoseSourceStatus lastSourceStatus = PoseSourceStatus.Stopped;
        private bool hasLastSourceStatus;
        private bool hasValidDataAge;
        private double dataAgeSeconds;
        private bool isStale;

        public AuvPoseApplyResult LastApplyResult { get; private set; } = AuvPoseApplyResult.NoState;
        public AuvPoseDriverStepResult LastDriverStepResult { get; private set; } = AuvPoseDriverStepResult.ImmediateLatest;
        public VehiclePoseBufferPushResult LastBufferPushResult { get; private set; } = VehiclePoseBufferPushResult.BufferNotInitialized;
        public AuvPoseInterpolationResult LastInterpolationResult { get; private set; } = AuvPoseInterpolationResult.NoSamples;
        public bool LastBufferPushAttempted { get; private set; }
        public Vector3 BaselinePosition => baselinePosition;
        public Quaternion BaselineRotation => baselineRotation;
        public string LastResetSourceExceptionMessage { get; private set; }
        public AuvPosePlaybackMode PlaybackMode { get => playbackMode; set => playbackMode = value; }
        public AuvPosePlaybackMode EffectivePlaybackMode => EffectivePlayback(playbackMode);
        public int ConfiguredBufferCapacity { get => bufferCapacity; set => bufferCapacity = value; }
        public int EffectiveBufferCapacity => bufferedSessionActive ? effectiveBufferCapacity : 0;
        public double ConfiguredInterpolationDelaySeconds { get => interpolationDelaySeconds; set => interpolationDelaySeconds = value; }
        public double EffectiveInterpolationDelaySeconds => bufferedSessionActive ? effectiveInterpolationDelaySeconds : 0.0;
        public double ConfiguredStaleTimeoutSeconds { get => staleTimeoutSeconds; set => staleTimeoutSeconds = value; }
        public double EffectiveStaleTimeoutSeconds => bufferedSessionActive ? effectiveStaleTimeoutSeconds : 0.0;
        public int BufferedSampleCount => poseBuffer != null ? poseBuffer.Count : 0;
        public bool HasBufferedState => poseBuffer != null && poseBuffer.HasAcceptedState;
        public bool HasValidDataAge => hasValidDataAge;
        public double DataAgeSeconds => hasValidDataAge ? dataAgeSeconds : 0.0;
        public bool IsStale => isStale;
        public bool HasPlaybackCursor => playbackCursor != null && playbackCursor.HasPreviousTarget;
        public double TargetSourceTimeSeconds => HasPlaybackCursor ? playbackCursor.PreviousTargetSourceTime : 0.0;
        public bool AwaitingFreshSampleAfterPause => awaitingFreshSampleAfterPause;

        internal bool HasAllocatedBuffer => poseBuffer != null;
        internal bool HasAllocatedCursor => playbackCursor != null;
        internal double MonotonicTargetBeforeClamp => playbackCursor != null ? playbackCursor.LastMonotonicTargetBeforeClamp : 0.0;
        internal double EffectiveTargetAfterClamp => playbackCursor != null ? playbackCursor.LastEffectiveTarget : 0.0;
        internal double LastProcessedLocalTime => hasLastProcessedLocalTime ? lastProcessedLocalTime : 0.0;

        public AuvMvpRotationInputMode RotationInputMode
        {
            get => rotationInputMode;
            set => rotationInputMode = value;
        }

        private void Awake()
        {
            CaptureBaselineOnce();
            SynchronizeSourceBinding();
            InitializeModes();
        }

        private void Update()
        {
            PollSourceAndApplyOnceAtLocalTime(Time.unscaledTimeAsDouble);
        }

        public void BindSource(MonoBehaviour sourceBehaviour)
        {
            poseSourceBehaviour = sourceBehaviour;
            SynchronizeSourceBinding();
        }

        public AuvPoseApplyResult PollSourceAndApplyOnce()
        {
            return PollSourceAndApplyOnceAtLocalTime(Time.unscaledTimeAsDouble);
        }

        internal AuvPoseApplyResult PollSourceAndApplyOnceAtLocalTime(double currentLocalTimeSeconds)
        {
            CaptureBaselineOnce();
            SynchronizeSourceBinding();
            SynchronizeModes();
            if (EffectivePlaybackMode == AuvPosePlaybackMode.ImmediateLatest)
            {
                return PollImmediateLatest();
            }

            return PollBuffered(currentLocalTimeSeconds);
        }

        public AuvPoseApplyResult TryApplyState(VehiclePoseState state)
        {
            CaptureBaselineOnce();
            if (!state.valid) return SetResult(AuvPoseApplyResult.InvalidState);

            AuvPoseApplyResult result = AuvMvpSingleAxisPoseMath.TryCalculate(
                state,
                baselinePosition,
                baselineRotation,
                out Vector3 targetPosition,
                out Quaternion targetRotation);
            if (result == AuvPoseApplyResult.MultipleRotationAxes &&
                rotationInputMode == AuvMvpRotationInputMode.CombinedEulerMvp)
            {
                result = AuvMvpCombinedPoseMath.TryCalculate(
                    state,
                    baselinePosition,
                    baselineRotation,
                    out targetPosition,
                    out targetRotation,
                    out _,
                    out _);
            }

            if (result != AuvPoseApplyResult.Applied) return SetResult(result);

            transform.SetPositionAndRotation(targetPosition, targetRotation);
            return SetResult(AuvPoseApplyResult.Applied);
        }

        public AuvPoseApplyResult ResetDriver()
        {
            CaptureBaselineOnce();
            LastResetSourceExceptionMessage = null;
            try
            {
                poseSource?.ResetSource();
            }
            catch (Exception exception)
            {
                LastResetSourceExceptionMessage = exception.GetType().FullName + ": " + exception.Message;
            }
            finally
            {
                transform.SetPositionAndRotation(baselinePosition, baselineRotation);
                ClearBufferedRuntimeState();
            }

            LastDriverStepResult = AuvPoseDriverStepResult.Reset;
            return SetResult(AuvPoseApplyResult.Reset);
        }

        private AuvPoseApplyResult PollImmediateLatest()
        {
            LastDriverStepResult = AuvPoseDriverStepResult.ImmediateLatest;
            if (poseSource == null) return SetResult(AuvPoseApplyResult.MissingSource);
            if (!poseSource.TryGetLatestState(out VehiclePoseState state)) return SetResult(AuvPoseApplyResult.NoState);
            return TryApplyState(state);
        }

        private AuvPoseApplyResult PollBuffered(double currentLocalTimeSeconds)
        {
            LastBufferPushAttempted = false;
            if (!VehiclePoseBufferPolicy.IsFinite(currentLocalTimeSeconds) || currentLocalTimeSeconds < 0.0 ||
                (hasLastProcessedLocalTime && currentLocalTimeSeconds < lastProcessedLocalTime))
                return FinishWithoutApply(AuvPoseDriverStepResult.BufferedInvalidLocalTime);

            hasLastProcessedLocalTime = true;
            lastProcessedLocalTime = currentLocalTimeSeconds;
            if (effectiveBufferCapacity < VehiclePoseBufferPolicy.MinimumValidCapacity)
                return FinishWithoutApply(AuvPoseDriverStepResult.BufferedInvalidCapacity);
            if (!VehiclePoseBufferPolicy.IsFinite(effectiveInterpolationDelaySeconds) || effectiveInterpolationDelaySeconds < 0.0)
                return FinishWithoutApply(AuvPoseDriverStepResult.BufferedInvalidDelay);
            if (!VehiclePoseBufferPolicy.IsFinite(effectiveStaleTimeoutSeconds) || effectiveStaleTimeoutSeconds < 0.0)
                return FinishWithoutApply(AuvPoseDriverStepResult.BufferedInvalidStaleTimeout);
            EnsureBufferedResources();
            if (poseSource == null)
            {
                LastDriverStepResult = AuvPoseDriverStepResult.MissingSource;
                return LastApplyResult;
            }

            PoseSourceStatus status = poseSource.Status;
            if (status == PoseSourceStatus.Paused)
            {
                awaitingFreshSampleAfterPause = true;
                hasLastSourceStatus = true;
                lastSourceStatus = status;
                UpdateDataAge(currentLocalTimeSeconds);
                isStale = false;
                return FinishWithoutApply(AuvPoseDriverStepResult.BufferedPausedHold);
            }

            bool resumedFromPause = hasLastSourceStatus && lastSourceStatus == PoseSourceStatus.Paused;
            if (resumedFromPause) awaitingFreshSampleAfterPause = true;
            hasLastSourceStatus = true;
            lastSourceStatus = status;

            bool poseRejected = false;
            bool acceptedFreshState = false;
            if (poseSource.TryGetLatestState(out VehiclePoseState state))
            {
                bool duplicateAccepted = poseBuffer.TryGetLastAcceptedTiming(out ulong lastAcceptedSequence, out _, out _) &&
                                         state.sequenceId == lastAcceptedSequence;
                if (!duplicateAccepted)
                {
                    AuvPoseApplyResult preflight = AuvPoseInterpolator.TryConvertEndpoint(
                        state, baselinePosition, baselineRotation, EffectiveRotation(rotationInputMode), out _, out _);
                    if (preflight != AuvPoseApplyResult.Applied)
                    {
                        poseRejected = true;
                        SetResult(preflight);
                    }
                    else
                    {
                        LastBufferPushAttempted = true;
                        LastBufferPushResult = poseBuffer.Push(state, currentLocalTimeSeconds);
                        if (LastBufferPushResult == VehiclePoseBufferPushResult.Accepted)
                        {
                            playbackCursor.SetAnchor(state.timestampSeconds, currentLocalTimeSeconds);
                            acceptedFreshState = true;
                            awaitingFreshSampleAfterPause = false;
                        }
                    }
                }
            }

            UpdateDataAge(currentLocalTimeSeconds);
            if (awaitingFreshSampleAfterPause && !acceptedFreshState)
            {
                isStale = false;
                return FinishWithoutApply(AuvPoseDriverStepResult.BufferedAwaitingFreshSample);
            }

            if (poseRejected)
            {
                isStale = false;
                return FinishWithoutApply(AuvPoseDriverStepResult.BufferedPoseRejected);
            }

            if (hasValidDataAge && poseBuffer.TryIsStale(currentLocalTimeSeconds, effectiveStaleTimeoutSeconds, out bool stale))
            {
                isStale = stale;
            }
            else
            {
                isStale = false;
            }

            if (isStale) return FinishWithoutApply(AuvPoseDriverStepResult.BufferedStaleHold);
            if (!poseBuffer.HasAcceptedState || !poseBuffer.TryGetOldest(out VehiclePoseState oldest, out _) ||
                !poseBuffer.TryGetLatest(out VehiclePoseState latest, out _))
            {
                LastInterpolationResult = AuvPoseInterpolationResult.NoSamples;
                return FinishWithoutApply(AuvPoseDriverStepResult.BufferedNoSamples);
            }

            if (!playbackCursor.TryCalculateTarget(
                    currentLocalTimeSeconds,
                    effectiveInterpolationDelaySeconds,
                    oldest.timestampSeconds,
                    latest.timestampSeconds,
                    out double targetSourceTime))
            {
                LastInterpolationResult = AuvPoseInterpolationResult.InvalidTargetTime;
                return FinishWithoutApply(AuvPoseDriverStepResult.BufferedInterpolationRejected);
            }

            LastInterpolationResult = AuvPoseInterpolator.TrySample(
                poseBuffer,
                targetSourceTime,
                baselinePosition,
                baselineRotation,
                EffectiveRotation(rotationInputMode),
                out Vector3 sampledPosition,
                out Quaternion sampledRotation,
                out _);
            if (!IsSuccessfulInterpolation(LastInterpolationResult))
                return FinishWithoutApply(AuvPoseDriverStepResult.BufferedInterpolationRejected);

            transform.SetPositionAndRotation(sampledPosition, sampledRotation);
            playbackCursor.Commit(targetSourceTime);
            LastDriverStepResult = AuvPoseDriverStepResult.BufferedApplied;
            return SetResult(AuvPoseApplyResult.Applied);
        }

        private void InitializeModes()
        {
            lastEffectivePlaybackMode = EffectivePlaybackMode;
            lastEffectiveRotationInputMode = EffectiveRotation(rotationInputMode);
            modesInitialized = true;
            if (lastEffectivePlaybackMode == AuvPosePlaybackMode.BufferedInterpolationMvp) BeginBufferedSession();
        }

        private void SynchronizeModes()
        {
            if (!modesInitialized) InitializeModes();
            AuvPosePlaybackMode effectivePlayback = EffectivePlaybackMode;
            if (effectivePlayback != lastEffectivePlaybackMode)
            {
                EndBufferedSession();
                lastEffectivePlaybackMode = effectivePlayback;
                if (effectivePlayback == AuvPosePlaybackMode.BufferedInterpolationMvp) BeginBufferedSession();
            }

            AuvMvpRotationInputMode effectiveRotation = EffectiveRotation(rotationInputMode);
            if (effectiveRotation != lastEffectiveRotationInputMode)
            {
                lastEffectiveRotationInputMode = effectiveRotation;
                if (effectivePlayback == AuvPosePlaybackMode.BufferedInterpolationMvp) ClearBufferedRuntimeState();
            }
        }

        private void BeginBufferedSession()
        {
            bufferedSessionActive = true;
            effectiveBufferCapacity = bufferCapacity;
            effectiveInterpolationDelaySeconds = interpolationDelaySeconds;
            effectiveStaleTimeoutSeconds = staleTimeoutSeconds;
            ClearBufferedRuntimeState();
            if (effectiveBufferCapacity >= VehiclePoseBufferPolicy.MinimumValidCapacity &&
                VehiclePoseBufferPolicy.IsFinite(effectiveInterpolationDelaySeconds) && effectiveInterpolationDelaySeconds >= 0.0 &&
                VehiclePoseBufferPolicy.IsFinite(effectiveStaleTimeoutSeconds) && effectiveStaleTimeoutSeconds >= 0.0)
            {
                if (poseBuffer == null || poseBuffer.Capacity != effectiveBufferCapacity)
                    poseBuffer = new VehiclePoseStateBuffer(effectiveBufferCapacity);
                if (playbackCursor == null) playbackCursor = new AuvPosePlaybackCursor();
            }
        }

        private void EndBufferedSession()
        {
            ClearBufferedRuntimeState();
            bufferedSessionActive = false;
            effectiveBufferCapacity = 0;
            effectiveInterpolationDelaySeconds = 0.0;
            effectiveStaleTimeoutSeconds = 0.0;
        }

        private void EnsureBufferedResources()
        {
            if (poseBuffer == null || poseBuffer.Capacity != effectiveBufferCapacity)
                poseBuffer = new VehiclePoseStateBuffer(effectiveBufferCapacity);
            if (playbackCursor == null) playbackCursor = new AuvPosePlaybackCursor();
        }

        private void ClearBufferedRuntimeState()
        {
            poseBuffer?.Clear();
            playbackCursor?.Reset();
            hasLastProcessedLocalTime = false;
            lastProcessedLocalTime = 0.0;
            awaitingFreshSampleAfterPause = false;
            hasLastSourceStatus = false;
            lastSourceStatus = PoseSourceStatus.Stopped;
            hasValidDataAge = false;
            dataAgeSeconds = 0.0;
            isStale = false;
            LastBufferPushAttempted = false;
            LastBufferPushResult = VehiclePoseBufferPushResult.BufferNotInitialized;
            LastInterpolationResult = AuvPoseInterpolationResult.NoSamples;
        }

        private void UpdateDataAge(double currentLocalTimeSeconds)
        {
            hasValidDataAge = poseBuffer != null && poseBuffer.TryGetDataAge(currentLocalTimeSeconds, out dataAgeSeconds);
            if (!hasValidDataAge) dataAgeSeconds = 0.0;
        }

        private void SynchronizeSourceBinding()
        {
            MonoBehaviour authoritative = poseSourceBehaviour;
            if (authoritative == null) authoritative = null;
            bool oldInterfaceMustClear = authoritative == null && poseSource != null;
            if (SameUnityObject(boundSourceBehaviour, authoritative) && !oldInterfaceMustClear) return;

            ClearBufferedRuntimeState();
            boundSourceBehaviour = authoritative;
            poseSource = authoritative != null ? authoritative as IVehiclePoseSource : null;
        }

        private static bool SameUnityObject(MonoBehaviour a, MonoBehaviour b)
        {
            bool aNull = a == null;
            bool bNull = b == null;
            if (aNull || bNull) return aNull && bNull;
            return a == b;
        }

        private static AuvPosePlaybackMode EffectivePlayback(AuvPosePlaybackMode configured)
        {
            return configured == AuvPosePlaybackMode.BufferedInterpolationMvp
                ? AuvPosePlaybackMode.BufferedInterpolationMvp
                : AuvPosePlaybackMode.ImmediateLatest;
        }

        private static AuvMvpRotationInputMode EffectiveRotation(AuvMvpRotationInputMode configured)
        {
            return configured == AuvMvpRotationInputMode.CombinedEulerMvp
                ? AuvMvpRotationInputMode.CombinedEulerMvp
                : AuvMvpRotationInputMode.SingleAxisOnly;
        }

        private static bool IsSuccessfulInterpolation(AuvPoseInterpolationResult result)
        {
            return result == AuvPoseInterpolationResult.HoldOnlySample ||
                   result == AuvPoseInterpolationResult.HoldOldest ||
                   result == AuvPoseInterpolationResult.HoldExactSample ||
                   result == AuvPoseInterpolationResult.Interpolated ||
                   result == AuvPoseInterpolationResult.HoldLatest;
        }

        private AuvPoseApplyResult FinishWithoutApply(AuvPoseDriverStepResult result)
        {
            LastDriverStepResult = result;
            return LastApplyResult;
        }

        private void CaptureBaselineOnce()
        {
            if (baselineCaptured) return;
            baselinePosition = transform.position;
            baselineRotation = transform.rotation;
            baselineCaptured = true;
        }

        private AuvPoseApplyResult SetResult(AuvPoseApplyResult result)
        {
            LastApplyResult = result;
            return result;
        }
    }
}
