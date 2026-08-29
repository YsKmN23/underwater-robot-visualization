using System;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.LocalTesting;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime.Constraints;
using UnderwaterRobotScene.Visualization.Transforms;
using UnityEngine;
using System.Collections.Generic;

namespace UnderwaterRobotScene.Visualization.Runtime
{
    public enum VehicleRuntimeSourceMode
    {
        LocalDiagnostic = 0,
        RouteFollowing = 1
    }

    [DisallowMultipleComponent]
    public sealed class VehicleDataRuntimeHost : MonoBehaviour
    {
        [Header("Explicit composition bindings")]
        [SerializeField] private VehiclePoseIntegrationConfiguration integrationConfiguration;
        [SerializeField] private VehiclePoseProfileConfiguration profileConfiguration;
        [SerializeField] private VehicleRuntimeSourceMode sourceMode =
            VehicleRuntimeSourceMode.LocalDiagnostic;

        [Header("Runtime observation")]
        [SerializeField] private DataSourceStatus sourceStatus = DataSourceStatus.Stopped;
        [SerializeField] private string activeEpoch = "unavailable";
        [SerializeField] private ulong publishedSamples;

        private VehicleStateStore store;
        private IManualStepDataSource source;
        private RouteFollowingSource routeSource;
        private double nextPublishMonotonicSeconds;
        private double epochStartedMonotonicSeconds;
        private double lastTickMonotonicSeconds;
        private bool initialized;
        private bool authoritySourceSelectionEnabled;
        private RouteSafetyFailureDiagnostic lastRouteSafetyFailure =
            RouteSafetyFailureDiagnostic.None;

        public string SourceId =>
            integrationConfiguration == null ? string.Empty : integrationConfiguration.SourceId;
        public string VehicleId =>
            integrationConfiguration == null ? string.Empty : integrationConfiguration.VehicleId;
        public VehiclePoseIntegrationConfiguration IntegrationConfiguration =>
            integrationConfiguration;
        public VehiclePoseProfileConfiguration ProfileConfiguration => profileConfiguration;
        public VehicleRuntimeSourceMode SourceMode => sourceMode;
        public bool AuthoritySourceSelectionEnabled =>
            authoritySourceSelectionEnabled;
        public VehicleStateStore Store => store;
        public DataSourceStatus SourceStatus => source == null ? DataSourceStatus.Stopped : source.Status;
        public double MonotonicNowSeconds => Time.realtimeSinceStartupAsDouble;
        public bool IsInitialized => initialized;
        public ActiveRouteSnapshot ActiveRouteSnapshot =>
            routeSource == null ? null : routeSource.Runtime.ActiveSnapshot;
        public VehicleRouteExecutionState? RouteExecutionState =>
            routeSource == null
                ? (VehicleRouteExecutionState?)null
                : routeSource.Runtime.State;
        public ulong RouteVersion => routeSource?.Runtime.RouteVersion ?? 0UL;
        public ulong RouteEpoch => routeSource?.Runtime.RouteEpoch ?? 0UL;
        public double RouteDistanceAlongRoute =>
            routeSource?.Runtime.DistanceAlongRoute ?? 0.0;
        public double RouteProgress01 => routeSource?.Runtime.Progress01 ?? 0.0;
        public double RouteCruiseSpeedMetersPerSecond =>
            routeSource?.Runtime.CruiseSpeedMetersPerSecond ?? 0.0;
        public RouteSafetyFailureDiagnostic LastRouteSafetyFailure =>
            lastRouteSafetyFailure;
        public UnityPoseConstraintDecision LastPoseConstraintDecision =>
            TryGetBoundDriver(out VehiclePoseDriver driver)
                ? driver.LastPoseConstraintDecision
                : UnityPoseConstraintDecision.NotEvaluated;
        public string LastPoseConstraintReason =>
            TryGetBoundDriver(out VehiclePoseDriver driver)
                ? driver.LastPoseConstraintReason
                : string.Empty;

        public void Configure(
            VehiclePoseIntegrationConfiguration configuration,
            VehiclePoseProfileConfiguration profile)
        {
            if (initialized)
            {
                throw new InvalidOperationException(
                    "Runtime host bindings cannot change while the source is initialized.");
            }

            integrationConfiguration = configuration ??
                throw new ArgumentNullException(nameof(configuration));
            profileConfiguration = profile ??
                throw new ArgumentNullException(nameof(profile));
        }

        public void ConfigureSourceMode(VehicleRuntimeSourceMode configuredMode)
        {
            if (initialized)
                throw new InvalidOperationException(
                    "Runtime source mode cannot change while initialized.");
            if (configuredMode != VehicleRuntimeSourceMode.LocalDiagnostic &&
                configuredMode != VehicleRuntimeSourceMode.RouteFollowing)
                throw new ArgumentOutOfRangeException(nameof(configuredMode));
            sourceMode = configuredMode;
        }

        private void OnEnable()
        {
            if (Application.isPlaying &&
                integrationConfiguration != null &&
                integrationConfiguration.AutoStart)
            {
                InitializeForDiagnostics(MonotonicNowSeconds);
            }
        }

        private void Update()
        {
            if (Application.isPlaying && initialized)
            {
                TickForDiagnostics(MonotonicNowSeconds);
            }
        }

        private void OnDisable()
        {
            ShutdownForDiagnostics();
        }

        private void OnDestroy()
        {
            ShutdownForDiagnostics();
        }

        public bool TryGetActiveEpoch(out ulong epoch)
        {
            if (store != null && store.TryGetActiveEpoch(SourceId, out epoch))
            {
                return true;
            }

            epoch = default;
            return false;
        }

        public bool TryGetLatestSourceTimestamp(out double sourceTimestampSeconds)
        {
            sourceTimestampSeconds = 0.0;
            if (!TryGetActiveEpoch(out ulong epoch) ||
                !store.TryReadLatest(SourceId, epoch, VehicleId, out ReceivedVehicleState latest))
            {
                return false;
            }

            sourceTimestampSeconds = latest.State.SourceTimestampSeconds;
            return true;
        }

        public double GetTargetSourceTimestamp(
            double localMonotonicNowSeconds,
            double renderDelaySeconds)
        {
            ValidateMonotonic(localMonotonicNowSeconds);
            if (double.IsNaN(renderDelaySeconds) ||
                double.IsInfinity(renderDelaySeconds) ||
                renderDelaySeconds < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(renderDelaySeconds),
                    "Render delay must be finite and non-negative.");
            }

            double target = localMonotonicNowSeconds -
                            epochStartedMonotonicSeconds -
                            renderDelaySeconds;
            return Math.Max(0.0, target);
        }

        public void StopSource()
        {
            source?.Stop();
            RefreshObservation();
        }

        public void StartSource()
        {
            if (!initialized)
            {
                InitializeForDiagnostics(MonotonicNowSeconds);
                return;
            }

            if (source != null && !source.IsRunning)
            {
                double now = MonotonicNowSeconds;
                source.Start(store);
                epochStartedMonotonicSeconds = now;
                nextPublishMonotonicSeconds = now;
                RefreshObservation();
            }
        }

        public void RestartSource()
        {
            if (!initialized)
            {
                InitializeForDiagnostics(MonotonicNowSeconds);
                return;
            }

            double now = MonotonicNowSeconds;
            RestartSourceCore(now);
            RefreshObservation();
        }

        public bool PauseRoute()
        {
            return routeSource != null && routeSource.Runtime.Pause();
        }

        public bool ResumeRoute()
        {
            return routeSource != null && routeSource.Runtime.Resume();
        }

        public bool RestartRoute()
        {
            if (routeSource == null)
                return false;
            RestartSourceCore(MonotonicNowSeconds);
            RefreshObservation();
            return true;
        }

        public bool CompleteRoute()
        {
            if (routeSource == null)
                return false;
            routeSource.Runtime.Complete();
            return true;
        }

        public bool TryApplyDraftRoute(
            IReadOnlyList<Vector3d> draftWaypoints,
            out string error)
        {
            return TryApplyDraftRoute(
                draftWaypoints,
                out error,
                out _);
        }

        public bool TryApplyDraftRoute(
            IReadOnlyList<Vector3d> draftWaypoints,
            out string error,
            out RouteSafetyFailureDiagnostic diagnostic)
        {
            error = string.Empty;
            diagnostic = RouteSafetyFailureDiagnostic.None;
            if (routeSource == null || ActiveRouteSnapshot == null)
            {
                error = "Route-following runtime is unavailable.";
                return false;
            }
            ActiveRouteSnapshot active = routeSource.Runtime.ActiveSnapshot;
            double now = MonotonicNowSeconds;
            if (routeSource.Runtime.State == VehicleRouteExecutionState.Running)
            {
                if (!TryGetDriverAcceptedPose(
                        out VehicleRoutePose acceptedPose,
                        out ulong acceptedEpoch,
                        out error))
                {
                    return false;
                }
                if (acceptedEpoch != routeSource.SourceEpoch)
                {
                    error = "The Driver accepted pose belongs to a retired SourceEpoch.";
                    return false;
                }
                if (!AtomicRouteReplanCandidate.TryBuild(
                        active,
                        draftWaypoints,
                        in acceptedPose,
                        now,
                        out ActiveRouteSnapshot candidate,
                        out error))
                {
                    return false;
                }
                if (!TryValidateCandidateRoute(candidate, out error, out diagnostic))
                {
                    return false;
                }
                if (!routeSource.TryPublishRunningReplan(
                        candidate, in acceptedPose, now, out error))
                {
                    return false;
                }

                epochStartedMonotonicSeconds = now;
                lastTickMonotonicSeconds = now;
                nextPublishMonotonicSeconds = now +
                    integrationConfiguration.SampleIntervalSeconds;
                RefreshObservation();
                return true;
            }

            var points = new List<Vector3d>();
            if (draftWaypoints != null)
            {
                for (int index = 0; index < draftWaypoints.Count; index++)
                    points.Add(draftWaypoints[index]);
            }
            ulong nextVersion = active.RouteVersion + 1UL;
            VehicleRoutePose activationPose =
                routeSource.Runtime.SampleCurrentPose();
            if (!ActiveRouteSnapshotBuilder.TryBuild(
                    active.VehicleId,
                    active.VehicleType,
                    active.RouteId + "-EDIT-" + nextVersion,
                    nextVersion,
                    points,
                    active.OrientationPolicy,
                    now,
                    activationPose.Orientation,
                    out ActiveRouteSnapshot next,
                    out error))
                return false;

            if (!TryValidateCandidateRoute(next, out error, out diagnostic))
                return false;

            if (!routeSource.TryActivateWhenNotRunning(next, out error))
                return false;

            epochStartedMonotonicSeconds = now;
            lastTickMonotonicSeconds = now;
            routeSource.Step(now);
            nextPublishMonotonicSeconds = now +
                integrationConfiguration.SampleIntervalSeconds;
            RefreshObservation();
            return true;
        }

        private bool TryValidateCandidateRoute(
            ActiveRouteSnapshot candidate,
            out string error)
        {
            return TryValidateCandidateRoute(candidate, out error, out _);
        }

        private bool TryValidateCandidateRoute(
            ActiveRouteSnapshot candidate,
            out string error,
            out RouteSafetyFailureDiagnostic diagnostic)
        {
            error = string.Empty;
            diagnostic = RouteSafetyFailureDiagnostic.None;
            lastRouteSafetyFailure = RouteSafetyFailureDiagnostic.None;
            if (candidate == null)
            {
                error = "A candidate route is required.";
                return false;
            }
            if (candidate.VehicleType == VehicleType.Usv)
            {
                error = string.Empty;
                return true;
            }
            string safetyVehicleLabel = candidate.VehicleType == VehicleType.Auv
                ? "AUV"
                : "ROV";
            if (profileConfiguration == null ||
                !profileConfiguration.TryBuildProfile(
                    out CoordinateTransformProfile transformProfile,
                    out error))
            {
                return false;
            }

            VehiclePoseDriver[] drivers =
                FindObjectsByType<VehiclePoseDriver>(
                    FindObjectsSortMode.None);
            for (int index = 0; index < drivers.Length; index++)
            {
                VehiclePoseDriver driver = drivers[index];
                if (!ReferenceEquals(driver.RuntimeHost, this))
                    continue;
                if (!(driver.PoseConstraintProvider is
                        IRouteSafetyValidator validator))
                {
                    error = "The " + safetyVehicleLabel +
                        " Driver has no route terrain safety validator.";
                    return false;
                }
                bool valid = validator.TryValidateRoute(
                    candidate,
                    in transformProfile,
                    out error);
                if (driver.PoseConstraintProvider is
                    IRouteSafetyDiagnosticProvider diagnostics)
                {
                    diagnostic = diagnostics.LastRouteSafetyFailure;
                }
                lastRouteSafetyFailure = diagnostic;
                return valid;
            }

            error = "No VehiclePoseDriver is bound to this " +
                safetyVehicleLabel + " route runtime.";
            return false;
        }

        public bool TryGetDriverAcceptedPose(
            out VehicleRoutePose acceptedPose,
            out ulong sourceEpoch,
            out string error)
        {
            VehiclePoseDriver[] drivers = FindObjectsByType<VehiclePoseDriver>(
                FindObjectsSortMode.None);
            for (int index = 0; index < drivers.Length; index++)
            {
                VehiclePoseDriver driver = drivers[index];
                if (!ReferenceEquals(driver.RuntimeHost, this))
                {
                    continue;
                }
                if (driver.TryGetLastAcceptedRoutePose(
                        out acceptedPose, out sourceEpoch))
                {
                    error = string.Empty;
                    return true;
                }

                error = "The matching VehiclePoseDriver has not committed an accepted pose yet.";
                acceptedPose = default;
                sourceEpoch = 0UL;
                return false;
            }

            error = "No VehiclePoseDriver is bound to this route runtime.";
            acceptedPose = default;
            sourceEpoch = 0UL;
            return false;
        }

        private bool TryGetBoundDriver(out VehiclePoseDriver boundDriver)
        {
            VehiclePoseDriver[] drivers = FindObjectsByType<VehiclePoseDriver>(
                FindObjectsSortMode.None);
            for (int index = 0; index < drivers.Length; index++)
            {
                if (ReferenceEquals(drivers[index].RuntimeHost, this))
                {
                    boundDriver = drivers[index];
                    return true;
                }
            }
            boundDriver = null;
            return false;
        }

        public void InitializeForDiagnostics(double monotonicNowSeconds)
        {
            ValidateMonotonic(monotonicNowSeconds);
            if (initialized)
            {
                return;
            }

            if (integrationConfiguration == null)
            {
                throw new InvalidOperationException("Integration Configuration is required.");
            }

            if (!integrationConfiguration.TryValidate(out string configurationError))
            {
                throw new InvalidOperationException(configurationError);
            }

            if (profileConfiguration == null)
            {
                throw new InvalidOperationException("Profile Configuration is required.");
            }

            if (!profileConfiguration.TryBuildProfile(
                    out CoordinateTransformProfile profile,
                    out string profileError))
            {
                throw new InvalidOperationException(profileError);
            }

            var storePolicy = new VehicleStateStorePolicy(
                integrationConfiguration.StoreCapacity,
                timeoutSeconds: integrationConfiguration.StaleTimeoutSeconds);
            store = new VehicleStateStore(storePolicy);
            authoritySourceSelectionEnabled =
                sourceMode == VehicleRuntimeSourceMode.RouteFollowing;
            source = sourceMode == VehicleRuntimeSourceMode.RouteFollowing
                ? BuildRouteSource(profile, monotonicNowSeconds)
                : BuildDiagnosticSource(profile);
            if (sourceMode == VehicleRuntimeSourceMode.RouteFollowing &&
                integrationConfiguration.VehicleType != VehicleType.Usv &&
                routeSource != null &&
                !TryValidateCandidateRoute(
                    routeSource.Runtime.ActiveSnapshot,
                    out string routeError,
                    out RouteSafetyFailureDiagnostic routeDiagnostic))
            {
                lastRouteSafetyFailure = routeDiagnostic;
                source.Dispose();
                source = null;
                routeSource = null;
                store.Dispose();
                store = null;
                throw new InvalidOperationException(
                    "Initial " +
                    (integrationConfiguration.VehicleType == VehicleType.Auv
                        ? "AUV"
                        : "ROV") +
                    " route rejected; source was not started: " +
                    routeError);
            }
            source.Start(store);
            epochStartedMonotonicSeconds = monotonicNowSeconds;
            nextPublishMonotonicSeconds = monotonicNowSeconds;
            lastTickMonotonicSeconds = monotonicNowSeconds;
            initialized = true;
            RefreshObservation();
        }

        public bool TrySelectAuthoritySourceMode(
            VehicleRuntimeSourceMode desiredMode,
            in VehicleRoutePose acceptedPose,
            ulong acceptedSourceEpoch,
            double monotonicNowSeconds,
            out string error)
        {
            error = string.Empty;
            ValidateMonotonic(monotonicNowSeconds);
            if (!authoritySourceSelectionEnabled)
            {
                return desiredMode == sourceMode;
            }
            if (!initialized || store == null || source == null ||
                routeSource == null)
            {
                error = "Authority source selection requires an initialized retained route source.";
                return false;
            }
            if (desiredMode != VehicleRuntimeSourceMode.LocalDiagnostic &&
                desiredMode != VehicleRuntimeSourceMode.RouteFollowing)
            {
                error = "Authority selected an unsupported runtime source mode.";
                return false;
            }
            if (!acceptedPose.Position.IsFinite ||
                !acceptedPose.Orientation.IsUsable)
            {
                error = "Authority source selection requires a finite accepted Driver pose.";
                return false;
            }
            if (!store.TryGetActiveEpoch(SourceId, out ulong currentEpoch) ||
                currentEpoch == 0UL || currentEpoch != acceptedSourceEpoch)
            {
                error = "Authority source selection requires the Driver and Store to agree on the current SourceEpoch.";
                return false;
            }
            if (desiredMode == sourceMode)
            {
                return true;
            }
            if (currentEpoch == ulong.MaxValue)
            {
                error = "Source epoch is exhausted.";
                return false;
            }

            ulong nextEpoch = currentEpoch + 1UL;
            IManualStepDataSource previousSource = source;
            LocalTestSource nextDiagnosticSource = null;
            if (desiredMode == VehicleRuntimeSourceMode.LocalDiagnostic)
            {
                if (!profileConfiguration.TryBuildProfile(
                        out CoordinateTransformProfile profile,
                        out error))
                {
                    return false;
                }
                nextDiagnosticSource = (LocalTestSource)
                    BuildDiagnosticSource(profile, in acceptedPose);
            }
            else if (!routeSource.Runtime.BeginResumeBridge(in acceptedPose))
            {
                error = "The retained route runtime rejected the accepted-pose continuity bridge.";
                return false;
            }
            previousSource.Stop();
            try
            {
                if (desiredMode == VehicleRuntimeSourceMode.LocalDiagnostic)
                {
                    source = nextDiagnosticSource;
                    nextDiagnosticSource.StartAtEpoch(store, nextEpoch);
                }
                else
                {
                    routeSource.StartAtEpoch(store, nextEpoch);
                    source = routeSource;
                }

                sourceMode = desiredMode;
                epochStartedMonotonicSeconds = monotonicNowSeconds;
                lastTickMonotonicSeconds = monotonicNowSeconds;
                int published = source.Step(monotonicNowSeconds);
                nextPublishMonotonicSeconds = monotonicNowSeconds +
                    integrationConfiguration.SampleIntervalSeconds;
                if (published != 1 ||
                    !store.TryGetActiveEpoch(SourceId, out ulong activeEpoch) ||
                    activeEpoch != nextEpoch)
                {
                    error = "The selected source failed to publish the first sample of its new SourceEpoch.";
                    return false;
                }
                if (!ReferenceEquals(previousSource, routeSource))
                {
                    previousSource.Dispose();
                }
                RefreshObservation();
                return true;
            }
            catch (Exception exception)
            {
                error = "Authority source transition failed: " +
                    exception.GetType().Name + ": " + exception.Message;
                RefreshObservation();
                return false;
            }
        }

        public void TickForDiagnostics(double monotonicNowSeconds)
        {
            ValidateMonotonic(monotonicNowSeconds);
            if (!initialized)
            {
                InitializeForDiagnostics(monotonicNowSeconds);
            }

            if (monotonicNowSeconds < lastTickMonotonicSeconds)
            {
                throw new InvalidOperationException("Runtime host monotonic clock regressed.");
            }

            lastTickMonotonicSeconds = monotonicNowSeconds;
            if (source != null && source.IsRunning)
            {
                int steps = 0;
                while (monotonicNowSeconds + 1e-9 >= nextPublishMonotonicSeconds &&
                       steps < integrationConfiguration.MaxCatchUpStepsPerFrame)
                {
                    source.Step(nextPublishMonotonicSeconds);
                    nextPublishMonotonicSeconds += integrationConfiguration.SampleIntervalSeconds;
                    steps++;
                }
            }

            RefreshObservation();
        }

        public void StartSourceForDiagnostics(double monotonicNowSeconds)
        {
            ValidateMonotonic(monotonicNowSeconds);
            if (!initialized)
            {
                InitializeForDiagnostics(monotonicNowSeconds);
                return;
            }

            if (!source.IsRunning)
            {
                source.Start(store);
                epochStartedMonotonicSeconds = monotonicNowSeconds;
                nextPublishMonotonicSeconds = monotonicNowSeconds;
                lastTickMonotonicSeconds = monotonicNowSeconds;
            }

            RefreshObservation();
        }

        public void RestartSourceForDiagnostics(double monotonicNowSeconds)
        {
            ValidateMonotonic(monotonicNowSeconds);
            if (!initialized)
            {
                InitializeForDiagnostics(monotonicNowSeconds);
                return;
            }

            RestartSourceCore(monotonicNowSeconds);
            RefreshObservation();
        }

        public bool NotifyConstraintHold(
            ulong observedSourceEpoch,
            Vector3 acceptedPosition,
            Quaternion acceptedRotation,
            double monotonicNowSeconds)
        {
            ValidateMonotonic(monotonicNowSeconds);
            if (routeSource == null ||
                !TryGetActiveEpoch(out ulong activeSourceEpoch) ||
                activeSourceEpoch != observedSourceEpoch ||
                !profileConfiguration.TryBuildProfile(
                    out CoordinateTransformProfile profile,
                    out _))
                return false;

            Quaternion alignment = new Quaternion(
                (float)profile.ModelAlignment.X,
                (float)profile.ModelAlignment.Y,
                (float)profile.ModelAlignment.Z,
                (float)profile.ModelAlignment.W);
            Quaternion sourceRotation =
                acceptedRotation * Quaternion.Inverse(alignment);
            var acceptedPose = new VehicleRoutePose(
                new Vector3d(
                    acceptedPosition.x / profile.PositionScale,
                    acceptedPosition.y / profile.PositionScale,
                    acceptedPosition.z / profile.PositionScale),
                new Quaterniond(
                    sourceRotation.x,
                    sourceRotation.y,
                    sourceRotation.z,
                    sourceRotation.w));
            if (sourceMode == VehicleRuntimeSourceMode.RouteFollowing)
            {
                if (routeSource.SourceEpoch != observedSourceEpoch ||
                    !routeSource.EnterConstraintHold(in acceptedPose))
                    return false;
            }
            else
            {
                if (!routeSource.Runtime.EnterHold(in acceptedPose))
                    return false;
                source.Stop();
            }

            epochStartedMonotonicSeconds = monotonicNowSeconds;
            lastTickMonotonicSeconds = monotonicNowSeconds;
            source.Step(monotonicNowSeconds);
            nextPublishMonotonicSeconds = monotonicNowSeconds +
                integrationConfiguration.SampleIntervalSeconds;
            RefreshObservation();
            return true;
        }

        public void ShutdownForDiagnostics()
        {
            if (!initialized)
            {
                return;
            }

            source?.Stop();
            source?.Dispose();
            if (routeSource != null && !ReferenceEquals(source, routeSource))
            {
                routeSource.Dispose();
            }
            store?.Dispose();
            source = null;
            routeSource = null;
            store = null;
            initialized = false;
            authoritySourceSelectionEnabled = false;
            sourceStatus = DataSourceStatus.Stopped;
            activeEpoch = "unavailable";
            publishedSamples = 0UL;
        }

        private void RefreshObservation()
        {
            sourceStatus = SourceStatus;
            if (TryGetActiveEpoch(out ulong epoch))
            {
                activeEpoch = epoch.ToString();
            }
            else
            {
                activeEpoch = "unavailable";
            }

            publishedSamples = source?.GetStatistics().PublishedSamples ?? 0UL;
        }

        private IManualStepDataSource BuildDiagnosticSource(
            CoordinateTransformProfile profile)
        {
            var origin = new VehicleRoutePose(
                new Vector3d(
                    integrationConfiguration.TestOrigin.x,
                    integrationConfiguration.TestOrigin.y,
                    integrationConfiguration.TestOrigin.z),
                Quaterniond.Identity);
            return BuildDiagnosticSource(profile, in origin, false);
        }

        private IManualStepDataSource BuildDiagnosticSource(
            CoordinateTransformProfile profile,
            in VehicleRoutePose anchor)
        {
            return BuildDiagnosticSource(profile, in anchor, true);
        }

        private IManualStepDataSource BuildDiagnosticSource(
            CoordinateTransformProfile profile,
            in VehicleRoutePose anchor,
            bool composeAnchorOrientation)
        {
            var vehicle = new LocalTestVehicle(
                integrationConfiguration.VehicleId,
                integrationConfiguration.VehicleType,
                anchor.Position,
                profile.SourceWorldFrame,
                profile.SourceBodyFrame);
            IDeterministicVehicleStateGenerator generator =
                integrationConfiguration.CreateStateGenerator();
            if (composeAnchorOrientation)
            {
                generator = new AnchoredStateGenerator(
                    generator, anchor.Orientation);
            }
            return new LocalTestSource(new LocalTestSourceConfig(
                integrationConfiguration.SourceId,
                integrationConfiguration.SampleIntervalSeconds,
                new[] { vehicle },
                generator));
        }

        private IManualStepDataSource BuildRouteSource(
            CoordinateTransformProfile profile,
            double monotonicNowSeconds)
        {
            if (profile.SourceWorldFrame != WorldFrame.UnityWorld ||
                profile.SourceBodyFrame != BodyFrame.UnityBody ||
                profile.AttitudeDirection != AttitudeDirection.BodyToWorld)
                throw new InvalidOperationException(
                    "Batch 1 route following requires a UnityWorld/UnityBody BodyToWorld profile.");

            VehicleRouteConfig config = VehicleRouteConfig.Load();
            if (config == null)
                throw new InvalidOperationException(
                    "VehicleRouteConfig is required for route following.");
            var points = new System.Collections.Generic.List<Vector3d>();
            Vector3 origin = integrationConfiguration.TestOrigin;
            points.Add(new Vector3d(origin.x, origin.y, origin.z));
            var offsets = config.GetLocalWaypoints(
                integrationConfiguration.VehicleType);
            for (int index = 0; index < offsets.Count; index++)
            {
                Vector3 offset = offsets[index];
                double y = integrationConfiguration.VehicleType == VehicleType.Usv
                    ? origin.y
                    : origin.y + offset.y;
                points.Add(new Vector3d(
                    origin.x + offset.x,
                    y,
                    origin.z + offset.z));
            }

            VehicleRouteOrientationPolicy policy = config.GetOrientationPolicy(
                integrationConfiguration.VehicleType);
            bool hasCompositionSeed = TryGetCompositionHeadingSeed(
                profile, out Quaterniond compositionSeed);
            bool built = hasCompositionSeed
                ? ActiveRouteSnapshotBuilder.TryBuild(
                    integrationConfiguration.VehicleId,
                    integrationConfiguration.VehicleType,
                    "E3C-" + integrationConfiguration.VehicleType + "-INITIAL",
                    1UL, points, policy, monotonicNowSeconds,
                    compositionSeed,
                    out ActiveRouteSnapshot snapshot,
                    out string error)
                : ActiveRouteSnapshotBuilder.TryBuild(
                    integrationConfiguration.VehicleId,
                    integrationConfiguration.VehicleType,
                    "E3C-" + integrationConfiguration.VehicleType + "-INITIAL",
                    1UL, points, policy, monotonicNowSeconds,
                    out snapshot, out error);
            if (!built)
                throw new InvalidOperationException(error);

            var runtime = new VehicleRouteRuntime(
                snapshot,
                config.GetCruiseSpeed(integrationConfiguration.VehicleType));
            routeSource = new RouteFollowingSource(
                integrationConfiguration.SourceId,
                integrationConfiguration.SampleIntervalSeconds,
                runtime,
                profile.SourceWorldFrame,
                profile.SourceBodyFrame);
            return routeSource;
        }

        private bool TryGetCompositionHeadingSeed(
            CoordinateTransformProfile profile,
            out Quaterniond seed)
        {
            seed = default;
            if (integrationConfiguration.VehicleType != VehicleType.Rov ||
                !TryGetBoundDriver(out VehiclePoseDriver driver) ||
                driver.TargetRoot == null)
            {
                return false;
            }

            Quaternion rootRotation = driver.TargetRoot.rotation;
            Quaternion alignment = new Quaternion(
                (float)profile.ModelAlignment.X,
                (float)profile.ModelAlignment.Y,
                (float)profile.ModelAlignment.Z,
                (float)profile.ModelAlignment.W);
            Quaternion sourceRotation =
                rootRotation * Quaternion.Inverse(alignment);
            seed = new Quaterniond(
                sourceRotation.x,
                sourceRotation.y,
                sourceRotation.z,
                sourceRotation.w);
            return seed.IsUsable;
        }

        private void RestartSourceCore(double monotonicNowSeconds)
        {
            ValidateMonotonic(monotonicNowSeconds);
            if (sourceMode == VehicleRuntimeSourceMode.RouteFollowing &&
                routeSource != null)
            {
                if (routeSource.IsRunning)
                {
                    routeSource.RestartExecution();
                }
                else
                {
                    routeSource.Start(store);
                    routeSource.Runtime.Restart();
                }
                routeSource.Step(monotonicNowSeconds);
            }
            else
            {
                source.Stop();
                source.Start(store);
                source.Step(monotonicNowSeconds);
            }

            epochStartedMonotonicSeconds = monotonicNowSeconds;
            nextPublishMonotonicSeconds = monotonicNowSeconds +
                integrationConfiguration.SampleIntervalSeconds;
            lastTickMonotonicSeconds = monotonicNowSeconds;
        }

        private static void ValidateMonotonic(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Monotonic time must be finite and non-negative.");
            }
        }

        private sealed class AnchoredStateGenerator :
            IDeterministicVehicleStateGenerator
        {
            private readonly IDeterministicVehicleStateGenerator inner;
            private readonly Quaterniond anchorOrientation;

            public AnchoredStateGenerator(
                IDeterministicVehicleStateGenerator innerGenerator,
                Quaterniond configuredAnchorOrientation)
            {
                inner = innerGenerator ??
                    throw new ArgumentNullException(nameof(innerGenerator));
                if (!configuredAnchorOrientation.TryNormalize(
                        out Quaterniond normalizedAnchorOrientation))
                {
                    throw new ArgumentException(
                        "The accepted anchor orientation must be usable.",
                        nameof(configuredAnchorOrientation));
                }
                anchorOrientation = normalizedAnchorOrientation;
            }

            public VehicleState Evaluate(
                LocalTestVehicle vehicle,
                ulong sampleIndex,
                double sourceTimestampSeconds)
            {
                VehicleState generated = inner.Evaluate(
                    vehicle, sampleIndex, sourceTimestampSeconds);
                Quaterniond orientation = Multiply(
                    anchorOrientation, generated.Orientation);
                return new VehicleState(
                    generated.VehicleId,
                    generated.VehicleType,
                    generated.SourceTimestampSeconds,
                    generated.SequenceNumber,
                    generated.Position,
                    orientation,
                    generated.LinearVelocity,
                    generated.AngularVelocity,
                    generated.LinearAcceleration,
                    generated.ValidFields,
                    generated.WorldFrame,
                    generated.BodyFrame);
            }

            private static Quaterniond Multiply(
                Quaterniond left,
                Quaterniond right)
            {
                return new Quaterniond(
                    left.W * right.X + left.X * right.W +
                        left.Y * right.Z - left.Z * right.Y,
                    left.W * right.Y - left.X * right.Z +
                        left.Y * right.W + left.Z * right.X,
                    left.W * right.Z + left.X * right.Y -
                        left.Y * right.X + left.Z * right.W,
                    left.W * right.W - left.X * right.X -
                        left.Y * right.Y - left.Z * right.Z);
            }
        }
    }
}
