using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Auv;
using UnderwaterRobotScene.Visualization.Runtime.Constraints;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    [InitializeOnLoad]
    public static class EnvE3DAuvTerrainSafetyPlayModeVerifier
    {
        private const string ScenePath =
            "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string ActiveKey = "E3D.AuvTerrain.Play.Active";
        private const string BatchKey = "E3D.AuvTerrain.Play.Batch";
        private const string ReportArgument = "-envE3DPlayModeReportPath";

        private enum Phase
        {
            WaitHealthy,
            RejectUnsafeApply,
            TriggerTerrainMiss,
            WaitForHold,
            VerifyHoldFreeze,
            ApplySafeRecovery,
            VerifyRecovery,
            VerifyControls
        }

        [Serializable]
        private sealed class PlayReport
        {
            public string schema;
            public string status;
            public string unityVersion;
            public string unsafeApplyError;
            public double frozenProgress;
            public float frozenRootDisplacement;
            public ulong holdSourceEpoch;
            public ulong recoveredSourceEpoch;
            public int actualTrackBefore;
            public int actualTrackAfter;
            public bool noTeleport;
            public bool noOldPoseFlashback;
            public bool rovUsvIsolated;
        }

        private static bool subscribed;
        private static Phase phase;
        private static double phaseStarted;
        private static VehicleDataRuntimeHost auv;
        private static VehicleDataRuntimeHost rov;
        private static VehicleDataRuntimeHost usv;
        private static VehiclePoseDriver driver;
        private static AuvTerrainClearanceConstraint constraint;
        private static MeshCollider terrain;
        private static VehicleTrajectoryVisualizationController trajectory;
        private static RuntimeIdentity auvBeforeUnsafe;
        private static StableIdentity rovIdentity;
        private static StableIdentity usvIdentity;
        private static Vector3 rootAtHold;
        private static double progressAtHold;
        private static float frozenRootDisplacement;
        private static int trackAtHold;
        private static ulong epochAtHold;
        private static ulong recoveryEpoch;
        private static Vector3 rootBeforeRecovery;
        private static string unsafeError;
        private static int trackAfter;
        private static VehicleRoutePose safeAnchor;

        static EnvE3DAuvTerrainSafetyPlayModeVerifier()
        {
            if (SessionState.GetBool(ActiveKey, false)) Subscribe();
        }

        public static void RunBatch()
        {
            if (SessionState.GetBool(ActiveKey, false))
                throw new InvalidOperationException(
                    "E3D AUV PlayMode verification is already active.");
            RequireExternalCreateNewPath();
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, true);
            Subscribe();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        private static void Subscribe()
        {
            if (subscribed) return;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnUpdate;
            subscribed = true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode ||
                !SessionState.GetBool(ActiveKey, false)) return;
            phase = Phase.WaitHealthy;
            phaseStarted = Time.realtimeSinceStartupAsDouble;
        }

        private static void OnUpdate()
        {
            if (!SessionState.GetBool(ActiveKey, false) ||
                !EditorApplication.isPlaying) return;
            try
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (now - phaseStarted > 25.0)
                    throw new InvalidOperationException(
                        "Timed out in E3D PlayMode phase " + phase + ".");
                switch (phase)
                {
                    case Phase.WaitHealthy:
                        if (!TryBind() || !Ready()) return;
                        Advance(Phase.RejectUnsafeApply, now);
                        break;
                    case Phase.RejectUnsafeApply:
                        VerifyUnsafeApplyHasNoSideEffects();
                        Advance(Phase.TriggerTerrainMiss, now);
                        break;
                    case Phase.TriggerTerrainMiss:
                        Require(terrain.enabled,
                            "Terrain was unexpectedly disabled.");
                        terrain.enabled = false;
                        Advance(Phase.WaitForHold, now);
                        break;
                    case Phase.WaitForHold:
                        if (auv.RouteExecutionState !=
                            VehicleRouteExecutionState.Hold) return;
                        rootAtHold = driver.TargetRoot.position;
                        progressAtHold = auv.RouteProgress01;
                        trackAtHold = ActualPointCount();
                        Require(auv.TryGetActiveEpoch(out epochAtHold),
                            "Hold has no SourceEpoch.");
                        Require(epochAtHold == auvBeforeUnsafe.SourceEpoch + 1UL,
                            "Constraint Hold did not retire the old SourceEpoch.");
                        Advance(Phase.VerifyHoldFreeze, now);
                        break;
                    case Phase.VerifyHoldFreeze:
                        if (now - phaseStarted < 0.8) return;
                        Require(Math.Abs(auv.RouteProgress01 - progressAtHold) <=
                                1e-12,
                            "AUV progress advanced during Hold.");
                        Require(Vector3.Distance(
                                driver.TargetRoot.position, rootAtHold) <= 1e-4f,
                            "AUV root moved during Hold.");
                        frozenRootDisplacement = Vector3.Distance(
                            driver.TargetRoot.position, rootAtHold);
                        Require(ActualPointCount() == trackAtHold,
                            "Actual Track changed during stable Hold.");
                        Advance(Phase.ApplySafeRecovery, now);
                        break;
                    case Phase.ApplySafeRecovery:
                        ApplySafeRecovery();
                        Advance(Phase.VerifyRecovery, now);
                        break;
                    case Phase.VerifyRecovery:
                        if (now - phaseStarted < 1.0) return;
                        Require((auv.RouteExecutionState ==
                                    VehicleRouteExecutionState.Running ||
                                 auv.RouteExecutionState ==
                                    VehicleRouteExecutionState.Completed) &&
                                auv.RouteProgress01 > 0.0,
                            "Safe Apply did not resume AUV motion.");
                        Require(driver.LastAppliedSourceEpoch == recoveryEpoch,
                            "Driver consumed a retired pose after recovery.");
                        Require(Vector3.Distance(driver.TargetRoot.position,
                                rootBeforeRecovery) < 2.0f,
                            "AUV teleported after safe Apply.");
                        trackAfter = ActualPointCount();
                        Require(trackAfter >= trackAtHold,
                            "Actual Track was cleared or rewound.");
                        Advance(Phase.VerifyControls, now);
                        break;
                    case Phase.VerifyControls:
                        VerifyControls();
                        WritePassReport();
                        Debug.Log(
                            "ENV_E3D_AUV_TERRAIN_SAFETY_PLAY_MODE_PASS");
                        Finish(0);
                        break;
                }
            }
            catch (Exception exception)
            {
                if (terrain != null) terrain.enabled = true;
                Debug.LogException(exception);
                Debug.LogError(
                    "ENV_E3D_AUV_TERRAIN_SAFETY_PLAY_MODE_FAIL | " +
                    exception.Message);
                Finish(1);
            }
        }

        private static void VerifyUnsafeApplyHasNoSideEffects()
        {
            auvBeforeUnsafe = Capture(auv);
            Require(auv.TryGetDriverAcceptedPose(
                    out safeAnchor, out ulong anchorEpoch,
                    out string anchorError), anchorError);
            Require(anchorEpoch == auvBeforeUnsafe.SourceEpoch,
                "Recovery anchor belongs to a retired SourceEpoch.");
            rovIdentity = CaptureStable(rov);
            usvIdentity = CaptureStable(usv);
            int trackBefore = ActualPointCount();
            Vector3 rootBefore = driver.TargetRoot.position;
            var unsafeDraft = new List<Vector3d>
            {
                new Vector3d(-1.85, -1.35, -1.65),
                new Vector3d(-1.85, -1.35, 1.35),
                new Vector3d(0.15, -2.35, 4.35),
                new Vector3d(1.15, -3.35, 7.35),
                new Vector3d(-1.85, -2.35, 10.35)
            };
            Require(!auv.TryApplyDraftRoute(unsafeDraft,
                    out unsafeError) &&
                    !string.IsNullOrWhiteSpace(unsafeError),
                "Audited hull-penetrating route was not rejected.");
            Require(Capture(auv).Equals(auvBeforeUnsafe) &&
                    ActualPointCount() == trackBefore &&
                    Vector3.Distance(driver.TargetRoot.position,
                        rootBefore) <= 1e-4f,
                "Rejected unsafe Apply changed runtime, track, or root state.");
            Require(CaptureStable(rov).Equals(rovIdentity) &&
                    CaptureStable(usv).Equals(usvIdentity),
                "Rejected AUV Apply changed ROV or USV route identity.");
        }

        private static void ApplySafeRecovery()
        {
            terrain.enabled = true;
            Physics.SyncTransforms();
            ActiveRouteSnapshot active = auv.ActiveRouteSnapshot;
            Vector3d first = active.GetWaypoint(0);
            Vector3d second = active.GetWaypoint(1);
            double dx = second.X - first.X;
            double dy = second.Y - first.Y;
            double dz = second.Z - first.Z;
            double inverse = 1.0 / Math.Sqrt(dx * dx + dy * dy + dz * dz);
            var tangent = new Vector3d(
                dx * inverse, dy * inverse, dz * inverse);
            var safe = new List<Vector3d>
            {
                safeAnchor.Position,
                Add(safeAnchor.Position, Scale(tangent, 0.20)),
                Add(safeAnchor.Position, Scale(tangent, 0.40))
            };
            ulong version = auv.RouteVersion;
            ulong routeEpoch = auv.RouteEpoch;
            StableIdentity rovBefore = CaptureStable(rov);
            StableIdentity usvBefore = CaptureStable(usv);
            rootBeforeRecovery = driver.TargetRoot.position;
            Require(auv.TryApplyDraftRoute(safe, out string error), error);
            Require(auv.RouteVersion == version + 1UL &&
                    auv.RouteEpoch == routeEpoch + 1UL &&
                    auv.RouteExecutionState ==
                        VehicleRouteExecutionState.Running &&
                    auv.TryGetActiveEpoch(out recoveryEpoch) &&
                    recoveryEpoch == epochAtHold + 1UL,
                "Safe Apply did not atomically restart from Hold.");
            Require(CaptureStable(rov).Equals(rovBefore) &&
                    CaptureStable(usv).Equals(usvBefore),
                "Safe AUV Apply changed ROV or USV route identity.");
            Require(Vector3.Distance(driver.TargetRoot.position,
                    rootBeforeRecovery) <= 1e-4f,
                "Safe Apply wrote the Movement Root outside Driver.");
        }

        private static void VerifyControls()
        {
            if (auv.RouteExecutionState ==
                VehicleRouteExecutionState.Completed)
                auv.RestartRoute();
            Require(auv.PauseRoute() &&
                    auv.RouteExecutionState ==
                        VehicleRouteExecutionState.Paused,
                "Pause regression after AUV recovery.");
            Require(auv.ResumeRoute() &&
                    auv.RouteExecutionState ==
                        VehicleRouteExecutionState.Running,
                "Resume regression after AUV recovery.");
            ulong version = auv.RouteVersion;
            Require(auv.TryGetActiveEpoch(out ulong epochBeforeRestart),
                "Restart precondition has no SourceEpoch.");
            Require(auv.RestartRoute() &&
                    auv.RouteVersion == version &&
                    auv.RouteExecutionState ==
                        VehicleRouteExecutionState.Running &&
                    auv.TryGetActiveEpoch(out ulong epochAfterRestart) &&
                    epochAfterRestart == epochBeforeRestart + 1UL,
                "Restart regression after AUV recovery.");
        }

        private static bool TryBind()
        {
            VehicleDataRuntimeHost[] hosts = UnityEngine.Object
                .FindObjectsByType<VehicleDataRuntimeHost>();
            VehiclePoseDriver[] drivers = UnityEngine.Object
                .FindObjectsByType<VehiclePoseDriver>();
            if (hosts.Length != 3 || drivers.Length != 3) return false;
            auv = hosts.Single(value =>
                value.IntegrationConfiguration.VehicleType == VehicleType.Auv);
            rov = hosts.Single(value =>
                value.IntegrationConfiguration.VehicleType == VehicleType.Rov);
            usv = hosts.Single(value =>
                value.IntegrationConfiguration.VehicleType == VehicleType.Usv);
            driver = drivers.Single(value =>
                ReferenceEquals(value.RuntimeHost, auv));
            constraint = driver.PoseConstraintProvider as
                AuvTerrainClearanceConstraint;
            terrain = constraint == null ||
                      constraint.SurfaceSampler == null
                ? null
                : constraint.SurfaceSampler.ContactTerrain;
            trajectory = UnityEngine.Object
                .FindAnyObjectByType<
                    VehicleTrajectoryVisualizationController>();
            return constraint != null && terrain != null &&
                constraint.WaterSurface != null &&
                constraint.WaterSurface.gameObject.activeInHierarchy &&
                constraint.Profile != null &&
                constraint.Profile.HullEnvelopeCornerCount == 8 &&
                Mathf.Abs(constraint.Profile.MinimumHullSubmergenceMeters -
                    0.18f) <= 1e-6f &&
                Mathf.Abs(constraint.Profile.MaximumClimbAngleDegrees -
                    45f) <= 1e-6f &&
                Mathf.Abs(constraint.Profile.MaximumDescentAngleDegrees -
                    45f) <= 1e-6f &&
                trajectory != null;
        }

        private static bool Ready()
        {
            return auv.IsInitialized &&
                auv.SourceStatus == DataSourceStatus.Running &&
                auv.RouteExecutionState ==
                    VehicleRouteExecutionState.Running &&
                driver.HasFreshAppliedPose &&
                driver.LastPoseConstraintDecision !=
                    UnityPoseConstraintDecision.HoldCurrent &&
                auv.TryGetActiveEpoch(out ulong epoch) &&
                driver.LastAppliedSourceEpoch == epoch;
        }

        private static RuntimeIdentity Capture(VehicleDataRuntimeHost host)
        {
            Require(host.TryGetActiveEpoch(out ulong epoch),
                "Runtime has no active SourceEpoch.");
            return new RuntimeIdentity(
                host.RouteVersion, host.RouteEpoch, epoch,
                host.RouteProgress01, host.RouteExecutionState.Value,
                host.Store.GetStatistics().AcceptedSamples);
        }

        private static StableIdentity CaptureStable(
            VehicleDataRuntimeHost host)
        {
            Require(host.TryGetActiveEpoch(out ulong epoch),
                "Runtime has no stable SourceEpoch.");
            return new StableIdentity(
                host.RouteVersion, host.RouteEpoch, epoch);
        }

        private static int ActualPointCount()
        {
            FieldInfo statesField = trajectory.GetType().GetField(
                "traceStates", BindingFlags.Instance | BindingFlags.NonPublic);
            IDictionary states = statesField == null
                ? null
                : statesField.GetValue(trajectory) as IDictionary;
            if (states == null ||
                !states.Contains(VehicleSelectionKind.Auv)) return 0;
            object state = states[VehicleSelectionKind.Auv];
            FieldInfo countField = state.GetType().GetField(
                "TotalPointCount",
                BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
            return countField == null
                ? 0
                : (int)countField.GetValue(state);
        }

        private static Vector3d Add(Vector3d a, Vector3d b)
        {
            return new Vector3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        private static Vector3d Scale(Vector3d value, double scalar)
        {
            return new Vector3d(
                value.X * scalar, value.Y * scalar, value.Z * scalar);
        }

        private static void WritePassReport()
        {
            string path = RequireExternalCreateNewPath();
            var report = new PlayReport
            {
                schema = "ENV-E3D-AuvTerrainSafety-PlayMode-v1",
                status = "ENV_E3D_AUV_TERRAIN_SAFETY_PLAY_MODE_PASS",
                unityVersion = Application.unityVersion,
                unsafeApplyError = unsafeError,
                frozenProgress = progressAtHold,
                frozenRootDisplacement = frozenRootDisplacement,
                holdSourceEpoch = epochAtHold,
                recoveredSourceEpoch = recoveryEpoch,
                actualTrackBefore = trackAtHold,
                actualTrackAfter = trackAfter,
                noTeleport = true,
                noOldPoseFlashback = true,
                rovUsvIsolated = true
            };
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = new FileStream(
                       path, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None))
            using (var writer = new StreamWriter(
                       stream, new UTF8Encoding(false)))
                writer.Write(JsonUtility.ToJson(report, true) +
                    Environment.NewLine);
        }

        private static string RequireExternalCreateNewPath()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            string value = string.Empty;
            for (int index = 0; index + 1 < arguments.Length; index++)
                if (arguments[index] == ReportArgument)
                    value = arguments[index + 1];
            Require(!string.IsNullOrWhiteSpace(value),
                "Missing " + ReportArgument + ".");
            string path = Path.GetFullPath(value);
            string project = Path.GetFullPath(Path.Combine(
                Application.dataPath, ".."));
            Require(!path.StartsWith(project +
                    Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase),
                "PlayMode report must be outside the Unity project.");
            Require(!File.Exists(path),
                "PlayMode report path must be create-new.");
            return path;
        }

        private static void Advance(Phase next, double now)
        {
            phase = next;
            phaseStarted = now;
        }

        private static void Finish(int exitCode)
        {
            bool batch = SessionState.GetBool(BatchKey, false);
            SessionState.SetBool(ActiveKey, false);
            SessionState.SetBool(BatchKey, false);
            if (subscribed)
            {
                EditorApplication.playModeStateChanged -=
                    OnPlayModeStateChanged;
                EditorApplication.update -= OnUpdate;
                subscribed = false;
            }
            if (EditorApplication.isPlaying)
                EditorApplication.ExitPlaymode();
            if (batch) EditorApplication.Exit(exitCode);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private readonly struct StableIdentity : IEquatable<StableIdentity>
        {
            public StableIdentity(
                ulong version, ulong routeEpoch, ulong sourceEpoch)
            {
                Version = version;
                RouteEpoch = routeEpoch;
                SourceEpoch = sourceEpoch;
            }
            private ulong Version { get; }
            private ulong RouteEpoch { get; }
            private ulong SourceEpoch { get; }
            public bool Equals(StableIdentity other)
            {
                return Version == other.Version &&
                    RouteEpoch == other.RouteEpoch &&
                    SourceEpoch == other.SourceEpoch;
            }
        }

        private readonly struct RuntimeIdentity : IEquatable<RuntimeIdentity>
        {
            public RuntimeIdentity(
                ulong version, ulong routeEpoch, ulong sourceEpoch,
                double progress, VehicleRouteExecutionState state,
                ulong acceptedSamples)
            {
                Version = version;
                RouteEpoch = routeEpoch;
                SourceEpoch = sourceEpoch;
                Progress = progress;
                State = state;
                AcceptedSamples = acceptedSamples;
            }
            public ulong SourceEpoch { get; }
            private ulong Version { get; }
            private ulong RouteEpoch { get; }
            private double Progress { get; }
            private VehicleRouteExecutionState State { get; }
            private ulong AcceptedSamples { get; }
            public bool Equals(RuntimeIdentity other)
            {
                return Version == other.Version &&
                    RouteEpoch == other.RouteEpoch &&
                    SourceEpoch == other.SourceEpoch &&
                    Math.Abs(Progress - other.Progress) <= 1e-12 &&
                    State == other.State &&
                    AcceptedSamples == other.AcceptedSamples;
            }
        }
    }
}
