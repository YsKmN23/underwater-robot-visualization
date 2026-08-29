using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Constraints;
using UnderwaterRobotScene.Visualization.Sampling;
using UnderwaterRobotScene.Visualization.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    [InitializeOnLoad]
    public static class ThreeVehiclePoseN6EPlayModeVerifier
    {
        private const string ActiveKey = "N6E.ThreeVehicle.Active";
        private const string PassedKey = "N6E.ThreeVehicle.Passed";
        private const string DetailKey = "N6E.ThreeVehicle.Detail";
        private const string BatchKey = "N6E.ThreeVehicle.Batch";
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";

        private enum Phase
        {
            WaitInitialHealthy,
            ObserveAllPublic,
            ObserveAuthorityCombination,
            WaitAllPublicAfterMatrix,
            WaitSourceHoldCapture,
            WaitSourceStale,
            WaitSourceRecovery,
            WaitDriverDisabled,
            WaitDriverRecovery,
            WaitFinal
        }

        private sealed class PoseSnapshot
        {
            public Vector3 Position;
            public Quaternion Rotation;

            public static PoseSnapshot Capture(Transform value)
            {
                return new PoseSnapshot
                {
                    Position = value.position,
                    Rotation = value.rotation
                };
            }
        }

        private sealed class LocalSnapshot
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;

            public static LocalSnapshot Capture(Transform value)
            {
                return new LocalSnapshot
                {
                    Position = value.localPosition,
                    Rotation = value.localRotation,
                    Scale = value.localScale
                };
            }

            public bool Matches(Transform value)
            {
                return Near(Position, value.localPosition, 1e-6f) &&
                       Near(Rotation, value.localRotation, 1e-5f) &&
                       Near(Scale, value.localScale, 1e-6f);
            }
        }

        private sealed class VehicleRuntime
        {
            public string Label;
            public string RootName;
            public string ModelName;
            public string DriverName;
            public Transform Root;
            public Transform Model;
            public VehiclePoseDriver Driver;
            public VehicleDataRuntimeHost Host;
            public VehiclePoseIntegrationConfiguration Configuration;
            public VehiclePoseProfileConfiguration Profile;
            public VehiclePoseControlAuthority Authority;
            public PropellerSpinner[] Spinners;
            public LocalSnapshot ModelLocal;
            public LocalSnapshot[] SpinnerLocals;
            public Quaternion[] SpinnerInitialRotations;
            public bool[] SpinnerRotationObserved;
            public Vector3 RootScale;
            public PoseSnapshot InitialPose;
            public ulong InitialEpoch;
            public ulong LastEpoch;
        }

        private static bool subscribed;
        private static bool referencesBound;
        private static int errorCount;
        private static int matrixMask;
        private static int matrixPasses;
        private static int isolationIndex;
        private static double enteredAt;
        private static double phaseStartedAt;
        private static Phase phase;
        private static DemoMotionController demo;
        private static VehicleRuntime[] vehicles;
        private static PoseSnapshot[] phasePoses;
        private static ulong[] phaseEpochs;
        private static PoseSnapshot staleHoldPose;
        private static Transform usvRudder;
        private static LocalSnapshot usvRudderLocal;

        static ThreeVehiclePoseN6EPlayModeVerifier()
        {
            if (SessionState.GetBool(ActiveKey, false))
            {
                Subscribe();
            }
        }

        [MenuItem("Tools/Underwater Demo/N6-E/Run Three-Vehicle Play Mode Verification")]
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
                    "N6-E three-vehicle Play Mode verification is already active.");
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
                errorCount = 0;
                matrixMask = 0;
                matrixPasses = 0;
                isolationIndex = 0;
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

                ObserveSpinnerRotations();
                double now = Time.realtimeSinceStartupAsDouble;
                double elapsed = now - phaseStartedAt;
                switch (phase)
                {
                    case Phase.WaitInitialHealthy:
                        if (AllHealthy())
                        {
                            CaptureInitialState();
                            Advance(Phase.ObserveAllPublic, now);
                        }
                        else if (now - enteredAt > 5.0)
                        {
                            throw new InvalidOperationException(
                                "All three Drivers did not become healthy within five seconds.");
                        }
                        break;

                    case Phase.ObserveAllPublic:
                        if (elapsed >= 1.1)
                        {
                            ValidateAllPublic(now);
                            StartAuthorityCombination(0, now);
                        }
                        break;

                    case Phase.ObserveAuthorityCombination:
                        if (elapsed >= 1.10)
                        {
                            ValidateAuthorityCombination(matrixMask);
                            matrixPasses++;
                            if (matrixMask < 7)
                            {
                                StartAuthorityCombination(matrixMask + 1, now);
                            }
                            else
                            {
                                SetAuthorityMask(7);
                                Advance(Phase.WaitAllPublicAfterMatrix, now);
                            }
                        }
                        break;

                    case Phase.WaitAllPublicAfterMatrix:
                        if (elapsed >= 0.6 && AllHealthy())
                        {
                            isolationIndex = 0;
                            StartSourceIsolation(now);
                        }
                        else if (elapsed > 3.0)
                        {
                            throw new InvalidOperationException(
                                "Drivers did not recover after the authority matrix.");
                        }
                        break;

                    case Phase.WaitSourceHoldCapture:
                        if (elapsed >= 0.35)
                        {
                            staleHoldPose = PoseSnapshot.Capture(
                                vehicles[isolationIndex].Root);
                            Advance(Phase.WaitSourceStale, now);
                        }
                        break;

                    case Phase.WaitSourceStale:
                        if (elapsed >= 0.65)
                        {
                            ValidateSourceStaleIsolation();
                            vehicles[isolationIndex].Host.RestartSource();
                            Advance(Phase.WaitSourceRecovery, now);
                        }
                        break;

                    case Phase.WaitSourceRecovery:
                        if (elapsed >= 0.65 &&
                            Healthy(vehicles[isolationIndex]))
                        {
                            ValidateSourceRecoveryIsolation();
                            isolationIndex++;
                            if (isolationIndex < vehicles.Length)
                            {
                                StartSourceIsolation(now);
                            }
                            else
                            {
                                isolationIndex = 0;
                                StartDriverDisableIsolation(now);
                            }
                        }
                        else if (elapsed > 3.0)
                        {
                            throw new InvalidOperationException(
                                vehicles[isolationIndex].Label +
                                " did not recover after Source restart.");
                        }
                        break;

                    case Phase.WaitDriverDisabled:
                        if (elapsed >= 0.5)
                        {
                            ValidateDriverDisabledIsolation();
                            vehicles[isolationIndex].Driver.enabled = true;
                            Advance(Phase.WaitDriverRecovery, now);
                        }
                        break;

                    case Phase.WaitDriverRecovery:
                        if (elapsed >= 0.5 &&
                            Healthy(vehicles[isolationIndex]) &&
                            PoseChanged(
                                phasePoses[isolationIndex],
                                vehicles[isolationIndex].Root))
                        {
                            ValidateDriverRecoveryIsolation();
                            isolationIndex++;
                            if (isolationIndex < vehicles.Length)
                            {
                                StartDriverDisableIsolation(now);
                            }
                            else
                            {
                                Advance(Phase.WaitFinal, now);
                            }
                        }
                        else if (elapsed > 3.0)
                        {
                            throw new InvalidOperationException(
                                vehicles[isolationIndex].Label +
                                " Driver did not recover after re-enable.");
                        }
                        break;

                    case Phase.WaitFinal:
                        if (elapsed >= 0.5 && AllHealthy())
                        {
                            ValidateFinal();
                            SessionState.SetBool(PassedKey, true);
                            SessionState.SetString(
                                DetailKey,
                                "Three independent Driver-owned chains, all eight Authority/source combinations, " +
                                "per-vehicle stop/stale/recovery/epoch isolation, Driver Disable isolation, " +
                                "root targeting and all local actuator protections passed.");
                            EditorApplication.ExitPlaymode();
                        }
                        else if (elapsed > 3.0)
                        {
                            throw new InvalidOperationException(
                                "Three-vehicle final healthy state was not reached.");
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

        private static void BindReferences()
        {
            demo = UnityEngine.Object.FindAnyObjectByType<DemoMotionController>();
            Require(demo != null, "DemoMotionController is missing.");
            vehicles = new[]
            {
                Bind("AUV", "AUV_Yellow_Underwater",
                    "AUV_FineModel_V1_Imported", "AUV_PublicPoseDriver", 1),
                Bind("ROV", "ROV_Box_Seabed",
                    "ROV_FineModel_V1_Imported", "ROV_PublicPoseDriver", 6),
                Bind("USV", "USV_Blue_Surface",
                    "USV_FineModel_V1_Imported", "USV_PublicPoseDriver", 2)
            };
            Require(vehicles.Select(item => item.Configuration.SourceId).Distinct().Count() == 3 &&
                    vehicles.Select(item => item.Configuration.VehicleId).Distinct().Count() == 3 &&
                    vehicles.Select(item => item.Host).Distinct().Count() == 3 &&
                    vehicles.Select(item => item.Driver).Distinct().Count() == 3 &&
                    vehicles.Select(item => item.Authority).Distinct().Count() == 3 &&
                    vehicles.Select(item => item.Root).Distinct().Count() == 3,
                "Runtime vehicle identities or instances are not isolated.");
            Require(ReferenceEquals(demo.auvControlAuthority, vehicles[0].Authority) &&
                    ReferenceEquals(demo.rovControlAuthority, vehicles[1].Authority) &&
                    ReferenceEquals(demo.usvControlAuthority, vehicles[2].Authority),
                "Demo authority references are incorrect.");
            usvRudder = RequireDescendant(vehicles[2].Root, "USV_Rudder_Main");
        }

        private static VehicleRuntime Bind(
            string label,
            string rootName,
            string modelName,
            string driverName,
            int spinnerCount)
        {
            Transform root = RequireGameObject(rootName).transform;
            Transform model = RequireDescendant(root, modelName);
            VehiclePoseDriver driver =
                RequireGameObject(driverName).GetComponent<VehiclePoseDriver>();
            Require(driver != null, label + " Driver is missing.");
            var value = new VehicleRuntime
            {
                Label = label,
                RootName = rootName,
                ModelName = modelName,
                DriverName = driverName,
                Root = root,
                Model = model,
                Driver = driver,
                Host = driver.RuntimeHost,
                Configuration = driver.IntegrationConfiguration,
                Profile = driver.ProfileConfiguration,
                Authority = root.GetComponent<VehiclePoseControlAuthority>(),
                Spinners = root.GetComponentsInChildren<PropellerSpinner>(true)
            };
            Require(value.Host != null &&
                    value.Configuration != null &&
                    value.Profile != null &&
                    value.Authority != null &&
                    value.Spinners.Length == spinnerCount &&
                    ReferenceEquals(driver.TargetRoot, root) &&
                    ReferenceEquals(driver.ControlAuthority, value.Authority) &&
                    ReferenceEquals(driver.RuntimeHost, value.Host) &&
                    ReferenceEquals(driver.IntegrationConfiguration, value.Configuration) &&
                    ReferenceEquals(driver.ProfileConfiguration, value.Profile),
                label + " runtime binding differs.");
            return value;
        }

        private static void CaptureInitialState()
        {
            foreach (VehicleRuntime vehicle in vehicles)
            {
                vehicle.ModelLocal = LocalSnapshot.Capture(vehicle.Model);
                vehicle.SpinnerLocals = vehicle.Spinners
                    .Select(item => LocalSnapshot.Capture(item.transform))
                    .ToArray();
                vehicle.SpinnerInitialRotations = vehicle.Spinners
                    .Select(item => item.transform.localRotation)
                    .ToArray();
                vehicle.SpinnerRotationObserved = new bool[vehicle.Spinners.Length];
                vehicle.RootScale = vehicle.Root.localScale;
                vehicle.InitialPose = PoseSnapshot.Capture(vehicle.Root);
                Require(vehicle.Host.TryGetActiveEpoch(out vehicle.InitialEpoch),
                    vehicle.Label + " initial epoch is unavailable.");
                vehicle.LastEpoch = vehicle.InitialEpoch;
            }
            usvRudderLocal = LocalSnapshot.Capture(usvRudder);
        }

        private static void ValidateAllPublic(double now)
        {
            Require(errorCount == 0,
                "Runtime Console reported an error during all-PublicData observation.");
            Require(AllHealthy(), "Not all Drivers remained healthy.");
            foreach (VehicleRuntime vehicle in vehicles)
            {
                Require(PoseChanged(vehicle.InitialPose, vehicle.Root),
                    vehicle.Label + " did not move under its public diagnostic source.");
                ValidateOwnSample(vehicle, now);
            }
            Require(!Near(vehicles[0].Root.position, vehicles[1].Root.position, 1e-4f) &&
                    !Near(vehicles[0].Root.position, vehicles[2].Root.position, 1e-4f) &&
                    !Near(vehicles[1].Root.position, vehicles[2].Root.position, 1e-4f),
                "Two vehicle roots unexpectedly converged to one pose position.");
            ValidateProtectedLocals();
        }

        private static void ValidateOwnSample(VehicleRuntime vehicle, double now)
        {
            Require(vehicle.Host.TryGetActiveEpoch(out ulong epoch),
                vehicle.Label + " epoch is unavailable.");
            Require(vehicle.Profile.TryBuildProfile(
                    out CoordinateTransformProfile profile,
                    out string profileError),
                vehicle.Label + " Profile failed: " + profileError);
            double targetTimestamp = vehicle.Host.GetTargetSourceTimestamp(
                now,
                vehicle.Configuration.RenderDelaySeconds);
            var request = new RenderSampleRequest(
                vehicle.Configuration.SourceId,
                epoch,
                vehicle.Configuration.VehicleId,
                targetTimestamp,
                now,
                vehicle.Host.SourceStatus,
                profile,
                vehicle.Configuration.BuildSamplingPolicy());
            RenderPoseSample sample =
                VehicleRenderSampler.Sample(vehicle.Host.Store, request);
            Require(sample.Succeeded,
                vehicle.Label + " independent expected sample failed: " + sample.Message);
            Require(UnityPoseAdapter.TryConvert(
                    sample.Position,
                    sample.Orientation,
                    out Vector3 expectedPosition,
                    out Quaternion expectedRotation),
                vehicle.Label + " expected sample adapter failed.");
            Require(vehicle.Driver.TrySampleAndApply(now),
                vehicle.Label + " Driver could not apply its own sample.");
            Require(vehicle.Driver.LastAppliedSourceEpoch == epoch,
                vehicle.Label + " Driver did not apply its own Source epoch.");
            if (vehicle.Driver.HasPoseConstraint)
            {
                Require(vehicle.Driver.LastPoseConstraintDecision ==
                        UnityPoseConstraintDecision.Apply,
                    vehicle.Label + " pose constraint did not accept its own sample.");
                Require(vehicle.Driver.TryGetLastAcceptedRoutePose(
                        out var acceptedPose, out ulong acceptedEpoch) &&
                        acceptedEpoch == epoch,
                    vehicle.Label +
                    " Driver did not publish the constrained accepted pose for its own epoch.");
                expectedPosition = new Vector3(
                    (float)(acceptedPose.Position.X * profile.PositionScale),
                    (float)(acceptedPose.Position.Y * profile.PositionScale),
                    (float)(acceptedPose.Position.Z * profile.PositionScale));
                var acceptedRotation = new Quaternion(
                    (float)acceptedPose.Orientation.X,
                    (float)acceptedPose.Orientation.Y,
                    (float)acceptedPose.Orientation.Z,
                    (float)acceptedPose.Orientation.W);
                var modelAlignment = new Quaternion(
                    (float)profile.ModelAlignment.X,
                    (float)profile.ModelAlignment.Y,
                    (float)profile.ModelAlignment.Z,
                    (float)profile.ModelAlignment.W);
                expectedRotation = acceptedRotation * modelAlignment;
            }
            Require(Near(vehicle.Root.position, expectedPosition, 1e-5f) &&
                    Near(vehicle.Root.rotation, expectedRotation, 1e-5f),
                vehicle.Label +
                (vehicle.Driver.HasPoseConstraint
                    ? " root does not match its constrained accepted pose."
                    : " root does not match its own Source/Store sample."));
        }

        private static void StartAuthorityCombination(int mask, double now)
        {
            matrixMask = mask;
            SetAuthorityMask(mask);
            phasePoses = CapturePoses();
            Advance(Phase.ObserveAuthorityCombination, now);
        }

        private static void SetAuthorityMask(int mask)
        {
            for (int index = 0; index < vehicles.Length; index++)
            {
                vehicles[index].Authority.Mode = (mask & (1 << index)) != 0
                    ? VehiclePoseControlMode.PublicData
                    : VehiclePoseControlMode.Demo;
            }
        }

        private static void ValidateAuthorityCombination(int mask)
        {
            for (int index = 0; index < vehicles.Length; index++)
            {
                VehicleRuntime vehicle = vehicles[index];
                bool expectsPublic = (mask & (1 << index)) != 0;
                bool demoDrives = DemoDrives(index);
                VehicleRuntimeSourceMode expectedSource = expectsPublic
                    ? VehicleRuntimeSourceMode.RouteFollowing
                    : VehicleRuntimeSourceMode.LocalDiagnostic;
                Require(vehicle.Authority.PublicDataOwnsControl == expectsPublic &&
                        vehicle.Authority.DemoOwnsControl == !expectsPublic &&
                        vehicle.Driver.OwnsControl &&
                        vehicle.Host.SourceMode == expectedSource &&
                        vehicle.Driver.LastAppliedSourceEpoch == CurrentEpoch(vehicle) &&
                        !demoDrives,
                    "Authority combination " + mask + " is incorrect for " + vehicle.Label + ".");
                Require(vehicle.Driver.LastFailureReason == RenderSampleFailureReason.None,
                    vehicle.Label + " Driver is unhealthy in authority combination " + mask + ".");
                Require(PoseChanged(phasePoses[index], vehicle.Root),
                    vehicle.Label + " stopped moving in authority combination " + mask + ".");
                Require(vehicle.Root.position.sqrMagnitude > 1e-4f,
                    vehicle.Label + " was reset to the world origin during authority switching.");
            }
            ValidateProtectedLocals();
        }

        private static bool DemoDrives(int index)
        {
            switch (index)
            {
                case 0:
                    return demo.DrivesAuv;
                case 1:
                    return demo.DrivesRov;
                case 2:
                    return demo.DrivesUsv;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        private static void StartSourceIsolation(double now)
        {
            Require(AllHealthy(), "Source isolation did not start from all-healthy.");
            phasePoses = CapturePoses();
            phaseEpochs = CaptureEpochs();
            vehicles[isolationIndex].Host.StopSource();
            Advance(Phase.WaitSourceHoldCapture, now);
        }

        private static void ValidateSourceStaleIsolation()
        {
            VehicleRuntime target = vehicles[isolationIndex];
            Require(target.Host.SourceStatus == DataSourceStatus.Stopped &&
                    target.Driver.LastFailureReason == RenderSampleFailureReason.Stale &&
                    Near(target.Root.position, staleHoldPose.Position, 1e-5f) &&
                    Near(target.Root.rotation, staleHoldPose.Rotation, 1e-5f),
                target.Label + " did not independently enter stale hold.");
            for (int index = 0; index < vehicles.Length; index++)
            {
                if (index == isolationIndex)
                {
                    continue;
                }
                VehicleRuntime sibling = vehicles[index];
                Require(Healthy(sibling) &&
                        PoseChanged(phasePoses[index], sibling.Root) &&
                        CurrentEpoch(sibling) == phaseEpochs[index],
                    sibling.Label + " was affected by " + target.Label + " Source stop.");
            }
        }

        private static void ValidateSourceRecoveryIsolation()
        {
            VehicleRuntime target = vehicles[isolationIndex];
            ulong recoveredEpoch = CurrentEpoch(target);
            Require(Healthy(target) && recoveredEpoch != phaseEpochs[isolationIndex],
                target.Label + " did not recover into a new epoch.");
            target.LastEpoch = recoveredEpoch;
            for (int index = 0; index < vehicles.Length; index++)
            {
                if (index == isolationIndex)
                {
                    continue;
                }
                Require(Healthy(vehicles[index]) &&
                        CurrentEpoch(vehicles[index]) == phaseEpochs[index],
                    vehicles[index].Label + " epoch changed during " +
                    target.Label + " restart.");
            }
        }

        private static void StartDriverDisableIsolation(double now)
        {
            Require(AllHealthy(), "Driver isolation did not start from all-healthy.");
            phasePoses = CapturePoses();
            phaseEpochs = CaptureEpochs();
            vehicles[isolationIndex].Driver.enabled = false;
            Advance(Phase.WaitDriverDisabled, now);
        }

        private static void ValidateDriverDisabledIsolation()
        {
            VehicleRuntime target = vehicles[isolationIndex];
            Require(!target.Driver.enabled &&
                    !target.Driver.OwnsControl &&
                    target.Authority.PublicDataOwnsControl &&
                    !DemoDrives(isolationIndex) &&
                    !PoseChanged(phasePoses[isolationIndex], target.Root),
                target.Label + " root changed while its public Driver was disabled.");
            for (int index = 0; index < vehicles.Length; index++)
            {
                Require(CurrentEpoch(vehicles[index]) == phaseEpochs[index],
                    vehicles[index].Label + " epoch changed during Driver Disable.");
                if (index == isolationIndex)
                {
                    continue;
                }
                Require(Healthy(vehicles[index]) &&
                        PoseChanged(phasePoses[index], vehicles[index].Root),
                    vehicles[index].Label + " was affected by " +
                    target.Label + " Driver Disable.");
            }
        }

        private static void ValidateDriverRecoveryIsolation()
        {
            VehicleRuntime target = vehicles[isolationIndex];
            Require(Healthy(target) &&
                    PoseChanged(phasePoses[isolationIndex], target.Root),
                target.Label + " Driver did not resume its own root.");
            for (int index = 0; index < vehicles.Length; index++)
            {
                Require(vehicles[index].Authority.PublicDataOwnsControl &&
                        CurrentEpoch(vehicles[index]) == phaseEpochs[index],
                    vehicles[index].Label +
                    " Authority or epoch changed during Driver recovery.");
            }
        }

        private static void ValidateFinal()
        {
            Require(errorCount == 0,
                "Captured " + errorCount + " Error/Exception/Assert messages.");
            Require(matrixPasses == 8,
                "Authority matrix did not cover all eight combinations.");
            Require(AllHealthy(), "Not all Drivers finished healthy.");
            foreach (VehicleRuntime vehicle in vehicles)
            {
                Require(vehicle.Spinners.All(item => item.enabled),
                    vehicle.Label + " has a disabled spinner.");
                if (!string.Equals(vehicle.Label, "USV", StringComparison.Ordinal))
                {
                    Require(vehicle.SpinnerRotationObserved.All(value => value),
                        vehicle.Label +
                        " did not preserve every spinner local animation.");
                }
            }
            ValidateProtectedLocals();
        }

        private static bool AllHealthy()
        {
            return vehicles != null && vehicles.All(Healthy);
        }

        private static bool Healthy(VehicleRuntime vehicle)
        {
            return vehicle.Driver.enabled &&
                   vehicle.Driver.OwnsControl &&
                   vehicle.Host.SourceStatus == DataSourceStatus.Running &&
                   vehicle.Driver.LastAppliedSourceEpoch == CurrentEpoch(vehicle) &&
                   vehicle.Driver.LastFailureReason == RenderSampleFailureReason.None &&
                   (vehicle.Driver.LastSampleMode == RenderSampleMode.Exact ||
                    vehicle.Driver.LastSampleMode == RenderSampleMode.Interpolated ||
                    vehicle.Driver.LastSampleMode == RenderSampleMode.HeldLatest);
        }

        private static PoseSnapshot[] CapturePoses()
        {
            return vehicles.Select(item => PoseSnapshot.Capture(item.Root)).ToArray();
        }

        private static ulong[] CaptureEpochs()
        {
            return vehicles.Select(CurrentEpoch).ToArray();
        }

        private static ulong CurrentEpoch(VehicleRuntime vehicle)
        {
            Require(vehicle.Host.TryGetActiveEpoch(out ulong epoch),
                vehicle.Label + " active epoch is unavailable.");
            return epoch;
        }

        private static void ObserveSpinnerRotations()
        {
            if (vehicles == null)
            {
                return;
            }
            foreach (VehicleRuntime vehicle in vehicles)
            {
                if (vehicle.SpinnerInitialRotations == null)
                {
                    continue;
                }
                for (int index = 0; index < vehicle.Spinners.Length; index++)
                {
                    if (!Near(
                            vehicle.Spinners[index].transform.localRotation,
                            vehicle.SpinnerInitialRotations[index],
                            1e-4f))
                    {
                        vehicle.SpinnerRotationObserved[index] = true;
                    }
                }
            }
        }

        private static void ValidateProtectedLocals()
        {
            foreach (VehicleRuntime vehicle in vehicles)
            {
                Require(vehicle.ModelLocal.Matches(vehicle.Model) &&
                        Near(vehicle.Root.localScale, vehicle.RootScale, 1e-6f),
                    vehicle.Label + " model local Transform or root scale changed.");
                for (int index = 0; index < vehicle.Spinners.Length; index++)
                {
                    Transform spinner = vehicle.Spinners[index].transform;
                    LocalSnapshot initial = vehicle.SpinnerLocals[index];
                    Require(Near(spinner.localPosition, initial.Position, 1e-6f) &&
                            Near(spinner.localScale, initial.Scale, 1e-6f),
                        vehicle.Label +
                        " spinner local position or scale was overwritten.");
                }
            }
            Require(usvRudderLocal.Matches(usvRudder),
                "USV main rudder local Transform changed.");
        }

        private static bool PoseChanged(PoseSnapshot before, Transform after)
        {
            return !Near(before.Position, after.position, 1e-5f) ||
                   !Near(before.Rotation, after.rotation, 1e-5f);
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
            string directory = EvidenceDirectory();
            Directory.CreateDirectory(directory);
            string status = passed
                ? "N6E_THREE_VEHICLE_PLAY_MODE_VALIDATION_PASS"
                : "N6E_THREE_VEHICLE_PLAY_MODE_VALIDATION_FAIL";
            string generated = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
            string json =
                "{\n" +
                "  \"status\": \"" + status + "\",\n" +
                "  \"generatedAtIso8601\": \"" + Escape(generated) + "\",\n" +
                "  \"passed\": " + (passed ? "true" : "false") + ",\n" +
                "  \"authorityCombinations\": " + matrixPasses + ",\n" +
                "  \"detail\": \"" + Escape(detail) + "\"\n" +
                "}\n";
            File.WriteAllText(
                Path.Combine(directory, "n6e_three_vehicle_playmode_report.json"),
                json,
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(directory, "n6e_three_vehicle_playmode_report.md"),
                "# N6-E Three-Vehicle Play Mode Coexistence Verification\n\n" +
                "- Status: `" + status + "`\n" +
                "- Authority combinations: `" + matrixPasses + "/8`\n" +
                "- Detail: " + detail + "\n",
                new UTF8Encoding(false));

            bool batch = SessionState.GetBool(BatchKey, false);
            SessionState.SetBool(ActiveKey, false);
            SessionState.SetBool(BatchKey, false);
            Unsubscribe();
            Debug.Log(status + " | " + detail);
            if (batch)
            {
                EditorApplication.Exit(passed ? 0 : 1);
            }
        }

        private static string EvidenceDirectory()
        {
            string configured = Environment.GetEnvironmentVariable("N6E_EVIDENCE_DIR");
            return !string.IsNullOrWhiteSpace(configured)
                ? Path.GetFullPath(configured)
                : Path.Combine(Path.GetTempPath(), "UnderwaterRobotScene", "N6E_Validation");
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error ||
                type == LogType.Exception ||
                type == LogType.Assert)
            {
                errorCount++;
            }
        }

        private static GameObject RequireGameObject(string name)
        {
            GameObject value = GameObject.Find(name);
            Require(value != null, "Missing GameObject " + name + ".");
            return value;
        }

        private static Transform RequireDescendant(Transform root, string name)
        {
            Transform[] matches = root.GetComponentsInChildren<Transform>(true)
                .Where(item => string.Equals(item.name, name, StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1, "Expected one descendant " + name + ".");
            return matches[0];
        }

        private static bool Near(Vector3 left, Vector3 right, float tolerance)
        {
            return (left - right).sqrMagnitude <= tolerance * tolerance;
        }

        private static bool Near(Quaternion left, Quaternion right, float tolerance)
        {
            return Mathf.Abs(Quaternion.Dot(left.normalized, right.normalized)) >=
                   1f - tolerance;
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
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
