using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace UnderwaterRobotScene.Visualization.Runtime.Terrain
{
    public interface IAuthoritativeTerrainSurfaceProvider
    {
        bool TryValidateAuthority(out TerrainAuthorityFailure failure);

        bool TrySampleAtXZ(
            float worldX,
            float worldZ,
            out TerrainAuthoritySample sample,
            out TerrainAuthorityFailure failure);
    }

    public enum TerrainAuthorityFailure
    {
        None = 0,
        MissingAuthority = 1,
        InvalidAuthority = 2,
        IdentityMismatch = 3,
        InvalidRequest = 4,
        UnsupportedTransform = 5,
        OutsideCoverage = 6,
        TopologyHole = 7,
        InvalidTriangle = 8,
        NonFiniteSample = 9,
        InvalidNormal = 10,
        StaleCache = 11
    }

    public readonly struct TerrainAuthoritySample
    {
        public TerrainAuthoritySample(
            Vector3 worldPoint,
            Vector3 worldNormal,
            float slopeDegrees,
            int cellX,
            int cellZ,
            int triangleIndex)
        {
            WorldPoint = worldPoint;
            WorldNormal = worldNormal;
            SlopeDegrees = slopeDegrees;
            CellX = cellX;
            CellZ = cellZ;
            TriangleIndex = triangleIndex;
        }

        public Vector3 WorldPoint { get; }
        public Vector3 WorldNormal { get; }
        public float SlopeDegrees { get; }
        public int CellX { get; }
        public int CellZ { get; }
        public int TriangleIndex { get; }
    }

    public readonly struct TerrainAuthorityTransformSignature : IEquatable<
        TerrainAuthorityTransformSignature>
    {
        private const float TransformTolerance = 0.000001f;

        public TerrainAuthorityTransformSignature(
            Vector3 position,
            Quaternion rotation,
            Vector3 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 Scale { get; }

        public bool TryValidateSupported(out TerrainAuthorityFailure failure)
        {
            if (!IsFinite(Position) || !IsFinite(Rotation) ||
                !IsFinite(Scale))
            {
                failure = TerrainAuthorityFailure.UnsupportedTransform;
                return false;
            }

            if (Mathf.Abs(Rotation.x) > TransformTolerance ||
                Mathf.Abs(Rotation.y) > TransformTolerance ||
                Mathf.Abs(Rotation.z) > TransformTolerance ||
                Mathf.Abs(Rotation.w - 1f) > TransformTolerance ||
                Mathf.Abs(Scale.x - 1f) > TransformTolerance ||
                Mathf.Abs(Scale.y - 1f) > TransformTolerance ||
                Mathf.Abs(Scale.z - 1f) > TransformTolerance)
            {
                failure = TerrainAuthorityFailure.UnsupportedTransform;
                return false;
            }

            failure = TerrainAuthorityFailure.None;
            return true;
        }

        public bool Equals(TerrainAuthorityTransformSignature other)
        {
            return Position.Equals(other.Position) &&
                Rotation.Equals(other.Rotation) &&
                Scale.Equals(other.Scale);
        }

        public override bool Equals(object obj)
        {
            return obj is TerrainAuthorityTransformSignature other &&
                Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Position.GetHashCode();
                hash = hash * 397 ^ Rotation.GetHashCode();
                hash = hash * 397 ^ Scale.GetHashCode();
                return hash;
            }
        }

        public static TerrainAuthorityTransformSignature From(
            Transform value)
        {
            if (value == null)
            {
                return default;
            }
            return new TerrainAuthorityTransformSignature(
                value.position,
                value.rotation,
                value.lossyScale);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) &&
                IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) &&
                IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public readonly struct TerrainAuthorityBindingState
    {
        public TerrainAuthorityBindingState(
            bool hasCollider,
            bool colliderEnabled,
            bool colliderActive,
            bool colliderIsTrigger,
            Mesh colliderMesh,
            Mesh visualMesh,
            TerrainAuthorityTransformSignature transform)
        {
            HasCollider = hasCollider;
            ColliderEnabled = colliderEnabled;
            ColliderActive = colliderActive;
            ColliderIsTrigger = colliderIsTrigger;
            ColliderMesh = colliderMesh;
            VisualMesh = visualMesh;
            Transform = transform;
        }

        public bool HasCollider { get; }
        public bool ColliderEnabled { get; }
        public bool ColliderActive { get; }
        public bool ColliderIsTrigger { get; }
        public Mesh ColliderMesh { get; }
        public Mesh VisualMesh { get; }
        public TerrainAuthorityTransformSignature Transform { get; }
    }

    public static class TerrainAuthorityBindingValidator
    {
        public static bool TryValidate(
            in TerrainAuthorityBindingState state,
            out TerrainAuthorityFailure failure)
        {
            if (!state.HasCollider)
            {
                failure = TerrainAuthorityFailure.MissingAuthority;
                return false;
            }
            if (!state.ColliderEnabled || !state.ColliderActive ||
                state.ColliderIsTrigger)
            {
                failure = TerrainAuthorityFailure.InvalidAuthority;
                return false;
            }
            if (state.ColliderMesh == null)
            {
                failure = TerrainAuthorityFailure.MissingAuthority;
                return false;
            }
            if (!state.ColliderMesh.isReadable)
            {
                failure = TerrainAuthorityFailure.InvalidAuthority;
                return false;
            }
            if (state.VisualMesh == null ||
                !ReferenceEquals(state.ColliderMesh, state.VisualMesh))
            {
                failure = TerrainAuthorityFailure.IdentityMismatch;
                return false;
            }
            return state.Transform.TryValidateSupported(out failure);
        }
    }

    public sealed class ValidatedContactMeshTerrainAuthority
    {
        private const float GridTolerance = 0.00001f;
        private const float MinimumNormalSquaredMagnitude = 1e-12f;
        private const string TopologyVersionValue =
            "CONTACT_GRID_X_MAJOR_Z_MINOR_ACB_ADC_V1";

        private readonly Mesh sourceMesh;
        private readonly Vector3[] vertices;
        private readonly int[] triangles;
        private readonly TerrainAuthorityTransformSignature transform;
        private readonly Bounds meshBounds;
        private readonly int sourceVertexCount;
        private readonly int sourceIndexCount;
        private readonly int xCount;
        private readonly int zCount;
        private readonly float spacing;
        private readonly float minX;
        private readonly float maxX;
        private readonly float minZ;
        private readonly float maxZ;

        private ValidatedContactMeshTerrainAuthority(
            Mesh sourceMesh,
            Vector3[] vertices,
            int[] triangles,
            TerrainAuthorityTransformSignature transform,
            int xCount,
            int zCount,
            float spacing,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            string geometryHash)
        {
            this.sourceMesh = sourceMesh;
            this.vertices = vertices;
            this.triangles = triangles;
            this.transform = transform;
            meshBounds = sourceMesh.bounds;
            sourceVertexCount = vertices.Length;
            sourceIndexCount = triangles.Length;
            this.xCount = xCount;
            this.zCount = zCount;
            this.spacing = spacing;
            this.minX = minX;
            this.maxX = maxX;
            this.minZ = minZ;
            this.maxZ = maxZ;
            GeometryHash = geometryHash;
        }

        public string TopologyVersion => TopologyVersionValue;
        public string GeometryHash { get; }
        public int XCount => xCount;
        public int ZCount => zCount;
        public float Spacing => spacing;
        public float LocalMinX => minX;
        public float LocalMaxX => maxX;
        public float LocalMinZ => minZ;
        public float LocalMaxZ => maxZ;

        public static bool TryCreate(
            Mesh mesh,
            in TerrainAuthorityTransformSignature transform,
            out ValidatedContactMeshTerrainAuthority authority,
            out TerrainAuthorityFailure failure)
        {
            authority = null;
            if (mesh == null)
            {
                failure = TerrainAuthorityFailure.MissingAuthority;
                return false;
            }
            if (!mesh.isReadable || mesh.subMeshCount != 1 ||
                mesh.GetTopology(0) != MeshTopology.Triangles)
            {
                failure = TerrainAuthorityFailure.InvalidAuthority;
                return false;
            }
            if (!transform.TryValidateSupported(out failure))
            {
                return false;
            }

            Vector3[] vertices;
            int[] triangles;
            try
            {
                vertices = mesh.vertices;
                triangles = mesh.triangles;
            }
            catch (Exception)
            {
                failure = TerrainAuthorityFailure.InvalidAuthority;
                return false;
            }

            if (!TryDiscoverGrid(
                    mesh.bounds,
                    vertices,
                    out int xCount,
                    out int zCount,
                    out float spacing,
                    out float minX,
                    out float maxX,
                    out float minZ,
                    out float maxZ,
                    out failure) ||
                !TryValidateTopology(
                    vertices,
                    triangles,
                    xCount,
                    zCount,
                    out failure))
            {
                return false;
            }

            string geometryHash;
            try
            {
                geometryHash = ComputeGeometryHash(vertices, triangles);
            }
            catch (Exception)
            {
                failure = TerrainAuthorityFailure.InvalidAuthority;
                return false;
            }

            authority = new ValidatedContactMeshTerrainAuthority(
                mesh,
                vertices,
                triangles,
                transform,
                xCount,
                zCount,
                spacing,
                minX,
                maxX,
                minZ,
                maxZ,
                geometryHash);
            failure = TerrainAuthorityFailure.None;
            return true;
        }

        public static bool TryValidateMeshData(
            Vector3[] vertices,
            int[] triangles,
            Bounds bounds,
            out TerrainAuthorityFailure failure)
        {
            return TryDiscoverGrid(
                       bounds,
                       vertices,
                       out int xCount,
                       out int zCount,
                       out _,
                       out _,
                       out _,
                       out _,
                       out _,
                       out failure) &&
                TryValidateTopology(
                    vertices,
                    triangles,
                    xCount,
                    zCount,
                    out failure);
        }

        public bool TryValidateCurrent(
            Mesh currentMesh,
            in TerrainAuthorityTransformSignature currentTransform,
            out TerrainAuthorityFailure failure)
        {
            if (currentMesh == null)
            {
                failure = TerrainAuthorityFailure.MissingAuthority;
                return false;
            }
            if (!currentMesh.isReadable)
            {
                failure = TerrainAuthorityFailure.InvalidAuthority;
                return false;
            }
            if (!currentTransform.TryValidateSupported(out failure))
            {
                return false;
            }
            if (!ReferenceEquals(currentMesh, sourceMesh) ||
                currentMesh.vertexCount != sourceVertexCount ||
                currentMesh.subMeshCount != 1 ||
                currentMesh.GetTopology(0) != MeshTopology.Triangles ||
                currentMesh.GetIndexCount(0) != (ulong)sourceIndexCount ||
                !currentMesh.bounds.Equals(meshBounds) ||
                !currentTransform.Equals(transform))
            {
                failure = TerrainAuthorityFailure.StaleCache;
                return false;
            }

            // In-place mutation with unchanged counts/bounds is unsupported.
            // Configure is the sanctioned mutation path and invalidates cache.
            failure = TerrainAuthorityFailure.None;
            return true;
        }

        public bool TrySampleAtXZ(
            float worldX,
            float worldZ,
            out TerrainAuthoritySample sample,
            out TerrainAuthorityFailure failure)
        {
            sample = default;
            if (!IsFinite(worldX) || !IsFinite(worldZ))
            {
                failure = TerrainAuthorityFailure.InvalidRequest;
                return false;
            }

            float localX = worldX - transform.Position.x;
            float localZ = worldZ - transform.Position.z;
            if (!IsFinite(localX) || !IsFinite(localZ))
            {
                failure = TerrainAuthorityFailure.NonFiniteSample;
                return false;
            }
            if ((localX < minX && !Near(localX, minX)) ||
                (localX > maxX && !Near(localX, maxX)) ||
                (localZ < minZ && !Near(localZ, minZ)) ||
                (localZ > maxZ && !Near(localZ, maxZ)))
            {
                failure = TerrainAuthorityFailure.OutsideCoverage;
                return false;
            }
            localX = Mathf.Clamp(localX, minX, maxX);
            localZ = Mathf.Clamp(localZ, minZ, maxZ);

            float gridX = (localX - minX) / spacing;
            float gridZ = (localZ - minZ) / spacing;
            int cellX = Mathf.Clamp(
                Mathf.FloorToInt(gridX), 0, xCount - 2);
            int cellZ = Mathf.Clamp(
                Mathf.FloorToInt(gridZ), 0, zCount - 2);
            float u = Mathf.Clamp01(gridX - cellX);
            float v = Mathf.Clamp01(gridZ - cellZ);
            int cellIndex = cellX * (zCount - 1) + cellZ;
            int triangleIndex = cellIndex * 2 + (v <= u ? 0 : 1);
            int triangleOffset = triangleIndex * 3;
            if (triangleOffset < 0 || triangleOffset + 2 >= triangles.Length)
            {
                failure = TerrainAuthorityFailure.TopologyHole;
                return false;
            }

            int i0 = triangles[triangleOffset];
            int i1 = triangles[triangleOffset + 1];
            int i2 = triangles[triangleOffset + 2];
            if (!IndicesValid(vertices.Length, i0, i1, i2))
            {
                failure = TerrainAuthorityFailure.InvalidTriangle;
                return false;
            }

            int a = cellX * zCount + cellZ;
            int b = (cellX + 1) * zCount + cellZ;
            int c = (cellX + 1) * zCount + cellZ + 1;
            int d = cellX * zCount + cellZ + 1;
            float localY;
            if (v <= u)
            {
                localY =
                    (1f - u) * vertices[a].y +
                    (u - v) * vertices[b].y +
                    v * vertices[c].y;
            }
            else
            {
                localY =
                    (1f - v) * vertices[a].y +
                    u * vertices[c].y +
                    (v - u) * vertices[d].y;
            }

            if (!TryComputeUpwardFaceNormal(
                    vertices[i0],
                    vertices[i1],
                    vertices[i2],
                    out Vector3 worldNormal,
                    out failure))
            {
                return false;
            }

            Vector3 worldPoint = new Vector3(
                localX + transform.Position.x,
                localY + transform.Position.y,
                localZ + transform.Position.z);
            float slope = Vector3.Angle(worldNormal, Vector3.up);
            if (!IsFinite(worldPoint) || !IsFinite(slope))
            {
                failure = TerrainAuthorityFailure.NonFiniteSample;
                return false;
            }

            sample = new TerrainAuthoritySample(
                worldPoint,
                worldNormal,
                slope,
                cellX,
                cellZ,
                triangleIndex);
            failure = TerrainAuthorityFailure.None;
            return true;
        }

        public static bool TryComputeUpwardFaceNormal(
            Vector3 first,
            Vector3 second,
            Vector3 third,
            out Vector3 normal,
            out TerrainAuthorityFailure failure)
        {
            normal = Vector3.zero;
            if (!IsFinite(first) || !IsFinite(second) || !IsFinite(third))
            {
                failure = TerrainAuthorityFailure.InvalidTriangle;
                return false;
            }
            Vector3 value = Vector3.Cross(second - first, third - first);
            float squared = value.sqrMagnitude;
            if (!IsFinite(value) || !IsFinite(squared) ||
                squared <= MinimumNormalSquaredMagnitude)
            {
                failure = TerrainAuthorityFailure.InvalidTriangle;
                return false;
            }
            normal = value / Mathf.Sqrt(squared);
            if (!IsFinite(normal) || normal.y <= 0f)
            {
                normal = Vector3.zero;
                failure = TerrainAuthorityFailure.InvalidNormal;
                return false;
            }
            failure = TerrainAuthorityFailure.None;
            return true;
        }

        private static bool TryDiscoverGrid(
            Bounds bounds,
            Vector3[] vertices,
            out int xCount,
            out int zCount,
            out float spacing,
            out float minX,
            out float maxX,
            out float minZ,
            out float maxZ,
            out TerrainAuthorityFailure failure)
        {
            xCount = 0;
            zCount = 0;
            spacing = 0f;
            minX = 0f;
            maxX = 0f;
            minZ = 0f;
            maxZ = 0f;
            if (vertices == null || vertices.Length < 4 ||
                !IsFinite(bounds.center) || !IsFinite(bounds.size))
            {
                failure = TerrainAuthorityFailure.InvalidAuthority;
                return false;
            }
            for (int index = 0; index < vertices.Length; index++)
            {
                if (!IsFinite(vertices[index]))
                {
                    failure = TerrainAuthorityFailure.InvalidAuthority;
                    return false;
                }
            }

            minX = vertices[0].x;
            minZ = vertices[0].z;
            zCount = 1;
            while (zCount < vertices.Length &&
                   Near(vertices[zCount].x, minX))
            {
                zCount++;
            }
            if (zCount < 2 || zCount >= vertices.Length ||
                vertices.Length % zCount != 0)
            {
                failure = TerrainAuthorityFailure.InvalidAuthority;
                return false;
            }
            xCount = vertices.Length / zCount;
            if (xCount < 2)
            {
                failure = TerrainAuthorityFailure.InvalidAuthority;
                return false;
            }

            float spacingZ = vertices[1].z - minZ;
            float spacingX = vertices[zCount].x - minX;
            if (!IsFinite(spacingX) || !IsFinite(spacingZ) ||
                spacingX <= 0f || spacingZ <= 0f ||
                !Near(spacingX, spacingZ))
            {
                failure = TerrainAuthorityFailure.InvalidAuthority;
                return false;
            }
            spacing = 0.5f * (spacingX + spacingZ);
            for (int x = 0; x < xCount; x++)
            {
                float expectedX = minX + x * spacing;
                for (int z = 0; z < zCount; z++)
                {
                    int index = x * zCount + z;
                    float expectedZ = minZ + z * spacing;
                    if (!Near(vertices[index].x, expectedX) ||
                        !Near(vertices[index].z, expectedZ))
                    {
                        failure = TerrainAuthorityFailure.InvalidAuthority;
                        return false;
                    }
                }
            }

            maxX = minX + (xCount - 1) * spacing;
            maxZ = minZ + (zCount - 1) * spacing;
            if (!Near(bounds.min.x, minX) || !Near(bounds.max.x, maxX) ||
                !Near(bounds.min.z, minZ) || !Near(bounds.max.z, maxZ))
            {
                failure = TerrainAuthorityFailure.InvalidAuthority;
                return false;
            }
            failure = TerrainAuthorityFailure.None;
            return true;
        }

        private static bool TryValidateTopology(
            Vector3[] vertices,
            int[] triangles,
            int xCount,
            int zCount,
            out TerrainAuthorityFailure failure)
        {
            if (triangles == null || triangles.Length % 3 != 0)
            {
                failure = TerrainAuthorityFailure.InvalidTriangle;
                return false;
            }
            int expectedIndexCount = (xCount - 1) * (zCount - 1) * 6;
            if (triangles.Length < expectedIndexCount)
            {
                failure = TerrainAuthorityFailure.TopologyHole;
                return false;
            }
            if (triangles.Length != expectedIndexCount)
            {
                failure = TerrainAuthorityFailure.InvalidAuthority;
                return false;
            }

            int cursor = 0;
            for (int x = 0; x < xCount - 1; x++)
            {
                for (int z = 0; z < zCount - 1; z++)
                {
                    int a = x * zCount + z;
                    int b = (x + 1) * zCount + z;
                    int c = (x + 1) * zCount + z + 1;
                    int d = x * zCount + z + 1;
                    int[] expected = { a, c, b, a, d, c };
                    for (int index = 0; index < expected.Length; index++)
                    {
                        int actual = triangles[cursor + index];
                        if (actual < 0 || actual >= vertices.Length)
                        {
                            failure = TerrainAuthorityFailure.InvalidTriangle;
                            return false;
                        }
                        if (actual != expected[index])
                        {
                            failure = TerrainAuthorityFailure.InvalidAuthority;
                            return false;
                        }
                    }

                    if (!TryComputeUpwardFaceNormal(
                            vertices[a], vertices[c], vertices[b],
                            out _, out failure) ||
                        !TryComputeUpwardFaceNormal(
                            vertices[a], vertices[d], vertices[c],
                            out _, out failure))
                    {
                        return false;
                    }
                    cursor += 6;
                }
            }
            failure = TerrainAuthorityFailure.None;
            return true;
        }

        private static string ComputeGeometryHash(
            Vector3[] vertices,
            int[] triangles)
        {
            using (var memory = new MemoryStream())
            using (var writer = new BinaryWriter(memory))
            {
                writer.Write(vertices.Length);
                for (int index = 0; index < vertices.Length; index++)
                {
                    writer.Write(vertices[index].x);
                    writer.Write(vertices[index].y);
                    writer.Write(vertices[index].z);
                }
                writer.Write(triangles.Length);
                for (int index = 0; index < triangles.Length; index++)
                {
                    writer.Write(triangles[index]);
                }
                writer.Flush();
                using (SHA256 sha = SHA256.Create())
                {
                    return BitConverter.ToString(
                            sha.ComputeHash(memory.ToArray()))
                        .Replace("-", string.Empty)
                        .ToLowerInvariant();
                }
            }
        }

        private static bool IndicesValid(
            int vertexCount,
            int first,
            int second,
            int third)
        {
            return first >= 0 && first < vertexCount &&
                second >= 0 && second < vertexCount &&
                third >= 0 && third < vertexCount;
        }

        private static bool Near(float actual, float expected)
        {
            float scale = Mathf.Max(
                1f,
                Mathf.Max(Mathf.Abs(actual), Mathf.Abs(expected)));
            return Mathf.Abs(actual - expected) <= GridTolerance * scale;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) &&
                IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
