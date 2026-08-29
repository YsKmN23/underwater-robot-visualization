using UnderwaterRobotScene.Visualization.Sampling;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime.Usv
{
    public enum UsvSurfaceVisualMode
    {
        Disabled = 0,
        LocalDiagnosticPublicData = 1
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1100)]
    public sealed class UsvSurfaceVisualController : MonoBehaviour
    {
        [Header("Explicit bindings")]
        [SerializeField] private Transform businessRoot;
        [SerializeField] private Transform importedModelRoot;
        [SerializeField] private FlatWaterSurfaceProvider waterSurfaceProvider;
        [SerializeField] private VehiclePoseDriver poseDriver;
        [SerializeField] private VehiclePoseControlAuthority controlAuthority;

        [Header("Explicit diagnostic gate")]
        [SerializeField] private UsvSurfaceVisualMode mode = UsvSurfaceVisualMode.Disabled;

        [Header("Diagnostic visual calibration only")]
        [SerializeField] private float diagnosticNeutralRootHeightAboveSurface = 0.18f;
        [SerializeField] private float maxSurfaceCorrection = 0.05f;

        [Header("Deterministic diagnostic visual motion")]
        [SerializeField] private float diagnosticPeriodSeconds = 8f;
        [SerializeField] private float heaveAmplitudeMeters = 0.015f;
        [SerializeField] private float pitchAmplitudeDegrees = 0.8f;
        [SerializeField] private float rollAmplitudeDegrees = 1.2f;
        [SerializeField] private float activationFadeSeconds = 0.75f;

        [Header("Reset thresholds")]
        [SerializeField] private float teleportDistanceMeters = 0.25f;
        [SerializeField] private float teleportAngleDegrees = 30f;
        [SerializeField] private float maxAcceptedDeltaTimeSeconds = 0.25f;

        private Vector3 originalLocalPosition;
        private Quaternion originalLocalRotation;
        private bool hasOriginalPose;
        private bool hasBaseline;
        private Vector3 previousBusinessPosition;
        private Quaternion previousBusinessRotation;
        private ulong activeEpoch;
        private double elapsedSeconds;
        private bool diagnosticActive;

        public Transform BusinessRoot => businessRoot;
        public Transform ImportedModelRoot => importedModelRoot;
        public FlatWaterSurfaceProvider WaterSurfaceProvider => waterSurfaceProvider;
        public VehiclePoseDriver PoseDriver => poseDriver;
        public VehiclePoseControlAuthority ControlAuthority => controlAuthority;
        public UsvSurfaceVisualMode Mode
        {
            get => mode;
            set => mode = value;
        }
        public float DiagnosticNeutralRootHeightAboveSurface =>
            diagnosticNeutralRootHeightAboveSurface;
        public float MaxSurfaceCorrection => maxSurfaceCorrection;
        public float DiagnosticPeriodSeconds => diagnosticPeriodSeconds;
        public float HeaveAmplitudeMeters => heaveAmplitudeMeters;
        public float PitchAmplitudeDegrees => pitchAmplitudeDegrees;
        public float RollAmplitudeDegrees => rollAmplitudeDegrees;
        public float ActivationFadeSeconds => activationFadeSeconds;
        public float TeleportDistanceMeters => teleportDistanceMeters;
        public float TeleportAngleDegrees => teleportAngleDegrees;
        public float MaxAcceptedDeltaTimeSeconds => maxAcceptedDeltaTimeSeconds;
        public bool DiagnosticActive => diagnosticActive;
        public double ElapsedSeconds => elapsedSeconds;

        public void Configure(
            Transform configuredBusinessRoot,
            Transform configuredImportedModelRoot,
            FlatWaterSurfaceProvider configuredProvider,
            VehiclePoseDriver configuredDriver,
            VehiclePoseControlAuthority configuredAuthority)
        {
            businessRoot = configuredBusinessRoot;
            importedModelRoot = configuredImportedModelRoot;
            waterSurfaceProvider = configuredProvider;
            poseDriver = configuredDriver;
            controlAuthority = configuredAuthority;
        }

        private void OnEnable()
        {
            originalLocalPosition = transform.localPosition;
            originalLocalRotation = transform.localRotation;
            hasOriginalPose = true;
            ResetToIdentity(true);
        }

        private void LateUpdate()
        {
            TickForDiagnostics(Time.unscaledDeltaTime);
        }

        public void TickForDiagnostics(double deltaTimeSeconds)
        {
            if (!CanRunDiagnostic(deltaTimeSeconds))
            {
                ResetToIdentity(true);
                return;
            }

            Vector3 businessPosition = businessRoot.position;
            Quaternion businessRotation = businessRoot.rotation;
            ulong currentEpoch = poseDriver.LastAppliedSourceEpoch;
            if (!hasBaseline)
            {
                EstablishBaseline(businessPosition, businessRotation, currentEpoch);
                ResetToIdentity(false);
                return;
            }

            if (currentEpoch != activeEpoch ||
                Vector3.Distance(previousBusinessPosition, businessPosition) >
                    teleportDistanceMeters ||
                Quaternion.Angle(previousBusinessRotation, businessRotation) >
                    teleportAngleDegrees)
            {
                EstablishBaseline(businessPosition, businessRotation, currentEpoch);
                ResetToIdentity(false);
                return;
            }

            previousBusinessPosition = businessPosition;
            previousBusinessRotation = businessRotation;
            if (!waterSurfaceProvider.TrySample(
                    businessPosition,
                    out Vector3 surfacePoint,
                    out Vector3 surfaceNormal))
            {
                ResetToIdentity(true);
                return;
            }

            float signedDistance =
                Vector3.Dot(businessPosition - surfacePoint, surfaceNormal);
            float surfaceCorrection =
                diagnosticNeutralRootHeightAboveSurface - signedDistance;
            if (!float.IsFinite(signedDistance) ||
                !float.IsFinite(surfaceCorrection) ||
                Mathf.Abs(surfaceCorrection) > maxSurfaceCorrection)
            {
                ResetToIdentity(true);
                return;
            }

            elapsedSeconds += deltaTimeSeconds;
            if (!DeterministicUsvDiagnosticVisualMotion.TryEvaluate(
                    elapsedSeconds,
                    diagnosticPeriodSeconds,
                    heaveAmplitudeMeters,
                    pitchAmplitudeDegrees,
                    rollAmplitudeDegrees,
                    activationFadeSeconds,
                    out UsvDiagnosticVisualMotionSample motion))
            {
                ResetToIdentity(true);
                return;
            }

            Vector3 localPosition =
                Vector3.up * (surfaceCorrection + motion.HeightOffsetMeters);
            Quaternion localRotation =
                Quaternion.Euler(motion.PitchDegrees, 0f, motion.RollDegrees);
            if (!IsFinite(localPosition) || !IsFinite(localRotation))
            {
                ResetToIdentity(true);
                return;
            }

            transform.SetLocalPositionAndRotation(localPosition, localRotation);
            diagnosticActive = true;
        }

        private bool CanRunDiagnostic(double deltaTimeSeconds)
        {
            return mode == UsvSurfaceVisualMode.LocalDiagnosticPublicData &&
                   controlAuthority != null &&
                   poseDriver != null &&
                   poseDriver.RuntimeHost != null &&
                   poseDriver.RuntimeHost.SourceMode ==
                       VehicleRuntimeSourceMode.LocalDiagnostic &&
                   poseDriver.isActiveAndEnabled &&
                   poseDriver.OwnsControl &&
                   poseDriver.HasAppliedPose &&
                   poseDriver.HasFreshAppliedPose &&
                   poseDriver.LastFailureReason == RenderSampleFailureReason.None &&
                   poseDriver.TargetRoot == businessRoot &&
                   businessRoot != null &&
                   importedModelRoot != null &&
                   importedModelRoot.parent == transform &&
                   waterSurfaceProvider != null &&
                   waterSurfaceProvider.isActiveAndEnabled &&
                   IsFinite(businessRoot.position) &&
                   IsFinite(businessRoot.rotation) &&
                   !double.IsNaN(deltaTimeSeconds) &&
                   !double.IsInfinity(deltaTimeSeconds) &&
                   deltaTimeSeconds >= 0.0 &&
                   deltaTimeSeconds <= maxAcceptedDeltaTimeSeconds;
        }

        private void EstablishBaseline(
            Vector3 businessPosition,
            Quaternion businessRotation,
            ulong epoch)
        {
            previousBusinessPosition = businessPosition;
            previousBusinessRotation = businessRotation;
            activeEpoch = epoch;
            elapsedSeconds = 0.0;
            hasBaseline = true;
        }

        private void ResetToIdentity(bool clearBaseline)
        {
            transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            diagnosticActive = false;
            elapsedSeconds = 0.0;
            if (clearBaseline)
            {
                hasBaseline = false;
                activeEpoch = default;
                previousBusinessPosition = default;
                previousBusinessRotation = Quaternion.identity;
            }
        }

        private void OnDisable()
        {
            RestoreOriginalPose();
        }

        private void OnDestroy()
        {
            RestoreOriginalPose();
        }

        private void RestoreOriginalPose()
        {
            diagnosticActive = false;
            elapsedSeconds = 0.0;
            hasBaseline = false;
            if (hasOriginalPose)
            {
                transform.SetLocalPositionAndRotation(
                    originalLocalPosition,
                    originalLocalRotation);
            }
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
