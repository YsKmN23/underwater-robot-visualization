using System;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime.Terrain
{
    public interface ITerrainSurfaceProvider
    {
        bool TryValidate(out string error);

        bool TrySample(
            Vector3 projectedContactWorld,
            float probeStartHeightMeters,
            float probeDistanceMeters,
            out TerrainSurfaceSample sample,
            out TerrainSurfaceSampleFailureReason failureReason);
    }

    public enum TerrainSurfaceSampleFailureReason
    {
        None = 0,
        MissingCollider = 1,
        DisabledCollider = 2,
        MissingMesh = 3,
        InvalidRequest = 4,
        NoHit = 5,
        InvalidHit = 6
    }

    public readonly struct TerrainSurfaceSample
    {
        public TerrainSurfaceSample(
            Vector3 point,
            Vector3 normal,
            float distance,
            float slopeDegrees,
            int triangleIndex)
        {
            Point = point;
            Normal = normal;
            Distance = distance;
            SlopeDegrees = slopeDegrees;
            TriangleIndex = triangleIndex;
        }

        public Vector3 Point { get; }
        public Vector3 Normal { get; }
        public float Distance { get; }
        public float SlopeDegrees { get; }
        public int TriangleIndex { get; }
    }

    [DisallowMultipleComponent]
    public sealed class TerrainSurfaceSampler : MonoBehaviour,
        ITerrainSurfaceProvider,
        IAuthoritativeTerrainSurfaceProvider
    {
        private const float MinimumUsableNormalSquaredMagnitude = 1e-12f;

        [SerializeField] private MeshCollider contactTerrain;
        [NonSerialized] private ValidatedContactMeshTerrainAuthority
            authorityCache;

        public MeshCollider ContactTerrain => contactTerrain;

        public void Configure(MeshCollider collider)
        {
            contactTerrain = collider == null
                ? throw new ArgumentNullException(nameof(collider))
                : collider;
            authorityCache = null;
        }

        public bool TryValidateAuthority(
            out TerrainAuthorityFailure failure)
        {
            return TryGetAuthority(out _, out failure);
        }

        public bool TrySampleAtXZ(
            float worldX,
            float worldZ,
            out TerrainAuthoritySample sample,
            out TerrainAuthorityFailure failure)
        {
            sample = default;
            if (!TryGetAuthority(
                    out ValidatedContactMeshTerrainAuthority authority,
                    out failure))
            {
                return false;
            }
            return authority.TrySampleAtXZ(
                worldX, worldZ, out sample, out failure);
        }

        public string AuthorityGeometryHash =>
            authorityCache == null ? string.Empty : authorityCache.GeometryHash;

        public int AuthorityGridXCount =>
            authorityCache == null ? 0 : authorityCache.XCount;

        public int AuthorityGridZCount =>
            authorityCache == null ? 0 : authorityCache.ZCount;

        public float AuthorityGridSpacing =>
            authorityCache == null ? 0f : authorityCache.Spacing;

        public Vector2 AuthorityLocalMin => authorityCache == null
            ? Vector2.zero
            : new Vector2(
                authorityCache.LocalMinX,
                authorityCache.LocalMinZ);

        public Vector2 AuthorityLocalMax => authorityCache == null
            ? Vector2.zero
            : new Vector2(
                authorityCache.LocalMaxX,
                authorityCache.LocalMaxZ);

        public bool TryValidate(out string error)
        {
            if (contactTerrain == null)
            {
                error = "A contact terrain MeshCollider is required.";
                return false;
            }

            if (!contactTerrain.enabled ||
                !contactTerrain.gameObject.activeInHierarchy)
            {
                error = "The contact terrain MeshCollider must be active and enabled.";
                return false;
            }

            if (contactTerrain.sharedMesh == null)
            {
                error = "The contact terrain MeshCollider has no shared mesh.";
                return false;
            }

            if (contactTerrain.isTrigger)
            {
                error = "The contact terrain MeshCollider cannot be a trigger.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public bool TrySample(
            Vector3 projectedContactWorld,
            float probeStartHeightMeters,
            float probeDistanceMeters,
            out TerrainSurfaceSample sample,
            out TerrainSurfaceSampleFailureReason failureReason)
        {
            sample = default;

            if (contactTerrain == null)
            {
                failureReason =
                    TerrainSurfaceSampleFailureReason.MissingCollider;
                return false;
            }

            if (!contactTerrain.enabled ||
                !contactTerrain.gameObject.activeInHierarchy)
            {
                failureReason =
                    TerrainSurfaceSampleFailureReason.DisabledCollider;
                return false;
            }

            if (contactTerrain.sharedMesh == null)
            {
                failureReason = TerrainSurfaceSampleFailureReason.MissingMesh;
                return false;
            }

            if (contactTerrain.isTrigger ||
                !IsFinite(projectedContactWorld) ||
                !IsFinite(probeStartHeightMeters) ||
                probeStartHeightMeters < 0f ||
                !IsFinite(probeDistanceMeters) ||
                probeDistanceMeters <= 0f)
            {
                failureReason =
                    TerrainSurfaceSampleFailureReason.InvalidRequest;
                return false;
            }

            Vector3 origin = projectedContactWorld +
                Vector3.up * probeStartHeightMeters;
            if (!IsFinite(origin))
            {
                failureReason =
                    TerrainSurfaceSampleFailureReason.InvalidRequest;
                return false;
            }

            var ray = new Ray(origin, Vector3.down);
            if (!contactTerrain.Raycast(
                    ray,
                    out RaycastHit hit,
                    probeDistanceMeters))
            {
                failureReason = TerrainSurfaceSampleFailureReason.NoHit;
                return false;
            }

            Vector3 normal = hit.normal;
            float normalSquaredMagnitude = normal.sqrMagnitude;
            if (!ReferenceEquals(hit.collider, contactTerrain) ||
                !IsFinite(hit.point) ||
                !IsFinite(normal) ||
                !IsFinite(hit.distance) ||
                hit.distance < 0f ||
                !IsFinite(normalSquaredMagnitude) ||
                normalSquaredMagnitude <=
                    MinimumUsableNormalSquaredMagnitude ||
                hit.triangleIndex < 0)
            {
                failureReason = TerrainSurfaceSampleFailureReason.InvalidHit;
                return false;
            }

            normal /= Mathf.Sqrt(normalSquaredMagnitude);
            float slopeDegrees = Vector3.Angle(normal, Vector3.up);
            if (!IsFinite(normal) || !IsFinite(slopeDegrees))
            {
                failureReason = TerrainSurfaceSampleFailureReason.InvalidHit;
                return false;
            }

            sample = new TerrainSurfaceSample(
                hit.point,
                normal,
                hit.distance,
                slopeDegrees,
                hit.triangleIndex);
            failureReason = TerrainSurfaceSampleFailureReason.None;
            return true;
        }

        private bool TryGetAuthority(
            out ValidatedContactMeshTerrainAuthority authority,
            out TerrainAuthorityFailure failure)
        {
            authority = null;
            MeshFilter filter = contactTerrain == null
                ? null
                : contactTerrain.GetComponent<MeshFilter>();
            Mesh colliderMesh = contactTerrain == null
                ? null
                : contactTerrain.sharedMesh;
            var transform = contactTerrain == null
                ? default
                : TerrainAuthorityTransformSignature.From(
                    contactTerrain.transform);
            var binding = new TerrainAuthorityBindingState(
                contactTerrain != null,
                contactTerrain != null && contactTerrain.enabled,
                contactTerrain != null &&
                    contactTerrain.gameObject.activeInHierarchy,
                contactTerrain != null && contactTerrain.isTrigger,
                colliderMesh,
                filter == null ? null : filter.sharedMesh,
                transform);
            if (!TerrainAuthorityBindingValidator.TryValidate(
                    in binding, out failure))
            {
                return false;
            }

            if (authorityCache == null)
            {
                if (!ValidatedContactMeshTerrainAuthority.TryCreate(
                        colliderMesh,
                        in transform,
                        out authorityCache,
                        out failure))
                {
                    return false;
                }
            }
            else if (!authorityCache.TryValidateCurrent(
                         colliderMesh, in transform, out failure))
            {
                return false;
            }

            authority = authorityCache;
            failure = TerrainAuthorityFailure.None;
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                IsFinite(value.y) &&
                IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
