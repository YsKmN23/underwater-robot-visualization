using System;
using System.Linq;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Constraints;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    [InitializeOnLoad]
    public static class EnvE3CRouteFollowingPlayModeVerifier
    {
        private const string ActiveKey = "E3C.RouteFollowing.Active";
        private const string BatchKey = "E3C.RouteFollowing.Batch";
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";

        private enum Phase
        {
            WaitHealthy,
            ObserveMovement,
            ObserveAuvPause,
            ObserveAuvResume,
            ObserveRovRestart,
            ObserveRovHold,
            ObserveUsvComplete,
            ObserveStableEndpoint
        }

        private sealed class Vehicle
        {
            public VehicleType Type;
            public VehicleDataRuntimeHost Host;
            public VehiclePoseDriver Driver;
            public Transform Root;
            public Vector3 Position;
            public double Progress;
            public ulong RouteEpoch;
            public ulong SourceEpoch;
        }

        private static bool subscribed;
        private static bool bound;
        private static double phaseStarted;
        private static Phase phase;
        private static Vehicle[] vehicles;
        private static Vector3 completedUsvPosition;

        static EnvE3CRouteFollowingPlayModeVerifier()
        {
            if (SessionState.GetBool(ActiveKey, false))
                Subscribe();
        }

        public static void RunBatch()
        {
            Begin(true);
        }

        [MenuItem("Tools/Underwater Demo/E3-C/Run Route Following Play Mode Verification")]
        public static void RunFromMenu()
        {
            Begin(false);
        }

        private static void Begin(bool batch)
        {
            if (SessionState.GetBool(ActiveKey, false))
                throw new InvalidOperationException(
                    "E3C route following verification is already active.");
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            Subscribe();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        private static void Subscribe()
        {
            if (subscribed)
                return;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnUpdate;
            subscribed = true;
        }

        private static void Unsubscribe()
        {
            if (!subscribed)
                return;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= OnUpdate;
            subscribed = false;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(ActiveKey, false))
                return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                bound = false;
                phase = Phase.WaitHealthy;
                phaseStarted = Time.realtimeSinceStartupAsDouble;
            }
        }

        private static void OnUpdate()
        {
            if (!SessionState.GetBool(ActiveKey, false) ||
                !EditorApplication.isPlaying)
                return;

            try
            {
                if (!bound)
                {
                    Bind();
                    bound = true;
                }

                double now = Time.realtimeSinceStartupAsDouble;
                if (now - phaseStarted > 12.0)
                    throw new InvalidOperationException(
                        "Timed out in E3C phase " + phase + ".");

                switch (phase)
                {
                    case Phase.WaitHealthy:
                        if (!vehicles.All(Healthy))
                            return;
                        Require(vehicles.All(value =>
                                value.Host.SourceMode ==
                                VehicleRuntimeSourceMode.RouteFollowing &&
                                value.Host.ActiveRouteSnapshot != null),
                            "All three hosts must expose an Active Route.");
                        CaptureAll();
                        Advance(Phase.ObserveMovement, now);
                        break;

                    case Phase.ObserveMovement:
                        if (now - phaseStarted < 1.0)
                            return;
                        Require(vehicles.All(value =>
                                value.Host.RouteProgress01 > value.Progress &&
                                Vector3.Distance(value.Root.position,
                                    value.Position) > 0.05f),
                            "AUV, ROV, and USV did not independently follow their routes.");
                        Require(Rov.Driver.HasPoseConstraint &&
                                Rov.Driver.LastPoseConstraintDecision ==
                                UnityPoseConstraintDecision.Apply,
                            "ROV did not pass through its terrain constraint.");
                        Require(Rov.Driver.LastPoseConstraintReason == "Supported" ||
                                Rov.Driver.LastPoseConstraintReason == "Corrected",
                            "ROV terrain constraint did not report supported contact.");
                        Require(RootIsLevel(Usv.Root),
                            "USV business root acquired pitch or roll.");
                        CaptureAll();
                        Require(Auv.Host.PauseRoute(), "AUV Pause failed.");
                        Advance(Phase.ObserveAuvPause, now);
                        break;

                    case Phase.ObserveAuvPause:
                        if (now - phaseStarted < 0.8)
                            return;
                        Require(Near(Auv.Host.RouteProgress01, Auv.Progress) &&
                                Rov.Host.RouteProgress01 > Rov.Progress &&
                                Usv.Host.RouteProgress01 > Usv.Progress,
                            "AUV Pause affected another vehicle or advanced AUV progress.");
                        Require(Auv.Host.ResumeRoute(), "AUV Resume failed.");
                        Auv.Progress = Auv.Host.RouteProgress01;
                        Advance(Phase.ObserveAuvResume, now);
                        break;

                    case Phase.ObserveAuvResume:
                        if (now - phaseStarted < 0.6)
                            return;
                        Require(Auv.Host.RouteProgress01 > Auv.Progress,
                            "AUV Resume did not continue route progress.");
                        CaptureAll();
                        Require(Rov.Host.RestartRoute(), "ROV Restart failed.");
                        Require(Rov.Host.RouteEpoch == Rov.RouteEpoch + 1UL,
                            "ROV Restart did not increment routeEpoch.");
                        Require(Rov.Host.TryGetActiveEpoch(out ulong restartedEpoch) &&
                                restartedEpoch != Rov.SourceEpoch,
                            "ROV Restart did not switch SourceEpoch immediately.");
                        Advance(Phase.ObserveRovRestart, now);
                        break;

                    case Phase.ObserveRovRestart:
                        if (now - phaseStarted < 0.5)
                            return;
                        Require(Rov.Host.RouteProgress01 < Rov.Progress &&
                                Auv.Host.RouteProgress01 > Auv.Progress &&
                                Usv.Host.RouteProgress01 > Usv.Progress,
                            "ROV Restart affected another vehicle or failed to reset progress.");
                        CaptureAll();
                        Require(Rov.Host.NotifyConstraintHold(
                                Rov.SourceEpoch,
                                Rov.Root.position,
                                Rov.Root.rotation,
                                now),
                            "ROV constraint Hold feedback was rejected.");
                        Require(Rov.Host.RouteExecutionState ==
                                VehicleRouteExecutionState.Hold,
                            "ROV did not enter Hold.");
                        Rov.Progress = Rov.Host.RouteProgress01;
                        Advance(Phase.ObserveRovHold, now);
                        break;

                    case Phase.ObserveRovHold:
                        if (now - phaseStarted < 0.7)
                            return;
                        Require(Near(Rov.Host.RouteProgress01, Rov.Progress) &&
                                Auv.Host.RouteProgress01 > Auv.Progress &&
                                Usv.Host.RouteProgress01 > Usv.Progress,
                            "ROV Hold advanced progress or affected another vehicle.");
                        Require(Rov.Host.RestartRoute(),
                            "ROV deterministic Hold recovery failed.");
                        Require(Usv.Host.CompleteRoute(), "USV Complete failed.");
                        Advance(Phase.ObserveUsvComplete, now);
                        break;

                    case Phase.ObserveUsvComplete:
                        if (now - phaseStarted < 0.8)
                            return;
                        Require(Usv.Host.RouteExecutionState ==
                                VehicleRouteExecutionState.Completed &&
                                Near(Usv.Host.RouteProgress01, 1.0),
                            "USV did not complete at its exact endpoint.");
                        completedUsvPosition = Usv.Root.position;
                        Advance(Phase.ObserveStableEndpoint, now);
                        break;

                    case Phase.ObserveStableEndpoint:
                        if (now - phaseStarted < 0.8)
                            return;
                        Require(Vector3.Distance(
                                completedUsvPosition,
                                Usv.Root.position) < 0.001f &&
                                Usv.Driver.HasFreshAppliedPose,
                            "Completed endpoint was unstable or source health stopped.");
                        Require(UnityEngine.Object.FindFirstObjectByType<
                                VehicleTrajectoryVisualizationController>() != null,
                            "Active Route/Actual Track visualization controller is missing.");
                        Pass();
                        break;
                }
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        private static void Bind()
        {
            VehicleDataRuntimeHost[] hosts = UnityEngine.Object.FindObjectsByType<
                VehicleDataRuntimeHost>(FindObjectsSortMode.None);
            VehiclePoseDriver[] drivers = UnityEngine.Object.FindObjectsByType<
                VehiclePoseDriver>(FindObjectsSortMode.None);
            Require(hosts.Length == 3 && drivers.Length == 3,
                "Expected exactly three hosts and three Drivers.");
            vehicles = hosts.Select(host =>
            {
                VehiclePoseDriver driver = drivers.Single(value =>
                    ReferenceEquals(value.RuntimeHost, host));
                return new Vehicle
                {
                    Type = host.IntegrationConfiguration.VehicleType,
                    Host = host,
                    Driver = driver,
                    Root = driver.TargetRoot
                };
            }).OrderBy(value => value.Type).ToArray();
            Require(vehicles.Select(value => value.Root).Distinct().Count() == 3,
                "Movement Root ownership is not isolated.");
        }

        private static bool Healthy(Vehicle value)
        {
            return value.Host.IsInitialized &&
                value.Host.SourceStatus == DataSourceStatus.Running &&
                value.Driver.HasFreshAppliedPose &&
                value.Driver.OwnsControl;
        }

        private static void CaptureAll()
        {
            foreach (Vehicle value in vehicles)
            {
                value.Position = value.Root.position;
                value.Progress = value.Host.RouteProgress01;
                value.RouteEpoch = value.Host.RouteEpoch;
                value.Host.TryGetActiveEpoch(out ulong sourceEpoch);
                value.SourceEpoch = sourceEpoch;
            }
        }

        private static Vehicle Auv => vehicles.Single(value => value.Type == VehicleType.Auv);
        private static Vehicle Rov => vehicles.Single(value => value.Type == VehicleType.Rov);
        private static Vehicle Usv => vehicles.Single(value => value.Type == VehicleType.Usv);

        private static bool RootIsLevel(Transform value)
        {
            Vector3 up = value.up;
            return Vector3.Angle(up, Vector3.up) < 0.01f;
        }

        private static bool Near(double a, double b)
        {
            return Math.Abs(a - b) <= 1e-8;
        }

        private static void Advance(Phase next, double now)
        {
            phase = next;
            phaseStarted = now;
        }

        private static void Pass()
        {
            Debug.Log("ENV_E3C_ROUTE_FOLLOWING_PLAY_MODE_PASS");
            Finish(0);
        }

        private static void Fail(Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("ENV_E3C_ROUTE_FOLLOWING_PLAY_MODE_FAIL | " +
                exception.Message);
            Finish(1);
        }

        private static void Finish(int exitCode)
        {
            bool batch = SessionState.GetBool(BatchKey, false);
            SessionState.SetBool(ActiveKey, false);
            SessionState.SetBool(BatchKey, false);
            Unsubscribe();
            if (EditorApplication.isPlaying)
                EditorApplication.ExitPlaymode();
            if (batch)
                EditorApplication.Exit(exitCode);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
