using UnderwaterRobotScene.Visualization.Sampling;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime.Usv
{
    public enum UsvActuatorVisualMode
    {
        Disabled = 0,
        DemoAndLocalDiagnosticPublicData = 1
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1050)]
    public sealed class UsvActuatorVisualCoordinator : MonoBehaviour
    {
        [Header("Explicit bindings")]
        [SerializeField] private Transform businessRoot;
        [SerializeField] private PropellerSpinner portVisualThruster;
        [SerializeField] private PropellerSpinner starboardVisualThruster;
        [SerializeField] private Transform rudderVisualPivot;
        [SerializeField] private VehiclePoseDriver poseDriver;
        [SerializeField] private VehiclePoseControlAuthority controlAuthority;

        [Header("Explicit VisualOnly gate")]
        [SerializeField] private UsvActuatorVisualMode mode =
            UsvActuatorVisualMode.Disabled;

        [Header("VisualOnly mapper tuning")]
        [SerializeField] private float speedDeadbandMetersPerSecond = 0.02f;
        [SerializeField] private float speedFullScaleMetersPerSecond = 0.60f;
        [SerializeField] private float yawDeadbandDegreesPerSecond = 2f;
        [SerializeField] private float yawFullScaleDegreesPerSecond = 90f;
        [SerializeField] private float minVisibleRpm = 120f;
        [SerializeField] private float cruiseRpm = 520f;
        [SerializeField] private float maxVisualRpm = 740f;
        [SerializeField] private float maxDifferentialRpm = 220f;
        [SerializeField] private float lowSpeedOffMetersPerSecond = 0.03f;
        [SerializeField] private float lowSpeedFullMetersPerSecond = 0.08f;
        [SerializeField] private float maxVisualRudderDegrees = 25f;

        [Header("VisualOnly slew limits")]
        [SerializeField] private float rpmRiseRate = 1600f;
        [SerializeField] private float rpmFallRate = 2200f;
        [SerializeField] private float rudderSlewRateDegreesPerSecond = 90f;

        [Header("Observer reset thresholds")]
        [SerializeField] private float maxAcceptedDeltaTimeSeconds = 0.25f;
        [SerializeField] private float teleportDistanceThresholdMeters = 0.25f;
        [SerializeField] private float rotationJumpThresholdDegrees = 30f;

        private float originalPortRpm;
        private float originalStarboardRpm;
        private Quaternion neutralRudderLocalRotation = Quaternion.identity;
        private bool hasOriginalState;
        private bool baselineValid;
        private Vector3 previousBusinessPosition;
        private Vector3 previousHullForward;
        private VehiclePoseControlMode previousAuthorityMode;
        private ulong previousSourceEpoch;
        private UsvActuatorVisualMode previousMode;
        private bool hasPreviousAuthority;
        private bool hasPreviousMode;
        private float currentPortRpm;
        private float currentStarboardRpm;
        private float currentRudderDegrees;
        private float lastForwardSpeedMetersPerSecond;
        private float lastYawRateDegreesPerSecond;
        private bool configurationErrorLogged;

        public Transform BusinessRoot => businessRoot;
        public PropellerSpinner PortVisualThruster => portVisualThruster;
        public PropellerSpinner StarboardVisualThruster => starboardVisualThruster;
        public Transform RudderVisualPivot => rudderVisualPivot;
        public VehiclePoseDriver PoseDriver => poseDriver;
        public VehiclePoseControlAuthority ControlAuthority => controlAuthority;
        public UsvActuatorVisualMode Mode
        {
            get => mode;
            set => mode = value;
        }
        public float SpeedDeadbandMetersPerSecond => speedDeadbandMetersPerSecond;
        public float SpeedFullScaleMetersPerSecond => speedFullScaleMetersPerSecond;
        public float YawDeadbandDegreesPerSecond => yawDeadbandDegreesPerSecond;
        public float YawFullScaleDegreesPerSecond => yawFullScaleDegreesPerSecond;
        public float MinVisibleRpm => minVisibleRpm;
        public float CruiseRpm => cruiseRpm;
        public float MaxVisualRpm => maxVisualRpm;
        public float MaxDifferentialRpm => maxDifferentialRpm;
        public float LowSpeedOffMetersPerSecond => lowSpeedOffMetersPerSecond;
        public float LowSpeedFullMetersPerSecond => lowSpeedFullMetersPerSecond;
        public float MaxVisualRudderDegrees => maxVisualRudderDegrees;
        public float RpmRiseRate => rpmRiseRate;
        public float RpmFallRate => rpmFallRate;
        public float RudderSlewRateDegreesPerSecond =>
            rudderSlewRateDegreesPerSecond;
        public float MaxAcceptedDeltaTimeSeconds => maxAcceptedDeltaTimeSeconds;
        public float TeleportDistanceThresholdMeters =>
            teleportDistanceThresholdMeters;
        public float RotationJumpThresholdDegrees =>
            rotationJumpThresholdDegrees;
        public float OriginalPortRpm => originalPortRpm;
        public float OriginalStarboardRpm => originalStarboardRpm;
        public Quaternion NeutralRudderLocalRotation =>
            neutralRudderLocalRotation;
        public bool BaselineValid => baselineValid;
        public float CurrentPortRpm => currentPortRpm;
        public float CurrentStarboardRpm => currentStarboardRpm;
        public float CurrentRudderDegrees => currentRudderDegrees;
        public float LastForwardSpeedMetersPerSecond =>
            lastForwardSpeedMetersPerSecond;
        public float LastYawRateDegreesPerSecond =>
            lastYawRateDegreesPerSecond;

        public void Configure(
            Transform configuredBusinessRoot,
            PropellerSpinner configuredPortVisualThruster,
            PropellerSpinner configuredStarboardVisualThruster,
            Transform configuredRudderVisualPivot,
            VehiclePoseDriver configuredPoseDriver,
            VehiclePoseControlAuthority configuredControlAuthority)
        {
            businessRoot = configuredBusinessRoot;
            portVisualThruster = configuredPortVisualThruster;
            starboardVisualThruster = configuredStarboardVisualThruster;
            rudderVisualPivot = configuredRudderVisualPivot;
            poseDriver = configuredPoseDriver;
            controlAuthority = configuredControlAuthority;
            CacheOriginalState();
        }

        private void OnEnable()
        {
            CacheOriginalState();
            ClearObserverState();
            if (Application.isPlaying && !ReferencesExist())
            {
                DisableForConfigurationError();
            }
        }

        private void LateUpdate()
        {
            TickForDiagnostics(Time.unscaledDeltaTime);
        }

        public void TickForDiagnostics(double deltaTimeSeconds)
        {
            if (mode == UsvActuatorVisualMode.Disabled)
            {
                RestoreOriginalActuatorState();
                ClearObserverState();
                previousMode = mode;
                hasPreviousMode = true;
                return;
            }

            if (!ReferencesExist())
            {
                DisableForConfigurationError();
                return;
            }
            if (!hasOriginalState)
            {
                CacheOriginalState();
            }
            if (!ReferencesActive())
            {
                HardReset();
                return;
            }
            if (!IsFinite(deltaTimeSeconds) ||
                deltaTimeSeconds <= 0.0 ||
                deltaTimeSeconds > maxAcceptedDeltaTimeSeconds)
            {
                HardReset();
                return;
            }
            if (!TryReadPose(out Vector3 position, out Vector3 hullForward))
            {
                HardReset();
                return;
            }
            if (!TryGetActiveEpoch(out ulong sourceEpoch))
            {
                HardReset();
                return;
            }

            VehiclePoseControlMode authorityMode = controlAuthority.Mode;
            if ((hasPreviousMode && previousMode != mode) ||
                (hasPreviousAuthority && previousAuthorityMode != authorityMode))
            {
                previousMode = mode;
                hasPreviousMode = true;
                previousAuthorityMode = authorityMode;
                hasPreviousAuthority = true;
                HardReset();
                return;
            }
            previousMode = mode;
            hasPreviousMode = true;
            previousAuthorityMode = authorityMode;
            hasPreviousAuthority = true;

            if (!GateAllowsCurrentAuthority())
            {
                HardReset();
                return;
            }

            if (!baselineValid)
            {
                CaptureBaseline(position, hullForward, sourceEpoch);
                ApplyHardNeutral();
                return;
            }
            if (sourceEpoch != previousSourceEpoch)
            {
                HardReset();
                CaptureBaseline(position, hullForward, sourceEpoch);
                return;
            }

            Vector3 displacement = position - previousBusinessPosition;
            displacement.y = 0f;
            float rotationJump = Vector3.Angle(previousHullForward, hullForward);
            if (!IsFinite(displacement) ||
                displacement.magnitude > teleportDistanceThresholdMeters ||
                !float.IsFinite(rotationJump) ||
                rotationJump > rotationJumpThresholdDegrees)
            {
                HardReset();
                CaptureBaseline(position, hullForward, sourceEpoch);
                return;
            }

            float deltaTime = (float)deltaTimeSeconds;
            Vector3 planarVelocity = displacement / deltaTime;
            float forwardSpeed = Vector3.Dot(planarVelocity, hullForward);
            float yawRate = Vector3.SignedAngle(
                                previousHullForward,
                                hullForward,
                                Vector3.up) /
                            deltaTime;
            previousBusinessPosition = position;
            previousHullForward = hullForward;
            previousSourceEpoch = sourceEpoch;
            if (!float.IsFinite(forwardSpeed) || !float.IsFinite(yawRate))
            {
                HardReset();
                return;
            }

            lastForwardSpeedMetersPerSecond = forwardSpeed;
            lastYawRateDegreesPerSecond = yawRate;
            UsvActuatorVisualConfig config = BuildMapperConfig();
            if (!DeterministicUsvActuatorVisualMapper.TryMap(
                    forwardSpeed,
                    yawRate,
                    in config,
                    out UsvActuatorVisualTargets targets))
            {
                HardReset();
                return;
            }

            currentPortRpm = MoveRpm(
                currentPortRpm,
                targets.PortRpm,
                deltaTime);
            currentStarboardRpm = MoveRpm(
                currentStarboardRpm,
                targets.StarboardRpm,
                deltaTime);
            currentRudderDegrees = Mathf.MoveTowards(
                currentRudderDegrees,
                targets.RudderDegrees,
                rudderSlewRateDegreesPerSecond * deltaTime);
            WriteCurrentActuators();
        }

        private bool GateAllowsCurrentAuthority()
        {
            if (mode !=
                UsvActuatorVisualMode.DemoAndLocalDiagnosticPublicData)
            {
                return false;
            }
            return poseDriver.isActiveAndEnabled &&
                   poseDriver.OwnsControl &&
                   poseDriver.HasAppliedPose &&
                   poseDriver.HasFreshAppliedPose &&
                   poseDriver.LastFailureReason == RenderSampleFailureReason.None &&
                   poseDriver.LastAppliedSourceEpoch != 0UL &&
                   poseDriver.TargetRoot == businessRoot;
        }

        private bool TryGetActiveEpoch(out ulong sourceEpoch)
        {
            sourceEpoch = poseDriver.LastAppliedSourceEpoch;
            return sourceEpoch != 0UL;
        }

        private bool TryReadPose(
            out Vector3 position,
            out Vector3 hullForward)
        {
            position = businessRoot.position;
            hullForward = businessRoot.right;
            hullForward.y = 0f;
            float sqrMagnitude = hullForward.sqrMagnitude;
            if (!IsFinite(position) ||
                !IsFinite(businessRoot.rotation) ||
                !IsFinite(hullForward) ||
                !float.IsFinite(sqrMagnitude) ||
                sqrMagnitude <= 0.000001f)
            {
                hullForward = default;
                return false;
            }
            hullForward /= Mathf.Sqrt(sqrMagnitude);
            return true;
        }

        private void CaptureBaseline(
            Vector3 position,
            Vector3 hullForward,
            ulong sourceEpoch)
        {
            previousBusinessPosition = position;
            previousHullForward = hullForward;
            previousSourceEpoch = sourceEpoch;
            baselineValid = true;
        }

        private float MoveRpm(float current, float target, float deltaTime)
        {
            float rate = target > current ? rpmRiseRate : rpmFallRate;
            return Mathf.MoveTowards(current, target, rate * deltaTime);
        }

        private void WriteCurrentActuators()
        {
            portVisualThruster.rpm = currentPortRpm;
            starboardVisualThruster.rpm = currentStarboardRpm;
            rudderVisualPivot.localRotation =
                neutralRudderLocalRotation *
                Quaternion.AngleAxis(currentRudderDegrees, Vector3.up);
        }

        private void ApplyHardNeutral()
        {
            currentPortRpm = 0f;
            currentStarboardRpm = 0f;
            currentRudderDegrees = 0f;
            lastForwardSpeedMetersPerSecond = 0f;
            lastYawRateDegreesPerSecond = 0f;
            if (ReferencesExist())
            {
                portVisualThruster.rpm = 0f;
                starboardVisualThruster.rpm = 0f;
                rudderVisualPivot.localRotation =
                    neutralRudderLocalRotation;
            }
        }

        private void HardReset()
        {
            ApplyHardNeutral();
            baselineValid = false;
            previousBusinessPosition = default;
            previousHullForward = default;
            previousSourceEpoch = default;
        }

        private void ClearObserverState()
        {
            baselineValid = false;
            previousBusinessPosition = default;
            previousHullForward = default;
            previousSourceEpoch = default;
            currentPortRpm = 0f;
            currentStarboardRpm = 0f;
            currentRudderDegrees = 0f;
            lastForwardSpeedMetersPerSecond = 0f;
            lastYawRateDegreesPerSecond = 0f;
            hasPreviousAuthority = false;
        }

        private void CacheOriginalState()
        {
            if (!ReferencesExist())
            {
                return;
            }
            originalPortRpm = portVisualThruster.rpm;
            originalStarboardRpm = starboardVisualThruster.rpm;
            neutralRudderLocalRotation = rudderVisualPivot.localRotation;
            hasOriginalState = true;
        }

        private void RestoreOriginalActuatorState()
        {
            if (!hasOriginalState || !ReferencesExist())
            {
                return;
            }
            portVisualThruster.rpm = originalPortRpm;
            starboardVisualThruster.rpm = originalStarboardRpm;
            rudderVisualPivot.localRotation = neutralRudderLocalRotation;
            currentPortRpm = originalPortRpm;
            currentStarboardRpm = originalStarboardRpm;
            currentRudderDegrees = 0f;
        }

        private bool ReferencesExist()
        {
            return businessRoot != null &&
                   portVisualThruster != null &&
                   starboardVisualThruster != null &&
                   rudderVisualPivot != null &&
                   poseDriver != null &&
                   controlAuthority != null &&
                   rudderVisualPivot.parent != null &&
                   rudderVisualPivot.parent.name == "USV_Rudder_Main";
        }

        private bool ReferencesActive()
        {
            return businessRoot.gameObject.activeInHierarchy &&
                   portVisualThruster.isActiveAndEnabled &&
                   starboardVisualThruster.isActiveAndEnabled &&
                   rudderVisualPivot.gameObject.activeInHierarchy;
        }

        private UsvActuatorVisualConfig BuildMapperConfig()
        {
            return new UsvActuatorVisualConfig(
                speedDeadbandMetersPerSecond,
                speedFullScaleMetersPerSecond,
                yawDeadbandDegreesPerSecond,
                yawFullScaleDegreesPerSecond,
                minVisibleRpm,
                cruiseRpm,
                maxVisualRpm,
                maxDifferentialRpm,
                lowSpeedOffMetersPerSecond,
                lowSpeedFullMetersPerSecond,
                maxVisualRudderDegrees);
        }

        private void DisableForConfigurationError()
        {
            RestoreOriginalActuatorState();
            ClearObserverState();
            if (!configurationErrorLogged)
            {
                Debug.LogError(
                    "UsvActuatorVisualCoordinator requires explicit business root, " +
                    "two Spinner, rudder Pivot, Driver and Authority bindings.");
                configurationErrorLogged = true;
            }
            enabled = false;
        }

        private void OnDisable()
        {
            RestoreOriginalActuatorState();
            ClearObserverState();
        }

        private void OnDestroy()
        {
            RestoreOriginalActuatorState();
            ClearObserverState();
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) &&
                   float.IsFinite(value.y) &&
                   float.IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return float.IsFinite(value.x) &&
                   float.IsFinite(value.y) &&
                   float.IsFinite(value.z) &&
                   float.IsFinite(value.w);
        }
    }
}
