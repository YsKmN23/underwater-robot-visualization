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
    public static class UsvModelPresentationParityInstaller
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string UsvRootPath = "USV_Blue_Surface";
        private const string VisualRootPath =
            "USV_Blue_Surface/USV_SurfaceVisualRoot";
        private const string ImportedRootName = "USV_FineModel_V1_Imported";
        private const string ImportedRootPath =
            VisualRootPath + "/" + ImportedRootName;
        private const string UsvAssetGuid = "a54e024e9e2694149b630b36a3886faf";
        private const string AuvAssetGuid = "01f4e92113033e24a83fefd4d213c91e";
        private const string RovAssetGuid = "81496ffad80dd2d43aeec8986511d0a9";
        private const string PositionX = "m_LocalPosition.x";
        private const string PositionY = "m_LocalPosition.y";
        private const string PositionZ = "m_LocalPosition.z";
        private const float PositionTolerance = 0.0000001f;
        private const float ScaleTolerance = 0.0000001f;
        private const float QuaternionDotTolerance = 0.9999999f;
        private static readonly float NegativeZero =
            BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));

        internal sealed class ContractAudit
        {
            public int ObsoleteObjectCount;
            public int ObsoleteOverridePropertyCount;
            public bool ObsoleteOverridesPresent;
            public bool ObsoleteOverridesAbsent;
            public int ManualObjectCount;
            public int ManualOverridePropertyCount;
            public bool ManualOverridesExact;
            public int AuvRemovedCount;
            public int RovRemovedCount;
            public int UsvRemovedCount;
            public string RemovalMode;
        }

        private sealed class PropertyContract
        {
            public string Path;
            public float Value;

            public PropertyContract(string path, float value)
            {
                Path = path;
                Value = value;
            }
        }

        private sealed class TransformContract
        {
            public string Path;
            public long SourceGameObjectId;
            public long SourceTransformId;
            public Vector3 SourcePosition;
            public Quaternion SourceRotation;
            public Vector3 SourceScale;
            public PropertyContract[] Properties;

            public TransformContract(
                string path,
                long sourceGameObjectId,
                long sourceTransformId,
                Vector3 sourcePosition,
                Quaternion sourceRotation,
                Vector3 sourceScale,
                params PropertyContract[] properties)
            {
                Path = path;
                SourceGameObjectId = sourceGameObjectId;
                SourceTransformId = sourceTransformId;
                SourcePosition = sourcePosition;
                SourceRotation = sourceRotation;
                SourceScale = sourceScale;
                Properties = properties;
            }
        }

        private sealed class ResolvedTransform
        {
            public TransformContract Contract;
            public Transform Instance;
            public Transform Source;
            public PropertyModification[] LocalTransformModifications;
        }

        private sealed class Inspection
        {
            public ResolvedTransform[] Obsolete;
            public ResolvedTransform[] Manual;
            public HashSet<long> AuvRemoved;
            public HashSet<long> RovRemoved;
            public HashSet<long> UsvRemoved;
            public bool ObsoletePresent;
            public bool ObsoleteAbsent;
            public bool ManualExact;
            public ContractAudit Audit;
        }

        private static readonly TransformContract[] ObsoleteContracts =
        {
            O("USV_AftBridge_FrontCrossBeam", 3556991200163212981L, 2969584960308536679L,
                V(-0.00059999997f, 0f, 0.00160341721f), Q(0f, 0f, 0f, 1f), V(1f, 1f, 1f),
                P(PositionZ, 0.00162f)),
            O("USV_Deck_DetailLayer/USV_Hatch_Latch_05", 6351990361574591837L, 5704241312971389843L,
                V(-0.0028f, 0.00468f, 0.00187844422f), Q(0f, 0f, 0f, 1f), V(1f, 1f, 1f),
                P(PositionZ, 0.00152f)),
            O("USV_Deck_DetailLayer/USV_Hatch_Latch_07", -8313513333990789211L, 1450112339515602928L,
                V(-0.0028f, 0.00572000025f, 0.00188327336f), Q(0f, 0f, 0f, 1f), V(1f, 1f, 1f),
                P(PositionY, 0.00572f), P(PositionZ, 0.0015f)),
            O("USV_Deck_DetailLayer/USV_Hatch_Latch_08", 8322339679693854390L, -1385558076445630698L,
                V(-0.0056f, 0.00572000025f, 0.00188545615f), Q(0f, 0f, 0f, 1f), V(1f, 1f, 1f),
                P(PositionY, 0.00572f), P(PositionZ, 0.00182f)),
            O("USV_Deck_DetailLayer/USV_Screw_01", 6601287227782011108L, -5645818967025812478L,
                V(-0.0005f, -0.00619999971f, 0.00112619461f), Q(0f, 0f, 0f, 1f), V(1f, 1f, 1f),
                P(PositionY, -0.0062f), P(PositionZ, 0.00117f)),
            O("USV_Deck_DetailLayer/USV_Screw_02", -8639110817292256393L, 5211601525506336427L,
                V(-0.00379999983f, -0.00619999971f, 0.00120402f), Q(0f, 0f, 0f, 1f), V(1f, 1f, 1f),
                P(PositionY, -0.0062f), P(PositionZ, 0.001243f)),
            O("USV_Deck_DetailLayer/USV_Screw_03", -2484984955985415932L, 8268961036269541307L,
                V(-0.00779999932f, -0.00619999971f, 0.00127838925f), Q(0f, 0f, 0f, 1f), V(1f, 1f, 1f),
                P(PositionY, -0.0062f), P(PositionZ, 0.001292f)),
            O("USV_Deck_DetailLayer/USV_Screw_06", 2720975823246538404L, 8778397170929248698L,
                V(-0.0022f, -0.00519999955f, 0.00152190484f), Q(0f, 0f, 0f, 1f), V(1f, 1f, 1f),
                P(PositionY, -0.0052f), P(PositionZ, 0.001525f)),
            O("USV_Deck_DetailLayer/USV_Screw_07", 8363746884531475831L, -8732389910500054166L,
                V(-0.00619999971f, -0.00519999955f, 0.00149872585f), Q(0f, 0f, 0f, 1f), V(1f, 1f, 1f),
                P(PositionY, -0.0052f), P(PositionZ, 0.001514f)),
            O("USV_Deck_DetailLayer/USV_Screw_09", 7084172365393892433L, -5636257190184610918L,
                V(-0.0005f, 0.0042f, 0.001024479f), Q(0f, 0f, 0f, 1f), V(1f, 1f, 1f),
                P(PositionZ, 0.001797f)),
            O("USV_Deck_DetailLayer/USV_Screw_11", 6532433307566686794L, -7289302485000555301L,
                V(-0.0005f, 0.00619999971f, 0.00114096724f), Q(0f, 0f, 0f, 1f), V(1f, 1f, 1f),
                P(PositionY, 0.0062f), P(PositionZ, 0.001176f)),
            O("USV_Deck_DetailLayer/USV_Screw_12", 8829677920469185345L, -5153260250766296614L,
                V(-0.00379999983f, 0.00619999971f, 0.00119463832f), Q(0f, 0f, 0f, 1f), V(1f, 1f, 1f),
                P(PositionY, 0.0062f), P(PositionZ, 0.00125f)),
            O("USV_Deck_DetailLayer/USV_Screw_13", -4073252244784226018L, 5499645746928411159L,
                V(-0.00779999932f, 0.00619999971f, 0.0012759082f), Q(0f, 0f, 0f, 1f), V(1f, 1f, 1f),
                P(PositionY, 0.0062f), P(PositionZ, 0.001303f)),
            O("USV_Deck_DetailLayer/USV_Screw_14", -8862235109820906382L, -2857974808805093144L,
                V(-0.0022f, 0.00519999955f, 0.00118984282f), Q(0f, 0f, 0f, 1f), V(1f, 1f, 1f),
                P(PositionY, 0.0052f), P(PositionZ, 0.001536f)),
            O("USV_Deck_DetailLayer/USV_Screw_15", 6450618057936746209L, -111684052810396658L,
                V(-0.00619999971f, 0.00519999955f, 0.00152199983f), Q(0f, 0f, 0f, 1f), V(1f, 1f, 1f),
                P(PositionY, 0.0052f), P(PositionZ, 0.001526f)),
            O("USV_Hull_DetailLayer/USV_Rubber_Bumper_L", -4132347665959948535L, -142554765977074443L,
                V(0.001385159f, -0.00748875225f, 0.000296711543f),
                Q(0.00363987777f, -0.05799295f, 0.996349752f, -0.06253582f),
                V(0.99999994f, 1f, 1f),
                P(PositionX, 0.00138f), P(PositionY, -0.00744f), P(PositionZ, 0.00025f)),
            O("USV_Hull_DetailLayer/USV_Rubber_Bumper_L_Segment_01", 7665048171445744250L, -6184354560867417463L,
                V(0.00769168651f, -0.00661333f, 0.000332687865f),
                Q(-0.0156464055f, 0.167704672f, 0.9814509f, -0.091566734f),
                V(1f, 1f, 1f),
                P(PositionX, 0.00749f), P(PositionY, -0.00652f), P(PositionZ, 0.0001f)),
            O("USV_Hull_DetailLayer/USV_Rubber_Bumper_L_Segment_02", 5640610952133074401L, -486252510381998748L,
                V(0.00459976867f, -0.007056837f, 0.0003200101f),
                Q(0.003916237f, -0.0528301746f, 0.9958635f, -0.07382102f),
                V(1f, 0.99999994f, 1f),
                P(PositionX, 0.00459f), P(PositionY, -0.007f), P(PositionZ, 0.0002f)),
            O("USV_Hull_DetailLayer/USV_Rubber_Bumper_L_Segment_03", 2191614151922865096L, -8303370484510951754L,
                V(-0.00205132528f, -0.00785685051f, 0.0002966173f),
                Q(0.00297367712f, -0.06127217f, 0.996943235f, -0.048384f),
                V(1f, 0.99999994f, 1f),
                P(PositionX, -0.00205f), P(PositionY, -0.00783f), P(PositionZ, 0.00036f)),
            O("USV_Hull_DetailLayer/USV_Rubber_Bumper_R", -9024681438153888239L, 8160868297110269845L,
                V(0.00138874934f, 0.00750377867f, 0.00038217305f),
                Q(-0.05800793f, 0.00359252538f, -0.0617087148f, 0.996400654f),
                V(1f, 1f, 1f),
                P(PositionX, 0.00138f), P(PositionY, 0.00745f), P(PositionZ, 0.00035f)),
            O("USV_Hull_DetailLayer/USV_Rubber_Bumper_R_Segment_01", 8580734625482386327L, -7004400549235361943L,
                V(0.0075946264f, 0.00663952343f, 0.000366704131f),
                Q(0.165516287f, -0.01542636f, -0.09150811f, 0.9818313f),
                V(1f, 1f, 1f),
                P(PositionX, 0.00749f), P(PositionY, 0.00651f), P(PositionZ, 0.00021f)),
            O("USV_Hull_DetailLayer/USV_Rubber_Bumper_R_Segment_02", 7488029883586523014L, 5532242623246358069L,
                V(0.004603056f, 0.00708145835f, 0.0003826733f),
                Q(-0.0519971624f, 0.00385574647f, -0.07384942f, 0.995905459f),
                V(1f, 1f, 1f),
                P(PositionX, 0.00459f), P(PositionY, 0.00701f), P(PositionZ, 0.0003f)),
            O("USV_Hull_DetailLayer/USV_Rubber_Bumper_R_Segment_03", -6175968672048726197L, 5403934026158114086L,
                V(-0.00205283077f, 0.007856897f, 0.000337983656f),
                Q(-0.0617137551f, 0.00299456157f, -0.0483737774f, 0.9969165f),
                V(1f, 1f, 1f),
                P(PositionX, -0.00205f), P(PositionY, 0.00784f), P(PositionZ, 0.0004f))
        };

        private static readonly TransformContract[] ManualContracts =
        {
            M("USV_Hull_DetailLayer/USV_Thruster_Mount_Base_L", -7489900470383087003L, -5975752602953212845L,
                V(-0.011f, -0.00519999955f, -0.00309999986f),
                P(PositionY, -0.0052f), P(PositionZ, -0.00282f)),
            M("USV_Hull_DetailLayer/USV_Thruster_Mount_Base_R", 3728000192501475672L, 9205091092225821268L,
                V(-0.011f, 0.00519999955f, -0.00309999986f),
                P(PositionZ, -0.00282f)),
            M("USV_Left_Surface_Thruster/USV_Left_Thruster_Mount_Strut_02", -5843707667221404939L, 4745049426707963793L,
                V(0.000900000334f, 0f, -0.0014500001f),
                P(PositionY, NegativeZero), P(PositionZ, -0.00131f)),
            M("USV_Right_Surface_Thruster/USV_Right_Thruster_Mount_Strut_02", 1496589393367712783L, -686477233632553967L,
                V(0.000900000334f, 0f, -0.0014500001f),
                P(PositionY, NegativeZero), P(PositionZ, -0.00124f))
        };

        private static readonly long[] ExpectedAuvRemoved =
        {
            -5915512702619113444L,
            -2851847025976376941L,
            -3453132823976914367L,
            7595150591780150038L,
            5247632831998414241L,
            1430321752612278021L,
            1843251456211512357L,
            -5188039868540026217L,
            -493726054848305735L,
            -5969793128053899409L
        };

        private static readonly long[] ExpectedRovRemoved =
        {
            9108783129420173466L
        };

        [MenuItem("Tools/Underwater Demo/G2/Install USV Model Presentation Parity")]
        public static void InstallFromMenu()
        {
            bool changed = InstallWithLifecycle();
            Debug.Log("G2 USV model presentation parity changed=" + changed);
        }

        public static void RunBatch()
        {
            bool changed = InstallWithLifecycle();
            Debug.Log(
                "M_GLOBAL_G2_USV_MODEL_PRESENTATION_PARITY_INSTALL_PASS | changed=" +
                changed);
        }

        public static bool InstallForCanonicalSceneRebuild()
        {
            Scene scene = RequireComposableScene();
            Inspection inspection = Inspect();
            bool changed =
                inspection.ObsoletePresent ||
                !inspection.ManualExact;
            if (!changed)
            {
                return false;
            }

            Transform visualRoot = RequireSceneTransform(VisualRootPath);
            Transform importedRoot = RequireSceneTransform(ImportedRootPath);
            Vector3 importedLocalPosition = importedRoot.localPosition;
            Quaternion importedLocalRotation = importedRoot.localRotation;
            Vector3 importedLocalScale = importedRoot.localScale;
            RevertObsoleteOverrides(inspection.Obsolete);
            ApplyManualOverrides(inspection.Manual);
            RestoreComposableVisualHierarchy(
                visualRoot,
                importedLocalPosition,
                importedLocalRotation,
                importedLocalScale);
            EditorSceneManager.MarkSceneDirty(scene);

            Inspection after = Inspect();
            Require(after.ObsoleteAbsent,
                "One or more obsolete USV Transform overrides survived G2.");
            Require(after.ManualExact,
                "The four manual USV Transform contracts were not rebuilt exactly.");
            Require(after.AuvRemoved.SetEquals(inspection.AuvRemoved) &&
                    after.RovRemoved.SetEquals(inspection.RovRemoved) &&
                    after.UsvRemoved.SetEquals(inspection.UsvRemoved),
                "G2 changed an AUV/ROV/USV removed-object override set.");
            return true;
        }

        private static void RestoreComposableVisualHierarchy(
            Transform visualRoot,
            Vector3 importedLocalPosition,
            Quaternion importedLocalRotation,
            Vector3 importedLocalScale)
        {
            Transform usvRoot = RequireSceneTransform(UsvRootPath);
            Transform[] importedRoots = usvRoot
                .GetComponentsInChildren<Transform>(true)
                .Where(item => string.Equals(
                    item.name,
                    ImportedRootName,
                    StringComparison.Ordinal))
                .ToArray();
            Require(importedRoots.Length == 1,
                "G2 could not resolve exactly one imported USV model after " +
                "reverting prefab overrides.");
            Transform importedRoot = importedRoots[0];
            if (importedRoot.parent != visualRoot)
            {
                importedRoot.SetParent(visualRoot, false);
                importedRoot.SetLocalPositionAndRotation(
                    importedLocalPosition,
                    importedLocalRotation);
                importedRoot.localScale = importedLocalScale;
            }

            Require(visualRoot.parent == usvRoot &&
                    usvRoot.childCount == 1 &&
                    visualRoot.childCount == 1 &&
                    visualRoot.GetChild(0) == importedRoot,
                "G2 did not preserve the canonical M2-C visual hierarchy.");
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
                        "Unity failed to save the G2 formal Scene.");
                    AssetDatabase.ImportAsset(
                        ScenePath,
                        ImportAssetOptions.ForceSynchronousImport |
                        ImportAssetOptions.ForceUpdate);
                    EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                    Inspection persisted = Inspect();
                    Require(persisted.ObsoleteAbsent && persisted.ManualExact,
                        "G2 persistent presentation validation failed.");
                }
                else
                {
                    Require(originalBytes.SequenceEqual(
                            File.ReadAllBytes(absoluteScenePath)),
                        "A G2 no-op changed formal Scene bytes.");
                    Require(!scene.isDirty,
                        "A G2 no-op unexpectedly dirtied the formal Scene.");
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
                        "G2 failed and rollback also failed. Original failure: " +
                        failure.Message + " | Rollback failure: " +
                        rollbackFailure.Message,
                        new AggregateException(failure, rollbackFailure));
                }

                throw;
            }
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
            Require(
                string.Equals(
                    Sha256(File.ReadAllBytes(absoluteScenePath)),
                    originalSha,
                    StringComparison.Ordinal),
                "G2 rollback failed to restore the original Scene SHA.");
        }

        internal static ContractAudit AuditCanonicalScene()
        {
            RequireComposableScene();
            return Inspect().Audit;
        }

        private static Inspection Inspect()
        {
            ResolvedTransform[] obsolete =
                ObsoleteContracts.Select(Resolve).ToArray();
            ResolvedTransform[] manual =
                ManualContracts.Select(Resolve).ToArray();

            bool obsoletePresent =
                obsolete.All(item => MatchesExactProperties(
                    item.LocalTransformModifications,
                    item.Contract.Properties));
            bool obsoleteAbsent =
                obsolete.All(item => item.LocalTransformModifications.Length == 0);
            Require(obsoletePresent || obsoleteAbsent,
                "The 23 obsolete USV Transform override set is partial, unknown, or altered.");

            foreach (ResolvedTransform item in manual)
            {
                Require(
                    item.LocalTransformModifications.All(
                        modification => item.Contract.Properties.Any(
                            property => string.Equals(
                                property.Path,
                                modification.propertyPath,
                                StringComparison.Ordinal))),
                    "A manual USV Transform contains an unauthorized local Transform override: " +
                    item.Contract.Path);
            }

            bool manualExact =
                manual.All(item => MatchesExactProperties(
                    item.LocalTransformModifications,
                    item.Contract.Properties));

            HashSet<long> auvRemoved = RemovedIds(
                "AUV_Yellow_Underwater/AUV_FineModel_V1_Imported",
                AuvAssetGuid);
            HashSet<long> rovRemoved = RemovedIds(
                "ROV_Box_Seabed/ROV_FineModel_V1_Imported",
                RovAssetGuid);
            HashSet<long> usvRemoved = RemovedIds(
                "USV_Blue_Surface/USV_SurfaceVisualRoot/USV_FineModel_V1_Imported",
                UsvAssetGuid);
            bool authorityRemovalMode =
                auvRemoved.SetEquals(ExpectedAuvRemoved) &&
                rovRemoved.SetEquals(ExpectedRovRemoved) &&
                usvRemoved.Count == 0;
            bool freshRemovalMode =
                auvRemoved.Count == 0 &&
                rovRemoved.Count == 0 &&
                usvRemoved.Count == 0;
            Require(authorityRemovalMode || freshRemovalMode,
                "The protected 10 AUV + 1 ROV removed-object set is partial or unknown.");

            return new Inspection
            {
                Obsolete = obsolete,
                Manual = manual,
                AuvRemoved = auvRemoved,
                RovRemoved = rovRemoved,
                UsvRemoved = usvRemoved,
                ObsoletePresent = obsoletePresent,
                ObsoleteAbsent = obsoleteAbsent,
                ManualExact = manualExact,
                Audit = new ContractAudit
                {
                    ObsoleteObjectCount = obsolete.Length,
                    ObsoleteOverridePropertyCount =
                        obsolete.Sum(item => item.LocalTransformModifications.Length),
                    ObsoleteOverridesPresent = obsoletePresent,
                    ObsoleteOverridesAbsent = obsoleteAbsent,
                    ManualObjectCount = manual.Length,
                    ManualOverridePropertyCount =
                        manual.Sum(item => item.LocalTransformModifications.Length),
                    ManualOverridesExact = manualExact,
                    AuvRemovedCount = auvRemoved.Count,
                    RovRemovedCount = rovRemoved.Count,
                    UsvRemovedCount = usvRemoved.Count,
                    RemovalMode = authorityRemovalMode ? "AUTHORITY_10_PLUS_1" : "FRESH_ZERO"
                }
            };
        }

        private static ResolvedTransform Resolve(TransformContract contract)
        {
            Transform instance = RequireSceneTransform(contract.Path);
            Transform source =
                PrefabUtility.GetCorrespondingObjectFromSource(instance);
            Require(source != null,
                "The G2 target is not an imported prefab Transform: " + contract.Path);
            Require(SourceIdentity(
                    source.gameObject,
                    UsvAssetGuid,
                    contract.SourceGameObjectId) &&
                    SourceIdentity(
                        source,
                        UsvAssetGuid,
                        contract.SourceTransformId),
                "The G2 target source identity changed: " + contract.Path);
            Require(
                Near(source.localPosition, contract.SourcePosition, PositionTolerance) &&
                Near(source.localScale, contract.SourceScale, ScaleTolerance) &&
                Mathf.Abs(Quaternion.Dot(
                    source.localRotation,
                    contract.SourceRotation)) >= QuaternionDotTolerance,
                "The current FBX source local TRS changed: " + contract.Path);

            GameObject prefabRoot =
                PrefabUtility.GetOutermostPrefabInstanceRoot(instance.gameObject);
            Require(prefabRoot != null,
                "Cannot resolve the USV prefab instance root: " + contract.Path);
            PropertyModification[] modifications =
                PrefabUtility.GetPropertyModifications(prefabRoot) ??
                Array.Empty<PropertyModification>();
            PropertyModification[] localTransformModifications =
                modifications
                    .Where(modification =>
                        modification != null &&
                        modification.target != null &&
                        SourceIdentity(
                            modification.target,
                            UsvAssetGuid,
                            contract.SourceTransformId) &&
                        IsLocalTransformProperty(modification.propertyPath))
                    .OrderBy(modification => modification.propertyPath, StringComparer.Ordinal)
                    .ToArray();

            return new ResolvedTransform
            {
                Contract = contract,
                Instance = instance,
                Source = source,
                LocalTransformModifications = localTransformModifications
            };
        }

        private static void RevertObsoleteOverrides(
            IEnumerable<ResolvedTransform> resolved)
        {
            foreach (ResolvedTransform item in resolved)
            {
                foreach (PropertyContract property in item.Contract.Properties)
                {
                    var serialized = new SerializedObject(item.Instance);
                    SerializedProperty value = FindProperty(serialized, property.Path);
                    Require(value != null,
                        "Cannot resolve the obsolete serialized property: " +
                        item.Contract.Path + " / " + property.Path);
                    PrefabUtility.RevertPropertyOverride(
                        value,
                        InteractionMode.AutomatedAction);
                }
            }
        }

        private static void ApplyManualOverrides(
            IEnumerable<ResolvedTransform> resolved)
        {
            foreach (ResolvedTransform item in resolved)
            {
                foreach (PropertyContract property in item.Contract.Properties)
                {
                    PropertyModification existing =
                        item.LocalTransformModifications.SingleOrDefault(
                            modification => string.Equals(
                                modification.propertyPath,
                                property.Path,
                                StringComparison.Ordinal));
                    if (existing != null &&
                        SerializedFloatEquals(existing.value, property.Value))
                    {
                        continue;
                    }

                    if (BitConverter.SingleToInt32Bits(property.Value) ==
                        unchecked((int)0x80000000))
                    {
                        ApplyNegativeZeroOverride(item, property);
                        continue;
                    }

                    var serialized = new SerializedObject(item.Instance);
                    SerializedProperty value = FindProperty(serialized, property.Path);
                    Require(value != null,
                        "Cannot resolve the manual serialized property: " +
                        item.Contract.Path + " / " + property.Path);
                    if (BitConverter.SingleToInt32Bits(value.floatValue) ==
                        BitConverter.SingleToInt32Bits(property.Value))
                    {
                        value.floatValue = TemporaryValue(property.Value);
                        Require(serialized.ApplyModifiedPropertiesWithoutUndo(),
                            "Unity did not create the manual serialized property override: " +
                            item.Contract.Path + " / " + property.Path);
                        serialized.Update();
                        value = FindProperty(serialized, property.Path);
                        Require(value != null,
                            "Unity lost the manual serialized property after override creation: " +
                            item.Contract.Path + " / " + property.Path);
                    }

                    value.floatValue = property.Value;
                    bool applied = serialized.ApplyModifiedPropertiesWithoutUndo();
                    Require(applied ||
                            BitConverter.SingleToInt32Bits(value.floatValue) ==
                            BitConverter.SingleToInt32Bits(property.Value),
                        "Unity did not apply the manual serialized property: " +
                        item.Contract.Path + " / " + property.Path);
                }
            }
        }

        private static void ApplyNegativeZeroOverride(
            ResolvedTransform item,
            PropertyContract property)
        {
            GameObject prefabRoot =
                PrefabUtility.GetOutermostPrefabInstanceRoot(
                    item.Instance.gameObject);
            Require(prefabRoot != null,
                "Cannot resolve the manual prefab root for negative zero.");
            var modifications = new List<PropertyModification>(
                PrefabUtility.GetPropertyModifications(prefabRoot) ??
                Array.Empty<PropertyModification>());
            modifications.RemoveAll(modification =>
                modification != null &&
                modification.target != null &&
                SourceIdentity(
                    modification.target,
                    UsvAssetGuid,
                    item.Contract.SourceTransformId) &&
                string.Equals(
                    modification.propertyPath,
                    property.Path,
                    StringComparison.Ordinal));
            modifications.Add(new PropertyModification
            {
                target = item.Source,
                propertyPath = property.Path,
                value = "-0",
                objectReference = null
            });
            PrefabUtility.SetPropertyModifications(
                prefabRoot,
                modifications.ToArray());
        }

        private static SerializedProperty FindProperty(
            SerializedObject serialized,
            string propertyPath)
        {
            int separator = propertyPath.LastIndexOf('.');
            Require(separator > 0 && separator + 1 < propertyPath.Length,
                "Invalid serialized property path: " + propertyPath);
            SerializedProperty parent =
                serialized.FindProperty(propertyPath.Substring(0, separator));
            return parent?.FindPropertyRelative(
                propertyPath.Substring(separator + 1));
        }

        private static bool MatchesExactProperties(
            PropertyModification[] actual,
            PropertyContract[] expected)
        {
            if (actual.Length != expected.Length)
            {
                return false;
            }

            return expected.All(property =>
                actual.Count(modification =>
                    string.Equals(
                        modification.propertyPath,
                        property.Path,
                        StringComparison.Ordinal) &&
                    SerializedFloatEquals(
                        modification.value,
                        property.Value)) == 1);
        }

        private static bool SerializedFloatEquals(string value, float expected)
        {
            if (string.Equals(value, "-0", StringComparison.Ordinal) &&
                BitConverter.SingleToInt32Bits(expected) ==
                unchecked((int)0x80000000))
            {
                return true;
            }

            return float.TryParse(
                       value,
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out float parsed) &&
                   BitConverter.SingleToInt32Bits(parsed) ==
                   BitConverter.SingleToInt32Bits(expected);
        }

        private static float TemporaryValue(float target)
        {
            if (target == 0f)
            {
                return 0.0001234567f;
            }

            float offset = Mathf.Max(Mathf.Abs(target) * 0.25f, 0.0001234567f);
            return target + offset;
        }

        private static HashSet<long> RemovedIds(string modelPath, string guid)
        {
            Transform model = RequireSceneTransform(modelPath);
            GameObject prefabRoot =
                PrefabUtility.GetOutermostPrefabInstanceRoot(model.gameObject);
            Require(prefabRoot != null,
                "Cannot resolve a protected prefab instance root: " + modelPath);
            Transform source =
                PrefabUtility.GetCorrespondingObjectFromSource(model);
            Require(source != null &&
                    string.Equals(
                        AssetDatabase.AssetPathToGUID(
                            AssetDatabase.GetAssetPath(source)),
                        guid,
                        StringComparison.Ordinal),
                "A protected imported model GUID changed: " + modelPath);

            var result = new HashSet<long>();
            foreach (var removed in
                     PrefabUtility.GetRemovedGameObjects(prefabRoot))
            {
                GameObject assetGameObject = removed.assetGameObject;
                Require(SourceIdentity(assetGameObject, guid, out long localId),
                    "A protected removed object has an unexpected source asset: " +
                    modelPath);
                Require(result.Add(localId),
                    "A protected removed-object source ID is duplicated: " + localId);
            }

            return result;
        }

        private static bool SourceIdentity(
            UnityEngine.Object value,
            string expectedGuid,
            long expectedLocalId)
        {
            return SourceIdentity(value, expectedGuid, out long localId) &&
                   localId == expectedLocalId;
        }

        private static bool SourceIdentity(
            UnityEngine.Object value,
            string expectedGuid,
            out long localId)
        {
            localId = 0;
            return value != null &&
                   AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                       value,
                       out string guid,
                       out localId) &&
                   string.Equals(guid, expectedGuid, StringComparison.Ordinal);
        }

        private static bool IsLocalTransformProperty(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                   (path.StartsWith("m_LocalPosition.", StringComparison.Ordinal) ||
                    path.StartsWith("m_LocalRotation.", StringComparison.Ordinal) ||
                    path.StartsWith("m_LocalScale.", StringComparison.Ordinal) ||
                    path.StartsWith("m_LocalEulerAnglesHint.", StringComparison.Ordinal));
        }

        private static Transform RequireSceneTransform(string path)
        {
            string[] segments = path.Split('/');
            Require(segments.Length > 0, "Invalid G2 hierarchy path.");
            Scene scene = SceneManager.GetActiveScene();
            GameObject[] roots = scene.GetRootGameObjects()
                .Where(root => string.Equals(
                    root.name,
                    segments[0],
                    StringComparison.Ordinal))
                .ToArray();
            Require(roots.Length == 1,
                "Expected one Scene root for G2 path: " + path);
            Transform current = roots[0].transform;
            for (int index = 1; index < segments.Length; index++)
            {
                Transform[] matches = Enumerable.Range(0, current.childCount)
                    .Select(childIndex => current.GetChild(childIndex))
                    .Where(child => string.Equals(
                        child.name,
                        segments[index],
                        StringComparison.Ordinal))
                    .ToArray();
                Require(matches.Length == 1,
                    "Expected one direct child for G2 path: " + path);
                current = matches[0];
            }

            return current;
        }

        private static Scene RequireFormalScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded,
                "The G2 formal Scene is not loaded.");
            Require(string.Equals(scene.path, ScenePath, StringComparison.Ordinal),
                "G2 may only run on the formal Scene.");
            Require(!scene.isDirty,
                "G2 refuses to run on a dirty formal Scene.");
            return scene;
        }

        private static Scene RequireComposableScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded,
                "The G2 formal Scene is not loaded.");
            Require(string.Equals(scene.path, ScenePath, StringComparison.Ordinal),
                "G2 may only run on the formal Scene.");
            return scene;
        }

        private static TransformContract O(
            string relativePath,
            long sourceGameObjectId,
            long sourceTransformId,
            Vector3 sourcePosition,
            Quaternion sourceRotation,
            Vector3 sourceScale,
            params PropertyContract[] properties)
        {
            return new TransformContract(
                "USV_Blue_Surface/USV_SurfaceVisualRoot/USV_FineModel_V1_Imported/" +
                relativePath,
                sourceGameObjectId,
                sourceTransformId,
                sourcePosition,
                sourceRotation,
                sourceScale,
                properties);
        }

        private static TransformContract M(
            string relativePath,
            long sourceGameObjectId,
            long sourceTransformId,
            Vector3 sourcePosition,
            params PropertyContract[] properties)
        {
            return O(
                relativePath,
                sourceGameObjectId,
                sourceTransformId,
                sourcePosition,
                Quaternion.identity,
                Vector3.one,
                properties);
        }

        private static PropertyContract P(string path, float value)
        {
            return new PropertyContract(path, value);
        }

        private static Vector3 V(float x, float y, float z)
        {
            return new Vector3(x, y, z);
        }

        private static Quaternion Q(float x, float y, float z, float w)
        {
            return new Quaternion(x, y, z, w);
        }

        private static bool Near(Vector3 left, Vector3 right, float tolerance)
        {
            return Mathf.Abs(left.x - right.x) <= tolerance &&
                   Mathf.Abs(left.y - right.y) <= tolerance &&
                   Mathf.Abs(left.z - right.z) <= tolerance;
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
