using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Auv;
using UnderwaterRobotScene.Visualization.Runtime.Rov;
using UnderwaterRobotScene.Visualization.Runtime.Terrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    internal enum EnvE3DAuvTerrainInstallMode
    {
        Apply = 0,
        RequireNoOp = 1
    }

    [Serializable]
    internal sealed class EnvE3DAuvTerrainInstallResult
    {
        internal bool Success;
        internal bool Changed;
        internal bool SceneSaved;
        internal bool SamplerAdded;
        internal bool ConstraintAdded;
        internal bool SamplerBindingChanged;
        internal bool WaterBindingChanged;
        internal bool ProfileChanged;
        internal bool DriverBindingChanged;
        internal int DuplicateComponentCount;
        internal string FailureStage;
        internal string FailureMessage;
    }

    public static class EnvE3DAuvTerrainSafetySceneInstaller
    {
        private const string ScenePath =
            "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string TemplatePath =
            "Assets/Editor/Visualization/Installation/" +
            "UnderwaterRobotDemo_Canonical.unity";
        private const string ManifestPath =
            "Assets/Editor/Visualization/Installation/" +
            "CanonicalSceneIdentityManifest.json";
        private const string BusinessReportArgument =
            "-envE3DBusinessReportPath";
        private const string AuthorityReportArgument =
            "-envE3DAuthorityReportPath";

        [Serializable]
        private sealed class BuildReport
        {
            public string schema;
            public string status;
            public string unityVersion;
            public bool changed;
            public bool sceneSaved;
            public bool noOpPassed;
            public int gameObjectCount;
            public int componentCount;
            public int missingReferenceCount;
            public bool formalTemplateBytesEqual;
            public string sceneSha256;
            public string templateSha256;
            public string semanticSha256;
            public string manifestSha256;
            public string canonicalGenerationId;
        }

        [MenuItem(
            "Tools/Underwater Demo/E3D/Apply AUV Terrain Safety Integration")]
        public static void ApplyFromMenu()
        {
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath, OpenSceneMode.Single);
            EnvE3DAuvTerrainInstallResult result = Execute(
                scene, EnvE3DAuvTerrainInstallMode.Apply);
            Require(result.Success,
                result.FailureStage + ": " + result.FailureMessage);
            Debug.Log("ENV_E3D_AUV_TERRAIN_INSTALLER_APPLY_PASS" +
                " | changed=" + result.Changed +
                " | saved=" + result.SceneSaved);
        }

        public static void RunBuildBusinessBatch()
        {
            string reportPath = RequireExternalCreateNewPath(
                BusinessReportArgument);
            string sceneAbsolute = AbsolutePath(ScenePath);
            string templateAbsolute = AbsolutePath(TemplatePath);
            string sceneMetaSha = Sha256File(sceneAbsolute + ".meta");
            string templateMetaSha = Sha256File(templateAbsolute + ".meta");

            Scene scene = EditorSceneManager.OpenScene(
                ScenePath, OpenSceneMode.Single);
            Require(scene.IsValid() && scene.isLoaded && !scene.isDirty,
                "Formal Scene did not open cleanly.");
            EnvE3DAuvTerrainInstallResult applied = Execute(
                scene, EnvE3DAuvTerrainInstallMode.Apply);
            Require(applied.Success,
                applied.FailureStage + ": " + applied.FailureMessage);
            byte[] sceneBytes = File.ReadAllBytes(sceneAbsolute);
            File.WriteAllBytes(templateAbsolute, sceneBytes);
            AssetDatabase.ImportAsset(
                TemplatePath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            Require(File.ReadAllBytes(templateAbsolute)
                    .SequenceEqual(sceneBytes),
                "Canonical template is not byte-identical to formal Scene.");

            Scene noOpScene = EditorSceneManager.OpenScene(
                ScenePath, OpenSceneMode.Single);
            EnvE3DAuvTerrainInstallResult noOp = Execute(
                noOpScene, EnvE3DAuvTerrainInstallMode.RequireNoOp);
            Require(noOp.Success && !noOp.Changed && !noOp.SceneSaved,
                "E3D installer RequireNoOp failed after Apply.");
            Require(string.Equals(sceneMetaSha,
                    Sha256File(sceneAbsolute + ".meta"),
                    StringComparison.Ordinal) &&
                string.Equals(templateMetaSha,
                    Sha256File(templateAbsolute + ".meta"),
                    StringComparison.Ordinal),
                "A protected Scene meta changed.");

            CountScene(noOpScene,
                out int gameObjects,
                out int components,
                out int missing);
            CanonicalSemanticSignature signature =
                CanonicalSceneRebuildOrchestrator
                    .BuildCanonicalSemanticSignature();
            var report = new BuildReport
            {
                schema = "ENV-E3D-AuvTerrainSafetyBusinessBuild-v1",
                status = "ENV_E3D_AUV_TERRAIN_BUSINESS_BUILD_PASS",
                unityVersion = Application.unityVersion,
                changed = applied.Changed,
                sceneSaved = applied.SceneSaved,
                noOpPassed = noOp.Success,
                gameObjectCount = gameObjects,
                componentCount = components,
                missingReferenceCount = missing,
                formalTemplateBytesEqual = true,
                sceneSha256 = Sha256(sceneBytes),
                templateSha256 = Sha256File(templateAbsolute),
                semanticSha256 = signature.CanonicalSemanticSha,
                manifestSha256 = Sha256File(AbsolutePath(ManifestPath)),
                canonicalGenerationId = string.Empty
            };
            WriteCreateNew(reportPath,
                JsonUtility.ToJson(report, true) + Environment.NewLine);
            Debug.Log(report.status + " | scene=" + report.sceneSha256 +
                " | semantic=" + report.semanticSha256);
        }

        public static void RunBindAuthorityBatch()
        {
            string reportPath = RequireExternalCreateNewPath(
                AuthorityReportArgument);
            string sceneAbsolute = AbsolutePath(ScenePath);
            string templateAbsolute = AbsolutePath(TemplatePath);
            string manifestAbsolute = AbsolutePath(ManifestPath);
            byte[] sceneBytes = File.ReadAllBytes(sceneAbsolute);
            Require(File.ReadAllBytes(templateAbsolute)
                    .SequenceEqual(sceneBytes),
                "Authority bind requires byte-identical Scene/template.");

            Scene scene = EditorSceneManager.OpenScene(
                ScenePath, OpenSceneMode.Single);
            EnvE3DAuvTerrainInstallResult noOp = Execute(
                scene, EnvE3DAuvTerrainInstallMode.RequireNoOp);
            Require(noOp.Success && !noOp.Changed && !noOp.SceneSaved,
                "Authority bind requires an E3D installer no-op.");
            CountScene(scene,
                out int gameObjects,
                out int components,
                out int missing);
            CanonicalSemanticSignature signature =
                CanonicalSceneRebuildOrchestrator
                    .BuildCanonicalSemanticSignature();
            string sceneSha = Sha256(sceneBytes);
            string generationId = "env-e3d-auv-terrain-ui-" +
                DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'") +
                "-" + sceneSha.Substring(0, 16);
            string evidenceId = "env-e3d-implementation-" +
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
                    gameObjects,
                    components,
                    missing,
                    evidenceId);
            File.WriteAllBytes(
                manifestAbsolute,
                manifest.CopyManifestBytes());
            AssetDatabase.ImportAsset(
                ManifestPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);

            var report = new BuildReport
            {
                schema = "ENV-E3D-CanonicalAuthorityBind-v1",
                status = "ENV_E3D_CANONICAL_AUTHORITY_BIND_PASS",
                unityVersion = Application.unityVersion,
                changed = false,
                sceneSaved = false,
                noOpPassed = true,
                gameObjectCount = gameObjects,
                componentCount = components,
                missingReferenceCount = missing,
                formalTemplateBytesEqual = true,
                sceneSha256 = sceneSha,
                templateSha256 = Sha256File(templateAbsolute),
                semanticSha256 = signature.CanonicalSemanticSha,
                manifestSha256 = manifest.ManifestSha256,
                canonicalGenerationId = generationId
            };
            WriteCreateNew(reportPath,
                JsonUtility.ToJson(report, true) + Environment.NewLine);
            Debug.Log(report.status + " | manifest=" +
                report.manifestSha256);
        }

        internal static EnvE3DAuvTerrainInstallResult Execute(
            Scene scene,
            EnvE3DAuvTerrainInstallMode mode)
        {
            var result = new EnvE3DAuvTerrainInstallResult
            {
                FailureStage = "Precondition",
                FailureMessage = string.Empty
            };
            try
            {
                Require(scene.IsValid() && scene.isLoaded &&
                        SceneManager.sceneCount == 1 && !scene.isDirty,
                    "Installer requires one clean loaded Scene.");
                GameObject auvDriverObject = RequireUniqueRoot(
                    scene, "AUV_PublicPoseDriver");
                GameObject rovDriverObject = RequireUniqueRoot(
                    scene, "ROV_PublicPoseDriver");
                GameObject usvDriverObject = RequireUniqueRoot(
                    scene, "USV_PublicPoseDriver");
                GameObject auvRoot = RequireUniqueRoot(
                    scene, "AUV_Yellow_Underwater");
                GameObject seabed = RequireUniqueRoot(scene, "Seabed");
                GameObject water = RequireUniqueRoot(scene, "Water_Surface");
                VehiclePoseDriver auvDriver =
                    RequireComponent<VehiclePoseDriver>(
                        auvDriverObject, "AUV Driver");
                VehiclePoseDriver rovDriver =
                    RequireComponent<VehiclePoseDriver>(
                        rovDriverObject, "ROV Driver");
                VehiclePoseDriver usvDriver =
                    RequireComponent<VehiclePoseDriver>(
                        usvDriverObject, "USV Driver");
                Require(ReferenceEquals(auvDriver.TargetRoot,
                        auvRoot.transform),
                    "AUV Driver target root drifted.");
                Require(rovDriver.PoseConstraintProvider is
                        RovTerrainContactConstraint,
                    "ROV provider must remain the ROV terrain constraint.");
                Require(usvDriver.PoseConstraintProvider == null,
                    "USV must not gain a terrain provider.");

                MeshCollider terrain =
                    RequireComponent<MeshCollider>(seabed, "Seabed collider");
                Require(terrain.enabled && !terrain.isTrigger &&
                        terrain.sharedMesh != null,
                    "Seabed collider is not a valid terrain provider.");
                TerrainSurfaceSampler[] samplers =
                    auvDriverObject.GetComponents<TerrainSurfaceSampler>();
                AuvTerrainClearanceConstraint[] constraints =
                    auvDriverObject
                        .GetComponents<AuvTerrainClearanceConstraint>();
                result.DuplicateComponentCount =
                    Math.Max(0, samplers.Length - 1) +
                    Math.Max(0, constraints.Length - 1);
                Require(result.DuplicateComponentCount == 0,
                    "Duplicate AUV terrain components exist.");

                if (mode == EnvE3DAuvTerrainInstallMode.RequireNoOp)
                {
                    result.FailureStage = "RequireNoOp";
                    Require(samplers.Length == 1 && constraints.Length == 1,
                        "RequireNoOp found missing AUV terrain components.");
                    Require(ReferenceEquals(
                                samplers[0].ContactTerrain, terrain) &&
                            ReferenceEquals(
                                constraints[0].SurfaceSampler, samplers[0]) &&
                            ReferenceEquals(
                                constraints[0].WaterSurface, water.transform) &&
                            MatchesApprovedProfile(constraints[0].Profile) &&
                            ReferenceEquals(
                                auvDriver.PoseConstraintProvider,
                                constraints[0]),
                        "RequireNoOp found AUV terrain binding drift.");
                    result.Success = true;
                    result.FailureStage = string.Empty;
                    return result;
                }

                result.FailureStage = "Apply";
                TerrainSurfaceSampler sampler = samplers.Length == 0
                    ? auvDriverObject.AddComponent<TerrainSurfaceSampler>()
                    : samplers[0];
                result.SamplerAdded = samplers.Length == 0;
                AuvTerrainClearanceConstraint constraint =
                    constraints.Length == 0
                        ? auvDriverObject
                            .AddComponent<AuvTerrainClearanceConstraint>()
                        : constraints[0];
                result.ConstraintAdded = constraints.Length == 0;
                result.SamplerBindingChanged =
                    !ReferenceEquals(sampler.ContactTerrain, terrain);
                result.ProfileChanged =
                    !MatchesApprovedProfile(constraint.Profile);
                bool constraintSamplerChanged =
                    !ReferenceEquals(constraint.SurfaceSampler, sampler);
                result.WaterBindingChanged =
                    !ReferenceEquals(constraint.WaterSurface, water.transform);
                result.DriverBindingChanged =
                    !ReferenceEquals(
                        auvDriver.PoseConstraintProvider, constraint);
                if (result.SamplerBindingChanged)
                    sampler.Configure(terrain);
                if (constraintSamplerChanged || result.WaterBindingChanged ||
                    result.ProfileChanged)
                {
                    constraint.Configure(
                        sampler,
                        AuvTerrainClearanceProfile
                            .CreateApprovedDefault(),
                        water.transform);
                }
                if (result.DriverBindingChanged)
                    auvDriver.ConfigurePoseConstraint(constraint);

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
                    EditorUtility.SetDirty(auvDriver);
                    EditorSceneManager.MarkSceneDirty(scene);
                    Require(EditorSceneManager.SaveScene(scene),
                        "Failed to save E3D formal Scene.");
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

        internal static bool MatchesApprovedProfile(
            AuvTerrainClearanceProfile value)
        {
            AuvTerrainClearanceProfile approved =
                AuvTerrainClearanceProfile.CreateApprovedDefault();
            if (value == null || !value.TryValidate(out _) ||
                value.ProbeCount != approved.ProbeCount ||
                value.HullEnvelopeCornerCount !=
                    approved.HullEnvelopeCornerCount)
                return false;
            for (int index = 0; index < value.ProbeCount; index++)
            {
                if (!value.LowerEnvelopeProbeOffsets[index].Equals(
                        approved.LowerEnvelopeProbeOffsets[index]))
                    return false;
            }
            for (int index = 0;
                 index < value.HullEnvelopeCornerCount;
                 index++)
            {
                if (!value.HullEnvelopeCornerOffsets[index].Equals(
                        approved.HullEnvelopeCornerOffsets[index]))
                    return false;
            }
            return value.MinimumHullClearanceMeters.Equals(
                    approved.MinimumHullClearanceMeters) &&
                value.MinimumHullSubmergenceMeters.Equals(
                    approved.MinimumHullSubmergenceMeters) &&
                value.MaximumUpwardCorrectionMeters.Equals(
                    approved.MaximumUpwardCorrectionMeters) &&
                value.TerrainLayerMask.value ==
                    approved.TerrainLayerMask.value &&
                value.ProbeStartHeightMeters.Equals(
                    approved.ProbeStartHeightMeters) &&
                value.ProbeDistanceMeters.Equals(
                    approved.ProbeDistanceMeters) &&
                value.MaximumSlopeDegrees.Equals(
                    approved.MaximumSlopeDegrees) &&
                value.SamplingToleranceMeters.Equals(
                    approved.SamplingToleranceMeters) &&
                value.SegmentValidationSpacingMeters.Equals(
                    approved.SegmentValidationSpacingMeters) &&
                value.MaximumClimbAngleDegrees.Equals(
                    approved.MaximumClimbAngleDegrees) &&
                value.MaximumDescentAngleDegrees.Equals(
                    approved.MaximumDescentAngleDegrees);
        }

        private static GameObject RequireUniqueRoot(
            Scene scene,
            string name)
        {
            GameObject[] values = scene.GetRootGameObjects()
                .Where(value => string.Equals(
                    value.name, name, StringComparison.Ordinal))
                .ToArray();
            Require(values.Length == 1,
                "Expected exactly one root /" + name + ".");
            return values[0];
        }

        private static T RequireComponent<T>(
            GameObject value,
            string label)
            where T : Component
        {
            T[] components = value.GetComponents<T>();
            Require(components.Length == 1, label + " must be unique.");
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

        private static string RequireExternalCreateNewPath(
            string argument)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            string value = string.Empty;
            for (int index = 0; index + 1 < arguments.Length; index++)
            {
                if (!string.Equals(arguments[index], argument,
                        StringComparison.Ordinal))
                    continue;
                Require(string.IsNullOrEmpty(value),
                    argument + " was supplied more than once.");
                value = arguments[index + 1];
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
            return Path.GetFullPath(Path.Combine(
                Application.dataPath, ".."));
        }

        private static string AbsolutePath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(
                ProjectRoot(), assetPath));
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

        private static void WriteCreateNew(
            string path,
            string content)
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
