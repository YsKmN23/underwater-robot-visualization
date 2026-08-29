using System;
using System.Collections.Generic;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime
{
    [CreateAssetMenu(
        fileName = "VehicleRouteConfig",
        menuName = "Underwater Robot/Visualization/Vehicle Route Config")]
    public sealed class VehicleRouteConfig : ScriptableObject
    {
        public const string ResourcesPath = "Visualization/VehicleRouteConfig";

        [Header("Actual trajectory")]
        [SerializeField, Min(0.01f)] private float actualSampleDistance = 0.35f;
        [SerializeField, Min(2)] private int maxActualPointsPerVehicle = 500;
        [SerializeField, Min(0.1f)] private float teleportBreakDistance = 10f;
        [SerializeField, Min(0.001f)] private float actualLineWidth = 0.055f;
        [SerializeField] private Color actualLineColor =
            new Color(0.2f, 1f, 0.35f, 1f);

        [Header("Actual trajectory motion gate")]
        [SerializeField, Min(0.1f)] private float actualMotionWindowSeconds = 2f;
        [SerializeField, Min(0.01f)]
        private float actualMotionStartDisplacement = 0.9f;
        [SerializeField, Min(0f)]
        private float actualMotionStopDisplacement = 0.3f;
        [SerializeField, Min(0f)] private float actualMotionStopHoldSeconds = 1f;
        [SerializeField] private bool flattenUsvActualTraceToInitialHeight = true;

        [Header("Planned trajectory")]
        [SerializeField, Min(0.001f)] private float plannedLineWidth = 0.04f;
        [SerializeField] private Color plannedLineColor =
            new Color(0.15f, 0.85f, 1f, 0.65f);
        [SerializeField, Min(0.02f)] private float plannedDashLength = 0.45f;
        [SerializeField, Min(0.02f)] private float plannedDashGap = 0.25f;

        [Header("Waypoints")]
        [SerializeField, Min(0.01f)] private float waypointDiameter = 0.18f;
        [SerializeField] private Color waypointColor =
            new Color(0.45f, 0.9f, 1f, 0.9f);

        [Header("Target ring")]
        [SerializeField, Min(0.05f)] private float targetRingDiameter = 0.9f;
        [SerializeField, Min(0.001f)] private float targetRingWidth = 0.035f;
        [SerializeField] private Color targetRingColor =
            new Color(0.1f, 0.9f, 1f, 0.85f);

        [Header("Target column")]
        [SerializeField, Min(0.05f)] private float targetColumnHeight = 0.8f;
        [SerializeField, Min(0.001f)] private float targetColumnWidth = 0.035f;
        [SerializeField] private Color targetColumnColor =
            new Color(0.1f, 0.9f, 1f, 0.85f);

        [Header("Local route offsets")]
        [SerializeField, Min(0.01f)] private float auvCruiseSpeed = 1.25f;
        [SerializeField, Min(0.01f)] private float rovCruiseSpeed = 0.45f;
        [SerializeField, Min(0.01f)] private float usvCruiseSpeed = 1.5f;
        [SerializeField] private List<Vector3> auvWaypoints =
            new List<Vector3>
            {
                new Vector3(0f, 0f, 3f),
                new Vector3(2f, -1f, 6f),
                new Vector3(3f, -2f, 9f),
                new Vector3(0f, -1f, 12f)
            };
        [SerializeField] private List<Vector3> rovWaypoints =
            new List<Vector3>
            {
                new Vector3(0f, 0f, 1f),
                new Vector3(-0.75f, 0f, 2f),
                new Vector3(-1.5f, 0f, 3f)
            };
        [SerializeField] private List<Vector3> usvWaypoints =
            new List<Vector3>
            {
                new Vector3(0f, 0f, 3f),
                new Vector3(3f, 0f, 6f),
                new Vector3(-2f, 0f, 10f),
                new Vector3(1f, 0f, 14f)
            };

        public float ActualSampleDistance => actualSampleDistance;
        public int MaxActualPointsPerVehicle => maxActualPointsPerVehicle;
        public float TeleportBreakDistance => teleportBreakDistance;
        public float ActualLineWidth => actualLineWidth;
        public Color ActualLineColor => actualLineColor;
        public float ActualMotionWindowSeconds => actualMotionWindowSeconds;
        public float ActualMotionStartDisplacement =>
            actualMotionStartDisplacement;
        public float ActualMotionStopDisplacement =>
            actualMotionStopDisplacement;
        public float ActualMotionStopHoldSeconds =>
            actualMotionStopHoldSeconds;
        public bool FlattenUsvActualTraceToInitialHeight =>
            flattenUsvActualTraceToInitialHeight;
        public float PlannedLineWidth => plannedLineWidth;
        public Color PlannedLineColor => plannedLineColor;
        public float PlannedDashLength => plannedDashLength;
        public float PlannedDashGap => plannedDashGap;
        public float WaypointDiameter => waypointDiameter;
        public Color WaypointColor => waypointColor;
        public float TargetRingDiameter => targetRingDiameter;
        public float TargetRingWidth => targetRingWidth;
        public Color TargetRingColor => targetRingColor;
        public float TargetColumnHeight => targetColumnHeight;
        public float TargetColumnWidth => targetColumnWidth;
        public Color TargetColumnColor => targetColumnColor;

        public float GetCruiseSpeed(VehicleType type)
        {
            switch (type)
            {
                case VehicleType.Auv: return auvCruiseSpeed;
                case VehicleType.Rov: return rovCruiseSpeed;
                case VehicleType.Usv: return usvCruiseSpeed;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        public VehicleRouteOrientationPolicy GetOrientationPolicy(
            VehicleType type)
        {
            switch (type)
            {
                case VehicleType.Auv:
                    return VehicleRouteOrientationPolicy.AuvThreeDimensional;
                case VehicleType.Rov:
                    return VehicleRouteOrientationPolicy.RovLevelYaw;
                case VehicleType.Usv:
                    return VehicleRouteOrientationPolicy.UsvSurfaceYaw;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        public IReadOnlyList<Vector3> GetLocalWaypoints(VehicleType type)
        {
            switch (type)
            {
                case VehicleType.Auv: return auvWaypoints;
                case VehicleType.Rov: return rovWaypoints;
                case VehicleType.Usv: return usvWaypoints;
                default: return Array.Empty<Vector3>();
            }
        }

        public static VehicleRouteConfig Load()
        {
            return Resources.Load<VehicleRouteConfig>(ResourcesPath);
        }

        public IReadOnlyList<Vector3> GetLocalWaypoints(
            VehicleSelectionKind kind)
        {
            switch (kind)
            {
                case VehicleSelectionKind.Auv:
                    return auvWaypoints;
                case VehicleSelectionKind.Rov:
                    return rovWaypoints;
                case VehicleSelectionKind.Usv:
                    return usvWaypoints;
                default:
                    return Array.Empty<Vector3>();
            }
        }

        private void OnValidate()
        {
            actualSampleDistance = Mathf.Max(0.01f, actualSampleDistance);
            maxActualPointsPerVehicle =
                Mathf.Max(2, maxActualPointsPerVehicle);
            teleportBreakDistance = Mathf.Max(
                actualSampleDistance * 2f,
                teleportBreakDistance);
            actualLineWidth = Mathf.Max(0.001f, actualLineWidth);
            actualMotionWindowSeconds = Mathf.Max(
                0.1f,
                actualMotionWindowSeconds);
            actualMotionStartDisplacement = Mathf.Max(
                0.01f,
                actualMotionStartDisplacement);
            actualMotionStopDisplacement = Mathf.Clamp(
                actualMotionStopDisplacement,
                0f,
                Mathf.Max(0f, actualMotionStartDisplacement - 0.001f));
            actualMotionStopHoldSeconds = Mathf.Max(
                0f,
                actualMotionStopHoldSeconds);
            plannedLineWidth = Mathf.Max(0.001f, plannedLineWidth);
            plannedDashLength = Mathf.Max(0.02f, plannedDashLength);
            plannedDashGap = Mathf.Max(0.02f, plannedDashGap);
            waypointDiameter = Mathf.Max(0.01f, waypointDiameter);
            targetRingDiameter = Mathf.Max(0.05f, targetRingDiameter);
            targetRingWidth = Mathf.Max(0.001f, targetRingWidth);
            targetColumnHeight = Mathf.Max(0.05f, targetColumnHeight);
            targetColumnWidth = Mathf.Max(0.001f, targetColumnWidth);
            auvCruiseSpeed = Mathf.Max(0.01f, auvCruiseSpeed);
            rovCruiseSpeed = Mathf.Max(0.01f, rovCruiseSpeed);
            usvCruiseSpeed = Mathf.Max(0.01f, usvCruiseSpeed);
        }
    }
}
