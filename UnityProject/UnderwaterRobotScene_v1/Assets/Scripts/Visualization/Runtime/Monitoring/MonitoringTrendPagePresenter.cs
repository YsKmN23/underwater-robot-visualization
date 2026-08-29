using System;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime.Monitoring
{
    public enum MonitoringPageKind
    {
        Summary = 0,
        Trends = 1
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1300)]
    public sealed class MonitoringTrendPagePresenter : MonoBehaviour
    {
        private const int MonitorLayer = 31;
        private const float RedrawIntervalSeconds = 0.2f;
        private const float VisibleWindowSeconds = 60f;
        private static readonly Color PageBackground = new Color(0.018f, 0.032f, 0.043f);
        private static readonly Color PanelBackground = new Color(0.045f, 0.075f, 0.095f);
        private static readonly Color HeaderBackground = new Color(0.035f, 0.105f, 0.135f);
        private static readonly Color FrameColor = new Color(0.16f, 0.43f, 0.50f);
        private static readonly Color Primary = new Color(0.91f, 0.96f, 0.98f);
        private static readonly Color Secondary = new Color(0.64f, 0.74f, 0.79f);
        private static readonly Color TrendColor = new Color(0.25f, 0.82f, 0.68f);

        private MonitoringSnapshotProvider provider;
        private MonitoringDashboardPresenter summaryPresenter;
        private MonitoringTrendHistory history;
        private GameObject summaryPage;
        private GameObject trendsPage;
        private TextMesh summarySelector;
        private TextMesh trendsSelector;
        private TextMesh trendsTitle;
        private TextMesh selectedText;
        private TextMesh statusText;
        private ChartVisual[] charts;
        private float nextRedrawTime;
        private MonitoringFleetSnapshot latestFleet;

        public MonitoringPageKind CurrentPage { get; private set; } =
            MonitoringPageKind.Summary;
        public MonitoringTrendHistory History => history;
        public GameObject SummaryPage => summaryPage;
        public GameObject TrendsPage => trendsPage;
        public int ChartCount => charts == null ? 0 : charts.Length;

        private void Awake()
        {
            EnsurePages();
        }

        private void Update()
        {
            EnsurePages();
            latestFleet = provider.Capture();
            history.Observe(in latestFleet);
            if (Input.GetKeyDown(KeyCode.Tab))
                SetPage(CurrentPage == MonitoringPageKind.Summary
                    ? MonitoringPageKind.Trends
                    : MonitoringPageKind.Summary);
            if (Time.unscaledTime + 0.0001f >= nextRedrawTime)
            {
                Redraw();
                nextRedrawTime = Time.unscaledTime + RedrawIntervalSeconds;
            }
        }

        public void EnsurePages()
        {
            if (trendsPage != null)
                return;
            provider = GetComponent<MonitoringSnapshotProvider>();
            summaryPresenter = GetComponent<MonitoringDashboardPresenter>();
            if (provider == null || summaryPresenter == null)
                throw new InvalidOperationException(
                    "Trend pages require the existing monitoring provider and presenter.");
            summaryPresenter.EnsureMonitorPage();
            history = new MonitoringTrendHistory();

            summaryPage = new GameObject("SUMMARY_Page");
            summaryPage.layer = MonitorLayer;
            summaryPage.transform.SetParent(transform, false);
            Transform monitorCamera = summaryPresenter.MonitorCamera.transform;
            for (int index = transform.childCount - 1; index >= 0; index--)
            {
                Transform child = transform.GetChild(index);
                if (child != summaryPage.transform && child != monitorCamera)
                    child.SetParent(summaryPage.transform, true);
            }
            summarySelector = CreateText(summaryPage.transform, "Summary_Page_Selector",
                new Vector2(1.0f, 5.02f), 0.030f, Secondary);

            trendsPage = new GameObject("TRENDS_Page");
            trendsPage.layer = MonitorLayer;
            trendsPage.transform.SetParent(transform, false);
            BuildTrendsPage();
            SetPage(MonitoringPageKind.Summary);
        }

        public void SetPage(MonitoringPageKind page)
        {
            CurrentPage = page;
            if (summaryPage != null)
                summaryPage.SetActive(page == MonitoringPageKind.Summary);
            if (trendsPage != null)
                trendsPage.SetActive(page == MonitoringPageKind.Trends);
            string selector = page == MonitoringPageKind.Summary
                ? "TAB: TRENDS"
                : "TAB: SUMMARY";
            if (summarySelector != null) summarySelector.text = selector;
            if (trendsSelector != null) trendsSelector.text = selector;
        }

        public void ObserveForDiagnostics(in MonitoringFleetSnapshot fleet)
        {
            EnsurePages();
            latestFleet = fleet;
            history.Observe(in fleet);
        }

        public void RedrawForDiagnostics()
        {
            EnsurePages();
            Redraw();
        }

        private void BuildTrendsPage()
        {
            Transform page = trendsPage.transform;
            CreatePanel(page, "Trends_Header", new Vector2(0f, 4.85f),
                new Vector2(18.8f, 0.78f), HeaderBackground, 0.8f);
            trendsTitle = CreateText(page, "Trends_Title", new Vector2(-9.1f, 5.08f),
                0.064f, new Color(0.72f, 0.93f, 1f));
            trendsTitle.text = "UNDERWATER ROBOT DATA MONITOR  /  TRENDS";
            selectedText = CreateText(page, "Trends_Selected", new Vector2(-9.1f, 4.35f),
                0.046f, Primary);
            statusText = CreateText(page, "Trends_Status", new Vector2(3.2f, 4.35f),
                0.038f, TrendColor);
            trendsSelector = CreateText(page, "Trends_Page_Selector",
                new Vector2(1.0f, 5.02f), 0.030f, Secondary);
            TextMesh timeAxis = CreateText(page, "Trends_Time_Axis",
                new Vector2(-2.0f, -4.75f), 0.034f, Secondary);
            timeAxis.text = "SOURCE TIME     -60 s  →  NOW";

            charts = new[]
            {
                CreateChart(page, "Vertical_Position_Y", "VERTICAL POSITION Y", "m",
                    new Vector2(-4.65f, 2.45f), new Vector2(9.0f, 2.75f),
                    TrendMetric.VerticalY, MonitoringTrendFields.VerticalPositionY),
                CreateChart(page, "Linear_Speed", "LINEAR SPEED", "m/s",
                    new Vector2(-4.65f, -1.15f), new Vector2(9.0f, 3.75f),
                    TrendMetric.Speed, MonitoringTrendFields.LinearSpeed),
                CreateChart(page, "Rendered_Heading", "RENDERED HEADING", "deg",
                    new Vector2(4.65f, 2.75f), new Vector2(9.0f, 2.15f),
                    TrendMetric.Heading, MonitoringTrendFields.Heading),
                CreateChart(page, "Rendered_Pitch", "RENDERED PITCH", "deg",
                    new Vector2(4.65f, 0.15f), new Vector2(9.0f, 2.15f),
                    TrendMetric.Pitch, MonitoringTrendFields.Pitch),
                CreateChart(page, "Rendered_Roll", "RENDERED ROLL", "deg",
                    new Vector2(4.65f, -2.45f), new Vector2(9.0f, 2.15f),
                    TrendMetric.Roll, MonitoringTrendFields.Roll)
            };
        }

        private ChartVisual CreateChart(
            Transform parent,
            string name,
            string displayName,
            string unit,
            Vector2 center,
            Vector2 size,
            TrendMetric metric,
            MonitoringTrendFields field)
        {
            CreatePanel(parent, name + "_Panel", center, size, PanelBackground, 0.8f);
            CreatePanel(parent, name + "_TopLine",
                new Vector2(center.x, center.y + size.y * 0.5f - 0.12f),
                new Vector2(size.x - 0.25f, 0.025f), FrameColor, 0.42f);
            TextMesh label = CreateText(parent, name + "_Label",
                new Vector2(center.x - size.x * 0.5f + 0.28f,
                    center.y + size.y * 0.5f - 0.23f), 0.038f, Secondary);
            label.text = displayName + "  (" + unit + ")";
            TextMesh value = CreateText(parent, name + "_Value",
                new Vector2(center.x + size.x * 0.5f - 2.25f,
                    center.y + size.y * 0.5f - 0.23f), 0.043f, Primary);
            GameObject meshObject = new GameObject(name + "_Line");
            meshObject.layer = MonitorLayer;
            meshObject.transform.SetParent(parent, false);
            MeshFilter filter = meshObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = meshObject.AddComponent<MeshRenderer>();
            Shader shader = Shader.Find("Unlit/Color");
            renderer.sharedMaterial = new Material(shader) { color = TrendColor };
            Mesh mesh = new Mesh { name = name + "_ReusableMesh" };
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;
            return new ChartVisual(
                metric, field, unit, center, size, value, mesh,
                MonitoringTrendHistory.DefaultCapacityPerVehicle * 2);
        }

        private void Redraw()
        {
            VehicleSelectionKind selected = latestFleet.SelectedVehicle;
            MonitoringTrendSeries series = history.GetSeries(selected);
            if (!latestFleet.TryGetSelected(out VehicleMonitorSnapshot snapshot) || series == null)
            {
                selectedText.text = "SELECTED VEHICLE  —";
                statusText.text = "NO VEHICLE SELECTED";
                for (int index = 0; index < charts.Length; index++)
                    charts[index].Clear("—");
                return;
            }

            selectedText.text = "SELECTED VEHICLE  " + snapshot.VehicleId +
                                "     SOURCE EPOCH  " + snapshot.SourceEpoch;
            statusText.text = snapshot.Health.ToString().ToUpperInvariant() +
                              "     " + series.Count + " SAMPLES     60 s WINDOW";
            for (int index = 0; index < charts.Length; index++)
                charts[index].Render(series);
        }

        private static MeshRenderer CreatePanel(
            Transform parent,
            string objectName,
            Vector2 position,
            Vector2 size,
            Color color,
            float z)
        {
            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panel.name = objectName;
            panel.layer = MonitorLayer;
            panel.transform.SetParent(parent, false);
            panel.transform.localPosition = new Vector3(position.x, position.y, z);
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

        private static TextMesh CreateText(
            Transform parent,
            string objectName,
            Vector2 position,
            float characterSize,
            Color color)
        {
            GameObject textObject = new GameObject(objectName);
            textObject.layer = MonitorLayer;
            textObject.transform.SetParent(parent, false);
            textObject.transform.localPosition = new Vector3(position.x, position.y, 0f);
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

        private enum TrendMetric
        {
            VerticalY,
            Heading,
            Pitch,
            Roll,
            Speed
        }

        private sealed class ChartVisual
        {
            private readonly TrendMetric metric;
            private readonly MonitoringTrendFields field;
            private readonly string unit;
            private readonly Vector2 center;
            private readonly Vector2 size;
            private readonly TextMesh valueText;
            private readonly Mesh mesh;
            private readonly Vector3[] vertices;
            private readonly int[] indices;

            public ChartVisual(
                TrendMetric metric,
                MonitoringTrendFields field,
                string unit,
                Vector2 center,
                Vector2 size,
                TextMesh valueText,
                Mesh mesh,
                int vertexCapacity)
            {
                this.metric = metric;
                this.field = field;
                this.unit = unit;
                this.center = center;
                this.size = size;
                this.valueText = valueText;
                this.mesh = mesh;
                vertices = new Vector3[vertexCapacity];
                indices = new int[vertexCapacity];
            }

            public void Clear(string value)
            {
                mesh.Clear(false);
                valueText.text = value;
            }

            public void Render(MonitoringTrendSeries series)
            {
                int count = series.Count;
                if (count == 0)
                {
                    Clear("—");
                    return;
                }

                double latestTime = series[count - 1].SourceTimestampSeconds;
                double earliestTime = Math.Max(0.0, latestTime - VisibleWindowSeconds);
                float min = float.PositiveInfinity;
                float max = float.NegativeInfinity;
                int first = 0;
                while (first < count && series[first].SourceTimestampSeconds < earliestTime)
                    first++;
                for (int index = first; index < count; index++)
                {
                    MonitoringTrendSample sample = series[index];
                    if (!sample.Has(field)) continue;
                    float value = Value(sample);
                    min = Mathf.Min(min, value);
                    max = Mathf.Max(max, value);
                }
                if (float.IsPositiveInfinity(min))
                {
                    Clear("UNAVAILABLE");
                    return;
                }

                float span = Mathf.Max(0.001f, max - min);
                float padding = Mathf.Max(metric == TrendMetric.Speed ? 0.05f : 1f, span * 0.12f);
                min -= padding;
                max += padding;
                double timeSpan = Math.Max(0.001, latestTime - earliestTime);
                int vertexCount = 0;
                bool havePrevious = false;
                MonitoringTrendSample previous = default;
                float previousValue = 0f;
                Vector3 previousPoint = default;
                float latestValue = 0f;
                for (int index = first; index < count; index++)
                {
                    MonitoringTrendSample current = series[index];
                    if (!current.Has(field))
                    {
                        havePrevious = false;
                        continue;
                    }
                    float currentValue = Value(current);
                    Vector3 currentPoint = Point(current.SourceTimestampSeconds,
                        currentValue, earliestTime, timeSpan, min, max);
                    bool connect = havePrevious &&
                        (!IsAngle(metric) || MonitoringTrendHistory.ShouldConnectAngles(
                            in previous, in current, field, previousValue, currentValue)) &&
                        !current.StartsNewSegment &&
                        current.SourceEpoch == previous.SourceEpoch;
                    if (connect && vertexCount + 2 <= vertices.Length)
                    {
                        vertices[vertexCount] = previousPoint;
                        indices[vertexCount] = vertexCount;
                        vertexCount++;
                        vertices[vertexCount] = currentPoint;
                        indices[vertexCount] = vertexCount;
                        vertexCount++;
                    }
                    previous = current;
                    previousValue = currentValue;
                    previousPoint = currentPoint;
                    latestValue = currentValue;
                    havePrevious = true;
                }

                mesh.Clear(false);
                if (vertexCount > 0)
                {
                    mesh.SetVertices(vertices, 0, vertexCount);
                    mesh.SetIndices(indices, 0, vertexCount,
                        MeshTopology.Lines, 0, false);
                    mesh.RecalculateBounds();
                }
                valueText.text = latestValue.ToString(
                    metric == TrendMetric.Speed ? "0.00" : "0.0") + " " + unit;
            }

            private Vector3 Point(
                double time,
                float value,
                double earliestTime,
                double timeSpan,
                float min,
                float max)
            {
                float left = center.x - size.x * 0.5f + 0.28f;
                float right = center.x + size.x * 0.5f - 0.28f;
                float bottom = center.y - size.y * 0.5f + 0.25f;
                float top = center.y + size.y * 0.5f - 0.62f;
                float x = Mathf.Lerp(left, right,
                    Mathf.Clamp01((float)((time - earliestTime) / timeSpan)));
                float y = Mathf.Lerp(bottom, top, Mathf.InverseLerp(min, max, value));
                return new Vector3(x, y, 0.1f);
            }

            private float Value(in MonitoringTrendSample sample)
            {
                switch (metric)
                {
                    case TrendMetric.VerticalY: return sample.VerticalPositionY;
                    case TrendMetric.Heading: return sample.HeadingDegrees;
                    case TrendMetric.Pitch: return sample.PitchDegrees;
                    case TrendMetric.Roll: return sample.RollDegrees;
                    case TrendMetric.Speed: return (float)sample.LinearSpeedMetersPerSecond;
                    default: return 0f;
                }
            }

            private static bool IsAngle(TrendMetric value)
            {
                return value == TrendMetric.Heading ||
                       value == TrendMetric.Pitch ||
                       value == TrendMetric.Roll;
            }
        }
    }
}
