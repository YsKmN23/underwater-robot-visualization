using System.Collections;
using System.Collections.Generic;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnderwaterRobotScene.Visualization.Runtime
{
    [DisallowMultipleComponent]
    public sealed class VehicleTrajectoryVisualizationController : MonoBehaviour
    {
        private const int MaxBindingFrames = 120;
        private const float FallbackSampleDistance = 0.35f;
        private const int FallbackMaxPointsPerVehicle = 500;
        private const float FallbackTeleportBreakDistance = 10f;
        private const float FallbackActualLineWidth = 0.055f;
        private const float FallbackMotionWindowSeconds = 2f;
        private const float FallbackMotionStartDisplacement = 0.9f;
        private const float FallbackMotionStopDisplacement = 0.3f;
        private const float FallbackMotionStopHoldSeconds = 1f;
        private const bool FallbackFlattenUsvActualTraceToInitialHeight = true;
        private const string VisualRootName = "V2_Trajectory_Visuals";
        private const int TargetRingSegmentCount = 64;
        private const float GeometryEpsilon = 0.0001f;

        private static readonly Color FallbackActualLineColor =
            new Color(0.2f, 1f, 0.35f, 1f);
        private static readonly Color FallbackPlannedLineColor =
            new Color(0.15f, 0.85f, 1f, 0.65f);
        private static readonly Color FallbackWaypointColor =
            new Color(0.45f, 0.9f, 1f, 0.9f);
        private static readonly Color FallbackTargetColor =
            new Color(0.1f, 0.9f, 1f, 0.85f);

        private sealed class TrajectorySegment
        {
            public readonly List<Vector3> Points = new List<Vector3>();
        }

        private struct DashSegment
        {
            public Vector3 Start;
            public Vector3 End;
        }

        private struct MotionSample
        {
            public float Time;
            public Vector3 Position;
        }

        private sealed class VehicleTraceState
        {
            public VehicleSelectionKind Kind;
            public Transform Vehicle;
            public VehicleDataRuntimeHost RouteHost;
            public ulong ObservedRouteVersion;
            public readonly LinkedList<TrajectorySegment> Segments =
                new LinkedList<TrajectorySegment>();
            public bool HasAcceptedPoint;
            public Vector3 LastAcceptedPoint;
            public int TotalPointCount;
            public Vector3 InitialPosition;
            public Quaternion InitialRotation;
            public float InitialTraceHeight;
            public readonly Queue<MotionSample> MotionSamples =
                new Queue<MotionSample>();
            public bool MeaningfullyMoving;
            public float LowMotionDuration;
            public bool HasObservedPosition;
            public Vector3 LastObservedPosition;
            public float LastObservedTime;
            public readonly List<Vector3> PlannedWorldPoints =
                new List<Vector3>();
            public int ValidConfiguredWaypointCount;
            public bool HasTarget;
            public Vector3 TargetWorldPosition;
            public bool PlannedRouteBuilt;
            public readonly List<DashSegment> PlannedDashSegments =
                new List<DashSegment>();
            public bool PlannedVisualGeometryBuilt;
        }

        private readonly Dictionary<VehicleSelectionKind, VehicleTraceState>
            traceStates =
                new Dictionary<VehicleSelectionKind, VehicleTraceState>(3);
        private readonly HashSet<string> warningKeys = new HashSet<string>();

        private VehicleRouteConfig routeConfig;
        private VehicleSelectionCameraController selectionController;
        private float sampleDistance = FallbackSampleDistance;
        private int maxPointsPerVehicle = FallbackMaxPointsPerVehicle;
        private float teleportBreakDistance = FallbackTeleportBreakDistance;
        private float motionWindowSeconds = FallbackMotionWindowSeconds;
        private float motionStartDisplacement =
            FallbackMotionStartDisplacement;
        private float motionStopDisplacement =
            FallbackMotionStopDisplacement;
        private float motionStopHoldSeconds =
            FallbackMotionStopHoldSeconds;
        private bool flattenUsvActualTraceToInitialHeight =
            FallbackFlattenUsvActualTraceToInitialHeight;
        private bool bindingComplete;
        private bool selectionSubscribed;
        private bool visualResourcesInitialized;
        private bool visualResourcesAvailable;
        private bool trajectoryVisualsVisible = true;
        private VehicleSelectionKind displayedKind =
            VehicleSelectionKind.None;
        private Transform visualRoot;
        private Material actualTrajectoryMaterial;
        private Material plannedTrajectoryMaterial;
        private Material waypointMaterial;
        private Material targetRingMaterial;
        private Material targetColumnMaterial;
        private readonly List<LineRenderer> actualLinePool =
            new List<LineRenderer>();
        private readonly List<LineRenderer> plannedDashPool =
            new List<LineRenderer>();
        private readonly List<GameObject> waypointPool =
            new List<GameObject>();
        private LineRenderer targetRingRenderer;
        private LineRenderer targetColumnRenderer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<
                    VehicleTrajectoryVisualizationController>() != null)
            {
                return;
            }

            GameObject root = new GameObject(
                nameof(VehicleTrajectoryVisualizationController));
            root.AddComponent<VehicleTrajectoryVisualizationController>();
        }

        private IEnumerator Start()
        {
            LoadConfiguration();
            EnsureVisualResources();
            yield return BindDependencies();
        }

        private void Update()
        {
            HandleTrajectoryVisibilityInput();

            if (!bindingComplete)
            {
                return;
            }

            foreach (VehicleTraceState state in traceStates.Values)
            {
                if (state.Vehicle == null)
                {
                    WarnOnce(
                        "VehicleUnavailable:" + state.Kind,
                        "Trajectory recording stopped for " + state.Kind +
                        " because its vehicle Transform is unavailable.");
                    continue;
                }

                bool changed = SampleVehicle(state);
                bool routeChanged = RefreshActiveRouteIfChanged(state);
                if (changed &&
                    state.Kind == displayedKind &&
                    visualRoot != null &&
                    visualRoot.gameObject.activeSelf)
                {
                    RefreshActualLines(state);
                }

                if (routeChanged && state.Kind == displayedKind)
                {
                    RefreshPlannedVisuals(state);
                }
            }
        }

        private void HandleTrajectoryVisibilityInput()
        {
            if (Input.GetKeyDown(KeyCode.T))
            {
                ToggleTrajectoryVisuals();
            }
        }

        private void ToggleTrajectoryVisuals()
        {
            trajectoryVisualsVisible = !trajectoryVisualsVisible;
            RefreshTrajectoryVisualVisibility();
        }

        private void LoadConfiguration()
        {
            routeConfig = VehicleRouteConfig.Load();
            if (routeConfig == null)
            {
                WarnOnce(
                    "MissingRouteConfig",
                    "VehicleRouteConfig was not found at Resources/" +
                    VehicleRouteConfig.ResourcesPath +
                    ". Actual trajectories will use runtime fallback sampling " +
                    "values; planned routes are disabled.");
                return;
            }

            sampleDistance = Mathf.Max(
                0.01f,
                routeConfig.ActualSampleDistance);
            maxPointsPerVehicle = Mathf.Max(
                2,
                routeConfig.MaxActualPointsPerVehicle);
            teleportBreakDistance = Mathf.Max(
                sampleDistance * 2f,
                routeConfig.TeleportBreakDistance);
            motionWindowSeconds = Mathf.Max(
                0.1f,
                routeConfig.ActualMotionWindowSeconds);
            motionStartDisplacement = Mathf.Max(
                0.01f,
                routeConfig.ActualMotionStartDisplacement);
            motionStopDisplacement = Mathf.Clamp(
                routeConfig.ActualMotionStopDisplacement,
                0f,
                Mathf.Max(0f, motionStartDisplacement - 0.001f));
            motionStopHoldSeconds = Mathf.Max(
                0f,
                routeConfig.ActualMotionStopHoldSeconds);
            flattenUsvActualTraceToInitialHeight =
                routeConfig.FlattenUsvActualTraceToInitialHeight;
        }

        private IEnumerator BindDependencies()
        {
            for (int frame = 0; frame < MaxBindingFrames; frame++)
            {
                if (selectionController == null)
                {
                    selectionController = FindFirstObjectByType<
                        VehicleSelectionCameraController>();
                }

                if (selectionController != null)
                {
                    SubscribeToSelection();
                    TryBindVehicle(VehicleSelectionKind.Auv);
                    TryBindVehicle(VehicleSelectionKind.Rov);
                    TryBindVehicle(VehicleSelectionKind.Usv);
                    bindingComplete = traceStates.Count > 0;

                    if (traceStates.Count == 3)
                    {
                        yield break;
                    }
                }

                yield return null;
            }

            if (selectionController == null)
            {
                WarnOnce(
                    "SelectionControllerBindingTimeout",
                    "VehicleTrajectoryVisualizationController could not bind " +
                    "VehicleSelectionCameraController within " +
                    MaxBindingFrames + " frames.");
            }
            else
            {
                WarnForMissingVehicle(VehicleSelectionKind.Auv);
                WarnForMissingVehicle(VehicleSelectionKind.Rov);
                WarnForMissingVehicle(VehicleSelectionKind.Usv);
            }

            bindingComplete = traceStates.Count > 0;
        }

        private void TryBindVehicle(VehicleSelectionKind kind)
        {
            if (traceStates.ContainsKey(kind))
            {
                return;
            }

            Transform vehicle;
            if (!selectionController.TryGetVehicleTransform(kind, out vehicle) ||
                vehicle == null)
            {
                return;
            }

            VehicleTraceState state = new VehicleTraceState
            {
                Kind = kind,
                Vehicle = vehicle,
                InitialPosition = vehicle.position,
                InitialRotation = vehicle.rotation,
                InitialTraceHeight = vehicle.position.y
            };

            state.RouteHost = FindRouteHost(kind);

            BuildPlannedRoute(state);
            traceStates.Add(kind, state);

            if (displayedKind == kind)
            {
                RefreshTrajectoryVisualVisibility();
            }
        }

        private void WarnForMissingVehicle(VehicleSelectionKind kind)
        {
            if (traceStates.ContainsKey(kind))
            {
                return;
            }

            WarnOnce(
                "MissingVehicle:" + kind,
                "VehicleTrajectoryVisualizationController could not bind the " +
                kind + " Transform. Other available vehicles will continue.");
        }

        private bool SampleVehicle(VehicleTraceState state)
        {
            return SampleVehicleAtTime(
                state,
                state.Vehicle.position,
                Time.time);
        }

        private bool SampleVehicleAtTime(
            VehicleTraceState state,
            Vector3 current,
            float observationTime)
        {
            if (!IsFinite(current))
            {
                WarnOnce(
                    "InvalidVehiclePosition:" + state.Kind,
                        "Trajectory sampling skipped a non-finite position for " +
                        state.Kind + ".");
                return false;
            }

            Vector3 tracePoint = GetActualTracePoint(state, current);
            UpdateMeaningfulMotion(state, current, observationTime);

            if (!state.HasAcceptedPoint)
            {
                StartNewSegment(state, tracePoint);
                return true;
            }

            if (!state.MeaningfullyMoving)
            {
                return false;
            }

            float distance = Vector3.Distance(
                state.LastAcceptedPoint,
                tracePoint);

            if (distance >= teleportBreakDistance)
            {
                StartNewSegment(state, tracePoint);
                return true;
            }

            if (distance < sampleDistance)
            {
                return false;
            }

            AppendPoint(state, tracePoint);
            return true;
        }

        private Vector3 GetActualTracePoint(
            VehicleTraceState state,
            Vector3 current)
        {
            if (state.Kind == VehicleSelectionKind.Usv &&
                flattenUsvActualTraceToInitialHeight)
            {
                return new Vector3(
                    current.x,
                    state.InitialTraceHeight,
                    current.z);
            }

            return current;
        }

        private void UpdateMeaningfulMotion(
            VehicleTraceState state,
            Vector3 current,
            float observationTime)
        {
            Vector3 decisionPosition =
                state.Kind == VehicleSelectionKind.Usv
                    ? new Vector3(current.x, 0f, current.z)
                    : current;

            float deltaTime = 0f;
            if (state.HasObservedPosition)
            {
                deltaTime = Mathf.Max(
                    0f,
                    observationTime - state.LastObservedTime);
            }

            state.MotionSamples.Enqueue(new MotionSample
            {
                Time = observationTime,
                Position = decisionPosition
            });
            while (state.MotionSamples.Count > 1 &&
                   observationTime - state.MotionSamples.Peek().Time >
                   motionWindowSeconds)
            {
                state.MotionSamples.Dequeue();
            }

            MotionSample oldest = state.MotionSamples.Peek();
            float netDisplacement = Vector3.Distance(
                oldest.Position,
                decisionPosition);

            if (!state.MeaningfullyMoving)
            {
                if (netDisplacement >= motionStartDisplacement)
                {
                    state.MeaningfullyMoving = true;
                    state.LowMotionDuration = 0f;
                }
            }
            else if (netDisplacement <= motionStopDisplacement)
            {
                state.LowMotionDuration += deltaTime;
                if (state.LowMotionDuration >= motionStopHoldSeconds)
                {
                    state.MeaningfullyMoving = false;
                    state.LowMotionDuration = 0f;
                }
            }
            else
            {
                state.LowMotionDuration = 0f;
            }

            state.HasObservedPosition = true;
            state.LastObservedPosition = decisionPosition;
            state.LastObservedTime = observationTime;
        }

        private void StartNewSegment(
            VehicleTraceState state,
            Vector3 point)
        {
            TrajectorySegment segment = new TrajectorySegment();
            segment.Points.Add(point);
            state.Segments.AddLast(segment);
            AcceptPoint(state, point);
        }

        private void AppendPoint(
            VehicleTraceState state,
            Vector3 point)
        {
            LinkedListNode<TrajectorySegment> last = state.Segments.Last;
            if (last == null)
            {
                StartNewSegment(state, point);
                return;
            }

            last.Value.Points.Add(point);
            AcceptPoint(state, point);
        }

        private void AcceptPoint(
            VehicleTraceState state,
            Vector3 point)
        {
            state.HasAcceptedPoint = true;
            state.LastAcceptedPoint = point;
            state.TotalPointCount++;
            TrimOldestPoints(state);
        }

        private void TrimOldestPoints(VehicleTraceState state)
        {
            int limit = Mathf.Max(2, maxPointsPerVehicle);
            while (state.TotalPointCount > limit)
            {
                LinkedListNode<TrajectorySegment> first =
                    state.Segments.First;
                if (first == null)
                {
                    state.TotalPointCount = 0;
                    return;
                }

                List<Vector3> points = first.Value.Points;
                if (points.Count > 0)
                {
                    points.RemoveAt(0);
                    state.TotalPointCount =
                        Mathf.Max(0, state.TotalPointCount - 1);
                }

                if (points.Count == 0)
                {
                    state.Segments.RemoveFirst();
                }
            }
        }

        private void BuildPlannedRoute(VehicleTraceState state)
        {
            state.PlannedRouteBuilt = true;
            state.PlannedWorldPoints.Clear();
            state.ValidConfiguredWaypointCount = 0;
            state.HasTarget = false;
            state.PlannedVisualGeometryBuilt = false;
            state.PlannedDashSegments.Clear();

            ActiveRouteSnapshot snapshot =
                state.RouteHost == null
                    ? null
                    : state.RouteHost.ActiveRouteSnapshot;
            if (snapshot != null)
            {
                for (int index = 0; index < snapshot.WaypointCount; index++)
                {
                    Vector3d point = snapshot.GetWaypoint(index);
                    Vector3 worldPoint = new Vector3(
                        (float)point.X,
                        (float)point.Y,
                        (float)point.Z);
                    state.PlannedWorldPoints.Add(worldPoint);
                    state.ValidConfiguredWaypointCount++;
                    state.HasTarget = true;
                    state.TargetWorldPosition = worldPoint;
                }

                state.ObservedRouteVersion = snapshot.RouteVersion;
                return;
            }

            if (routeConfig == null)
            {
                return;
            }

            state.PlannedWorldPoints.Add(state.InitialPosition);
            IReadOnlyList<Vector3> localWaypoints =
                routeConfig.GetLocalWaypoints(state.Kind);
            bool skippedInvalidPoint = false;

            if (localWaypoints != null)
            {
                for (int i = 0; i < localWaypoints.Count; i++)
                {
                    Vector3 localOffset = localWaypoints[i];
                    if (!IsFinite(localOffset))
                    {
                        skippedInvalidPoint = true;
                        continue;
                    }

                    Vector3 worldPoint =
                        state.InitialPosition +
                        state.InitialRotation * localOffset;
                    state.PlannedWorldPoints.Add(worldPoint);
                    state.HasTarget = true;
                    state.TargetWorldPosition = worldPoint;
                }
            }

            state.ValidConfiguredWaypointCount =
                state.PlannedWorldPoints.Count;

            if (skippedInvalidPoint)
            {
                WarnOnce(
                    "InvalidRoutePoint:" + state.Kind,
                    "One or more non-finite planned route points were skipped " +
                    "for " + state.Kind + ".");
            }

            if (!state.HasTarget)
            {
                WarnOnce(
                    "NoValidRoute:" + state.Kind,
                    "No valid planned route waypoint is available for " +
                    state.Kind + ".");
            }
        }

        private bool RefreshActiveRouteIfChanged(VehicleTraceState state)
        {
            if (state.RouteHost == null)
                state.RouteHost = FindRouteHost(state.Kind);
            ActiveRouteSnapshot snapshot = state.RouteHost == null
                ? null
                : state.RouteHost.ActiveRouteSnapshot;
            if (snapshot == null ||
                snapshot.RouteVersion == state.ObservedRouteVersion)
                return false;
            BuildPlannedRoute(state);
            return true;
        }

        private static VehicleDataRuntimeHost FindRouteHost(
            VehicleSelectionKind kind)
        {
            VehicleType expected;
            switch (kind)
            {
                case VehicleSelectionKind.Auv: expected = VehicleType.Auv; break;
                case VehicleSelectionKind.Rov: expected = VehicleType.Rov; break;
                case VehicleSelectionKind.Usv: expected = VehicleType.Usv; break;
                default: return null;
            }

            VehicleDataRuntimeHost[] hosts = FindObjectsByType<
                VehicleDataRuntimeHost>(FindObjectsSortMode.None);
            for (int index = 0; index < hosts.Length; index++)
            {
                if (hosts[index] != null &&
                    hosts[index].IntegrationConfiguration != null &&
                    hosts[index].IntegrationConfiguration.VehicleType == expected)
                    return hosts[index];
            }
            return null;
        }

        private void SubscribeToSelection()
        {
            if (selectionSubscribed || selectionController == null)
            {
                return;
            }

            selectionController.SelectionChanged += OnSelectionChanged;
            selectionSubscribed = true;
            OnSelectionChanged(
                selectionController.SelectedVehicle,
                selectionController.SelectedTransform);
        }

        private void OnSelectionChanged(
            VehicleSelectionKind kind,
            Transform selectedTransform)
        {
            displayedKind = kind;
            if (kind == VehicleSelectionKind.None)
            {
                RefreshTrajectoryVisualVisibility();
                return;
            }

            VehicleTraceState state;
            if (!traceStates.TryGetValue(kind, out state) ||
                state.Vehicle == null)
            {
                HideAllVisuals();
                WarnOnce(
                    "MissingSelectedState:" + kind,
                    "No trajectory state is available for the selected " +
                    kind + " vehicle.");
                return;
            }

            if (selectedTransform != null &&
                selectedTransform != state.Vehicle)
            {
                WarnOnce(
                    "SelectedTransformMismatch:" + kind,
                    "The selected vehicle Transform does not match its " +
                    "trajectory state. Visualization will use the bound state.");
            }

            RefreshTrajectoryVisualVisibility();
        }

        private void RefreshTrajectoryVisualVisibility()
        {
            if (!trajectoryVisualsVisible ||
                displayedKind == VehicleSelectionKind.None)
            {
                HideAllVisuals();
                return;
            }

            ShowVehicleVisuals(displayedKind);
        }

        private void ShowVehicleVisuals(VehicleSelectionKind kind)
        {
            VehicleTraceState state;
            if (!traceStates.TryGetValue(kind, out state))
            {
                HideAllVisuals();
                return;
            }

            EnsureVisualResources();
            if (!visualResourcesAvailable || visualRoot == null)
            {
                HideAllVisuals();
                return;
            }

            visualRoot.gameObject.SetActive(true);
            RefreshActualLines(state);
            RefreshPlannedVisuals(state);
        }

        private void HideAllVisuals()
        {
            DeactivateLinePool(actualLinePool, 0);
            DeactivateLinePool(plannedDashPool, 0);
            DeactivateWaypointPool(0);

            if (targetRingRenderer != null)
            {
                targetRingRenderer.gameObject.SetActive(false);
            }

            if (targetColumnRenderer != null)
            {
                targetColumnRenderer.gameObject.SetActive(false);
            }

            if (visualRoot != null)
            {
                visualRoot.gameObject.SetActive(false);
            }
        }

        private void EnsureVisualResources()
        {
            if (visualResourcesInitialized)
            {
                return;
            }

            visualResourcesInitialized = true;
            GameObject rootObject = new GameObject(VisualRootName);
            rootObject.transform.SetParent(transform, false);
            visualRoot = rootObject.transform;
            rootObject.SetActive(false);

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                WarnOnce(
                    "MissingTrajectoryShader",
                    "Trajectory visualization could not find a compatible " +
                    "unlit shader. Trajectory recording will continue.");
                visualResourcesAvailable = false;
                return;
            }

            actualTrajectoryMaterial = CreateRuntimeMaterial(
                shader,
                "V2 Actual Trajectory (Runtime)",
                routeConfig != null
                    ? routeConfig.ActualLineColor
                    : FallbackActualLineColor);
            plannedTrajectoryMaterial = CreateRuntimeMaterial(
                shader,
                "V2 Planned Trajectory (Runtime)",
                routeConfig != null
                    ? routeConfig.PlannedLineColor
                    : FallbackPlannedLineColor);
            waypointMaterial = CreateRuntimeMaterial(
                shader,
                "V2 Planned Waypoint (Runtime)",
                routeConfig != null
                    ? routeConfig.WaypointColor
                    : FallbackWaypointColor);
            targetRingMaterial = CreateRuntimeMaterial(
                shader,
                "V2 Target Ring (Runtime)",
                routeConfig != null
                    ? routeConfig.TargetRingColor
                    : FallbackTargetColor);
            targetColumnMaterial = CreateRuntimeMaterial(
                shader,
                "V2 Target Column (Runtime)",
                routeConfig != null
                    ? routeConfig.TargetColumnColor
                    : FallbackTargetColor);
            visualResourcesAvailable = true;
        }

        private static Material CreateRuntimeMaterial(
            Shader shader,
            string materialName,
            Color color)
        {
            Material material = new Material(shader)
            {
                name = materialName,
                color = color
            };
            return material;
        }

        private void RefreshActualLines(VehicleTraceState state)
        {
            if (!visualResourcesAvailable)
            {
                return;
            }

            Color color = routeConfig != null
                ? routeConfig.ActualLineColor
                : FallbackActualLineColor;
            float width = routeConfig != null
                ? Mathf.Max(0.001f, routeConfig.ActualLineWidth)
                : FallbackActualLineWidth;
            int activeCount = 0;

            foreach (TrajectorySegment segment in state.Segments)
            {
                if (segment.Points.Count < 2)
                {
                    continue;
                }

                LineRenderer line = GetActualLine(activeCount++);
                ConfigureLine(
                    line,
                    actualTrajectoryMaterial,
                    color,
                    width,
                    false);
                line.positionCount = segment.Points.Count;
                for (int i = 0; i < segment.Points.Count; i++)
                {
                    line.SetPosition(i, segment.Points[i]);
                }

                line.gameObject.SetActive(true);
            }

            DeactivateLinePool(actualLinePool, activeCount);
        }

        private void RefreshPlannedVisuals(VehicleTraceState state)
        {
            if (!visualResourcesAvailable ||
                routeConfig == null ||
                !state.HasTarget)
            {
                DeactivateLinePool(plannedDashPool, 0);
                DeactivateWaypointPool(0);
                HideTarget();
                return;
            }

            EnsurePlannedVisualGeometry(state);
            int dashCount = 0;
            if (state.ValidConfiguredWaypointCount >= 2)
            {
                Color plannedColor = routeConfig.PlannedLineColor;
                float plannedWidth = Mathf.Max(
                    0.001f,
                    routeConfig.PlannedLineWidth);

                for (int i = 0; i < state.PlannedDashSegments.Count; i++)
                {
                    DashSegment dash = state.PlannedDashSegments[i];
                    if ((dash.End - dash.Start).sqrMagnitude <=
                        GeometryEpsilon * GeometryEpsilon)
                    {
                        continue;
                    }

                    LineRenderer line = GetPlannedDashLine(dashCount++);
                    ConfigureLine(
                        line,
                        plannedTrajectoryMaterial,
                        plannedColor,
                        plannedWidth,
                        false);
                    line.positionCount = 2;
                    line.SetPosition(0, dash.Start);
                    line.SetPosition(1, dash.End);
                    line.gameObject.SetActive(true);
                }
            }

            DeactivateLinePool(plannedDashPool, dashCount);
            RefreshWaypoints(state);
            RefreshTarget(state);
        }

        private void EnsurePlannedVisualGeometry(VehicleTraceState state)
        {
            if (state.PlannedVisualGeometryBuilt)
            {
                return;
            }

            state.PlannedVisualGeometryBuilt = true;
            state.PlannedDashSegments.Clear();
            if (routeConfig == null ||
                state.ValidConfiguredWaypointCount < 2)
            {
                return;
            }

            BuildDashSegments(
                state.PlannedWorldPoints,
                Mathf.Max(0.02f, routeConfig.PlannedDashLength),
                Mathf.Max(0.02f, routeConfig.PlannedDashGap),
                state.PlannedDashSegments);
        }

        private static void BuildDashSegments(
            IReadOnlyList<Vector3> polyline,
            float dashLength,
            float gapLength,
            List<DashSegment> output)
        {
            output.Clear();
            if (polyline == null || polyline.Count < 2)
            {
                return;
            }

            dashLength = Mathf.Max(GeometryEpsilon, dashLength);
            gapLength = Mathf.Max(GeometryEpsilon, gapLength);
            bool drawingDash = true;
            float remainingInPatternPart = dashLength;

            for (int edgeIndex = 0;
                 edgeIndex < polyline.Count - 1;
                 edgeIndex++)
            {
                Vector3 edgeStart = polyline[edgeIndex];
                Vector3 edgeEnd = polyline[edgeIndex + 1];
                Vector3 edge = edgeEnd - edgeStart;
                float edgeLength = edge.magnitude;
                if (edgeLength <= GeometryEpsilon)
                {
                    continue;
                }

                Vector3 direction = edge / edgeLength;
                float distanceAlongEdge = 0f;
                while (distanceAlongEdge < edgeLength - GeometryEpsilon)
                {
                    float edgeRemaining = edgeLength - distanceAlongEdge;
                    float step = Mathf.Min(
                        remainingInPatternPart,
                        edgeRemaining);
                    if (step <= GeometryEpsilon)
                    {
                        break;
                    }

                    if (drawingDash)
                    {
                        Vector3 start =
                            edgeStart + direction * distanceAlongEdge;
                        Vector3 end =
                            edgeStart +
                            direction * (distanceAlongEdge + step);
                        if ((end - start).sqrMagnitude >
                            GeometryEpsilon * GeometryEpsilon)
                        {
                            output.Add(new DashSegment
                            {
                                Start = start,
                                End = end
                            });
                        }
                    }

                    distanceAlongEdge += step;
                    remainingInPatternPart -= step;
                    if (remainingInPatternPart <= GeometryEpsilon)
                    {
                        drawingDash = !drawingDash;
                        remainingInPatternPart =
                            drawingDash ? dashLength : gapLength;
                    }
                }
            }
        }

        private void RefreshWaypoints(VehicleTraceState state)
        {
            int ordinaryWaypointCount = Mathf.Max(
                0,
                state.ValidConfiguredWaypointCount - 2);
            Color color = routeConfig.WaypointColor;
            float diameter = Mathf.Max(
                0.01f,
                routeConfig.WaypointDiameter);

            for (int i = 0; i < ordinaryWaypointCount; i++)
            {
                GameObject waypoint = GetWaypoint(i);
                waypoint.transform.position =
                    state.PlannedWorldPoints[i + 1];
                waypoint.transform.rotation = Quaternion.identity;
                waypoint.transform.localScale =
                    Vector3.one * diameter;

                Renderer renderer = waypoint.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = waypointMaterial;
                }

                SetMaterialColor(waypointMaterial, color);
                waypoint.SetActive(true);
            }

            DeactivateWaypointPool(ordinaryWaypointCount);
        }

        private void RefreshTarget(VehicleTraceState state)
        {
            EnsureTargetObjects();
            if (targetRingRenderer == null ||
                targetColumnRenderer == null)
            {
                HideTarget();
                return;
            }

            Vector3 center = state.TargetWorldPosition;
            float ringRadius = Mathf.Max(
                0.05f,
                routeConfig.TargetRingDiameter) * 0.5f;
            float ringWidth = Mathf.Max(
                0.001f,
                routeConfig.TargetRingWidth);
            Color ringColor = routeConfig.TargetRingColor;
            ConfigureLine(
                targetRingRenderer,
                targetRingMaterial,
                ringColor,
                ringWidth,
                true);
            targetRingRenderer.positionCount = TargetRingSegmentCount;
            for (int i = 0; i < TargetRingSegmentCount; i++)
            {
                float angle =
                    i * Mathf.PI * 2f / TargetRingSegmentCount;
                targetRingRenderer.SetPosition(
                    i,
                    center +
                    new Vector3(
                        Mathf.Cos(angle) * ringRadius,
                        0f,
                        Mathf.Sin(angle) * ringRadius));
            }

            float columnHeight = Mathf.Max(
                0.05f,
                routeConfig.TargetColumnHeight);
            float columnWidth = Mathf.Max(
                0.001f,
                routeConfig.TargetColumnWidth);
            Color columnColor = routeConfig.TargetColumnColor;
            ConfigureLine(
                targetColumnRenderer,
                targetColumnMaterial,
                columnColor,
                columnWidth,
                false);
            targetColumnRenderer.positionCount = 2;
            targetColumnRenderer.SetPosition(0, center);
            targetColumnRenderer.SetPosition(
                1,
                center + Vector3.up * columnHeight);

            targetRingRenderer.gameObject.SetActive(true);
            targetColumnRenderer.gameObject.SetActive(true);
        }

        private void HideTarget()
        {
            if (targetRingRenderer != null)
            {
                targetRingRenderer.gameObject.SetActive(false);
            }

            if (targetColumnRenderer != null)
            {
                targetColumnRenderer.gameObject.SetActive(false);
            }
        }

        private LineRenderer GetActualLine(int index)
        {
            while (actualLinePool.Count <= index)
            {
                actualLinePool.Add(CreatePooledLine(
                    "ActualLine_" + actualLinePool.Count,
                    actualTrajectoryMaterial));
            }

            return actualLinePool[index];
        }

        private LineRenderer GetPlannedDashLine(int index)
        {
            while (plannedDashPool.Count <= index)
            {
                plannedDashPool.Add(CreatePooledLine(
                    "PlannedDash_" + plannedDashPool.Count,
                    plannedTrajectoryMaterial));
            }

            return plannedDashPool[index];
        }

        private GameObject GetWaypoint(int index)
        {
            while (waypointPool.Count <= index)
            {
                GameObject waypoint = GameObject.CreatePrimitive(
                    PrimitiveType.Sphere);
                waypoint.name = "Waypoint_" + waypointPool.Count;
                waypoint.transform.SetParent(visualRoot, false);

                Component primitiveCollider =
                    waypoint.GetComponent("SphereCollider");
                if (primitiveCollider != null)
                {
                    DestroyRuntimeObject(primitiveCollider);
                }

                Renderer renderer = waypoint.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = waypointMaterial;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.motionVectorGenerationMode =
                        MotionVectorGenerationMode.ForceNoMotion;
                }

                waypoint.SetActive(false);
                waypointPool.Add(waypoint);
            }

            return waypointPool[index];
        }

        private void EnsureTargetObjects()
        {
            if (targetRingRenderer == null)
            {
                targetRingRenderer = CreatePooledLine(
                    "TargetRing",
                    targetRingMaterial);
            }

            if (targetColumnRenderer == null)
            {
                targetColumnRenderer = CreatePooledLine(
                    "TargetColumn",
                    targetColumnMaterial);
            }
        }

        private LineRenderer CreatePooledLine(
            string objectName,
            Material material)
        {
            GameObject lineObject = new GameObject(objectName);
            lineObject.transform.SetParent(visualRoot, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            line.sharedMaterial = material;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            lineObject.SetActive(false);
            return line;
        }

        private static void ConfigureLine(
            LineRenderer line,
            Material material,
            Color color,
            float width,
            bool loop)
        {
            line.useWorldSpace = true;
            line.loop = loop;
            line.sharedMaterial = material;
            line.startColor = color;
            line.endColor = color;
            line.startWidth = width;
            line.endWidth = width;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            SetMaterialColor(material, color);
        }

        private static void SetMaterialColor(
            Material material,
            Color color)
        {
            if (material != null)
            {
                material.color = color;
            }
        }

        private static void DeactivateLinePool(
            List<LineRenderer> pool,
            int activeCount)
        {
            for (int i = activeCount; i < pool.Count; i++)
            {
                if (pool[i] != null)
                {
                    pool[i].gameObject.SetActive(false);
                }
            }
        }

        private void DeactivateWaypointPool(int activeCount)
        {
            for (int i = activeCount; i < waypointPool.Count; i++)
            {
                if (waypointPool[i] != null)
                {
                    waypointPool[i].SetActive(false);
                }
            }
        }

        private void OnDestroy()
        {
            if (selectionSubscribed && selectionController != null)
            {
                selectionController.SelectionChanged -= OnSelectionChanged;
            }

            selectionSubscribed = false;
            DestroyRuntimeObject(
                visualRoot != null ? visualRoot.gameObject : null);
            DestroyRuntimeObject(actualTrajectoryMaterial);
            DestroyRuntimeObject(plannedTrajectoryMaterial);
            DestroyRuntimeObject(waypointMaterial);
            DestroyRuntimeObject(targetRingMaterial);
            DestroyRuntimeObject(targetColumnMaterial);
        }

        private static void DestroyRuntimeObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private void WarnOnce(string key, string message)
        {
            if (warningKeys.Add(key))
            {
                Debug.LogWarning(message, this);
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) &&
                   !float.IsInfinity(value.z);
        }
    }
}
