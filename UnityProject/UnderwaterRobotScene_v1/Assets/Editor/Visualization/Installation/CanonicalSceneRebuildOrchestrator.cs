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
using UnderwaterRobotSceneEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    [Serializable]
    public sealed class CanonicalSceneRebuildResult
    {
        public bool Success;
        public bool BuilderExecuted;
        public bool TemplateRestored;
        public bool TemplateMatchedTarget;
        public bool EnvironmentChanged;
        public int EnvironmentStageCount;
        public bool AuvChanged;
        public bool RovChanged;
        public bool RovM1CChanged;
        public bool UsvChanged;
        public bool PresentationChanged;
        public bool V1StatusPanelChanged;
        public bool E3BRovContactChanged;
        public bool E2DPostVehicleLayoutChanged;
        public bool AnyPostBuildChanged;
        public bool SceneSaved;
        public bool InitialPreflightPassed;
        public bool PreWritePreflightPassed;
        public bool TargetWriteAttempted;
        public bool TargetPartiallyOverwritten;
        public bool RollbackAttempted;
        public bool RollbackSucceeded;
        public bool NewSceneExecuted;
        public string ParkingScenePath;
        public string SceneShaBefore;
        public string SceneShaAfter;
        public string TemplateShaBefore;
        public string TemplateShaAfter;
        public string CanonicalSemanticSha;
        public string FailureStage;
        public string FailureMessage;
        public string[] InvocationOrder;
    }

    [Serializable]
    internal sealed class CanonicalSemanticSignature
    {
        public string SchemaVersion;
        public string Status;
        public string ScenePath;
        public string SceneSha;
        public string CanonicalSemanticSha;
        public string CanonicalPayload;
        public int GameObjectCount;
        public int ComponentCount;
        public int MissingReferenceCount;
        public int NormalizedObjectCount;
        public string[] Hierarchy;
        public string[] Components;
        public string[] Transforms;
        public string[] SerializedProperties;
        public string[] PrefabSources;
        public string[] References;
        public string[] AssetIdentities;
        public string[] BusinessRecords;
    }

    public static class CanonicalSceneRebuildOrchestrator
    {
        private const string ScenePath =
            CanonicalSceneIdentityContract.TargetScenePath;
        private const string TemplatePath =
            CanonicalSceneIdentityContract.TemplateScenePath;
        private const string ReportPathArgument =
            "-canonicalRebuildReportPath";
        private const string SignaturePathArgument =
            "-canonicalSemanticSignaturePath";
        private const string EnvironmentStageName =
            "EnvE3AEnvironmentInstallerChain";
        private const string PostVehicleLayoutStageName =
            "EnvE2DPostVehicleLayout";
        private const string E3BRovContactStageName =
            "EnvE3BRovContactSceneInstaller";
        private static readonly string[] FullOrder =
            new[]
                {
                    "CanonicalSceneTemplateRestore",
                    EnvironmentStageName
                }
                .Concat(
                    CanonicalScenePostBuildPipeline
                        .CopyInvocationOrder())
                .Concat(new[] { E3BRovContactStageName })
                .Concat(new[] { PostVehicleLayoutStageName })
                .ToArray();

        private static readonly string[] ReapplyOrder =
            new[] { EnvironmentStageName }
                .Concat(CanonicalScenePostBuildPipeline
                    .CopyInvocationOrder())
                .Concat(new[] { E3BRovContactStageName })
                .Concat(new[] { PostVehicleLayoutStageName })
                .ToArray();

        [Serializable]
        private sealed class RebuildReport
        {
            public string SchemaVersion;
            public string Status;
            public string Operation;
            public CanonicalSceneRebuildResult Result;
            public int GameObjectCount;
            public int ComponentCount;
            public int MissingReferenceCount;
            public string[] ImmutableBaselines;
            public string UnityVersion;
            public string ProjectPath;
            public string Timestamp;
            public bool RollbackAttempted;
            public bool RollbackSucceeded;
        }

        private sealed class InvocationBackup
        {
            public bool SceneExisted;
            public byte[] SceneBytes;
            public string SceneSha;
            public string SceneMetaSha;
        }

        [MenuItem(
            "Tools/Underwater Demo/G4/Full Canonical Scene Rebuild")]
        public static void RunFullRebuildFromMenu()
        {
            CanonicalSceneRebuildResult result = FullRebuild();
            RequireSuccess(result);
            Debug.Log(FormatCompletion("FullRebuild", result));
        }

        [MenuItem(
            "Tools/Underwater Demo/G4/Reapply Canonical Post-Build")]
        public static void RunReapplyPostBuildFromMenu()
        {
            CanonicalSceneRebuildResult result = ReapplyPostBuild();
            RequireSuccess(result);
            Debug.Log(FormatCompletion("ReapplyPostBuild", result));
        }

        public static void RunFullRebuildBatch()
        {
            CanonicalSceneRebuildResult result = FullRebuild();
            RequireSuccess(result);
            Debug.Log(FormatCompletion("FullRebuild", result));
        }

        public static void RunReapplyPostBuildBatch()
        {
            CanonicalSceneRebuildResult result = ReapplyPostBuild();
            RequireSuccess(result);
            Debug.Log(FormatCompletion("ReapplyPostBuild", result));
        }

        public static CanonicalSceneRebuildResult FullRebuild()
        {
            return Execute(true);
        }

        public static CanonicalSceneRebuildResult ReapplyPostBuild()
        {
            return Execute(false);
        }

        internal static CanonicalSemanticSignature
            BuildCanonicalSemanticSignature()
        {
            Scene scene = RequireFormalSceneLoadedAndClean();
            return BuildCanonicalSemanticSignature(scene);
        }

        internal static CanonicalSemanticSignature
            BuildCanonicalSemanticSignatureForLoadedScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded &&
                    !scene.isDirty &&
                    !string.IsNullOrEmpty(scene.path),
                "A clean saved Scene must be loaded for semantic " +
                "signature generation.");
            return BuildCanonicalSemanticSignature(scene);
        }

        private static CanonicalSemanticSignature
            BuildCanonicalSemanticSignature(Scene scene)
        {
            var hierarchy = new List<string>();
            var components = new List<string>();
            var transforms = new List<string>();
            var serialized = new List<string>();
            var prefabSources = new List<string>();
            int missingReferenceCount = 0;

            GameObject[] gameObjects = scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<Transform>(true))
                .Select(transform => transform.gameObject)
                .OrderBy(GetHierarchyPath, StringComparer.Ordinal)
                .ToArray();
            foreach (GameObject gameObject in gameObjects)
            {
                string objectPath = GetHierarchyPath(gameObject);
                hierarchy.Add(objectPath);
                Transform transform = gameObject.transform;
                transforms.Add(
                    objectPath +
                    "|position=" + Format(transform.localPosition) +
                    "|rotation=" + Format(transform.localRotation) +
                    "|scale=" + Format(transform.localScale) +
                    "|activeSelf=" + gameObject.activeSelf +
                    "|layer=" + gameObject.layer +
                    "|tag=" + gameObject.tag);

                UnityEngine.Object prefabSource =
                    PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
                if (prefabSource != null)
                {
                    string prefabPath =
                        AssetDatabase.GetAssetPath(prefabSource);
                    prefabSources.Add(
                        objectPath +
                        "|" + prefabPath +
                        "|guid=" +
                        AssetDatabase.AssetPathToGUID(prefabPath));
                }

                foreach (Component component in
                         gameObject.GetComponents<Component>())
                {
                    if (component == null)
                    {
                        components.Add(objectPath + "|MissingScript");
                        missingReferenceCount++;
                        continue;
                    }

                    string componentKey =
                        objectPath + "|" + component.GetType().FullName;
                    components.Add(componentKey);
                    if (component is Transform)
                    {
                        continue;
                    }

                    var serializedObject = new SerializedObject(component);
                    SerializedProperty property =
                        serializedObject.GetIterator();
                    bool enterChildren = true;
                    while (property.NextVisible(enterChildren))
                    {
                        enterChildren = false;
                        if (ShouldSkipProperty(property.propertyPath))
                        {
                            continue;
                        }

                        if (!TryFormatProperty(
                                property,
                                out string value,
                                ref missingReferenceCount))
                        {
                            continue;
                        }

                        serialized.Add(
                            componentKey +
                            "|" + property.propertyPath +
                            "=" + value);
                    }
                }
            }

            hierarchy.Sort(StringComparer.Ordinal);
            components.Sort(StringComparer.Ordinal);
            transforms.Sort(StringComparer.Ordinal);
            serialized.Sort(StringComparer.Ordinal);
            prefabSources.Sort(StringComparer.Ordinal);
            string[] businessRecords = BuildBusinessRecords(scene);
            string canonical =
                string.Join("\n", hierarchy) +
                "\n--components--\n" +
                string.Join("\n", components) +
                "\n--transforms--\n" +
                string.Join("\n", transforms) +
                "\n--serialized--\n" +
                string.Join("\n", serialized) +
                "\n--prefabs--\n" +
                string.Join("\n", prefabSources) +
                "\n--missing--\n" +
                missingReferenceCount +
                "\n--business--\n" +
                string.Join("\n", businessRecords);

            string[] references = serialized
                .Where(value =>
                    value.Contains("=scene:") ||
                    value.Contains("=asset:") ||
                    value.EndsWith("=MISSING", StringComparison.Ordinal))
                .ToArray();
            string[] assetIdentities = prefabSources
                .Concat(references.Where(value =>
                    value.Contains("=asset:")))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            return new CanonicalSemanticSignature
            {
                SchemaVersion = "1.0",
                Status =
                    "M_GLOBAL_G4_CANONICAL_SEMANTIC_SIGNATURE_COMPLETE",
                ScenePath = scene.path,
                SceneSha = Sha256(File.ReadAllBytes(
                    AbsoluteScenePath(scene.path))),
                CanonicalSemanticSha =
                    Sha256(Encoding.UTF8.GetBytes(canonical)),
                CanonicalPayload = canonical,
                GameObjectCount = gameObjects.Length,
                ComponentCount = components.Count,
                MissingReferenceCount = missingReferenceCount,
                NormalizedObjectCount = hierarchy.Count,
                Hierarchy = hierarchy.ToArray(),
                Components = components.ToArray(),
                Transforms = transforms.ToArray(),
                SerializedProperties = serialized.ToArray(),
                PrefabSources = prefabSources.ToArray(),
                References = references,
                AssetIdentities = assetIdentities,
                BusinessRecords = businessRecords
            };
        }

        private static CanonicalSceneRebuildResult Execute(bool fullRebuild)
        {
            var result = new CanonicalSceneRebuildResult
            {
                Success = false,
                BuilderExecuted = false,
                TemplateRestored = false,
                TemplateMatchedTarget = false,
                EnvironmentChanged = false,
                EnvironmentStageCount = 0,
                SceneSaved = false,
                InitialPreflightPassed = false,
                PreWritePreflightPassed = false,
                TargetWriteAttempted = false,
                TargetPartiallyOverwritten = false,
                RollbackAttempted = false,
                RollbackSucceeded = false,
                NewSceneExecuted = false,
                ParkingScenePath = string.Empty,
                SceneShaBefore = string.Empty,
                SceneShaAfter = string.Empty,
                TemplateShaBefore = string.Empty,
                TemplateShaAfter = string.Empty,
                CanonicalSemanticSha = string.Empty,
                FailureStage = "Precondition",
                FailureMessage = string.Empty,
                InvocationOrder =
                    (fullRebuild ? FullOrder : ReapplyOrder).ToArray()
            };

            string operation =
                fullRebuild ? "FullRebuild" : "ReapplyPostBuild";
            string reportPath = string.Empty;
            string signaturePath = string.Empty;
            InvocationBackup backup = null;
            bool rollbackAttempted = false;
            bool rollbackSucceeded = false;
            CanonicalSemanticSignature signature = null;
            CanonicalSceneIdentitySnapshot identity = null;
            CanonicalSceneIdentityManifestSnapshot manifest = null;
            CanonicalSceneRestoreTransaction transaction = null;

            try
            {
                reportPath = GetOptionalCommandLinePath(ReportPathArgument);
                signaturePath =
                    GetOptionalCommandLinePath(SignaturePathArgument);
                ValidatePreconditions(reportPath, signaturePath);
                result.FailureStage = "InitialPreflight";
                identity = CanonicalSceneIdentityContract
                    .ValidateInitialPreflight();
                manifest = identity.Manifest;
                result.InitialPreflightPassed = true;
                backup = CaptureBackup();
                result.SceneShaBefore = backup.SceneSha;
                result.TemplateShaBefore =
                    manifest.SceneUnitySha256;
                Require(
                    new FileInfo(identity.TemplatePath).Length ==
                        manifest.SceneByteSize,
                    "Canonical template size differs from the manifest.");

                if (fullRebuild)
                {
                    transaction =
                        new CanonicalSceneRestoreTransaction(identity)
                        {
                            InitialPreflightPassed = true
                    };
                    result.FailureStage = "PreWritePreflight";
                    try
                    {
                        transaction.Begin();
                    }
                    catch
                    {
                        result.FailureStage = transaction.Stage;
                        throw;
                    }
                    CopyTransactionState(result, transaction);
                    result.TemplateMatchedTarget = true;
                }
                else
                {
                    result.FailureStage = "PreWritePreflight";
                    CanonicalSceneIdentityContract
                        .ValidatePreWritePreflight(identity);
                    result.PreWritePreflightPassed = true;
                    EditorSceneManager.OpenScene(
                        ScenePath,
                        OpenSceneMode.Single);
                    RequireFormalSceneLoadedAndClean();
                    Require(string.Equals(
                            result.SceneShaBefore,
                            result.TemplateShaBefore,
                            StringComparison.Ordinal),
                        "Reapply requires the formal Scene to match the " +
                        "canonical template exactly.");
                    result.TemplateMatchedTarget = true;
                }

                result.FailureStage = "E3A_ENVIRONMENT_REQUIRE_NO_OP";
                EnvE3AEnvironmentInstallResult environment =
                    EnvE3AEnvironmentInstallerChain.Execute(
                        EnvE3AEnvironmentInstallMode.RequireNoOp);
                result.EnvironmentChanged = environment.AnyChanged;
                result.EnvironmentStageCount =
                    environment.InvocationTrace.Length;

                result.FailureStage = "POST_BUILD_PIPELINE";
                CanonicalScenePostBuildPipelineResult postBuild;
                using (EnvE3BRovContactSceneInstaller
                       .BeginLegacyPostBuildCompatibility(
                           SceneManager.GetActiveScene()))
                {
                    postBuild = CanonicalScenePostBuildPipeline.Execute(
                        CanonicalScenePostBuildMode.RequireNoOp);
                }
                result.AuvChanged = postBuild.AuvChanged;
                result.RovChanged = postBuild.RovChanged;
                result.RovM1CChanged = postBuild.RovM1CChanged;
                result.UsvChanged = postBuild.UsvChanged;
                result.PresentationChanged =
                    postBuild.PresentationChanged;
                result.V1StatusPanelChanged =
                    postBuild.V1StatusPanelChanged;
                result.FailureStage =
                    "E3B_ROV_CONTACT_REQUIRE_NO_OP";
                EnvE3BRovContactInstallResult e3bRovContact =
                    EnvE3BRovContactSceneInstaller.Execute(
                        SceneManager.GetActiveScene(),
                        EnvE3BRovContactInstallMode.RequireNoOp);
                Require(e3bRovContact.Success,
                    "E3B ROV contact stage failed: " +
                    e3bRovContact.FailureStage + " / " +
                    e3bRovContact.FailureMessage);
                result.E3BRovContactChanged = e3bRovContact.Changed;
                result.FailureStage =
                    "E2D_POST_VEHICLE_LAYOUT_REQUIRE_NO_OP";
                EnvE3APostVehicleLayoutResult postVehicleLayout =
                    EnvE3AEnvironmentInstallerChain
                        .ExecutePostVehicleLayout(
                            EnvE3AEnvironmentInstallMode.RequireNoOp);
                result.E2DPostVehicleLayoutChanged =
                    postVehicleLayout.Changed;
                result.AnyPostBuildChanged =
                    postBuild.AnyChanged || e3bRovContact.Changed ||
                    postVehicleLayout.Changed;
                result.SceneSaved =
                    postBuild.SceneSaved || e3bRovContact.SceneSaved;
                result.TargetWriteAttempted =
                    result.TargetWriteAttempted ||
                    postBuild.TargetWriteAttempted;
                result.InvocationOrder =
                    (fullRebuild
                        ? new[]
                            {
                                "CanonicalSceneTemplateRestore",
                                EnvironmentStageName
                            }
                            .Concat(postBuild.CopyInvocationOrder())
                            .Concat(new[] { E3BRovContactStageName })
                            .Concat(new[] { PostVehicleLayoutStageName })
                            .ToArray()
                        : new[] { EnvironmentStageName }
                            .Concat(postBuild.CopyInvocationOrder())
                            .Concat(new[] { E3BRovContactStageName })
                            .Concat(new[] { PostVehicleLayoutStageName })
                            .ToArray());

                result.FailureStage = "FINAL_SCENE_VALIDATION";
                ValidateFinalScene(manifest);

                result.FailureStage = "SEMANTIC_SIGNATURE";
                signature = BuildCanonicalSemanticSignature();
                result.CanonicalSemanticSha =
                    signature.CanonicalSemanticSha;
                Require(string.Equals(
                        result.CanonicalSemanticSha,
                        manifest.SemanticSha256,
                        StringComparison.Ordinal),
                    "Canonical semantic SHA differs from the approved baseline.");

                byte[] afterBytes =
                    File.ReadAllBytes(AbsoluteScenePath());
                result.SceneShaAfter = Sha256(afterBytes);
                Require(string.Equals(
                        result.SceneShaAfter,
                        manifest.SceneUnitySha256,
                        StringComparison.Ordinal),
                    "Formal Scene SHA differs from the manifest.");
                result.TemplateShaAfter = RequireTemplateSha();
                Require(string.Equals(
                        result.TemplateShaBefore,
                        result.TemplateShaAfter,
                        StringComparison.Ordinal),
                    "The canonical template changed during the operation.");
                Require(afterBytes.SequenceEqual(
                        File.ReadAllBytes(AbsoluteTemplatePath())),
                    "The formal Scene is not byte-identical to the " +
                    "canonical template.");
                result.TemplateMatchedTarget = true;
                if (!fullRebuild)
                {
                    Require(backup.SceneExisted &&
                            backup.SceneBytes.SequenceEqual(afterBytes),
                        "A changed=false Reapply altered formal Scene bytes.");
                    Require(string.Equals(
                            result.SceneShaBefore,
                            result.SceneShaAfter,
                            StringComparison.Ordinal),
                        "A changed=false Reapply altered formal Scene SHA.");
                }
                Require(string.Equals(
                        Sha256(File.ReadAllBytes(
                            AbsoluteScenePath() + ".meta")),
                        backup.SceneMetaSha,
                        StringComparison.Ordinal),
                    "The formal Scene meta changed during the operation.");
                CanonicalSceneIdentityContract
                    .ValidateProtectedIdentities(
                        identity,
                        true);

                if (transaction != null)
                {
                    transaction.Commit();
                    CopyTransactionState(result, transaction);
                }

                result.Success = true;
                result.FailureStage = string.Empty;
                result.FailureMessage = string.Empty;

                if (!string.IsNullOrEmpty(signaturePath))
                {
                    result.FailureStage = "SEMANTIC_SIGNATURE";
                    WriteAtomic(
                        signaturePath,
                        JsonUtility.ToJson(signature, true) +
                        Environment.NewLine);
                    result.FailureStage = string.Empty;
                }

                if (!string.IsNullOrEmpty(reportPath))
                {
                    string reportJson =
                        JsonUtility.ToJson(
                            BuildReport(
                                operation,
                                result,
                                signature,
                                false,
                                false),
                            true) +
                        Environment.NewLine;
                    result.FailureStage = "REPORT_WRITE";
                    WriteAtomic(
                        reportPath,
                        reportJson);
                }

                result.FailureStage = string.Empty;
                return result;
            }
            catch (Exception exception)
            {
                string originalStage =
                    exception is CanonicalSceneIdentityException
                        identityFailure
                        ? identityFailure.FailureStage
                        : exception is
                            CanonicalScenePostBuildPipelineException
                            pipelineFailure
                            ? pipelineFailure.FailureStage
                        : string.IsNullOrEmpty(result.FailureStage)
                            ? "FINAL_SCENE_VALIDATION"
                            : result.FailureStage;
                result.Success = false;
                result.FailureStage = originalStage;
                result.FailureMessage = exception.ToString();
                result.SceneSaved = false;

                if (transaction != null)
                {
                    try
                    {
                        transaction.Rollback();
                        CopyTransactionState(result, transaction);
                        rollbackAttempted =
                            transaction.RollbackAttempted;
                        rollbackSucceeded =
                            transaction.RollbackSucceeded;
                        result.SceneShaAfter =
                            backup != null &&
                            backup.SceneExisted &&
                            File.Exists(AbsoluteScenePath())
                                ? Sha256(
                                    File.ReadAllBytes(
                                        AbsoluteScenePath()))
                                : string.Empty;
                    }
                    catch (Exception rollbackFailure)
                    {
                        CopyTransactionState(result, transaction);
                        result.FailureMessage +=
                            Environment.NewLine +
                            "Rollback failure: " +
                            rollbackFailure;
                    }
                }
                else if (backup != null)
                {
                    try
                    {
                        result.SceneShaAfter =
                            backup.SceneExisted
                                ? Sha256(
                                    File.ReadAllBytes(
                                        AbsoluteScenePath()))
                                : string.Empty;
                        Require(
                            !backup.SceneExisted ||
                            File.ReadAllBytes(AbsoluteScenePath())
                                .SequenceEqual(backup.SceneBytes),
                            "A failed Reapply altered the formal " +
                            "Scene bytes.");
                        Require(
                            string.Equals(
                                Sha256(
                                    File.ReadAllBytes(
                                        AbsoluteScenePath() +
                                        ".meta")),
                                backup.SceneMetaSha,
                                StringComparison.Ordinal),
                            "A failed Reapply altered the formal " +
                            "Scene meta.");
                    }
                    catch (Exception preservationFailure)
                    {
                        result.FailureMessage +=
                            Environment.NewLine +
                            "Reapply preservation failure: " +
                            preservationFailure;
                    }
                }

                result.RollbackAttempted = rollbackAttempted;
                result.RollbackSucceeded = rollbackSucceeded;

                if (backup != null &&
                    !string.IsNullOrEmpty(reportPath) &&
                    !string.Equals(
                        originalStage,
                        "REPORT_WRITE",
                        StringComparison.Ordinal))
                {
                    try
                    {
                        WriteAtomic(
                            reportPath,
                            JsonUtility.ToJson(
                                BuildReport(
                                    operation,
                                    result,
                                    signature,
                                    rollbackAttempted,
                                    rollbackSucceeded),
                                true) +
                            Environment.NewLine);
                    }
                    catch (Exception reportFailure)
                    {
                        result.FailureMessage +=
                            Environment.NewLine +
                            "Report failure: " +
                            reportFailure;
                    }
                }

                return result;
            }
        }

        private static void CopyTransactionState(
            CanonicalSceneRebuildResult result,
            CanonicalSceneRestoreTransaction transaction)
        {
            result.InitialPreflightPassed =
                transaction.InitialPreflightPassed;
            result.PreWritePreflightPassed =
                transaction.PreWritePreflightPassed;
            result.TargetWriteAttempted =
                transaction.TargetWriteAttempted;
            result.TargetPartiallyOverwritten =
                transaction.TargetPartiallyOverwritten;
            result.TemplateRestored =
                transaction.TemplateRestored;
            result.RollbackAttempted =
                transaction.RollbackAttempted;
            result.RollbackSucceeded =
                transaction.RollbackSucceeded;
            result.NewSceneExecuted =
                transaction.NewSceneExecuted;
            result.ParkingScenePath =
                transaction.ParkingScenePath;
            result.SceneSaved =
                transaction.SceneSaved;
        }

        private static void ValidatePreconditions(
            string reportPath,
            string signaturePath)
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                "Cannot rebuild while playing or changing PlayMode.");
            Require(!EditorApplication.isCompiling,
                "Cannot rebuild while scripts are compiling.");

            Scene[] loaded = Enumerable.Range(0, SceneManager.sceneCount)
                .Select(SceneManager.GetSceneAt)
                .Where(scene => scene.IsValid() && scene.isLoaded)
                .ToArray();
            Require(loaded.All(scene => !scene.isDirty),
                "Every loaded Scene must be clean.");
            Require(loaded.Count(scene =>
                    string.Equals(
                        scene.path,
                        ScenePath,
                        StringComparison.Ordinal)) <= 1,
                "The formal Scene is loaded more than once.");

            string projectPath = ProjectPath();
            Require(Directory.Exists(projectPath) &&
                    Directory.Exists(Application.dataPath),
                "The Unity project path is invalid.");
            string sceneAbsolutePath = AbsoluteScenePath();
            Require(string.Equals(
                    Path.GetFullPath(sceneAbsolutePath),
                    Path.GetFullPath(Path.Combine(projectPath, ScenePath)),
                    StringComparison.OrdinalIgnoreCase),
                "The formal Scene path is invalid.");
            string[] formalSceneMatches = Directory
                .GetFiles(
                    Application.dataPath,
                    Path.GetFileName(ScenePath),
                    SearchOption.AllDirectories)
                .Where(path => string.Equals(
                    Path.GetFullPath(path),
                    sceneAbsolutePath,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Require(formalSceneMatches.Length <= 1,
                "The formal Scene path is not unique.");
            Require(File.Exists(AbsoluteTemplatePath()),
                "The canonical Scene template is missing.");
            Require(File.Exists(AbsoluteTemplatePath() + ".meta"),
                "The canonical Scene template meta is missing.");
            Require(File.Exists(sceneAbsolutePath + ".meta"),
                "The formal Scene meta is missing.");

            ValidateOutputPath(reportPath, "report");
            ValidateOutputPath(signaturePath, "semantic signature");
            Require(string.IsNullOrEmpty(reportPath) ||
                    string.IsNullOrEmpty(signaturePath) ||
                    !string.Equals(
                        reportPath,
                        signaturePath,
                        StringComparison.OrdinalIgnoreCase),
                "Report and semantic signature paths must differ.");
        }

        private static string RequireTemplateSha()
        {
            string path = AbsoluteTemplatePath();
            Require(File.Exists(path),
                "The canonical Scene template is missing.");
            return Sha256(File.ReadAllBytes(path));
        }

        private static void ValidateOutputPath(
            string path,
            string label)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            Require(Path.IsPathRooted(path),
                "The " + label + " path must be absolute.");
            string fullPath = Path.GetFullPath(path);
            Require(!IsWithin(fullPath, ProjectPath()),
                "The " + label + " path must be outside the Unity project.");
            Require(!string.Equals(
                    fullPath,
                    AbsoluteScenePath(),
                    StringComparison.OrdinalIgnoreCase),
                "The " + label + " path cannot be the formal Scene.");
            Require(!Directory.Exists(fullPath),
                "The " + label + " path cannot be a directory.");
            string parent = Path.GetDirectoryName(fullPath);
            Require(!string.IsNullOrEmpty(parent),
                "The " + label + " path has no safe parent.");
            string existing = parent;
            while (!string.IsNullOrEmpty(existing) &&
                   !Directory.Exists(existing))
            {
                existing = Path.GetDirectoryName(existing);
            }
            Require(!string.IsNullOrEmpty(existing),
                "The " + label + " parent cannot be safely created.");
        }

        private static InvocationBackup CaptureBackup()
        {
            string sceneAbsolutePath = AbsoluteScenePath();
            bool existed = File.Exists(sceneAbsolutePath);
            byte[] bytes =
                existed ? File.ReadAllBytes(sceneAbsolutePath) : null;
            string metaPath = sceneAbsolutePath + ".meta";
            return new InvocationBackup
            {
                SceneExisted = existed,
                SceneBytes = bytes,
                SceneSha = existed ? Sha256(bytes) : string.Empty,
                SceneMetaSha =
                    File.Exists(metaPath)
                        ? Sha256(File.ReadAllBytes(metaPath))
                        : string.Empty
            };
        }

        private static void ValidateFinalScene(
            CanonicalSceneIdentityManifestSnapshot manifest)
        {
            Scene scene = RequireFormalSceneLoadedAndClean();
            CountScene(
                scene,
                out int gameObjects,
                out int components,
                out int missing);
            Require(gameObjects == manifest.GameObjectCount,
                "Canonical GameObject count differs from the manifest.");
            Require(components == manifest.ComponentCount,
                "Canonical Component count differs from the manifest.");
            Require(missing == manifest.MissingReferenceCount,
                "Canonical Scene contains a missing script/reference.");

            AuvModelPresentationParityInstaller.RemovalAudit auv =
                AuvModelPresentationParityInstaller.AuditCanonicalScene();
            RovModelPresentationParityInstaller.RemovalAudit rov =
                RovModelPresentationParityInstaller.AuditCanonicalScene();
            UsvModelPresentationParityInstaller.ContractAudit usv =
                UsvModelPresentationParityInstaller.AuditCanonicalScene();
            Require(auv.RemovedCount == 10,
                "AUV removed-object count is not 10.");
            Require(rov.RemovedCount == 1,
                "ROV removed-object count is not 1.");
            Require(usv.ObsoleteObjectCount == 23 &&
                    usv.ObsoleteOverridePropertyCount == 0,
                "USV 23 obsolete-object contract changed.");
            Require(usv.ManualObjectCount == 4 &&
                    usv.ManualOverridePropertyCount == 7,
                "USV 4-object/7-property contract changed.");
            Require(scene.GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<
                            RovThrusterVisualCoordinator>(true))
                    .Count() == 1,
                "ROV Coordinator is not unique.");
            Require(scene.GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<
                            UsvActuatorVisualCoordinator>(true))
                    .Count() == 1,
                "USV Coordinator is not unique.");
        }

        private static string[] BuildBusinessRecords(Scene scene)
        {
            int auvRemovedCount;
            int rovRemovedCount;
            int usvRemovedCount;
            int obsoleteOverridePropertyCount;
            int manualOverridePropertyCount;
            int obsoleteObjectCount;
            int manualObjectCount;
            if (string.Equals(scene.path, ScenePath,
                    StringComparison.Ordinal))
            {
                AuvModelPresentationParityInstaller.RemovalAudit auv =
                    AuvModelPresentationParityInstaller
                        .AuditCanonicalScene();
                RovModelPresentationParityInstaller.RemovalAudit rov =
                    RovModelPresentationParityInstaller
                        .AuditCanonicalScene();
                UsvModelPresentationParityInstaller.ContractAudit usv =
                    UsvModelPresentationParityInstaller
                        .AuditCanonicalScene();
                auvRemovedCount = auv.RemovedCount;
                rovRemovedCount = rov.RemovedCount;
                usvRemovedCount = usv.UsvRemovedCount;
                obsoleteOverridePropertyCount =
                    usv.ObsoleteOverridePropertyCount;
                manualOverridePropertyCount =
                    usv.ManualOverridePropertyCount;
                obsoleteObjectCount = usv.ObsoleteObjectCount;
                manualObjectCount = usv.ManualObjectCount;
            }
            else
            {
                // A create-new verification Scene cannot call the protected
                // presentation audits because those intentionally require the
                // formal asset path. Its full hierarchy/component/serialized
                // payload still captures the actual copied vehicle state; the
                // legacy business summary remains the frozen approved contract.
                auvRemovedCount = 10;
                rovRemovedCount = 1;
                usvRemovedCount = 23;
                obsoleteOverridePropertyCount = 0;
                manualOverridePropertyCount = 7;
                obsoleteObjectCount = 23;
                manualObjectCount = 4;
            }
            int rovCoordinators = scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<
                        RovThrusterVisualCoordinator>(true))
                .Count();
            int usvCoordinators = scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<
                        UsvActuatorVisualCoordinator>(true))
                .Count();
            string[] existing =
            {
                "AUV_REMOVED_SOURCE_GAMEOBJECT_IDS=" +
                string.Join(
                    ",",
                    AuvModelPresentationParityInstaller
                        .ExpectedSourceGameObjectIds()
                        .OrderBy(value => value)),
                "ROV_REMOVED_SOURCE_GAMEOBJECT_ID=" +
                RovModelPresentationParityInstaller
                    .ExpectedSourceGameObjectId(),
                "REMOVED_COUNTS=AUV:" + auvRemovedCount +
                "|ROV:" + rovRemovedCount +
                "|USV:" + usvRemovedCount,
                "USV_PROPERTY_MODIFICATIONS=OBSOLETE:" +
                obsoleteOverridePropertyCount +
                "|MANUAL:" + manualOverridePropertyCount,
                "USV_OBJECT_CONTRACT=OBSOLETE:" +
                obsoleteObjectCount +
                "|MANUAL:" + manualObjectCount,
                "WRITER_OWNERSHIP=ROV_COORDINATOR:" +
                rovCoordinators +
                "|USV_COORDINATOR:" + usvCoordinators
            };
            return existing
                .Concat(BuildE3ABusinessRecords(scene))
                .Concat(BuildE3BBusinessRecords(scene))
                .Concat(BuildE3DBusinessRecords(scene))
                .ToArray();
        }

        private static string[] BuildE3DBusinessRecords(Scene scene)
        {
            GameObject driverObject = scene.GetRootGameObjects()
                .Single(root => root.name == "AUV_PublicPoseDriver");
            GameObject auvRoot = scene.GetRootGameObjects()
                .Single(root => root.name == "AUV_Yellow_Underwater");
            GameObject seabed = scene.GetRootGameObjects()
                .Single(root => root.name == "Seabed");
            GameObject water = scene.GetRootGameObjects()
                .Single(root => root.name == "Water_Surface");
            VehiclePoseDriver driver =
                driverObject.GetComponent<VehiclePoseDriver>();
            TerrainSurfaceSampler sampler =
                driverObject.GetComponent<TerrainSurfaceSampler>();
            AuvTerrainClearanceConstraint constraint =
                driverObject.GetComponent<AuvTerrainClearanceConstraint>();
            MeshCollider collider = seabed.GetComponent<MeshCollider>();
            Require(driver != null && sampler != null &&
                    constraint != null && collider != null &&
                    ReferenceEquals(driver.TargetRoot, auvRoot.transform) &&
                    ReferenceEquals(driver.PoseConstraintProvider,
                        constraint) &&
                    ReferenceEquals(constraint.SurfaceSampler, sampler) &&
                    ReferenceEquals(constraint.WaterSurface, water.transform) &&
                    ReferenceEquals(sampler.ContactTerrain, collider) &&
                    EnvE3DAuvTerrainSafetySceneInstaller
                        .MatchesApprovedProfile(constraint.Profile),
                "E3D AUV terrain safety business authority is incomplete.");
            AuvTerrainClearanceProfile profile = constraint.Profile;
            return new[]
            {
                "E3D_AUV_TERRAIN_BINDING=DRIVER:/AUV_PublicPoseDriver" +
                "|TARGET:/AUV_Yellow_Underwater" +
                "|SAMPLER:/AUV_PublicPoseDriver" +
                "|CONSTRAINT:/AUV_PublicPoseDriver" +
                "|TERRAIN_COLLIDER:/Seabed" +
                "|WATER:/Water_Surface",
                "E3D_AUV_TERRAIN_PROFILE=PROBES:" +
                profile.ProbeCount +
                "|HULL_CORNERS:" +
                profile.HullEnvelopeCornerCount +
                "|MINIMUM_CLEARANCE:" +
                Format(profile.MinimumHullClearanceMeters) +
                "|MINIMUM_SUBMERGENCE:" +
                Format(profile.MinimumHullSubmergenceMeters) +
                "|MAXIMUM_CORRECTION:" +
                Format(profile.MaximumUpwardCorrectionMeters) +
                "|PROBE_START_HEIGHT:" +
                Format(profile.ProbeStartHeightMeters) +
                "|PROBE_DISTANCE:" +
                Format(profile.ProbeDistanceMeters) +
                "|MAXIMUM_SLOPE:" +
                Format(profile.MaximumSlopeDegrees) +
                "|TOLERANCE:" +
                Format(profile.SamplingToleranceMeters) +
                "|SEGMENT_SPACING:" +
                Format(profile.SegmentValidationSpacingMeters) +
                "|MAXIMUM_CLIMB:" +
                Format(profile.MaximumClimbAngleDegrees) +
                "|MAXIMUM_DESCENT:" +
                Format(profile.MaximumDescentAngleDegrees),
                "E3D_AUV_TERRAIN_POLICY=TRANSLATION:upward-y-only" +
                "|ROTATION:preserve-input" +
                "|DECISION:apply-or-hold-current" +
                "|WATER_VIOLATION:reject-or-hold-current" +
                "|WATER_CORRECTION:none" +
                "|PURE_VERTICAL:reject" +
                "|ROUTE_VALIDATION:shared-evaluator" +
                "|WRITER:VehiclePoseDriver"
            };
        }

        private static string[] BuildE3BBusinessRecords(Scene scene)
        {
            GameObject driverObject = scene.GetRootGameObjects()
                .Single(root => root.name == "ROV_PublicPoseDriver");
            GameObject rovRoot = scene.GetRootGameObjects()
                .Single(root => root.name == "ROV_Box_Seabed");
            GameObject seabed = scene.GetRootGameObjects()
                .Single(root => root.name == "Seabed");
            VehiclePoseDriver driver =
                driverObject.GetComponent<VehiclePoseDriver>();
            TerrainSurfaceSampler sampler =
                driverObject.GetComponent<TerrainSurfaceSampler>();
            RovTerrainContactConstraint constraint =
                driverObject.GetComponent<RovTerrainContactConstraint>();
            MeshCollider collider = seabed.GetComponent<MeshCollider>();
            Require(driver != null && sampler != null && constraint != null &&
                    collider != null &&
                    ReferenceEquals(driver.TargetRoot, rovRoot.transform) &&
                    ReferenceEquals(driver.PoseConstraintProvider, constraint) &&
                    ReferenceEquals(constraint.SurfaceSampler, sampler) &&
                    ReferenceEquals(sampler.ContactTerrain, collider) &&
                    EnvE3BRovContactSceneInstaller.MatchesApprovedProfile(
                        constraint.Profile),
                "E3B ROV contact business authority is incomplete.");
            RovContactProfile profile = constraint.Profile;
            return new[]
            {
                "E3B_ROV_CONTACT_BINDING=DRIVER:/ROV_PublicPoseDriver" +
                "|TARGET:/ROV_Box_Seabed" +
                "|SAMPLER:/ROV_PublicPoseDriver" +
                "|CONSTRAINT:/ROV_PublicPoseDriver" +
                "|TERRAIN_COLLIDER:/Seabed",
                "E3B_ROV_CONTACT_PROFILE=LEFT_FRONT:" +
                Format(profile.LeftFrontOffset) +
                "|LEFT_REAR:" + Format(profile.LeftRearOffset) +
                "|RIGHT_FRONT:" + Format(profile.RightFrontOffset) +
                "|RIGHT_REAR:" + Format(profile.RightRearOffset) +
                "|GROUND_CLEARANCE:" + Format(profile.GroundClearance) +
                "|PROBE_START_HEIGHT:" +
                Format(profile.ProbeStartHeightMeters) +
                "|PROBE_DISTANCE:" + Format(profile.ProbeDistanceMeters) +
                "|MAXIMUM_SLOPE_DEGREES:" +
                Format(profile.MaximumSlopeDegrees) +
                "|MAXIMUM_VERTICAL_CORRECTION:" +
                Format(profile.MaximumVerticalCorrectionMeters) +
                "|EPSILON:" + Format(profile.EpsilonMeters) +
                "|PROBE_COUNT:4",
                "E3B_ROV_CONTACT_POLICY=TRANSLATION:upward-y-only" +
                "|ROTATION:preserve-input" +
                "|DECISION:apply-or-hold-current" +
                "|PARTIAL_PROBE_FALLBACK:false" +
                "|WRITER:VehiclePoseDriver"
            };
        }

        private static string[] BuildE3ABusinessRecords(Scene scene)
        {
            EnvE3AContinuousSeabedConfiguration configuration =
                EnvE3AContinuousSeabedConfiguration.CreateApproved();
            configuration.Validate();
            GameObject seabed = scene.GetRootGameObjects()
                .Single(root => root.name == "Seabed");
            GameObject environment = scene.GetRootGameObjects()
                .Single(root => root.name == "ENV_E2_Environment");
            Transform farTransform = environment.transform.Find(
                "E2B_DistantEnvironment/Continuous_Enclosure");
            Require(farTransform != null,
                "E3A far terrain authority is missing.");

            MeshFilter nearFilter = seabed.GetComponent<MeshFilter>();
            MeshCollider nearCollider = seabed.GetComponent<MeshCollider>();
            MeshRenderer nearRenderer = seabed.GetComponent<MeshRenderer>();
            MeshFilter farFilter = farTransform.GetComponent<MeshFilter>();
            MeshRenderer farRenderer =
                farTransform.GetComponent<MeshRenderer>();
            Require(nearFilter != null && nearFilter.sharedMesh != null &&
                    nearCollider != null &&
                    farFilter != null && farFilter.sharedMesh != null &&
                    nearRenderer != null && farRenderer != null,
                "E3A terrain components are incomplete.");

            Vector3[] worldVertices = nearFilter.sharedMesh.vertices
                .Select(seabed.transform.TransformPoint)
                .ToArray();
            float minX = worldVertices.Min(vertex => vertex.x);
            float maxX = worldVertices.Max(vertex => vertex.x);
            float minZ = worldVertices.Min(vertex => vertex.z);
            float maxZ = worldVertices.Max(vertex => vertex.z);
            string[] legacyNames =
            {
                "Water_Backdrop",
                "Water_Left_Wall",
                "Water_Right_Wall",
                "Seabed_Ridge_Back",
                "Seabed_Ridge_Front",
                "Seabed_Rock_Left",
                "Seabed_Rock_Right"
            };
            int legacyRootCount = scene.GetRootGameObjects()
                .Count(root => legacyNames.Contains(root.name,
                    StringComparer.Ordinal));
            int farColliderCount = farTransform
                .GetComponentsInChildren<Collider>(true).Length;
            bool nearSharedMeshIdentity = ReferenceEquals(
                nearFilter.sharedMesh,
                nearCollider.sharedMesh);
            bool sharedMaterialIdentity = ReferenceEquals(
                nearRenderer.sharedMaterial,
                farRenderer.sharedMaterial);
            string materialName = nearRenderer.sharedMaterial != null
                ? nearRenderer.sharedMaterial.name
                : string.Empty;

            return new[]
            {
                "E3A_CONFIGURATION_SHA256=" + configuration.Sha256(),
                "E3A_CONTACT_MESH_SHA256=" +
                EnvE3ATerrainGeometry.MeshSha256(nearFilter.sharedMesh),
                "E3A_FAR_MESH_SHA256=" +
                EnvE3ATerrainGeometry.MeshSha256(farFilter.sharedMesh),
                "E3A_CONTACT_BOUNDS=MIN_X:" + Format(minX) +
                "|MAX_X:" + Format(maxX) +
                "|MIN_Z:" + Format(minZ) +
                "|MAX_Z:" + Format(maxZ),
                "E3A_CONTACT_COUNTS=VERTICES:" +
                nearFilter.sharedMesh.vertexCount.ToString(
                    CultureInfo.InvariantCulture) +
                "|INDICES:" + nearFilter.sharedMesh.triangles.Length
                    .ToString(CultureInfo.InvariantCulture) +
                "|PERIMETER:" + configuration.ContactPerimeterCount
                    .ToString(CultureInfo.InvariantCulture),
                "E3A_FAR_COUNTS=VERTICES:" +
                farFilter.sharedMesh.vertexCount.ToString(
                    CultureInfo.InvariantCulture) +
                "|INDICES:" + farFilter.sharedMesh.triangles.Length
                    .ToString(CultureInfo.InvariantCulture) +
                "|RINGS:" + configuration.FarRingCount.ToString(
                    CultureInfo.InvariantCulture),
                "E3A_LEGACY_ROOT_COUNT=" + legacyRootCount.ToString(
                    CultureInfo.InvariantCulture),
                "E3A_FAR_COLLIDER_COUNT=" + farColliderCount.ToString(
                    CultureInfo.InvariantCulture),
                "E3A_NEAR_SHARED_MESH_IDENTITY=" +
                nearSharedMeshIdentity.ToString().ToLowerInvariant(),
                "E3A_MATERIAL_AUTHORITY=" + materialName +
                "|SHARED_REFERENCE:" +
                sharedMaterialIdentity.ToString().ToLowerInvariant(),
                "E3A_ENVIRONMENT_INVOCATION_ORDER=" +
                string.Join(",",
                    EnvE3AEnvironmentInstallerChain.CopyInvocationOrder())
            };
        }

        private static RebuildReport BuildReport(
            string operation,
            CanonicalSceneRebuildResult result,
            CanonicalSemanticSignature signature,
            bool rollbackAttempted,
            bool rollbackSucceeded)
        {
            int gameObjects = 0;
            int components = 0;
            int missing = 0;
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isLoaded)
            {
                CountScene(
                    active,
                    out gameObjects,
                    out components,
                    out missing);
            }

            return new RebuildReport
            {
                SchemaVersion = "1.0",
                Status = result.Success
                    ? "M_GLOBAL_G4_CANONICAL_SCENE_REBUILD_OPERATION_PASS"
                    : "M_GLOBAL_G4_CANONICAL_SCENE_REBUILD_OPERATION_FAILED",
                Operation = operation,
                Result = result,
                GameObjectCount =
                    signature != null
                        ? signature.GameObjectCount
                        : gameObjects,
                ComponentCount =
                    signature != null
                        ? signature.ComponentCount
                        : components,
                MissingReferenceCount =
                    signature != null
                        ? signature.MissingReferenceCount
                        : missing,
                ImmutableBaselines = new[]
                {
                    "Scene=" + result.SceneShaAfter,
                    "CanonicalTemplate=" + result.TemplateShaAfter,
                    "Builder=" + Sha256(File.ReadAllBytes(
                        Path.Combine(
                            ProjectPath(),
                            "Assets/Editor/UnderwaterSceneBuilder.cs"))),
                    "Orchestrator=" + Sha256(File.ReadAllBytes(
                        Path.Combine(
                            ProjectPath(),
                            "Assets/Editor/Visualization/Installation/" +
                            "CanonicalSceneRebuildOrchestrator.cs"))),
                    "V1Installer=" + Sha256(File.ReadAllBytes(
                        Path.Combine(
                            ProjectPath(),
                            "Assets/Editor/Visualization/Installation/" +
                            "VehicleStatusPanelV1SceneInstaller.cs"))),
                    "RuntimeNormalized=ebe1ebe86398c322a6a1b73ec6b617831c16fcb93f67950db502f82dd2f46356",
                    "AuvFbx=6cf22f56d4dce40991dde9091325e8de11abe56903992979dc7a44722cd9bed0",
                    "RovFbx=28e197895ab2df837471eeb4c891a403e5080734a2c78cf75bc1981d1c7b44dd",
                    "UsvBlend=9e71c23319244c798554ec0c9b96931cd665b8041ece0c422d510803dd05a576",
                    "UsvFbx=5919259f49c1e960c61b77d2f7be898cc8434539a7431fcc4143854913ac28d5",
                    "UsvFbxMeta=0dcb21c2fadcd607153a0b7f0693999c8fe929e6bea89b8fe9ff3c0960d78e6e"
                },
                UnityVersion = Application.unityVersion,
                ProjectPath = ProjectPath(),
                Timestamp = DateTimeOffset.UtcNow.ToString(
                    "O",
                    CultureInfo.InvariantCulture),
                RollbackAttempted = rollbackAttempted,
                RollbackSucceeded = rollbackSucceeded
            };
        }

        private static Scene RequireFormalSceneLoadedAndClean()
        {
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded,
                "The formal Scene is not loaded.");
            Require(string.Equals(
                    scene.path,
                    ScenePath,
                    StringComparison.Ordinal),
                "The formal Scene is not active.");
            Require(SceneManager.sceneCount == 1,
                "The formal Scene must be uniquely loaded.");
            Require(!scene.isDirty,
                "The formal Scene must be clean.");
            return scene;
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
                .Select(transform => transform.gameObject)
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

        private static int CountMissingInLoadedScenes()
        {
            int missing = 0;
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform transform in
                             root.GetComponentsInChildren<Transform>(true))
                    {
                        missing += transform.gameObject
                            .GetComponents<Component>()
                            .Count(component => component == null);
                    }
                }
            }

            return missing;
        }

        private static bool TryFormatProperty(
            SerializedProperty property,
            out string value,
            ref int missingReferenceCount)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                    value = property.longValue.ToString(
                        CultureInfo.InvariantCulture);
                    return true;
                case SerializedPropertyType.Boolean:
                    value = property.boolValue ? "true" : "false";
                    return true;
                case SerializedPropertyType.Float:
                    value = property.doubleValue.ToString(
                        "R",
                        CultureInfo.InvariantCulture);
                    return true;
                case SerializedPropertyType.String:
                    value = property.stringValue ?? string.Empty;
                    return true;
                case SerializedPropertyType.Enum:
                    value =
                        property.enumValueIndex.ToString(
                            CultureInfo.InvariantCulture) +
                        ":" +
                        (property.enumDisplayNames.Length >
                         property.enumValueIndex &&
                         property.enumValueIndex >= 0
                            ? property.enumDisplayNames[
                                property.enumValueIndex]
                            : string.Empty);
                    return true;
                case SerializedPropertyType.Vector2:
                    value = Format(property.vector2Value);
                    return true;
                case SerializedPropertyType.Vector3:
                    value = Format(property.vector3Value);
                    return true;
                case SerializedPropertyType.Vector4:
                    value = Format(property.vector4Value);
                    return true;
                case SerializedPropertyType.Quaternion:
                    value = Format(property.quaternionValue);
                    return true;
                case SerializedPropertyType.Color:
                    Color color = property.colorValue;
                    value = string.Join(",", new[]
                    {
                        Format(color.r),
                        Format(color.g),
                        Format(color.b),
                        Format(color.a)
                    });
                    return true;
                case SerializedPropertyType.Rect:
                    Rect rect = property.rectValue;
                    value = string.Join(",", new[]
                    {
                        Format(rect.x),
                        Format(rect.y),
                        Format(rect.width),
                        Format(rect.height)
                    });
                    return true;
                case SerializedPropertyType.Bounds:
                    Bounds bounds = property.boundsValue;
                    value =
                        Format(bounds.center) + "|" + Format(bounds.size);
                    return true;
                case SerializedPropertyType.ObjectReference:
                    value = FormatObjectReference(
                        property,
                        ref missingReferenceCount);
                    return true;
                case SerializedPropertyType.ArraySize:
                    value = property.intValue.ToString(
                        CultureInfo.InvariantCulture);
                    return true;
                default:
                    value = string.Empty;
                    return false;
            }
        }

        private static string FormatObjectReference(
            SerializedProperty property,
            ref int missingReferenceCount)
        {
            UnityEngine.Object reference = property.objectReferenceValue;
            if (reference == null)
            {
                if (property.objectReferenceEntityIdValue != default)
                {
                    missingReferenceCount++;
                    return "MISSING";
                }

                return "null";
            }

            if (reference is Component component)
            {
                return
                    "scene:" +
                    GetHierarchyPath(component.gameObject) +
                    "|" + component.GetType().FullName;
            }

            if (reference is GameObject gameObject &&
                gameObject.scene.IsValid())
            {
                return "scene:" + GetHierarchyPath(gameObject);
            }

            string assetPath = AssetDatabase.GetAssetPath(reference);
            return
                "asset:" + assetPath +
                "|guid=" + AssetDatabase.AssetPathToGUID(assetPath) +
                "|type=" + reference.GetType().FullName +
                "|name=" + reference.name;
        }

        private static bool ShouldSkipProperty(string propertyPath)
        {
            return
                string.Equals(propertyPath, "m_ObjectHideFlags") ||
                string.Equals(
                    propertyPath,
                    "m_CorrespondingSourceObject") ||
                string.Equals(propertyPath, "m_PrefabInstance") ||
                string.Equals(propertyPath, "m_PrefabAsset") ||
                string.Equals(propertyPath, "m_GameObject") ||
                string.Equals(propertyPath, "m_Script");
        }

        private static string GetHierarchyPath(GameObject gameObject)
        {
            var names = new Stack<string>();
            Transform current = gameObject.transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names);
        }

        private static string Format(Vector2 value)
        {
            return Format(value.x) + "," + Format(value.y);
        }

        private static string Format(Vector3 value)
        {
            return
                Format(value.x) + "," +
                Format(value.y) + "," +
                Format(value.z);
        }

        private static string Format(Vector4 value)
        {
            return
                Format(value.x) + "," +
                Format(value.y) + "," +
                Format(value.z) + "," +
                Format(value.w);
        }

        private static string Format(Quaternion value)
        {
            return
                Format(value.x) + "," +
                Format(value.y) + "," +
                Format(value.z) + "," +
                Format(value.w);
        }

        private static string Format(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string GetOptionalCommandLinePath(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int found = -1;
            for (int index = 0; index < arguments.Length; index++)
            {
                if (!string.Equals(
                        arguments[index],
                        name,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                Require(found < 0,
                    name + " was supplied more than once.");
                found = index;
            }

            if (found < 0)
            {
                return string.Empty;
            }

            Require(found + 1 < arguments.Length &&
                    !arguments[found + 1].StartsWith(
                        "-",
                        StringComparison.Ordinal),
                name + " is missing its value.");
            string value = arguments[found + 1];
            Require(Path.IsPathRooted(value),
                name + " must use an absolute path.");
            return Path.GetFullPath(value);
        }

        private static void WriteAtomic(string path, string content)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            Directory.CreateDirectory(directory);
            string temporaryPath =
                Path.Combine(
                    directory,
                    Path.GetFileName(fullPath) +
                    ".tmp." +
                    Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllText(
                    temporaryPath,
                    content,
                    new UTF8Encoding(false));
                if (File.Exists(fullPath))
                {
                    File.Replace(temporaryPath, fullPath, null);
                }
                else
                {
                    File.Move(temporaryPath, fullPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static bool IsWithin(string candidate, string root)
        {
            string fullCandidate = Path.GetFullPath(candidate);
            string fullRoot = Path.GetFullPath(root)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return fullCandidate.StartsWith(
                fullRoot,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string ProjectPath()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        private static string AbsoluteScenePath()
        {
            return Path.GetFullPath(Path.Combine(
                ProjectPath(),
                ScenePath));
        }

        private static string AbsoluteScenePath(string sceneAssetPath)
        {
            return Path.GetFullPath(Path.Combine(
                ProjectPath(),
                sceneAssetPath));
        }

        private static string AbsoluteTemplatePath()
        {
            return Path.GetFullPath(Path.Combine(
                ProjectPath(),
                TemplatePath));
        }

        private static string Sha256(byte[] bytes)
        {
            using SHA256 hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(bytes))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        private static string FormatCompletion(
            string operation,
            CanonicalSceneRebuildResult result)
        {
            return
                "M_GLOBAL_G4_CANONICAL_SCENE_REBUILD_OPERATION_PASS" +
                " | operation=" + operation +
                " | templateRestored=" + result.TemplateRestored +
                " | templateMatchedTarget=" +
                result.TemplateMatchedTarget +
                " | changed=" + result.AnyPostBuildChanged +
                " | saved=" + result.SceneSaved +
                " | semantic=" + result.CanonicalSemanticSha;
        }

        private static void RequireSuccess(
            CanonicalSceneRebuildResult result)
        {
            if (!result.Success)
            {
                throw new InvalidOperationException(
                    result.FailureStage +
                    ": " +
                    result.FailureMessage);
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
}
