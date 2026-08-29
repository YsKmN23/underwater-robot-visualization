using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    internal sealed class EnvE2BInstallResult
    {
        internal bool Changed;
        internal string ConfigurationSha256;
        internal string MeshSha256;
        internal int VertexCount;
        internal int IndexCount;
        internal int RendererCount;
        internal int LegacyRendererToggleCount;
    }

    internal sealed class EnvE2BInspection
    {
        internal string RootPath;
        internal string MeshPath;
        internal string MeshSha256;
        internal int VertexCount;
        internal int IndexCount;
        internal int RendererCount;
        internal int ColliderCount;
        internal string MaterialName;
        internal bool[] LegacyRendererEnabled;
    }

    public static class EnvE2BSceneInstaller
    {
        internal const string ExpectedTerrainSha256 =
            "0b073ce749a99dd4d1a3b23b0f24668f2d61b309b9d9d4127e8969962fc10015";

        internal static EnvE2BInstallResult InstallIntoLoadedScene(
            Scene scene,
            EnvE2BConfiguration configuration)
        {
            Require(scene.IsValid() && scene.isLoaded,
                "ENV-E2B requires one valid loaded Scene.");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                "ENV-E2B installation is edit-mode only.");
            Require(configuration != null,
                "ENV-E2B configuration is null.");
            configuration.Validate();

            bool changed = false;
            GameObject environment = RequireSingleRoot(
                scene,
                EnvE2BConfiguration.EnvironmentRootName);
            GameObject seabed = RequireSingleRoot(scene, "Seabed");
            Require(environment.transform.position == Vector3.zero &&
                    environment.transform.rotation == Quaternion.identity &&
                    environment.transform.lossyScale == Vector3.one &&
                    seabed.transform.position == Vector3.zero &&
                    seabed.transform.rotation == Quaternion.identity &&
                    seabed.transform.lossyScale == Vector3.one,
                "Near/far parent transforms must share world identity.");
            MeshFilter seabedFilter = seabed.GetComponent<MeshFilter>();
            MeshRenderer seabedRenderer =
                seabed.GetComponent<MeshRenderer>();
            Require(seabedFilter != null &&
                    seabedFilter.sharedMesh != null &&
                    seabedRenderer != null &&
                    seabedRenderer.sharedMaterial != null &&
                    string.Equals(
                        seabedRenderer.sharedMaterial.name,
                        "Demo_Seabed",
                        StringComparison.Ordinal),
                "Seabed mesh/material authority is missing.");

            Transform e2b = FindDirectChild(
                environment.transform,
                EnvE2BConfiguration.RootName);
            if (e2b == null)
            {
                var root = new GameObject(EnvE2BConfiguration.RootName);
                e2b = root.transform;
                e2b.SetParent(environment.transform, false);
                changed = true;
            }
            Require(environment.transform.Cast<Transform>()
                    .Count(child => string.Equals(
                        child.name,
                        EnvE2BConfiguration.RootName,
                        StringComparison.Ordinal)) == 1,
                "ENV-E2B root is not unique.");
            changed |= EnsureLocalIdentity(e2b);
            Require(e2b.GetComponents<Component>().Length == 1,
                "ENV-E2B root may contain only a Transform.");

            Transform visual = FindDirectChild(
                e2b,
                EnvE2BConfiguration.MeshObjectName);
            if (visual == null)
            {
                var visualObject = new GameObject(
                    EnvE2BConfiguration.MeshObjectName);
                visual = visualObject.transform;
                visual.SetParent(e2b, false);
                changed = true;
            }
            Require(e2b.childCount == 1,
                "ENV-E2B root contains an unauthorized visual child.");
            changed |= EnsureLocalIdentity(visual);
            if (visual.gameObject.layer != 0)
            {
                visual.gameObject.layer = 0;
                changed = true;
            }

            MeshFilter filter = visual.GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = visual.gameObject.AddComponent<MeshFilter>();
                changed = true;
            }
            MeshRenderer renderer = visual.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = visual.gameObject.AddComponent<MeshRenderer>();
                changed = true;
            }
            Require(visual.childCount == 0 &&
                    visual.GetComponents<Component>().All(component =>
                        component is Transform ||
                        component is MeshFilter ||
                        component is MeshRenderer) &&
                    visual.GetComponentsInChildren<Collider>(true).Length == 0 &&
                    visual.GetComponentsInChildren<Rigidbody>(true).Length == 0 &&
                    visual.GetComponentsInChildren<Renderer>(true).Length == 1,
                "Continuous_Enclosure must be strict render-only geometry.");

            Mesh approved = EnvE3ATerrainGeometry.BuildFarRenderMesh(
                seabedFilter.sharedMesh,
                configuration.ContinuousSeabedConfiguration);
            if (!MeshMatches(filter.sharedMesh, approved))
            {
                Mesh previous = filter.sharedMesh;
                filter.sharedMesh = approved;
                DestroyReplacedTransientMesh(previous, approved);
                changed = true;
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(approved);
            }

            if (!ReferenceEquals(
                    renderer.sharedMaterial,
                    seabedRenderer.sharedMaterial))
            {
                renderer.sharedMaterial = seabedRenderer.sharedMaterial;
                changed = true;
            }
            changed |= SetRendererConfiguration(renderer);

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }
            EnvE2BInspection inspection = InspectLoadedScene(scene);
            return new EnvE2BInstallResult
            {
                Changed = changed,
                ConfigurationSha256 = configuration.Sha256(),
                MeshSha256 = inspection.MeshSha256,
                VertexCount = inspection.VertexCount,
                IndexCount = inspection.IndexCount,
                RendererCount = inspection.RendererCount,
                LegacyRendererToggleCount = 0
            };
        }

        internal static EnvE2BInspection InspectLoadedScene(Scene scene)
        {
            GameObject environment = RequireSingleRoot(
                scene,
                EnvE2BConfiguration.EnvironmentRootName);
            Transform root = environment.transform.Cast<Transform>()
                .Single(child => string.Equals(
                    child.name,
                    EnvE2BConfiguration.RootName,
                    StringComparison.Ordinal));
            Require(root.childCount == 1,
                "ENV-E2B visual child count is invalid.");
            Transform visual = root.GetChild(0);
            Require(string.Equals(
                    visual.name,
                    EnvE2BConfiguration.MeshObjectName,
                    StringComparison.Ordinal),
                "ENV-E2B visual object name is invalid.");
            MeshFilter filter = visual.GetComponent<MeshFilter>();
            MeshRenderer renderer = visual.GetComponent<MeshRenderer>();
            Require(filter != null && filter.sharedMesh != null &&
                    renderer != null,
                "ENV-E2B mesh or renderer is missing.");
            return new EnvE2BInspection
            {
                RootPath = "/ENV_E2_Environment/" +
                           EnvE2BConfiguration.RootName,
                MeshPath = "/ENV_E2_Environment/" +
                           EnvE2BConfiguration.RootName + "/" +
                           EnvE2BConfiguration.MeshObjectName,
                MeshSha256 =
                    EnvE3ATerrainGeometry.MeshSha256(filter.sharedMesh),
                VertexCount = filter.sharedMesh.vertexCount,
                IndexCount = filter.sharedMesh.triangles.Length,
                RendererCount =
                    root.GetComponentsInChildren<Renderer>(true).Length,
                ColliderCount =
                    root.GetComponentsInChildren<Collider>(true).Length,
                MaterialName = renderer.sharedMaterial == null
                    ? string.Empty
                    : renderer.sharedMaterial.name,
                LegacyRendererEnabled = Array.Empty<bool>()
            };
        }

        [Obsolete("Use EnvE3ATerrainGeometry.BuildFarRenderMesh with the final contact mesh.")]
        internal static void BuildApprovedMesh(
            out Vector3[] vertices,
            out int[] triangles)
        {
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded,
                "The compatibility mesh view requires an active Scene.");
            GameObject seabed = RequireSingleRoot(scene, "Seabed");
            MeshFilter filter = seabed.GetComponent<MeshFilter>();
            Require(filter != null && filter.sharedMesh != null,
                "The compatibility mesh view requires final /Seabed.");
            Mesh mesh = EnvE3ATerrainGeometry.BuildFarRenderMesh(
                filter.sharedMesh,
                EnvE3AContinuousSeabedConfiguration.CreateApproved());
            try
            {
                vertices = mesh.vertices;
                triangles = mesh.triangles;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        internal static string MeshSha256(Mesh mesh)
        {
            return EnvE3ATerrainGeometry.MeshSha256(mesh);
        }

        private static bool MeshMatches(Mesh current, Mesh approved)
        {
            return current != null && approved != null &&
                   string.Equals(current.name, approved.name,
                       StringComparison.Ordinal) &&
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

        private static bool EnsureLocalIdentity(Transform transform)
        {
            bool changed = false;
            if (transform.localPosition != Vector3.zero)
            {
                transform.localPosition = Vector3.zero;
                changed = true;
            }
            if (transform.localRotation != Quaternion.identity)
            {
                transform.localRotation = Quaternion.identity;
                changed = true;
            }
            if (transform.localScale != Vector3.one)
            {
                transform.localScale = Vector3.one;
                changed = true;
            }
            return changed;
        }

        private static bool SetRendererConfiguration(
            MeshRenderer renderer)
        {
            bool changed = false;
            if (!renderer.enabled)
            {
                renderer.enabled = true;
                changed = true;
            }
            if (renderer.shadowCastingMode != ShadowCastingMode.On)
            {
                renderer.shadowCastingMode = ShadowCastingMode.On;
                changed = true;
            }
            if (!renderer.receiveShadows)
            {
                renderer.receiveShadows = true;
                changed = true;
            }
            return changed;
        }

        private static Transform FindDirectChild(
            Transform parent,
            string name)
        {
            Transform[] matches = parent.Cast<Transform>()
                .Where(child => string.Equals(
                    child.name,
                    name,
                    StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length <= 1,
                "Duplicate direct child: " + name);
            return matches.Length == 0 ? null : matches[0];
        }

        internal static GameObject RequireSingleRoot(
            Scene scene,
            string name)
        {
            GameObject[] roots = scene.GetRootGameObjects()
                .Where(root => string.Equals(
                    root.name,
                    name,
                    StringComparison.Ordinal))
                .ToArray();
            Require(roots.Length == 1,
                "Expected one root named " + name + ".");
            return roots[0];
        }

        internal static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
