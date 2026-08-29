using System;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime.Constraints;
using UnderwaterRobotScene.Visualization.Runtime.Terrain;
using UnderwaterRobotScene.Visualization.Transforms;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime.Auv
{
    [DisallowMultipleComponent]
    public sealed class AuvTerrainClearanceConstraint : MonoBehaviour,
        IUnityPoseConstraint,
        IRouteSafetyValidator,
        IRouteSafetyDiagnosticProvider
    {
        [SerializeField] private TerrainSurfaceSampler surfaceSampler;
        [SerializeField] private AuvTerrainClearanceProfile profile;
        [SerializeField] private Transform waterSurface;

        public TerrainSurfaceSampler SurfaceSampler => surfaceSampler;
        public AuvTerrainClearanceProfile Profile => profile;
        public Transform WaterSurface => waterSurface;
        public RouteSafetyFailureDiagnostic LastRouteSafetyFailure { get; private set; } =
            RouteSafetyFailureDiagnostic.None;

        public void Configure(
            TerrainSurfaceSampler sampler,
            AuvTerrainClearanceProfile clearanceProfile,
            Transform waterSurfaceAuthority)
        {
            surfaceSampler = sampler == null
                ? throw new ArgumentNullException(nameof(sampler))
                : sampler;
            profile = clearanceProfile == null
                ? throw new ArgumentNullException(nameof(clearanceProfile))
                : clearanceProfile;
            waterSurface = waterSurfaceAuthority == null
                ? throw new ArgumentNullException(nameof(waterSurfaceAuthority))
                : waterSurfaceAuthority;
        }

        public bool TryValidate(out string error)
        {
            error = string.Empty;
            if (surfaceSampler == null)
            {
                error = "An AUV terrain surface sampler is required.";
                return false;
            }
            if (profile == null || !profile.TryValidate(out error))
            {
                return false;
            }
            if (!TryGetWaterSurfaceY(out _, out error))
            {
                return false;
            }
            if (!surfaceSampler.TryValidateAuthority(
                    out TerrainAuthorityFailure authorityFailure))
            {
                error = "AUV terrain authority validation failed: " +
                    authorityFailure + ".";
                return false;
            }
            MeshCollider terrain = surfaceSampler.ContactTerrain;
            if ((profile.TerrainLayerMask.value &
                 (1 << terrain.gameObject.layer)) == 0)
            {
                error = "The AUV profile LayerMask excludes its terrain provider.";
                return false;
            }
            return true;
        }

        public AuvTerrainClearanceResult Evaluate(
            Vector3 position,
            Quaternion rotation)
        {
            return AuvTerrainClearanceEvaluator.Evaluate(
                position,
                rotation,
                profile,
                surfaceSampler,
                TryGetWaterSurfaceY(out float waterSurfaceY, out _)
                    ? waterSurfaceY
                    : float.NaN);
        }

        public UnityPoseConstraintResult Constrain(
            in UnityPoseConstraintRequest request)
        {
            AuvTerrainClearanceResult result = Evaluate(
                request.Position, request.Rotation);
            return new UnityPoseConstraintResult(
                result.MayApply
                    ? UnityPoseConstraintDecision.Apply
                    : UnityPoseConstraintDecision.HoldCurrent,
                result.OutputPosition,
                result.OutputRotation,
                string.IsNullOrEmpty(result.Reason)
                    ? result.State.ToString()
                    : result.Reason);
        }

        public bool TryValidateRoute(
            ActiveRouteSnapshot candidate,
            in CoordinateTransformProfile transformProfile,
            out string error)
        {
            LastRouteSafetyFailure = RouteSafetyFailureDiagnostic.None;
            if (!TryValidate(out error))
            {
                return false;
            }
            bool valid = AuvTerrainClearanceEvaluator.TryValidateRoute(
                candidate,
                in transformProfile,
                profile,
                surfaceSampler,
                TryGetWaterSurfaceY(out float waterSurfaceY, out _)
                    ? waterSurfaceY
                    : float.NaN,
                out error,
                out RouteSafetyFailureDiagnostic diagnostic);
            LastRouteSafetyFailure = diagnostic;
            return valid;
        }

        public void ResetObservation()
        {
        }

        private bool TryGetWaterSurfaceY(
            out float waterSurfaceY,
            out string error)
        {
            waterSurfaceY = float.NaN;
            error = string.Empty;
            if (waterSurface == null)
            {
                error = "A bound Water_Surface Transform is required.";
                return false;
            }
            if (!waterSurface.gameObject.activeInHierarchy)
            {
                error = "The bound Water_Surface authority must be active.";
                return false;
            }
            Vector3 position = waterSurface.position;
            if (!IsFinite(position))
            {
                error = "The bound Water_Surface position must be finite.";
                return false;
            }
            waterSurfaceY = position.y;
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
