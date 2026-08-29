using System;
using System.IO;
using System.Linq;
using System.Text;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Constraints;
using UnderwaterRobotScene.Visualization.Runtime.Usv;
using UnderwaterRobotScene.Visualization.Sampling;
using UnderwaterRobotScene.Visualization.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    [InitializeOnLoad]
    public static class ThreeVehicleSoleWriterTransitionPlayModeVerifier
    {
        private const string ActiveKey = "E3D.SoleWriterTransition.Active";
        private const string PassedKey = "E3D.SoleWriterTransition.Passed";
        private const string DetailKey = "E3D.SoleWriterTransition.Detail";
        private const string BatchKey = "E3D.SoleWriterTransition.Batch";
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const float ContinuityToleranceMeters = 0.10f;
        private const float MaximumFrameStepMeters = 0.25f;
        private const float ContinuityToleranceDegrees = 5f;
        private const float MaximumFrameStepDegrees = 30f;

        private enum Phase
        {
            WaitInitialHealthy,
            WaitDemoEpoch,
            ObserveDemo,
            WaitPublicEpoch,
            ObservePublic,
            WaitFinal
        }

        private sealed class Vehicle
        {
            public string Label;
            public Transform Root;
            public VehiclePoseDriver Driver;
            public VehicleDataRuntimeHost Host;
            public VehiclePoseControlAuthority Authority;
            public ActiveRouteSnapshot Route;
            public ulong RouteVersion;
            public ulong RouteEpoch;
        }

        private static bool subscribed;
        private static bool referencesBound;
        private static int errors;
        private static int targetIndex;
        private static Phase phase;
        private static double enteredAt;
        private static double phaseStartedAt;
        private static DemoMotionController demo;
        private static Vehicle[] vehicles;
        private static ulong[] siblingEpochs;
        private static Vector3[] siblingPoses;
        private static ulong transitionFromEpoch;
        private static Vector3 transitionFromPose;
        private static Quaternion transitionFromRotation;
        private static Vector3 lastObservedPose;
        private static Quaternion lastObservedRotation;
        private static float maximumObservedStep;
        private static float maximumObservedAngleStep;
        private static double suspendedRouteDistance;

        static ThreeVehicleSoleWriterTransitionPlayModeVerifier()
        {
            if (SessionState.GetBool(ActiveKey, false))
            {
                Subscribe();
            }
        }

        [MenuItem("Tools/Underwater Demo/E3D/Run Sole-Writer Source Transition Verification")]
        public static void RunFromMenu()
        {
            Begin(false);
        }

        public static void RunBatch()
        {
            Begin(true);
        }

        private static void Begin(bool batch)
        {
            if (SessionState.GetBool(ActiveKey, false))
            {
                throw new InvalidOperationException(
                    "The E3D sole-writer transition verifier is already active.");
            }
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(PassedKey, false);
            SessionState.SetString(DetailKey, "Verification did not complete.");
            SessionState.SetBool(BatchKey, batch);
            Subscribe();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        private static void Subscribe()
        {
            if (subscribed)
            {
                return;
            }
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;
            Application.logMessageReceived += OnLog;
            subscribed = true;
        }

        private static void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= OnEditorUpdate;
            Application.logMessageReceived -= OnLog;
            subscribed = false;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(ActiveKey, false))
            {
                return;
            }
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                enteredAt = Time.realtimeSinceStartupAsDouble;
                phaseStartedAt = enteredAt;
                phase = Phase.WaitInitialHealthy;
                referencesBound = false;
                errors = 0;
                targetIndex = 0;
                maximumObservedStep = 0f;
                maximumObservedAngleStep = 0f;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                Finish();
            }
        }

        private static void OnEditorUpdate()
        {
            if (!SessionState.GetBool(ActiveKey, false) ||
                !EditorApplication.isPlaying)
            {
                return;
            }
            try
            {
                if (!referencesBound)
                {
                    BindReferences();
                    referencesBound = true;
                }
                ObserveFrameStep();
                double now = Time.realtimeSinceStartupAsDouble;
                double elapsed = now - phaseStartedAt;
                switch (phase)
                {
                    case Phase.WaitInitialHealthy:
                        if (AllPublicHealthy())
                        {
                            BeginDemoTransition(now);
                        }
                        else if (now - enteredAt > 7.0)
                        {
                            throw new InvalidOperationException(
                                "All three Driver-owned RouteFollowing chains did not become healthy.");
                        }
                        break;

                    case Phase.WaitDemoEpoch:
                        if (TransitionReady(VehicleRuntimeSourceMode.LocalDiagnostic))
                        {
                            ValidateTransitionFirstPose(
                                VehicleRuntimeSourceMode.LocalDiagnostic);
                            suspendedRouteDistance = Target.Host.RouteDistanceAlongRoute;
                            ValidateDemoContract();
                            Advance(Phase.ObserveDemo, now);
                        }
                        else if (elapsed > 3.0)
                        {
                            throw new InvalidOperationException(
                                Target.Label + " did not enter a healthy LocalDiagnostic epoch.");
                        }
                        break;

                    case Phase.ObserveDemo:
                        if (elapsed >= 1.10)
                        {
                            Require(Math.Abs(
                                    Target.Host.RouteDistanceAlongRoute -
                                    suspendedRouteDistance) <= 1e-9,
                                Target.Label + " route execution advanced while Demo was active.");
                            Require(Vector3.Distance(
                                    transitionFromPose, Target.Root.position) > 0.0001f,
                                Target.Label + " did not move through the Driver in Demo mode.");
                            ValidateSiblings();
                            BeginPublicTransition(now);
                        }
                        break;

                    case Phase.WaitPublicEpoch:
                        if (TransitionReady(VehicleRuntimeSourceMode.RouteFollowing))
                        {
                            ValidateTransitionFirstPose(
                                VehicleRuntimeSourceMode.RouteFollowing);
                            ValidateRouteIdentity();
                            Advance(Phase.ObservePublic, now);
                        }
                        else if (elapsed > 3.0)
                        {
                            throw new InvalidOperationException(
                                Target.Label + " did not return to a healthy RouteFollowing epoch.");
                        }
                        break;

                    case Phase.ObservePublic:
                        if (elapsed >= 0.45)
                        {
                            Require(Vector3.Distance(
                                    transitionFromPose, Target.Root.position) > 0.0001f,
                                Target.Label + " did not resume route execution through its continuity bridge.");
                            ValidateCurrentSamplerAndRetiredEpoch();
                            ValidateSiblings();
                            targetIndex++;
                            if (targetIndex < vehicles.Length)
                            {
                                BeginDemoTransition(now);
                            }
                            else
                            {
                                Advance(Phase.WaitFinal, now);
                            }
                        }
                        break;

                    case Phase.WaitFinal:
                        if (elapsed >= 0.35 && AllPublicHealthy())
                        {
                            Require(errors == 0,
                                "Runtime Console reported " + errors + " errors.");
                            Require(maximumObservedStep <= MaximumFrameStepMeters,
                                "A transition produced a root step of " +
                                maximumObservedStep.ToString("F4") + " m.");
                            Require(maximumObservedAngleStep <=
                                    MaximumFrameStepDegrees,
                                "A transition produced a root rotation step of " +
                                maximumObservedAngleStep.ToString("F3") + " degrees.");
                            SessionState.SetBool(PassedKey, true);
                            SessionState.SetString(
                                DetailKey,
                                "AUV, ROV and USV each completed RouteFollowing -> LocalDiagnostic -> " +
                                "RouteFollowing with Driver ownership, newer isolated epochs, retired old " +
                                "epochs, first-pose continuity, suspended route authority and preserved route identity.");
                            EditorApplication.ExitPlaymode();
                        }
                        else if (elapsed > 3.0)
                        {
                            throw new InvalidOperationException(
                                "The final all-PublicData healthy state was not reached.");
                        }
                        break;
                }
            }
            catch (Exception exception)
            {
                SessionState.SetBool(PassedKey, false);
                SessionState.SetString(
                    DetailKey,
                    exception.GetType().Name + ": " + exception.Message);
                EditorApplication.ExitPlaymode();
            }
        }

        private static Vehicle Target => vehicles[targetIndex];

        private static void BindReferences()
        {
            demo = UnityEngine.Object.FindAnyObjectByType<DemoMotionController>();
            Require(demo != null, "DemoMotionController is missing.");
            vehicles = new[]
            {
                Bind("AUV", "AUV_Yellow_Underwater", "AUV_PublicPoseDriver"),
                Bind("ROV", "ROV_Box_Seabed", "ROV_PublicPoseDriver"),
                Bind("USV", "USV_Blue_Surface", "USV_PublicPoseDriver")
            };
            Require(vehicles.Select(value => value.Root).Distinct().Count() == 3 &&
                    vehicles.Select(value => value.Host).Distinct().Count() == 3 &&
                    vehicles.Select(value => value.Driver).Distinct().Count() == 3,
                "Vehicle roots, Hosts and Drivers must be isolated.");
            foreach (Vehicle vehicle in vehicles)
            {
                vehicle.Authority.Mode = VehiclePoseControlMode.PublicData;
            }
            Require(!demo.DrivesAuv && !demo.DrivesRov && !demo.DrivesUsv,
                "DemoMotionController still reports a production root-writer role.");
        }

        private static Vehicle Bind(
            string label,
            string rootName,
            string driverName)
        {
            GameObject rootObject = GameObject.Find(rootName);
            GameObject driverObject = GameObject.Find(driverName);
            Require(rootObject != null && driverObject != null,
                label + " root or Driver object is missing.");
            VehiclePoseDriver driver = driverObject.GetComponent<VehiclePoseDriver>();
            VehiclePoseControlAuthority authority =
                rootObject.GetComponent<VehiclePoseControlAuthority>();
            Require(driver != null && driver.RuntimeHost != null && authority != null &&
                    ReferenceEquals(driver.TargetRoot, rootObject.transform) &&
                    ReferenceEquals(driver.ControlAuthority, authority),
                label + " Driver/Host/Authority binding is invalid.");
            var value = new Vehicle
            {
                Label = label,
                Root = rootObject.transform,
                Driver = driver,
                Host = driver.RuntimeHost,
                Authority = authority,
                Route = driver.RuntimeHost.ActiveRouteSnapshot,
                RouteVersion = driver.RuntimeHost.RouteVersion,
                RouteEpoch = driver.RuntimeHost.RouteEpoch
            };
            Require(value.Route != null && value.Host.AuthoritySourceSelectionEnabled,
                label + " does not expose a retained authority-selectable route source.");
            return value;
        }

        private static void BeginDemoTransition(double now)
        {
            Require(AllPublicHealthy(),
                "A source transition did not start from three healthy RouteFollowing chains.");
            CaptureTransitionBaseline();
            Target.Authority.Mode = VehiclePoseControlMode.Demo;
            Advance(Phase.WaitDemoEpoch, now);
        }

        private static void BeginPublicTransition(double now)
        {
            CaptureTransitionBaseline();
            Target.Authority.Mode = VehiclePoseControlMode.PublicData;
            Advance(Phase.WaitPublicEpoch, now);
        }

        private static void CaptureTransitionBaseline()
        {
            siblingEpochs = vehicles.Select(CurrentEpoch).ToArray();
            siblingPoses = vehicles.Select(value => value.Root.position).ToArray();
            transitionFromEpoch = CurrentEpoch(Target);
            transitionFromPose = Target.Root.position;
            transitionFromRotation = Target.Root.rotation;
            lastObservedPose = transitionFromPose;
            lastObservedRotation = transitionFromRotation;
            maximumObservedStep = 0f;
            maximumObservedAngleStep = 0f;
        }

        private static bool TransitionReady(VehicleRuntimeSourceMode expectedMode)
        {
            return Target.Host.SourceMode == expectedMode &&
                   Target.Driver.OwnsControl &&
                   Target.Driver.HasFreshAppliedPose &&
                   Target.Driver.LastFailureReason == RenderSampleFailureReason.None &&
                   Target.Driver.LastAppliedSourceEpoch == CurrentEpoch(Target) &&
                   CurrentEpoch(Target) > transitionFromEpoch;
        }

        private static void ValidateTransitionFirstPose(
            VehicleRuntimeSourceMode expectedMode)
        {
            ulong newEpoch = CurrentEpoch(Target);
            Require(Target.Driver.OwnsControl &&
                    Target.Host.SourceMode == expectedMode &&
                    Target.Driver.LastAppliedSourceEpoch == newEpoch,
                Target.Label + " Driver/Store/source did not converge on the new epoch.");
            Require(Vector3.Distance(transitionFromPose, Target.Root.position) <=
                    ContinuityToleranceMeters,
                Target.Label + " first new-epoch pose was not continuous.");
            Require(Quaternion.Angle(
                        transitionFromRotation,
                        Target.Root.rotation) <= ContinuityToleranceDegrees,
                Target.Label + " first new-epoch orientation was not continuous.");
            Require(newEpoch > transitionFromEpoch,
                Target.Label + " did not allocate a strictly newer SourceEpoch.");
            ValidateRouteIdentity();
            ValidateSiblings();
            ValidateCurrentSamplerAndRetiredEpoch();
        }

        private static void ValidateDemoContract()
        {
            Require(!DemoDrives(targetIndex),
                Target.Label + " DemoMotionController still owns the root.");
            if (Target.Label == "AUV")
            {
                IUnityPoseConstraint constraint =
                    Target.Driver.PoseConstraintProvider as IUnityPoseConstraint;
                Require(constraint != null,
                    "AUV terrain/water constraint is missing.");
                Require(Target.Driver.LastPoseConstraintDecision ==
                        UnityPoseConstraintDecision.Apply,
                    "AUV Demo sample did not pass through its constraint.");
                constraint.ResetObservation();
                var unsafeRequest = new UnityPoseConstraintRequest(
                    Target.Root.position + Vector3.down * 1000f,
                    Target.Root.rotation,
                    CurrentEpoch(Target));
                UnityPoseConstraintResult unsafeResult =
                    constraint.Constrain(in unsafeRequest);
                Require(unsafeResult.Decision ==
                        UnityPoseConstraintDecision.HoldCurrent,
                    "An unsafe AUV Demo candidate was not rejected fail-closed.");
                constraint.ResetObservation();
            }
            else if (Target.Label == "ROV")
            {
                Require(Target.Driver.PoseConstraintProvider is IUnityPoseConstraint &&
                        Target.Driver.LastPoseConstraintDecision ==
                            UnityPoseConstraintDecision.Apply,
                    "ROV Demo contact constraint did not remain in the Driver path.");
            }
            else
            {
                UsvSurfaceVisualController surface =
                    UnityEngine.Object.FindObjectsByType<UsvSurfaceVisualController>()
                        .Single(value => ReferenceEquals(
                            value.BusinessRoot, Target.Root));
                Require(ReferenceEquals(surface.PoseDriver, Target.Driver) &&
                        surface.ImportedModelRoot.parent == surface.transform &&
                        surface.WaterSurfaceProvider != null,
                    "USV child-local surface presentation is not separated from the business root.");
                Require(surface.WaterSurfaceProvider.TrySample(
                        Target.Root.position,
                        out Vector3 surfacePoint,
                        out Vector3 surfaceNormal) &&
                        Mathf.Abs(Vector3.Dot(
                            Target.Root.position - surfacePoint,
                            surfaceNormal)) <= 0.5f,
                    "USV Demo business root is outside its surface authority tolerance.");
            }
        }

        private static void ValidateRouteIdentity()
        {
            Require(ReferenceEquals(Target.Route, Target.Host.ActiveRouteSnapshot) &&
                    Target.Host.RouteVersion == Target.RouteVersion &&
                    Target.Host.RouteEpoch == Target.RouteEpoch,
                Target.Label + " Active route identity/version/RouteEpoch changed during source selection.");
        }

        private static void ValidateSiblings()
        {
            for (int index = 0; index < vehicles.Length; index++)
            {
                if (index == targetIndex)
                {
                    continue;
                }
                Vehicle sibling = vehicles[index];
                Require(CurrentEpoch(sibling) == siblingEpochs[index] &&
                        Healthy(sibling, VehicleRuntimeSourceMode.RouteFollowing),
                    sibling.Label + " epoch/health changed during " +
                    Target.Label + " source transition.");
                Require(Vector3.Distance(
                        siblingPoses[index], sibling.Root.position) > 0.0001f,
                    sibling.Label + " stopped moving during " +
                    Target.Label + " source transition.");
            }
        }

        private static void ValidateCurrentSamplerAndRetiredEpoch()
        {
            Vehicle target = Target;
            ulong currentEpoch = CurrentEpoch(target);
            Require(target.Driver.ProfileConfiguration.TryBuildProfile(
                    out CoordinateTransformProfile profile,
                    out string profileError),
                target.Label + " profile failed: " + profileError);
            double now = target.Host.MonotonicNowSeconds;
            var currentRequest = new RenderSampleRequest(
                target.Host.SourceId,
                currentEpoch,
                target.Host.VehicleId,
                target.Host.GetTargetSourceTimestamp(
                    now,
                    target.Driver.IntegrationConfiguration.RenderDelaySeconds),
                now,
                target.Host.SourceStatus,
                profile,
                target.Driver.IntegrationConfiguration.BuildSamplingPolicy());
            Require(VehicleRenderSampler.Sample(
                    target.Host.Store, currentRequest).Succeeded,
                target.Label + " current SourceEpoch was not sampleable.");
            var retiredRequest = new RenderSampleRequest(
                target.Host.SourceId,
                transitionFromEpoch,
                target.Host.VehicleId,
                0.0,
                now,
                target.Host.SourceStatus,
                profile,
                target.Driver.IntegrationConfiguration.BuildSamplingPolicy());
            Require(!VehicleRenderSampler.Sample(
                    target.Host.Store, retiredRequest).Succeeded,
                target.Label + " retired SourceEpoch remained sampleable.");
        }

        private static void ObserveFrameStep()
        {
            if (vehicles == null || targetIndex >= vehicles.Length ||
                phase == Phase.WaitInitialHealthy)
            {
                return;
            }
            Vector3 current = Target.Root.position;
            maximumObservedStep = Mathf.Max(
                maximumObservedStep,
                Vector3.Distance(lastObservedPose, current));
            maximumObservedAngleStep = Mathf.Max(
                maximumObservedAngleStep,
                Quaternion.Angle(
                    lastObservedRotation,
                    Target.Root.rotation));
            lastObservedPose = current;
            lastObservedRotation = Target.Root.rotation;
        }

        private static bool AllPublicHealthy()
        {
            return vehicles != null && vehicles.All(value =>
                value.Authority.PublicDataOwnsControl &&
                Healthy(value, VehicleRuntimeSourceMode.RouteFollowing));
        }

        private static bool Healthy(
            Vehicle vehicle,
            VehicleRuntimeSourceMode mode)
        {
            return vehicle.Driver.enabled &&
                   vehicle.Driver.OwnsControl &&
                   vehicle.Host.SourceMode == mode &&
                   vehicle.Host.SourceStatus == DataSourceStatus.Running &&
                   vehicle.Driver.HasFreshAppliedPose &&
                   vehicle.Driver.LastFailureReason == RenderSampleFailureReason.None &&
                   vehicle.Driver.LastAppliedSourceEpoch == CurrentEpoch(vehicle);
        }

        private static ulong CurrentEpoch(Vehicle vehicle)
        {
            Require(vehicle.Host.TryGetActiveEpoch(out ulong epoch),
                vehicle.Label + " active SourceEpoch is unavailable.");
            return epoch;
        }

        private static bool DemoDrives(int index)
        {
            return index == 0
                ? demo.DrivesAuv
                : index == 1
                    ? demo.DrivesRov
                    : demo.DrivesUsv;
        }

        private static void Advance(Phase next, double now)
        {
            phase = next;
            phaseStartedAt = now;
        }

        private static void Finish()
        {
            bool passed = SessionState.GetBool(PassedKey, false);
            string detail = SessionState.GetString(DetailKey, "No result.");
            string marker = passed
                ? "ENV_E3D_SOLE_WRITER_SOURCE_TRANSITION_PLAYMODE_PASS"
                : "ENV_E3D_SOLE_WRITER_SOURCE_TRANSITION_PLAYMODE_FAIL";
            string directory = Path.Combine(
                Path.GetTempPath(),
                "UnderwaterRobotScene",
                "E3D_SoleWriterTransition");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "e3d_sole_writer_transition_report.txt"),
                marker + "\n" + detail + "\n",
                new UTF8Encoding(false));
            bool batch = SessionState.GetBool(BatchKey, false);
            SessionState.SetBool(ActiveKey, false);
            SessionState.SetBool(BatchKey, false);
            Unsubscribe();
            Debug.Log(marker + " | " + detail);
            if (batch)
            {
                EditorApplication.Exit(passed ? 0 : 1);
            }
        }

        private static void OnLog(
            string condition,
            string stackTrace,
            LogType type)
        {
            if (type == LogType.Error ||
                type == LogType.Exception ||
                type == LogType.Assert)
            {
                errors++;
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
