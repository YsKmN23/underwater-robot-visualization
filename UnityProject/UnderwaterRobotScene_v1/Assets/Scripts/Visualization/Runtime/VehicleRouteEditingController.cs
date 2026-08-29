using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime.Constraints;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime
{
    public readonly struct RouteEditorPanelLayout
    {
        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;
        private const float BaseMargin = 24f;
        private const float BasePreferredWidth = 640f;
        private const float BaseMinimumWidth = 420f;
        private const float BaseTitleHeight = 48f;
        private const float BaseRowHeight = 38f;
        private const float BaseButtonHeight = 46f;
        private const float BaseGap = 12f;

        private RouteEditorPanelLayout(
            Rect panelRect,
            Rect contentRect,
            Rect titleRect,
            Rect statusRect,
            Rect holdRect,
            Rect draftRect,
            Rect heightSourceRect,
            Rect helpRect,
            Rect depthRect,
            Rect applyRect,
            Rect deleteRect,
            Rect clearRect,
            Rect cancelRect,
            Rect pauseRect,
            Rect resumeRect,
            Rect restartRect,
            Rect completeRect,
            Rect feedbackRect,
            Rect lastOutcomeRect,
            float uiScale)
        {
            PanelRect = panelRect;
            ContentRect = contentRect;
            TitleRect = titleRect;
            StatusRect = statusRect;
            HoldRect = holdRect;
            DraftRect = draftRect;
            HeightSourceRect = heightSourceRect;
            HelpRect = helpRect;
            DepthRect = depthRect;
            ApplyRect = applyRect;
            DeleteRect = deleteRect;
            ClearRect = clearRect;
            CancelRect = cancelRect;
            PauseRect = pauseRect;
            ResumeRect = resumeRect;
            RestartRect = restartRect;
            CompleteRect = completeRect;
            FeedbackRect = feedbackRect;
            LastOutcomeRect = lastOutcomeRect;
            UiScale = uiScale;
        }

        public Rect PanelRect { get; }
        public Rect ContentRect { get; }
        public Rect TitleRect { get; }
        public Rect StatusRect { get; }
        public Rect HoldRect { get; }
        public Rect DraftRect { get; }
        public Rect HeightSourceRect { get; }
        public Rect HelpRect { get; }
        public Rect DepthRect { get; }
        public Rect ApplyRect { get; }
        public Rect DeleteRect { get; }
        public Rect ClearRect { get; }
        public Rect CancelRect { get; }
        public Rect PauseRect { get; }
        public Rect ResumeRect { get; }
        public Rect RestartRect { get; }
        public Rect CompleteRect { get; }
        public Rect FeedbackRect { get; }
        public Rect LastOutcomeRect { get; }
        public float UiScale { get; }

        public static float CalculateUiScale(int screenWidth, int screenHeight)
        {
            float scale = Mathf.Min(
                screenWidth / ReferenceWidth,
                screenHeight / ReferenceHeight);
            return Mathf.Clamp(scale, 0.72f, 2.0f);
        }

        public static RouteEditorPanelLayout Calculate(
            int screenWidth,
            int screenHeight,
            bool showAuvDepth,
            float measuredFeedbackHeight)
        {
            return Calculate(
                screenWidth,
                screenHeight,
                showAuvDepth,
                measuredFeedbackHeight,
                32f);
        }

        public static RouteEditorPanelLayout Calculate(
            int screenWidth,
            int screenHeight,
            bool showAuvDepth,
            float measuredFeedbackHeight,
            float measuredOutcomeHeight)
        {
            float uiScale = CalculateUiScale(screenWidth, screenHeight);
            float margin = BaseMargin * uiScale;
            float preferredWidth = BasePreferredWidth * uiScale;
            float minimumWidth = BaseMinimumWidth * uiScale;
            float titleHeight = BaseTitleHeight * uiScale;
            float rowHeight = BaseRowHeight * uiScale;
            float buttonHeight = BaseButtonHeight * uiScale;
            float gap = BaseGap * uiScale;
            float availableWidth = Mathf.Max(1f, screenWidth - margin * 2f);
            float panelWidth = Mathf.Min(
                preferredWidth,
                Mathf.Max(minimumWidth, availableWidth));
            panelWidth = Mathf.Min(panelWidth, availableWidth);
            float panelX = margin;
            float contentWidth = Mathf.Max(1f, panelWidth - margin * 2f);
            float y = titleHeight + 8f * uiScale;
            var status = new Rect(margin, y, contentWidth, rowHeight + 4f * uiScale);
            y += status.height + 3f * uiScale;
            var hold = new Rect(margin, y, contentWidth, rowHeight);
            y += rowHeight + 2f * uiScale;
            var draft = new Rect(margin, y, contentWidth, rowHeight);
            y += rowHeight + 6f * uiScale;
            Rect heightSource = Rect.zero;
            if (showAuvDepth)
            {
                heightSource = new Rect(margin, y, contentWidth, 58f * uiScale);
                y += heightSource.height;
            }
            var help = new Rect(margin, y, contentWidth, 64f * uiScale);
            y += help.height;
            Rect depth = Rect.zero;
            if (showAuvDepth)
            {
                depth = new Rect(margin, y, contentWidth, 58f * uiScale);
                y += depth.height;
            }
            y += gap;

            float buttonWidth = (contentWidth - gap * 3f) / 4f;
            Rect apply = Button(margin, y, buttonWidth, buttonHeight);
            Rect delete = Button(margin + (buttonWidth + gap), y, buttonWidth, buttonHeight);
            Rect clear = Button(margin + (buttonWidth + gap) * 2f,
                y, buttonWidth, buttonHeight);
            Rect cancel = Button(margin + (buttonWidth + gap) * 3f,
                y, buttonWidth, buttonHeight);
            y += buttonHeight + gap;
            Rect pause = Button(margin, y, buttonWidth, buttonHeight);
            Rect resume = Button(margin + (buttonWidth + gap), y, buttonWidth, buttonHeight);
            Rect restart = Button(margin + (buttonWidth + gap) * 2f,
                y, buttonWidth, buttonHeight);
            Rect complete = Button(margin + (buttonWidth + gap) * 3f,
                y, buttonWidth, buttonHeight);
            y += buttonHeight + gap;

            float maximumPanelHeight = Mathf.Max(1f,
                screenHeight - margin * 2f);
            float outcomeHeight = Mathf.Max(78f * uiScale,
                IsFinite(measuredOutcomeHeight)
                    ? measuredOutcomeHeight
                    : 78f * uiScale);
            outcomeHeight = Mathf.Min(
                outcomeHeight,
                Mathf.Max(20f,
                    maximumPanelHeight - y - gap - 20f * uiScale - margin));
            float feedbackHeight = Mathf.Max(56f * uiScale,
                IsFinite(measuredFeedbackHeight)
                    ? measuredFeedbackHeight
                    : 56f * uiScale);
            feedbackHeight = Mathf.Min(
                feedbackHeight,
                Mathf.Max(20f,
                    maximumPanelHeight - y - gap -
                    outcomeHeight - margin));
            var feedback = new Rect(
                margin, y, contentWidth, feedbackHeight);
            y += feedbackHeight + gap;
            var lastOutcome = new Rect(
                margin, y, contentWidth, outcomeHeight);
            float panelHeight = Mathf.Min(
                maximumPanelHeight,
                lastOutcome.yMax + margin);
            float panelY = Mathf.Clamp(
                margin,
                0f,
                Mathf.Max(0f, screenHeight - panelHeight));
            var panel = new Rect(panelX, panelY, panelWidth, panelHeight);
            var content = new Rect(
                margin,
                titleHeight,
                contentWidth,
                panelHeight - titleHeight - margin);
            var title = new Rect(0f, 0f, panelWidth, titleHeight);
            return new RouteEditorPanelLayout(
                panel, content, title, status, hold, draft, heightSource, help, depth,
                apply, delete, clear, cancel,
                pause, resume, restart, complete, feedback, lastOutcome, uiScale);
        }

        private static Rect Button(float x, float y, float width, float height)
        {
            return new Rect(x, y, width, height);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public enum RouteEditHeightSource
    {
        SelectedWaypoint = 0,
        PreviousWaypoint = 1,
        ActiveRoute = 2,
        VehiclePose = 3
    }

    public readonly struct RouteEditHeightResolution
    {
        public RouteEditHeightResolution(
            RouteEditHeightSource source,
            float worldY,
            int sourceWaypointIndex)
        {
            Source = source;
            WorldY = worldY;
            SourceWaypointIndex = sourceWaypointIndex;
        }

        public RouteEditHeightSource Source { get; }
        public float WorldY { get; }
        public int SourceWaypointIndex { get; }
    }

    public static class RouteEditorVehiclePolicy
    {
        public static bool SupportsVerticalWaypointEditing(
            VehicleSelectionKind vehicleKind)
        {
            return vehicleKind == VehicleSelectionKind.Auv ||
                vehicleKind == VehicleSelectionKind.Rov;
        }
    }

    public static class RouteEditHeightResolver
    {
        public static bool TryResolve(
            VehicleSelectionKind vehicleKind,
            DraftRouteSession draft,
            ActiveRouteSnapshot active,
            float fallbackVehicleY,
            out RouteEditHeightResolution resolution)
        {
            resolution = default;
            if (draft != null)
            {
                int selected = draft.SelectedWaypointIndex;
                if (selected >= 0 && selected < draft.Waypoints.Count)
                {
                    if (!TryConvertY(draft.Waypoints[selected].Y, out float y))
                        return false;
                    resolution = new RouteEditHeightResolution(
                        RouteEditHeightSource.SelectedWaypoint, y, selected);
                    return true;
                }

                if (RouteEditorVehiclePolicy.SupportsVerticalWaypointEditing(
                        vehicleKind) &&
                    draft.Waypoints.Count > 0)
                {
                    int previous = draft.Waypoints.Count - 1;
                    if (!TryConvertY(draft.Waypoints[previous].Y, out float y))
                        return false;
                    resolution = new RouteEditHeightResolution(
                        RouteEditHeightSource.PreviousWaypoint, y, previous);
                    return true;
                }
            }

            if (active != null && active.WaypointCount > 0)
            {
                if (!TryConvertY(active.GetWaypoint(0).Y, out float y))
                    return false;
                resolution = new RouteEditHeightResolution(
                    RouteEditHeightSource.ActiveRoute, y, 0);
                return true;
            }

            if (!IsFinite(fallbackVehicleY))
                return false;
            resolution = new RouteEditHeightResolution(
                RouteEditHeightSource.VehiclePose, fallbackVehicleY, -1);
            return true;
        }

        public static string FormatAuvFeedback(
            in RouteEditHeightResolution resolution,
            int draftWaypointCount)
        {
            return FormatHeightFeedback(in resolution, draftWaypointCount);
        }

        public static string FormatHeightFeedback(
            in RouteEditHeightResolution resolution,
            int draftWaypointCount)
        {
            string sourceText;
            switch (resolution.Source)
            {
                case RouteEditHeightSource.SelectedWaypoint:
                    sourceText = "Selected waypoint " +
                        (resolution.SourceWaypointIndex + 1);
                    break;
                case RouteEditHeightSource.PreviousWaypoint:
                    sourceText = "Previous waypoint " +
                        (resolution.SourceWaypointIndex + 1);
                    break;
                case RouteEditHeightSource.ActiveRoute:
                    sourceText = "Active route waypoint 1";
                    break;
                default:
                    sourceText = "Vehicle pose";
                    break;
            }

            string text = "Height source: " + sourceText +
                " | Y=" + resolution.WorldY.ToString(
                    "0.00", CultureInfo.InvariantCulture) + "m";
            if (resolution.Source == RouteEditHeightSource.SelectedWaypoint &&
                resolution.SourceWaypointIndex >= 0 &&
                resolution.SourceWaypointIndex < draftWaypointCount - 1)
            {
                text += "\nNew point appends to route end.";
            }
            return text;
        }

        private static bool TryConvertY(double value, out float converted)
        {
            converted = (float)value;
            return !double.IsNaN(value) && !double.IsInfinity(value) &&
                IsFinite(converted);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public static class VehicleRouteProjection
    {
        public static bool TryProjectRouteEditorPointer(
            VehicleSelectionKind vehicleKind,
            Ray ray,
            float worldY,
            out Vector3 point)
        {
            point = default;
            if (vehicleKind != VehicleSelectionKind.Auv &&
                vehicleKind != VehicleSelectionKind.Rov &&
                vehicleKind != VehicleSelectionKind.Usv)
            {
                return false;
            }
            return TryProjectToHorizontalPlane(ray, worldY, out point);
        }

        public static bool TryProjectToHorizontalPlane(
            Ray ray,
            float worldY,
            out Vector3 point)
        {
            point = default;
            if (Mathf.Abs(ray.direction.y) < 0.00001f)
                return false;
            float distance = (worldY - ray.origin.y) / ray.direction.y;
            if (distance <= 0f || float.IsNaN(distance) || float.IsInfinity(distance))
                return false;
            point = ray.GetPoint(distance);
            point.y = worldY;
            return IsFinite(point);
        }

        private static bool IsFinite(Vector3 point)
        {
            return !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
                   !float.IsNaN(point.y) && !float.IsInfinity(point.y) &&
                   !float.IsNaN(point.z) && !float.IsInfinity(point.z);
        }
    }

    public enum RoutePointerGestureState
    {
        Idle = 0,
        PendingDrag = 1,
        Dragging = 2
    }

    public sealed class RoutePointerGesture
    {
        public const float DragThresholdPixels = 6f;

        private Vector2 pointerDownScreenPosition;
        private Vector3 originalWaypoint;
        private Vector3 pointerAnchor;
        private bool hasPointerAnchor;

        public RoutePointerGestureState State { get; private set; }
        public bool HasPointerAnchor => hasPointerAnchor;

        public void Begin(
            Vector2 screenPosition,
            Vector3 waypoint,
            bool hasProjectedPointerDown,
            Vector3 projectedPointerDown)
        {
            pointerDownScreenPosition = screenPosition;
            originalWaypoint = waypoint;
            hasPointerAnchor = hasProjectedPointerDown;
            pointerAnchor = hasProjectedPointerDown
                ? waypoint - projectedPointerDown
                : Vector3.zero;
            State = RoutePointerGestureState.PendingDrag;
        }

        public bool TryGetDragTarget(
            Vector2 screenPosition,
            bool hasCurrentProjection,
            Vector3 currentProjection,
            bool preserveOriginalY,
            out Vector3 target)
        {
            target = default;
            if (State == RoutePointerGestureState.Idle)
                return false;

            if (State == RoutePointerGestureState.PendingDrag &&
                (screenPosition - pointerDownScreenPosition).sqrMagnitude >=
                DragThresholdPixels * DragThresholdPixels)
                State = RoutePointerGestureState.Dragging;

            if (State != RoutePointerGestureState.Dragging ||
                !hasPointerAnchor || !hasCurrentProjection)
                return false;

            target = currentProjection + pointerAnchor;
            if (preserveOriginalY)
                target.y = originalWaypoint.y;
            return true;
        }

        public void Reset()
        {
            pointerDownScreenPosition = default;
            originalWaypoint = default;
            pointerAnchor = default;
            hasPointerAnchor = false;
            State = RoutePointerGestureState.Idle;
        }
    }

    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class VehicleRouteEditingController : MonoBehaviour
    {
        private const int MaxBindingFrames = 120;
        private const float HandleScreenRadius = 16f;
        public const double VerticalWaypointStepMetres = 0.25;
        private sealed class VehicleEditState
        {
            public VehicleSelectionKind Kind;
            public Transform Vehicle;
            public VehicleDataRuntimeHost Host;
            public readonly DraftRouteSession Draft = new DraftRouteSession();
            public DraftRouteVisual Visual;
            public string LastApplyOutcome = "No Apply recorded.";
        }

        private readonly Dictionary<VehicleSelectionKind, VehicleEditState> states =
            new Dictionary<VehicleSelectionKind, VehicleEditState>(3);
        private static VehicleRouteEditingController inputOwner;
        private VehicleSelectionCameraController selection;
        private Camera targetCamera;
        private VehicleSelectionKind selectedKind;
        private bool subscribed;
        private readonly RoutePointerGesture pointerGesture =
            new RoutePointerGesture();
        private bool hasPanelLayout;
        private RouteEditorPanelLayout panelLayout;
        private string feedback = "Select a vehicle, then press E to edit its route.";
        private float guiStyleScale;
        private GUIStyle panelStyle;
        private GUIStyle statusStyle;
        private GUIStyle secondaryStyle;
        private GUIStyle helperStyle;
        private GUIStyle buttonStyle;
        private GUIStyle feedbackStyle;
        private GUIStyle outcomeStyle;

        public bool IsEditingCurrentVehicle =>
            CurrentState != null && CurrentState.Draft.IsEditing;
        public static VehicleRouteEditingController InputOwner => inputOwner;
        public VehicleSelectionKind SelectedVehicle => selectedKind;
        public string Feedback => feedback;
        public string LastApplyOutcome =>
            CurrentState == null ? string.Empty : CurrentState.LastApplyOutcome;

        private VehicleEditState CurrentState
        {
            get
            {
                states.TryGetValue(selectedKind, out VehicleEditState state);
                return state;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<VehicleRouteEditingController>() != null)
                return;
            new GameObject(nameof(VehicleRouteEditingController))
                .AddComponent<VehicleRouteEditingController>();
        }

        private IEnumerator Start()
        {
            for (int frame = 0; frame < MaxBindingFrames; frame++)
            {
                if (selection == null)
                    selection = FindFirstObjectByType<VehicleSelectionCameraController>();
                if (selection != null)
                {
                    targetCamera = selection.TargetCamera != null
                        ? selection.TargetCamera
                        : Camera.main;
                    BindState(VehicleSelectionKind.Auv, VehicleType.Auv);
                    BindState(VehicleSelectionKind.Rov, VehicleType.Rov);
                    BindState(VehicleSelectionKind.Usv, VehicleType.Usv);
                    Subscribe();
                    if (states.Count == 3)
                        yield break;
                }
                yield return null;
            }
            ShowFeedback("Route editor binding is incomplete; unavailable vehicles stay isolated.");
        }

        private void OnEnable()
        {
            if (inputOwner == null)
                inputOwner = this;
        }

        private void OnDisable()
        {
            EndDrag();
            RouteEditingInputContext.SetRouteEditorActive(this, false);
            if (subscribed && selection != null)
                selection.SelectionChanged -= OnSelectionChanged;
            subscribed = false;
            if (ReferenceEquals(inputOwner, this))
                inputOwner = null;
        }

        private void Update()
        {
            if (inputOwner == null)
                inputOwner = this;
            if (!ReferenceEquals(inputOwner, this))
                return;
            if (selection == null || targetCamera == null)
                return;

            VehicleEditState state = CurrentState;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                EndDrag();
                if (state != null && state.Draft.IsEditing)
                    CancelCurrent();
                else
                    selection.CancelSelection();
                return;
            }
            if (Input.GetKeyDown(KeyCode.E))
            {
                ToggleEditMode(state);
                state = CurrentState;
            }

            if (state == null || !state.Draft.IsEditing)
            {
                RefreshVisuals();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace))
                DeleteSelected(state);
            if (Input.GetKeyDown(KeyCode.C))
                ClearCurrent(state);
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                ApplyCurrent(state);
            if (Input.GetKeyDown(KeyCode.P))
                TogglePause(state);
            if (Input.GetKeyDown(KeyCode.R))
            {
                state.Host.RestartRoute();
                ShowFeedback("Route restarted; Apply is now locked while Running.");
            }
            if (Input.GetKeyDown(KeyCode.End))
            {
                state.Host.CompleteRoute();
                ShowFeedback("Route marked Completed; Apply is available.");
            }
            if (RouteEditorVehiclePolicy.SupportsVerticalWaypointEditing(
                    state.Kind) &&
                (Input.GetKeyDown(KeyCode.PageUp) || Input.GetKeyDown(KeyCode.PageDown)))
                AdjustSelectedWaypointVertical(
                    state,
                    Input.GetKeyDown(KeyCode.PageUp)
                        ? VerticalWaypointStepMetres
                        : -VerticalWaypointStepMetres);

            HandlePrimaryPointer(state);
            RefreshVisuals();
        }

        private void OnGUI()
        {
            if (!ReferenceEquals(inputOwner, this))
                return;
            VehicleEditState state = CurrentState;
            if (state == null || state.Host == null)
                return;

            bool showVerticalEditing =
                RouteEditorVehiclePolicy.SupportsVerticalWaypointEditing(
                    state.Kind) &&
                state.Draft.IsEditing;
            float uiScale = RouteEditorPanelLayout.CalculateUiScale(
                Screen.width, Screen.height);
            EnsureGuiStyles(uiScale);
            RouteEditorPanelLayout initial =
                RouteEditorPanelLayout.Calculate(
                    Screen.width, Screen.height, showVerticalEditing, 32f, 32f);
            float feedbackHeight = feedbackStyle.CalcHeight(
                new GUIContent(feedback), initial.FeedbackRect.width);
            float outcomeHeight = outcomeStyle.CalcHeight(
                new GUIContent("LAST APPLY OUTCOME\n" + state.LastApplyOutcome),
                initial.LastOutcomeRect.width);
            panelLayout = RouteEditorPanelLayout.Calculate(
                Screen.width,
                Screen.height,
                showVerticalEditing,
                feedbackHeight,
                outcomeHeight);
            hasPanelLayout = true;

            GUI.Box(panelLayout.PanelRect,
                "ROUTE EDITOR  /  " + state.Kind, panelStyle);
            GUI.BeginGroup(panelLayout.PanelRect);
            GUI.Label(panelLayout.StatusRect,
                state.Kind + "     MODE  " +
                (state.Draft.IsEditing ? "EDIT" : "NORMAL") +
                "     STATE  " + state.Host.RouteExecutionState,
                statusStyle);
            GUI.Label(panelLayout.HoldRect,
                state.Host.RouteExecutionState == VehicleRouteExecutionState.Hold
                    ? "Hold reason: " + state.Host.LastPoseConstraintReason
                    : string.Empty,
                secondaryStyle);
            GUI.Label(panelLayout.DraftRect,
                "Draft: " + state.Draft.Waypoints.Count +
                " points     Active Route v" + state.Host.RouteVersion,
                secondaryStyle);
            if (showVerticalEditing)
            {
                if (RouteEditHeightResolver.TryResolve(
                        state.Kind,
                        state.Draft,
                        state.Host.ActiveRouteSnapshot,
                        state.Vehicle.position.y,
                        out RouteEditHeightResolution height))
                {
                    GUI.Label(panelLayout.HeightSourceRect,
                        RouteEditHeightResolver.FormatHeightFeedback(
                            in height, state.Draft.Waypoints.Count),
                        secondaryStyle);
                }
                else
                {
                    GUI.Label(panelLayout.HeightSourceRect,
                        "Height source: unavailable", secondaryStyle);
                }
            }
            GUI.Label(panelLayout.HelpRect,
                state.Draft.IsEditing
                    ? "LMB add/select/drag | E exit | Esc cancel\nDel remove | C clear | Enter apply | P pause/resume"
                    : "Press E to enter editing. F/T/orbit/zoom remain available.",
                helperStyle);
            if (showVerticalEditing)
                GUI.Label(panelLayout.DepthRect,
                    "PageUp: Raise waypoint (+Y)\n" +
                    "PageDown: Lower waypoint (-Y)", helperStyle);

            GUI.enabled = state.Draft.IsEditing;
            if (GUI.Button(panelLayout.ApplyRect, "Apply", buttonStyle)) ApplyCurrent(state);
            if (GUI.Button(panelLayout.DeleteRect, "Delete", buttonStyle)) DeleteSelected(state);
            if (GUI.Button(panelLayout.ClearRect, "Clear", buttonStyle)) ClearCurrent(state);
            if (GUI.Button(panelLayout.CancelRect, "Cancel", buttonStyle)) CancelCurrent();
            GUI.enabled = true;
            if (GUI.Button(panelLayout.PauseRect, "Pause", buttonStyle)) state.Host.PauseRoute();
            if (GUI.Button(panelLayout.ResumeRect, "Resume", buttonStyle)) state.Host.ResumeRoute();
            if (GUI.Button(panelLayout.RestartRect, "Restart", buttonStyle)) state.Host.RestartRoute();
            if (GUI.Button(panelLayout.CompleteRect, "Complete", buttonStyle)) state.Host.CompleteRoute();
            GUI.Label(panelLayout.FeedbackRect, feedback, feedbackStyle);
            GUI.Label(panelLayout.LastOutcomeRect,
                "LAST APPLY OUTCOME\n" + state.LastApplyOutcome,
                outcomeStyle);
            GUI.EndGroup();
        }

        private void EnsureGuiStyles(float scale)
        {
            if (panelStyle != null && Mathf.Abs(guiStyleScale - scale) < 0.001f)
                return;
            guiStyleScale = scale;
            panelStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = Mathf.RoundToInt(28f * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = new Color(0.62f, 0.92f, 0.96f) }
            };
            statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(24f * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };
            secondaryStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(20f * scale),
                wordWrap = true,
                normal = { textColor = new Color(0.82f, 0.90f, 0.92f) }
            };
            helperStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(17f * scale),
                wordWrap = true,
                normal = { textColor = new Color(0.62f, 0.72f, 0.76f) }
            };
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(21f * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            feedbackStyle = new GUIStyle(secondaryStyle)
            {
                fontSize = Mathf.RoundToInt(18f * scale),
                normal = { textColor = new Color(0.68f, 0.87f, 0.90f) }
            };
            outcomeStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = Mathf.RoundToInt(20f * scale),
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(
                    Mathf.RoundToInt(10f * scale),
                    Mathf.RoundToInt(10f * scale),
                    Mathf.RoundToInt(8f * scale),
                    Mathf.RoundToInt(8f * scale)),
                normal = { textColor = new Color(0.72f, 0.94f, 0.86f) }
            };
        }

        public bool EnterEditMode(VehicleSelectionKind kind)
        {
            EndDrag();
            if (!states.TryGetValue(kind, out VehicleEditState state) ||
                state.Host == null || state.Host.ActiveRouteSnapshot == null)
                return false;
            selectedKind = kind;
            state.Draft.Begin(state.Host.ActiveRouteSnapshot);
            RouteEditingInputContext.SetRouteEditorActive(this, true);
            ShowFeedback("Draft copied from Active Route v" + state.Host.RouteVersion + ".");
            RefreshVisuals();
            return true;
        }

        private void ToggleEditMode(VehicleEditState state)
        {
            if (state == null)
            {
                ShowFeedback("Select AUV, ROV, or USV before entering Route Edit Mode.");
                return;
            }
            EndDrag();
            if (state.Draft.IsEditing)
            {
                state.Draft.ExitPreservingDraft();
                RouteEditingInputContext.SetRouteEditorActive(this, false);
                ShowFeedback("Normal Mode; this vehicle's uncommitted Draft is preserved.");
            }
            else
            {
                EnterEditMode(state.Kind);
            }
        }

        private void HandlePrimaryPointer(VehicleEditState state)
        {
            if (IsPointerOverOverlay())
            {
                EndDrag();
                return;
            }
            if (Input.GetMouseButtonDown(0))
            {
                int handle = FindHandleAtScreenPoint(state, Input.mousePosition);
                if (handle >= 0)
                {
                    state.Draft.Select(handle);
                    Vector3 original = ToVector3(state.Draft.Waypoints[handle]);
                    bool hasProjection = TryProjectPointer(
                        state, Input.mousePosition, out Vector3 projected);
                    pointerGesture.Begin(
                        Input.mousePosition,
                        original,
                        hasProjection,
                        projected);
                    ShowFeedback("Waypoint " + (handle + 1) + " selected; drag to move it.");
                }
                else if (TryProjectPointer(state, Input.mousePosition, out Vector3 point))
                {
                    state.Draft.Add(ToVector3d(point));
                    ShowFeedback("Waypoint appended to the end of this Draft.");
                }
                else
                {
                    ShowFeedback(
                        "The pointer ray does not intersect this vehicle's edit plane; Draft unchanged.");
                }
            }
            if (Input.GetMouseButton(0))
            {
                bool hasProjection = TryProjectPointer(
                    state, Input.mousePosition, out Vector3 projected);
                if (pointerGesture.TryGetDragTarget(
                        Input.mousePosition,
                        hasProjection,
                        projected,
                        RouteEditorVehiclePolicy.SupportsVerticalWaypointEditing(
                            state.Kind),
                        out Vector3 moved))
                    state.Draft.MoveSelected(ToVector3d(moved));
            }
            if (Input.GetMouseButtonUp(0)) EndDrag();
        }

        private bool TryProjectPointer(
            VehicleEditState state,
            Vector2 screenPoint,
            out Vector3 point)
        {
            Ray ray = targetCamera.ScreenPointToRay(screenPoint);
            if (!RouteEditHeightResolver.TryResolve(
                    state.Kind,
                    state.Draft,
                    state.Host.ActiveRouteSnapshot,
                    state.Vehicle.position.y,
                    out RouteEditHeightResolution height))
            {
                point = default;
                return false;
            }
            float nominalY = height.WorldY;
            return VehicleRouteProjection.TryProjectRouteEditorPointer(
                state.Kind, ray, nominalY, out point);
        }

        private int FindHandleAtScreenPoint(VehicleEditState state, Vector2 point)
        {
            int nearest = -1;
            float nearestSquared = HandleScreenRadius * HandleScreenRadius;
            for (int index = 0; index < state.Draft.Waypoints.Count; index++)
            {
                Vector3 screen = targetCamera.WorldToScreenPoint(
                    ToVector3(state.Draft.Waypoints[index]));
                if (screen.z <= 0f) continue;
                float squared = ((Vector2)screen - point).sqrMagnitude;
                if (squared <= nearestSquared)
                {
                    nearest = index;
                    nearestSquared = squared;
                }
            }
            return nearest;
        }

        private void DeleteSelected(VehicleEditState state)
        {
            EndDrag();
            ShowFeedback(state.Draft.DeleteSelected()
                ? "Selected waypoint deleted; invalid Drafts remain editable but cannot Apply."
                : "Select a Draft waypoint before deleting.");
        }

        private void ClearCurrent(VehicleEditState state)
        {
            EndDrag();
            state.Draft.Clear();
            ShowFeedback("Only this vehicle's Draft was cleared; Active Route and Actual Track are unchanged.");
        }

        private void CancelCurrent()
        {
            VehicleEditState state = CurrentState;
            if (state == null) return;
            EndDrag();
            state.Draft.Cancel(state.Host.ActiveRouteSnapshot);
            RouteEditingInputContext.SetRouteEditorActive(this, false);
            selection.SelectVehicle(state.Kind);
            ShowFeedback("Draft cancelled; Active Route and progress are unchanged.");
            RefreshVisuals();
        }

        private void ApplyCurrent(VehicleEditState state)
        {
            EndDrag();
            ActiveRouteSnapshot active = state.Host.ActiveRouteSnapshot;
            ulong activeVersion = state.Host.RouteVersion;
            bool wasRunning = state.Host.RouteExecutionState ==
                VehicleRouteExecutionState.Running;
            if (!state.Draft.TryValidate(
                    active,
                    state.Host.MonotonicNowSeconds,
                    out string error))
            {
                state.LastApplyOutcome = FormatRejectedOutcome(
                    wasRunning,
                    activeVersion,
                    error,
                    RouteSafetyFailureDiagnostic.None);
                ShowFeedback(state.LastApplyOutcome);
                return;
            }
            if (!state.Host.TryApplyDraftRoute(
                    state.Draft.Waypoints,
                    out error,
                    out RouteSafetyFailureDiagnostic diagnostic))
            {
                state.LastApplyOutcome = FormatRejectedOutcome(
                    wasRunning,
                    activeVersion,
                    error,
                    diagnostic);
                ShowFeedback(state.LastApplyOutcome);
                return;
            }
            state.Draft.AcceptAppliedSnapshot(state.Host.ActiveRouteSnapshot);
            state.LastApplyOutcome = wasRunning
                ? "Published Active v" + state.Host.RouteVersion +
                  "; State Running.\nAtomic continuation from accepted pose."
                : "Published Active v" + state.Host.RouteVersion +
                  "; State " + state.Host.RouteExecutionState +
                  ".\nRoute published through the Batch 1 data chain.";
            ShowFeedback(state.LastApplyOutcome);
        }

        private void TogglePause(VehicleEditState state)
        {
            bool changed = state.Host.RouteExecutionState == VehicleRouteExecutionState.Running
                ? state.Host.PauseRoute()
                : state.Host.ResumeRoute();
            ShowFeedback(changed
                ? "Route state is now " + state.Host.RouteExecutionState + "."
                : "Pause/Resume is not available from " + state.Host.RouteExecutionState + ".");
        }

        private void AdjustSelectedWaypointVertical(
            VehicleEditState state,
            double delta)
        {
            int index = state.Draft.SelectedWaypointIndex;
            if (index < 0 || index >= state.Draft.Waypoints.Count)
            {
                ShowFeedback("Select an AUV or ROV waypoint before adjusting its vertical position.");
                return;
            }
            Vector3d value = state.Draft.Waypoints[index];
            state.Draft.MoveSelected(AdjustWaypointVertical(value, delta));
            ShowFeedback(delta > 0f
                ? "Waypoint raised by 0.25 m (+Y)."
                : "Waypoint lowered by 0.25 m (-Y).");
        }

        public static string FormatRejectedOutcome(
            bool wasRunning,
            ulong activeVersion,
            string error,
            RouteSafetyFailureDiagnostic diagnostic)
        {
            string classification = string.Empty;
            if (diagnostic.HasFailure)
            {
                int segment = diagnostic.SegmentIndex;
                classification = wasRunning && segment == 1
                    ? "Connection segment"
                    : "Draft segment " + (wasRunning ? segment - 1 : segment);
                classification += "; " + diagnostic.Percentage.ToString("0.0") +
                    "%; " + diagnostic.TerrainState;
            }
            return "Apply rejected; Route not published; Active v" +
                activeVersion + " unchanged." +
                (string.IsNullOrEmpty(classification)
                    ? "\n" + error
                    : "\n" + classification);
        }

        public static Vector3d AdjustAuvWaypointDepth(
            Vector3d waypoint,
            double delta)
        {
            return AdjustWaypointVertical(waypoint, delta);
        }

        public static Vector3d AdjustWaypointVertical(
            Vector3d waypoint,
            double delta)
        {
            return new Vector3d(
                waypoint.X,
                waypoint.Y + delta,
                waypoint.Z);
        }

        public static bool IsScreenPointOverPanel(
            Vector2 screenPoint,
            float screenHeight,
            Rect panel)
        {
            return panel.Contains(new Vector2(
                screenPoint.x,
                screenHeight - screenPoint.y));
        }

        private void BindState(VehicleSelectionKind kind, VehicleType type)
        {
            if (states.ContainsKey(kind)) return;
            if (!selection.TryGetVehicleTransform(kind, out Transform vehicle)) return;
            VehicleDataRuntimeHost host = FindHost(type);
            if (host == null || host.ActiveRouteSnapshot == null) return;
            states.Add(kind, new VehicleEditState
            {
                Kind = kind,
                Vehicle = vehicle,
                Host = host,
                Visual = new DraftRouteVisual(kind, transform)
            });
        }

        private static VehicleDataRuntimeHost FindHost(VehicleType type)
        {
            VehicleDataRuntimeHost[] hosts = FindObjectsByType<VehicleDataRuntimeHost>(
                FindObjectsSortMode.None);
            for (int index = 0; index < hosts.Length; index++)
            {
                VehiclePoseIntegrationConfiguration config = hosts[index].IntegrationConfiguration;
                if (config != null && config.VehicleType == type) return hosts[index];
            }
            return null;
        }

        private void Subscribe()
        {
            if (subscribed) return;
            selection.SelectionChanged += OnSelectionChanged;
            subscribed = true;
            OnSelectionChanged(selection.SelectedVehicle, selection.SelectedTransform);
        }

        private void OnSelectionChanged(VehicleSelectionKind kind, Transform ignored)
        {
            EndDrag();
            selectedKind = kind;
            VehicleEditState state = CurrentState;
            RouteEditingInputContext.SetRouteEditorActive(
                this, state != null && state.Draft.IsEditing);
            RefreshVisuals();
        }

        private void RefreshVisuals()
        {
            foreach (KeyValuePair<VehicleSelectionKind, VehicleEditState> entry in states)
            {
                bool visible = entry.Key == selectedKind && entry.Value.Draft.IsEditing;
                entry.Value.Visual.Refresh(entry.Value.Draft, visible);
            }
        }

        private void EndDrag()
        {
            pointerGesture.Reset();
        }

        private bool IsPointerOverOverlay()
        {
            Rect current = hasPanelLayout
                ? panelLayout.PanelRect
                : RouteEditorPanelLayout.Calculate(
                    Screen.width,
                    Screen.height,
                    CurrentState != null &&
                    RouteEditorVehiclePolicy.SupportsVerticalWaypointEditing(
                        CurrentState.Kind) &&
                    CurrentState.Draft.IsEditing,
                    32f).PanelRect;
            return IsScreenPointOverPanel(
                Input.mousePosition,
                Screen.height,
                current);
        }

        private void ShowFeedback(string message)
        {
            feedback = message;
        }

        private static Vector3 ToVector3(Vector3d point)
        {
            return new Vector3((float)point.X, (float)point.Y, (float)point.Z);
        }

        private static Vector3d ToVector3d(Vector3 point)
        {
            return new Vector3d(point.x, point.y, point.z);
        }
    }

    internal sealed class DraftRouteVisual
    {
        private readonly GameObject root;
        private readonly LineRenderer line;
        private readonly List<GameObject> handles = new List<GameObject>();
        private readonly Material lineMaterial;
        private readonly Material handleMaterial;
        private readonly Material selectedMaterial;
        private readonly Material invalidMaterial;

        public DraftRouteVisual(VehicleSelectionKind kind, Transform parent)
        {
            root = new GameObject("E3C_DraftRoute_" + kind);
            root.transform.SetParent(parent, false);
            root.layer = LayerMask.NameToLayer("Ignore Raycast");
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            lineMaterial = NewMaterial(shader, "Draft Line " + kind, new Color(1f, 0.2f, 0.85f, 0.9f));
            handleMaterial = NewMaterial(shader, "Draft Handle " + kind, new Color(1f, 0.35f, 0.85f, 1f));
            selectedMaterial = NewMaterial(shader, "Draft Selected " + kind, new Color(1f, 0.9f, 0.1f, 1f));
            invalidMaterial = NewMaterial(shader, "Draft Invalid " + kind, new Color(1f, 0.15f, 0.1f, 1f));
            line = root.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = 0.075f;
            line.material = lineMaterial;
            line.numCapVertices = 2;
            root.SetActive(false);
        }

        public void Refresh(DraftRouteSession draft, bool visible)
        {
            root.SetActive(visible);
            if (!visible) return;
            bool invalid = draft.Waypoints.Count < 2;
            line.material = invalid ? invalidMaterial : lineMaterial;
            line.positionCount = draft.Waypoints.Count;
            for (int index = 0; index < draft.Waypoints.Count; index++)
            {
                Vector3 point = new Vector3(
                    (float)draft.Waypoints[index].X,
                    (float)draft.Waypoints[index].Y,
                    (float)draft.Waypoints[index].Z);
                line.SetPosition(index, point);
                GameObject handle = GetHandle(index);
                handle.transform.position = point;
                handle.transform.localScale = Vector3.one *
                    (index == draft.SelectedWaypointIndex ? 0.22f : 0.15f);
                handle.GetComponent<Renderer>().sharedMaterial = invalid
                    ? invalidMaterial
                    : index == draft.SelectedWaypointIndex
                        ? selectedMaterial
                        : handleMaterial;
                handle.SetActive(true);
            }
            for (int index = draft.Waypoints.Count; index < handles.Count; index++)
                handles[index].SetActive(false);
        }

        private GameObject GetHandle(int index)
        {
            while (handles.Count <= index)
            {
                GameObject handle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                handle.name = "DraftWaypoint_" + handles.Count;
                handle.layer = root.layer;
                handle.transform.SetParent(root.transform, false);
                Collider collider = handle.GetComponent<Collider>();
                if (collider != null) Object.Destroy(collider);
                handles.Add(handle);
            }
            return handles[index];
        }

        private static Material NewMaterial(Shader shader, string name, Color color)
        {
            Material material = new Material(shader) { name = name, color = color };
            return material;
        }
    }
}
