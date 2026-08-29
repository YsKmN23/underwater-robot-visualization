using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.LocalTesting;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Auv;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    public static class EnvE3DOperatingVolumeExpansionPhase2BInstaller
    {
        private const string ScenePath =
            CanonicalSceneIdentityContract.TargetScenePath;
        private const string TemplatePath =
            CanonicalSceneIdentityContract.TemplateScenePath;
        private const string ManifestPath =
            CanonicalSceneIdentityContract.IdentityManifestPath;
        private const string AuvRootName = "AUV_Yellow_Underwater";
        private const string AuvHostName = "AUV_PublicData_RuntimeHost";
        private const string RovRootName = "ROV_Box_Seabed";
        private const string RovHostName = "ROV_PublicData_RuntimeHost";

        // Frozen audited AUV envelope: profile bounds size=(..., 1.342817, ...)
        // and lowerY=-0.512817. The upper bound is 0.158592 + 1.342817/2.
        private const float UpperEnvelopeMeters = 0.8300005f;
        private const float LowerEnvelopeMeters = 0.512817f;
        private const float MinimumClearanceMeters = 0.18f;

        [MenuItem(
            "Tools/Underwater Demo/E3D/Apply Operating Volume Expansion Phase 2B")]
        public static void ApplyFromMenu()
        {
            EnvE3DOperatingVolumeExpansionPhase2BResult result = Apply();
            if (!result.Success)
            {
                throw new InvalidOperationException(
                    result.FailureStage + ": " + result.FailureMessage);
            }

            Debug.Log(
                "ENV_E3D_OPERATING_VOLUME_PHASE_2B_PREPARATION_APPLY_PASS" +
                " | sceneChanged=" + result.SceneChanged +
                " | canonicalUpdated=" + result.CanonicalUpdated +
                " | config=" + result.ConfigurationSha256 +
                " | contactMesh=" + result.ContactMeshSha256 +
                " | farMesh=" + result.FarMeshSha256 +
                " | rovDelta=" + result.RovTranslationDelta.ToString("R") +
                " | rovRootY=" + result.RovRootYAfter.ToString("R") +
                " | auvRootY=" + result.AuvRootY.ToString("R"));
        }

        internal static EnvE3DOperatingVolumeExpansionPhase2BResult Apply()
        {
            var result = new EnvE3DOperatingVolumeExpansionPhase2BResult();
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                    "The Phase 2B installer cannot run in Play Mode.");
                Scene scene = SceneManager.GetActiveScene();
                Require(scene.IsValid() && scene.isLoaded &&
                        SceneManager.sceneCount == 1 &&
                        string.Equals(scene.path, ScenePath,
                            StringComparison.Ordinal) &&
                        !EditorSceneManager.IsPreviewScene(scene),
                    "Open the formal UnderwaterRobotDemo Scene as the only " +
                    "clean active Scene before applying Phase 2B.");
                Require(!scene.isDirty,
                    "The formal Scene must be clean before applying Phase 2B.");

                EnvE3AContinuousSeabedConfiguration configuration =
                    EnvE3AContinuousSeabedConfiguration.CreateApproved();
                RovLandingMigrationPlan rovPlan =
                    BuildRovLandingMigrationPlan(scene, configuration);
                result.RovTranslationDelta = rovPlan.AppliedDelta;
                result.RovRootYBefore = rovPlan.RootPositionBefore.y;
                result.RovRootYAfter = rovPlan.RootPositionAfter.y;
                result.RovContactMinY = rovPlan.ContactMinY;
                result.RovContactMaxY = rovPlan.ContactMaxY;
                result.RovRawFitPassed = rovPlan.RawFitPassed;
                result.RovTranslatedFitPassed = rovPlan.TranslatedFitPassed;
                result.SceneChanged |= ApplyRovVerticalMigration(
                    scene,
                    rovPlan);
                EnvE3AEnvironmentInstallResult environment =
                    EnvE3AEnvironmentInstallerChain.Execute(
                        EnvE3AEnvironmentInstallMode.MutationAllowed);
                result.ConfigurationSha256 = environment.ConfigurationSha256;
                result.ContactMeshSha256 = environment.ContactMeshSha256;
                result.FarMeshSha256 = environment.FarMeshSha256;
                result.SceneChanged = environment.AnyChanged;

                result.AuvRootY = BalancedAuvRootY(configuration);
                result.SceneChanged |= ApplyAuvNominalRoot(scene,
                    result.AuvRootY);
                if (scene.isDirty)
                {
                    Require(EditorSceneManager.SaveScene(scene),
                        "Failed to save the managed formal Scene.");
                }
                Require(!scene.isDirty,
                    "Formal Scene remained dirty after the managed save.");

                EnvE3DAuvTerrainInstallResult safety =
                    EnvE3DAuvTerrainSafetySceneInstaller.Execute(
                        scene,
                        EnvE3DAuvTerrainInstallMode.RequireNoOp);
                Require(safety.Success,
                    "AUV terrain safety binding is invalid after Phase 2B: " +
                    safety.FailureMessage);

                result.CanonicalUpdated = UpdateCanonicalArtifacts();
                result.Success = true;
                return result;
            }
            catch (Exception exception)
            {
                result.Success = false;
                result.FailureStage = "Apply";
                result.FailureMessage = exception.ToString();
                return result;
            }
        }

        internal static float BalancedAuvRootY(
            EnvE3AContinuousSeabedConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            float upperBoundary = configuration.WaterDatumY -
                UpperEnvelopeMeters;
            float lowerBoundary = configuration.HoldingTerrainY +
                LowerEnvelopeMeters + MinimumClearanceMeters;
            return (upperBoundary + lowerBoundary) * 0.5f;
        }

        private sealed class RovLandingMigrationPlan
        {
            internal float SourceReferenceY;
            internal float TargetReferenceY;
            internal float AppliedDelta;
            internal Vector3 RootPositionBefore;
            internal Vector3 RootPositionAfter;
            internal float ContactMinY;
            internal float ContactMaxY;
            internal bool RawFitPassed;
            internal bool TranslatedFitPassed;
        }

        private static RovLandingMigrationPlan BuildRovLandingMigrationPlan(
            Scene scene,
            EnvE3AContinuousSeabedConfiguration configuration)
        {
            GameObject seabed = RequireUniqueRoot(scene, "Seabed");
            MeshFilter filter = RequireComponent<MeshFilter>(seabed);
            Require(filter.sharedMesh != null,
                "Seabed MeshFilter has no current Mesh for ROV preflight.");
            Require(EnvE3ATerrainGeometry.TrySampleMigrationSourceContactMesh(
                    filter.sharedMesh,
                    seabed.transform,
                    configuration.ActivityBounds.MaxX,
                    0f,
                    out float sourceReferenceY,
                    out _,
                    out string sourceFailure),
                "Could not read the current stable seabed reference for ROV " +
                "migration: " + sourceFailure);

            GameObject rov = RequireUniqueRoot(scene, RovRootName);
            Require(EnvE2ARovContactGeometry.TryResolveContactAuthority(
                    rov.transform,
                    out EnvE2AContactPoint[] currentPoints,
                    out _),
                "Could not resolve the four ROV contact points for preflight.");
            Require(currentPoints.Length == 4,
                "ROV preflight requires exactly four contact points.");

            float targetReferenceY = configuration.HoldingTerrainY;
            float delta = targetReferenceY - sourceReferenceY;
            var plan = new RovLandingMigrationPlan
            {
                SourceReferenceY = sourceReferenceY,
                TargetReferenceY = targetReferenceY,
                RootPositionBefore = rov.transform.position,
                ContactMinY = currentPoints.Min(point => point.World.y),
                ContactMaxY = currentPoints.Max(point => point.World.y),
                AppliedDelta = 0f
            };

            Mesh probeMesh = null;
            try
            {
                probeMesh = EnvE3ATerrainGeometry.BuildContactMesh(
                    configuration);
                plan.RawFitPassed =
                    EnvE3ATerrainGeometry.TryApplyRovLandingFit(
                        probeMesh,
                        seabed.transform,
                        currentPoints,
                        configuration,
                        out _);
                if (plan.RawFitPassed)
                {
                    Require(Mathf.Abs(delta) <= 0.0001f,
                        "ROV contact points already fit the new terrain but " +
                        "the derived vertical reference delta is non-zero.");
                    plan.TranslatedFitPassed = true;
                }
                else
                {
                    EnvE2AContactPoint[] translatedPoints =
                        TranslateRovContactPoints(currentPoints, delta);
                    plan.TranslatedFitPassed =
                        EnvE3ATerrainGeometry.TryApplyRovLandingFit(
                            probeMesh,
                            seabed.transform,
                            translatedPoints,
                            configuration,
                            out string translatedFailure);
                    Require(plan.TranslatedFitPassed,
                        "ROV Phase 2B translated landing preflight failed: " +
                        translatedFailure);
                    plan.AppliedDelta = delta;
                }
            }
            finally
            {
                if (probeMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(probeMesh);
                }
            }

            plan.RootPositionAfter = new Vector3(
                plan.RootPositionBefore.x,
                plan.RootPositionBefore.y + plan.AppliedDelta,
                plan.RootPositionBefore.z);
            return plan;
        }

        private static EnvE2AContactPoint[] TranslateRovContactPoints(
            EnvE2AContactPoint[] points,
            float delta)
        {
            return points.Select(point => new EnvE2AContactPoint(
                    point.Role,
                    point.SourcePath,
                    new Vector3(
                        point.World.x,
                        point.World.y + delta,
                        point.World.z)))
                .ToArray();
        }

        private static bool ApplyRovVerticalMigration(
            Scene scene,
            RovLandingMigrationPlan plan)
        {
            if (Mathf.Abs(plan.AppliedDelta) <= 0.0001f)
            {
                return false;
            }

            GameObject rov = RequireUniqueRoot(scene, RovRootName);
            GameObject hostObject = RequireUniqueRoot(scene, RovHostName);
            VehiclePoseIntegrationConfiguration host =
                RequireComponent<VehiclePoseIntegrationConfiguration>(
                    hostObject);
            Require(Approximately(rov.transform.position,
                    plan.RootPositionBefore),
                "ROV root changed between preflight and migration.");

            Undo.RecordObject(rov.transform,
                "Apply Phase 2B ROV terrain-relative vertical migration");
            rov.transform.position = plan.RootPositionAfter;
            EditorUtility.SetDirty(rov.transform);

            Vector3 origin = host.TestOrigin;
            Vector3 targetOrigin = new Vector3(
                origin.x,
                origin.y + plan.AppliedDelta,
                origin.z);
            host.ConfigureLocalTest(
                host.SourceId,
                host.VehicleId,
                host.VehicleType,
                host.GeneratorKind,
                targetOrigin,
                (float)host.SampleIntervalSeconds,
                host.StoreCapacity,
                (float)host.StaleTimeoutSeconds,
                host.MaxCatchUpStepsPerFrame,
                host.AutoStart,
                (float)host.RenderDelaySeconds,
                (float)host.MaxInterpolationGapSeconds,
                (float)host.MaxHoldSourceTimeSeconds,
                (float)host.ExactTimeToleranceSeconds,
                host.AfterLatestBehavior,
                host.AllowSingleSampleHold);
            EditorUtility.SetDirty(host);
            EditorSceneManager.MarkSceneDirty(scene);
            return true;
        }

        private static bool ApplyAuvNominalRoot(Scene scene, float targetY)
        {
            GameObject auv = RequireUniqueRoot(scene, AuvRootName);
            GameObject hostObject = RequireUniqueRoot(scene, AuvHostName);
            VehiclePoseIntegrationConfiguration host =
                RequireComponent<VehiclePoseIntegrationConfiguration>(
                    hostObject);

            Vector3 current = auv.transform.position;
            Vector3 target = new Vector3(current.x, targetY, current.z);
            bool changed = !Approximately(current, target);
            if (changed)
            {
                Undo.RecordObject(auv.transform,
                    "Apply Phase 2B AUV nominal operating root");
                auv.transform.position = target;
                EditorUtility.SetDirty(auv.transform);
            }

            Vector3 origin = host.TestOrigin;
            Vector3 targetOrigin = new Vector3(origin.x, targetY, origin.z);
            if (!Approximately(origin, targetOrigin))
            {
                host.ConfigureLocalTest(
                    host.SourceId,
                    host.VehicleId,
                    host.VehicleType,
                    host.GeneratorKind,
                    targetOrigin,
                    (float)host.SampleIntervalSeconds,
                    host.StoreCapacity,
                    (float)host.StaleTimeoutSeconds,
                    host.MaxCatchUpStepsPerFrame,
                    host.AutoStart,
                    (float)host.RenderDelaySeconds,
                    (float)host.MaxInterpolationGapSeconds,
                    (float)host.MaxHoldSourceTimeSeconds,
                    (float)host.ExactTimeToleranceSeconds,
                    host.AfterLatestBehavior,
                    host.AllowSingleSampleHold);
                EditorUtility.SetDirty(host);
                changed = true;
            }

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }

            Require(Approximately(auv.transform.position, target) &&
                    Approximately(host.TestOrigin, targetOrigin),
                "AUV nominal root and route origin did not converge.");
            return changed;
        }

        private static bool UpdateCanonicalArtifacts()
        {
            string projectRoot = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            string sceneAbsolute = Path.Combine(
                projectRoot,
                ScenePath.Replace('/', Path.DirectorySeparatorChar));
            string templateAbsolute = Path.Combine(
                projectRoot,
                TemplatePath.Replace('/', Path.DirectorySeparatorChar));
            string manifestAbsolute = Path.Combine(
                projectRoot,
                ManifestPath.Replace('/', Path.DirectorySeparatorChar));
            byte[] sceneBytes = File.ReadAllBytes(sceneAbsolute);
            bool changed = !File.Exists(templateAbsolute) ||
                !File.ReadAllBytes(templateAbsolute).SequenceEqual(sceneBytes);
            if (changed)
            {
                File.WriteAllBytes(templateAbsolute, sceneBytes);
                AssetDatabase.ImportAsset(
                    TemplatePath,
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);
            }
            Require(File.ReadAllBytes(templateAbsolute)
                    .SequenceEqual(sceneBytes),
                "Canonical template is not byte-identical to formal Scene.");

            CanonicalSemanticSignature signature =
                CanonicalSceneRebuildOrchestrator
                    .BuildCanonicalSemanticSignatureForLoadedScene();
            CanonicalSceneIdentityManifestSnapshot current =
                CanonicalSceneIdentityContract.LoadApprovedManifestSnapshot();
            string sceneSha = Sha256(sceneBytes);
            string generationId =
                "env-e3d-operating-volume-phase2b-" +
                sceneSha.Substring(0, 16);
            string evidenceId =
                "env-e3d-phase2b-" +
                Sha256(Encoding.UTF8.GetBytes(
                    sceneSha + "\n" + signature.CanonicalSemanticSha));
            bool manifestMatches =
                string.Equals(current.SceneUnitySha256, sceneSha,
                    StringComparison.Ordinal) &&
                string.Equals(current.SemanticSha256,
                    signature.CanonicalSemanticSha,
                    StringComparison.Ordinal) &&
                current.GameObjectCount == signature.GameObjectCount &&
                current.ComponentCount == signature.ComponentCount &&
                current.MissingReferenceCount ==
                    signature.MissingReferenceCount;
            if (!manifestMatches)
            {
                CanonicalSceneIdentityManifestPrecomputation replacement =
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
                    replacement.CopyManifestBytes());
                AssetDatabase.ImportAsset(
                    ManifestPath,
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);
                changed = true;
            }

            return changed;
        }

        private static GameObject RequireUniqueRoot(Scene scene, string name)
        {
            GameObject[] matches = scene.GetRootGameObjects()
                .Where(root => string.Equals(root.name, name,
                    StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1,
                "Expected exactly one root /" + name + ".");
            return matches[0];
        }

        private static T RequireComponent<T>(GameObject gameObject)
            where T : Component
        {
            T component = gameObject.GetComponent<T>();
            Require(component != null,
                gameObject.name + " is missing " + typeof(T).Name + ".");
            return component;
        }

        private static bool Approximately(Vector3 left, Vector3 right)
        {
            return (left - right).sqrMagnitude <= 0.00000001f;
        }

        private static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }

    internal sealed class EnvE3DOperatingVolumeExpansionPhase2BResult
    {
        internal bool Success;
        internal bool SceneChanged;
        internal bool CanonicalUpdated;
        internal string ConfigurationSha256 = string.Empty;
        internal string ContactMeshSha256 = string.Empty;
        internal string FarMeshSha256 = string.Empty;
        internal float RovTranslationDelta;
        internal float RovRootYBefore;
        internal float RovRootYAfter;
        internal float RovContactMinY;
        internal float RovContactMaxY;
        internal bool RovRawFitPassed;
        internal bool RovTranslatedFitPassed;
        internal float AuvRootY;
        internal string FailureStage = string.Empty;
        internal string FailureMessage = string.Empty;
    }
}
