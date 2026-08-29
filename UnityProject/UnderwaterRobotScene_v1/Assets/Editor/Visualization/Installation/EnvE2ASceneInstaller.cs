using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    internal sealed class EnvE2AInstallResult
    {
        internal EnvE2AInstallResult(
            bool changed,
            string configurationSha256,
            string[] ownedHierarchyPaths,
            string terrainMeshSha256)
        {
            Changed = changed;
            ConfigurationSha256 = configurationSha256;
            OwnedHierarchyPaths = ownedHierarchyPaths;
            TerrainMeshSha256 = terrainMeshSha256;
        }

        internal bool Changed { get; }
        internal string ConfigurationSha256 { get; }
        internal string[] OwnedHierarchyPaths { get; }
        internal string TerrainMeshSha256 { get; }
    }

    internal sealed class EnvE2AInspection
    {
        internal string ConfigurationSha256 { get; set; }
        internal Vector3 WaterPosition { get; set; }
        internal Vector3 WaterScale { get; set; }
        internal Bounds WaterWorldBounds { get; set; }
        internal int WaterRendererCount { get; set; }
        internal int WaterColliderCount { get; set; }
        internal int WaterProviderCount { get; set; }
        internal Bounds SeabedWorldBounds { get; set; }
        internal string TerrainMeshSha256 { get; set; }
        internal string[] OwnedHierarchyPaths { get; set; }
        internal int VisibleTerrainSurfaceCount { get; set; }
        internal int CollidingTerrainSurfaceCount { get; set; }
        internal int MissingReferenceCount { get; set; }
    }

    internal static class EnvE2ATerrainGeometry
    {
        internal static float EvaluateBaseHeight(
            EnvE2AConfiguration configuration,
            float localX,
            float localZ)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            return EnvE3ATerrainGeometry.EvaluateBaseHeight(
                configuration.ContinuousSeabedConfiguration,
                localX,
                localZ);
        }

        internal static float SampleWorldHeight(
            Mesh terrainMesh,
            Transform terrainTransform,
            float worldX,
            float worldZ)
        {
            if (!EnvE3ATerrainGeometry.TrySampleContactMesh(
                    terrainMesh,
                    terrainTransform,
                    worldX,
                    worldZ,
                    out float height,
                    out Vector3 unusedNormal))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(worldX),
                    "The sample is outside the E3A contact terrain.");
            }

            return height;
        }

        internal static bool TryApplyRovLandingFit(
            Mesh terrainMesh,
            Transform terrainTransform,
            EnvE2AContactPoint[] contactPoints,
            EnvE2AConfiguration configuration,
            out string failureStatus)
        {
            if (configuration == null)
            {
                failureStatus =
                    "ENV_E2A_ROV_CONTACT_SURFACE_UNSATISFIABLE";
                return false;
            }

            return EnvE3ATerrainGeometry.TryApplyRovLandingFit(
                terrainMesh,
                terrainTransform,
                contactPoints,
                configuration.ContinuousSeabedConfiguration,
                out failureStatus);
        }
    }

    public static class EnvE2ASceneInstaller
    {
        private const float WaterThickness = 0.02f;
        private static readonly string[] OwnedPaths =
        {
            "/Water_Surface",
            "/Seabed",
            "/ENV_E2_Environment"
        };
        public static bool InstallForCanonicalSceneRebuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "ENV-E2A installer cannot run in Play Mode.");
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new InvalidOperationException(
                    "ENV-E2A installer requires one loaded active Scene.");
            }

            return InstallIntoLoadedScene(
                scene,
                EnvE2AConfiguration.CreateApproved()).Changed;
        }

        internal static EnvE2AInstallResult InstallIntoLoadedScene(
            Scene scene,
            EnvE2AConfiguration configuration)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new ArgumentException(
                    "A valid loaded Scene is required.",
                    nameof(scene));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            configuration.Validate();
            bool changed = false;
            GameObject water = RequireSingleRoot(scene, "Water_Surface");
            GameObject seabed = RequireSingleRoot(scene, "Seabed");
            RequireSingleRoot(scene, "ENV_E2_Environment");

            RequireComponent<MeshRenderer>(water);
            RequireComponent<MeshFilter>(water);
            RequireComponent<BoxCollider>(water);
            EnvE2APlanarBounds waterBounds = configuration
                .ContinuousSeabedConfiguration
                .WaterBounds;
            changed |= ApplyWaterAuthority(
                water.transform,
                new Vector3(
                    waterBounds.Width,
                    WaterThickness,
                    waterBounds.Depth));

            changed |= SetVector(
                seabed.transform,
                Vector3.zero,
                Vector3.one);
            changed |= SetRotation(seabed.transform, Quaternion.identity);

            MeshFilter meshFilter =
                RequireComponent<MeshFilter>(seabed);
            RequireComponent<MeshRenderer>(seabed);
            Collider[] existingColliders =
                seabed.GetComponents<Collider>();
            MeshCollider meshCollider =
                seabed.GetComponent<MeshCollider>();
            foreach (Collider collider in existingColliders)
            {
                if (collider == meshCollider)
                {
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(collider);
                changed = true;
            }

            if (meshCollider == null)
            {
                meshCollider = seabed.AddComponent<MeshCollider>();
                changed = true;
            }

            Mesh approvedMesh = BuildTerrainMesh(configuration);
            GameObject rov = RequireSingleRoot(
                scene,
                "ROV_Box_Seabed");
            if (!EnvE2ARovContactGeometry
                .TryResolveContactAuthority(
                    rov.transform,
                    out EnvE2AContactPoint[] contactPoints,
                    out EnvE2AContactAuthorityEvidence contactEvidence))
            {
                UnityEngine.Object.DestroyImmediate(approvedMesh);
                throw new InvalidOperationException(
                    "ENV_E2A_ROV_CONTACT_AUTHORITY_AMBIGUOUS | " +
                    "USER_REVIEW_REQUIRED");
            }

            string landingFailure = string.Empty;
            if (string.IsNullOrEmpty(contactEvidence.Sha256()) ||
                !EnvE2ATerrainGeometry.TryApplyRovLandingFit(
                    approvedMesh,
                    seabed.transform,
                    contactPoints,
                    configuration,
                    out landingFailure))
            {
                UnityEngine.Object.DestroyImmediate(approvedMesh);
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(landingFailure)
                        ? "ENV_E2A_ROV_CONTACT_SURFACE_UNSATISFIABLE"
                        : landingFailure);
            }

            if (!MeshMatches(meshFilter.sharedMesh, approvedMesh))
            {
                Mesh previousFilterMesh = meshFilter.sharedMesh;
                Mesh previousColliderMesh = meshCollider.sharedMesh;
                meshFilter.sharedMesh = approvedMesh;
                meshCollider.sharedMesh = approvedMesh;
                DestroyReplacedTransientMesh(
                    previousFilterMesh, approvedMesh);
                if (previousColliderMesh != previousFilterMesh)
                {
                    DestroyReplacedTransientMesh(
                        previousColliderMesh, approvedMesh);
                }

                changed = true;
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(approvedMesh);
                if (meshCollider.sharedMesh != meshFilter.sharedMesh)
                {
                    meshCollider.sharedMesh = meshFilter.sharedMesh;
                    changed = true;
                }
            }

            if (!meshCollider.enabled)
            {
                meshCollider.enabled = true;
                changed = true;
            }

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }

            string meshSha = TerrainMeshSha256(
                meshFilter.sharedMesh);
            return new EnvE2AInstallResult(
                changed,
                configuration.Sha256(),
                (string[])OwnedPaths.Clone(),
                meshSha);
        }

        internal static EnvE2AInspection InspectLoadedScene(
            Scene scene,
            EnvE2AConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            GameObject water = RequireSingleRoot(scene, "Water_Surface");
            GameObject seabed = RequireSingleRoot(scene, "Seabed");
            RequireSingleRoot(scene, "ENV_E2_Environment");
            MeshRenderer waterRenderer =
                RequireComponent<MeshRenderer>(water);
            RequireComponent<MeshFilter>(water);
            RequireComponent<BoxCollider>(water);
            int waterProviderCount = RequireSingleComponentByTypeName(
                water,
                "FlatWaterSurfaceProvider");
            MeshFilter meshFilter =
                RequireComponent<MeshFilter>(seabed);
            MeshRenderer renderer =
                RequireComponent<MeshRenderer>(seabed);
            MeshCollider collider =
                RequireComponent<MeshCollider>(seabed);
            Bounds localBounds = meshFilter.sharedMesh.bounds;
            Bounds worldBounds = TransformBounds(
                seabed.transform.localToWorldMatrix,
                localBounds);
            return new EnvE2AInspection
            {
                ConfigurationSha256 = configuration.Sha256(),
                WaterPosition = water.transform.position,
                WaterScale = water.transform.localScale,
                WaterWorldBounds = waterRenderer.bounds,
                WaterRendererCount =
                    water.GetComponents<Renderer>().Length,
                WaterColliderCount =
                    water.GetComponents<Collider>().Length,
                WaterProviderCount = waterProviderCount,
                SeabedWorldBounds = worldBounds,
                TerrainMeshSha256 =
                    TerrainMeshSha256(meshFilter.sharedMesh),
                OwnedHierarchyPaths = (string[])OwnedPaths.Clone(),
                VisibleTerrainSurfaceCount =
                    renderer.enabled ? 1 : 0,
                CollidingTerrainSurfaceCount =
                    collider.enabled ? 1 : 0,
                MissingReferenceCount = 0
            };
        }

        internal static string TerrainMeshSha256(Mesh mesh)
        {
            return EnvE3ATerrainGeometry.MeshSha256(mesh);
        }

        private static Mesh BuildTerrainMesh(
            EnvE2AConfiguration configuration)
        {
            return EnvE3ATerrainGeometry.BuildContactMesh(
                configuration.ContinuousSeabedConfiguration);
        }

        private static bool MeshMatches(Mesh current, Mesh approved)
        {
            return current != null &&
                current.name == approved.name &&
                current.vertices.SequenceEqual(approved.vertices) &&
                current.triangles.SequenceEqual(approved.triangles) &&
                current.uv.SequenceEqual(approved.uv) &&
                current.normals.SequenceEqual(approved.normals) &&
                current.bounds == approved.bounds;
        }

        private static void DestroyReplacedTransientMesh(
            Mesh previous,
            Mesh replacement)
        {
            if (previous != null && previous != replacement &&
                !EditorUtility.IsPersistent(previous))
            {
                UnityEngine.Object.DestroyImmediate(previous);
            }
        }

        private static bool SetVector(
            Transform transform,
            Vector3 position,
            Vector3 scale)
        {
            bool changed = false;
            if (transform.position != position)
            {
                transform.position = position;
                changed = true;
            }

            if (transform.localScale != scale)
            {
                transform.localScale = scale;
                changed = true;
            }

            return changed;
        }

        private static bool SetRotation(
            Transform transform,
            Quaternion rotation)
        {
            if (transform.rotation == rotation)
            {
                return false;
            }

            transform.rotation = rotation;
            return true;
        }

        private static bool ApplyWaterAuthority(
            Transform transform,
            Vector3 scale)
        {
            if (transform.position == Vector3.zero &&
                transform.rotation == Quaternion.identity &&
                transform.localScale == scale)
            {
                return false;
            }

            Undo.RecordObject(
                transform,
                "Apply ENV-E2A water authority");
            transform.position = Vector3.zero;
            transform.rotation = Quaternion.identity;
            transform.localScale = scale;
            EditorUtility.SetDirty(transform);
            return true;
        }

        private static GameObject RequireSingleRoot(
            Scene scene,
            string name)
        {
            GameObject[] matches = scene.GetRootGameObjects()
                .Where(root => string.Equals(
                    root.name,
                    name,
                    StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    "Expected exactly one root /" + name +
                    " but found " + matches.Length + ".");
            }

            return matches[0];
        }

        private static T RequireComponent<T>(GameObject gameObject)
            where T : Component
        {
            T component = gameObject.GetComponent<T>();
            if (component == null)
            {
                throw new InvalidOperationException(
                    gameObject.name + " is missing " +
                    typeof(T).Name + ".");
            }

            return component;
        }

        private static int RequireSingleComponentByTypeName(
            GameObject gameObject,
            string typeName)
        {
            int count = gameObject.GetComponents<Component>()
                .Count(component =>
                    component != null &&
                    string.Equals(
                        component.GetType().Name,
                        typeName,
                        StringComparison.Ordinal));
            if (count != 1)
            {
                throw new InvalidOperationException(
                    gameObject.name + " expected exactly one " +
                    typeName + " but found " + count + ".");
            }

            return count;
        }

        private static Bounds TransformBounds(
            Matrix4x4 matrix,
            Bounds bounds)
        {
            Vector3 center = matrix.MultiplyPoint3x4(bounds.center);
            Vector3 extents = bounds.extents;
            Vector3 axisX = matrix.MultiplyVector(
                new Vector3(extents.x, 0f, 0f));
            Vector3 axisY = matrix.MultiplyVector(
                new Vector3(0f, extents.y, 0f));
            Vector3 axisZ = matrix.MultiplyVector(
                new Vector3(0f, 0f, extents.z));
            extents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) +
                    Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) +
                    Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) +
                    Mathf.Abs(axisZ.z));
            return new Bounds(center, extents * 2f);
        }

    }
}
