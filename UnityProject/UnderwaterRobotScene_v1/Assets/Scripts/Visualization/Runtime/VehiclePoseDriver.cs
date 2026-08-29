using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime.Constraints;
using UnderwaterRobotScene.Visualization.Sampling;
using UnderwaterRobotScene.Visualization.Transforms;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime
{
    [DisallowMultipleComponent]
    public sealed class VehiclePoseDriver : MonoBehaviour
    {
        [Header("Explicit bindings")]
        [SerializeField] private VehicleDataRuntimeHost runtimeHost;
        [SerializeField] private VehiclePoseIntegrationConfiguration integrationConfiguration;
        [SerializeField] private VehiclePoseProfileConfiguration profileConfiguration;
        [SerializeField] private VehiclePoseControlAuthority controlAuthority;
        [SerializeField] private Transform targetRoot;
        [SerializeField] private MonoBehaviour poseConstraintProvider;

        [Header("Runtime observation")]
        [SerializeField] private bool ownsControl;
        [SerializeField] private RenderSampleMode lastSampleMode = RenderSampleMode.None;
        [SerializeField] private RenderSampleFailureReason lastFailureReason = RenderSampleFailureReason.NoData;
        [SerializeField] private string lastFailureMessage = "Not sampled.";
        [SerializeField] private double lastDataAgeSeconds;
        [SerializeField] private double lastSuccessfulMonotonicSeconds = -1.0;
        [SerializeField] private string currentEpoch = "unavailable";
        [SerializeField] private bool hasAppliedPose;
        [SerializeField] private bool hasFreshAppliedPose;
        [SerializeField] private ulong lastAppliedSourceEpoch;
        [SerializeField] private UnityPoseConstraintDecision
            lastPoseConstraintDecision =
                UnityPoseConstraintDecision.NotEvaluated;
        [SerializeField] private string lastPoseConstraintReason = string.Empty;

        private bool hasObservedConstraintEpoch;
        private ulong lastObservedConstraintEpoch;
        private bool hasAcceptedRoutePose;
        private VehicleRoutePose lastAcceptedRoutePose;
        private Vector3 lastAppliedPosition;
        private Quaternion lastAppliedRotation;

        public bool OwnsControl => ownsControl;
        public RenderSampleMode LastSampleMode => lastSampleMode;
        public RenderSampleFailureReason LastFailureReason => lastFailureReason;
        public string LastFailureMessage => lastFailureMessage;
        public double LastDataAgeSeconds => lastDataAgeSeconds;
        public double LastSuccessfulMonotonicSeconds => lastSuccessfulMonotonicSeconds;
        public bool HasAppliedPose => hasAppliedPose;
        public bool HasFreshAppliedPose => hasFreshAppliedPose;
        public ulong LastAppliedSourceEpoch => lastAppliedSourceEpoch;
        public MonoBehaviour PoseConstraintProvider => poseConstraintProvider;
        public bool HasPoseConstraint =>
            poseConstraintProvider is IUnityPoseConstraint;
        public UnityPoseConstraintDecision LastPoseConstraintDecision =>
            lastPoseConstraintDecision;
        public string LastPoseConstraintReason => lastPoseConstraintReason;
        public Transform TargetRoot => targetRoot;
        public VehicleDataRuntimeHost RuntimeHost => runtimeHost;
        public VehiclePoseIntegrationConfiguration IntegrationConfiguration =>
            integrationConfiguration;
        public VehiclePoseProfileConfiguration ProfileConfiguration => profileConfiguration;
        public VehiclePoseControlAuthority ControlAuthority => controlAuthority;
        public string SourceId =>
            integrationConfiguration == null ? string.Empty : integrationConfiguration.SourceId;
        public string VehicleId =>
            integrationConfiguration == null ? string.Empty : integrationConfiguration.VehicleId;

        public bool TryGetLastAppliedPose(
            out Vector3 position,
            out Quaternion rotation,
            out ulong sourceEpoch)
        {
            if (hasAppliedPose && IsFinite(lastAppliedPosition) &&
                IsUsable(lastAppliedRotation))
            {
                position = lastAppliedPosition;
                rotation = lastAppliedRotation;
                sourceEpoch = lastAppliedSourceEpoch;
                return true;
            }

            position = default;
            rotation = default;
            sourceEpoch = 0UL;
            return false;
        }

        public bool TryGetLastAcceptedRoutePose(
            out VehicleRoutePose pose,
            out ulong sourceEpoch)
        {
            if (hasAcceptedRoutePose && hasAppliedPose && hasFreshAppliedPose &&
                isActiveAndEnabled && ownsControl)
            {
                pose = lastAcceptedRoutePose;
                sourceEpoch = lastAppliedSourceEpoch;
                return true;
            }

            pose = default;
            sourceEpoch = 0UL;
            return false;
        }

        public void Configure(
            VehicleDataRuntimeHost host,
            VehiclePoseIntegrationConfiguration configuration,
            VehiclePoseProfileConfiguration profile,
            VehiclePoseControlAuthority authority,
            Transform target)
        {
            runtimeHost = host == null ? throw new System.ArgumentNullException(nameof(host)) : host;
            integrationConfiguration = configuration == null
                ? throw new System.ArgumentNullException(nameof(configuration))
                : configuration;
            profileConfiguration = profile == null
                ? throw new System.ArgumentNullException(nameof(profile))
                : profile;
            controlAuthority = authority == null
                ? throw new System.ArgumentNullException(nameof(authority))
                : authority;
            targetRoot = target == null
                ? throw new System.ArgumentNullException(nameof(target))
                : target;
        }

        public void Configure(
            VehicleDataRuntimeHost host,
            VehiclePoseIntegrationConfiguration configuration,
            VehiclePoseProfileConfiguration profile,
            VehiclePoseControlAuthority authority,
            Transform target,
            MonoBehaviour constraintProvider)
        {
            Configure(host, configuration, profile, authority, target);
            ConfigurePoseConstraint(constraintProvider);
        }

        public void ConfigurePoseConstraint(MonoBehaviour constraintProvider)
        {
            if (constraintProvider != null &&
                !(constraintProvider is IUnityPoseConstraint))
            {
                throw new System.ArgumentException(
                    "The pose constraint provider must implement IUnityPoseConstraint.",
                    nameof(constraintProvider));
            }

            if (ReferenceEquals(poseConstraintProvider, constraintProvider))
            {
                return;
            }

            ResetConstraintObservation();
            poseConstraintProvider = constraintProvider;
            lastPoseConstraintDecision =
                UnityPoseConstraintDecision.NotEvaluated;
            lastPoseConstraintReason = string.Empty;
        }

        private void LateUpdate()
        {
            if (Application.isPlaying)
            {
                TrySampleAndApply(runtimeHost == null
                    ? Time.realtimeSinceStartupAsDouble
                    : runtimeHost.MonotonicNowSeconds);
            }
        }

        private void OnDisable()
        {
            ResetConstraintObservation();
            ownsControl = false;
            hasFreshAppliedPose = false;
            hasAcceptedRoutePose = false;
            lastPoseConstraintDecision =
                UnityPoseConstraintDecision.NotEvaluated;
            lastPoseConstraintReason = string.Empty;
        }

        public bool TrySampleAndApply(double localMonotonicNowSeconds)
        {
            if (!isActiveAndEnabled)
            {
                ownsControl = false;
                hasFreshAppliedPose = false;
                return false;
            }

            bool nextOwnsControl =
                controlAuthority != null;
            if (ownsControl && !nextOwnsControl)
            {
                ResetConstraintObservation();
            }

            ownsControl = nextOwnsControl;
            lastPoseConstraintDecision =
                UnityPoseConstraintDecision.NotEvaluated;
            lastPoseConstraintReason = string.Empty;
            if (!ownsControl)
            {
                hasFreshAppliedPose = false;
                return false;
            }

            if (runtimeHost == null ||
                integrationConfiguration == null ||
                profileConfiguration == null ||
                controlAuthority == null ||
                targetRoot == null)
            {
                return Fail(
                    RenderSampleFailureReason.InvalidRequest,
                    "Host, integration configuration, profile, authority, and target are required.");
            }

            if (!ReferenceEquals(
                    runtimeHost.IntegrationConfiguration,
                    integrationConfiguration))
            {
                return Fail(
                    RenderSampleFailureReason.InvalidRequest,
                    "Host and Driver must share one Integration Configuration.");
            }

            VehicleRuntimeSourceMode desiredSourceMode =
                controlAuthority.PublicDataOwnsControl
                    ? VehicleRuntimeSourceMode.RouteFollowing
                    : VehicleRuntimeSourceMode.LocalDiagnostic;
            if (runtimeHost.AuthoritySourceSelectionEnabled &&
                runtimeHost.SourceMode != desiredSourceMode &&
                hasAcceptedRoutePose && hasAppliedPose &&
                hasFreshAppliedPose)
            {
                if (!runtimeHost.TrySelectAuthoritySourceMode(
                        desiredSourceMode,
                        in lastAcceptedRoutePose,
                        lastAppliedSourceEpoch,
                        localMonotonicNowSeconds,
                        out string transitionError))
                {
                    return Fail(
                        RenderSampleFailureReason.InvalidRequest,
                        transitionError);
                }
            }

            if (!profileConfiguration.TryBuildProfile(
                    out CoordinateTransformProfile profile,
                    out string profileError))
            {
                return Fail(RenderSampleFailureReason.ConversionFailed, profileError);
            }

            if (!integrationConfiguration.TryValidate(out string configurationError))
            {
                return Fail(RenderSampleFailureReason.InvalidRequest, configurationError);
            }

            if (!runtimeHost.TryGetActiveEpoch(out ulong epoch))
            {
                currentEpoch = "unavailable";
                return Fail(RenderSampleFailureReason.EpochUnavailable, "The runtime source has no active epoch.");
            }

            currentEpoch = epoch.ToString();
            if (!runtimeHost.TryGetLatestSourceTimestamp(out double latestSourceTimestamp))
            {
                return Fail(RenderSampleFailureReason.NoData, "The active epoch has no vehicle state.");
            }

            double targetSourceTimestamp = runtimeHost.GetTargetSourceTimestamp(
                localMonotonicNowSeconds,
                integrationConfiguration.RenderDelaySeconds);

            RenderSamplingPolicy policy = integrationConfiguration.BuildSamplingPolicy();
            var request = new RenderSampleRequest(
                integrationConfiguration.SourceId,
                epoch,
                integrationConfiguration.VehicleId,
                targetSourceTimestamp,
                localMonotonicNowSeconds,
                runtimeHost.SourceStatus,
                profile,
                policy);
            RenderPoseSample sample = VehicleRenderSampler.Sample(runtimeHost.Store, request);
            lastSampleMode = sample.Mode;
            lastFailureReason = sample.FailureReason;
            lastFailureMessage = sample.Message;
            lastDataAgeSeconds = sample.HasSourceHealth ? sample.LocalDataAgeSeconds : 0.0;
            if (!sample.Succeeded)
            {
                hasFreshAppliedPose = false;
                return false;
            }

            if (!UnityPoseAdapter.TryConvert(
                    sample.Position,
                    sample.Orientation,
                    out Vector3 position,
                    out Quaternion orientation))
            {
                return Fail(
                    RenderSampleFailureReason.ConversionFailed,
                    "Unity Pose Adapter rejected a non-finite or unusable pose.");
            }

            IUnityPoseConstraint poseConstraint =
                poseConstraintProvider as IUnityPoseConstraint;
            if (poseConstraint != null)
            {
                if (!hasObservedConstraintEpoch ||
                    lastObservedConstraintEpoch != epoch)
                {
                    poseConstraint.ResetObservation();
                    hasObservedConstraintEpoch = true;
                    lastObservedConstraintEpoch = epoch;
                }

                var constraintRequest = new UnityPoseConstraintRequest(
                    position,
                    orientation,
                    epoch);
                UnityPoseConstraintResult constraintResult =
                    poseConstraint.Constrain(in constraintRequest);
                lastPoseConstraintDecision = constraintResult.Decision;
                lastPoseConstraintReason =
                    constraintResult.Reason ?? string.Empty;
                if (constraintResult.Decision ==
                    UnityPoseConstraintDecision.HoldCurrent)
                {
                    runtimeHost.NotifyConstraintHold(
                        epoch,
                        targetRoot.position,
                        targetRoot.rotation,
                        localMonotonicNowSeconds);
                    hasFreshAppliedPose = false;
                    lastFailureReason = RenderSampleFailureReason.None;
                    lastFailureMessage = string.Empty;
                    return false;
                }

                if (constraintResult.Decision !=
                    UnityPoseConstraintDecision.Apply)
                {
                    return Fail(
                        RenderSampleFailureReason.InvalidRequest,
                        "The pose constraint returned an unsupported decision.");
                }

                if (!IsFinite(constraintResult.Position) ||
                    !IsUsable(constraintResult.Rotation))
                {
                    return Fail(
                        RenderSampleFailureReason.ConversionFailed,
                        "The pose constraint returned a non-finite or unusable pose.");
                }

                position = constraintResult.Position;
                orientation = constraintResult.Rotation;
            }

            VehicleRoutePose acceptedRoutePose = default;
            if (!TryConvertAcceptedRoutePose(
                    profile, position, orientation, out acceptedRoutePose))
            {
                return Fail(
                    RenderSampleFailureReason.ConversionFailed,
                    "The final accepted Unity pose could not be converted back to route coordinates.");
            }
            targetRoot.SetPositionAndRotation(position, orientation);
            lastAppliedPosition = position;
            lastAppliedRotation = orientation;
            lastAcceptedRoutePose = acceptedRoutePose;
            lastSuccessfulMonotonicSeconds = localMonotonicNowSeconds;
            hasAppliedPose = true;
            hasAcceptedRoutePose = true;
            hasFreshAppliedPose = true;
            lastAppliedSourceEpoch = epoch;
            lastFailureReason = RenderSampleFailureReason.None;
            lastFailureMessage = string.Empty;
            return true;
        }

        private bool Fail(RenderSampleFailureReason reason, string message)
        {
            lastSampleMode = RenderSampleMode.None;
            lastFailureReason = reason;
            lastFailureMessage = message;
            hasFreshAppliedPose = false;
            return false;
        }

        private void ResetConstraintObservation()
        {
            if (poseConstraintProvider is IUnityPoseConstraint constraint)
            {
                constraint.ResetObservation();
            }

            hasObservedConstraintEpoch = false;
            lastObservedConstraintEpoch = 0UL;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                IsFinite(value.y) &&
                IsFinite(value.z);
        }

        private static bool IsUsable(Quaternion value)
        {
            if (!IsFinite(value.x) || !IsFinite(value.y) ||
                !IsFinite(value.z) || !IsFinite(value.w))
            {
                return false;
            }

            double squaredMagnitude =
                (double)value.x * value.x +
                (double)value.y * value.y +
                (double)value.z * value.z +
                (double)value.w * value.w;
            return !double.IsNaN(squaredMagnitude) &&
                !double.IsInfinity(squaredMagnitude) &&
                squaredMagnitude > 1e-12;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool TryConvertAcceptedRoutePose(
            CoordinateTransformProfile profile,
            Vector3 position,
            Quaternion orientation,
            out VehicleRoutePose pose)
        {
            pose = default;
            if (!IsFinite(position) || !IsUsable(orientation) ||
                !Numeric.IsFinite(profile.PositionScale) ||
                profile.PositionScale <= 0.0)
            {
                return false;
            }

            Quaternion alignment = new Quaternion(
                (float)profile.ModelAlignment.X,
                (float)profile.ModelAlignment.Y,
                (float)profile.ModelAlignment.Z,
                (float)profile.ModelAlignment.W);
            Quaternion sourceRotation =
                orientation * Quaternion.Inverse(alignment);
            var converted = new VehicleRoutePose(
                new Vector3d(
                    position.x / profile.PositionScale,
                    position.y / profile.PositionScale,
                    position.z / profile.PositionScale),
                new Quaterniond(
                    sourceRotation.x,
                    sourceRotation.y,
                    sourceRotation.z,
                    sourceRotation.w));
            if (!converted.Position.IsFinite || !converted.Orientation.IsUsable)
            {
                return false;
            }

            pose = converted;
            return true;
        }
    }
}
