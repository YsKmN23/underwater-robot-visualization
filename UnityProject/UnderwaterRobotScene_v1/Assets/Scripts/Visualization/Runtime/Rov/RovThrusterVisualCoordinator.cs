using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime.Rov
{
    public enum RovThrusterVisualRole
    {
        SurgeVisualRight = 0,
        SurgeVisualLeft = 1,
        HeaveVisualRight = 2,
        HeaveVisualLeft = 3,
        SwayFront = 4,
        SwayRear = 5
    }

    /// <summary>
    /// VISUAL_ONLY linkage from observed ROV root motion to propeller animation RPM.
    /// This component does not allocate thrust and does not write any Transform.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    public sealed class RovThrusterVisualCoordinator : MonoBehaviour
    {
        [Header("Explicit VISUAL_ONLY role bindings")]
        [SerializeField] private PropellerSpinner surgeVisualRightSpinner;
        [SerializeField] private PropellerSpinner surgeVisualLeftSpinner;
        [SerializeField] private PropellerSpinner heaveVisualRightSpinner;
        [SerializeField] private PropellerSpinner heaveVisualLeftSpinner;
        [SerializeField] private PropellerSpinner swayFrontSpinner;
        [SerializeField] private PropellerSpinner swayRearSpinner;

        [Header("VISUAL_ONLY RPM")]
        [SerializeField] private float visualIdleRpm;
        [SerializeField] private float surgeMaxVisualRpm = 720f;
        [SerializeField] private float heaveMaxVisualRpm = 680f;
        [SerializeField] private float swayMaxVisualRpm = 700f;

        [Header("VISUAL_ONLY linear motion")]
        [SerializeField] private float linearDeadZone = 0.005f;
        [SerializeField] private float surgeFullScaleSpeed = 0.35f;
        [SerializeField] private float heaveFullScaleSpeed = 0.15f;
        [SerializeField] private float swayFullScaleSpeed = 0.27f;

        [Header("VISUAL_ONLY angular motion")]
        [SerializeField] private float angularDeadZoneDegreesPerSecond = 0.5f;
        [SerializeField] private float angularFullScaleDegreesPerSecond = 30f;
        [SerializeField] private float angularGlobalWeight = 0.20f;

        [Header("VISUAL_ONLY RPM smoothing")]
        [SerializeField] private float rpmRiseRatePerSecond = 1800f;
        [SerializeField] private float rpmFallRatePerSecond = 2400f;

        [Header("VISUAL_ONLY time and discontinuity")]
        [SerializeField] private float maxValidDeltaTime = 0.25f;
        [SerializeField] private float teleportDistanceThreshold = 0.25f;
        [SerializeField] private float teleportAngleThresholdDegrees = 30f;

        private bool runtimeInitialized;
        private bool originalRpmCached;
        private bool originalRpmRestored;
        private bool hasPreviousPose;
        private Vector3 previousPosition;
        private Quaternion previousRotation;
        private float originalSurgeVisualRightRpm;
        private float originalSurgeVisualLeftRpm;
        private float originalHeaveVisualRightRpm;
        private float originalHeaveVisualLeftRpm;
        private float originalSwayFrontRpm;
        private float originalSwayRearRpm;
        private bool lastFrameWasDiscontinuity;
        private bool lastFrameHadInvalidInput;

        public PropellerSpinner SurgeVisualRightSpinner => surgeVisualRightSpinner;
        public PropellerSpinner SurgeVisualLeftSpinner => surgeVisualLeftSpinner;
        public PropellerSpinner HeaveVisualRightSpinner => heaveVisualRightSpinner;
        public PropellerSpinner HeaveVisualLeftSpinner => heaveVisualLeftSpinner;
        public PropellerSpinner SwayFrontSpinner => swayFrontSpinner;
        public PropellerSpinner SwayRearSpinner => swayRearSpinner;
        public float VisualIdleRpm => visualIdleRpm;
        public float SurgeMaxVisualRpm => surgeMaxVisualRpm;
        public float HeaveMaxVisualRpm => heaveMaxVisualRpm;
        public float SwayMaxVisualRpm => swayMaxVisualRpm;
        public float LinearDeadZone => linearDeadZone;
        public float SurgeFullScaleSpeed => surgeFullScaleSpeed;
        public float HeaveFullScaleSpeed => heaveFullScaleSpeed;
        public float SwayFullScaleSpeed => swayFullScaleSpeed;
        public float AngularDeadZoneDegreesPerSecond => angularDeadZoneDegreesPerSecond;
        public float AngularFullScaleDegreesPerSecond => angularFullScaleDegreesPerSecond;
        public float AngularGlobalWeight => angularGlobalWeight;
        public float RpmRiseRatePerSecond => rpmRiseRatePerSecond;
        public float RpmFallRatePerSecond => rpmFallRatePerSecond;
        public float MaxValidDeltaTime => maxValidDeltaTime;
        public float TeleportDistanceThreshold => teleportDistanceThreshold;
        public float TeleportAngleThresholdDegrees => teleportAngleThresholdDegrees;
        public bool RuntimeInitialized => runtimeInitialized;
        public bool HasPreviousPose => hasPreviousPose;
        public bool LastFrameWasDiscontinuity => lastFrameWasDiscontinuity;
        public bool LastFrameHadInvalidInput => lastFrameHadInvalidInput;
        public bool OriginalRpmCached => originalRpmCached;

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (!TryInitializeRuntime(out string error))
            {
                Debug.LogError("ROV VISUAL_ONLY thruster linkage disabled: " + error, this);
                RestoreOriginalRpm();
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (!runtimeInitialized)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            Vector3 currentPosition = transform.position;
            Quaternion currentRotation = transform.rotation;
            lastFrameWasDiscontinuity = false;
            lastFrameHadInvalidInput = false;

            if (!IsFinite(deltaTime) ||
                deltaTime <= 0f ||
                deltaTime > maxValidDeltaTime ||
                !IsFinite(currentPosition) ||
                !IsUsable(currentRotation))
            {
                lastFrameHadInvalidInput = true;
                ApplyImmediateIdle();
                hasPreviousPose = false;
                return;
            }

            if (!hasPreviousPose)
            {
                previousPosition = currentPosition;
                previousRotation = currentRotation.normalized;
                hasPreviousPose = true;
                ApplySmoothedTargets(
                    visualIdleRpm,
                    visualIdleRpm,
                    visualIdleRpm,
                    deltaTime);
                return;
            }

            bool valid = TryEvaluatePoseForDiagnostics(
                previousPosition,
                previousRotation,
                currentPosition,
                currentRotation,
                deltaTime,
                out Vector3 targetRpm,
                out bool discontinuity);
            if (!valid)
            {
                lastFrameHadInvalidInput = true;
                ApplyImmediateIdle();
                hasPreviousPose = false;
                return;
            }

            previousPosition = currentPosition;
            previousRotation = currentRotation.normalized;
            lastFrameWasDiscontinuity = discontinuity;
            if (discontinuity)
            {
                ApplySmoothedTargets(
                    visualIdleRpm,
                    visualIdleRpm,
                    visualIdleRpm,
                    deltaTime);
                return;
            }

            ApplySmoothedTargets(targetRpm.x, targetRpm.y, targetRpm.z, deltaTime);
        }

        private void OnDisable()
        {
            RestoreOriginalRpm();
            runtimeInitialized = false;
            hasPreviousPose = false;
        }

        private void OnDestroy()
        {
            RestoreOriginalRpm();
        }

        /// <summary>
        /// Side-effect-free VISUAL_ONLY calculation entry used by the dedicated verifier.
        /// Vector3 components are Surge, Heave, and Sway target RPM.
        /// </summary>
        public bool TryEvaluatePoseForDiagnostics(
            Vector3 fromPosition,
            Quaternion fromRotation,
            Vector3 toPosition,
            Quaternion toRotation,
            float deltaTime,
            out Vector3 targetRpm,
            out bool discontinuity)
        {
            targetRpm = new Vector3(visualIdleRpm, visualIdleRpm, visualIdleRpm);
            discontinuity = false;
            if (!TryValidateSettings(out _) ||
                !IsFinite(deltaTime) ||
                deltaTime <= 0f ||
                deltaTime > maxValidDeltaTime ||
                !IsFinite(fromPosition) ||
                !IsFinite(toPosition) ||
                !IsUsable(fromRotation) ||
                !IsUsable(toRotation))
            {
                return false;
            }

            Quaternion normalizedFrom = fromRotation.normalized;
            Quaternion normalizedTo = toRotation.normalized;
            Vector3 worldDelta = toPosition - fromPosition;
            float angleDelta = Quaternion.Angle(normalizedFrom, normalizedTo);
            if (!IsFinite(worldDelta) || !IsFinite(angleDelta))
            {
                return false;
            }

            if (worldDelta.magnitude > teleportDistanceThreshold ||
                angleDelta > teleportAngleThresholdDegrees)
            {
                discontinuity = true;
                return true;
            }

            Vector3 localDelta = Quaternion.Inverse(normalizedFrom) * worldDelta;
            Vector3 localVelocity = localDelta / deltaTime;
            float angularSpeed = angleDelta / deltaTime;
            if (!IsFinite(localVelocity) || !IsFinite(angularSpeed))
            {
                return false;
            }

            float surgeActivity = NormalizeMagnitude(
                Mathf.Abs(localVelocity.x),
                linearDeadZone,
                surgeFullScaleSpeed);
            float heaveActivity = NormalizeMagnitude(
                Mathf.Abs(localVelocity.y),
                linearDeadZone,
                heaveFullScaleSpeed);
            float swayActivity = NormalizeMagnitude(
                Mathf.Abs(localVelocity.z),
                linearDeadZone,
                swayFullScaleSpeed);
            float angularActivity = NormalizeMagnitude(
                angularSpeed,
                angularDeadZoneDegreesPerSecond,
                angularFullScaleDegreesPerSecond);
            float angularContribution = angularGlobalWeight * angularActivity;

            float surgeFinal = Mathf.Clamp01(surgeActivity + angularContribution);
            float heaveFinal = Mathf.Clamp01(heaveActivity + angularContribution);
            float swayFinal = Mathf.Clamp01(swayActivity + angularContribution);
            targetRpm = new Vector3(
                ClampFinite(
                    Mathf.Lerp(visualIdleRpm, surgeMaxVisualRpm, surgeFinal),
                    visualIdleRpm,
                    surgeMaxVisualRpm),
                ClampFinite(
                    Mathf.Lerp(visualIdleRpm, heaveMaxVisualRpm, heaveFinal),
                    visualIdleRpm,
                    heaveMaxVisualRpm),
                ClampFinite(
                    Mathf.Lerp(visualIdleRpm, swayMaxVisualRpm, swayFinal),
                    visualIdleRpm,
                    swayMaxVisualRpm));
            return IsFinite(targetRpm);
        }

        private bool TryInitializeRuntime(out string error)
        {
            runtimeInitialized = false;
            hasPreviousPose = false;
            lastFrameWasDiscontinuity = false;
            lastFrameHadInvalidInput = false;
            originalRpmCached = false;
            originalRpmRestored = false;

            if (!TryValidateBindings(out error) || !TryValidateSettings(out error))
            {
                return false;
            }

            originalSurgeVisualRightRpm = surgeVisualRightSpinner.rpm;
            originalSurgeVisualLeftRpm = surgeVisualLeftSpinner.rpm;
            originalHeaveVisualRightRpm = heaveVisualRightSpinner.rpm;
            originalHeaveVisualLeftRpm = heaveVisualLeftSpinner.rpm;
            originalSwayFrontRpm = swayFrontSpinner.rpm;
            originalSwayRearRpm = swayRearSpinner.rpm;
            originalRpmCached = true;

            ApplyImmediateIdle();
            runtimeInitialized = true;
            error = string.Empty;
            return true;
        }

        private bool TryValidateBindings(out string error)
        {
            PropellerSpinner[] values =
            {
                surgeVisualRightSpinner,
                surgeVisualLeftSpinner,
                heaveVisualRightSpinner,
                heaveVisualLeftSpinner,
                swayFrontSpinner,
                swayRearSpinner
            };
            if (Array.Exists(values, value => value == null))
            {
                error = "All six explicit PropellerSpinner references are required.";
                return false;
            }

            var unique = new HashSet<PropellerSpinner>(values);
            if (unique.Count != values.Length)
            {
                error = "All six explicit PropellerSpinner references must be unique.";
                return false;
            }

            if (Array.Exists(values, value => !IsFinite(value.rpm)))
            {
                error = "All six original Scene RPM values must be finite.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private bool TryValidateSettings(out string error)
        {
            float[] finiteValues =
            {
                visualIdleRpm,
                surgeMaxVisualRpm,
                heaveMaxVisualRpm,
                swayMaxVisualRpm,
                linearDeadZone,
                surgeFullScaleSpeed,
                heaveFullScaleSpeed,
                swayFullScaleSpeed,
                angularDeadZoneDegreesPerSecond,
                angularFullScaleDegreesPerSecond,
                angularGlobalWeight,
                rpmRiseRatePerSecond,
                rpmFallRatePerSecond,
                maxValidDeltaTime,
                teleportDistanceThreshold,
                teleportAngleThresholdDegrees
            };
            if (Array.Exists(finiteValues, value => !IsFinite(value)) ||
                visualIdleRpm < 0f ||
                surgeMaxVisualRpm < visualIdleRpm ||
                heaveMaxVisualRpm < visualIdleRpm ||
                swayMaxVisualRpm < visualIdleRpm ||
                linearDeadZone < 0f ||
                surgeFullScaleSpeed <= linearDeadZone ||
                heaveFullScaleSpeed <= linearDeadZone ||
                swayFullScaleSpeed <= linearDeadZone ||
                angularDeadZoneDegreesPerSecond < 0f ||
                angularFullScaleDegreesPerSecond <= angularDeadZoneDegreesPerSecond ||
                angularGlobalWeight < 0f ||
                angularGlobalWeight > 1f ||
                rpmRiseRatePerSecond <= 0f ||
                rpmFallRatePerSecond <= 0f ||
                maxValidDeltaTime <= 0f ||
                teleportDistanceThreshold <= 0f ||
                teleportAngleThresholdDegrees <= 0f)
            {
                error = "One or more VISUAL_ONLY configuration values are invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void ApplySmoothedTargets(
            float surgeTarget,
            float heaveTarget,
            float swayTarget,
            float deltaTime)
        {
            if (!IsFinite(deltaTime) || deltaTime <= 0f)
            {
                ApplyImmediateIdle();
                return;
            }

            float nextSurge = MoveGroupTowards(
                surgeVisualRightSpinner.rpm,
                surgeVisualLeftSpinner.rpm,
                surgeTarget,
                surgeMaxVisualRpm,
                deltaTime);
            float nextHeave = MoveGroupTowards(
                heaveVisualRightSpinner.rpm,
                heaveVisualLeftSpinner.rpm,
                heaveTarget,
                heaveMaxVisualRpm,
                deltaTime);
            float nextSway = MoveGroupTowards(
                swayFrontSpinner.rpm,
                swayRearSpinner.rpm,
                swayTarget,
                swayMaxVisualRpm,
                deltaTime);
            SetPair(surgeVisualRightSpinner, surgeVisualLeftSpinner, nextSurge);
            SetPair(heaveVisualRightSpinner, heaveVisualLeftSpinner, nextHeave);
            SetPair(swayFrontSpinner, swayRearSpinner, nextSway);
        }

        private float MoveGroupTowards(
            float firstCurrent,
            float secondCurrent,
            float target,
            float maximum,
            float deltaTime)
        {
            float safeFirst = ClampFinite(firstCurrent, visualIdleRpm, maximum);
            float safeSecond = ClampFinite(secondCurrent, visualIdleRpm, maximum);
            float current = (safeFirst + safeSecond) * 0.5f;
            float safeTarget = ClampFinite(target, visualIdleRpm, maximum);
            float rate = safeTarget > current
                ? rpmRiseRatePerSecond
                : rpmFallRatePerSecond;
            float maximumDelta = rate * deltaTime;
            if (!IsFinite(maximumDelta) || maximumDelta < 0f)
            {
                return visualIdleRpm;
            }

            return ClampFinite(
                Mathf.MoveTowards(current, safeTarget, maximumDelta),
                visualIdleRpm,
                maximum);
        }

        private void ApplyImmediateIdle()
        {
            if (surgeVisualRightSpinner != null) surgeVisualRightSpinner.rpm = visualIdleRpm;
            if (surgeVisualLeftSpinner != null) surgeVisualLeftSpinner.rpm = visualIdleRpm;
            if (heaveVisualRightSpinner != null) heaveVisualRightSpinner.rpm = visualIdleRpm;
            if (heaveVisualLeftSpinner != null) heaveVisualLeftSpinner.rpm = visualIdleRpm;
            if (swayFrontSpinner != null) swayFrontSpinner.rpm = visualIdleRpm;
            if (swayRearSpinner != null) swayRearSpinner.rpm = visualIdleRpm;
        }

        private void RestoreOriginalRpm()
        {
            if (!originalRpmCached || originalRpmRestored)
            {
                return;
            }

            if (surgeVisualRightSpinner != null)
                surgeVisualRightSpinner.rpm = originalSurgeVisualRightRpm;
            if (surgeVisualLeftSpinner != null)
                surgeVisualLeftSpinner.rpm = originalSurgeVisualLeftRpm;
            if (heaveVisualRightSpinner != null)
                heaveVisualRightSpinner.rpm = originalHeaveVisualRightRpm;
            if (heaveVisualLeftSpinner != null)
                heaveVisualLeftSpinner.rpm = originalHeaveVisualLeftRpm;
            if (swayFrontSpinner != null)
                swayFrontSpinner.rpm = originalSwayFrontRpm;
            if (swayRearSpinner != null)
                swayRearSpinner.rpm = originalSwayRearRpm;
            originalRpmRestored = true;
        }

        private static void SetPair(
            PropellerSpinner first,
            PropellerSpinner second,
            float value)
        {
            first.rpm = value;
            second.rpm = value;
        }

        private static float NormalizeMagnitude(
            float value,
            float deadZone,
            float fullScale)
        {
            if (!IsFinite(value) ||
                !IsFinite(deadZone) ||
                !IsFinite(fullScale) ||
                fullScale <= deadZone ||
                value <= deadZone)
            {
                return 0f;
            }

            return Mathf.Clamp01((value - deadZone) / (fullScale - deadZone));
        }

        private static float ClampFinite(float value, float minimum, float maximum)
        {
            return IsFinite(value)
                ? Mathf.Clamp(value, minimum, maximum)
                : minimum;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsUsable(Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w) &&
                   value.x * value.x +
                   value.y * value.y +
                   value.z * value.z +
                   value.w * value.w > 0.000001f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
