using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnderwaterRobotScene.Visualization.Runtime
{
    public enum VehicleSelectionKind
    {
        None = 0,
        Auv = 1,
        Rov = 2,
        Usv = 3
    }

    [DisallowMultipleComponent]
    public sealed class VehicleSelectionCameraController : MonoBehaviour
    {
        private const string AuvRootName = "AUV_Yellow_Underwater";
        private const string RovRootName = "ROV_Box_Seabed";
        private const string UsvRootName = "USV_Blue_Surface";
        private const float DefaultPitch = 22f;
        private const float DefaultObliqueYawOffset = 32f;
        private const float AbsoluteMinimumDistance = 1.5f;
        private const float AbsoluteMaximumDistance = 18f;
        private const float MinimumOrbitPitch = 8f;
        private const float MaximumOrbitPitch = 68f;
        private const float MinimumTerrainClearance = 0.35f;
        private const float VehicleFocusPadding = 0.65f;
        private const float EnclosureEdgeMargin = 1.5f;
        private const float RuntimeNearClip = 0.15f;

        private sealed class VehicleTarget
        {
            public VehicleSelectionKind Kind;
            public Transform Root;
            public Vector3 LocalForward;
            public Renderer[] RingRenderers;
        }

        [SerializeField, Range(1.15f, 1.30f)]
        [Tooltip("Core ring diameter relative to the selected vehicle's largest horizontal model size.")]
        private float ringSizeMultiplier = 1.18f;

        [SerializeField, Range(1.05f, 1.10f)]
        [Tooltip("Glow radius relative to the core ring radius.")]
        private float ringGlowScale = 1.05f;

        [SerializeField, Range(0.72f, 0.82f)]
        [Tooltip("AUV-only core ring diameter multiplier for its long, narrow hull.")]
        private float auvRingSizeMultiplier = 0.75f;

        private readonly List<VehicleTarget> targets = new List<VehicleTarget>(3);
        private Camera targetCamera;
        private VehicleTarget selectedTarget;
        private GameObject ringRoot;
        private Material ringGlowMaterial;
        private Material ringCoreMaterial;
        private Vector3 globalPosition;
        private Quaternion globalRotation;
        private float globalFieldOfView;
        private Vector3 cameraVelocity;
        private float orbitYaw;
        private float orbitPitch = DefaultPitch;
        private float orbitDistance = 4f;
        private float minDistance = 1.25f;
        private float maxDistance = 10f;
        private Renderer terrainRenderer;
        private Renderer enclosureRenderer;
        private Vector3 overviewFocus;
        private bool initialized;
        private bool following;
        private bool restoringGlobal;

        public VehicleSelectionKind SelectedVehicle
        {
            get { return selectedTarget != null ? selectedTarget.Kind : VehicleSelectionKind.None; }
        }

        public Transform SelectedTransform
        {
            get { return selectedTarget != null ? selectedTarget.Root : null; }
        }

        public bool IsFollowing
        {
            get { return following; }
        }

        public bool RingVisible
        {
            get { return ringRoot != null && ringRoot.activeSelf; }
        }

        public Camera TargetCamera
        {
            get { return targetCamera; }
        }

        public float RingSizeMultiplier
        {
            get { return ringSizeMultiplier; }
        }

        public float AuvRingSizeMultiplier
        {
            get { return auvRingSizeMultiplier; }
        }

        public float CurrentRingDiameter { get; private set; }

        public float CurrentRingOuterDiameter { get; private set; }

        public float MinimumDistance => AbsoluteMinimumDistance;

        public float MaximumDistance => AbsoluteMaximumDistance;

        public float MinimumPitch => MinimumOrbitPitch;

        public float MaximumPitch => MaximumOrbitPitch;

        public float TerrainClearance => MinimumTerrainClearance;

        public float FocusPadding => VehicleFocusPadding;

        public float CurrentMinimumDistance => minDistance;

        public float CurrentMaximumDistance => maxDistance;

        public float CurrentOrbitDistance => orbitDistance;

        public float CurrentOrbitPitch => orbitPitch;

        public float CurrentOrbitYaw => orbitYaw;

        public Vector3 OverviewPosition => globalPosition;

        public event Action<VehicleSelectionKind, Transform> SelectionChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            Camera mainCamera = Camera.main;
            GameObject auv = GameObject.Find(AuvRootName);
            GameObject rov = GameObject.Find(RovRootName);
            GameObject usv = GameObject.Find(UsvRootName);

            if (mainCamera == null || auv == null || rov == null || usv == null)
            {
                return;
            }

            VehicleSelectionCameraController controller =
                mainCamera.GetComponent<VehicleSelectionCameraController>();
            if (controller == null)
            {
                controller = mainCamera.gameObject.AddComponent<VehicleSelectionCameraController>();
            }

            controller.Initialize(mainCamera, auv.transform, rov.transform, usv.transform);
        }

        public void Initialize(Camera cameraToControl, Transform auv, Transform rov, Transform usv)
        {
            if (initialized || cameraToControl == null || auv == null || rov == null || usv == null)
            {
                return;
            }

            targetCamera = cameraToControl;
            globalPosition = targetCamera.transform.position;
            globalRotation = targetCamera.transform.rotation;
            globalFieldOfView = targetCamera.fieldOfView;
            targetCamera.nearClipPlane =
                Mathf.Min(targetCamera.nearClipPlane, RuntimeNearClip);

            targets.Add(new VehicleTarget
            {
                Kind = VehicleSelectionKind.Auv,
                Root = auv,
                LocalForward = Vector3.right,
                RingRenderers = CollectRingRenderers(auv)
            });
            targets.Add(new VehicleTarget
            {
                Kind = VehicleSelectionKind.Rov,
                Root = rov,
                LocalForward = Vector3.right,
                RingRenderers = CollectRingRenderers(rov)
            });
            targets.Add(new VehicleTarget
            {
                Kind = VehicleSelectionKind.Usv,
                Root = usv,
                LocalForward = Vector3.left,
                RingRenderers = CollectRingRenderers(usv)
            });

            CacheSafetyGeometry();
            ConfigureOverview();
            targetCamera.transform.position = globalPosition;
            targetCamera.transform.rotation = globalRotation;
            CreateSelectionRing();
            initialized = true;
        }

        private void Update()
        {
            if (!initialized)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            {
                SelectVehicle(VehicleSelectionKind.Auv);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            {
                SelectVehicle(VehicleSelectionKind.Rov);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
            {
                SelectVehicle(VehicleSelectionKind.Usv);
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (FindFirstObjectByType<VehicleRouteEditingController>() == null)
                    CancelSelection();
            }
            else if (Input.GetKeyDown(KeyCode.F))
            {
                ToggleFollow();
            }

            if (Input.GetMouseButtonDown(0) &&
                RouteEditingInputContext.SelectionMayConsumePrimaryPointer())
            {
                TrySelectFromScreenPoint(Input.mousePosition);
            }

            if (!following)
            {
                return;
            }

            if (Input.GetMouseButton(1))
            {
                ApplyOrbitInput(
                    Input.GetAxisRaw("Mouse X") * 4f,
                    -Input.GetAxisRaw("Mouse Y") * 3f);
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.001f)
            {
                ApplyZoomInput(scroll);
            }
        }

        private void LateUpdate()
        {
            if (!initialized)
            {
                return;
            }

            UpdateSelectionRing();

            if (following && selectedTarget != null)
            {
                UpdateFollowCamera();
            }
            else if (restoringGlobal)
            {
                UpdateGlobalRestore();
            }
        }

        public void SelectVehicle(VehicleSelectionKind kind)
        {
            if (!initialized || kind == VehicleSelectionKind.None)
            {
                CancelSelection();
                return;
            }

            VehicleTarget target = FindTarget(kind);
            if (target == null)
            {
                return;
            }

            bool changedTarget = selectedTarget != target;
            selectedTarget = target;
            if (ringRoot != null)
            {
                ringRoot.SetActive(true);
            }

            BeginFollow(changedTarget || !following);

            if (changedTarget)
            {
                SelectionChanged?.Invoke(target.Kind, target.Root);
            }
        }

        public void CancelSelection()
        {
            bool hadSelection = selectedTarget != null;
            selectedTarget = null;
            CurrentRingDiameter = 0f;
            CurrentRingOuterDiameter = 0f;
            if (ringRoot != null)
            {
                ringRoot.SetActive(false);
            }

            ExitFollow();

            if (hadSelection)
            {
                SelectionChanged?.Invoke(VehicleSelectionKind.None, null);
            }
        }

        public bool TryGetVehicleTransform(
            VehicleSelectionKind kind,
            out Transform vehicle)
        {
            VehicleTarget target = FindTarget(kind);
            vehicle = target != null ? target.Root : null;
            return vehicle != null;
        }

        public void ToggleFollow()
        {
            if (selectedTarget == null)
            {
                return;
            }

            if (following)
            {
                ExitFollow();
            }
            else
            {
                BeginFollow(false);
            }
        }

        public void ExitFollow()
        {
            following = false;
            restoringGlobal = true;
            cameraVelocity = Vector3.zero;
        }

        public void ApplyOrbitInput(float yawDelta, float pitchDelta)
        {
            if (!IsFinite(yawDelta) || !IsFinite(pitchDelta))
            {
                return;
            }

            orbitYaw = Mathf.Repeat(orbitYaw + yawDelta, 360f);
            orbitPitch = Mathf.Clamp(
                orbitPitch + pitchDelta,
                MinimumOrbitPitch,
                MaximumOrbitPitch);
        }

        public void ApplyZoomInput(float scrollDelta)
        {
            if (!IsFinite(scrollDelta))
            {
                return;
            }

            orbitDistance = Mathf.Clamp(
                orbitDistance - scrollDelta * 0.55f,
                minDistance,
                maxDistance);
        }

        public void SetOrbitState(float yaw, float pitch, float distance)
        {
            if (!IsFinite(yaw) || !IsFinite(pitch) || !IsFinite(distance))
            {
                return;
            }

            orbitYaw = Mathf.Repeat(yaw, 360f);
            orbitPitch = Mathf.Clamp(
                pitch,
                MinimumOrbitPitch,
                MaximumOrbitPitch);
            orbitDistance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        public void SnapToCurrentView()
        {
            if (!initialized || targetCamera == null)
            {
                return;
            }

            cameraVelocity = Vector3.zero;
            if (following && selectedTarget != null)
            {
                Vector3 position;
                Quaternion rotation;
                if (TryCalculateFollowPose(out position, out rotation))
                {
                    targetCamera.transform.SetPositionAndRotation(position, rotation);
                    targetCamera.fieldOfView = globalFieldOfView;
                }
                return;
            }

            targetCamera.transform.SetPositionAndRotation(
                globalPosition,
                globalRotation);
            targetCamera.fieldOfView = globalFieldOfView;
            restoringGlobal = false;
        }

        public bool IsCameraPoseSafe(out string reason)
        {
            reason = string.Empty;
            if (!initialized || targetCamera == null)
            {
                reason = "Controller is not initialized.";
                return false;
            }

            Transform cameraTransform = targetCamera.transform;
            if (!IsFinite(cameraTransform.position) ||
                !IsFinite(cameraTransform.rotation))
            {
                reason = "Camera pose contains a non-finite value.";
                return false;
            }

            float terrainHeight;
            if (TryGetTerrainHeight(cameraTransform.position, out terrainHeight) &&
                cameraTransform.position.y <
                terrainHeight + MinimumTerrainClearance - 0.001f)
            {
                reason = "Camera is below terrain clearance.";
                return false;
            }

            if (!IsInsideEnclosure(cameraTransform.position))
            {
                reason = "Camera is outside the distant-enclosure viewing range.";
                return false;
            }

            if (selectedTarget != null)
            {
                Bounds bounds;
                if (TryGetTargetBounds(selectedTarget, out bounds))
                {
                    bounds.Expand(VehicleFocusPadding * 2f);
                    if (bounds.Contains(cameraTransform.position))
                    {
                        reason = "Camera is inside the selected vehicle bounds.";
                        return false;
                    }
                }
            }

            return true;
        }

        public bool TryGetTerrainHeight(
            Vector3 worldPosition,
            out float terrainHeight)
        {
            terrainHeight = 0f;
            if (terrainRenderer == null ||
                !terrainRenderer.enabled ||
                !terrainRenderer.gameObject.activeInHierarchy)
            {
                return false;
            }

            Bounds bounds = terrainRenderer.bounds;
            if (worldPosition.x < bounds.min.x ||
                worldPosition.x > bounds.max.x ||
                worldPosition.z < bounds.min.z ||
                worldPosition.z > bounds.max.z)
            {
                return false;
            }

            // Use a conservative physics-free clearance plane so the
            // controller stays compatible with the lean built-in module set.
            terrainHeight = bounds.max.y;
            return true;
        }

        public bool TrySelectFromScreenPoint(Vector2 screenPoint)
        {
            if (!initialized || targetCamera == null)
            {
                return false;
            }

            return TrySelectFromRay(targetCamera.ScreenPointToRay(screenPoint));
        }

        public bool TrySelectFromRay(Ray ray)
        {
            VehicleTarget nearest = null;
            float nearestDistance = float.PositiveInfinity;

            for (int i = 0; i < targets.Count; i++)
            {
                float distance;
                if (TryIntersectTarget(ray, targets[i], out distance) && distance < nearestDistance)
                {
                    nearest = targets[i];
                    nearestDistance = distance;
                }
            }

            if (nearest == null)
            {
                CancelSelection();
                return false;
            }

            SelectVehicle(nearest.Kind);
            return true;
        }

        private void BeginFollow(bool resetOrbit)
        {
            if (selectedTarget == null)
            {
                return;
            }

            Bounds bounds;
            if (!TryGetTargetBounds(selectedTarget, out bounds))
            {
                return;
            }

            if (resetOrbit)
            {
                Vector3 visualForward =
                    selectedTarget.Root.TransformDirection(selectedTarget.LocalForward).normalized;
                orbitYaw = Mathf.Repeat(
                    Mathf.Atan2(visualForward.x, visualForward.z) *
                    Mathf.Rad2Deg + DefaultObliqueYawOffset,
                    360f);
                orbitPitch = DefaultPitch;

                float targetRadius = Mathf.Max(0.5f, bounds.extents.magnitude);
                float fitDistance = CalculateFitDistance(bounds);
                minDistance = Mathf.Clamp(
                    targetRadius + VehicleFocusPadding +
                    targetCamera.nearClipPlane,
                    AbsoluteMinimumDistance,
                    AbsoluteMaximumDistance - 1f);
                maxDistance = Mathf.Clamp(
                    Mathf.Max(minDistance + 2f, fitDistance * 2.25f),
                    minDistance + 0.5f,
                    AbsoluteMaximumDistance);
                orbitDistance = Mathf.Clamp(
                    fitDistance * 1.08f,
                    minDistance,
                    maxDistance);
            }
            else
            {
                orbitPitch = Mathf.Clamp(
                    orbitPitch,
                    MinimumOrbitPitch,
                    MaximumOrbitPitch);
                orbitDistance = Mathf.Clamp(
                    orbitDistance,
                    minDistance,
                    maxDistance);
            }

            following = true;
            restoringGlobal = false;
            cameraVelocity = Vector3.zero;
        }

        private void UpdateFollowCamera()
        {
            Vector3 desiredPosition;
            Quaternion desiredRotation;
            if (!TryCalculateFollowPose(
                    out desiredPosition,
                    out desiredRotation))
            {
                CancelSelection();
                return;
            }

            float positionSmoothTime = 0.18f;
            float rotationBlend = 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime);
            Vector3 currentPosition = IsFinite(targetCamera.transform.position)
                ? targetCamera.transform.position
                : desiredPosition;
            Vector3 smoothedPosition = Vector3.SmoothDamp(
                currentPosition,
                desiredPosition,
                ref cameraVelocity,
                positionSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);
            Bounds selectedBounds;
            TryGetTargetBounds(selectedTarget, out selectedBounds);
            Vector3 focusPoint = FocusPoint(selectedBounds);
            smoothedPosition = MakeSafePosition(
                smoothedPosition,
                selectedBounds,
                focusPoint,
                minDistance);
            Quaternion smoothedLook = LookAt(focusPoint, smoothedPosition);

            targetCamera.transform.position = smoothedPosition;
            targetCamera.transform.rotation = Quaternion.Slerp(
                IsFinite(targetCamera.transform.rotation)
                    ? targetCamera.transform.rotation
                    : desiredRotation,
                smoothedLook,
                rotationBlend);
            targetCamera.fieldOfView = Mathf.Lerp(
                targetCamera.fieldOfView,
                globalFieldOfView,
                rotationBlend);
        }

        private void UpdateGlobalRestore()
        {
            float rotationBlend = 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime);
            if (!IsFinite(targetCamera.transform.position) ||
                !IsFinite(targetCamera.transform.rotation))
            {
                targetCamera.transform.SetPositionAndRotation(
                    globalPosition,
                    globalRotation);
            }
            targetCamera.transform.position = Vector3.SmoothDamp(
                targetCamera.transform.position,
                globalPosition,
                ref cameraVelocity,
                0.18f,
                Mathf.Infinity,
                Time.unscaledDeltaTime);
            targetCamera.transform.rotation = Quaternion.Slerp(
                targetCamera.transform.rotation,
                globalRotation,
                rotationBlend);
            targetCamera.fieldOfView = Mathf.Lerp(
                targetCamera.fieldOfView,
                globalFieldOfView,
                rotationBlend);

            if ((targetCamera.transform.position - globalPosition).sqrMagnitude < 0.000001f &&
                Quaternion.Angle(targetCamera.transform.rotation, globalRotation) < 0.02f &&
                Mathf.Abs(targetCamera.fieldOfView - globalFieldOfView) < 0.01f)
            {
                targetCamera.transform.position = globalPosition;
                targetCamera.transform.rotation = globalRotation;
                targetCamera.fieldOfView = globalFieldOfView;
                restoringGlobal = false;
                cameraVelocity = Vector3.zero;
            }
        }

        private bool TryCalculateFollowPose(
            out Vector3 desiredPosition,
            out Quaternion desiredRotation)
        {
            desiredPosition = globalPosition;
            desiredRotation = globalRotation;
            Bounds bounds;
            if (selectedTarget == null ||
                !TryGetTargetBounds(selectedTarget, out bounds))
            {
                return false;
            }

            orbitYaw = IsFinite(orbitYaw)
                ? Mathf.Repeat(orbitYaw, 360f)
                : 0f;
            orbitPitch = IsFinite(orbitPitch)
                ? Mathf.Clamp(
                    orbitPitch,
                    MinimumOrbitPitch,
                    MaximumOrbitPitch)
                : DefaultPitch;
            orbitDistance = IsFinite(orbitDistance)
                ? Mathf.Clamp(orbitDistance, minDistance, maxDistance)
                : minDistance;

            Vector3 focusPoint = FocusPoint(bounds);
            Quaternion orbitRotation =
                Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            desiredPosition = focusPoint -
                orbitRotation * Vector3.forward * orbitDistance;
            desiredPosition = MakeSafePosition(
                desiredPosition,
                bounds,
                focusPoint,
                minDistance);
            desiredRotation = LookAt(focusPoint, desiredPosition);
            return true;
        }

        private void CacheSafetyGeometry()
        {
            GameObject seabed = GameObject.Find("Seabed");
            if (seabed != null)
            {
                terrainRenderer =
                    seabed.GetComponentInChildren<Renderer>(true);
            }

            GameObject enclosure = GameObject.Find("Continuous_Enclosure");
            if (enclosure != null)
            {
                enclosureRenderer =
                    enclosure.GetComponentInChildren<Renderer>(true);
            }
        }

        private void ConfigureOverview()
        {
            Bounds bounds;
            bool hasBounds = false;
            bounds = default(Bounds);
            for (int i = 0; i < targets.Count; i++)
            {
                Bounds targetBounds;
                if (!TryGetTargetBounds(targets[i], out targetBounds))
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = targetBounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(targetBounds);
                }
            }

            if (!hasBounds)
            {
                return;
            }

            overviewFocus = bounds.center;
            Vector3 euler = globalRotation.eulerAngles;
            float pitch = euler.x > 180f ? euler.x - 360f : euler.x;
            pitch = Mathf.Clamp(
                pitch,
                MinimumOrbitPitch,
                MaximumOrbitPitch);
            Quaternion overviewRotation =
                Quaternion.Euler(pitch, euler.y, 0f);
            float overviewDistance = Mathf.Clamp(
                CalculateFitDistance(bounds) * 1.08f,
                AbsoluteMinimumDistance,
                AbsoluteMaximumDistance);
            globalPosition = overviewFocus -
                overviewRotation * Vector3.forward * overviewDistance;
            globalPosition = MakeSafePosition(
                globalPosition,
                bounds,
                overviewFocus,
                Mathf.Min(
                    overviewDistance,
                    bounds.extents.magnitude + VehicleFocusPadding));
            globalRotation = LookAt(overviewFocus, globalPosition);
        }

        private float CalculateFitDistance(Bounds bounds)
        {
            float verticalFieldOfView =
                Mathf.Clamp(globalFieldOfView, 20f, 100f) * Mathf.Deg2Rad;
            float aspect = targetCamera != null
                ? Mathf.Max(0.5f, targetCamera.aspect)
                : 16f / 9f;
            float horizontalFieldOfView = 2f * Mathf.Atan(
                Mathf.Tan(verticalFieldOfView * 0.5f) * aspect);
            float limitingHalfAngle =
                Mathf.Min(verticalFieldOfView, horizontalFieldOfView) * 0.5f;
            float radius = Mathf.Max(0.5f, bounds.extents.magnitude) +
                VehicleFocusPadding;
            return radius / Mathf.Max(0.15f, Mathf.Sin(limitingHalfAngle));
        }

        private Vector3 MakeSafePosition(
            Vector3 proposed,
            Bounds protectedBounds,
            Vector3 focusPoint,
            float requiredDistance)
        {
            if (!IsFinite(proposed))
            {
                proposed = focusPoint -
                    Quaternion.Euler(
                        DefaultPitch,
                        orbitYaw,
                        0f) * Vector3.forward *
                    Mathf.Max(AbsoluteMinimumDistance, requiredDistance);
            }

            Bounds paddedBounds = protectedBounds;
            paddedBounds.Expand(VehicleFocusPadding * 2f);
            float safeDistance = Mathf.Max(
                requiredDistance,
                paddedBounds.extents.magnitude +
                targetCamera.nearClipPlane);
            for (int iteration = 0; iteration < 8; iteration++)
            {
                Vector3 away = proposed - focusPoint;
                if (!IsFinite(away) || away.sqrMagnitude < 0.0001f)
                {
                    away = -Vector3.forward;
                }

                if (away.magnitude < safeDistance ||
                    paddedBounds.Contains(proposed))
                {
                    proposed =
                        focusPoint + away.normalized * safeDistance;
                }

                proposed = ClampInsideEnclosure(proposed);
                float terrainHeight;
                if (TryGetTerrainHeight(
                        proposed,
                        out terrainHeight))
                {
                    proposed.y = Mathf.Max(
                        proposed.y,
                        terrainHeight + MinimumTerrainClearance);
                }

                if (SatisfiesSafety(proposed, paddedBounds))
                {
                    return proposed;
                }
            }

            Vector3 fallbackDirection =
                Quaternion.Euler(
                    MaximumOrbitPitch,
                    orbitYaw,
                    0f) * -Vector3.forward;
            proposed = focusPoint +
                fallbackDirection.normalized * safeDistance;
            for (int iteration = 0; iteration < 8; iteration++)
            {
                proposed = ClampInsideEnclosure(proposed);
                float terrainHeight;
                if (TryGetTerrainHeight(
                        proposed,
                        out terrainHeight))
                {
                    proposed.y = Mathf.Max(
                        proposed.y,
                        terrainHeight + MinimumTerrainClearance);
                }
                if (!paddedBounds.Contains(proposed) &&
                    SatisfiesSafety(proposed, paddedBounds))
                {
                    return proposed;
                }
                proposed.y += safeDistance * 0.25f;
            }

            return proposed;
        }

        private bool SatisfiesSafety(
            Vector3 position,
            Bounds paddedBounds)
        {
            if (!IsFinite(position) ||
                paddedBounds.Contains(position) ||
                !IsInsideEnclosure(position))
            {
                return false;
            }

            float terrainHeight;
            return !TryGetTerrainHeight(position, out terrainHeight) ||
                   position.y >=
                   terrainHeight + MinimumTerrainClearance - 0.001f;
        }

        private Vector3 ClampInsideEnclosure(Vector3 position)
        {
            if (enclosureRenderer == null)
            {
                return position;
            }

            Bounds bounds = enclosureRenderer.bounds;
            Vector2 center = new Vector2(bounds.center.x, bounds.center.z);
            Vector2 horizontal = new Vector2(position.x, position.z);
            Vector2 offset = horizontal - center;
            float safeRadius = Mathf.Max(
                AbsoluteMinimumDistance,
                Mathf.Min(bounds.extents.x, bounds.extents.z) -
                EnclosureEdgeMargin);
            if (offset.sqrMagnitude > safeRadius * safeRadius)
            {
                horizontal = center + offset.normalized * safeRadius;
                position.x = horizontal.x;
                position.z = horizontal.y;
            }
            return position;
        }

        private bool IsInsideEnclosure(Vector3 position)
        {
            if (enclosureRenderer == null)
            {
                return true;
            }

            Bounds bounds = enclosureRenderer.bounds;
            Vector2 offset = new Vector2(
                position.x - bounds.center.x,
                position.z - bounds.center.z);
            float safeRadius = Mathf.Max(
                AbsoluteMinimumDistance,
                Mathf.Min(bounds.extents.x, bounds.extents.z) -
                EnclosureEdgeMargin);
            return offset.sqrMagnitude <=
                safeRadius * safeRadius + 0.001f;
        }

        private static Vector3 FocusPoint(Bounds bounds)
        {
            return bounds.center +
                Vector3.up * Mathf.Max(
                    0.05f,
                    bounds.extents.y * 0.12f);
        }

        private static Quaternion LookAt(
            Vector3 focusPoint,
            Vector3 cameraPosition)
        {
            Vector3 forward = focusPoint - cameraPosition;
            if (!IsFinite(forward) || forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }
            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w);
        }

        private void CreateSelectionRing()
        {
            ringRoot = new GameObject("V2_Selection_Ring");
            ringRoot.layer = LayerMask.NameToLayer("Ignore Raycast");
            ringRoot.SetActive(false);

            Shader ringShader = Shader.Find("Sprites/Default");
            if (ringShader == null)
            {
                ringShader = Shader.Find("Unlit/Color");
            }

            if (ringShader == null)
            {
                Debug.LogError("V2 selection ring could not find an unlit shader.");
                return;
            }

            ringGlowMaterial = new Material(ringShader)
            {
                name = "V2 Selection Ring Glow (Runtime)"
            };
            ringCoreMaterial = new Material(ringShader)
            {
                name = "V2 Selection Ring Core (Runtime)"
            };

            CreateRingLine(
                "Glow",
                ringGlowScale,
                0.055f,
                new Color(0.12f, 1f, 0.42f, 0.22f),
                ringGlowMaterial);
            CreateRingLine(
                "Core",
                1f,
                0.026f,
                new Color(0.32f, 1f, 0.58f, 0.95f),
                ringCoreMaterial);
        }

        private void CreateRingLine(
            string objectName,
            float normalizedRadius,
            float width,
            Color color,
            Material material)
        {
            const int segmentCount = 72;
            GameObject lineObject = new GameObject(objectName);
            lineObject.layer = ringRoot.layer;
            lineObject.transform.SetParent(ringRoot.transform, false);

            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.loop = true;
            line.useWorldSpace = false;
            line.alignment = LineAlignment.TransformZ;
            line.positionCount = segmentCount;
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;
            line.material = material;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            for (int i = 0; i < segmentCount; i++)
            {
                float angle = i * Mathf.PI * 2f / segmentCount;
                line.SetPosition(
                    i,
                    new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * normalizedRadius);
            }
        }

        private void UpdateSelectionRing()
        {
            if (selectedTarget == null || ringRoot == null || !ringRoot.activeSelf)
            {
                return;
            }

            Bounds bounds;
            if (!TryGetRingBounds(selectedTarget, out bounds))
            {
                ringRoot.SetActive(false);
                CurrentRingDiameter = 0f;
                CurrentRingOuterDiameter = 0f;
                return;
            }

            float horizontalSize = Mathf.Max(bounds.size.x, bounds.size.z);
            float effectiveSizeMultiplier =
                selectedTarget.Kind == VehicleSelectionKind.Auv
                    ? auvRingSizeMultiplier
                    : ringSizeMultiplier;
            float radius = Mathf.Max(horizontalSize * effectiveSizeMultiplier * 0.5f, 0.20f);
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 3.2f) * 0.015f;
            CurrentRingDiameter = radius * 2f;
            CurrentRingOuterDiameter = CurrentRingDiameter * ringGlowScale;
            ringRoot.transform.position =
                bounds.center + Vector3.up * (bounds.extents.y + Mathf.Max(0.12f, bounds.size.y * 0.12f));
            ringRoot.transform.rotation = Quaternion.identity;
            ringRoot.transform.localScale = Vector3.one * (radius * pulse);
        }

        private VehicleTarget FindTarget(VehicleSelectionKind kind)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].Kind == kind)
                {
                    return targets[i];
                }
            }

            return null;
        }

        private static bool TryIntersectTarget(Ray ray, VehicleTarget target, out float distance)
        {
            Bounds bounds;
            if (!TryGetTargetBounds(target, out bounds))
            {
                distance = 0f;
                return false;
            }

            return bounds.IntersectRay(ray, out distance);
        }

        private static Renderer[] CollectRingRenderers(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            List<Renderer> modelRenderers = new List<Renderer>(renderers.Length);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                {
                    continue;
                }

                if (renderer.GetComponent<TextMesh>() != null ||
                    renderer.GetComponent("TextMeshPro") != null ||
                    renderer.GetComponent("TextMeshProUGUI") != null)
                {
                    continue;
                }

                modelRenderers.Add(renderer);
            }

            return modelRenderers.ToArray();
        }

        private static bool TryGetRingBounds(VehicleTarget target, out Bounds bounds)
        {
            bounds = default(Bounds);
            if (target == null || target.Root == null || target.RingRenderers == null)
            {
                return false;
            }

            bool hasBounds = false;
            for (int i = 0; i < target.RingRenderers.Length; i++)
            {
                Renderer renderer = target.RingRenderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }

        private static bool TryGetTargetBounds(VehicleTarget target, out Bounds bounds)
        {
            return TryGetRingBounds(target, out bounds);
        }

        private void OnDestroy()
        {
            if (ringRoot != null)
            {
                Destroy(ringRoot);
            }

            if (ringGlowMaterial != null)
            {
                Destroy(ringGlowMaterial);
            }

            if (ringCoreMaterial != null)
            {
                Destroy(ringCoreMaterial);
            }
        }
    }
}
