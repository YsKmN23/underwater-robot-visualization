using System;
using System.IO;
using System.Linq;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Monitoring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    internal static class V3BMonitoringDashboardVerifier
    {
        private const string ScenePath =
            "Assets/Scenes/UnderwaterRobotDemo.unity";

        public static void RunBatch()
        {
            try
            {
                Run();
                Debug.Log("V3B_MONITORING_VERIFICATION: PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("V3B_MONITORING_VERIFICATION: FAIL");
                EditorApplication.Exit(1);
            }
        }

        private static void Run()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            VehicleDataRuntimeHost[] hosts = UnityEngine.Object
                .FindObjectsByType<VehicleDataRuntimeHost>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            VehiclePoseDriver[] drivers = UnityEngine.Object
                .FindObjectsByType<VehiclePoseDriver>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            Require(hosts.Length == 3, "Expected exactly three runtime hosts.");
            Require(drivers.Length == 3, "Expected exactly three pose drivers.");
            Require(MonitoringDashboardPresenter.ShouldBootstrapForScene(
                    "UnderwaterRobotDemo"),
                "Formal Scene bootstrap identity was rejected.");
            Require(!MonitoringDashboardPresenter.ShouldBootstrapForScene(
                    "UnrelatedTestScene"),
                "Unrelated Scene would receive the monitoring dashboard.");

            double now = Time.realtimeSinceStartupAsDouble;
            foreach (VehicleDataRuntimeHost host in hosts)
            {
                Require(host.SourceMode == VehicleRuntimeSourceMode.RouteFollowing,
                    host.VehicleId + " is not using RouteFollowing.");
                host.InitializeForDiagnostics(now);
                host.TickForDiagnostics(now);
            }

            foreach (VehiclePoseDriver driver in drivers)
            {
                Require(driver.TrySampleAndApply(now),
                    driver.VehicleId + " could not apply its first pose.");
                Require(driver.TryGetLastAppliedPose(
                        out _, out _, out ulong epoch) && epoch > 0UL,
                    driver.VehicleId + " has no read-only final applied pose.");
            }

            GameObject root = new GameObject("V3B_Verifier_ReadModel");
            try
            {
                MonitoringSnapshotProvider provider =
                    root.AddComponent<MonitoringSnapshotProvider>();
                MonitoringDashboardPresenter presenter =
                    root.AddComponent<MonitoringDashboardPresenter>();
                MonitoringFleetSnapshot fleet = provider.Capture();
                presenter.RefreshNow();
                Require(presenter.MonitorCamera != null &&
                        presenter.MonitorCamera.targetDisplay == 1,
                    "Monitoring camera is not routed to Display 2.");
                Require(presenter.MonitorCamera.cullingMask == (1 << 31),
                    "Monitoring camera can render non-monitor Scene layers.");
                Require((Camera.main.cullingMask & (1 << 31)) == 0,
                    "Main 3D camera still renders the monitoring layer.");
                Require(presenter.RefreshInterval == 0.2f,
                    "Monitoring refresh is no longer 5 Hz.");
                Require(presenter.GetComponentsInChildren<VehiclePoseDriver>(true).Length == 0,
                    "Monitoring page contains vehicle runtime geometry.");
                Require(presenter.GetComponentsInChildren<TextMesh>(true).Length >= 14,
                    "Monitoring page presentation is incomplete.");
                Require(presenter.GetComponentsInChildren<MeshRenderer>(true).Length >= 20,
                    "Monitoring dashboard panel/card structure is incomplete.");
                Require(Camera.main != null && Camera.main.enabled &&
                        presenter.MonitorCamera.enabled &&
                        Camera.main.targetDisplay == 0,
                    "Display 1 and Display 2 cameras are not simultaneously active.");
                Require(presenter.SecondPhysicalDisplayAvailable ==
                        (Display.displays.Length > 1),
                    "Second-display availability guard is inconsistent.");
                Require(Math.Abs(presenter.MonitorCamera.orthographicSize - 5.4f) < 1e-6f &&
                        presenter.MonitorCamera.rect == new Rect(0f, 0f, 1f, 1f),
                    "Monitor camera no longer uses the resolution-independent 16:9 page contract.");
                VerifyStatusColors();
                VerifyRouteEditorLayouts();
                VerifyVehicle(fleet.Auv, VehicleType.Auv, "AUV-01");
                VerifyVehicle(fleet.Rov, VehicleType.Rov, "ROV-01");
                VerifyVehicle(fleet.Usv, VehicleType.Usv, "USV-01");
                Require(fleet.Auv.HasSafetyConstraint,
                    "AUV safety constraint was not exposed.");
                Require(fleet.Rov.HasSafetyConstraint,
                    "ROV safety constraint was not exposed.");
                Require(fleet.Auv.SafetyReason != null &&
                        fleet.Rov.SafetyReason != null,
                    "AUV/ROV safety decision reason was not readable.");
                Require(!fleet.Usv.HasSafetyConstraint,
                    "USV must not expose fabricated business safety.");
                Require(fleet.Auv.VehicleId != fleet.Rov.VehicleId &&
                        fleet.Auv.VehicleId != fleet.Usv.VehicleId &&
                        fleet.Rov.VehicleId != fleet.Usv.VehicleId,
                    "Vehicle snapshots crossed identities.");

                VerifySelection(provider, drivers);
                presenter.RefreshNow();
                RenderMonitorPreview(presenter.MonitorCamera, 1920, 1080,
                    "V3E1_Monitor_1920x1080.png");
                VerifyVelocityValidity();
                VerifyPositionUnits(hosts);
                VerifyLegacyStatusRetirement();

                VehicleDataRuntimeHost auv = hosts.Single(value =>
                    value.IntegrationConfiguration.VehicleType == VehicleType.Auv);
                VehiclePoseDriver auvDriver = drivers.Single(value =>
                    value.IntegrationConfiguration.VehicleType == VehicleType.Auv);
                VerifyTimeEpochAndSequence(auv, auvDriver);

                Require(auv.RestartRoute(), "AUV route did not restart to Running.");
                Require(provider.Capture().Auv.RouteState ==
                        VehicleRouteExecutionState.Running,
                    "Running route state did not reach the read model.");
                Require(auv.PauseRoute(), "AUV route did not enter Paused.");
                Require(provider.Capture().Auv.RouteState ==
                        VehicleRouteExecutionState.Paused,
                    "Paused route state did not reach the read model.");
                Require(auv.ResumeRoute(), "AUV route did not resume.");
                Require(auv.TryGetActiveEpoch(out ulong holdEpoch),
                    "AUV Hold precondition has no active epoch.");
                Require(auvDriver.TryGetLastAppliedPose(
                        out Vector3 holdPosition,
                        out Quaternion holdRotation,
                        out _) &&
                        auv.NotifyConstraintHold(
                            holdEpoch, holdPosition, holdRotation,
                            Time.realtimeSinceStartupAsDouble),
                    "AUV did not enter Hold through the formal constraint entry.");
                Require(provider.Capture().Auv.RouteState ==
                        VehicleRouteExecutionState.Hold,
                    "Hold route state did not reach the read model.");
                Require(auv.RestartRoute(),
                    "AUV route did not restart after Hold.");
                Require(auv.CompleteRoute(), "AUV route did not complete.");
                Require(provider.Capture().Auv.RouteState ==
                        VehicleRouteExecutionState.Completed,
                    "Completed route state did not reach the read model.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                foreach (VehicleDataRuntimeHost host in hosts)
                    host.ShutdownForDiagnostics();
            }

        }

        private static void VerifyStatusColors()
        {
            Color fresh = MonitoringDashboardPresenter.HealthStatusColor(
                MonitoringDataHealth.Fresh);
            Color stale = MonitoringDashboardPresenter.HealthStatusColor(
                MonitoringDataHealth.Stale);
            Color invalid = MonitoringDashboardPresenter.HealthStatusColor(
                MonitoringDataHealth.Invalid);
            Require(fresh != stale && stale != invalid && fresh != invalid,
                "Fresh, Stale, and Invalid do not have distinct status colors.");

            Color running = MonitoringDashboardPresenter.RouteColor(
                VehicleRouteExecutionState.Running);
            Color paused = MonitoringDashboardPresenter.RouteColor(
                VehicleRouteExecutionState.Paused);
            Color hold = MonitoringDashboardPresenter.RouteColor(
                VehicleRouteExecutionState.Hold);
            Color completed = MonitoringDashboardPresenter.RouteColor(
                VehicleRouteExecutionState.Completed);
            Require(running != paused && paused != hold && hold != completed,
                "Running, Paused, Hold, and Completed colors are not distinct.");
        }

        private static void VerifyRouteEditorLayouts()
        {
            VerifyRouteEditorLayout(1920, 1080, 1f, 640f, 46f, 24f);
            VerifyRouteEditorLayout(1280, 720, 0.72f, 460.8f, 33.12f, 17.28f);
            VerifyRouteEditorLayout(1366, 768, 0.72f, 460.8f, 33.12f, 17.28f);
            VerifyRouteEditorLayout(3840, 2160, 2f, 1280f, 92f, 48f);
        }

        private static void VerifyRouteEditorLayout(
            int width,
            int height,
            float expectedScale,
            float expectedPanelWidth,
            float expectedButtonHeight,
            float expectedMargin)
        {
            RouteEditorPanelLayout layout = RouteEditorPanelLayout.Calculate(
                width, height, true, 60f * expectedScale, 72f * expectedScale);
            Require(Math.Abs(layout.UiScale - expectedScale) < 0.001f,
                width + "x" + height + " Route Editor scale mismatch.");
            Require(Math.Abs(layout.PanelRect.width - expectedPanelWidth) < 0.1f,
                width + "x" + height + " Route Editor width mismatch.");
            Require(Math.Abs(layout.ApplyRect.height - expectedButtonHeight) < 0.1f,
                width + "x" + height + " Route Editor button height mismatch.");
            Require(Math.Abs(layout.PanelRect.x - expectedMargin) < 0.1f &&
                    Math.Abs(layout.PanelRect.y - expectedMargin) < 0.1f,
                width + "x" + height + " Route Editor is not top-left anchored.");
            Require(layout.PanelRect.xMin >= 0f && layout.PanelRect.yMin >= 0f &&
                    layout.PanelRect.xMax <= width && layout.PanelRect.yMax <= height,
                width + "x" + height + " Route Editor leaves the screen.");
            Require(layout.ApplyRect.xMax < layout.DeleteRect.xMin &&
                    layout.DeleteRect.xMax < layout.ClearRect.xMin &&
                    layout.ClearRect.xMax < layout.CancelRect.xMin,
                width + "x" + height + " edit buttons overlap.");
            Require(layout.PauseRect.yMax < layout.FeedbackRect.yMin &&
                    layout.FeedbackRect.yMax < layout.LastOutcomeRect.yMin &&
                    layout.LastOutcomeRect.yMax <= layout.PanelRect.height,
                width + "x" + height + " Route Editor vertical regions overlap.");
        }

        private static void RenderMonitorPreview(
            Camera camera,
            int width,
            int height,
            string fileName)
        {
            RenderTexture target = new RenderTexture(width, height, 24);
            Texture2D capture = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                capture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                capture.Apply();
                string path = Path.GetFullPath(Path.Combine(
                    Application.dataPath, "..", fileName));
                File.WriteAllBytes(path, capture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(capture);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static void VerifyLegacyStatusRetirement()
        {
            VehicleStatusPanelPresenter legacy = UnityEngine.Object
                .FindFirstObjectByType<VehicleStatusPanelPresenter>(
                    FindObjectsInactive.Include);
            Require(legacy != null && legacy.gameObject.activeSelf,
                "Formal legacy VEHICLE STATUS source was not found active.");
            Require(MonitoringDashboardPresenter.RetireLegacyStatusDisplay() == 1,
                "Legacy VEHICLE STATUS retirement count was not exactly one.");
            Require(!legacy.gameObject.activeSelf,
                "Legacy VEHICLE STATUS remained active after retirement.");
        }

        private static void VerifySelection(
            MonitoringSnapshotProvider provider,
            VehiclePoseDriver[] drivers)
        {
            VehicleSelectionCameraController selection = provider.Selection;
            Require(selection != null, "Existing selection controller was not found.");
            Camera camera = Camera.main;
            Require(camera != null, "Formal Scene main camera was not found.");
            selection.Initialize(
                camera,
                drivers.Single(value => value.IntegrationConfiguration.VehicleType ==
                    VehicleType.Auv).TargetRoot,
                drivers.Single(value => value.IntegrationConfiguration.VehicleType ==
                    VehicleType.Rov).TargetRoot,
                drivers.Single(value => value.IntegrationConfiguration.VehicleType ==
                    VehicleType.Usv).TargetRoot);

            VehicleSelectionKind[] kinds =
            {
                VehicleSelectionKind.Auv,
                VehicleSelectionKind.Rov,
                VehicleSelectionKind.Usv
            };
            foreach (VehicleSelectionKind kind in kinds)
            {
                provider.SelectVehicle(kind);
                MonitoringFleetSnapshot selectedFleet = provider.Capture();
                Require(selectedFleet.SelectedVehicle == kind,
                    kind + " selection did not reach MonitoringFleetSnapshot.");
                Require(selectedFleet.TryGetSelected(out VehicleMonitorSnapshot selected),
                    kind + " selection could not be resolved.");
                Require(selected.VehicleType == ToVehicleType(kind),
                    kind + " selection resolved another vehicle snapshot.");
            }
        }

        private static VehicleType ToVehicleType(VehicleSelectionKind kind)
        {
            switch (kind)
            {
                case VehicleSelectionKind.Auv: return VehicleType.Auv;
                case VehicleSelectionKind.Rov: return VehicleType.Rov;
                case VehicleSelectionKind.Usv: return VehicleType.Usv;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static void VerifyVelocityValidity()
        {
            Vector3d zero = new Vector3d(0.0, 0.0, 0.0);
            Require(VehicleMonitoringSnapshotBuilder.HasValidLinearVelocity(
                    VehicleStateFields.LinearVelocity, zero),
                "A valid zero LinearVelocity was rejected.");
            Require(!VehicleMonitoringSnapshotBuilder.HasValidLinearVelocity(
                    VehicleStateFields.Position | VehicleStateFields.Orientation, zero),
                "A zero value without LinearVelocity validity was treated as speed.");
        }

        private static void VerifyPositionUnits(VehicleDataRuntimeHost[] hosts)
        {
            foreach (VehicleDataRuntimeHost host in hosts)
            {
                Require(Math.Abs(host.ProfileConfiguration.PositionScale - 1f) < 1e-6f,
                    host.VehicleId + " PositionScale is not 1; dashboard metre label is invalid.");
            }
        }

        private static void VerifyTimeEpochAndSequence(
            VehicleDataRuntimeHost host,
            VehiclePoseDriver driver)
        {
            double start = Time.realtimeSinceStartupAsDouble;
            VehicleMonitorSnapshot fresh = CaptureAt(host, driver, start);
            Require(fresh.Health == MonitoringDataHealth.Fresh,
                "Controlled snapshot did not begin Fresh.");
            ulong firstSequence = fresh.SequenceNumber;
            ulong firstEpoch = fresh.SourceEpoch;

            double staleAt = start +
                host.IntegrationConfiguration.StaleTimeoutSeconds + 0.01;
            VehicleMonitorSnapshot stale = CaptureAt(host, driver, staleAt);
            Require(stale.Health == MonitoringDataHealth.Stale,
                "Controlled snapshot did not become Stale.");

            host.TickForDiagnostics(staleAt);
            Require(driver.TrySampleAndApply(staleAt),
                "Recovered source pose was not applied.");
            VehicleMonitorSnapshot recovered = CaptureAt(host, driver, staleAt);
            Require(recovered.Health == MonitoringDataHealth.Fresh,
                "Controlled stale snapshot did not recover to Fresh.");
            Require(recovered.SequenceNumber > firstSequence,
                "SequenceNumber did not advance after a source step.");

            host.RestartSourceForDiagnostics(staleAt + 0.01);
            Require(host.TryGetActiveEpoch(out ulong restartedEpoch) &&
                    restartedEpoch != firstEpoch,
                "Source restart did not expose a new SourceEpoch.");
            VehicleMonitorSnapshot beforeNewApply =
                CaptureAt(host, driver, staleAt + 0.01);
            Require(beforeNewApply.SourceEpoch == restartedEpoch,
                "Monitoring snapshot did not expose the restarted SourceEpoch.");
            Require(!beforeNewApply.HasAppliedPose,
                "Old applied pose survived a SourceEpoch mismatch.");

            host.TickForDiagnostics(staleAt + 0.01);
            Require(driver.TrySampleAndApply(staleAt + 0.01),
                "Restarted epoch pose was not applied.");
            VehicleMonitorSnapshot afterNewApply =
                CaptureAt(host, driver, staleAt + 0.01);
            Require(afterNewApply.SourceEpoch == restartedEpoch &&
                    afterNewApply.HasAppliedPose,
                "New epoch pose did not become the current applied pose.");
        }

        private static VehicleMonitorSnapshot CaptureAt(
            VehicleDataRuntimeHost host,
            VehiclePoseDriver driver,
            double monotonicNow)
        {
            return VehicleMonitoringSnapshotBuilder.Capture(
                host.IntegrationConfiguration.VehicleType,
                host,
                driver,
                driver.ControlAuthority,
                string.Empty,
                monotonicNow);
        }

        private static void VerifyVehicle(
            VehicleMonitorSnapshot value,
            VehicleType expectedType,
            string expectedId)
        {
            Require(value.VehicleType == expectedType,
                expectedType + " snapshot type mismatch.");
            Require(string.Equals(value.VehicleId, expectedId,
                    StringComparison.Ordinal),
                expectedType + " snapshot ID mismatch.");
            Require(value.Health == MonitoringDataHealth.Fresh,
                expectedType + " snapshot is not Fresh.");
            Require(value.HasAppliedPose,
                expectedType + " final applied pose is unavailable.");
            Require(value.HasLinearSpeed,
                expectedType + " authoritative linear velocity is unavailable.");
            Require(value.SourceEpoch > 0UL,
                expectedType + " SourceEpoch is unavailable.");
            Require(value.HasRoute,
                expectedType + " route is unavailable.");
            Require(value.WaypointCount >= 2,
                expectedType + " route waypoints are unavailable.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
