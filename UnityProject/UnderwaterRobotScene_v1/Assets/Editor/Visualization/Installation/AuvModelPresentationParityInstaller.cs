using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    public static class AuvModelPresentationParityInstaller
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string BusinessRootPath = "AUV_Yellow_Underwater";
        private const string ImportedRootPath =
            "AUV_Yellow_Underwater/AUV_FineModel_V1_Imported";
        private const string AssetPath = "Assets/Models/AUV/AUV_FineModel_V1.fbx";
        private const string AssetGuid = "01f4e92113033e24a83fefd4d213c91e";

        internal static long FaultInjectionBothRemovedAndPresentSourceId;

        internal sealed class RemovalAudit
        {
            public int ExpectedCount;
            public int RemovedCount;
            public int PresentCount;
            public int MissingCount;
            public bool ExactRemovedSet;
            public bool SourceObjectsPresent;
            public int TransformObjectCount;
            public int TransformPropertyCount;
            public bool TransformParityExact;
            public string[] RemovedPaths;
            public string[] PresentPaths;
        }

        private sealed class RemovalContract
        {
            public string RelativePath;
            public long SourceGameObjectId;
            public long SourceTransformId;
            public int FrozenIndex;

            public string FullScenePath => ImportedRootPath + "/" + RelativePath;
            public string Name =>
                RelativePath.Substring(RelativePath.LastIndexOf('/') + 1);
        }

        private sealed class ResolvedRemoval
        {
            public RemovalContract Contract;
            public GameObject SourceGameObject;
            public Transform SourceTransform;
            public GameObject InstanceGameObject;
            public bool IsRemoved;
        }

        private sealed class PositionProperty
        {
            public string Path;
            public float Value;
        }

        private sealed class TransformParityContract
        {
            public string RelativePath;
            public long SourceGameObjectId;
            public long SourceTransformId;
            public PositionProperty[] Properties;
        }

        private sealed class ResolvedTransformParity
        {
            public TransformParityContract Contract;
            public Transform Instance;
            public Transform Source;
            public PropertyModification[] ActualProperties;
            public bool Exact;
        }

        private sealed class Inspection
        {
            public GameObject PrefabInstanceRoot;
            public ResolvedRemoval[] Items;
            public ResolvedTransformParity[] TransformItems;
            public RemovalAudit Audit;
        }

        private static readonly RemovalContract[] Contracts =
        {
            C("AUV_Lifting_Eye_1_Assembly/AUV_Lifting_Eye_1",
                -5915512702619113444L, 5462670048220848239L, 0),
            C("AUV_Lifting_Eye_1_Assembly/AUV_Lifting_Eye_1_Base",
                -2851847025976376941L, -7212198484463563889L, 1),
            C("AUV_Lifting_Eye_2_Assembly/AUV_Lifting_Eye_2",
                -3453132823976914367L, -1597904326949878856L, 2),
            C("AUV_Lifting_Eye_2_Assembly/AUV_Lifting_Eye_2_Base",
                7595150591780150038L, 188435892960435649L, 3),
            C("AUV_Service_Slot_Left_Dark_Insert",
                5247632831998414241L, 7205862854685256062L, 4),
            C("AUV_Service_Slot_Left_Frame",
                1430321752612278021L, -1026988542566152721L, 5),
            C("AUV_Top_Small_Fitting_Front",
                1843251456211512357L, 8209806912794944493L, 6),
            C("AUV_Top_Small_Fitting_Front_Base",
                -5188039868540026217L, -1280527908344813886L, 7),
            C("AUV_Top_Small_Fitting_Rear",
                -493726054848305735L, -5560176190942758946L, 8),
            C("AUV_Top_Small_Fitting_Rear_Base",
                -5969793128053899409L, 5525776302577949656L, 9)
        };

        private static readonly TransformParityContract[] TransformContracts =
        {
            T("AUV_Nose_Flush_Horizontal_Black_Port",
                2003811651410823271L, 7599478396006025894L,
                P("m_LocalPosition.x", 0.03038f)),
            T("AUV_Nose_Flush_Port_Recess_Shadow",
                -5594406155433125967L, 2617361953750730967L,
                P("m_LocalPosition.x", 0.03026f)),
            T("AUV_Round_Port_Left_Front_Flange",
                -2905786549386309875L, 5231717639298608381L,
                P("m_LocalPosition.y", -0.00364f)),
            T("AUV_Round_Port_Left_Front_Window",
                3947533077261014477L, 560617248469799875L,
                P("m_LocalPosition.y", -0.00384f)),
            T("AUV_Sensor_Window_Left_Frame",
                -7065058890391268023L, -5520465291289709199L,
                P("m_LocalPosition.y", -0.00364f),
                P("m_LocalPosition.z", 0.00038f)),
            T("AUV_Sensor_Window_Left_Glass",
                6916171865144957429L, 7483276913260440510L,
                P("m_LocalPosition.y", -0.00382f),
                P("m_LocalPosition.z", 0.00039f)),
            T("AUV_Tail_Fin_Bottom",
                -3933243924104854319L, -6832152349402094101L,
                P("m_LocalPosition.x", -0.02618f),
                P("m_LocalPosition.z", 0.00358f)),
            T("AUV_Tail_Fin_Top",
                1739982512196367827L, -2856096000616163508L,
                P("m_LocalPosition.x", -0.02622f),
                P("m_LocalPosition.z", -0.00377f))
        };

        [MenuItem("Tools/Underwater Demo/G3/Install AUV Model Presentation Parity")]
        public static void InstallFromMenu()
        {
            bool changed = InstallWithLifecycle();
            Debug.Log("G3 AUV model presentation parity changed=" + changed);
        }

        public static void RunBatch()
        {
            string before = Sha256(File.ReadAllBytes(AbsoluteScenePath()));
            bool changed = InstallWithLifecycle();
            string after = Sha256(File.ReadAllBytes(AbsoluteScenePath()));
            Debug.Log(
                "M_GLOBAL_G3_AUV_MODEL_PRESENTATION_PARITY_INSTALL_PASS | changed=" +
                changed + " | before=" + before + " | after=" + after);
        }

        public static bool InstallForCanonicalSceneRebuild()
        {
            Scene scene = RequireComposableScene();
            Inspection before = Inspect();
            ResolvedRemoval[] pending = before.Items
                .Where(item => !item.IsRemoved)
                .OrderByDescending(item =>
                    item.Contract.RelativePath.Count(character => character == '/'))
                .ThenBy(item => item.Contract.FrozenIndex)
                .ToArray();
            if (pending.Length == 0 && before.Audit.TransformParityExact)
            {
                Require(before.Audit.ExactRemovedSet,
                    "AUV no-op did not have the exact 10-object removed set.");
                return false;
            }

            foreach (ResolvedRemoval item in pending)
            {
                Require(item.InstanceGameObject != null,
                    "A pending AUV removal lost its Scene instance: " +
                    item.Contract.FullScenePath);
                UnityEngine.Object.DestroyImmediate(item.InstanceGameObject);
            }
            ApplyTransformParity(before.TransformItems);
            EditorSceneManager.MarkSceneDirty(scene);

            Inspection after = Inspect();
            Require(after.Audit.ExactRemovedSet,
                "AUV did not establish the exact 10-object removed set.");
            Require(after.Audit.SourceObjectsPresent,
                "One or more AUV source FBX objects disappeared.");
            Require(after.Audit.MissingCount == 0,
                "An AUV target is neither present nor represented as removed.");
            Require(after.Audit.TransformParityExact,
                "AUV presentation Transform parity was not rebuilt exactly.");
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
                        "Unity failed to save the AUV presentation parity Scene.");
                    AssetDatabase.ImportAsset(
                        ScenePath,
                        ImportAssetOptions.ForceSynchronousImport |
                        ImportAssetOptions.ForceUpdate);
                    EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                    RemovalAudit audit = AuditCanonicalScene();
                    Require(audit.ExactRemovedSet &&
                            audit.SourceObjectsPresent &&
                            audit.MissingCount == 0 &&
                            audit.TransformParityExact,
                        "AUV persistent presentation validation failed.");
                }
                else
                {
                    Require(ByteEquals(
                            originalBytes,
                            File.ReadAllBytes(absoluteScenePath)),
                        "AUV no-op changed formal Scene bytes.");
                    Require(!scene.isDirty,
                        "AUV no-op unexpectedly dirtied the formal Scene.");
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
                        "AUV presentation parity failed and rollback also failed. " +
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

        internal static long[] ExpectedSourceGameObjectIds()
        {
            return Contracts.Select(contract => contract.SourceGameObjectId).ToArray();
        }

        internal static string[] ExpectedFullScenePaths()
        {
            return Contracts.Select(contract => contract.FullScenePath).ToArray();
        }

        private static Inspection Inspect()
        {
            RequireSceneTransform(BusinessRootPath);
            Transform importedRoot = RequireSceneTransform(ImportedRootPath);
            GameObject prefabInstanceRoot =
                PrefabUtility.GetOutermostPrefabInstanceRoot(importedRoot.gameObject);
            Require(prefabInstanceRoot != null,
                "Cannot resolve the AUV prefab instance root.");
            Transform sourceRoot =
                PrefabUtility.GetCorrespondingObjectFromSource(importedRoot);
            Require(sourceRoot != null,
                "The AUV imported root is not a source-backed prefab instance.");
            RequireSourceAsset(sourceRoot);
            ResolvedTransformParity[] transformItems =
                ResolveTransformParity(
                    importedRoot,
                    sourceRoot,
                    prefabInstanceRoot);

            var expectedIds = new HashSet<long>(
                Contracts.Select(contract => contract.SourceGameObjectId));
            Require(expectedIds.Count == Contracts.Length,
                "The frozen AUV source GameObject identities are duplicated.");
            Require(Contracts.Select(contract => contract.SourceTransformId)
                        .Distinct().Count() == Contracts.Length,
                "The frozen AUV source Transform identities are duplicated.");

            var removedById = new Dictionary<long, GameObject>();
            foreach (var removed in
                     PrefabUtility.GetRemovedGameObjects(prefabInstanceRoot))
            {
                GameObject assetGameObject = removed.assetGameObject;
                Require(SourceIdentity(
                        assetGameObject,
                        AssetGuid,
                        out long sourceGameObjectId),
                    "An AUV removed object belongs to an unexpected source asset.");
                Require(expectedIds.Contains(sourceGameObjectId),
                    "Unknown AUV removed-object override: " + sourceGameObjectId);
                Require(removedById.TryAdd(sourceGameObjectId, assetGameObject),
                    "Duplicate AUV removed-object identity: " + sourceGameObjectId);
            }

            var items = new List<ResolvedRemoval>();
            foreach (RemovalContract contract in Contracts)
            {
                Transform source = RequireDirectPath(sourceRoot, contract.RelativePath);
                Require(SourceIdentity(
                            source.gameObject,
                            AssetGuid,
                            contract.SourceGameObjectId) &&
                        SourceIdentity(
                            source,
                            AssetGuid,
                            contract.SourceTransformId),
                    "AUV source identity changed: " + contract.RelativePath);
                ValidateSourceParent(sourceRoot, source, contract);

                Transform instance = TryDirectPath(importedRoot, contract.RelativePath);
                GameObject[] sameNameInstances = importedRoot
                    .GetComponentsInChildren<Transform>(true)
                    .Where(candidate => string.Equals(
                        candidate.name,
                        contract.Name,
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
                               contract.SourceGameObjectId);
                }), "AUV contains a same-name wrong-source candidate: " +
                    contract.FullScenePath);

                bool isRemoved = removedById.ContainsKey(contract.SourceGameObjectId);
                if (instance != null)
                {
                    GameObject instanceSource =
                        PrefabUtility.GetCorrespondingObjectFromSource(instance.gameObject);
                    Require(SourceIdentity(
                            instanceSource,
                            AssetGuid,
                            contract.SourceGameObjectId) &&
                            SourceIdentity(
                                PrefabUtility.GetCorrespondingObjectFromSource(instance),
                                AssetGuid,
                                contract.SourceTransformId),
                        "AUV Scene instance source identity changed: " +
                        contract.FullScenePath);
                }

                bool injectedPresent =
                    FaultInjectionBothRemovedAndPresentSourceId ==
                    contract.SourceGameObjectId;
                Require(!(isRemoved && (instance != null || injectedPresent)),
                    "An AUV target is both removed and present: " +
                    contract.FullScenePath);
                Require(isRemoved || instance != null,
                    "An AUV target is neither removed nor present: " +
                    contract.FullScenePath);

                items.Add(new ResolvedRemoval
                {
                    Contract = contract,
                    SourceGameObject = source.gameObject,
                    SourceTransform = source,
                    InstanceGameObject = instance != null ? instance.gameObject : null,
                    IsRemoved = isRemoved
                });
            }

            string[] removedPaths = items
                .Where(item => item.IsRemoved)
                .Select(item => item.Contract.FullScenePath)
                .ToArray();
            string[] presentPaths = items
                .Where(item => !item.IsRemoved)
                .Select(item => item.Contract.FullScenePath)
                .ToArray();
            return new Inspection
            {
                PrefabInstanceRoot = prefabInstanceRoot,
                Items = items.ToArray(),
                TransformItems = transformItems,
                Audit = new RemovalAudit
                {
                    ExpectedCount = Contracts.Length,
                    RemovedCount = removedPaths.Length,
                    PresentCount = presentPaths.Length,
                    MissingCount = items.Count(item =>
                        !item.IsRemoved && item.InstanceGameObject == null),
                    ExactRemovedSet = removedPaths.Length == Contracts.Length,
                    SourceObjectsPresent = items.All(item =>
                        item.SourceGameObject != null &&
                        item.SourceTransform != null),
                    TransformObjectCount = transformItems.Length,
                    TransformPropertyCount = transformItems.Sum(item =>
                        item.ActualProperties.Length),
                    TransformParityExact =
                        transformItems.All(item => item.Exact),
                    RemovedPaths = removedPaths,
                    PresentPaths = presentPaths
                }
            };
        }

        private static ResolvedTransformParity[] ResolveTransformParity(
            Transform importedRoot,
            Transform sourceRoot,
            GameObject prefabInstanceRoot)
        {
            PropertyModification[] allModifications =
                PrefabUtility.GetPropertyModifications(prefabInstanceRoot) ??
                Array.Empty<PropertyModification>();
            return TransformContracts.Select(contract =>
            {
                Transform source =
                    RequireDirectPath(sourceRoot, contract.RelativePath);
                Transform instance =
                    RequireDirectPath(importedRoot, contract.RelativePath);
                Require(SourceIdentity(
                            source.gameObject,
                            AssetGuid,
                            contract.SourceGameObjectId) &&
                        SourceIdentity(
                            source,
                            AssetGuid,
                            contract.SourceTransformId),
                    "AUV presentation Transform source identity changed: " +
                    contract.RelativePath);
                Require(source.parent == sourceRoot,
                    "AUV presentation Transform direct parent changed: " +
                    contract.RelativePath);
                Require(
                    PrefabUtility.GetCorrespondingObjectFromSource(instance) ==
                    source,
                    "AUV presentation Transform instance source changed: " +
                    contract.RelativePath);
                PropertyModification[] actual = allModifications
                    .Where(modification =>
                        modification != null &&
                        modification.target == source &&
                        modification.propertyPath.StartsWith(
                            "m_LocalPosition.",
                            StringComparison.Ordinal))
                    .OrderBy(modification =>
                        modification.propertyPath,
                        StringComparer.Ordinal)
                    .ToArray();
                Require(actual.All(modification =>
                        contract.Properties.Any(property =>
                            string.Equals(
                                property.Path,
                                modification.propertyPath,
                                StringComparison.Ordinal))),
                    "AUV presentation Transform has an unauthorized position override: " +
                    contract.RelativePath);
                return new ResolvedTransformParity
                {
                    Contract = contract,
                    Instance = instance,
                    Source = source,
                    ActualProperties = actual,
                    Exact = MatchesExactProperties(
                        actual,
                        contract.Properties)
                };
            }).ToArray();
        }

        private static void ApplyTransformParity(
            IEnumerable<ResolvedTransformParity> resolved)
        {
            foreach (ResolvedTransformParity item in resolved)
            {
                foreach (PositionProperty property in item.Contract.Properties)
                {
                    PropertyModification existing =
                        item.ActualProperties.SingleOrDefault(modification =>
                            string.Equals(
                                modification.propertyPath,
                                property.Path,
                                StringComparison.Ordinal));
                    if (existing != null &&
                        SerializedFloatEquals(existing.value, property.Value))
                    {
                        continue;
                    }

                    var serialized = new SerializedObject(item.Instance);
                    SerializedProperty value = FindProperty(
                        serialized,
                        property.Path);
                    Require(value != null,
                        "Cannot resolve AUV presentation property: " +
                        item.Contract.RelativePath + " / " + property.Path);
                    value.floatValue = property.Value;
                    bool applied =
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    Require(applied ||
                            BitConverter.SingleToInt32Bits(value.floatValue) ==
                            BitConverter.SingleToInt32Bits(property.Value),
                        "Unity did not apply AUV presentation property: " +
                        item.Contract.RelativePath + " / " + property.Path);
                }
            }
        }

        private static SerializedProperty FindProperty(
            SerializedObject serialized,
            string propertyPath)
        {
            int separator = propertyPath.LastIndexOf('.');
            Require(separator > 0,
                "Invalid AUV presentation property path: " + propertyPath);
            SerializedProperty parent =
                serialized.FindProperty(propertyPath.Substring(0, separator));
            return parent?.FindPropertyRelative(
                propertyPath.Substring(separator + 1));
        }

        private static bool MatchesExactProperties(
            PropertyModification[] actual,
            PositionProperty[] expected)
        {
            return actual.Length == expected.Length &&
                   expected.All(property =>
                       actual.Count(modification =>
                           string.Equals(
                               modification.propertyPath,
                               property.Path,
                               StringComparison.Ordinal) &&
                           SerializedFloatEquals(
                               modification.value,
                               property.Value)) == 1);
        }

        private static bool SerializedFloatEquals(
            string value,
            float expected)
        {
            return float.TryParse(
                       value,
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out float parsed) &&
                   BitConverter.SingleToInt32Bits(parsed) ==
                   BitConverter.SingleToInt32Bits(expected);
        }

        private static void ValidateSourceParent(
            Transform sourceRoot,
            Transform source,
            RemovalContract contract)
        {
            int separator = contract.RelativePath.LastIndexOf('/');
            Transform expectedParent = separator < 0
                ? sourceRoot
                : RequireDirectPath(
                    sourceRoot,
                    contract.RelativePath.Substring(0, separator));
            Require(source.parent == expectedParent,
                "AUV source direct parent changed: " + contract.RelativePath);
        }

        private static void RequireSourceAsset(Transform sourceRoot)
        {
            string actualPath = AssetDatabase.GetAssetPath(sourceRoot);
            Require(string.Equals(actualPath, AssetPath, StringComparison.Ordinal),
                "AUV source asset path changed: " + actualPath);
            Require(string.Equals(
                    AssetDatabase.AssetPathToGUID(actualPath),
                    AssetGuid,
                    StringComparison.Ordinal),
                "AUV source asset GUID changed.");
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
                "Expected one AUV Scene root for path: " + path);
            Transform current = roots[0].transform;
            for (int index = 1; index < segments.Length; index++)
            {
                Transform[] matches = DirectChildren(current, segments[index]);
                Require(matches.Length == 1,
                    "Expected one AUV direct child for path: " + path);
                current = matches[0];
            }

            return current;
        }

        private static Transform RequireDirectPath(
            Transform root,
            string relativePath)
        {
            Transform result = TryDirectPath(root, relativePath, true);
            Require(result != null,
                "AUV source hierarchy path is missing: " + relativePath);
            return result;
        }

        private static Transform TryDirectPath(
            Transform root,
            string relativePath)
        {
            return TryDirectPath(root, relativePath, false);
        }

        private static Transform TryDirectPath(
            Transform root,
            string relativePath,
            bool requireUnique)
        {
            Transform current = root;
            foreach (string segment in relativePath.Split('/'))
            {
                Transform[] matches = DirectChildren(current, segment);
                if (matches.Length == 0)
                {
                    return null;
                }

                if (requireUnique)
                {
                    Require(matches.Length == 1,
                        "AUV hierarchy path is not unique: " + relativePath);
                }
                else if (matches.Length != 1)
                {
                    throw new InvalidOperationException(
                        "AUV Scene hierarchy path is not unique: " +
                        relativePath);
                }

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
                "AUV presentation parity refuses PlayMode.");
            Require(!EditorApplication.isCompiling,
                "AUV presentation parity refuses to run while compiling.");
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded,
                "The AUV formal Scene is not loaded.");
            Require(string.Equals(scene.path, ScenePath, StringComparison.Ordinal),
                "AUV presentation parity may only run on the formal Scene.");
            Require(SceneManager.sceneCount == 1,
                "AUV presentation parity requires exactly one loaded Scene.");
            Require(!scene.isDirty,
                "AUV presentation parity refuses a dirty formal Scene.");
            return scene;
        }

        private static Scene RequireComposableScene()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                "AUV presentation parity refuses PlayMode.");
            Require(!EditorApplication.isCompiling,
                "AUV presentation parity refuses to run while compiling.");
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded,
                "The AUV formal Scene is not loaded.");
            Require(string.Equals(scene.path, ScenePath, StringComparison.Ordinal),
                "AUV presentation parity may only run on the formal Scene.");
            Require(SceneManager.sceneCount == 1,
                "AUV presentation parity requires exactly one loaded Scene.");
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
                "AUV rollback failed to restore the original Scene SHA.");
            Require(!SceneManager.GetActiveScene().isDirty,
                "AUV rollback reopened a dirty Scene.");
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

        private static bool ByteEquals(byte[] left, byte[] right)
        {
            return left.Length == right.Length &&
                   left.SequenceEqual(right);
        }

        private static RemovalContract C(
            string relativePath,
            long sourceGameObjectId,
            long sourceTransformId,
            int frozenIndex)
        {
            return new RemovalContract
            {
                RelativePath = relativePath,
                SourceGameObjectId = sourceGameObjectId,
                SourceTransformId = sourceTransformId,
                FrozenIndex = frozenIndex
            };
        }

        private static TransformParityContract T(
            string relativePath,
            long sourceGameObjectId,
            long sourceTransformId,
            params PositionProperty[] properties)
        {
            return new TransformParityContract
            {
                RelativePath = relativePath,
                SourceGameObjectId = sourceGameObjectId,
                SourceTransformId = sourceTransformId,
                Properties = properties
            };
        }

        private static PositionProperty P(string path, float value)
        {
            return new PositionProperty
            {
                Path = path,
                Value = value
            };
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
