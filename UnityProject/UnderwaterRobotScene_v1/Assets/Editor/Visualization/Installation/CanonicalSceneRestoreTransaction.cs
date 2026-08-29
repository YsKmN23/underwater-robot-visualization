using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    internal sealed class CanonicalSceneRestoreTransaction
    {
        private readonly CanonicalSceneIdentitySnapshot identity;
        private readonly byte[] preTargetBytes;
        private readonly string preTargetSha;
        private readonly string preTargetMetaSha;
        private readonly string temporaryPath;
        private readonly string backupPath;
        private readonly string rollbackDisplacedPath;

        private bool replacementCompleted;
        private bool committed;

        internal CanonicalSceneRestoreTransaction(
            CanonicalSceneIdentitySnapshot identitySnapshot)
        {
            identity = identitySnapshot ??
                throw new ArgumentNullException(nameof(identitySnapshot));
            preTargetBytes =
                File.ReadAllBytes(identity.TargetPath);
            preTargetSha = Sha256(preTargetBytes);
            preTargetMetaSha =
                CanonicalSceneIdentityContract.Sha256File(
                    identity.TargetMetaPath);

            string nonce = Guid.NewGuid().ToString("N");
            temporaryPath =
                identity.TargetPath +
                ".v1restore." +
                nonce +
                ".tmp";
            backupPath =
                identity.TargetPath +
                ".v1restore." +
                nonce +
                ".backup";
            rollbackDisplacedPath =
                identity.TargetPath +
                ".v1restore." +
                nonce +
                ".rollback-displaced";

            Stage = "Preflight";
        }

        internal string Stage { get; private set; }

        internal bool InitialPreflightPassed { get; set; }

        internal bool PreWritePreflightPassed { get; private set; }

        internal bool TargetWriteAttempted { get; private set; }

        internal bool TargetPartiallyOverwritten { get; private set; }

        internal bool TemplateRestored => replacementCompleted;

        internal bool RollbackAttempted { get; private set; }

        internal bool RollbackSucceeded { get; private set; }

        internal bool SceneSaved => false;

        internal bool NewSceneExecuted => false;

        internal string ParkingScenePath =>
            CanonicalSceneIdentityContract.TemplateScenePath;

        internal string PreTargetSha => preTargetSha;

        internal void Begin()
        {
            try
            {
                Stage = "PreWritePreflight";
                CanonicalSceneIdentityContract
                    .ValidatePreWritePreflight(identity);
                PreWritePreflightPassed = true;

                Stage = "ReleaseTarget";
                Scene template = EditorSceneManager.OpenScene(
                    CanonicalSceneIdentityContract.TemplateScenePath,
                    OpenSceneMode.Single);
                Require(
                    template.IsValid() &&
                    template.isLoaded &&
                    !template.isDirty,
                    "The verified canonical template could not be " +
                    "opened cleanly as the parking Scene.");
                CanonicalSceneIdentityContract
                    .ValidateProtectedIdentities(
                        identity,
                        false);

                Stage = "PrepareAtomicReplacement";
                byte[] approvedBytes =
                    identity.CopyApprovedTemplateBytes();
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.WriteThrough))
                {
                    stream.Write(
                        approvedBytes,
                        0,
                        approvedBytes.Length);
                    stream.Flush(true);
                }

                Require(
                    string.Equals(
                        CanonicalSceneIdentityContract.Sha256File(
                            temporaryPath),
                        CanonicalSceneIdentityContract
                            .ApprovedTemplateUnitySha,
                        StringComparison.Ordinal),
                    "The same-directory replacement file does not " +
                    "match the approved template SHA.");

                Stage = "ReplaceTarget";
                TargetWriteAttempted = true;
                File.Replace(
                    temporaryPath,
                    identity.TargetPath,
                    backupPath,
                    true);
                replacementCompleted = true;
                TargetPartiallyOverwritten = false;

                Stage = "ImportTarget";
                AssetDatabase.ImportAsset(
                    CanonicalSceneIdentityContract.TargetScenePath,
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);

                Stage = "OpenTarget";
                Scene formal = EditorSceneManager.OpenScene(
                    CanonicalSceneIdentityContract.TargetScenePath,
                    OpenSceneMode.Single);
                Require(
                    formal.IsValid() &&
                    formal.isLoaded &&
                    !formal.isDirty,
                    "The atomically restored formal target could not " +
                    "be opened cleanly.");

                Stage = "PostWriteIdentity";
                CanonicalSceneIdentityContract
                    .ValidateProtectedIdentities(
                        identity,
                        true);
                Require(
                    string.Equals(
                        CanonicalSceneIdentityContract.Sha256File(
                            identity.TargetMetaPath),
                        preTargetMetaSha,
                        StringComparison.Ordinal),
                    "The formal target meta changed during restore.");
            }
            catch
            {
                if (!replacementCompleted)
                {
                    CleanupTransientFiles();
                }

                throw;
            }
        }

        internal void Commit()
        {
            Require(
                replacementCompleted,
                "Cannot commit before the target replacement.");
            Stage = "Commit";
            CanonicalSceneIdentityContract
                .ValidateProtectedIdentities(
                    identity,
                    true);
            DeleteIfExists(backupPath);
            DeleteIfExists(temporaryPath);
            DeleteIfExists(rollbackDisplacedPath);
            committed = true;
        }

        internal void Rollback()
        {
            if (committed || !replacementCompleted)
            {
                CleanupTransientFiles();
                return;
            }

            RollbackAttempted = true;
            Stage = "Rollback";
            try
            {
                Scene template = EditorSceneManager.OpenScene(
                    CanonicalSceneIdentityContract.TemplateScenePath,
                    OpenSceneMode.Single);
                Require(
                    template.IsValid() &&
                    template.isLoaded &&
                    !template.isDirty,
                    "Could not release the failed formal target using " +
                    "the verified template Scene.");

                DeleteIfExists(rollbackDisplacedPath);
                File.Replace(
                    backupPath,
                    identity.TargetPath,
                    rollbackDisplacedPath,
                    true);

                AssetDatabase.ImportAsset(
                    CanonicalSceneIdentityContract.TargetScenePath,
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);

                Require(
                    string.Equals(
                        CanonicalSceneIdentityContract.Sha256File(
                            identity.TargetPath),
                        preTargetSha,
                        StringComparison.Ordinal),
                    "Rollback did not restore the exact pre-target SHA.");
                Require(
                    File.ReadAllBytes(identity.TargetPath)
                        .SequenceEqual(preTargetBytes),
                    "Rollback did not restore the exact pre-target bytes.");
                Require(
                    string.Equals(
                        CanonicalSceneIdentityContract.Sha256File(
                            identity.TargetMetaPath),
                        preTargetMetaSha,
                        StringComparison.Ordinal),
                    "Rollback changed the formal target meta.");
                CanonicalSceneIdentityContract
                    .ValidateProtectedIdentities(
                        identity,
                        false);

                RestoreLoadedScenes();
                DeleteIfExists(rollbackDisplacedPath);
                DeleteIfExists(temporaryPath);
                RollbackSucceeded = true;
            }
            finally
            {
                if (RollbackSucceeded)
                {
                    DeleteIfExists(backupPath);
                }
            }
        }

        internal void CleanupAfterFailureWithoutWrite()
        {
            if (!replacementCompleted)
            {
                CleanupTransientFiles();
            }
        }

        private void RestoreLoadedScenes()
        {
            string[] paths = identity.LoadedScenePaths
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (paths.Length == 0)
            {
                return;
            }

            Scene first = EditorSceneManager.OpenScene(
                paths[0],
                OpenSceneMode.Single);
            Require(
                first.IsValid() &&
                first.isLoaded &&
                !first.isDirty,
                "Rollback could not reopen the first prior Scene.");

            for (int index = 1;
                 index < paths.Length;
                 index++)
            {
                Scene loaded = EditorSceneManager.OpenScene(
                    paths[index],
                    OpenSceneMode.Additive);
                Require(
                    loaded.IsValid() &&
                    loaded.isLoaded &&
                    !loaded.isDirty,
                    "Rollback could not reopen a prior additive Scene.");
            }

            if (!string.IsNullOrEmpty(identity.ActiveScenePath))
            {
                Scene active = SceneManager.GetSceneByPath(
                    identity.ActiveScenePath);
                Require(
                    active.IsValid() &&
                    active.isLoaded &&
                    EditorSceneManager.SetActiveScene(active),
                    "Rollback could not restore the prior active Scene.");
            }
        }

        private void CleanupTransientFiles()
        {
            DeleteIfExists(temporaryPath);
            DeleteIfExists(rollbackDisplacedPath);
            if (!replacementCompleted)
            {
                DeleteIfExists(backupPath);
            }
        }

        private static string Sha256(byte[] bytes)
        {
            using System.Security.Cryptography.SHA256 hash =
                System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(bytes))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
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
