using System;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.State
{
    public sealed class ScriptedPoseSource : MonoBehaviour, IVehiclePoseSource
    {
        [SerializeField] private string sourceId = "AUV_SCRIPTED_POSE_MVP";
        [SerializeField] private ScriptedPoseMode mode = ScriptedPoseMode.Static;
        [SerializeField, Min(0.001f)] private double sampleRateHz = 60.0;
        [SerializeField, Min(0f)] private double positionAmplitude = 1.0;
        [SerializeField, Min(0f)] private double angleAmplitudeDegrees = 10.0;
        [SerializeField, Min(0.001f)] private double periodSeconds = 4.0;
        [SerializeField] private bool outputValid = true;

        private PoseSourceStatus status = PoseSourceStatus.Stopped;
        private double accumulatedSeconds;
        private double runningStartedAtSeconds;
        private VehiclePoseState pausedState;

        public string SourceId => sourceId;
        public PoseSourceStatus Status => status;
        public bool IsRunning => status == PoseSourceStatus.Running;

        public ScriptedPoseMode Mode
        {
            get => mode;
            set => mode = value;
        }

        public double SampleRateHz
        {
            get => sampleRateHz;
            set => sampleRateHz = Math.Max(0.001, value);
        }

        public double PositionAmplitude
        {
            get => positionAmplitude;
            set => positionAmplitude = Math.Max(0.0, value);
        }

        public double AngleAmplitudeDegrees
        {
            get => angleAmplitudeDegrees;
            set => angleAmplitudeDegrees = Math.Max(0.0, value);
        }

        public double PeriodSeconds
        {
            get => periodSeconds;
            set => periodSeconds = Math.Max(0.001, value);
        }

        public bool OutputValid
        {
            get => outputValid;
            set => outputValid = value;
        }

        public void StartSource()
        {
            if (status == PoseSourceStatus.Running) return;
            runningStartedAtSeconds = ClockSeconds;
            status = PoseSourceStatus.Running;
        }

        public void StopSource()
        {
            if (status == PoseSourceStatus.Running)
            {
                accumulatedSeconds = CurrentElapsedSeconds;
            }

            status = PoseSourceStatus.Stopped;
        }

        public void ResetSource()
        {
            accumulatedSeconds = 0.0;
            runningStartedAtSeconds = ClockSeconds;
            pausedState = EvaluateAtElapsedSeconds(0.0);
        }

        public void PauseSource()
        {
            if (status != PoseSourceStatus.Running) return;
            accumulatedSeconds = CurrentElapsedSeconds;
            pausedState = EvaluateAtElapsedSeconds(accumulatedSeconds);
            status = PoseSourceStatus.Paused;
        }

        public void ResumeSource()
        {
            if (status == PoseSourceStatus.Paused) StartSource();
        }

        public bool TryGetLatestState(out VehiclePoseState state)
        {
            if (status == PoseSourceStatus.Stopped)
            {
                state = default;
                return false;
            }

            if (status == PoseSourceStatus.Paused)
            {
                state = pausedState;
                return true;
            }

            state = EvaluateAtElapsedSeconds(CurrentElapsedSeconds);
            return true;
        }

        private double ClockSeconds => Time.realtimeSinceStartupAsDouble;

        private double CurrentElapsedSeconds => accumulatedSeconds + Math.Max(0.0, ClockSeconds - runningStartedAtSeconds);

        private VehiclePoseState EvaluateAtElapsedSeconds(double elapsedSeconds)
        {
            long sampleIndex = Math.Max(0L, (long)Math.Floor(elapsedSeconds * sampleRateHz + 1e-9));
            return ScriptedPoseEvaluator.Evaluate(
                mode,
                sampleIndex,
                sampleRateHz,
                positionAmplitude,
                angleAmplitudeDegrees,
                periodSeconds,
                outputValid);
        }

        private void OnValidate()
        {
            sampleRateHz = Math.Max(0.001, sampleRateHz);
            positionAmplitude = Math.Max(0.0, positionAmplitude);
            angleAmplitudeDegrees = Math.Max(0.0, angleAmplitudeDegrees);
            periodSeconds = Math.Max(0.001, periodSeconds);
            if (string.IsNullOrWhiteSpace(sourceId)) sourceId = "SCRIPTED_POSE_SOURCE";
        }
    }
}
