using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    public static class RovModelPresentationParityInstaller
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string BusinessRootPath = "ROV_Box_Seabed";
        private const string ImportedRootPath =
            "ROV_Box_Seabed/ROV_FineModel_V1_Imported";
        private const string AssetPath = "Assets/Models/ROV/ROV_FineModel_V1.fbx";
        private const string AssetGuid = "81496ffad80dd2d43aeec8986511d0a9";
        private const string RelativePath = "ROV_Top_Ring_Faces_Front";
        private const long SourceGameObjectId = 9108783129420173466L;
        private const long SourceTransformId = -3287885758762120742L;

        internal sealed class RemovalAudit
        {
            public int ExpectedCount;
            public int RemovedCount;
            public int PresentCount;
            public int MissingCount;
            public bool ExactRemovedSet;
            public bool SourceObjectPresent;
            public string FullScenePath;
        }

        private sealed class Inspection
        {
            public GameObject PrefabInstanceRoot;
            public GameObject InstanceGameObject;
            public bool IsRemoved;
            public RemovalAudit Audit;
        }

        [MenuItem("Tools/Underwater Demo/G3/Install ROV Model Presentation Parity")]
        public static void InstallFromMenu()
        {
            bool changed = InstallWithLifecycle();
            Debug.Log("G3 ROV model presentation parity changed=" + changed);
        }

        public static void RunBatch()
        {
            string before = Sha256(File.ReadAllBytes(AbsoluteScenePath()));
            bool changed = InstallWithLifecycle();
            string after = Sha256(File.ReadAllBytes(AbsoluteScenePath()));
            Debug.Log(
                "M_GLOBAL_G3_ROV_MODEL_PRESENTATION_PARITY_INSTALL_PASS | changed=" +
                changed + " | before=" + before + " | after=" + after);
        }

        public static bool InstallForCanonicalSceneRebuild()
        {
            Scene scene = RequireComposableScene();
            Inspection before = Inspect();
            if (before.IsRemoved)
            {
                Require(before.Audit.ExactRemovedSet,
                    "ROV no-op did not have the exact one-object removed set.");
                return false;
            }

            Require(before.InstanceGameObject != null,
                "The pending ROV removal lost its Scene instance.");
            UnityEngine.Object.DestroyImmediate(before.InstanceGameObject);
            EditorSceneManager.MarkSceneDirty(scene);

            Inspection after = Inspect();
            Require(after.Audit.ExactRemovedSet,
                "ROV did not establish the exact one-object removed set.");
            Require(after.Audit.SourceObjectPresent,
                "The ROV source FBX object disappeared.");
            Require(after.Audit.MissingCount == 0,
                "The ROV target is neither present nor represented as removed.");
            return true;
        }

        private static bool InstallWithLifecycle()
        {
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            RequireFormalScene();
            string absoluteScenePath = AbsoluteScenePath();
            byte[] originalBytes = File.ReadAllBytes(absoluteScenePath);
            string originalSha = Sha256(originalBytes);
            try
            {
                bool changed = InstallForCanonicalSceneRebuild();
                if (changed)
                {
                    Require(EditorSceneManager.SaveScene(scene),
                        "Unity failed to save the ROV presentation parity Scene.");
                    AssetDatabase.ImportAsset(
                        ScenePath,
                        ImportAssetOptions.ForceSynchronousImport |
                        ImportAssetOptions.ForceUpdate);
                    EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                    RemovalAudit audit = Inspect().Audit;
                    Require(audit.ExactRemovedSet &&
                            audit.SourceObjectPresent &&
                            audit.MissingCount == 0,
                        "ROV persistent presentation validation failed.");
                }
                else
                {
                    Require(originalBytes.SequenceEqual(
                            File.ReadAllBytes(absoluteScenePath)),
                        "ROV no-op changed formal Scene bytes.");
                    Require(!scene.isDirty,
                        "ROV no-op unexpectedly dirtied the formal Scene.");
                }

                return changed;
            }
            catch (Exception failure)
            {
                try
                {
                    RestoreOriginalScene(
                        absoluteScenePath,
                        originalBytes,
                        originalSha);
                }
                catch (Exception rollbackFailure)
                {
                    throw new InvalidOperationException(
                        "ROV presentation parity failed and rollback also failed. " +
                        "Original failure: " + failure.Message +
                        " | Rollback failure: " + rollbackFailure.Message,
                        new AggregateException(failure, rollbackFailure));
                }

                throw;
            }
        }

        internal static RemovalAudit AuditCanonicalScene()
        {
            RequireComposableScene();
            return Inspect().Audit;
        }

        internal static long ExpectedSourceGameObjectId()
        {
            return SourceGameObjectId;
        }

        internal static string ExpectedFullScenePath()
        {
            return ImportedRootPath + "/" + RelativePath;
        }

        private static Inspection Inspect()
        {
            RequireSceneTransform(BusinessRootPath);
            Transform importedRoot = RequireSceneTransform(ImportedRootPath);
            GameObject prefabInstanceRoot =
                PrefabUtility.GetOutermostPrefabInstanceRoot(importedRoot.gameObject);
            Require(prefabInstanceRoot != null,
                "Cannot resolve the ROV prefab instance root.");
            Transform sourceRoot =
                PrefabUtility.GetCorrespondingObjectFromSource(importedRoot);
            Require(sourceRoot != null,
                "The ROV imported root is not a source-backed prefab instance.");
            string actualPath = AssetDatabase.GetAssetPath(sourceRoot);
            Require(string.Equals(actualPath, AssetPath, StringComparison.Ordinal),
                "ROV source asset path changed: " + actualPath);
            Require(string.Equals(
                    AssetDatabase.AssetPathToGUID(actualPath),
                    AssetGuid,
                    StringComparison.Ordinal),
                "ROV source asset GUID changed.");

            Transform source = RequireDirectPath(sourceRoot, RelativePath);
            Require(SourceIdentity(
                        source.gameObject,
                        AssetGuid,
                        SourceGameObjectId) &&
                    SourceIdentity(
                        source,
                        AssetGuid,
                        SourceTransformId),
                "ROV source identity changed.");
            Require(source.parent == sourceRoot,
                "ROV source direct parent changed.");

            var removedById = new Dictionary<long, GameObject>();
            foreach (var removed in
                     PrefabUtility.GetRemovedGameObjects(prefabInstanceRoot))
            {
                GameObject assetGameObject = removed.assetGameObject;
                Require(SourceIdentity(
                        assetGameObject,
                        AssetGuid,
                        out long sourceGameObjectId),
                    "A ROV removed object belongs to an unexpected source asset.");
                Require(sourceGameObjectId == SourceGameObjectId,
                    "Unknown ROV removed-object override: " + sourceGameObjectId);
                Require(removedById.TryAdd(sourceGameObjectId, assetGameObject),
                    "Duplicate ROV removed-object identity.");
            }

            Transform instance = TryDirectPath(importedRoot, RelativePath);
            GameObject[] sameNameInstances = importedRoot
                .GetComponentsInChildren<Transform>(true)
                .Where(candidate => string.Equals(
                    candidate.name,
                    RelativePath,
                    StringComparison.Ordinal))
                .Select(candidate => candidate.gameObject)
                .ToArray();
            Require(sameNameInstances.All(candidate =>
            {
                GameObject candidateSource =
                    PrefabUtility.GetCorrespondingObjectFromSource(candidate);
                return SourceIdentity(
                           candidateSource,
                           AssetGuid,
                           SourceGameObjectId);
            }), "ROV contains a same-name wrong-source candidate.");

            bool isRemoved = removedById.ContainsKey(SourceGameObjectId);
            if (instance != null)
            {
                Require(SourceIdentity(
                            PrefabUtility.GetCorrespondingObjectFromSource(
                                instance.gameObject),
                            AssetGuid,
                            SourceGameObjectId) &&
                        SourceIdentity(
                            PrefabUtility.GetCorrespondingObjectFromSource(instance),
                            AssetGuid,
                            SourceTransformId),
                    "ROV Scene instance source identity changed.");
            }

            Require(!(isRemoved && instance != null),
                "The ROV target is both removed and present.");
            Require(isRemoved || instance != null,
                "The ROV target is neither removed nor present.");

            return new Inspection
            {
                PrefabInstanceRoot = prefabInstanceRoot,
                InstanceGameObject = instance != null ? instance.gameObject : null,
                IsRemoved = isRemoved,
                Audit = new RemovalAudit
                {
                    ExpectedCount = 1,
                    RemovedCount = isRemoved ? 1 : 0,
                    PresentCount = instance != null ? 1 : 0,
                    MissingCount = !isRemoved && instance == null ? 1 : 0,
                    ExactRemovedSet = isRemoved && removedById.Count == 1,
                    SourceObjectPresent = source != null,
                    FullScenePath = ExpectedFullScenePath()
                }
            };
        }

        private static Transform RequireSceneTransform(string path)
        {
            string[] segments = path.Split('/');
            Scene scene = SceneManager.GetActiveScene();
            GameObject[] roots = scene.GetRootGameObjects()
                .Where(root => string.Equals(
                    root.name,
                    segments[0],
                    StringComparison.Ordinal))
                .ToArray();
            Require(roots.Length == 1,
                "Expected one ROV Scene root for path: " + path);
            Transform current = roots[0].transform;
            for (int index = 1; index < segments.Length; index++)
            {
                Transform[] matches = DirectChildren(current, segments[index]);
                Require(matches.Length == 1,
                    "Expected one ROV direct child for path: " + path);
                current = matches[0];
            }

            return current;
        }

        private static Transform RequireDirectPath(
            Transform root,
            string relativePath)
        {
            Transform result = TryDirectPath(root, relativePath);
            Require(result != null,
                "ROV source hierarchy path is missing: " + relativePath);
            return result;
        }

        private static Transform TryDirectPath(
            Transform root,
            string relativePath)
        {
            Transform current = root;
            foreach (string segment in relativePath.Split('/'))
            {
                Transform[] matches = DirectChildren(current, segment);
                if (matches.Length == 0)
                {
                    return null;
                }

                Require(matches.Length == 1,
                    "ROV hierarchy path is not unique: " + relativePath);
                current = matches[0];
            }

            return current;
        }

        private static Transform[] DirectChildren(
            Transform parent,
            string name)
        {
            return Enumerable.Range(0, parent.childCount)
                .Select(index => parent.GetChild(index))
                .Where(child => string.Equals(
                    child.name,
                    name,
                    StringComparison.Ordinal))
                .ToArray();
        }

        private static bool SourceIdentity(
            UnityEngine.Object value,
            string expectedGuid,
            long expectedLocalId)
        {
            return SourceIdentity(value, expectedGuid, out long actualLocalId) &&
                   actualLocalId == expectedLocalId;
        }

        private static bool SourceIdentity(
            UnityEngine.Object value,
            string expectedGuid,
            out long actualLocalId)
        {
            actualLocalId = 0;
            return value != null &&
                   AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                       value,
                       out string actualGuid,
                       out actualLocalId) &&
                   string.Equals(
                       actualGuid,
                       expectedGuid,
                       StringComparison.Ordinal);
        }

        private static Scene RequireFormalScene()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                "ROV presentation parity refuses PlayMode.");
            Require(!EditorApplication.isCompiling,
                "ROV presentation parity refuses to run while compiling.");
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded,
                "The ROV formal Scene is not loaded.");
            Require(string.Equals(scene.path, ScenePath, StringComparison.Ordinal),
                "ROV presentation parity may only run on the formal Scene.");
            Require(SceneManager.sceneCount == 1,
                "ROV presentation parity requires exactly one loaded Scene.");
            Require(!scene.isDirty,
                "ROV presentation parity refuses a dirty formal Scene.");
            return scene;
        }

        private static Scene RequireComposableScene()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                "ROV presentation parity refuses PlayMode.");
            Require(!EditorApplication.isCompiling,
                "ROV presentation parity refuses to run while compiling.");
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded,
                "The ROV formal Scene is not loaded.");
            Require(string.Equals(scene.path, ScenePath, StringComparison.Ordinal),
                "ROV presentation parity may only run on the formal Scene.");
            Require(SceneManager.sceneCount == 1,
                "ROV presentation parity requires exactly one loaded Scene.");
            return scene;
        }

        private static void RestoreOriginalScene(
            string absoluteScenePath,
            byte[] originalBytes,
            string originalSha)
        {
            File.WriteAllBytes(absoluteScenePath, originalBytes);
            AssetDatabase.ImportAsset(
                ScenePath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Require(string.Equals(
                    Sha256(File.ReadAllBytes(absoluteScenePath)),
                    originalSha,
                    StringComparison.Ordinal),
                "ROV rollback failed to restore the original Scene SHA.");
            Require(!SceneManager.GetActiveScene().isDirty,
                "ROV rollback reopened a dirty Scene.");
        }

        private static string AbsoluteScenePath()
        {
            return Path.GetFullPath(Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                ScenePath));
        }

        private static string Sha256(byte[] bytes)
        {
            using SHA256 hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(bytes))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
