using System;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.LocalTesting;
using UnderwaterRobotScene.Visualization.Sampling;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime
{
    [DisallowMultipleComponent]
    public sealed class VehiclePoseIntegrationConfiguration : MonoBehaviour
    {
        [Header("Local Test / N5-N6 Integration Test Configuration")]
        [SerializeField] private string sourceId = "local-test";
        [SerializeField] private string vehicleId = "LOCAL-TEST-VEHICLE";
        [SerializeField] private VehicleType vehicleType = VehicleType.Unknown;
        [SerializeField] private DeterministicVehicleStateGeneratorKind generatorKind =
            DeterministicVehicleStateGeneratorKind.Default;
        [SerializeField] private Vector3 testOrigin = Vector3.zero;

        [Header("Local source and store")]
        [SerializeField, Min(0.01f)] private float sampleIntervalSeconds = 0.1f;
        [SerializeField, Min(2)] private int storeCapacity = 64;
        [SerializeField, Min(0.05f)] private float staleTimeoutSeconds = 0.75f;
        [SerializeField, Min(1)] private int maxCatchUpStepsPerFrame = 8;
        [SerializeField] private bool autoStart = true;

        [Header("Render sampling policy")]
        [SerializeField, Min(0f)] private float renderDelaySeconds = 0.15f;
        [SerializeField, Min(0.001f)] private float maxInterpolationGapSeconds = 0.25f;
        [SerializeField, Min(0f)] private float maxHoldSourceTimeSeconds = 0.25f;
        [SerializeField, Min(0f)] private float exactTimeToleranceSeconds = 0.000001f;
        [SerializeField] private AfterLatestBehavior afterLatestBehavior =
            AfterLatestBehavior.HoldLatest;
        [SerializeField] private bool allowSingleSampleHold = true;

        public string SourceId => sourceId;
        public string VehicleId => vehicleId;
        public VehicleType VehicleType => vehicleType;
        public DeterministicVehicleStateGeneratorKind GeneratorKind => generatorKind;
        public Vector3 TestOrigin => testOrigin;
        public double SampleIntervalSeconds => sampleIntervalSeconds;
        public int StoreCapacity => storeCapacity;
        public double StaleTimeoutSeconds => staleTimeoutSeconds;
        public int MaxCatchUpStepsPerFrame => maxCatchUpStepsPerFrame;
        public bool AutoStart => autoStart;
        public double RenderDelaySeconds => renderDelaySeconds;
        public double MaxInterpolationGapSeconds => maxInterpolationGapSeconds;
        public double MaxHoldSourceTimeSeconds => maxHoldSourceTimeSeconds;
        public double ExactTimeToleranceSeconds => exactTimeToleranceSeconds;
        public AfterLatestBehavior AfterLatestBehavior => afterLatestBehavior;
        public bool AllowSingleSampleHold => allowSingleSampleHold;

        public void ConfigureLocalTest(
            string configuredSourceId,
            string configuredVehicleId,
            VehicleType configuredVehicleType,
            DeterministicVehicleStateGeneratorKind configuredGeneratorKind,
            Vector3 configuredTestOrigin,
            float configuredSampleIntervalSeconds,
            int configuredStoreCapacity,
            float configuredStaleTimeoutSeconds,
            int configuredMaxCatchUpStepsPerFrame,
            bool configuredAutoStart,
            float configuredRenderDelaySeconds,
            float configuredMaxInterpolationGapSeconds,
            float configuredMaxHoldSourceTimeSeconds,
            float configuredExactTimeToleranceSeconds,
            AfterLatestBehavior configuredAfterLatestBehavior,
            bool configuredAllowSingleSampleHold)
        {
            sourceId = configuredSourceId;
            vehicleId = configuredVehicleId;
            vehicleType = configuredVehicleType;
            generatorKind = configuredGeneratorKind;
            testOrigin = configuredTestOrigin;
            sampleIntervalSeconds = configuredSampleIntervalSeconds;
            storeCapacity = configuredStoreCapacity;
            staleTimeoutSeconds = configuredStaleTimeoutSeconds;
            maxCatchUpStepsPerFrame = configuredMaxCatchUpStepsPerFrame;
            autoStart = configuredAutoStart;
            renderDelaySeconds = configuredRenderDelaySeconds;
            maxInterpolationGapSeconds = configuredMaxInterpolationGapSeconds;
            maxHoldSourceTimeSeconds = configuredMaxHoldSourceTimeSeconds;
            exactTimeToleranceSeconds = configuredExactTimeToleranceSeconds;
            afterLatestBehavior = configuredAfterLatestBehavior;
            allowSingleSampleHold = configuredAllowSingleSampleHold;

            if (!TryValidate(out string error))
            {
                throw new ArgumentException(error, nameof(configuredSourceId));
            }
        }

        public bool TryValidate(out string error)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                error = "Local test SourceId must be explicit.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(vehicleId))
            {
                error = "Local test VehicleId must be explicit.";
                return false;
            }

            if (vehicleType == VehicleType.Unknown)
            {
                error = "Local test vehicle type must be explicit.";
                return false;
            }

            if (generatorKind != DeterministicVehicleStateGeneratorKind.Default &&
                generatorKind != DeterministicVehicleStateGeneratorKind.AuvIntegrationTrajectory &&
                generatorKind != DeterministicVehicleStateGeneratorKind.RovDiagnosticTrajectory &&
                generatorKind != DeterministicVehicleStateGeneratorKind.UsvDiagnosticTrajectory)
            {
                error = "Deterministic state generator kind is unknown.";
                return false;
            }

            if (!IsFinite(testOrigin.x) || !IsFinite(testOrigin.y) || !IsFinite(testOrigin.z))
            {
                error = "Local test origin must be finite.";
                return false;
            }

            if (!IsFinite(sampleIntervalSeconds) || sampleIntervalSeconds <= 0f)
            {
                error = "Sample interval must be finite and greater than zero.";
                return false;
            }

            if (storeCapacity < 2)
            {
                error = "Store capacity must be at least two.";
                return false;
            }

            if (!IsFinite(staleTimeoutSeconds) || staleTimeoutSeconds <= 0f)
            {
                error = "Stale timeout must be finite and greater than zero.";
                return false;
            }

            if (maxCatchUpStepsPerFrame < 1)
            {
                error = "Maximum catch-up steps per frame must be positive.";
                return false;
            }

            if (!IsFinite(renderDelaySeconds) || renderDelaySeconds < 0f)
            {
                error = "Render delay must be finite and non-negative.";
                return false;
            }

            var policy = new RenderSamplingPolicy(
                maxInterpolationGapSeconds,
                maxHoldSourceTimeSeconds,
                exactTimeToleranceSeconds,
                afterLatestBehavior,
                allowSingleSampleHold);
            return policy.TryValidate(out error);
        }

        public IDeterministicVehicleStateGenerator CreateStateGenerator()
        {
            if (!TryValidate(out string error))
            {
                throw new InvalidOperationException(error);
            }

            switch (generatorKind)
            {
                case DeterministicVehicleStateGeneratorKind.Default:
                    return new DefaultDeterministicVehicleStateGenerator(sampleIntervalSeconds);
                case DeterministicVehicleStateGeneratorKind.AuvIntegrationTrajectory:
                    return new DeterministicAuvIntegrationTrajectory();
                case DeterministicVehicleStateGeneratorKind.RovDiagnosticTrajectory:
                    return new DeterministicRovDiagnosticTrajectory();
                case DeterministicVehicleStateGeneratorKind.UsvDiagnosticTrajectory:
                    return new DeterministicUsvDiagnosticTrajectory();
                default:
                    throw new InvalidOperationException("Deterministic state generator kind is unknown.");
            }
        }

        public RenderSamplingPolicy BuildSamplingPolicy()
        {
            if (!TryValidate(out string error))
            {
                throw new InvalidOperationException(error);
            }

            return new RenderSamplingPolicy(
                maxInterpolationGapSeconds,
                maxHoldSourceTimeSeconds,
                exactTimeToleranceSeconds,
                afterLatestBehavior,
                allowSingleSampleHold);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
