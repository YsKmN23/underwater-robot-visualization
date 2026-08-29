using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Auv;
using UnderwaterRobotScene.Visualization.Runtime.Rov;
using UnderwaterRobotScene.Visualization.Runtime.Terrain;
using UnderwaterRobotScene.Visualization.Runtime.Usv;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    internal enum EnvE3BRovContactInstallMode
    {
        Apply,
        RequireNoOp
    }

    [Serializable]
    internal sealed class EnvE3BRovContactInstallResult
    {
        internal bool Success;
        internal bool Changed;
        internal bool SceneSaved;
        internal bool SamplerAdded;
        internal bool ConstraintAdded;
        internal bool SamplerBindingChanged;
        internal bool ProfileChanged;
        internal bool WaterBindingChanged;
        internal bool DriverBindingChanged;
        internal int DuplicateComponentCount;
        internal string FailureStage;
        internal string FailureMessage;
    }

    public static class EnvE3BRovContactSceneInstaller
    {
        internal sealed class LegacyPostBuildCompatibilityScope : IDisposable
        {
            private readonly Scene scene;
            private readonly int undoGroup;
            private bool disposed;

            internal LegacyPostBuildCompatibilityScope(
                Scene value,
                int group)
            {
                scene = value;
                undoGroup = group;
            }

            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                Undo.RevertAllDownToGroup(undoGroup);
                Require(scene.IsValid() && scene.isLoaded && !scene.isDirty,
                    "Legacy post-build compatibility did not restore a " +
                    "clean Candidate Scene.");
                GameObject driverObject = RequireUniqueRoot(
                    scene, "ROV_PublicPoseDriver");
                VehiclePoseDriver restoredDriver =
                    RequireComponent<VehiclePoseDriver>(
                        driverObject,
                        "/ROV_PublicPoseDriver VehiclePoseDriver");
                TerrainSurfaceSampler restoredSampler =
                    RequireComponent<TerrainSurfaceSampler>(
                        driverObject,
                        "/ROV_PublicPoseDriver TerrainSurfaceSampler");
                RovTerrainContactConstraint restoredConstraint =
                    RequireComponent<RovTerrainContactConstraint>(
                        driverObject,
                        "/ROV_PublicPoseDriver RovTerrainContactConstraint");
                Require(restoredDriver != null && restoredSampler != null &&
                        restoredConstraint != null &&
                        ReferenceEquals(restoredDriver.PoseConstraintProvider,
                            restoredConstraint) &&
                        ReferenceEquals(restoredConstraint.SurfaceSampler,
                            restoredSampler),
                    "Legacy post-build compatibility did not restore exact " +
                    "E3B bindings.");
            }
        }

        private const string ScenePath =
            "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string TemplatePath =
            "Assets/Editor/Visualization/Installation/" +
            "UnderwaterRobotDemo_Canonical.unity";
        private const string ManifestPath =
            "Assets/Editor/Visualization/Installation/" +
            "CanonicalSceneIdentityManifest.json";
        private const string ReportPathArgument =
            "-envE3BRovContactInstallerReportPath";
        private const string Batch1ReportPathArgument =
            "-rovVerticalBatch1InstallerReportPath";

        [Serializable]
        private sealed class CandidateBuildReport
        {
            public string schema;
            public string status;
            public string unityVersion;
            public bool success;
            public bool changed;
            public bool sceneSaved;
            public bool samplerAdded;
            public bool constraintAdded;
            public bool samplerBindingChanged;
            public bool profileChanged;
            public bool waterBindingChanged;
            public bool driverBindingChanged;
            public int duplicateComponentCount;
            public bool requireNoOpPassed;
            public bool requireNoOpChanged;
            public bool requireNoOpSceneSaved;
            public int gameObjectCountBefore;
            public int gameObjectCountAfter;
            public int componentCountBefore;
            public int componentCountAfter;
            public int missingReferenceCount;
            public bool transformsUnchanged;
            public bool terrainUnchanged;
            public bool sceneMetaUnchanged;
            public bool templateMetaUnchanged;
            public bool formalTemplateBytesEqual;
            public string sceneSha256;
            public string templateSha256;
            public string manifestSha256;
            public string semanticSha256;
            public string canonicalGenerationId;
            public string candidateEvidenceId;
            public int businessRecordCount;
            public int e3bBusinessRecordCount;
        }

        private readonly struct TransformSnapshot
        {
            internal TransformSnapshot(Transform value)
            {
                Value = value;
                Parent = value.parent;
                SiblingIndex = value.GetSiblingIndex();
                LocalPosition = value.localPosition;
                LocalRotation = value.localRotation;
                LocalScale = value.localScale;
                ActiveSelf = value.gameObject.activeSelf;
            }

            private Transform Value { get; }
            private Transform Parent { get; }
            private int SiblingIndex { get; }
            private Vector3 LocalPosition { get; }
            private Quaternion LocalRotation { get; }
            private Vector3 LocalScale { get; }
            private bool ActiveSelf { get; }

            internal bool Matches()
            {
                return Value != null &&
                    ReferenceEquals(Value.parent, Parent) &&
                    Value.GetSiblingIndex() == SiblingIndex &&
                    Value.localPosition.Equals(LocalPosition) &&
                    Value.localRotation.Equals(LocalRotation) &&
                    Value.localScale.Equals(LocalScale) &&
                    Value.gameObject.activeSelf == ActiveSelf;
            }
        }

        [MenuItem(
            "Tools/Underwater Demo/E3B/Apply ROV Contact Candidate Integration")]
        public static void ApplyFromMenu()
        {
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            EnvE3BRovContactInstallResult result = Execute(
                scene,
                EnvE3BRovContactInstallMode.Apply);
            Require(result.Success,
                result.FailureStage + ": " + result.FailureMessage);
            Debug.Log("ENV_E3B_ROV_CONTACT_SCENE_INSTALLER_APPLY_PASS" +
                " | changed=" + result.Changed +
                " | saved=" + result.SceneSaved);
        }

        public static void RunBuildCandidateBatch()
        {
            string reportPath = RequireExternalCreateNewPath(
                ReportPathArgument);
            string projectRoot = ProjectRoot();
            string sceneAbsolute = AbsolutePath(ScenePath);
            string templateAbsolute = AbsolutePath(TemplatePath);
            string manifestAbsolute = AbsolutePath(ManifestPath);
            string sceneMeta = sceneAbsolute + ".meta";
            string templateMeta = templateAbsolute + ".meta";
            string sceneMetaShaBefore = Sha256File(sceneMeta);
            string templateMetaShaBefore = Sha256File(templateMeta);

            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            Require(scene.IsValid() && scene.isLoaded && !scene.isDirty,
                "Candidate formal Scene did not open cleanly.");
            CountScene(scene,
                out int gameObjectsBefore,
                out int componentsBefore,
                out int missingBefore);
            Require(gameObjectsBefore == 548 &&
                    componentsBefore == 1567 &&
                    missingBefore == 0,
                "Frozen baseline counts are not 548/1567/0.");
            TransformSnapshot[] transforms = CaptureTransforms(scene);
            GameObject seabed = RequireUniqueRoot(scene, "Seabed");
            MeshFilter terrainFilter = RequireComponent<MeshFilter>(
                seabed, "/Seabed MeshFilter");
            MeshCollider terrainCollider = RequireComponent<MeshCollider>(
                seabed, "/Seabed MeshCollider");
            Mesh terrainMesh = terrainFilter.sharedMesh;
            Vector3 terrainPosition = seabed.transform.position;
            Quaternion terrainRotation = seabed.transform.rotation;
            Vector3 terrainScale = seabed.transform.localScale;

            EnvE3BRovContactInstallResult applied = Execute(
                scene,
                EnvE3BRovContactInstallMode.Apply);
            Require(applied.Success,
                applied.FailureStage + ": " + applied.FailureMessage);
            CountScene(scene,
                out int gameObjectsAfter,
                out int componentsAfter,
                out int missingAfter);
            bool transformsUnchanged = transforms.All(value => value.Matches());
            bool terrainUnchanged =
                ReferenceEquals(terrainFilter.sharedMesh, terrainMesh) &&
                ReferenceEquals(terrainCollider.sharedMesh, terrainMesh) &&
                seabed.transform.position.Equals(terrainPosition) &&
                seabed.transform.rotation.Equals(terrainRotation) &&
                seabed.transform.localScale.Equals(terrainScale);
            Require(applied.Changed && applied.SceneSaved &&
                    applied.SamplerAdded && applied.ConstraintAdded &&
                    gameObjectsAfter == 548 &&
                    componentsAfter == 1569 && missingAfter == 0 &&
                    transformsUnchanged && terrainUnchanged,
                "Apply did not produce the exact two-component Candidate delta.");

            byte[] sceneBytes = File.ReadAllBytes(sceneAbsolute);
            File.WriteAllBytes(templateAbsolute, sceneBytes);
            AssetDatabase.ImportAsset(
                TemplatePath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            Require(File.ReadAllBytes(templateAbsolute)
                    .SequenceEqual(sceneBytes),
                "Candidate template does not equal the formal Scene bytes.");

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            CanonicalSemanticSignature signature =
                CanonicalSceneRebuildOrchestrator
                    .BuildCanonicalSemanticSignature();
            string sceneSha = Sha256(sceneBytes);
            string generationId =
                "env-e3b-batch4-" +
                DateTimeOffset.UtcNow.ToString(
                    "yyyyMMdd'T'HHmmss'Z'",
                    CultureInfo.InvariantCulture) +
                "-candidate-" + sceneSha.Substring(0, 16);
            string evidenceId = "candidate-manifest-" +
                Sha256(Encoding.UTF8.GetBytes(
                    sceneSha + "\n" + signature.CanonicalSemanticSha));
            CanonicalSceneIdentityManifestSnapshot current =
                CanonicalSceneIdentityContract
                    .LoadApprovedManifestSnapshot();
            CanonicalSceneIdentityManifestPrecomputation manifest =
                CanonicalSceneIdentityContract.PrecomputeReplacementManifest(
                    current,
                    generationId,
                    sceneSha,
                    sceneBytes.LongLength,
                    signature.CanonicalSemanticSha,
                    signature.GameObjectCount,
                    signature.ComponentCount,
                    signature.MissingReferenceCount,
                    evidenceId);
            File.WriteAllBytes(
                manifestAbsolute,
                manifest.CopyManifestBytes());
            AssetDatabase.ImportAsset(
                ManifestPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);

            Scene noOpScene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            EnvE3BRovContactInstallResult noOp = Execute(
                noOpScene,
                EnvE3BRovContactInstallMode.RequireNoOp);
            Require(noOp.Success && !noOp.Changed && !noOp.SceneSaved,
                "RequireNoOp did not prove the completed Candidate binding.");
            Require(string.Equals(sceneMetaShaBefore,
                    Sha256File(sceneMeta), StringComparison.Ordinal) &&
                    string.Equals(templateMetaShaBefore,
                    Sha256File(templateMeta), StringComparison.Ordinal),
                "A protected Scene meta changed during Candidate generation.");
            Require(signature.BusinessRecords.Length == 20 &&
                    signature.BusinessRecords.Count(value =>
                        value.StartsWith("E3B_", StringComparison.Ordinal)) == 3,
                "Candidate semantic business-record count is not 20/3.");

            var report = new CandidateBuildReport
            {
                schema = "ENV-E3B-RovContactCandidateBuild-v1",
                status =
                    "ENV_E3B_ROV_CONTACT_SCENE_INSTALLER_APPLY_PASS",
                unityVersion = Application.unityVersion,
                success = true,
                changed = applied.Changed,
                sceneSaved = applied.SceneSaved,
                samplerAdded = applied.SamplerAdded,
                constraintAdded = applied.ConstraintAdded,
                samplerBindingChanged = applied.SamplerBindingChanged,
                profileChanged = applied.ProfileChanged,
                driverBindingChanged = applied.DriverBindingChanged,
                duplicateComponentCount = applied.DuplicateComponentCount,
                requireNoOpPassed = noOp.Success,
                requireNoOpChanged = noOp.Changed,
                requireNoOpSceneSaved = noOp.SceneSaved,
                gameObjectCountBefore = gameObjectsBefore,
                gameObjectCountAfter = gameObjectsAfter,
                componentCountBefore = componentsBefore,
                componentCountAfter = componentsAfter,
                missingReferenceCount = missingAfter,
                transformsUnchanged = transformsUnchanged,
                terrainUnchanged = terrainUnchanged,
                sceneMetaUnchanged = string.Equals(sceneMetaShaBefore,
                    Sha256File(sceneMeta), StringComparison.Ordinal),
                templateMetaUnchanged = string.Equals(templateMetaShaBefore,
                    Sha256File(templateMeta), StringComparison.Ordinal),
                formalTemplateBytesEqual = File.ReadAllBytes(templateAbsolute)
                    .SequenceEqual(File.ReadAllBytes(sceneAbsolute)),
                sceneSha256 = sceneSha,
                templateSha256 = Sha256File(templateAbsolute),
                manifestSha256 = manifest.ManifestSha256,
                semanticSha256 = signature.CanonicalSemanticSha,
                canonicalGenerationId = generationId,
                candidateEvidenceId = evidenceId,
                businessRecordCount = signature.BusinessRecords.Length,
                e3bBusinessRecordCount = signature.BusinessRecords.Count(
                    value => value.StartsWith(
                        "E3B_", StringComparison.Ordinal))
            };
            WriteCreateNew(reportPath,
                JsonUtility.ToJson(report, true) + Environment.NewLine);
            Debug.Log(report.status + " | scene=" + report.sceneSha256 +
                " | semantic=" + report.semanticSha256 +
                " | project=" + projectRoot);
        }

        public static void RunBatch1SafetyFoundationBatch()
        {
            string reportPath = RequireExternalCreateNewPath(
                Batch1ReportPathArgument);
            string sceneAbsolute = AbsolutePath(ScenePath);
            string templateAbsolute = AbsolutePath(TemplatePath);
            string manifestAbsolute = AbsolutePath(ManifestPath);
            string sceneMeta = sceneAbsolute + ".meta";
            string templateMeta = templateAbsolute + ".meta";
            string sceneMetaShaBefore = Sha256File(sceneMeta);
            string templateMetaShaBefore = Sha256File(templateMeta);

            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            Require(scene.IsValid() && scene.isLoaded && !scene.isDirty,
                "Batch 1 formal Scene did not open cleanly.");
            CountScene(scene,
                out int gameObjectsBefore,
                out int componentsBefore,
                out int missingBefore);
            Require(missingBefore == 0,
                "Batch 1 baseline contains missing references.");
            TransformSnapshot[] transforms = CaptureTransforms(scene);
            GameObject seabed = RequireUniqueRoot(scene, "Seabed");
            MeshFilter terrainFilter = RequireComponent<MeshFilter>(
                seabed, "/Seabed MeshFilter");
            MeshCollider terrainCollider = RequireComponent<MeshCollider>(
                seabed, "/Seabed MeshCollider");
            Mesh terrainMesh = terrainFilter.sharedMesh;
            Vector3 terrainPosition = seabed.transform.position;
            Quaternion terrainRotation = seabed.transform.rotation;
            Vector3 terrainScale = seabed.transform.localScale;

            EnvE3BRovContactInstallResult applied = Execute(
                scene,
                EnvE3BRovContactInstallMode.Apply);
            Require(applied.Success,
                applied.FailureStage + ": " + applied.FailureMessage);
            CountScene(scene,
                out int gameObjectsAfter,
                out int componentsAfter,
                out int missingAfter);
            bool transformsUnchanged = transforms.All(value => value.Matches());
            bool terrainUnchanged =
                ReferenceEquals(terrainFilter.sharedMesh, terrainMesh) &&
                ReferenceEquals(terrainCollider.sharedMesh, terrainMesh) &&
                seabed.transform.position.Equals(terrainPosition) &&
                seabed.transform.rotation.Equals(terrainRotation) &&
                seabed.transform.localScale.Equals(terrainScale);
            Require(gameObjectsAfter == gameObjectsBefore &&
                    componentsAfter == componentsBefore &&
                    missingAfter == 0 && transformsUnchanged && terrainUnchanged,
                "Batch 1 changed Scene structure, transforms, or terrain authority.");

            byte[] sceneBytes = File.ReadAllBytes(sceneAbsolute);
            File.WriteAllBytes(templateAbsolute, sceneBytes);
            AssetDatabase.ImportAsset(
                TemplatePath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            Require(File.ReadAllBytes(templateAbsolute).SequenceEqual(sceneBytes),
                "Batch 1 canonical template differs from the formal Scene.");

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            CanonicalSemanticSignature signature =
                CanonicalSceneRebuildOrchestrator
                    .BuildCanonicalSemanticSignature();
            string sceneSha = Sha256(sceneBytes);
            string generationId =
                "rov-vertical-batch1-" +
                DateTimeOffset.UtcNow.ToString(
                    "yyyyMMdd'T'HHmmss'Z'",
                    CultureInfo.InvariantCulture) +
                "-candidate-" + sceneSha.Substring(0, 16);
            string evidenceId = "candidate-manifest-" +
                Sha256(Encoding.UTF8.GetBytes(
                    sceneSha + "\n" + signature.CanonicalSemanticSha));
            CanonicalSceneIdentityManifestSnapshot current =
                CanonicalSceneIdentityContract.LoadApprovedManifestSnapshot();
            CanonicalSceneIdentityManifestPrecomputation manifest =
                CanonicalSceneIdentityContract.PrecomputeReplacementManifest(
                    current,
                    generationId,
                    sceneSha,
                    sceneBytes.LongLength,
                    signature.CanonicalSemanticSha,
                    signature.GameObjectCount,
                    signature.ComponentCount,
                    signature.MissingReferenceCount,
                    evidenceId);
            File.WriteAllBytes(
                manifestAbsolute,
                manifest.CopyManifestBytes());
            AssetDatabase.ImportAsset(
                ManifestPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);

            Scene noOpScene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            EnvE3BRovContactInstallResult noOp = Execute(
                noOpScene,
                EnvE3BRovContactInstallMode.RequireNoOp);
            Require(noOp.Success && !noOp.Changed && !noOp.SceneSaved,
                "Batch 1 RequireNoOp did not prove persistent bindings.");
            Require(string.Equals(sceneMetaShaBefore,
                    Sha256File(sceneMeta), StringComparison.Ordinal) &&
                    string.Equals(templateMetaShaBefore,
                    Sha256File(templateMeta), StringComparison.Ordinal),
                "Batch 1 changed a protected Scene meta file.");

            var report = new CandidateBuildReport
            {
                schema = "ROV-VerticalMotion-Batch1-SafetyFoundation-v1",
                status = "ROV_VERTICAL_MOTION_BATCH1_SCENE_INTEGRATION_PASS",
                unityVersion = Application.unityVersion,
                success = true,
                changed = applied.Changed,
                sceneSaved = applied.SceneSaved,
                samplerAdded = applied.SamplerAdded,
                constraintAdded = applied.ConstraintAdded,
                samplerBindingChanged = applied.SamplerBindingChanged,
                profileChanged = applied.ProfileChanged,
                waterBindingChanged = applied.WaterBindingChanged,
                driverBindingChanged = applied.DriverBindingChanged,
                duplicateComponentCount = applied.DuplicateComponentCount,
                requireNoOpPassed = noOp.Success,
                requireNoOpChanged = noOp.Changed,
                requireNoOpSceneSaved = noOp.SceneSaved,
                gameObjectCountBefore = gameObjectsBefore,
                gameObjectCountAfter = gameObjectsAfter,
                componentCountBefore = componentsBefore,
                componentCountAfter = componentsAfter,
                missingReferenceCount = missingAfter,
                transformsUnchanged = transformsUnchanged,
                terrainUnchanged = terrainUnchanged,
                sceneMetaUnchanged = string.Equals(sceneMetaShaBefore,
                    Sha256File(sceneMeta), StringComparison.Ordinal),
                templateMetaUnchanged = string.Equals(templateMetaShaBefore,
                    Sha256File(templateMeta), StringComparison.Ordinal),
                formalTemplateBytesEqual = File.ReadAllBytes(templateAbsolute)
                    .SequenceEqual(File.ReadAllBytes(sceneAbsolute)),
                sceneSha256 = sceneSha,
                templateSha256 = Sha256File(templateAbsolute),
                manifestSha256 = manifest.ManifestSha256,
                semanticSha256 = signature.CanonicalSemanticSha,
                canonicalGenerationId = generationId,
                candidateEvidenceId = evidenceId,
                businessRecordCount = signature.BusinessRecords.Length,
                e3bBusinessRecordCount = signature.BusinessRecords.Count(
                    value => value.StartsWith("E3B_", StringComparison.Ordinal))
            };
            WriteCreateNew(reportPath,
                JsonUtility.ToJson(report, true) + Environment.NewLine);
            Debug.Log(report.status + " | scene=" + report.sceneSha256 +
                " | semantic=" + report.semanticSha256);
        }

        internal static EnvE3BRovContactInstallResult Execute(
            Scene scene,
            EnvE3BRovContactInstallMode mode)
        {
            var result = new EnvE3BRovContactInstallResult
            {
                Success = false,
                FailureStage = "Precondition",
                FailureMessage = string.Empty
            };
            try
            {
                Require(scene.IsValid() && scene.isLoaded &&
                        SceneManager.sceneCount == 1 &&
                        !scene.isDirty,
                    "Installer requires one clean loaded Scene.");
                GameObject driverObject = RequireUniqueRoot(
                    scene, "ROV_PublicPoseDriver");
                GameObject seabed = RequireUniqueRoot(scene, "Seabed");
                GameObject water = RequireUniqueRoot(scene, "Water_Surface");
                GameObject rovRoot = RequireUniqueRoot(
                    scene, "ROV_Box_Seabed");
                GameObject auvDriverObject = RequireUniqueRoot(
                    scene, "AUV_PublicPoseDriver");
                GameObject usvDriverObject = RequireUniqueRoot(
                    scene, "USV_PublicPoseDriver");

                VehiclePoseDriver driver = RequireComponent<VehiclePoseDriver>(
                    driverObject, "/ROV_PublicPoseDriver VehiclePoseDriver");
                VehiclePoseDriver auvDriver = RequireComponent<VehiclePoseDriver>(
                    auvDriverObject, "/AUV_PublicPoseDriver VehiclePoseDriver");
                VehiclePoseDriver usvDriver = RequireComponent<VehiclePoseDriver>(
                    usvDriverObject, "/USV_PublicPoseDriver VehiclePoseDriver");
                Require(ReferenceEquals(driver.TargetRoot, rovRoot.transform),
                    "ROV Driver target root is not /ROV_Box_Seabed.");
                Require((auvDriver.PoseConstraintProvider == null ||
                         auvDriver.PoseConstraintProvider is
                             AuvTerrainClearanceConstraint) &&
                        usvDriver.PoseConstraintProvider == null,
                    "AUV provider must be null or the E3D clearance " +
                    "constraint; USV provider must remain null.");

                MeshCollider[] terrainColliders =
                    seabed.GetComponents<MeshCollider>();
                Require(terrainColliders.Length == 1 &&
                        terrainColliders[0].enabled &&
                        !terrainColliders[0].isTrigger,
                    "/Seabed must have one enabled non-trigger MeshCollider.");
                MeshCollider terrainCollider = terrainColliders[0];
                FlatWaterSurfaceProvider waterProvider =
                    RequireComponent<FlatWaterSurfaceProvider>(
                        water, "/Water_Surface FlatWaterSurfaceProvider");
                MeshFilter terrainFilter = RequireComponent<MeshFilter>(
                    seabed, "/Seabed MeshFilter");
                Require(terrainFilter.sharedMesh != null &&
                        ReferenceEquals(terrainFilter.sharedMesh,
                            terrainCollider.sharedMesh),
                    "/Seabed MeshFilter and MeshCollider must share one Mesh.");

                TerrainSurfaceSampler[] samplers =
                    driverObject.GetComponents<TerrainSurfaceSampler>();
                RovTerrainContactConstraint[] constraints =
                    driverObject.GetComponents<RovTerrainContactConstraint>();
                result.DuplicateComponentCount =
                    Math.Max(0, samplers.Length - 1) +
                    Math.Max(0, constraints.Length - 1);
                Require(result.DuplicateComponentCount == 0,
                    "Duplicate E3B components exist on /ROV_PublicPoseDriver.");

                if (mode == EnvE3BRovContactInstallMode.RequireNoOp)
                {
                    result.FailureStage = "RequireNoOp";
                    Require(samplers.Length == 1 && constraints.Length == 1,
                        "RequireNoOp found missing E3B components.");
                    RovContactProfile profile = constraints[0].Profile;
                    Require(ReferenceEquals(samplers[0].ContactTerrain,
                                terrainCollider) &&
                            ReferenceEquals(constraints[0].SurfaceSampler,
                                samplers[0]) &&
                            ReferenceEquals(constraints[0].WaterSurfaceProvider,
                                waterProvider) &&
                            MatchesApprovedProfile(profile) &&
                            ReferenceEquals(driver.PoseConstraintProvider,
                                constraints[0]),
                        "RequireNoOp found an E3B binding/profile drift.");
                    result.Success = true;
                    result.FailureStage = string.Empty;
                    return result;
                }

                result.FailureStage = "Apply";
                TerrainSurfaceSampler sampler;
                if (samplers.Length == 0)
                {
                    sampler = driverObject.AddComponent<TerrainSurfaceSampler>();
                    result.SamplerAdded = true;
                }
                else
                {
                    sampler = samplers[0];
                }

                RovTerrainContactConstraint constraint;
                if (constraints.Length == 0)
                {
                    constraint = driverObject
                        .AddComponent<RovTerrainContactConstraint>();
                    result.ConstraintAdded = true;
                }
                else
                {
                    constraint = constraints[0];
                }

                result.SamplerBindingChanged =
                    !ReferenceEquals(sampler.ContactTerrain, terrainCollider);
                result.ProfileChanged =
                    !MatchesApprovedProfile(constraint.Profile);
                bool constraintSamplerChanged =
                    !ReferenceEquals(constraint.SurfaceSampler, sampler);
                result.WaterBindingChanged =
                    !ReferenceEquals(constraint.WaterSurfaceProvider,
                        waterProvider);
                result.DriverBindingChanged =
                    !ReferenceEquals(driver.PoseConstraintProvider, constraint);

                if (result.SamplerBindingChanged)
                    sampler.Configure(terrainCollider);
                if (constraintSamplerChanged || result.ProfileChanged ||
                    result.WaterBindingChanged)
                {
                    constraint.Configure(
                        sampler,
                        RovContactProfile.CreateApprovedDefault(),
                        waterProvider);
                }
                if (result.DriverBindingChanged)
                    driver.ConfigurePoseConstraint(constraint);

                result.Changed = result.SamplerAdded ||
                    result.ConstraintAdded ||
                    result.SamplerBindingChanged ||
                    constraintSamplerChanged ||
                    result.WaterBindingChanged ||
                    result.ProfileChanged ||
                    result.DriverBindingChanged;
                if (result.Changed)
                {
                    EditorUtility.SetDirty(sampler);
                    EditorUtility.SetDirty(constraint);
                    EditorUtility.SetDirty(driver);
                    EditorSceneManager.MarkSceneDirty(scene);
                    Require(EditorSceneManager.SaveScene(scene),
                        "Failed to save the integrated Candidate Scene.");
                    result.SceneSaved = true;
                }

                result.Success = true;
                result.FailureStage = string.Empty;
                return result;
            }
            catch (Exception exception)
            {
                result.Success = false;
                result.FailureMessage = exception.ToString();
                return result;
            }
        }

        internal static LegacyPostBuildCompatibilityScope
            BeginLegacyPostBuildCompatibility(Scene scene)
        {
            Require(scene.IsValid() && scene.isLoaded && !scene.isDirty,
                "Legacy post-build compatibility requires one clean Scene.");
            GameObject driverObject = RequireUniqueRoot(
                scene, "ROV_PublicPoseDriver");
            VehiclePoseDriver driver = RequireComponent<VehiclePoseDriver>(
                driverObject, "/ROV_PublicPoseDriver VehiclePoseDriver");
            TerrainSurfaceSampler sampler =
                RequireComponent<TerrainSurfaceSampler>(
                    driverObject,
                    "/ROV_PublicPoseDriver TerrainSurfaceSampler");
            RovTerrainContactConstraint constraint =
                RequireComponent<RovTerrainContactConstraint>(
                    driverObject,
                    "/ROV_PublicPoseDriver RovTerrainContactConstraint");
            Require(ReferenceEquals(driver.PoseConstraintProvider, constraint) &&
                    ReferenceEquals(constraint.SurfaceSampler, sampler),
                "Legacy post-build compatibility requires exact E3B bindings.");
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(
                "E3B legacy post-build no-op compatibility");
            Undo.DestroyObjectImmediate(constraint);
            Undo.DestroyObjectImmediate(sampler);
            return new LegacyPostBuildCompatibilityScope(
                scene, group);
        }

        internal static bool MatchesApprovedProfile(RovContactProfile value)
        {
            RovContactProfile approved =
                RovContactProfile.CreateApprovedDefault();
            return value != null && value.TryValidate(out _) &&
                value.LeftFrontOffset.Equals(approved.LeftFrontOffset) &&
                value.LeftRearOffset.Equals(approved.LeftRearOffset) &&
                value.RightFrontOffset.Equals(approved.RightFrontOffset) &&
                value.RightRearOffset.Equals(approved.RightRearOffset) &&
                value.UpperEnvelopeMinimum.Equals(
                    approved.UpperEnvelopeMinimum) &&
                value.UpperEnvelopeMaximum.Equals(
                    approved.UpperEnvelopeMaximum) &&
                value.GroundClearance.Equals(approved.GroundClearance) &&
                value.ProbeStartHeightMeters.Equals(
                    approved.ProbeStartHeightMeters) &&
                value.ProbeDistanceMeters.Equals(
                    approved.ProbeDistanceMeters) &&
                value.MaximumSlopeDegrees.Equals(
                    approved.MaximumSlopeDegrees) &&
                value.MaximumVerticalCorrectionMeters.Equals(
                    approved.MaximumVerticalCorrectionMeters) &&
                value.EpsilonMeters.Equals(approved.EpsilonMeters);
        }

        private static TransformSnapshot[] CaptureTransforms(Scene scene)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<Transform>(true))
                .Select(value => new TransformSnapshot(value))
                .ToArray();
        }

        private static GameObject RequireUniqueRoot(Scene scene, string name)
        {
            GameObject[] values = scene.GetRootGameObjects()
                .Where(value => string.Equals(
                    value.name, name, StringComparison.Ordinal))
                .ToArray();
            Require(values.Length == 1,
                "Expected exactly one root /" + name + ".");
            return values[0];
        }

        private static T RequireComponent<T>(GameObject value, string label)
            where T : Component
        {
            T[] components = value.GetComponents<T>();
            Require(components.Length == 1,
                label + " must be unique.");
            return components[0];
        }

        private static void CountScene(
            Scene scene,
            out int gameObjects,
            out int components,
            out int missing)
        {
            GameObject[] all = scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<Transform>(true))
                .Select(value => value.gameObject)
                .Distinct()
                .ToArray();
            gameObjects = all.Length;
            components = 0;
            missing = 0;
            foreach (GameObject value in all)
            {
                Component[] values = value.GetComponents<Component>();
                components += values.Length;
                missing += values.Count(component => component == null);
            }
        }

        private static string RequireExternalCreateNewPath(string argument)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            string value = string.Empty;
            for (int index = 0; index + 1 < arguments.Length; index++)
            {
                if (string.Equals(arguments[index], argument,
                        StringComparison.Ordinal))
                {
                    Require(string.IsNullOrEmpty(value),
                        argument + " was supplied more than once.");
                    value = arguments[index + 1];
                }
            }
            Require(!string.IsNullOrWhiteSpace(value),
                "Missing " + argument + ".");
            string fullPath = Path.GetFullPath(value);
            Require(!fullPath.StartsWith(
                    ProjectRoot() + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase),
                "Report path must be outside the Unity project.");
            Require(!File.Exists(fullPath),
                "Report path must be create-new.");
            return fullPath;
        }

        private static string ProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static string AbsolutePath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(ProjectRoot(), assetPath));
        }

        private static string Sha256File(string path)
        {
            return Sha256(File.ReadAllBytes(path));
        }

        private static string Sha256(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create())
            {
                return BitConverter.ToString(hash.ComputeHash(bytes))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private static void WriteCreateNew(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = new FileStream(
                       path,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var writer = new StreamWriter(
                       stream,
                       new UTF8Encoding(false)))
            {
                writer.Write(content);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
