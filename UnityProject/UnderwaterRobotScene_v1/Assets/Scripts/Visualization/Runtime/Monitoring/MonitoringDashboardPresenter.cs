using System.Globalization;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime.Constraints;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.Visualization.Runtime.Monitoring
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1200)]
    public sealed class MonitoringDashboardPresenter : MonoBehaviour
    {
        private const float RefreshIntervalSeconds = 0.2f;
        private const string FormalSceneName = "UnderwaterRobotDemo";
        private const int MonitorDisplayIndex = 1;
        private const int MonitorLayer = 31;
        private const float MonitorWorldY = 10000f;
        private const float ProgressBarWidth = 8.25f;

        private static readonly Color PageBackground =
            new Color(0.018f, 0.032f, 0.043f);
        private static readonly Color PanelBackground =
            new Color(0.045f, 0.075f, 0.095f);
        private static readonly Color HeaderBackground =
            new Color(0.035f, 0.105f, 0.135f);
        private static readonly Color TextPrimary =
            new Color(0.91f, 0.96f, 0.98f);
        private static readonly Color TextSecondary =
            new Color(0.64f, 0.74f, 0.79f);
        private static readonly Color HealthyColor =
            new Color(0.25f, 0.82f, 0.68f);
        private static readonly Color PausedColor =
            new Color(0.96f, 0.78f, 0.28f);
        private static readonly Color HoldColor =
            new Color(0.96f, 0.52f, 0.20f);
        private static readonly Color FaultColor =
            new Color(0.93f, 0.28f, 0.30f);
        private static readonly Color MutedColor =
            new Color(0.42f, 0.50f, 0.54f);
        private static readonly Color FrameColor =
            new Color(0.16f, 0.43f, 0.50f);

        private MonitoringSnapshotProvider provider;
        private MonitoringFleetSnapshot snapshot;
        private float nextRefreshTime;
        private Camera monitorCamera;
        private TextMesh titleText;
        private TextMesh systemText;
        private TextMesh auvOverviewText;
        private TextMesh rovOverviewText;
        private TextMesh usvOverviewText;
        private TextMesh selectedHeadingText;
        private TextMesh selectedLabelText;
        private TextMesh selectedText;
        private TextMesh routeHeadingText;
        private TextMesh routeText;
        private TextMesh safetyHeadingText;
        private TextMesh safetyText;
        private TextMesh diagnosticText;
        private TextMesh outcomeHeadingText;
        private TextMesh outcomeText;
        private MeshRenderer auvAccent;
        private MeshRenderer rovAccent;
        private MeshRenderer usvAccent;
        private MeshRenderer routeAccent;
        private MeshRenderer safetyAccent;
        private MeshRenderer progressFill;
        private Transform progressFillTransform;
        private MeshRenderer auvHealthBadge;
        private MeshRenderer auvRouteBadge;
        private MeshRenderer rovHealthBadge;
        private MeshRenderer rovRouteBadge;
        private MeshRenderer usvHealthBadge;
        private MeshRenderer usvRouteBadge;

        public Camera MonitorCamera => monitorCamera;
        public int TargetDisplay => MonitorDisplayIndex;
        public float RefreshInterval => RefreshIntervalSeconds;
        public bool SecondPhysicalDisplayAvailable =>
            Display.displays.Length > MonitorDisplayIndex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!ShouldBootstrapForScene(SceneManager.GetActiveScene().name))
                return;
            RetireLegacyStatusDisplay();
            if (FindFirstObjectByType<MonitoringDashboardPresenter>() != null)
                return;
            GameObject root = new GameObject("V3E1_Monitoring_Display_2");
            root.AddComponent<MonitoringSnapshotProvider>();
            root.AddComponent<MonitoringDashboardPresenter>();
            root.AddComponent<MonitoringTrendPagePresenter>();
        }

        public static bool ShouldBootstrapForScene(string sceneName)
        {
            return string.Equals(sceneName, FormalSceneName,
                System.StringComparison.Ordinal);
        }

        public static int RetireLegacyStatusDisplay()
        {
            VehicleStatusPanelPresenter[] legacy =
                FindObjectsByType<VehicleStatusPanelPresenter>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
            int retired = 0;
            for (int index = 0; index < legacy.Length; index++)
            {
                VehicleStatusPanelPresenter presenter = legacy[index];
                if (presenter != null && presenter.gameObject.activeSelf)
                {
                    presenter.gameObject.SetActive(false);
                    retired++;
                }
            }
            return retired;
        }

        private void Awake()
        {
            EnsureMonitorPage();
            RefreshNow();
            if (SecondPhysicalDisplayAvailable)
                Display.displays[MonitorDisplayIndex].Activate();
        }

        private void Update()
        {
            if (Time.unscaledTime + 0.0001f >= nextRefreshTime)
                RefreshNow();
        }

        public void RefreshNow()
        {
            EnsureMonitorPage();
            snapshot = provider.Capture();
            FormatAndApplyDisplay();
            nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;
        }

        public void EnsureMonitorPage()
        {
            if (provider == null)
            {
                provider = GetComponent<MonitoringSnapshotProvider>();
                if (provider == null)
                    provider = gameObject.AddComponent<MonitoringSnapshotProvider>();
            }
            if (monitorCamera != null)
                return;

            gameObject.layer = MonitorLayer;
            transform.position = new Vector3(0f, MonitorWorldY, 0f);
            GameObject cameraObject = new GameObject("Display_2_Monitor_Camera");
            cameraObject.layer = MonitorLayer;
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);
            monitorCamera = cameraObject.AddComponent<Camera>();
            monitorCamera.orthographic = true;
            monitorCamera.orthographicSize = 5.4f;
            monitorCamera.clearFlags = CameraClearFlags.SolidColor;
            monitorCamera.backgroundColor = PageBackground;
            monitorCamera.cullingMask = 1 << MonitorLayer;
            monitorCamera.targetDisplay = MonitorDisplayIndex;
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
                mainCamera.cullingMask &= ~(1 << MonitorLayer);

            CreatePanel("Header_Panel", new Vector2(0f, 4.85f),
                new Vector2(18.8f, 0.78f), HeaderBackground, 0.8f);
            CreatePanel("AUV_Overview_Panel", new Vector2(-6.2f, 3.45f),
                new Vector2(5.85f, 1.72f), PanelBackground, 0.8f);
            CreatePanel("ROV_Overview_Panel", new Vector2(0f, 3.45f),
                new Vector2(5.85f, 1.72f), PanelBackground, 0.8f);
            CreatePanel("USV_Overview_Panel", new Vector2(6.2f, 3.45f),
                new Vector2(5.85f, 1.72f), PanelBackground, 0.8f);
            CreatePanel("Selected_Panel", new Vector2(-4.65f, 0.60f),
                new Vector2(9.1f, 3.75f), PanelBackground, 0.8f);
            CreatePanel("Route_Panel", new Vector2(4.65f, 1.20f),
                new Vector2(9.1f, 2.55f), PanelBackground, 0.8f);
            CreatePanel("Safety_Panel", new Vector2(4.65f, -1.08f),
                new Vector2(9.1f, 1.62f), PanelBackground, 0.8f);
            CreatePanel("Diagnostic_Panel", new Vector2(0f, -2.67f),
                new Vector2(18.8f, 1.18f), PanelBackground, 0.8f);
            CreatePanel("Outcome_Panel", new Vector2(0f, -4.30f),
                new Vector2(18.8f, 1.72f), HeaderBackground, 0.8f);
            CreateCornerFrame("Selected_Frame", new Vector2(-4.65f, 0.60f),
                new Vector2(9.1f, 3.75f));
            CreateCornerFrame("Route_Frame", new Vector2(4.65f, 1.20f),
                new Vector2(9.1f, 2.55f));
            CreateCornerFrame("Safety_Frame", new Vector2(4.65f, -1.08f),
                new Vector2(9.1f, 1.62f));
            CreateCornerFrame("Outcome_Frame", new Vector2(0f, -4.30f),
                new Vector2(18.8f, 1.72f));

            auvAccent = CreatePanel("AUV_Status_Accent", new Vector2(-9.08f, 3.45f),
                new Vector2(0.09f, 1.56f), HealthyColor, 0.45f);
            rovAccent = CreatePanel("ROV_Status_Accent", new Vector2(-2.88f, 3.45f),
                new Vector2(0.09f, 1.56f), HealthyColor, 0.45f);
            usvAccent = CreatePanel("USV_Status_Accent", new Vector2(3.32f, 3.45f),
                new Vector2(0.09f, 1.56f), HealthyColor, 0.45f);
            routeAccent = CreatePanel("Route_Status_Accent", new Vector2(0.18f, 1.20f),
                new Vector2(0.09f, 2.35f), HealthyColor, 0.45f);
            safetyAccent = CreatePanel("Safety_Status_Accent", new Vector2(0.18f, -1.08f),
                new Vector2(0.09f, 1.44f), HealthyColor, 0.45f);
            CreatePanel("Selected_Accent", new Vector2(-9.15f, 0.60f),
                new Vector2(0.05f, 3.45f), FrameColor, 0.44f);
            CreatePanel("Outcome_Accent", new Vector2(-9.32f, -4.30f),
                new Vector2(0.06f, 1.45f), HealthyColor, 0.44f);
            auvHealthBadge = CreatePanel("AUV_Health_Badge", new Vector2(-8.08f, 3.46f),
                new Vector2(1.28f, 0.36f), HeaderBackground, 0.48f);
            auvRouteBadge = CreatePanel("AUV_Route_Badge", new Vector2(-6.47f, 3.46f),
                new Vector2(1.68f, 0.36f), HeaderBackground, 0.48f);
            rovHealthBadge = CreatePanel("ROV_Health_Badge", new Vector2(-1.88f, 3.46f),
                new Vector2(1.28f, 0.36f), HeaderBackground, 0.48f);
            rovRouteBadge = CreatePanel("ROV_Route_Badge", new Vector2(-0.27f, 3.46f),
                new Vector2(1.68f, 0.36f), HeaderBackground, 0.48f);
            usvHealthBadge = CreatePanel("USV_Health_Badge", new Vector2(4.32f, 3.46f),
                new Vector2(1.28f, 0.36f), HeaderBackground, 0.48f);
            usvRouteBadge = CreatePanel("USV_Route_Badge", new Vector2(5.93f, 3.46f),
                new Vector2(1.68f, 0.36f), HeaderBackground, 0.48f);
            CreatePanel("Route_Progress_Background", new Vector2(4.65f, 0.25f),
                new Vector2(ProgressBarWidth, 0.16f), MutedColor, 0.42f);
            progressFill = CreatePanel("Route_Progress_Fill", new Vector2(0.525f, 0.25f),
                new Vector2(0.01f, 0.16f), HealthyColor, 0.38f);
            progressFillTransform = progressFill.transform;
            for (int tick = 0; tick <= 10; tick++)
            {
                CreatePanel("Route_Progress_Tick_" + tick,
                    new Vector2(0.525f + ProgressBarWidth * tick / 10f, 0.25f),
                    new Vector2(0.018f, 0.22f), PageBackground, 0.30f);
            }
            CreatePanel("Header_Technical_Line", new Vector2(0f, 4.48f),
                new Vector2(18.35f, 0.018f), FrameColor, 0.40f);

            titleText = CreateText("Title", new Vector2(-9.1f, 5.08f), 0.064f,
                new Color(0.72f, 0.93f, 1f));
            systemText = CreateText("System_Status", new Vector2(5.55f, 5.02f),
                0.042f, HealthyColor);
            auvOverviewText = CreateText("AUV_Overview", new Vector2(-8.82f, 4.12f),
                0.049f, TextPrimary);
            rovOverviewText = CreateText("ROV_Overview", new Vector2(-2.62f, 4.12f),
                0.049f, TextPrimary);
            usvOverviewText = CreateText("USV_Overview", new Vector2(3.58f, 4.12f),
                0.049f, TextPrimary);
            selectedHeadingText = CreateText("Selected_Heading",
                new Vector2(-8.85f, 2.20f), 0.043f, TextSecondary);
            selectedLabelText = CreateText("Selected_Labels", new Vector2(-8.85f, 1.75f),
                0.032f, TextSecondary);
            selectedText = CreateText("Selected_Values", new Vector2(-6.95f, 1.75f),
                0.050f, TextPrimary);
            routeHeadingText = CreateText("Route_Heading", new Vector2(0.42f, 2.18f),
                0.043f, TextSecondary);
            routeText = CreateText("Route", new Vector2(0.42f, 1.72f), 0.047f,
                TextPrimary);
            safetyHeadingText = CreateText("Safety_Heading", new Vector2(0.42f, -0.52f),
                0.043f, TextSecondary);
            safetyText = CreateText("Safety", new Vector2(0.42f, -0.92f), 0.045f,
                TextPrimary);
            diagnosticText = CreateText("Diagnostic_Strip", new Vector2(-9.1f, -2.35f),
                0.034f, TextSecondary);
            outcomeHeadingText = CreateText("Outcome_Heading", new Vector2(-9.1f, -3.75f),
                0.040f, TextSecondary);
            outcomeText = CreateText("Latest_Outcome", new Vector2(-9.1f, -4.25f),
                0.055f, TextPrimary);
            selectedHeadingText.text = "SELECTED VEHICLE  /  POSE & MOTION";
            selectedLabelText.text = "VEHICLE ID\n\nPOSITION\n\nATTITUDE\n\nLINEAR SPEED";
            routeHeadingText.text = "ROUTE EXECUTION";
            safetyHeadingText.text = "SAFETY";
            outcomeHeadingText.text = "LATEST STATUS / OUTCOME";
        }

        private MeshRenderer CreatePanel(
            string objectName,
            Vector2 localPosition,
            Vector2 size,
            Color color,
            float z)
        {
            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panel.name = objectName;
            panel.layer = MonitorLayer;
            panel.transform.SetParent(transform, false);
            panel.transform.localPosition = new Vector3(localPosition.x, localPosition.y, z);
            panel.transform.localScale = new Vector3(size.x, size.y, 1f);
            Collider collider = panel.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }
            MeshRenderer renderer = panel.GetComponent<MeshRenderer>();
            Shader shader = Shader.Find("Unlit/Color");
            renderer.sharedMaterial = new Material(shader) { color = color };
            return renderer;
        }

        private void CreateCornerFrame(
            string namePrefix,
            Vector2 center,
            Vector2 size)
        {
            float left = center.x - size.x * 0.5f + 0.12f;
            float right = center.x + size.x * 0.5f - 0.12f;
            float top = center.y + size.y * 0.5f - 0.10f;
            float bottom = center.y - size.y * 0.5f + 0.10f;
            CreatePanel(namePrefix + "_TL_H", new Vector2(left + 0.28f, top),
                new Vector2(0.56f, 0.025f), FrameColor, 0.43f);
            CreatePanel(namePrefix + "_TL_V", new Vector2(left, top - 0.20f),
                new Vector2(0.025f, 0.40f), FrameColor, 0.43f);
            CreatePanel(namePrefix + "_BR_H", new Vector2(right - 0.28f, bottom),
                new Vector2(0.56f, 0.025f), FrameColor, 0.43f);
            CreatePanel(namePrefix + "_BR_V", new Vector2(right, bottom + 0.20f),
                new Vector2(0.025f, 0.40f), FrameColor, 0.43f);
        }

        private TextMesh CreateText(
            string objectName,
            Vector2 localPosition,
            float characterSize,
            Color color)
        {
            GameObject textObject = new GameObject(objectName);
            textObject.layer = MonitorLayer;
            textObject.transform.SetParent(transform, false);
            textObject.transform.localPosition =
                new Vector3(localPosition.x, localPosition.y, 0f);
            TextMesh text = textObject.AddComponent<TextMesh>();
            text.anchor = TextAnchor.UpperLeft;
            text.alignment = TextAlignment.Left;
            text.fontSize = 64;
            text.characterSize = characterSize;
            text.color = color;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                text.font = font;
                text.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
            return text;
        }

        private void FormatAndApplyDisplay()
        {
            titleText.text = "UNDERWATER ROBOT DATA MONITOR";
            MonitoringDataHealth systemHealth = WorstHealth(
                snapshot.Auv.Health, snapshot.Rov.Health, snapshot.Usv.Health);
            systemText.text = "● LIVE   /   SYSTEM " +
                (systemHealth == MonitoringDataHealth.Fresh
                    ? "NORMAL"
                    : HealthText(systemHealth));
            systemText.color = HealthStatusColor(systemHealth);
            ApplyOverview(snapshot.Auv, snapshot.SelectedVehicle == VehicleSelectionKind.Auv,
                auvOverviewText, auvAccent, auvHealthBadge, auvRouteBadge);
            ApplyOverview(snapshot.Rov, snapshot.SelectedVehicle == VehicleSelectionKind.Rov,
                rovOverviewText, rovAccent, rovHealthBadge, rovRouteBadge);
            ApplyOverview(snapshot.Usv, snapshot.SelectedVehicle == VehicleSelectionKind.Usv,
                usvOverviewText, usvAccent, usvHealthBadge, usvRouteBadge);

            if (!snapshot.TryGetSelected(out VehicleMonitorSnapshot selected))
            {
                selectedText.text = "NO VEHICLE SELECTED\nSelect AUV, ROV, or USV in Display 1.";
                selectedLabelText.gameObject.SetActive(false);
                routeText.text = "NO ROUTE SELECTED";
                safetyText.text = "NO VEHICLE SELECTED";
                diagnosticText.text = "LOGICAL SOURCE —    DATA AGE —    SEQUENCE —    SOURCE EPOCH —";
                outcomeText.text = "Waiting for vehicle selection.";
                UpdateProgress(0.0, MutedColor);
                return;
            }

            selectedLabelText.gameObject.SetActive(true);
            selectedText.text = FormatSelected(selected);
            routeText.text = FormatRoute(selected);
            safetyText.text = FormatSafety(selected);
            diagnosticText.text = FormatDiagnostic(selected);
            outcomeText.text = selected.LatestOutcome;
            Color routeColor = RouteColor(selected.RouteState);
            routeAccent.sharedMaterial.color = routeColor;
            safetyAccent.sharedMaterial.color = SafetyColor(selected);
            UpdateProgress(selected.HasRoute ? selected.RouteProgress01 : 0.0, routeColor);
        }

        private static void ApplyOverview(
            VehicleMonitorSnapshot value,
            bool selected,
            TextMesh text,
            MeshRenderer accent,
            MeshRenderer healthBadge,
            MeshRenderer routeBadge)
        {
            text.text = (selected ? "● " : string.Empty) + value.VehicleId +
                        "\n" + HealthText(value.Health) + "     " +
                        (value.HasRoute ? value.RouteState.ToString().ToUpperInvariant() :
                            "NO ROUTE") + "\n" +
                        (value.HasLinearSpeed ? D(value.LinearSpeedMetersPerSecond) + " m/s" :
                            "SPEED —") + "     " + AgeText(value);
            Color healthColor = HealthStatusColor(value.Health);
            text.color = selected ? Color.white : TextPrimary;
            accent.sharedMaterial.color = healthColor;
            healthBadge.sharedMaterial.color = Dimmed(healthColor);
            routeBadge.sharedMaterial.color = Dimmed(
                value.HasRoute ? RouteColor(value.RouteState) : MutedColor);
            accent.transform.localScale = new Vector3(
                selected ? 0.16f : 0.09f, 1.56f, 1f);
        }

        private void UpdateProgress(double progress01, Color color)
        {
            float width = ProgressBarWidth * Mathf.Clamp01((float)progress01);
            progressFillTransform.localScale = new Vector3(Mathf.Max(0.01f, width), 0.16f, 1f);
            progressFillTransform.localPosition =
                new Vector3(0.525f + width * 0.5f, 0.25f, 0.38f);
            progressFill.sharedMaterial.color = color;
        }

        private static string FormatSelected(VehicleMonitorSnapshot value)
        {
            Vector3 p = value.AppliedPosition;
            Vector3 r = value.AppliedEulerDegrees;
            return value.VehicleId + "\n" +
                   "\nX " + F(p.x, "0.00") + "   Y " + F(p.y, "0.00") +
                   "   Z " + F(p.z, "0.00") + " m\n\n" +
                   "HDG " + F(r.y, "0.0") + "°   PITCH " +
                   F(r.x, "0.0") + "°   ROLL " + F(r.z, "0.0") + "°\n\n" +
                   (value.HasLinearSpeed
                       ? D(value.LinearSpeedMetersPerSecond) + " m/s"
                       : "UNAVAILABLE");
        }

        private static Color Dimmed(Color color)
        {
            return Color.Lerp(PanelBackground, color, 0.28f);
        }

        private static string FormatRoute(VehicleMonitorSnapshot value)
        {
            if (!value.HasRoute)
                return "UNAVAILABLE\nNo authoritative route.";
            return value.RouteState.ToString().ToUpperInvariant() + "     " +
                   (value.RouteProgress01 * 100.0).ToString(
                       "0.0", CultureInfo.InvariantCulture) + "%\n" +
                   D(value.DistanceAlongRoute) + " / " + D(value.TotalRouteLength) +
                   " m     •     " + value.RouteId + "\n" +
                   value.WaypointCount + " waypoints     •     Cruise " +
                   D(value.CruiseSpeedMetersPerSecond) + " m/s";
        }

        private static string FormatSafety(VehicleMonitorSnapshot value)
        {
            if (value.VehicleType == VehicleType.Usv)
                return "NOT APPLICABLE     Business safety constraint";
            string reason = string.IsNullOrWhiteSpace(value.SafetyReason)
                ? "No active reason"
                : value.SafetyReason;
            string rejection = value.RouteRejection.HasFailure
                ? "     •     REJECTED segment " + value.RouteRejection.SegmentIndex
                : string.Empty;
            return value.SafetyDecision.ToString().ToUpperInvariant() +
                   "     " + reason + rejection;
        }

        private static string FormatDiagnostic(VehicleMonitorSnapshot value)
        {
            return "LOGICAL SOURCE  " + SourceText(value.SourceMode) +
                   "     DATA AGE  " + AgeText(value) +
                   "     SEQUENCE  " + value.SequenceNumber +
                   "     SOURCE EPOCH  " + value.SourceEpoch + "\n" +
                   "SAMPLE  " + value.SampleMode + "     FRAMES  " + value.WorldFrame +
                   " / " + value.BodyFrame + "     ROUTE VERSION / EPOCH  " +
                   value.RouteVersion + " / " + value.RouteEpoch;
        }

        private static MonitoringDataHealth WorstHealth(
            MonitoringDataHealth first,
            MonitoringDataHealth second,
            MonitoringDataHealth third)
        {
            return (MonitoringDataHealth)Mathf.Min(
                (int)first, Mathf.Min((int)second, (int)third));
        }

        public static Color HealthStatusColor(MonitoringDataHealth health)
        {
            switch (health)
            {
                case MonitoringDataHealth.Fresh: return HealthyColor;
                case MonitoringDataHealth.Stale: return HoldColor;
                case MonitoringDataHealth.Invalid: return FaultColor;
                case MonitoringDataHealth.Disabled:
                case MonitoringDataHealth.Unavailable: return MutedColor;
                default: return PausedColor;
            }
        }

        public static Color RouteColor(VehicleRouteExecutionState state)
        {
            switch (state)
            {
                case VehicleRouteExecutionState.Running: return HealthyColor;
                case VehicleRouteExecutionState.Paused: return PausedColor;
                case VehicleRouteExecutionState.Hold: return HoldColor;
                case VehicleRouteExecutionState.Completed: return new Color(0.35f, 0.70f, 0.92f);
                default: return MutedColor;
            }
        }

        private static Color SafetyColor(VehicleMonitorSnapshot value)
        {
            if (!value.HasSafetyConstraint) return MutedColor;
            switch (value.SafetyDecision)
            {
                case UnityPoseConstraintDecision.HoldCurrent: return HoldColor;
                default: return HealthyColor;
            }
        }

        private static string HealthText(MonitoringDataHealth health)
        {
            return health == MonitoringDataHealth.Fresh
                ? "FRESH"
                : health.ToString().ToUpperInvariant();
        }

        private static string SourceText(VehicleRuntimeSourceMode mode)
        {
            return mode == VehicleRuntimeSourceMode.RouteFollowing
                ? "ROUTE_FOLLOWING"
                : "LOCAL_DIAGNOSTIC";
        }

        private static string AgeText(VehicleMonitorSnapshot value)
        {
            return value.HasDataAge
                ? (value.DataAgeSeconds * 1000.0).ToString(
                    "0", CultureInfo.InvariantCulture) + " ms"
                : "—";
        }

        private static string F(float value, string format)
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        private static string D(double value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
