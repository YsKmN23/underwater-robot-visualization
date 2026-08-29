using System;
using System.Linq;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Constraints;
using UnderwaterRobotScene.Visualization.Runtime.Monitoring;
using UnderwaterRobotScene.Visualization.Sampling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    internal static class V3CMonitoringTrendChartsVerifier
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";

        public static void RunBatch()
        {
            try
            {
                Run();
                Debug.Log("V3C_MONITORING_TRENDS_VERIFICATION: PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("V3C_MONITORING_TRENDS_VERIFICATION: FAIL");
                EditorApplication.Exit(1);
            }
        }

        private static void Run()
        {
            VerifyHistorySemantics();
            VerifyPageContract();
        }

        private static void VerifyHistorySemantics()
        {
            var history = new MonitoringTrendHistory(3);
            MonitoringFleetSnapshot first = Fleet(1UL, 1UL, 0.0,
                MonitoringDataHealth.Fresh, true);
            history.Observe(in first);
            Require(history.Auv.Count == 1 && history.Rov.Count == 1 &&
                    history.Usv.Count == 1,
                "All three vehicles must acquire independent history.");

            history.Observe(in first);
            Require(history.Auv.Count == 1,
                "A duplicate SourceEpoch/SequenceNumber was appended.");

            MonitoringFleetSnapshot skipped = Fleet(1UL, 3UL, 0.2,
                MonitoringDataHealth.Fresh, true);
            history.Observe(in skipped);
            Require(history.Auv.Count == 2 &&
                    history.Auv[1].SequenceNumber == 3UL,
                "A skipped sequence was synthesized or rejected.");

            MonitoringFleetSnapshot stale = Fleet(1UL, 3UL, 0.2,
                MonitoringDataHealth.Stale, true);
            history.Observe(in stale);
            Require(history.Auv.Count == 2,
                "Stale history repeated the latest value.");

            MonitoringFleetSnapshot recovery = Fleet(1UL, 4UL, 0.3,
                MonitoringDataHealth.Fresh, true);
            history.Observe(in recovery);
            Require(history.Auv[2].StartsNewSegment,
                "Recovery did not start a new segment.");

            MonitoringFleetSnapshot invalidSpeed = Fleet(1UL, 5UL, 0.4,
                MonitoringDataHealth.Fresh, false);
            history.Observe(in invalidSpeed);
            MonitoringTrendSample invalidSample = history.Auv[2];
            Require(!invalidSample.Has(MonitoringTrendFields.LinearSpeed) &&
                    invalidSample.Has(MonitoringTrendFields.VerticalPositionY),
                "Missing velocity validity did not create a metric gap.");

            MonitoringFleetSnapshot newEpoch = Fleet(2UL, 0UL, 0.0,
                MonitoringDataHealth.Fresh, true);
            history.Observe(in newEpoch);
            Require(history.Auv.Count == 1 && history.Auv.SourceEpoch == 2UL &&
                    history.Auv[0].StartsNewSegment,
                "SourceEpoch transition did not reset active visible history.");

            for (ulong sequence = 1UL; sequence <= 4UL; sequence++)
            {
                MonitoringFleetSnapshot fleet = Fleet(2UL, sequence,
                    sequence * 0.1, MonitoringDataHealth.Fresh, true);
                history.Observe(in fleet);
            }
            Require(history.Auv.Count == history.Auv.Capacity &&
                    history.Auv[0].SequenceNumber == 2UL,
                "Bounded history did not evict oldest samples.");

            MonitoringTrendSample plus179 = Sample(179f, false);
            MonitoringTrendSample minus179 = Sample(-179f, false);
            Require(!MonitoringTrendHistory.ShouldConnectAngles(
                    in plus179, in minus179, MonitoringTrendFields.Heading,
                    179f, -179f),
                "Heading wrap would render a false 358-degree line.");

            int auvCount = history.Auv.Count;
            int rovCount = history.Rov.Count;
            var selectedRov = new MonitoringFleetSnapshot(
                newEpoch.Auv, newEpoch.Rov, newEpoch.Usv,
                VehicleSelectionKind.Rov);
            history.Observe(in selectedRov);
            Require(history.Auv.Count == auvCount && history.Rov.Count == rovCount,
                "Selection changed history acquisition ownership.");
        }

        private static void VerifyPageContract()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject root = new GameObject("V3C_Verifier_Monitoring");
            try
            {
                root.AddComponent<MonitoringSnapshotProvider>();
                MonitoringDashboardPresenter summary =
                    root.AddComponent<MonitoringDashboardPresenter>();
                MonitoringTrendPagePresenter trends =
                    root.AddComponent<MonitoringTrendPagePresenter>();
                trends.EnsurePages();
                Require(summary.MonitorCamera != null &&
                        summary.MonitorCamera.targetDisplay == 1 &&
                        summary.MonitorCamera.cullingMask == (1 << 31),
                    "Trend page is not isolated to Display 2.");
                Require(trends.SummaryPage != null && trends.TrendsPage != null &&
                        trends.ChartCount == 5,
                    "Summary/Trends page structure is incomplete.");
                Require(trends.CurrentPage == MonitoringPageKind.Summary &&
                        trends.SummaryPage.activeSelf && !trends.TrendsPage.activeSelf,
                    "Summary is not the default page.");
                trends.SetPage(MonitoringPageKind.Trends);
                Require(!trends.SummaryPage.activeSelf && trends.TrendsPage.activeSelf,
                    "Page selector did not switch to Trends.");
                Require(trends.GetComponentsInChildren<VehiclePoseDriver>(true).Length == 0,
                    "Trend presentation introduced a vehicle runtime writer.");
                Require(Camera.main != null && Camera.main.targetDisplay == 0 &&
                        Camera.main.enabled && summary.MonitorCamera.enabled,
                    "Display 1 was disabled or rerouted.");
                VerifyWorldLayout(trends.TrendsPage.transform, 1920, 1080);
                VerifyWorldLayout(trends.TrendsPage.transform, 3840, 2160);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void VerifyWorldLayout(Transform page, int width, int height)
        {
            Require(width * 9 == height * 16,
                "Trend page contract requires a 16:9 target.");
            Renderer[] renderers = page.GetComponentsInChildren<Renderer>(true);
            Require(renderers.Length >= 20,
                "Trend page does not contain the expected static visual structure.");
            foreach (Renderer renderer in renderers)
            {
                Vector3 position = renderer.transform.localPosition;
                Require(position.x >= -9.6f && position.x <= 9.6f &&
                        position.y >= -5.35f && position.y <= 5.35f,
                    renderer.name + " leaves the resolution-independent 16:9 page.");
            }
        }

        private static MonitoringFleetSnapshot Fleet(
            ulong epoch,
            ulong sequence,
            double timestamp,
            MonitoringDataHealth health,
            bool validSpeed)
        {
            return new MonitoringFleetSnapshot(
                Snapshot(VehicleType.Auv, "AUV-01", epoch, sequence, timestamp,
                    health, validSpeed),
                Snapshot(VehicleType.Rov, "ROV-01", epoch, sequence, timestamp,
                    health, validSpeed),
                Snapshot(VehicleType.Usv, "USV-01", epoch, sequence, timestamp,
                    health, validSpeed),
                VehicleSelectionKind.Auv);
        }

        private static VehicleMonitorSnapshot Snapshot(
            VehicleType type,
            string id,
            ulong epoch,
            ulong sequence,
            double timestamp,
            MonitoringDataHealth health,
            bool validSpeed)
        {
            return new VehicleMonitorSnapshot(
                type, id, health,
                VehicleRuntimeSourceMode.RouteFollowing,
                DataSourceStatus.Running,
                true, 0.01,
                true, new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f),
                validSpeed, validSpeed ? 0.0 : 0.0,
                true, timestamp,
                RenderSampleMode.Exact,
                sequence, epoch, "route-source",
                WorldFrame.UnityWorld, BodyFrame.UnityBody,
                true, VehicleRouteExecutionState.Running,
                "route", 1UL, 1UL, 3,
                1.0, 10.0, 0.1, 1.0,
                type != VehicleType.Usv,
                UnityPoseConstraintDecision.Apply,
                string.Empty, RouteSafetyFailureDiagnostic.None,
                "Route Running");
        }

        private static MonitoringTrendSample Sample(
            float heading,
            bool startsNewSegment)
        {
            return new MonitoringTrendSample(
                0.0, 1UL, 1UL,
                MonitoringTrendFields.Heading,
                0f, heading, 0f, 0f, 0.0,
                startsNewSegment);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
