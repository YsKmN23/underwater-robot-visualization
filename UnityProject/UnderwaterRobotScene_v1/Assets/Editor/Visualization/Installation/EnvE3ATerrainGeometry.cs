using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnderwaterRobotScene.Visualization.Runtime.Terrain;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    internal static class EnvE3ATerrainGeometry
    {
        private const float MigrationGridTolerance = 0.00001f;
        private const float MigrationNormalMinimumSquaredMagnitude = 1e-12f;
        private const string MigrationContactMeshName =
            "ENV_E3A_ContactTerrainMesh";

        internal static Mesh BuildContactMesh(
            EnvE3AContinuousSeabedConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            configuration.Validate();
            int xCount = configuration.ContactGridXCount;
            int zCount = configuration.ContactGridZCount;
            float spacing = configuration.ContactGridSpacingMeters;
            var vertices = new Vector3[configuration.ContactVertexCount];
            var uv = new Vector2[vertices.Length];
            for (int xIndex = 0; xIndex < xCount; xIndex++)
            {
                float x = configuration.ContactBounds.MinX +
                    xIndex * spacing;
                for (int zIndex = 0; zIndex < zCount; zIndex++)
                {
                    float z = configuration.ContactBounds.MinZ +
                        zIndex * spacing;
                    int index = xIndex * zCount + zIndex;
                    vertices[index] = new Vector3(
                        x,
                        EvaluateBaseHeight(configuration, x, z),
                        z);
                    uv[index] = new Vector2(x, z);
                }
            }

            var triangles = new int[configuration.ContactIndexCount];
            int cursor = 0;
            for (int xIndex = 0; xIndex < xCount - 1; xIndex++)
            {
                for (int zIndex = 0; zIndex < zCount - 1; zIndex++)
                {
                    int a = xIndex * zCount + zIndex;
                    int b = (xIndex + 1) * zCount + zIndex;
                    int c = (xIndex + 1) * zCount + zIndex + 1;
                    int d = xIndex * zCount + zIndex + 1;
                    triangles[cursor++] = a;
                    triangles[cursor++] = c;
                    triangles[cursor++] = b;
                    triangles[cursor++] = a;
                    triangles[cursor++] = d;
                    triangles[cursor++] = c;
                }
            }

            var mesh = new Mesh
            {
                name = configuration.ContactMeshName,
                indexFormat = vertices.Length > ushort.MaxValue
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16,
                vertices = vertices,
                triangles = triangles,
                uv = uv
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        internal static Mesh BuildFarRenderMesh(
            Mesh finalContactMesh,
            EnvE3AContinuousSeabedConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            configuration.Validate();
            ValidateContactMesh(finalContactMesh, configuration);
            Vector3[] inner = CopyContactPerimeter(finalContactMesh);
            Vector3[] innerNormals = CopyContactPerimeterNormals(
                finalContactMesh,
                configuration);
            int count = configuration.ContactPerimeterCount;
            var vertices = new Vector3[configuration.FarVertexCount];
            var uv = new Vector2[vertices.Length];
            int vertexWriteCount = 0;
            for (int index = 0; index < count; index++)
            {
                float theta = Mathf.PI * 2f * index / count;
                float variation =
                    configuration.FarReliefMaximumMeters *
                    (0.55f * Mathf.Sin(3f * theta + 0.35f) +
                     0.25f * Mathf.Sin(5f * theta - 0.8f));
                vertices[index] = inner[index];
                vertexWriteCount++;
                vertices[count + index] = MapPerimeterToRectangle(
                    inner[index],
                    configuration.ContactBounds,
                    configuration.FarMidHalfExtentX,
                    configuration.FarMidHalfExtentZ);
                vertices[count + index].y = inner[index].y +
                    SmoothStep01(0.5f) * variation;
                vertexWriteCount++;
                vertices[count * 2 + index] = MapPerimeterToRectangle(
                    inner[index],
                    configuration.ContactBounds,
                    configuration.FarOuterHalfExtentX,
                    configuration.FarOuterHalfExtentZ);
                vertices[count * 2 + index].y =
                    inner[index].y + variation;
                vertexWriteCount++;
            }
            if (vertexWriteCount != vertices.Length)
            {
                throw new InvalidOperationException(
                    "Far terrain vertex writes did not fill the allocation.");
            }

            var triangles = new int[configuration.FarIndexCount];
            int cursor = 0;
            for (int band = 0;
                 band < configuration.FarRingCount - 1;
                 band++)
            {
                int innerOffset = band * count;
                int outerOffset = (band + 1) * count;
                for (int index = 0; index < count; index++)
                {
                    int next = (index + 1) % count;
                    triangles[cursor++] = innerOffset + index;
                    triangles[cursor++] = innerOffset + next;
                    triangles[cursor++] = outerOffset + next;
                    triangles[cursor++] = innerOffset + index;
                    triangles[cursor++] = outerOffset + next;
                    triangles[cursor++] = outerOffset + index;
                }
            }
            if (cursor != triangles.Length)
            {
                throw new InvalidOperationException(
                    "Far terrain triangle writes did not fill the allocation.");
            }

            for (int index = 0; index < vertices.Length; index++)
            {
                uv[index] = new Vector2(
                    vertices[index].x,
                    vertices[index].z);
            }

            Vector3[] normals = BuildAreaWeightedNormals(
                vertices,
                triangles);
            for (int index = 0; index < count; index++)
            {
                normals[index] = innerNormals[index];
            }

            var mesh = new Mesh
            {
                name = configuration.FarRenderMeshName,
                indexFormat = vertices.Length > ushort.MaxValue
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16,
                vertices = vertices,
                triangles = triangles,
                uv = uv,
                normals = normals
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3[] CopyContactPerimeterNormals(
            Mesh finalContactMesh,
            EnvE3AContinuousSeabedConfiguration configuration)
        {
            Vector3[] normals = finalContactMesh.normals;
            int xCount = configuration.ContactGridXCount;
            int zCount = configuration.ContactGridZCount;
            var perimeter =
                new Vector3[configuration.ContactPerimeterCount];
            int cursor = 0;
            for (int x = 0; x < xCount; x++)
            {
                perimeter[cursor++] = normals[x * zCount];
            }
            for (int z = 1; z < zCount; z++)
            {
                perimeter[cursor++] =
                    normals[(xCount - 1) * zCount + z];
            }
            for (int x = xCount - 2; x >= 0; x--)
            {
                perimeter[cursor++] =
                    normals[x * zCount + zCount - 1];
            }
            for (int z = zCount - 2; z > 0; z--)
            {
                perimeter[cursor++] = normals[z];
            }
            if (cursor != perimeter.Length)
            {
                throw new InvalidOperationException(
                    "Contact perimeter normal count does not match authority.");
            }
            return perimeter;
        }

        private static Vector3 MapPerimeterToRectangle(
            Vector3 inner,
            EnvE2APlanarBounds contactBounds,
            float halfExtentX,
            float halfExtentZ)
        {
            if (inner.z == contactBounds.MinZ)
            {
                float t = (inner.x - contactBounds.MinX) /
                    contactBounds.Width;
                return new Vector3(
                    Mathf.Lerp(-halfExtentX, halfExtentX, t),
                    inner.y,
                    -halfExtentZ);
            }
            if (inner.x == contactBounds.MaxX)
            {
                float t = (inner.z - contactBounds.MinZ) /
                    contactBounds.Depth;
                return new Vector3(
                    halfExtentX,
                    inner.y,
                    Mathf.Lerp(-halfExtentZ, halfExtentZ, t));
            }
            if (inner.z == contactBounds.MaxZ)
            {
                float t = (contactBounds.MaxX - inner.x) /
                    contactBounds.Width;
                return new Vector3(
                    Mathf.Lerp(halfExtentX, -halfExtentX, t),
                    inner.y,
                    halfExtentZ);
            }
            if (inner.x != contactBounds.MinX)
            {
                throw new InvalidOperationException(
                    "Contact perimeter sample is not on an authority side.");
            }
            float leftT = (contactBounds.MaxZ - inner.z) /
                contactBounds.Depth;
            return new Vector3(
                -halfExtentX,
                inner.y,
                Mathf.Lerp(halfExtentZ, -halfExtentZ, leftT));
        }

        private static float SmoothStep01(float t)
        {
            return t * t * (3f - 2f * t);
        }

        private static Vector3[] BuildAreaWeightedNormals(
            Vector3[] vertices,
            int[] triangles)
        {
            var normals = new Vector3[vertices.Length];
            for (int index = 0; index < triangles.Length; index += 3)
            {
                int a = triangles[index];
                int b = triangles[index + 1];
                int c = triangles[index + 2];
                Vector3 areaNormal = Vector3.Cross(
                    vertices[b] - vertices[a],
                    vertices[c] - vertices[a]);
                if (areaNormal.sqrMagnitude <= 0f || areaNormal.y <= 0f)
                {
                    throw new InvalidOperationException(
                        "Far terrain contains a degenerate or downward triangle.");
                }
                normals[a] += areaNormal;
                normals[b] += areaNormal;
                normals[c] += areaNormal;
            }
            for (int index = 0; index < normals.Length; index++)
            {
                if (normals[index].sqrMagnitude <= 0f)
                {
                    throw new InvalidOperationException(
                        "Far terrain normal accumulation is empty.");
                }
                normals[index].Normalize();
            }
            return normals;
        }

        internal static float EvaluateBaseHeight(
            EnvE3AContinuousSeabedConfiguration configuration,
            float localX,
            float localZ)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            float normalizedX = localX /
                (configuration.CenterShelfBounds.Width * 0.5f);
            float normalizedZ = localZ /
                (configuration.CenterShelfBounds.Depth * 0.5f);
            float rho = Mathf.Sqrt(
                normalizedX * normalizedX +
                normalizedZ * normalizedZ);
            float theta = Mathf.Atan2(normalizedZ, normalizedX);
            float boundary =
                0.92f +
                0.045f * Mathf.Sin(3f * theta) +
                0.025f * Mathf.Cos(5f * theta);
            float q = rho / boundary;
            if (q <= 1f)
            {
                return configuration.CenterShelfNominalY;
            }

            if (q >= configuration.ShelfTransitionOuterMultiplier)
            {
                return configuration.HoldingTerrainY;
            }

            float t = (q - 1f) /
                (configuration.ShelfTransitionOuterMultiplier - 1f);
            float smooth = t * t * (3f - 2f * t);
            return Mathf.Lerp(
                configuration.CenterShelfNominalY,
                configuration.HoldingTerrainY,
                smooth);
        }

        internal static Vector3[] CopyContactPerimeter(
            Mesh finalContactMesh)
        {
            EnvE3AContinuousSeabedConfiguration configuration =
                EnvE3AContinuousSeabedConfiguration.CreateApproved();
            ValidateContactMesh(finalContactMesh, configuration);
            int xCount = configuration.ContactGridXCount;
            int zCount = configuration.ContactGridZCount;
            Vector3[] vertices = finalContactMesh.vertices;
            var perimeter =
                new Vector3[configuration.ContactPerimeterCount];
            int cursor = 0;
            for (int x = 0; x < xCount; x++)
            {
                perimeter[cursor++] = vertices[x * zCount];
            }

            for (int z = 1; z < zCount; z++)
            {
                perimeter[cursor++] =
                    vertices[(xCount - 1) * zCount + z];
            }

            for (int x = xCount - 2; x >= 0; x--)
            {
                perimeter[cursor++] =
                    vertices[x * zCount + zCount - 1];
            }

            for (int z = zCount - 2; z > 0; z--)
            {
                perimeter[cursor++] = vertices[z];
            }

            if (cursor != perimeter.Length)
            {
                throw new InvalidOperationException(
                    "Contact perimeter count does not match authority.");
            }

            return perimeter;
        }

        internal static bool TrySampleContactMesh(
            Mesh mesh,
            Transform transform,
            float worldX,
            float worldZ,
            out float height,
            out Vector3 normal)
        {
            height = 0f;
            normal = Vector3.zero;
            if (mesh == null || transform == null)
            {
                return false;
            }

            EnvE3AContinuousSeabedConfiguration configuration =
                EnvE3AContinuousSeabedConfiguration.CreateApproved();
            int xCount = configuration.ContactGridXCount;
            int zCount = configuration.ContactGridZCount;
            float spacing = configuration.ContactGridSpacingMeters;
            Vector3 localProbe = transform.InverseTransformPoint(
                new Vector3(worldX, transform.position.y, worldZ));
            if (!configuration.ContactBounds.Contains(
                    localProbe.x, localProbe.z))
            {
                return false;
            }

            ValidateContactMesh(mesh, configuration);
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            float minX = configuration.ContactBounds.MinX;
            float minZ = configuration.ContactBounds.MinZ;
            float gridX = (localProbe.x - minX) / spacing;
            float gridZ = (localProbe.z - minZ) / spacing;
            int xIndex = Mathf.Min(Mathf.FloorToInt(gridX), xCount - 2);
            int zIndex = Mathf.Min(Mathf.FloorToInt(gridZ), zCount - 2);
            float u = gridX - xIndex;
            float v = gridZ - zIndex;
            int a = xIndex * zCount + zIndex;
            int b = (xIndex + 1) * zCount + zIndex;
            int c = (xIndex + 1) * zCount + zIndex + 1;
            int d = xIndex * zCount + zIndex + 1;
            float localY;
            Vector3 localNormal;
            if (v <= u)
            {
                localY =
                    (1f - u) * vertices[a].y +
                    (u - v) * vertices[b].y +
                    v * vertices[c].y;
                localNormal =
                    (1f - u) * normals[a] +
                    (u - v) * normals[b] +
                    v * normals[c];
            }
            else
            {
                localY =
                    (1f - v) * vertices[a].y +
                    u * vertices[c].y +
                    (v - u) * vertices[d].y;
                localNormal =
                    (1f - v) * normals[a] +
                    u * normals[c] +
                    (v - u) * normals[d];
            }

            height = transform.TransformPoint(
                new Vector3(localProbe.x, localY, localProbe.z)).y;
            normal = transform.TransformDirection(localNormal).normalized;
            return float.IsFinite(height) && normal.sqrMagnitude > 0f;
        }

        internal static bool TrySampleMigrationSourceContactMesh(
            Mesh mesh,
            Transform transform,
            float worldX,
            float worldZ,
            out float height,
            out Vector3 normal,
            out string failure)
        {
            if (transform == null)
            {
                height = 0f;
                normal = Vector3.zero;
                failure = "ENV_E3D_MIGRATION_SOURCE_TRANSFORM_MISSING";
                return false;
            }

            TerrainAuthorityTransformSignature signature =
                TerrainAuthorityTransformSignature.From(transform);
            return TrySampleMigrationSourceContactMesh(
                mesh,
                signature,
                worldX,
                worldZ,
                out height,
                out normal,
                out failure);
        }

        internal static bool TrySampleMigrationSourceContactMesh(
            Mesh mesh,
            in TerrainAuthorityTransformSignature transform,
            float worldX,
            float worldZ,
            out float height,
            out Vector3 normal,
            out string failure)
        {
            height = 0f;
            normal = Vector3.zero;
            failure = string.Empty;
            if (!IsFiniteMigrationValue(worldX) ||
                !IsFiniteMigrationValue(worldZ))
            {
                failure = "ENV_E3D_MIGRATION_SOURCE_REQUEST_INVALID";
                return false;
            }
            if (!transform.TryValidateSupported(out _))
            {
                failure = "ENV_E3D_MIGRATION_SOURCE_TRANSFORM_UNSUPPORTED";
                return false;
            }
            if (!TryValidateMigrationSourceContactMesh(
                    mesh,
                    out Vector3[] vertices,
                    out int[] triangles,
                    out int xCount,
                    out int zCount,
                    out float spacing,
                    out float minX,
                    out float maxX,
                    out float minZ,
                    out float maxZ,
                    out failure))
            {
                return false;
            }

            float localX = worldX - transform.Position.x;
            float localZ = worldZ - transform.Position.z;
            if (!IsFiniteMigrationValue(localX) ||
                !IsFiniteMigrationValue(localZ))
            {
                failure = "ENV_E3D_MIGRATION_SOURCE_REQUEST_INVALID";
                return false;
            }
            if (localX < minX || localX > maxX ||
                localZ < minZ || localZ > maxZ)
            {
                failure = "ENV_E3D_MIGRATION_SOURCE_OUTSIDE_COVERAGE";
                return false;
            }

            float gridX = (localX - minX) / spacing;
            float gridZ = (localZ - minZ) / spacing;
            int cellX = Mathf.Min(Mathf.FloorToInt(gridX), xCount - 2);
            int cellZ = Mathf.Min(Mathf.FloorToInt(gridZ), zCount - 2);
            float u = gridX - cellX;
            float v = gridZ - cellZ;
            if (cellX < 0 || cellZ < 0 ||
                u < 0f || u > 1f || v < 0f || v > 1f)
            {
                failure = "ENV_E3D_MIGRATION_SOURCE_SAMPLE_INVALID";
                return false;
            }

            int a = cellX * zCount + cellZ;
            int b = (cellX + 1) * zCount + cellZ;
            int c = (cellX + 1) * zCount + cellZ + 1;
            int d = cellX * zCount + cellZ + 1;
            float localY;
            int triangleOffset;
            if (v <= u)
            {
                localY =
                    (1f - u) * vertices[a].y +
                    (u - v) * vertices[b].y +
                    v * vertices[c].y;
                triangleOffset =
                    (cellX * (zCount - 1) + cellZ) * 6;
            }
            else
            {
                localY =
                    (1f - v) * vertices[a].y +
                    u * vertices[c].y +
                    (v - u) * vertices[d].y;
                triangleOffset =
                    (cellX * (zCount - 1) + cellZ) * 6 + 3;
            }

            if (!TryComputeMigrationSourceUpwardNormal(
                    vertices[triangles[triangleOffset]],
                    vertices[triangles[triangleOffset + 1]],
                    vertices[triangles[triangleOffset + 2]],
                    out normal))
            {
                failure = "ENV_E3D_MIGRATION_SOURCE_SAMPLE_INVALID";
                return false;
            }
            height = localY + transform.Position.y;
            if (!IsFiniteMigrationValue(height))
            {
                height = 0f;
                normal = Vector3.zero;
                failure = "ENV_E3D_MIGRATION_SOURCE_SAMPLE_INVALID";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static bool TryValidateMigrationSourceContactMesh(
            Mesh mesh,
            out Vector3[] vertices,
            out int[] triangles,
            out int xCount,
            out int zCount,
            out float spacing,
            out float minX,
            out float maxX,
            out float minZ,
            out float maxZ,
            out string failure)
        {
            vertices = null;
            triangles = null;
            xCount = 0;
            zCount = 0;
            spacing = 0f;
            minX = 0f;
            maxX = 0f;
            minZ = 0f;
            maxZ = 0f;
            failure = string.Empty;
            if (mesh == null)
            {
                failure = "ENV_E3D_MIGRATION_SOURCE_MESH_MISSING";
                return false;
            }
            if (!mesh.isReadable)
            {
                failure = "ENV_E3D_MIGRATION_SOURCE_MESH_NOT_READABLE";
                return false;
            }
            if (!string.Equals(
                    mesh.name,
                    MigrationContactMeshName,
                    StringComparison.Ordinal))
            {
                failure = "ENV_E3D_MIGRATION_SOURCE_MESH_NAME_INVALID";
                return false;
            }

            Vector3[] normals;
            Vector2[] uv;
            Bounds serializedBounds;
            try
            {
                if (mesh.subMeshCount != 1 ||
                    mesh.GetTopology(0) != MeshTopology.Triangles)
                {
                    failure = "ENV_E3D_MIGRATION_SOURCE_MESH_STRUCTURE_INVALID";
                    return false;
                }
                vertices = mesh.vertices;
                triangles = mesh.triangles;
                normals = mesh.normals;
                uv = mesh.uv;
                serializedBounds = mesh.bounds;
            }
            catch (Exception)
            {
                failure = "ENV_E3D_MIGRATION_SOURCE_MESH_DATA_UNREADABLE";
                return false;
            }

            if (vertices == null || vertices.Length < 4 ||
                triangles == null ||
                normals == null || normals.Length != vertices.Length ||
                uv == null || uv.Length != vertices.Length ||
                !IsFiniteMigrationVector(serializedBounds.center) ||
                !IsFiniteMigrationVector(serializedBounds.size))
            {
                failure = "ENV_E3D_MIGRATION_SOURCE_MESH_CHANNELS_INVALID";
                return false;
            }

            minX = vertices[0].x;
            minZ = vertices[0].z;
            zCount = 1;
            while (zCount < vertices.Length &&
                   NearMigrationGrid(vertices[zCount].x, minX))
            {
                zCount++;
            }
            if (zCount < 2 || zCount >= vertices.Length ||
                vertices.Length % zCount != 0)
            {
                failure = "ENV_E3D_MIGRATION_SOURCE_GRID_INVALID";
                return false;
            }
            xCount = vertices.Length / zCount;
            if (xCount < 2)
            {
                failure = "ENV_E3D_MIGRATION_SOURCE_GRID_INVALID";
                return false;
            }

            float spacingZ = vertices[1].z - minZ;
            float spacingX = vertices[zCount].x - minX;
            if (!IsFiniteMigrationValue(spacingX) ||
                !IsFiniteMigrationValue(spacingZ) ||
                spacingX <= 0f || spacingZ <= 0f ||
                !NearMigrationGrid(spacingX, spacingZ))
            {
                failure = "ENV_E3D_MIGRATION_SOURCE_GRID_INVALID";
                return false;
            }
            spacing = 0.5f * (spacingX + spacingZ);

            var calculatedBounds = new Bounds(vertices[0], Vector3.zero);
            for (int x = 0; x < xCount; x++)
            {
                float expectedX = minX + x * spacing;
                for (int z = 0; z < zCount; z++)
                {
                    int index = x * zCount + z;
                    float expectedZ = minZ + z * spacing;
                    Vector3 vertex = vertices[index];
                    Vector3 renderNormal = normals[index];
                    Vector2 textureCoordinate = uv[index];
                    if (!IsFiniteMigrationVector(vertex) ||
                        !IsFiniteMigrationVector(renderNormal) ||
                        renderNormal.sqrMagnitude <=
                            MigrationNormalMinimumSquaredMagnitude ||
                        !IsFiniteMigrationValue(textureCoordinate.x) ||
                        !IsFiniteMigrationValue(textureCoordinate.y) ||
                        !NearMigrationGrid(vertex.x, expectedX) ||
                        !NearMigrationGrid(vertex.z, expectedZ) ||
                        !NearMigrationGrid(textureCoordinate.x, expectedX) ||
                        !NearMigrationGrid(textureCoordinate.y, expectedZ))
                    {
                        failure = "ENV_E3D_MIGRATION_SOURCE_GRID_INVALID";
                        return false;
                    }
                    calculatedBounds.Encapsulate(vertex);
                }
            }

            maxX = minX + (xCount - 1) * spacing;
            maxZ = minZ + (zCount - 1) * spacing;
            if (!NearMigrationVector(
                    serializedBounds.center,
                    calculatedBounds.center) ||
                !NearMigrationVector(
                    serializedBounds.size,
                    calculatedBounds.size) ||
                !NearMigrationGrid(serializedBounds.min.x, minX) ||
                !NearMigrationGrid(serializedBounds.max.x, maxX) ||
                !NearMigrationGrid(serializedBounds.min.z, minZ) ||
                !NearMigrationGrid(serializedBounds.max.z, maxZ))
            {
                failure = "ENV_E3D_MIGRATION_SOURCE_BOUNDS_INVALID";
                return false;
            }

            long expectedIndexCount =
                (long)(xCount - 1) * (zCount - 1) * 6;
            if (triangles.LongLength != expectedIndexCount)
            {
                failure = "ENV_E3D_MIGRATION_SOURCE_TOPOLOGY_INVALID";
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
                    if (triangles[cursor++] != a ||
                        triangles[cursor++] != c ||
                        triangles[cursor++] != b ||
                        triangles[cursor++] != a ||
                        triangles[cursor++] != d ||
                        triangles[cursor++] != c ||
                        !TryComputeMigrationSourceUpwardNormal(
                            vertices[a], vertices[c], vertices[b], out _) ||
                        !TryComputeMigrationSourceUpwardNormal(
                            vertices[a], vertices[d], vertices[c], out _))
                    {
                        failure = "ENV_E3D_MIGRATION_SOURCE_TOPOLOGY_INVALID";
                        return false;
                    }
                }
            }

            failure = string.Empty;
            return true;
        }

        private static bool TryComputeMigrationSourceUpwardNormal(
            Vector3 first,
            Vector3 second,
            Vector3 third,
            out Vector3 normal)
        {
            normal = Vector3.zero;
            if (!IsFiniteMigrationVector(first) ||
                !IsFiniteMigrationVector(second) ||
                !IsFiniteMigrationVector(third))
            {
                return false;
            }
            Vector3 value = Vector3.Cross(second - first, third - first);
            float squared = value.sqrMagnitude;
            if (!IsFiniteMigrationVector(value) ||
                !IsFiniteMigrationValue(squared) ||
                squared <= MigrationNormalMinimumSquaredMagnitude)
            {
                return false;
            }
            normal = value / Mathf.Sqrt(squared);
            return IsFiniteMigrationVector(normal) && normal.y > 0f;
        }

        private static bool NearMigrationVector(Vector3 left, Vector3 right)
        {
            return NearMigrationGrid(left.x, right.x) &&
                NearMigrationGrid(left.y, right.y) &&
                NearMigrationGrid(left.z, right.z);
        }

        private static bool NearMigrationGrid(float left, float right)
        {
            float scale = Mathf.Max(
                1f,
                Mathf.Max(Mathf.Abs(left), Mathf.Abs(right)));
            return Mathf.Abs(left - right) <= MigrationGridTolerance * scale;
        }

        private static bool IsFiniteMigrationVector(Vector3 value)
        {
            return IsFiniteMigrationValue(value.x) &&
                IsFiniteMigrationValue(value.y) &&
                IsFiniteMigrationValue(value.z);
        }

        private static bool IsFiniteMigrationValue(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        internal static bool TryApplyRovLandingFit(
            Mesh terrainMesh,
            Transform terrainTransform,
            EnvE2AContactPoint[] contactPoints,
            EnvE3AContinuousSeabedConfiguration configuration,
            out string failureStatus)
        {
            failureStatus = string.Empty;
            if (terrainMesh == null || terrainTransform == null ||
                contactPoints == null || contactPoints.Length != 4 ||
                configuration == null)
            {
                failureStatus =
                    "ENV_E2A_ROV_CONTACT_SURFACE_UNSATISFIABLE";
                return false;
            }

            Vector3[] original = terrainMesh.vertices;
            Vector3[] fitted = (Vector3[])original.Clone();
            Vector3[] localContacts = contactPoints
                .Select(point => terrainTransform.InverseTransformPoint(
                    point.World))
                .ToArray();
            float transitionBand = configuration.ContactGridSpacingMeters;
            float minimumX = localContacts.Min(point => point.x) -
                transitionBand;
            float maximumX = localContacts.Max(point => point.x) +
                transitionBand;
            float minimumZ = localContacts.Min(point => point.z) -
                transitionBand;
            float maximumZ = localContacts.Max(point => point.z) +
                transitionBand;
            for (int index = 0; index < fitted.Length; index++)
            {
                Vector3 vertex = fitted[index];
                float outsideX = Mathf.Max(
                    minimumX - vertex.x, vertex.x - maximumX, 0f);
                float outsideZ = Mathf.Max(
                    minimumZ - vertex.z, vertex.z - maximumZ, 0f);
                float outsideDistance = Mathf.Sqrt(
                    outsideX * outsideX + outsideZ * outsideZ);
                if (outsideDistance >= transitionBand)
                {
                    continue;
                }

                double weightedHeight = 0d;
                double weightSum = 0d;
                foreach (EnvE2AContactPoint contact in contactPoints)
                {
                    Vector3 targetLocal =
                        terrainTransform.InverseTransformPoint(new Vector3(
                            contact.World.x,
                            contact.World.y -
                                configuration.TargetRovClearanceMeters,
                            contact.World.z));
                    float deltaX = vertex.x - targetLocal.x;
                    float deltaZ = vertex.z - targetLocal.z;
                    double weight = 1d / Math.Max(
                        deltaX * deltaX + deltaZ * deltaZ,
                        0.000001d);
                    weightedHeight += weight * targetLocal.y;
                    weightSum += weight;
                }

                float targetY = (float)(weightedHeight / weightSum);
                float blend = outsideDistance <= 0f
                    ? 1f
                    : 1f - Mathf.SmoothStep(
                        0f, 1f, outsideDistance / transitionBand);
                vertex.y = Mathf.Lerp(vertex.y, targetY, blend);
                fitted[index] = vertex;
            }

            terrainMesh.vertices = fitted;
            terrainMesh.RecalculateNormals();
            terrainMesh.RecalculateBounds();
            if (!HasMaximumSlope(
                terrainMesh, configuration.MaximumContactSlopeDegrees))
            {
                RestoreVertices(terrainMesh, original);
                failureStatus =
                    "ENV_E2A_ROV_CONTACT_SURFACE_UNSATISFIABLE";
                return false;
            }

            foreach (EnvE2AContactPoint contact in contactPoints)
            {
                if (!TrySampleContactMesh(
                        terrainMesh, terrainTransform,
                        contact.World.x, contact.World.z,
                        out float surfaceY, out Vector3 unusedNormal))
                {
                    RestoreVertices(terrainMesh, original);
                    failureStatus =
                        "ENV_E2A_ROV_CONTACT_SURFACE_UNSATISFIABLE";
                    return false;
                }

                float clearance = contact.World.y - surfaceY;
                if (!float.IsFinite(clearance) ||
                    clearance < configuration.MinimumRovClearanceMeters -
                        0.00001f ||
                    clearance > configuration.MaximumRovClearanceMeters +
                        0.00001f)
                {
                    RestoreVertices(terrainMesh, original);
                    failureStatus =
                        "ENV_E2A_ROV_CONTACT_SURFACE_UNSATISFIABLE";
                    return false;
                }
            }

            return true;
        }

        internal static string MeshSha256(Mesh mesh)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            using (var memory = new MemoryStream())
            using (var writer = new BinaryWriter(memory))
            {
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                Vector3[] normals = mesh.normals;
                Vector2[] uv = mesh.uv;
                writer.Write(vertices.Length);
                foreach (Vector3 vertex in vertices)
                {
                    WriteVector(writer, vertex);
                }

                writer.Write(triangles.Length);
                foreach (int index in triangles)
                {
                    writer.Write(index);
                }

                writer.Write(uv.Length);
                foreach (Vector2 item in uv)
                {
                    writer.Write(item.x);
                    writer.Write(item.y);
                }

                writer.Write(normals.Length);
                foreach (Vector3 item in normals)
                {
                    WriteVector(writer, item);
                }

                WriteVector(writer, mesh.bounds.center);
                WriteVector(writer, mesh.bounds.size);
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

        internal static void ValidateContactMesh(
            Mesh mesh,
            EnvE3AContinuousSeabedConfiguration configuration)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            if (mesh.vertexCount != configuration.ContactVertexCount ||
                mesh.triangles.Length != configuration.ContactIndexCount ||
                mesh.normals.Length != mesh.vertexCount ||
                mesh.uv.Length != mesh.vertexCount ||
                !string.Equals(
                    mesh.name,
                    configuration.ContactMeshName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Contact terrain mesh does not match the authority grid.");
            }

            int xCount = configuration.ContactGridXCount;
            int zCount = configuration.ContactGridZCount;
            float spacing = configuration.ContactGridSpacingMeters;
            Vector3[] vertices = mesh.vertices;
            Vector2[] uv = mesh.uv;
            for (int xIndex = 0; xIndex < xCount; xIndex++)
            {
                float expectedX = configuration.ContactBounds.MinX +
                    xIndex * spacing;
                for (int zIndex = 0; zIndex < zCount; zIndex++)
                {
                    int index = xIndex * zCount + zIndex;
                    float expectedZ = configuration.ContactBounds.MinZ +
                        zIndex * spacing;
                    if (vertices[index].x != expectedX ||
                        vertices[index].z != expectedZ ||
                        uv[index] != new Vector2(expectedX, expectedZ))
                    {
                        throw new InvalidOperationException(
                            "Contact terrain XZ/UV grid drifted from authority.");
                    }
                }
            }

            int[] triangles = mesh.triangles;
            int cursor = 0;
            for (int xIndex = 0; xIndex < xCount - 1; xIndex++)
            {
                for (int zIndex = 0; zIndex < zCount - 1; zIndex++)
                {
                    int a = xIndex * zCount + zIndex;
                    int b = (xIndex + 1) * zCount + zIndex;
                    int c = (xIndex + 1) * zCount + zIndex + 1;
                    int d = xIndex * zCount + zIndex + 1;
                    if (triangles[cursor++] != a ||
                        triangles[cursor++] != c ||
                        triangles[cursor++] != b ||
                        triangles[cursor++] != a ||
                        triangles[cursor++] != d ||
                        triangles[cursor++] != c)
                    {
                        throw new InvalidOperationException(
                            "Contact terrain topology drifted from authority.");
                    }
                }
            }
        }

        private static bool HasMaximumSlope(Mesh mesh, float maximumDegrees)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            for (int index = 0; index < triangles.Length; index += 3)
            {
                Vector3 a = vertices[triangles[index]];
                Vector3 b = vertices[triangles[index + 1]];
                Vector3 c = vertices[triangles[index + 2]];
                Vector3 faceNormal = Vector3.Cross(b - a, c - a);
                if (faceNormal.sqrMagnitude <= 0f)
                {
                    return false;
                }

                float angle = Vector3.Angle(faceNormal.normalized, Vector3.up);
                angle = Mathf.Min(angle, 180f - angle);
                if (angle > maximumDegrees + 0.0001f)
                {
                    return false;
                }
            }

            return true;
        }

        private static void RestoreVertices(Mesh mesh, Vector3[] vertices)
        {
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private static void WriteVector(BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
        }
    }
}
