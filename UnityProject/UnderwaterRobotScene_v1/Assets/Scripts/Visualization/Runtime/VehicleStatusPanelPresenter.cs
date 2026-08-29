using System;
using System.Globalization;
using System.Text;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Sampling;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime
{
    [DisallowMultipleComponent]
    public sealed class VehicleStatusPanelPresenter : MonoBehaviour
    {
        private enum VehicleDisplayState
        {
            Invalid,
            Disabled,
            NoData,
            Stale,
            Normal
        }

        [Serializable]
        private sealed class VehicleBinding
        {
            [SerializeField] private VehicleType expectedType;
            [SerializeField] private VehicleDataRuntimeHost host;
            [SerializeField] private VehiclePoseDriver driver;
            [SerializeField] private VehiclePoseControlAuthority authority;

            public VehicleType ExpectedType => expectedType;
            public VehicleDataRuntimeHost Host => host;
            public VehiclePoseDriver Driver => driver;
            public VehiclePoseControlAuthority Authority => authority;

            public void Configure(
                VehicleType type,
                VehicleDataRuntimeHost configuredHost,
                VehiclePoseDriver configuredDriver,
                VehiclePoseControlAuthority configuredAuthority)
            {
                expectedType = type;
                host = configuredHost;
                driver = configuredDriver;
                authority = configuredAuthority;
            }

            public bool Matches(
                VehicleType type,
                VehicleDataRuntimeHost configuredHost,
                VehiclePoseDriver configuredDriver,
                VehiclePoseControlAuthority configuredAuthority)
            {
                return expectedType == type &&
                       ReferenceEquals(host, configuredHost) &&
                       ReferenceEquals(driver, configuredDriver) &&
                       ReferenceEquals(authority, configuredAuthority);
            }
        }

        private readonly struct VehiclePresentation
        {
            public VehiclePresentation(
                VehicleType vehicleType,
                string vehicleId,
                VehicleDisplayState state,
                VehiclePoseControlMode authorityMode,
                bool hasPose,
                Vector3 position,
                Vector3 rotation)
            {
                VehicleType = vehicleType;
                VehicleId = vehicleId ?? string.Empty;
                State = state;
                AuthorityMode = authorityMode;
                HasPose = hasPose;
                Position = position;
                Rotation = rotation;
            }

            public VehicleType VehicleType { get; }
            public string VehicleId { get; }
            public VehicleDisplayState State { get; }
            public VehiclePoseControlMode AuthorityMode { get; }
            public bool HasPose { get; }
            public Vector3 Position { get; }
            public Vector3 Rotation { get; }
        }

        private static readonly VehicleStateFields RequiredPoseFields =
            VehicleStateFields.Position | VehicleStateFields.Orientation;

        [Header("Explicit V1 output")]
        [SerializeField] private TextMesh targetText;

        [Header("Explicit vehicle bindings")]
        [SerializeField] private VehicleBinding auv = new VehicleBinding();
        [SerializeField] private VehicleBinding rov = new VehicleBinding();
        [SerializeField] private VehicleBinding usv = new VehicleBinding();

        [Header("Presentation refresh")]
        [SerializeField, Range(0.1f, 0.25f)]
        private float refreshIntervalSeconds = 0.2f;

        private readonly StringBuilder textBuilder = new StringBuilder(512);
        private float refreshAccumulator;
        private string lastRenderedText = string.Empty;

        public TextMesh TargetText => targetText;
        public VehicleDataRuntimeHost AuvHost => auv == null ? null : auv.Host;
        public VehiclePoseDriver AuvDriver => auv == null ? null : auv.Driver;
        public VehiclePoseControlAuthority AuvAuthority =>
            auv == null ? null : auv.Authority;
        public VehicleDataRuntimeHost RovHost => rov == null ? null : rov.Host;
        public VehiclePoseDriver RovDriver => rov == null ? null : rov.Driver;
        public VehiclePoseControlAuthority RovAuthority =>
            rov == null ? null : rov.Authority;
        public VehicleDataRuntimeHost UsvHost => usv == null ? null : usv.Host;
        public VehiclePoseDriver UsvDriver => usv == null ? null : usv.Driver;
        public VehiclePoseControlAuthority UsvAuthority =>
            usv == null ? null : usv.Authority;
        public float RefreshIntervalSeconds => refreshIntervalSeconds;
        public string LastRenderedText => lastRenderedText;

        public void Configure(
            TextMesh output,
            VehicleDataRuntimeHost auvHost,
            VehiclePoseDriver auvDriver,
            VehiclePoseControlAuthority auvAuthority,
            VehicleDataRuntimeHost rovHost,
            VehiclePoseDriver rovDriver,
            VehiclePoseControlAuthority rovAuthority,
            VehicleDataRuntimeHost usvHost,
            VehiclePoseDriver usvDriver,
            VehiclePoseControlAuthority usvAuthority,
            float refreshSeconds = 0.2f)
        {
            targetText = output;
            if (auv == null) auv = new VehicleBinding();
            if (rov == null) rov = new VehicleBinding();
            if (usv == null) usv = new VehicleBinding();
            auv.Configure(VehicleType.Auv, auvHost, auvDriver, auvAuthority);
            rov.Configure(VehicleType.Rov, rovHost, rovDriver, rovAuthority);
            usv.Configure(VehicleType.Usv, usvHost, usvDriver, usvAuthority);
            refreshIntervalSeconds = Mathf.Clamp(refreshSeconds, 0.1f, 0.25f);
        }

        public bool MatchesConfiguration(
            TextMesh output,
            VehicleDataRuntimeHost auvHost,
            VehiclePoseDriver auvDriver,
            VehiclePoseControlAuthority auvAuthority,
            VehicleDataRuntimeHost rovHost,
            VehiclePoseDriver rovDriver,
            VehiclePoseControlAuthority rovAuthority,
            VehicleDataRuntimeHost usvHost,
            VehiclePoseDriver usvDriver,
            VehiclePoseControlAuthority usvAuthority,
            float refreshSeconds = 0.2f)
        {
            return ReferenceEquals(targetText, output) &&
                   auv != null &&
                   auv.Matches(
                       VehicleType.Auv,
                       auvHost,
                       auvDriver,
                       auvAuthority) &&
                   rov != null &&
                   rov.Matches(
                       VehicleType.Rov,
                       rovHost,
                       rovDriver,
                       rovAuthority) &&
                   usv != null &&
                   usv.Matches(
                       VehicleType.Usv,
                       usvHost,
                       usvDriver,
                       usvAuthority) &&
                   Mathf.Abs(refreshIntervalSeconds - refreshSeconds) <=
                   0.000001f;
        }

        private void OnEnable()
        {
            refreshAccumulator = refreshIntervalSeconds;
        }

        private void Update()
        {
            refreshAccumulator += Time.unscaledDeltaTime;
            if (refreshAccumulator + 0.000001f < refreshIntervalSeconds)
            {
                return;
            }

            refreshAccumulator %= refreshIntervalSeconds;
            RefreshNow();
        }

        public void RefreshNow()
        {
            if (targetText == null)
            {
                return;
            }

            textBuilder.Clear();
            textBuilder.Append("VEHICLE STATUS\n");
            AppendVehicle(textBuilder, Evaluate(auv));
            textBuilder.Append('\n');
            AppendVehicle(textBuilder, Evaluate(rov));
            textBuilder.Append('\n');
            AppendVehicle(textBuilder, Evaluate(usv));

            string rendered = textBuilder.ToString();
            if (!string.Equals(rendered, lastRenderedText, StringComparison.Ordinal))
            {
                lastRenderedText = rendered;
                targetText.text = rendered;
            }
        }

        private static VehiclePresentation Evaluate(VehicleBinding binding)
        {
            VehicleType fallbackType =
                binding == null ? VehicleType.Unknown : binding.ExpectedType;
            if (binding == null ||
                binding.Host == null ||
                binding.Driver == null ||
                binding.Authority == null)
            {
                return WithoutPose(
                    fallbackType,
                    SafeVehicleId(binding),
                    VehicleDisplayState.Invalid,
                    SafeAuthorityMode(binding));
            }

            VehicleDataRuntimeHost host = binding.Host;
            VehiclePoseDriver driver = binding.Driver;
            VehiclePoseControlAuthority authority = binding.Authority;
            string vehicleId = host.VehicleId;
            VehicleType vehicleType =
                host.IntegrationConfiguration == null
                    ? binding.ExpectedType
                    : host.IntegrationConfiguration.VehicleType;

            if (vehicleType != binding.ExpectedType ||
                host.SourceStatus == DataSourceStatus.Faulted)
            {
                return WithoutPose(
                    vehicleType,
                    vehicleId,
                    VehicleDisplayState.Invalid,
                    authority.Mode);
            }

            if (!driver.isActiveAndEnabled)
            {
                return WithLastAppliedPoseIfCurrent(
                    vehicleType,
                    vehicleId,
                    VehicleDisplayState.Disabled,
                    authority.Mode,
                    driver,
                    0UL,
                    false);
            }

            if (!host.IsInitialized)
            {
                return WithoutPose(
                    vehicleType,
                    vehicleId,
                    VehicleDisplayState.NoData,
                    authority.Mode);
            }

            if (host.SourceStatus == DataSourceStatus.Stopped ||
                host.SourceStatus == DataSourceStatus.Stopping ||
                host.SourceStatus == DataSourceStatus.Disposed)
            {
                ulong disabledEpoch;
                bool hasDisabledEpoch =
                    host.TryGetActiveEpoch(out disabledEpoch);
                return WithLastAppliedPoseIfCurrent(
                    vehicleType,
                    vehicleId,
                    VehicleDisplayState.Disabled,
                    authority.Mode,
                    driver,
                    disabledEpoch,
                    hasDisabledEpoch);
            }

            VehicleStateStore store = host.Store;
            if (store == null ||
                !host.TryGetActiveEpoch(out ulong epoch))
            {
                return WithoutPose(
                    vehicleType,
                    vehicleId,
                    VehicleDisplayState.NoData,
                    authority.Mode);
            }

            VehicleSnapshot snapshot;
            try
            {
                if (!store.TryReadSnapshot(
                        host.SourceId,
                        epoch,
                        vehicleId,
                        host.MonotonicNowSeconds,
                        out snapshot))
                {
                    return WithoutPose(
                        vehicleType,
                        vehicleId,
                        VehicleDisplayState.NoData,
                        authority.Mode);
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                return WithoutPose(
                    vehicleType,
                    vehicleId,
                    VehicleDisplayState.Invalid,
                    authority.Mode);
            }

            ReceivedVehicleState latest = snapshot.Latest;
            if (!latest.IsStructurallyValid ||
                latest.State.VehicleType != binding.ExpectedType ||
                (latest.State.ValidFields & RequiredPoseFields) !=
                RequiredPoseFields)
            {
                return WithoutPose(
                    vehicleType,
                    vehicleId,
                    VehicleDisplayState.Invalid,
                    authority.Mode);
            }

            if (snapshot.IsTimedOut)
            {
                return WithLastAppliedPoseIfCurrent(
                    vehicleType,
                    vehicleId,
                    VehicleDisplayState.Stale,
                    authority.Mode,
                    driver,
                    epoch,
                    true);
            }

            bool currentSamplingPath =
                host.SourceStatus == DataSourceStatus.Running &&
                driver.isActiveAndEnabled &&
                driver.OwnsControl;
            if (currentSamplingPath &&
                !driver.HasFreshAppliedPose &&
                IsCurrentInvalidFailure(driver.LastFailureReason))
            {
                return WithoutPose(
                    vehicleType,
                    vehicleId,
                    VehicleDisplayState.Invalid,
                    authority.Mode);
            }

            Vector3 position = default;
            Vector3 rotation = default;
            bool hasCurrentPose =
                driver.HasAppliedPose &&
                driver.HasFreshAppliedPose &&
                driver.LastAppliedSourceEpoch == epoch &&
                TryReadFinitePose(
                    driver.TargetRoot,
                    out position,
                    out rotation);
            if (host.SourceStatus == DataSourceStatus.Running &&
                driver.OwnsControl &&
                hasCurrentPose)
            {
                return new VehiclePresentation(
                    vehicleType,
                    vehicleId,
                    VehicleDisplayState.Normal,
                    authority.Mode,
                    true,
                    position,
                    rotation);
            }

            return WithoutPose(
                vehicleType,
                vehicleId,
                VehicleDisplayState.NoData,
                authority.Mode);
        }

        private static VehiclePresentation WithLastAppliedPoseIfCurrent(
            VehicleType vehicleType,
            string vehicleId,
            VehicleDisplayState state,
            VehiclePoseControlMode authorityMode,
            VehiclePoseDriver driver,
            ulong epoch,
            bool requireEpoch)
        {
            Vector3 position = default;
            Vector3 rotation = default;
            bool hasPose =
                driver != null &&
                driver.HasAppliedPose &&
                (!requireEpoch || driver.LastAppliedSourceEpoch == epoch) &&
                TryReadFinitePose(
                    driver.TargetRoot,
                    out position,
                    out rotation);
            return new VehiclePresentation(
                vehicleType,
                vehicleId,
                state,
                authorityMode,
                hasPose,
                hasPose ? position : default,
                hasPose ? rotation : default);
        }

        private static VehiclePresentation WithoutPose(
            VehicleType vehicleType,
            string vehicleId,
            VehicleDisplayState state,
            VehiclePoseControlMode authorityMode)
        {
            return new VehiclePresentation(
                vehicleType,
                vehicleId,
                state,
                authorityMode,
                false,
                default,
                default);
        }

        private static bool TryReadFinitePose(
            Transform target,
            out Vector3 position,
            out Vector3 rotation)
        {
            if (target == null)
            {
                position = default;
                rotation = default;
                return false;
            }

            position = target.position;
            Vector3 euler = target.eulerAngles;
            rotation = new Vector3(
                Mathf.DeltaAngle(0f, euler.x),
                Mathf.DeltaAngle(0f, euler.y),
                Mathf.DeltaAngle(0f, euler.z));
            return IsFinite(position.x) &&
                   IsFinite(position.y) &&
                   IsFinite(position.z) &&
                   IsFinite(rotation.x) &&
                   IsFinite(rotation.y) &&
                   IsFinite(rotation.z);
        }

        private static bool IsCurrentInvalidFailure(
            RenderSampleFailureReason reason)
        {
            return reason == RenderSampleFailureReason.InvalidRequest ||
                   reason == RenderSampleFailureReason.InvalidPolicy ||
                   reason == RenderSampleFailureReason.InvalidHistory ||
                   reason == RenderSampleFailureReason.ConversionFailed ||
                   reason == RenderSampleFailureReason.InterpolationFailed ||
                   reason == RenderSampleFailureReason.LocalClockRegression;
        }

        private static string SafeVehicleId(VehicleBinding binding)
        {
            return binding == null || binding.Host == null
                ? "UNBOUND"
                : binding.Host.VehicleId;
        }

        private static VehiclePoseControlMode SafeAuthorityMode(
            VehicleBinding binding)
        {
            return binding == null || binding.Authority == null
                ? VehiclePoseControlMode.Demo
                : binding.Authority.Mode;
        }

        private static void AppendVehicle(
            StringBuilder builder,
            in VehiclePresentation value)
        {
            builder.Append(VehicleTypeLabel(value.VehicleType));
            builder.Append(' ');
            builder.Append(string.IsNullOrWhiteSpace(value.VehicleId)
                ? "UNBOUND"
                : value.VehicleId);
            builder.Append(" | ");
            builder.Append(StateLabel(value.State));
            builder.Append(" | ");
            builder.Append(value.AuthorityMode == VehiclePoseControlMode.PublicData
                ? "PUBLIC_DATA"
                : "DEMO");
            builder.Append(" | ");
            if (!value.HasPose)
            {
                builder.Append("P— | R—");
                return;
            }

            builder.Append("P(");
            AppendNumber(builder, value.Position.x, "0.00");
            builder.Append(',');
            AppendNumber(builder, value.Position.y, "0.00");
            builder.Append(',');
            AppendNumber(builder, value.Position.z, "0.00");
            builder.Append(") | R(");
            AppendNumber(builder, value.Rotation.x, "0");
            builder.Append(',');
            AppendNumber(builder, value.Rotation.y, "0");
            builder.Append(',');
            AppendNumber(builder, value.Rotation.z, "0");
            builder.Append(')');
        }

        private static void AppendNumber(
            StringBuilder builder,
            float value,
            string format)
        {
            builder.Append(value.ToString(format, CultureInfo.InvariantCulture));
        }

        private static string VehicleTypeLabel(VehicleType type)
        {
            switch (type)
            {
                case VehicleType.Auv: return "AUV";
                case VehicleType.Rov: return "ROV";
                case VehicleType.Usv: return "USV";
                default: return "UNKNOWN";
            }
        }

        private static string StateLabel(VehicleDisplayState state)
        {
            switch (state)
            {
                case VehicleDisplayState.Invalid: return "INVALID";
                case VehicleDisplayState.Disabled: return "DISABLED";
                case VehicleDisplayState.NoData: return "NO DATA";
                case VehicleDisplayState.Stale: return "STALE";
                case VehicleDisplayState.Normal: return "NORMAL";
                default: return "INVALID";
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void OnValidate()
        {
            refreshIntervalSeconds =
                Mathf.Clamp(refreshIntervalSeconds, 0.1f, 0.25f);
        }
    }
}
