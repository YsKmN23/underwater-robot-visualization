using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    public static class CanonicalSceneRebuildVerifier
    {
        private const string ScenePath =
            CanonicalSceneIdentityContract.TargetScenePath;

        [Serializable]
        private sealed class Check
        {
            public string Name;
            public bool Passed;
            public string Detail;
        }

        [Serializable]
        private sealed class VerificationReport
        {
            public string SchemaVersion;
            public string Status;
            public string SceneShaBefore;
            public string SceneShaAfter;
            public string CanonicalSemanticSha;
            public int GameObjectCount;
            public int ComponentCount;
            public int MissingReferenceCount;
            public bool FirstReapplyByteNoOp;
            public bool SecondReapplyByteNoOp;
            public int ProjectInternalReportCountBefore;
            public int ProjectInternalReportCountAfter;
            public Check[] Checks;
        }

        public static void RunBatch()
        {
            var checks = new List<Check>();
            CanonicalSceneIdentityManifestSnapshot manifest =
                CanonicalSceneIdentityContract
                    .LoadApprovedManifestSnapshot();
            VerifyTypeAndEntryContract(checks);
            VerifySourceBoundary(checks);

            Scene scene =
                EditorSceneManager.OpenScene(
                    ScenePath,
                    OpenSceneMode.Single);
            Require(scene.IsValid() && scene.isLoaded && !scene.isDirty,
                "The G4 verifier could not load the clean formal Scene.");
            string scenePath = AbsoluteScenePath();
            byte[] before = File.ReadAllBytes(scenePath);
            string beforeSha = Sha256(before);
            Require(
                before.Length == manifest.SceneByteSize &&
                string.Equals(
                    beforeSha,
                    manifest.SceneUnitySha256,
                    StringComparison.Ordinal),
                "Formal Scene byte identity differs from the manifest.");
            int internalReportsBefore = CountProjectInternalReports();

            CanonicalSceneRebuildResult first =
                CanonicalSceneRebuildOrchestrator.ReapplyPostBuild();
            VerifyNoOp(first, "first", manifest);
            byte[] afterFirst = File.ReadAllBytes(scenePath);
            Require(before.SequenceEqual(afterFirst),
                "First authority Reapply was not a byte no-op.");

            CanonicalSceneRebuildResult second =
                CanonicalSceneRebuildOrchestrator.ReapplyPostBuild();
            VerifyNoOp(second, "second", manifest);
            byte[] afterSecond = File.ReadAllBytes(scenePath);
            Require(before.SequenceEqual(afterSecond),
                "Second authority Reapply was not a byte no-op.");
            Require(!SceneManager.GetActiveScene().isDirty,
                "Authority Reapply left the Scene dirty.");
            Add(
                checks,
                "Authority consecutive Reapply",
                "Two runs returned five false changed values, saved=false, and preserved exact bytes.");

            CanonicalSemanticSignature signature =
                CanonicalSceneRebuildOrchestrator
                    .BuildCanonicalSemanticSignature();
            Require(string.Equals(
                    signature.CanonicalSemanticSha,
                    manifest.SemanticSha256,
                    StringComparison.Ordinal),
                "Canonical semantic SHA changed.");
            Require(
                    signature.GameObjectCount ==
                        manifest.GameObjectCount &&
                    signature.ComponentCount ==
                        manifest.ComponentCount &&
                    signature.MissingReferenceCount ==
                        manifest.MissingReferenceCount,
                "Canonical GO/Component/Missing counts changed.");
            Require(
                    signature.Hierarchy.Length ==
                        manifest.GameObjectCount &&
                    signature.NormalizedObjectCount ==
                        manifest.GameObjectCount &&
                    !string.IsNullOrEmpty(signature.CanonicalPayload) &&
                    signature.SerializedProperties.Length > 0 &&
                    signature.References.Length > 0 &&
                    signature.AssetIdentities.Length > 0 &&
                    signature.BusinessRecords.Length == 23 &&
                    signature.BusinessRecords.Count(value =>
                        value.StartsWith("E3B_", StringComparison.Ordinal)) == 3 &&
                    signature.BusinessRecords.Count(value =>
                        value.StartsWith("E3D_", StringComparison.Ordinal)) == 3,
                "Canonical semantic payload is incomplete.");
            Add(
                checks,
                "Complete semantic payload",
                "Hierarchy, components, transforms, serialized properties, references, asset identities, business records, and canonical payload are present.");

            VerifyBusinessContracts(checks);
            VerifyE3ABusinessRecords(signature.BusinessRecords, checks);
            VerifyE3BBusinessRecords(signature.BusinessRecords, checks);
            VerifyE3DBusinessRecords(signature.BusinessRecords, checks);
            VerifyDirtyPrecondition(before, beforeSha, checks);

            int internalReportsAfter = CountProjectInternalReports();
            Require(internalReportsAfter == internalReportsBefore,
                "G4 created a project-internal JSON report without authorization.");
            Add(
                checks,
                "Report boundary",
                "No project-internal JSON report was created.");

            var report = new VerificationReport
            {
                SchemaVersion = "1.0",
                Status =
                    "M_GLOBAL_G4_CANONICAL_SCENE_REBUILD_VERIFIER_PASS",
                SceneShaBefore = beforeSha,
                SceneShaAfter =
                    Sha256(File.ReadAllBytes(scenePath)),
                CanonicalSemanticSha =
                    signature.CanonicalSemanticSha,
                GameObjectCount = signature.GameObjectCount,
                ComponentCount = signature.ComponentCount,
                MissingReferenceCount =
                    signature.MissingReferenceCount,
                FirstReapplyByteNoOp =
                    before.SequenceEqual(afterFirst),
                SecondReapplyByteNoOp =
                    before.SequenceEqual(afterSecond),
                ProjectInternalReportCountBefore =
                    internalReportsBefore,
                ProjectInternalReportCountAfter =
                    internalReportsAfter,
                Checks = checks.ToArray()
            };
            WriteRequestedReport(report);
            Debug.Log(
                "M_GLOBAL_G4_CANONICAL_SCENE_REBUILD_VERIFIER_PASS" +
                " | scene=" + report.SceneShaAfter +
                " | semantic=" + report.CanonicalSemanticSha +
                " | counts=" + report.GameObjectCount +
                "/" + report.ComponentCount +
                "/" + report.MissingReferenceCount);
        }

        private static void VerifyTypeAndEntryContract(
            ICollection<Check> checks)
        {
            Type resultType = typeof(CanonicalSceneRebuildResult);
            string[] fields =
            {
                "Success",
                "BuilderExecuted",
                "EnvironmentChanged",
                "EnvironmentStageCount",
                "E2DPostVehicleLayoutChanged",
                "AuvChanged",
                "RovChanged",
                "RovM1CChanged",
                "UsvChanged",
                "PresentationChanged",
                "E3BRovContactChanged",
                "AnyPostBuildChanged",
                "SceneSaved",
                "SceneShaBefore",
                "SceneShaAfter",
                "CanonicalSemanticSha",
                "FailureStage",
                "FailureMessage",
                "InvocationOrder"
            };
            Require(fields.All(name =>
                    resultType.GetField(name) != null),
                "CanonicalSceneRebuildResult fields are incomplete.");

            Type orchestrator =
                typeof(CanonicalSceneRebuildOrchestrator);
            RequireMethod(
                orchestrator,
                "FullRebuild",
                typeof(CanonicalSceneRebuildResult));
            RequireMethod(
                orchestrator,
                "ReapplyPostBuild",
                typeof(CanonicalSceneRebuildResult));
            RequireMethod(
                orchestrator,
                "RunFullRebuildBatch",
                typeof(void));
            RequireMethod(
                orchestrator,
                "RunReapplyPostBuildBatch",
                typeof(void));
            RequireMethod(
                orchestrator,
                "RunFullRebuildFromMenu",
                typeof(void));
            RequireMethod(
                orchestrator,
                "RunReapplyPostBuildFromMenu",
                typeof(void));
            Add(
                checks,
                "Types and public entries",
                "Result fields and all six exact public static entries exist.");
        }

        private static void VerifySourceBoundary(
            ICollection<Check> checks)
        {
            string installation = Path.Combine(
                ProjectPath(),
                "Assets/Editor/Visualization/Installation");
            string orchestrator = File.ReadAllText(Path.Combine(
                installation,
                "CanonicalSceneRebuildOrchestrator.cs"));
            string pipeline = File.ReadAllText(Path.Combine(
                installation,
                "CanonicalScenePostBuildPipeline.cs"));

            Require(
                Count(
                    orchestrator,
                    "CanonicalScenePostBuildPipeline.Execute(") == 1,
                "Orchestrator does not use exactly one shared pipeline call.");
            Require(Count(orchestrator,
                        "EnvE3AEnvironmentInstallerChain.Execute(") == 1,
                "Orchestrator does not use exactly one shared E3A " +
                "environment chain call.");
            int environmentCall = orchestrator.IndexOf(
                "EnvE3AEnvironmentInstallerChain.Execute(",
                StringComparison.Ordinal);
            int vehicleCall = orchestrator.IndexOf(
                "CanonicalScenePostBuildPipeline.Execute(",
                StringComparison.Ordinal);
            Require(environmentCall >= 0 && vehicleCall > environmentCall,
                "E3A environment no-op stage is not before the vehicle " +
                "post-build pipeline.");
            Require(Count(orchestrator,
                        ".ExecutePostVehicleLayout(") == 1,
                "Orchestrator does not use exactly one E2D post-vehicle " +
                "layout call.");
            int postVehicleCall = orchestrator.IndexOf(
                ".ExecutePostVehicleLayout(",
                StringComparison.Ordinal);
            Require(Count(orchestrator,
                        "EnvE3BRovContactSceneInstaller.Execute(") == 1,
                "Orchestrator does not use exactly one shared E3B ROV " +
                "contact installer call.");
            int e3bCall = orchestrator.IndexOf(
                "EnvE3BRovContactSceneInstaller.Execute(",
                StringComparison.Ordinal);
            Require(e3bCall > vehicleCall && postVehicleCall > e3bCall,
                "E2D post-vehicle layout is not after the vehicle pipeline.");
            string[] installerTypes =
                CanonicalScenePostBuildPipeline.CopyInvocationOrder();
            Require(installerTypes.All(name =>
                    Regex.Matches(
                        orchestrator,
                        Regex.Escape(name) +
                        @"\s*\.\s*(InstallForCanonicalSceneRebuild|" +
                        @"InstallCanonicalUsvPostBuildChain|" +
                        @"InstallCanonicalModelPresentationParity)\s*\(",
                        RegexOptions.CultureInvariant).Count == 0),
                "Orchestrator still directly owns an installer call.");

            int previous = -1;
            foreach (string name in installerTypes)
            {
                Require(
                    Count(
                        pipeline,
                        "\"" + name + "\"") == 1 &&
                    Regex.Matches(
                        pipeline,
                        Regex.Escape(name) +
                        @"\s*\.\s*(InstallForCanonicalSceneRebuild|" +
                        @"InstallCanonicalUsvPostBuildChain|" +
                        @"InstallCanonicalModelPresentationParity)\s*\(",
                        RegexOptions.CultureInvariant).Count == 1,
                    "Pipeline registration/call count changed: " + name);
                int current = pipeline.IndexOf(
                    name,
                    previous + 1,
                    StringComparison.Ordinal);
                Require(
                    current > previous,
                    "Pipeline installer order changed.");
                previous = current;
            }

            Require(
                    !pipeline.Contains("System.Reflection") &&
                    !pipeline.Contains("dynamic ") &&
                    !pipeline.Contains("ExecuteMenuItem") &&
                    !pipeline.Contains("AssetDatabase.FindAssets") &&
                    !pipeline.Contains("GetTypes(") &&
                    !pipeline.Contains("Parallel.") &&
                    !pipeline.Contains("Task.Run"),
                "Pipeline crossed its synchronous direct-source boundary.");
            Require(Count(orchestrator, "return Execute(true);") == 1 &&
                    Count(orchestrator, "return Execute(false);") == 1,
                "Full and Reapply do not share one core implementation.");
            int reapply = orchestrator.IndexOf(
                "public static CanonicalSceneRebuildResult ReapplyPostBuild()",
                StringComparison.Ordinal);
            int execute = orchestrator.IndexOf(
                "private static CanonicalSceneRebuildResult Execute",
                StringComparison.Ordinal);
            Require(reapply >= 0 && execute > reapply,
                "Shared Execute core is missing.");
            Add(
                checks,
                "Shared pipeline source boundary",
                "Pipeline uniquely owns the fixed synchronous direct order; Orchestrator retains one shared Execute core.");
        }

        private static void VerifyNoOp(
            CanonicalSceneRebuildResult result,
            string label,
            CanonicalSceneIdentityManifestSnapshot manifest)
        {
            Require(result.Success,
                label + " Reapply failed: " +
                result.FailureStage + " / " +
                result.FailureMessage);
            Require(!result.BuilderExecuted &&
                    !result.EnvironmentChanged &&
                    result.EnvironmentStageCount == 5 &&
                    !result.E2DPostVehicleLayoutChanged &&
                    !result.AuvChanged &&
                    !result.RovChanged &&
                    !result.RovM1CChanged &&
                    !result.UsvChanged &&
                    !result.PresentationChanged &&
                    !result.E3BRovContactChanged &&
                    !result.AnyPostBuildChanged &&
                    !result.SceneSaved,
                label + " Reapply was not a complete no-op.");
            Require(string.Equals(
                        result.SceneShaBefore,
                        manifest.SceneUnitySha256,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        result.SceneShaAfter,
                        manifest.SceneUnitySha256,
                        StringComparison.Ordinal),
                label + " Reapply changed Scene SHA.");
            Require(string.Equals(
                    result.CanonicalSemanticSha,
                    manifest.SemanticSha256,
                    StringComparison.Ordinal),
                label + " Reapply changed semantic SHA.");
            Require(result.InvocationOrder.SequenceEqual(
                    new[] { "EnvE3AEnvironmentInstallerChain" }
                        .Concat(CanonicalScenePostBuildPipeline
                            .CopyInvocationOrder())
                        .Concat(new[] {
                            "EnvE3BRovContactSceneInstaller" })
                        .Concat(new[] { "EnvE2DPostVehicleLayout" })),
                label + " Reapply invocation order changed.");
        }

        private static void VerifyE3ABusinessRecords(
            string[] records,
            ICollection<Check> checks)
        {
            string[] prefixes =
            {
                "E3A_CONFIGURATION_SHA256=",
                "E3A_CONTACT_MESH_SHA256=",
                "E3A_FAR_MESH_SHA256=",
                "E3A_CONTACT_BOUNDS=",
                "E3A_CONTACT_COUNTS=",
                "E3A_FAR_COUNTS=",
                "E3A_LEGACY_ROOT_COUNT=0",
                "E3A_FAR_COLLIDER_COUNT=0",
                "E3A_NEAR_SHARED_MESH_IDENTITY=true",
                "E3A_MATERIAL_AUTHORITY=Demo_Seabed|SHARED_REFERENCE:true",
                "E3A_ENVIRONMENT_INVOCATION_ORDER="
            };
            Require(prefixes.All(prefix => records.Any(record =>
                    record.StartsWith(prefix, StringComparison.Ordinal))),
                "Canonical semantic payload is missing E3A business " +
                "records.");
            Require(records.All(record =>
                    record.IndexOf("CANDIDATE",
                        StringComparison.OrdinalIgnoreCase) < 0 &&
                    record.IndexOf("APPROVAL",
                        StringComparison.OrdinalIgnoreCase) < 0 &&
                    record.IndexOf("COMMIT",
                        StringComparison.OrdinalIgnoreCase) < 0),
                "Canonical semantic payload contains a forbidden E3A " +
                "identity self-reference.");
            Add(checks,
                "E3A semantic business records",
                "Configuration/contact/far SHA, bounds/counts, legacy, " +
                "Collider, shared mesh/material and environment order are " +
                "present without Candidate/approval/commit identity.");
        }

        private static void VerifyE3BBusinessRecords(
            string[] records,
            ICollection<Check> checks)
        {
            string[] e3b = records.Where(record =>
                    record.StartsWith("E3B_", StringComparison.Ordinal))
                .ToArray();
            Require(e3b.Length == 3 &&
                    e3b[0] ==
                    "E3B_ROV_CONTACT_BINDING=DRIVER:/ROV_PublicPoseDriver" +
                    "|TARGET:/ROV_Box_Seabed" +
                    "|SAMPLER:/ROV_PublicPoseDriver" +
                    "|CONSTRAINT:/ROV_PublicPoseDriver" +
                    "|TERRAIN_COLLIDER:/Seabed" &&
                    e3b[1].StartsWith(
                        "E3B_ROV_CONTACT_PROFILE=LEFT_FRONT:",
                        StringComparison.Ordinal) &&
                    e3b[1].EndsWith("|PROBE_COUNT:4",
                        StringComparison.Ordinal) &&
                    e3b[2] ==
                    "E3B_ROV_CONTACT_POLICY=TRANSLATION:upward-y-only" +
                    "|ROTATION:preserve-input" +
                    "|DECISION:apply-or-hold-current" +
                    "|PARTIAL_PROBE_FALLBACK:false" +
                    "|WRITER:VehiclePoseDriver",
                "Canonical semantic payload has an invalid E3B business " +
                "record contract.");
            Require(e3b.All(record =>
                    record.IndexOf("CANDIDATE",
                        StringComparison.OrdinalIgnoreCase) < 0 &&
                    record.IndexOf("APPROVAL",
                        StringComparison.OrdinalIgnoreCase) < 0 &&
                    record.IndexOf("COMMIT",
                        StringComparison.OrdinalIgnoreCase) < 0),
                "Canonical semantic payload contains a forbidden E3B " +
                "identity self-reference.");
            Add(checks,
                "E3B semantic business records",
                "Binding, approved profile and upward-only ownership policy " +
                "are present in fixed order without identity self-reference.");
        }

        private static void VerifyE3DBusinessRecords(
            string[] records,
            ICollection<Check> checks)
        {
            string[] e3d = records.Where(record =>
                    record.StartsWith("E3D_", StringComparison.Ordinal))
                .ToArray();
            Require(e3d.Length == 3 &&
                    e3d[0] ==
                    "E3D_AUV_TERRAIN_BINDING=DRIVER:/AUV_PublicPoseDriver" +
                    "|TARGET:/AUV_Yellow_Underwater" +
                    "|SAMPLER:/AUV_PublicPoseDriver" +
                    "|CONSTRAINT:/AUV_PublicPoseDriver" +
                    "|TERRAIN_COLLIDER:/Seabed" +
                    "|WATER:/Water_Surface" &&
                    e3d[1].StartsWith(
                        "E3D_AUV_TERRAIN_PROFILE=PROBES:7" +
                        "|HULL_CORNERS:8" +
                        "|MINIMUM_CLEARANCE:0.18" +
                        "|MINIMUM_SUBMERGENCE:0.18" +
                        "|MAXIMUM_CORRECTION:0.5",
                        StringComparison.Ordinal) &&
                    e3d[1].EndsWith(
                        "|TOLERANCE:0.002|SEGMENT_SPACING:0.5" +
                        "|MAXIMUM_CLIMB:45|MAXIMUM_DESCENT:45",
                        StringComparison.Ordinal) &&
                    e3d[2] ==
                    "E3D_AUV_TERRAIN_POLICY=TRANSLATION:upward-y-only" +
                    "|ROTATION:preserve-input" +
                    "|DECISION:apply-or-hold-current" +
                    "|WATER_VIOLATION:reject-or-hold-current" +
                    "|WATER_CORRECTION:none" +
                    "|PURE_VERTICAL:reject" +
                    "|ROUTE_VALIDATION:shared-evaluator" +
                    "|WRITER:VehiclePoseDriver",
                "Canonical semantic payload has an invalid E3D business " +
                "record contract.");
            Require(e3d.All(record =>
                    record.IndexOf("CANDIDATE",
                        StringComparison.OrdinalIgnoreCase) < 0 &&
                    record.IndexOf("APPROVAL",
                        StringComparison.OrdinalIgnoreCase) < 0 &&
                    record.IndexOf("COMMIT",
                        StringComparison.OrdinalIgnoreCase) < 0),
                "Canonical semantic payload contains a forbidden E3D " +
                "identity self-reference.");
            Add(checks,
                "E3D semantic business records",
                "AUV binding, seven-probe profile and shared-evaluator " +
                "ownership policy are present without identity self-reference.");
        }

        private static void VerifyBusinessContracts(
            ICollection<Check> checks)
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
            Require(auv.RemovedCount == 10 &&
                    rov.RemovedCount == 1 &&
                    usv.ObsoleteObjectCount == 23 &&
                    usv.ObsoleteOverridePropertyCount == 0 &&
                    usv.ManualObjectCount == 4 &&
                    usv.ManualOverridePropertyCount == 7,
                "The 23/4/11 business contract changed.");
            Add(
                checks,
                "23/4/11 presentation contract",
                "USV obsolete=23/0 properties, manual=4/7 properties, AUV removed=10, ROV removed=1.");
        }

        private static void VerifyDirtyPrecondition(
            byte[] authorityBytes,
            string authoritySha,
            ICollection<Check> checks)
        {
            Scene scene = SceneManager.GetActiveScene();
            var temporary = new GameObject(
                "G4_Dirty_Precondition_Probe");
            SceneManager.MoveGameObjectToScene(temporary, scene);
            EditorSceneManager.MarkSceneDirty(scene);
            CanonicalSceneRebuildResult result =
                CanonicalSceneRebuildOrchestrator
                    .ReapplyPostBuild();
            Require(!result.Success &&
                    result.FailureStage == "Precondition" &&
                    !result.BuilderExecuted &&
                    !result.SceneSaved,
                "Dirty precondition was not rejected before execution.");
            Require(Sha256(File.ReadAllBytes(AbsoluteScenePath())) ==
                    authoritySha,
                "Dirty precondition changed formal Scene bytes.");
            UnityEngine.Object.DestroyImmediate(temporary);
            EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            Require(authorityBytes.SequenceEqual(
                    File.ReadAllBytes(AbsoluteScenePath())) &&
                    !SceneManager.GetActiveScene().isDirty,
                "Dirty precondition cleanup did not restore clean authority.");
            Add(
                checks,
                "Precondition atomicity",
                "A dirty loaded Scene is rejected as Precondition with no disk write.");
        }

        private static void RequireMethod(
            Type type,
            string name,
            Type returnType)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Public | BindingFlags.Static);
            Require(method != null &&
                    method.ReturnType == returnType &&
                    method.GetParameters().Length == 0,
                "Public entry signature changed: " + name);
        }

        private static int CountProjectInternalReports()
        {
            return Directory.GetFiles(
                    ProjectPath(),
                    "*.json",
                    SearchOption.AllDirectories)
                .Count(path =>
                    !IsGeneratedUnityDirectory(path) &&
                    (Path.GetFileName(path).IndexOf(
                         "canonical",
                         StringComparison.OrdinalIgnoreCase) >= 0 ||
                     Path.GetFileName(path).IndexOf(
                         "g4",
                         StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static bool IsGeneratedUnityDirectory(string path)
        {
            string normalized = path.Replace('\\', '/');
            return normalized.Contains("/Library/") ||
                   normalized.Contains("/Temp/") ||
                   normalized.Contains("/Logs/") ||
                   normalized.Contains("/obj/");
        }

        private static void WriteRequestedReport(
            VerificationReport report)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            string path = string.Empty;
            for (int index = 0;
                 index + 1 < arguments.Length;
                 index++)
            {
                if (string.Equals(
                        arguments[index],
                        "-g4VerifierReportPath",
                        StringComparison.Ordinal))
                {
                    path = arguments[index + 1];
                    break;
                }
            }

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            string fullPath = Path.GetFullPath(path);
            Require(!fullPath.StartsWith(
                    ProjectPath() +
                    Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase),
                "Verifier report path must be outside the project.");
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(
                fullPath,
                JsonUtility.ToJson(report, true) +
                Environment.NewLine,
                new UTF8Encoding(false));
        }

        private static int Count(string value, string token)
        {
            int count = 0;
            int index = 0;
            while ((index = value.IndexOf(
                       token,
                       index,
                       StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += token.Length;
            }

            return count;
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

        private static string Sha256(byte[] bytes)
        {
            using SHA256 hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(bytes))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        private static void Add(
            ICollection<Check> checks,
            string name,
            string detail)
        {
            checks.Add(new Check
            {
                Name = name,
                Passed = true,
                Detail = detail
            });
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
