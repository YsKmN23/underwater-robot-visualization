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
    public static class CanonicalModelPresentationParityVerifier
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string ReportArgument = "-g3PresentationReportPath";

        [Serializable]
        private sealed class VerificationReport
        {
            public string Status;
            public string Mode;
            public string SceneShaBefore;
            public string SceneShaAfter;
            public bool AuvChanged;
            public bool RovChanged;
            public bool UsvChanged;
            public bool AnyChanged;
            public bool SceneSaved;
            public bool SecondAuvChanged;
            public bool SecondRovChanged;
            public bool SecondUsvChanged;
            public bool SecondAnyChanged;
            public bool SecondSceneSaved;
            public bool SecondPassByteNoOp;
            public int AuvRemovedCount;
            public int RovRemovedCount;
            public int UsvObsoleteObjectCount;
            public int UsvObsoletePropertyCount;
            public int UsvManualObjectCount;
            public int UsvManualPropertyCount;
            public int GameObjectCount;
            public int ComponentCount;
            public int MissingReferenceCount;
            public string[] InvocationOrder;
        }

        [MenuItem("Tools/Underwater Demo/G3/Verify Canonical Model Presentation Parity")]
        public static void RunFromMenu()
        {
            RunBatch();
        }

        public static void RunBatch()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            string absoluteScenePath = AbsoluteScenePath();
            byte[] beforeBytes = File.ReadAllBytes(absoluteScenePath);
            string beforeSha = Sha256(beforeBytes);

            bool auvChanged =
                AuvModelPresentationParityInstaller
                    .InstallForCanonicalSceneRebuild();
            bool rovChanged =
                RovModelPresentationParityInstaller
                    .InstallForCanonicalSceneRebuild();
            bool usvChanged =
                UsvModelPresentationParityInstaller
                    .InstallForCanonicalSceneRebuild();
            CanonicalModelPresentationParityResult chain =
                CanonicalModelPresentationParityInstallerChain
                    .InstallCanonicalModelPresentationParity();

            Require(!auvChanged, "Authority AUV Installer was not a no-op.");
            Require(!rovChanged, "Authority ROV Installer was not a no-op.");
            Require(!usvChanged, "Authority USV Installer was not a no-op.");
            Require(chain.Success, "Authority presentation Chain failed: " +
                chain.FailureStage + " / " + chain.FailureMessage);
            Require(
                !chain.AuvChanged &&
                !chain.RovChanged &&
                !chain.UsvChanged &&
                !chain.AnyChanged &&
                !chain.SceneSaved,
                "Authority presentation Chain was not an exact no-op.");
            Require(chain.InvocationOrder.SequenceEqual(ExpectedInvocationOrder()),
                "Authority presentation Chain invocation order changed.");

            byte[] afterBytes = File.ReadAllBytes(absoluteScenePath);
            Require(beforeBytes.SequenceEqual(afterBytes),
                "Authority no-op changed formal Scene bytes.");
            Require(string.Equals(
                    beforeSha,
                    Sha256(afterBytes),
                    StringComparison.Ordinal),
                "Authority no-op changed formal Scene SHA.");
            Require(!SceneManager.GetActiveScene().isDirty,
                "Authority no-op left the formal Scene dirty.");

            VerificationReport report = BuildReport(
                "AUTHORITY_NO_OP",
                beforeSha,
                Sha256(afterBytes),
                chain);
            Require(report.AuvRemovedCount == 10,
                "Authority AUV removed count is not 10.");
            Require(report.RovRemovedCount == 1,
                "Authority ROV removed count is not 1.");
            Require(report.UsvObsoleteObjectCount == 23 &&
                    report.UsvObsoletePropertyCount == 0,
                "Authority USV obsolete override contract changed.");
            Require(report.UsvManualObjectCount == 4 &&
                    report.UsvManualPropertyCount == 7,
                "Authority USV manual override contract changed.");
            Require(report.GameObjectCount == 552,
                "Authority GameObject count changed from 552.");
            Require(report.ComponentCount == 1588,
                "Authority Component count changed from 1,588.");
            Require(report.MissingReferenceCount == 0,
                "Authority Scene contains a missing component/reference.");

            WriteRequestedReport(report);
            Debug.Log(
                "M_GLOBAL_G3_CANONICAL_MODEL_PRESENTATION_PARITY_VERIFY_PASS" +
                " | mode=AUTHORITY_NO_OP" +
                " | sceneSha=" + report.SceneShaAfter +
                " | auvRemoved=" + report.AuvRemovedCount +
                " | rovRemoved=" + report.RovRemovedCount +
                " | usv=23/4/7" +
                " | go=" + report.GameObjectCount +
                " | components=" + report.ComponentCount +
                " | missing=" + report.MissingReferenceCount);
        }

        public static void RunFreshBuilderBatch()
        {
            UnderwaterRobotSceneEditor.UnderwaterSceneBuilder.BuildFromCommandLine();
            RequireFormalScene();

            AuvPublicPoseN5SceneInstaller.InstallForCanonicalSceneRebuild();
            RovRootPoseN6BSceneInstaller.InstallForCanonicalSceneRebuild();
            RovThrusterVisualM1CSceneInstaller.InstallForCanonicalSceneRebuild();
            UsvPostBuildInstallerChainResult usvChain =
                UsvPostBuildInstallerChain.InstallCanonicalUsvPostBuildChain();
            Require(usvChain.Success,
                "Fresh USV post-build Chain failed: " +
                usvChain.FailureStage + " / " + usvChain.FailureMessage);

            string absoluteScenePath = AbsoluteScenePath();
            string beforeFirstSha =
                Sha256(File.ReadAllBytes(absoluteScenePath));
            CanonicalModelPresentationParityResult first =
                CanonicalModelPresentationParityInstallerChain
                    .InstallCanonicalModelPresentationParity();
            Require(first.Success,
                "Fresh first presentation Chain failed: " +
                first.FailureStage + " / " + first.FailureMessage);
            Require(
                first.AuvChanged &&
                first.RovChanged &&
                first.UsvChanged &&
                first.AnyChanged &&
                first.SceneSaved,
                "Fresh first presentation Chain did not report true/true/true.");
            Require(first.InvocationOrder.SequenceEqual(ExpectedInvocationOrder()),
                "Fresh first presentation Chain invocation order changed.");

            byte[] beforeSecondBytes = File.ReadAllBytes(absoluteScenePath);
            string beforeSecondSha = Sha256(beforeSecondBytes);
            CanonicalModelPresentationParityResult second =
                CanonicalModelPresentationParityInstallerChain
                    .InstallCanonicalModelPresentationParity();
            byte[] afterSecondBytes = File.ReadAllBytes(absoluteScenePath);
            string afterSecondSha = Sha256(afterSecondBytes);
            Require(second.Success,
                "Fresh second presentation Chain failed: " +
                second.FailureStage + " / " + second.FailureMessage);
            Require(
                !second.AuvChanged &&
                !second.RovChanged &&
                !second.UsvChanged &&
                !second.AnyChanged &&
                !second.SceneSaved,
                "Fresh second presentation Chain was not false/false/false.");
            Require(beforeSecondBytes.SequenceEqual(afterSecondBytes) &&
                    string.Equals(
                        beforeSecondSha,
                        afterSecondSha,
                        StringComparison.Ordinal),
                "Fresh second presentation Chain was not a byte no-op.");
            Require(!SceneManager.GetActiveScene().isDirty,
                "Fresh second presentation Chain left the Scene dirty.");

            VerificationReport report = BuildReport(
                "FRESH_FULL_SEQUENCE",
                beforeFirstSha,
                afterSecondSha,
                first);
            report.SecondAuvChanged = second.AuvChanged;
            report.SecondRovChanged = second.RovChanged;
            report.SecondUsvChanged = second.UsvChanged;
            report.SecondAnyChanged = second.AnyChanged;
            report.SecondSceneSaved = second.SceneSaved;
            report.SecondPassByteNoOp =
                beforeSecondBytes.SequenceEqual(afterSecondBytes);

            Require(report.AuvRemovedCount == 10,
                "Fresh AUV removed count is not 10.");
            Require(report.RovRemovedCount == 1,
                "Fresh ROV removed count is not 1.");
            Require(report.UsvObsoleteObjectCount == 23 &&
                    report.UsvObsoletePropertyCount == 0,
                "Fresh USV obsolete override contract changed.");
            Require(report.UsvManualObjectCount == 4 &&
                    report.UsvManualPropertyCount == 7,
                "Fresh USV manual override contract changed.");
            Require(report.GameObjectCount == 552,
                "Fresh GameObject count changed from 552.");
            Require(report.ComponentCount == 1588,
                "Fresh Component count changed from 1,588.");
            Require(report.MissingReferenceCount == 0,
                "Fresh Scene contains a missing component/reference.");
            RequireSourceAssetsStillContainFrozenObjects();

            WriteRequestedReport(report);
            Debug.Log(
                "M_GLOBAL_G3_CANONICAL_MODEL_PRESENTATION_PARITY_VERIFY_PASS" +
                " | mode=FRESH_FULL_SEQUENCE" +
                " | first=true/true/true" +
                " | second=false/false/false" +
                " | secondByteNoOp=True" +
                " | beforeFirst=" + beforeFirstSha +
                " | final=" + afterSecondSha +
                " | auvRemoved=" + report.AuvRemovedCount +
                " | rovRemoved=" + report.RovRemovedCount +
                " | usv=23/4/7" +
                " | go=" + report.GameObjectCount +
                " | components=" + report.ComponentCount +
                " | missing=" + report.MissingReferenceCount);
        }

        private static VerificationReport BuildReport(
            string mode,
            string beforeSha,
            string afterSha,
            CanonicalModelPresentationParityResult chain)
        {
            AuvModelPresentationParityInstaller.RemovalAudit auv =
                AuvModelPresentationParityInstaller.AuditCanonicalScene();
            RovModelPresentationParityInstaller.RemovalAudit rov =
                RovModelPresentationParityInstaller.AuditCanonicalScene();
            UsvModelPresentationParityInstaller.ContractAudit usv =
                UsvModelPresentationParityInstaller.AuditCanonicalScene();
            CountScene(
                out int gameObjects,
                out int components,
                out int missing);
            return new VerificationReport
            {
                Status =
                    "M_GLOBAL_G3_AUV_ROV_MODEL_PRESENTATION_PARITY_VERIFY_PASS",
                Mode = mode,
                SceneShaBefore = beforeSha,
                SceneShaAfter = afterSha,
                AuvChanged = chain.AuvChanged,
                RovChanged = chain.RovChanged,
                UsvChanged = chain.UsvChanged,
                AnyChanged = chain.AnyChanged,
                SceneSaved = chain.SceneSaved,
                AuvRemovedCount = auv.RemovedCount,
                RovRemovedCount = rov.RemovedCount,
                UsvObsoleteObjectCount = usv.ObsoleteObjectCount,
                UsvObsoletePropertyCount =
                    usv.ObsoleteOverridePropertyCount,
                UsvManualObjectCount = usv.ManualObjectCount,
                UsvManualPropertyCount =
                    usv.ManualOverridePropertyCount,
                GameObjectCount = gameObjects,
                ComponentCount = components,
                MissingReferenceCount = missing,
                InvocationOrder = chain.InvocationOrder
            };
        }

        private static void RequireSourceAssetsStillContainFrozenObjects()
        {
            GameObject auvAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Models/AUV/AUV_FineModel_V1.fbx");
            GameObject rovAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Models/ROV/ROV_FineModel_V1.fbx");
            Require(auvAsset != null && rovAsset != null,
                "AUV/ROV source FBX asset could not be loaded.");
            var auvIds = new HashSet<long>();
            foreach (Transform transform in
                     auvAsset.GetComponentsInChildren<Transform>(true))
            {
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        transform.gameObject,
                        out string guid,
                        out long localId) &&
                    string.Equals(
                        guid,
                        "01f4e92113033e24a83fefd4d213c91e",
                        StringComparison.Ordinal))
                {
                    auvIds.Add(localId);
                }
            }

            Require(
                AuvModelPresentationParityInstaller
                    .ExpectedSourceGameObjectIds()
                    .All(auvIds.Contains),
                "The AUV source FBX no longer contains all 10 frozen objects.");
            bool rovPresent = rovAsset
                .GetComponentsInChildren<Transform>(true)
                .Select(transform => transform.gameObject)
                .Any(value =>
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        value,
                        out string guid,
                        out long localId) &&
                    string.Equals(
                        guid,
                        "81496ffad80dd2d43aeec8986511d0a9",
                        StringComparison.Ordinal) &&
                    localId ==
                    RovModelPresentationParityInstaller
                        .ExpectedSourceGameObjectId());
            Require(rovPresent,
                "The ROV source FBX no longer contains the frozen object.");
        }

        private static void CountScene(
            out int gameObjectCount,
            out int componentCount,
            out int missingReferenceCount)
        {
            Scene scene = RequireFormalScene();
            GameObject[] gameObjects = scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<Transform>(true))
                .Select(transform => transform.gameObject)
                .Distinct()
                .ToArray();
            int components = 0;
            int missing = 0;
            foreach (GameObject gameObject in gameObjects)
            {
                Component[] values = gameObject.GetComponents<Component>();
                components += values.Length;
                missing += values.Count(value => value == null);
            }

            gameObjectCount = gameObjects.Length;
            componentCount = components;
            missingReferenceCount = missing;
        }

        private static Scene RequireFormalScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded,
                "G3 verifier formal Scene is not loaded.");
            Require(string.Equals(scene.path, ScenePath, StringComparison.Ordinal),
                "G3 verifier may only run on the formal Scene.");
            Require(SceneManager.sceneCount == 1,
                "G3 verifier requires exactly one loaded Scene.");
            Require(!scene.isDirty,
                "G3 verifier refuses a dirty formal Scene.");
            return scene;
        }

        private static string[] ExpectedInvocationOrder()
        {
            return new[]
            {
                "AuvModelPresentationParityInstaller",
                "RovModelPresentationParityInstaller",
                "UsvModelPresentationParityInstaller"
            };
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

        private static void WriteRequestedReport(VerificationReport report)
        {
            string requestedPath = ArgumentValue(ReportArgument);
            if (string.IsNullOrEmpty(requestedPath))
            {
                return;
            }

            string fullPath = Path.GetFullPath(requestedPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(
                fullPath,
                JsonUtility.ToJson(report, true) + Environment.NewLine);
        }

        private static string ArgumentValue(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index + 1 < arguments.Length; index++)
            {
                if (string.Equals(
                        arguments[index],
                        name,
                        StringComparison.Ordinal))
                {
                    return arguments[index + 1];
                }
            }
            return string.Empty;
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
