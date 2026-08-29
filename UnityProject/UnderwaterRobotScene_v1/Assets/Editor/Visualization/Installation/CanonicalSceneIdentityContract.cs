using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    internal sealed class CanonicalSceneIdentityException :
        InvalidOperationException
    {
        internal CanonicalSceneIdentityException(
            string failureStage,
            string message)
            : base(message)
        {
            FailureStage = failureStage;
        }

        internal string FailureStage { get; }
    }

    internal sealed class CanonicalSceneIdentitySnapshot
    {
        private readonly byte[] approvedTemplateBytes;

        internal CanonicalSceneIdentitySnapshot(
            string projectRoot,
            string targetPath,
            string targetMetaPath,
            string templatePath,
            string templateMetaPath,
            byte[] templateBytes,
            CanonicalSceneIdentityManifestSnapshot manifest,
            string[] loadedScenePaths,
            string activeScenePath)
        {
            ProjectRoot = projectRoot;
            TargetPath = targetPath;
            TargetMetaPath = targetMetaPath;
            TemplatePath = templatePath;
            TemplateMetaPath = templateMetaPath;
            approvedTemplateBytes =
                (byte[])templateBytes.Clone();
            Manifest = manifest ??
                throw new ArgumentNullException(nameof(manifest));
            LoadedScenePaths =
                loadedScenePaths == null
                    ? Array.Empty<string>()
                    : loadedScenePaths.ToArray();
            ActiveScenePath = activeScenePath ?? string.Empty;
        }

        internal string ProjectRoot { get; }

        internal string TargetPath { get; }

        internal string TargetMetaPath { get; }

        internal string TemplatePath { get; }

        internal string TemplateMetaPath { get; }

        internal string[] LoadedScenePaths { get; }

        internal string ActiveScenePath { get; }

        internal CanonicalSceneIdentityManifestSnapshot Manifest { get; }

        internal byte[] CopyApprovedTemplateBytes()
        {
            return (byte[])approvedTemplateBytes.Clone();
        }
    }

    internal sealed class CanonicalSceneIdentityManifestSnapshot
    {
        private readonly byte[] manifestBytes;

        internal CanonicalSceneIdentityManifestSnapshot(
            int schemaVersion,
            string canonicalGenerationId,
            string sceneUnitySha256,
            long sceneByteSize,
            string semanticSha256,
            int gameObjectCount,
            int componentCount,
            int missingReferenceCount,
            string candidateEvidenceId,
            string manifestPath,
            byte[] bytes,
            string manifestSha256)
        {
            SchemaVersion = schemaVersion;
            CanonicalGenerationId = canonicalGenerationId;
            SceneUnitySha256 = sceneUnitySha256;
            SceneByteSize = sceneByteSize;
            SemanticSha256 = semanticSha256;
            GameObjectCount = gameObjectCount;
            ComponentCount = componentCount;
            MissingReferenceCount = missingReferenceCount;
            CandidateEvidenceId = candidateEvidenceId;
            ManifestPath = manifestPath;
            manifestBytes = (byte[])bytes.Clone();
            ManifestSha256 = manifestSha256;
        }

        internal int SchemaVersion { get; }
        internal string CanonicalGenerationId { get; }
        internal string SceneUnitySha256 { get; }
        internal long SceneByteSize { get; }
        internal string SemanticSha256 { get; }
        internal int GameObjectCount { get; }
        internal int ComponentCount { get; }
        internal int MissingReferenceCount { get; }
        internal string CandidateEvidenceId { get; }
        internal string ManifestPath { get; }
        internal string ManifestSha256 { get; }

        internal byte[] CopyManifestBytes()
        {
            return (byte[])manifestBytes.Clone();
        }
    }

    internal sealed class CanonicalSceneIdentityManifestPrecomputation
    {
        private readonly byte[] bytes;

        internal CanonicalSceneIdentityManifestPrecomputation(
            CanonicalSceneIdentityManifestSnapshot snapshot,
            byte[] manifestBytes)
        {
            Snapshot = snapshot ??
                throw new ArgumentNullException(nameof(snapshot));
            bytes = manifestBytes == null
                ? throw new ArgumentNullException(nameof(manifestBytes))
                : (byte[])manifestBytes.Clone();
            ManifestSha256 = snapshot.ManifestSha256;
            ArtifactLabel =
                "PRECOMPUTED ONLY / NOT ACTIVE AUTHORITY";
        }

        internal CanonicalSceneIdentityManifestSnapshot Snapshot { get; }
        internal string ManifestSha256 { get; }
        internal string ArtifactLabel { get; }

        internal byte[] CopyManifestBytes()
        {
            return (byte[])bytes.Clone();
        }
    }

    internal static class CanonicalSceneIdentityContract
    {
        internal const string TargetScenePath =
            "Assets/Scenes/UnderwaterRobotDemo.unity";
        internal const string TemplateScenePath =
            "Assets/Editor/Visualization/Installation/" +
            "UnderwaterRobotDemo_Canonical.unity";
        internal const string IdentityManifestPath =
            "Assets/Editor/Visualization/Installation/" +
            "CanonicalSceneIdentityManifest.json";
        internal static string ApprovedTemplateUnitySha =>
            LoadApprovedManifestSnapshot().SceneUnitySha256;
        internal static long ApprovedTemplateUnitySize =>
            LoadApprovedManifestSnapshot().SceneByteSize;
        internal const string ApprovedTemplateMetaSha =
            "1c23fe30225b6acfd942b4ef98718837cc25ad4d668d7df43f970489fdac9ca7";
        internal const string ApprovedTemplateGuid =
            "3f04f2e57ca846a29b9262cdf215862d";
        internal const string ApprovedTargetMetaSha =
            "72501b58f7ec4b1c7485b2fe3ffccac6a82ab187ebd7be4e7c2303c8aada8043";
        internal const string ApprovedTargetGuid =
            "8ee7b16bd085b084cbb82b5b3afcf5ba";

        private const string TemplateUnityStage =
            "Precondition.TemplateUnityIdentity";
        private const string TemplateMetaStage =
            "Precondition.TemplateMetaIdentity";
        private const string TargetMetaStage =
            "Precondition.TargetMetaIdentity";
        private const string PathStage =
            "Precondition.PathIdentity";
        private const string LoadedSceneStage =
            "Precondition.LoadedScenes";
        private const string ManifestStage =
            "Precondition.IdentityManifest";

        private static readonly Regex GuidLine = new Regex(
            @"^guid:\s*([0-9a-f]{32})\s*$",
            RegexOptions.Multiline |
            RegexOptions.CultureInvariant);

        internal static CanonicalSceneIdentitySnapshot
            ValidateInitialPreflight()
        {
            ResolvedPaths paths = ResolveAndValidatePaths();
            CanonicalSceneIdentityManifestSnapshot manifest =
                LoadApprovedManifestSnapshot();
            byte[] templateBytes =
                ValidateTemplateUnity(paths.TemplatePath, manifest);
            ValidateMeta(
                paths.TemplateMetaPath,
                ApprovedTemplateMetaSha,
                ApprovedTemplateGuid,
                TemplateMetaStage,
                "canonical template");
            ValidateMeta(
                paths.TargetMetaPath,
                ApprovedTargetMetaSha,
                ApprovedTargetGuid,
                TargetMetaStage,
                "formal target");
            LoadedSceneState loaded = ValidateLoadedScenes();

            return new CanonicalSceneIdentitySnapshot(
                paths.ProjectRoot,
                paths.TargetPath,
                paths.TargetMetaPath,
                paths.TemplatePath,
                paths.TemplateMetaPath,
                templateBytes,
                manifest,
                loaded.Paths,
                loaded.ActivePath);
        }

        internal static void ValidatePreWritePreflight(
            CanonicalSceneIdentitySnapshot initial)
        {
            if (initial == null)
            {
                throw new ArgumentNullException(nameof(initial));
            }

            ResolvedPaths paths = ResolveAndValidatePaths();
            RequireEqualPath(
                paths.ProjectRoot,
                initial.ProjectRoot,
                "project root");
            RequireEqualPath(
                paths.TargetPath,
                initial.TargetPath,
                "formal target");
            RequireEqualPath(
                paths.TargetMetaPath,
                initial.TargetMetaPath,
                "formal target meta");
            RequireEqualPath(
                paths.TemplatePath,
                initial.TemplatePath,
                "canonical template");
            RequireEqualPath(
                paths.TemplateMetaPath,
                initial.TemplateMetaPath,
                "canonical template meta");

            byte[] currentTemplate =
                ValidateTemplateUnity(
                    paths.TemplatePath,
                    initial.Manifest);
            byte[] approvedTemplate =
                initial.CopyApprovedTemplateBytes();
            if (!currentTemplate.SequenceEqual(approvedTemplate))
            {
                Fail(
                    TemplateUnityStage,
                    "The canonical template bytes changed between " +
                    "InitialPreflight and PreWritePreflight.");
            }

            CanonicalSceneIdentityManifestSnapshot currentManifest =
                LoadApprovedManifestSnapshot();
            if (!currentManifest.CopyManifestBytes()
                    .SequenceEqual(
                        initial.Manifest.CopyManifestBytes()))
            {
                Fail(
                    ManifestStage,
                    "The identity manifest bytes changed between " +
                    "InitialPreflight and PreWritePreflight.");
            }

            ValidateMeta(
                paths.TemplateMetaPath,
                ApprovedTemplateMetaSha,
                ApprovedTemplateGuid,
                TemplateMetaStage,
                "canonical template");
            ValidateMeta(
                paths.TargetMetaPath,
                ApprovedTargetMetaSha,
                ApprovedTargetGuid,
                TargetMetaStage,
                "formal target");
            ValidateLoadedScenes();
        }

        internal static void ValidateProtectedIdentities(
            CanonicalSceneIdentitySnapshot initial,
            bool requireTargetUnityApproved)
        {
            ValidatePreWritePreflight(initial);
            if (requireTargetUnityApproved)
            {
                ValidateSha(
                    initial.TargetPath,
                    initial.Manifest.SceneUnitySha256,
                    "PostWrite.TargetUnityIdentity",
                    "formal target Scene");
            }
        }

        internal static string Sha256File(string path)
        {
            using SHA256 hash = SHA256.Create();
            using FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            return BitConverter.ToString(hash.ComputeHash(stream))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        internal static CanonicalSceneIdentityManifestSnapshot
            LoadApprovedManifestSnapshot()
        {
            string projectRoot = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            string path = ResolveAssetPath(
                projectRoot,
                IdentityManifestPath);
            string expected = Path.GetFullPath(Path.Combine(
                projectRoot,
                IdentityManifestPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));
            if (!string.Equals(
                    path,
                    expected,
                    StringComparison.Ordinal))
            {
                Fail(
                    ManifestStage,
                    "The canonical identity manifest path is not exact.");
            }

            return LoadManifestSnapshot(
                path,
                projectRoot,
                true);
        }

        internal static CanonicalSceneIdentityManifestSnapshot
            LoadManifestSnapshotAtPathForVerification(string path)
        {
            string projectRoot = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            return LoadManifestSnapshot(
                path,
                projectRoot,
                false);
        }

        internal static CanonicalSceneIdentityManifestPrecomputation
            PrecomputeReplacementManifest(
                CanonicalSceneIdentityManifestSnapshot currentAuthority,
                string canonicalGenerationId,
                string sceneUnitySha256,
                long sceneByteSize,
                string semanticSha256,
                int gameObjectCount,
                int componentCount,
                int missingReferenceCount,
                string candidateEvidenceId)
        {
            if (currentAuthority == null)
            {
                throw new ArgumentNullException(nameof(currentAuthority));
            }

            string json =
                "{\n" +
                "  \"schemaVersion\": " +
                currentAuthority.SchemaVersion
                    .ToString(CultureInfo.InvariantCulture) +
                ",\n" +
                "  \"canonicalGenerationId\": \"" +
                EscapeJsonString(canonicalGenerationId) +
                "\",\n" +
                "  \"sceneUnitySha256\": \"" +
                EscapeJsonString(sceneUnitySha256) +
                "\",\n" +
                "  \"sceneByteSize\": " +
                sceneByteSize.ToString(CultureInfo.InvariantCulture) +
                ",\n" +
                "  \"semanticSha256\": \"" +
                EscapeJsonString(semanticSha256) +
                "\",\n" +
                "  \"gameObjectCount\": " +
                gameObjectCount.ToString(CultureInfo.InvariantCulture) +
                ",\n" +
                "  \"componentCount\": " +
                componentCount.ToString(CultureInfo.InvariantCulture) +
                ",\n" +
                "  \"missingReferenceCount\": " +
                missingReferenceCount.ToString(CultureInfo.InvariantCulture) +
                ",\n" +
                "  \"candidateEvidenceId\": \"" +
                EscapeJsonString(candidateEvidenceId) +
                "\"\n" +
                "}\n";
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(json);
            CanonicalSceneIdentityManifestSnapshot snapshot =
                ParseManifest("<precomputed-memory-only>", bytes);
            return new CanonicalSceneIdentityManifestPrecomputation(
                snapshot,
                bytes);
        }

        private static string EscapeJsonString(string value)
        {
            if (value == null)
            {
                Fail(
                    ManifestStage,
                    "Manifest string values cannot be null.");
            }

            var builder = new StringBuilder(value.Length + 16);
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < 0x20)
                        {
                            builder.Append("\\u");
                            builder.Append(
                                ((int)character).ToString(
                                    "x4",
                                    CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }
                        break;
                }
            }

            return builder.ToString();
        }

        private static CanonicalSceneIdentityManifestSnapshot
            LoadManifestSnapshot(
                string suppliedPath,
                string projectRoot,
                bool requireCanonicalPath)
        {
            if (string.IsNullOrWhiteSpace(suppliedPath) ||
                !Path.IsPathRooted(suppliedPath))
            {
                Fail(
                    ManifestStage,
                    "The identity manifest path must be a non-empty " +
                    "absolute path.");
            }

            string[] pathSegments = suppliedPath
                .Replace('\\', '/')
                .Split('/');
            if (pathSegments.Any(segment =>
                    string.Equals(
                        segment,
                        "..",
                        StringComparison.Ordinal)))
            {
                Fail(
                    ManifestStage,
                    "The identity manifest path cannot contain '..'.");
            }

            string fullPath = Path.GetFullPath(suppliedPath);
            string rootWithSeparator =
                Path.GetFullPath(projectRoot).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(
                    rootWithSeparator,
                    StringComparison.OrdinalIgnoreCase))
            {
                Fail(
                    ManifestStage,
                    "The identity manifest path is outside the Unity project.");
            }

            string canonicalPath = Path.GetFullPath(Path.Combine(
                projectRoot,
                IdentityManifestPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));
            if (requireCanonicalPath &&
                !string.Equals(
                    fullPath,
                    canonicalPath,
                    StringComparison.Ordinal))
            {
                Fail(
                    ManifestStage,
                    "The identity manifest path is not the fixed " +
                    "canonical path.");
            }

            if (Directory.Exists(fullPath))
            {
                Fail(
                    ManifestStage,
                    "The identity manifest path resolves to a directory.");
            }

            if (!File.Exists(fullPath))
            {
                Fail(
                    ManifestStage,
                    "The identity manifest is missing: " + fullPath);
            }

            string extension = Path.GetExtension(fullPath);
            if (string.Equals(
                    extension,
                    ".meta",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    extension,
                    ".unity",
                    StringComparison.OrdinalIgnoreCase))
            {
                Fail(
                    ManifestStage,
                    "The identity manifest cannot be a Scene or meta file.");
            }

            ValidateRegularFile(
                fullPath,
                projectRoot,
                "identity manifest");

            string[] protectedPaths =
            {
                ResolveAssetPath(projectRoot, TargetScenePath),
                ResolveAssetPath(projectRoot, TemplateScenePath),
                ResolveAssetPath(projectRoot, TargetScenePath) + ".meta",
                ResolveAssetPath(projectRoot, TemplateScenePath) + ".meta"
            };
            foreach (string protectedPath in protectedPaths)
            {
                if (string.Equals(
                        fullPath,
                        protectedPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    FilesReferToSamePhysicalFile(
                        fullPath,
                        protectedPath))
                {
                    Fail(
                        ManifestStage,
                        "The identity manifest resolves to a protected " +
                        "Scene identity file.");
                }
            }

            if (!requireCanonicalPath &&
                !string.Equals(
                    fullPath,
                    canonicalPath,
                    StringComparison.OrdinalIgnoreCase) &&
                FilesReferToSamePhysicalFile(
                    fullPath,
                    canonicalPath))
            {
                Fail(
                    ManifestStage,
                    "The verification manifest resolves to the canonical " +
                    "manifest physical file.");
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(fullPath);
            }
            catch (Exception exception)
            {
                throw new CanonicalSceneIdentityException(
                    ManifestStage,
                    "Could not read identity manifest bytes: " +
                    exception.Message);
            }

            return ParseManifest(fullPath, bytes);
        }

        private static CanonicalSceneIdentityManifestSnapshot
            ParseManifest(string path, byte[] bytes)
        {
            Dictionary<string, ManifestJsonValue> values;
            try
            {
                string json = new UTF8Encoding(false, true)
                    .GetString(bytes);
                values = new StrictManifestJsonParser(json).Parse();
            }
            catch (CanonicalSceneIdentityException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new CanonicalSceneIdentityException(
                    ManifestStage,
                    "Identity manifest JSON is invalid: " +
                    exception.Message);
            }

            int schemaVersion = RequireManifestInt(
                values,
                "schemaVersion",
                0,
                int.MaxValue);
            if (schemaVersion != 1)
            {
                Fail(
                    ManifestStage,
                    "Unsupported identity manifest schema version: " +
                    schemaVersion);
            }

            string generationId = RequireManifestIdentifier(
                values,
                "canonicalGenerationId");
            string sceneSha = RequireManifestSha(
                values,
                "sceneUnitySha256");
            long sceneSize = RequireManifestLong(
                values,
                "sceneByteSize",
                1,
                long.MaxValue);
            string semanticSha = RequireManifestSha(
                values,
                "semanticSha256");
            int gameObjects = RequireManifestInt(
                values,
                "gameObjectCount",
                0,
                int.MaxValue);
            int components = RequireManifestInt(
                values,
                "componentCount",
                0,
                int.MaxValue);
            int missing = RequireManifestInt(
                values,
                "missingReferenceCount",
                0,
                int.MaxValue);
            string evidenceId = RequireManifestIdentifier(
                values,
                "candidateEvidenceId");

            return new CanonicalSceneIdentityManifestSnapshot(
                schemaVersion,
                generationId,
                sceneSha,
                sceneSize,
                semanticSha,
                gameObjects,
                components,
                missing,
                evidenceId,
                path,
                bytes,
                Sha256(bytes));
        }

        private static string RequireManifestIdentifier(
            IReadOnlyDictionary<string, ManifestJsonValue> values,
            string name)
        {
            string value = RequireManifestString(values, name);
            if (string.IsNullOrWhiteSpace(value) ||
                !string.Equals(
                    value,
                    value.Trim(),
                    StringComparison.Ordinal) ||
                value.Length > 256)
            {
                Fail(
                    ManifestStage,
                    name + " must be a non-empty trimmed identifier.");
            }

            return value;
        }

        private static string RequireManifestSha(
            IReadOnlyDictionary<string, ManifestJsonValue> values,
            string name)
        {
            string value = RequireManifestString(values, name);
            if (!Regex.IsMatch(
                    value,
                    @"\A[0-9a-f]{64}\z",
                    RegexOptions.CultureInvariant))
            {
                Fail(
                    ManifestStage,
                    name +
                    " must be exactly 64 lowercase hexadecimal characters.");
            }

            return value;
        }

        private static string RequireManifestString(
            IReadOnlyDictionary<string, ManifestJsonValue> values,
            string name)
        {
            ManifestJsonValue value = values[name];
            if (!value.IsString)
            {
                Fail(
                    ManifestStage,
                    name + " must be a JSON string.");
            }

            return value.StringValue;
        }

        private static int RequireManifestInt(
            IReadOnlyDictionary<string, ManifestJsonValue> values,
            string name,
            int minimum,
            int maximum)
        {
            return checked((int)RequireManifestLong(
                values,
                name,
                minimum,
                maximum));
        }

        private static long RequireManifestLong(
            IReadOnlyDictionary<string, ManifestJsonValue> values,
            string name,
            long minimum,
            long maximum)
        {
            ManifestJsonValue value = values[name];
            if (value.IsString ||
                value.IntegerValue < minimum ||
                value.IntegerValue > maximum)
            {
                Fail(
                    ManifestStage,
                    name + " is outside the approved integer range.");
            }

            return value.IntegerValue;
        }

        internal static string ReadGuid(
            string metaPath,
            string failureStage,
            string label)
        {
            string text;
            try
            {
                text = File.ReadAllText(metaPath);
            }
            catch (Exception exception)
            {
                throw new CanonicalSceneIdentityException(
                    failureStage,
                    "Could not read " + label + " meta: " +
                    exception.Message);
            }

            MatchCollection matches = GuidLine.Matches(text);
            if (matches.Count != 1)
            {
                Fail(
                    failureStage,
                    label + " meta must contain exactly one valid GUID.");
            }

            return matches[0].Groups[1].Value;
        }

        private static ResolvedPaths ResolveAndValidatePaths()
        {
            string projectRoot = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            string expectedAssets = Path.GetFullPath(
                Path.Combine(projectRoot, "Assets"));
            if (!string.Equals(
                    expectedAssets,
                    Path.GetFullPath(Application.dataPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                Fail(
                    PathStage,
                    "Application.dataPath is outside the resolved project root.");
            }

            string targetPath = ResolveAssetPath(
                projectRoot,
                TargetScenePath);
            string templatePath = ResolveAssetPath(
                projectRoot,
                TemplateScenePath);
            string targetMetaPath = targetPath + ".meta";
            string templateMetaPath = templatePath + ".meta";

            ValidateRegularFile(
                targetPath,
                projectRoot,
                "formal target Scene");
            ValidateRegularFile(
                targetMetaPath,
                projectRoot,
                "formal target Scene meta");
            ValidateRegularFile(
                templatePath,
                projectRoot,
                "canonical template Scene");
            ValidateRegularFile(
                templateMetaPath,
                projectRoot,
                "canonical template Scene meta");

            if (string.Equals(
                    targetPath,
                    templatePath,
                    StringComparison.OrdinalIgnoreCase) ||
                FilesReferToSamePhysicalFile(
                    targetPath,
                    templatePath))
            {
                Fail(
                    PathStage,
                    "Formal target and canonical template resolve to " +
                    "the same physical file.");
            }

            return new ResolvedPaths
            {
                ProjectRoot = projectRoot,
                TargetPath = targetPath,
                TargetMetaPath = targetMetaPath,
                TemplatePath = templatePath,
                TemplateMetaPath = templateMetaPath
            };
        }

        private static string ResolveAssetPath(
            string projectRoot,
            string assetPath)
        {
            string candidate = Path.GetFullPath(
                Path.Combine(
                    projectRoot,
                    assetPath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));
            string rootWithSeparator =
                projectRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(
                    rootWithSeparator,
                    StringComparison.OrdinalIgnoreCase))
            {
                Fail(
                    PathStage,
                    "Asset path escapes the Unity project root: " +
                    assetPath);
            }

            return candidate;
        }

        private static void ValidateRegularFile(
            string path,
            string projectRoot,
            string label)
        {
            if (!File.Exists(path))
            {
                Fail(
                    PathStage,
                    label + " is missing: " + path);
            }

            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes &
                    (FileAttributes.Directory |
                     FileAttributes.Device |
                     FileAttributes.ReparsePoint)) != 0)
            {
                Fail(
                    PathStage,
                    label + " is not an approved regular file: " + path);
            }

            EnsureNoReparsePoint(path, projectRoot, label);
        }

        private static void EnsureNoReparsePoint(
            string path,
            string projectRoot,
            string label)
        {
            string current = Path.GetFullPath(path);
            string root = Path.GetFullPath(projectRoot);
            while (!string.Equals(
                current,
                root,
                StringComparison.OrdinalIgnoreCase))
            {
                string parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent))
                {
                    Fail(
                        PathStage,
                        label + " path could not be traced to project root.");
                }

                current = parent;
                FileAttributes attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    Fail(
                        PathStage,
                        label + " path contains a symlink or junction: " +
                        current);
                }
            }
        }

        private static byte[] ValidateTemplateUnity(
            string path,
            CanonicalSceneIdentityManifestSnapshot manifest)
        {
            FileInfo info = new FileInfo(path);
            if (info.Length != manifest.SceneByteSize)
            {
                Fail(
                    TemplateUnityStage,
                    "Canonical template size identity mismatch. " +
                    "Expected " +
                    manifest.SceneByteSize +
                    ", actual " +
                    info.Length +
                    ".");
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception exception)
            {
                throw new CanonicalSceneIdentityException(
                    TemplateUnityStage,
                    "Could not read canonical template bytes: " +
                    exception.Message);
            }

            string actual = Sha256(bytes);
            if (!string.Equals(
                    actual,
                    manifest.SceneUnitySha256,
                    StringComparison.Ordinal))
            {
                Fail(
                    TemplateUnityStage,
                    "Canonical template SHA identity mismatch. " +
                    "Expected " +
                    manifest.SceneUnitySha256 +
                    ", actual " +
                    actual +
                    ".");
            }

            return bytes;
        }

        private static void ValidateMeta(
            string path,
            string expectedSha,
            string expectedGuid,
            string failureStage,
            string label)
        {
            string actualSha = Sha256File(path);
            if (!string.Equals(
                    actualSha,
                    expectedSha,
                    StringComparison.Ordinal))
            {
                Fail(
                    failureStage,
                    label + " meta SHA identity mismatch. Expected " +
                    expectedSha +
                    ", actual " +
                    actualSha +
                    ".");
            }

            string actualGuid = ReadGuid(
                path,
                failureStage,
                label);
            if (!string.Equals(
                    actualGuid,
                    expectedGuid,
                    StringComparison.Ordinal))
            {
                Fail(
                    failureStage,
                    label + " GUID identity mismatch. Expected " +
                    expectedGuid +
                    ", actual " +
                    actualGuid +
                    ".");
            }
        }

        private static LoadedSceneState ValidateLoadedScenes()
        {
            var paths = new List<string>();
            Scene active = SceneManager.GetActiveScene();
            string activePath =
                active.IsValid() ? active.path : string.Empty;

            for (int index = 0;
                 index < SceneManager.sceneCount;
                 index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                if (scene.isDirty)
                {
                    Fail(
                        LoadedSceneStage,
                        "Loaded Scene is dirty and cannot be released: " +
                        (string.IsNullOrEmpty(scene.path)
                            ? "<untitled>"
                            : scene.path));
                }

                if (string.IsNullOrEmpty(scene.path))
                {
                    if (scene.GetRootGameObjects().Length != 0)
                    {
                        Fail(
                            LoadedSceneStage,
                            "An untitled loaded Scene contains unsaved " +
                            "objects.");
                    }

                    continue;
                }

                paths.Add(scene.path);
            }

            return new LoadedSceneState
            {
                Paths = paths
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                ActivePath = activePath
            };
        }

        private static void ValidateSha(
            string path,
            string expectedSha,
            string failureStage,
            string label)
        {
            string actual = Sha256File(path);
            if (!string.Equals(
                    actual,
                    expectedSha,
                    StringComparison.Ordinal))
            {
                Fail(
                    failureStage,
                    label + " SHA identity mismatch. Expected " +
                    expectedSha +
                    ", actual " +
                    actual +
                    ".");
            }
        }

        private static void RequireEqualPath(
            string actual,
            string expected,
            string label)
        {
            if (!string.Equals(
                    actual,
                    expected,
                    StringComparison.OrdinalIgnoreCase))
            {
                Fail(
                    PathStage,
                    label + " path changed between preflight passes.");
            }
        }

        private static string Sha256(byte[] bytes)
        {
            using SHA256 hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(bytes))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        private static bool FilesReferToSamePhysicalFile(
            string first,
            string second)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                return false;
            }

            using SafeFileHandle firstHandle = OpenIdentityHandle(first);
            using SafeFileHandle secondHandle = OpenIdentityHandle(second);
            if (firstHandle.IsInvalid || secondHandle.IsInvalid)
            {
                Fail(
                    PathStage,
                    "Could not resolve physical file identity.");
            }

            var firstInfo = new ByHandleFileInformation();
            var secondInfo = new ByHandleFileInformation();
            bool firstRead = GetFileInformationByHandle(
                firstHandle,
                out firstInfo);
            bool secondRead = GetFileInformationByHandle(
                secondHandle,
                out secondInfo);
            if (!firstRead || !secondRead)
            {
                Fail(
                    PathStage,
                    "Could not query physical file identity.");
            }

            return firstInfo.VolumeSerialNumber ==
                   secondInfo.VolumeSerialNumber &&
                   firstInfo.FileIndexHigh ==
                   secondInfo.FileIndexHigh &&
                   firstInfo.FileIndexLow ==
                   secondInfo.FileIndexLow;
        }

        private static SafeFileHandle OpenIdentityHandle(string path)
        {
            return CreateFile(
                path,
                0,
                FileShare.Read |
                FileShare.Write |
                FileShare.Delete,
                IntPtr.Zero,
                FileMode.Open,
                0,
                IntPtr.Zero);
        }

        private static void Fail(string stage, string message)
        {
            throw new CanonicalSceneIdentityException(stage, message);
        }

        private sealed class ManifestJsonValue
        {
            internal bool IsString;
            internal string StringValue;
            internal long IntegerValue;
        }

        private sealed class StrictManifestJsonParser
        {
            private static readonly HashSet<string> AllowedFields =
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "schemaVersion",
                    "canonicalGenerationId",
                    "sceneUnitySha256",
                    "sceneByteSize",
                    "semanticSha256",
                    "gameObjectCount",
                    "componentCount",
                    "missingReferenceCount",
                    "candidateEvidenceId"
                };

            private readonly string json;
            private int index;

            internal StrictManifestJsonParser(string json)
            {
                this.json = json ??
                    throw new ArgumentNullException(nameof(json));
            }

            internal Dictionary<string, ManifestJsonValue> Parse()
            {
                var values =
                    new Dictionary<string, ManifestJsonValue>(
                        StringComparer.Ordinal);
                SkipWhitespace();
                Expect('{');
                SkipWhitespace();
                if (TryConsume('}'))
                {
                    Fail(
                        ManifestStage,
                        "Identity manifest cannot be empty.");
                }

                while (true)
                {
                    string name = ParseString();
                    if (!AllowedFields.Contains(name))
                    {
                        Fail(
                            ManifestStage,
                            "Unknown identity manifest field: " + name);
                    }

                    if (values.ContainsKey(name))
                    {
                        Fail(
                            ManifestStage,
                            "Duplicate identity manifest field: " + name);
                    }

                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    ManifestJsonValue value = ParseValue();
                    values.Add(name, value);
                    SkipWhitespace();
                    if (TryConsume('}'))
                    {
                        break;
                    }

                    Expect(',');
                    SkipWhitespace();
                    if (Peek('}'))
                    {
                        Fail(
                            ManifestStage,
                            "Identity manifest cannot contain a trailing comma.");
                    }
                }

                SkipWhitespace();
                if (index != json.Length)
                {
                    Fail(
                        ManifestStage,
                        "Identity manifest contains trailing JSON content.");
                }

                string[] missing = AllowedFields
                    .Where(name => !values.ContainsKey(name))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
                if (missing.Length != 0)
                {
                    Fail(
                        ManifestStage,
                        "Identity manifest is missing fields: " +
                        string.Join(", ", missing));
                }

                return values;
            }

            private ManifestJsonValue ParseValue()
            {
                if (Peek('"'))
                {
                    return new ManifestJsonValue
                    {
                        IsString = true,
                        StringValue = ParseString()
                    };
                }

                int start = index;
                if (TryConsume('-') && index >= json.Length)
                {
                    Fail(
                        ManifestStage,
                        "Identity manifest contains an incomplete number.");
                }

                if (index >= json.Length ||
                    json[index] < '0' ||
                    json[index] > '9')
                {
                    Fail(
                        ManifestStage,
                        "Identity manifest values must be strings or integers.");
                }

                if (json[index] == '0')
                {
                    index++;
                    if (index < json.Length &&
                        json[index] >= '0' &&
                        json[index] <= '9')
                    {
                        Fail(
                            ManifestStage,
                            "Identity manifest integers cannot have " +
                            "leading zeroes.");
                    }
                }
                else
                {
                    while (index < json.Length &&
                           json[index] >= '0' &&
                           json[index] <= '9')
                    {
                        index++;
                    }
                }

                if (index < json.Length &&
                    (json[index] == '.' ||
                     json[index] == 'e' ||
                     json[index] == 'E' ||
                     json[index] == '+'))
                {
                    Fail(
                        ManifestStage,
                        "Identity manifest numeric fields must be integers.");
                }

                string token = json.Substring(start, index - start);
                if (!long.TryParse(
                        token,
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out long value))
                {
                    Fail(
                        ManifestStage,
                        "Identity manifest integer is invalid or overflows.");
                }

                return new ManifestJsonValue
                {
                    IsString = false,
                    IntegerValue = value
                };
            }

            private string ParseString()
            {
                Expect('"');
                var builder = new StringBuilder();
                while (index < json.Length)
                {
                    char value = json[index++];
                    if (value == '"')
                    {
                        return builder.ToString();
                    }

                    if (value < 0x20)
                    {
                        Fail(
                            ManifestStage,
                            "Identity manifest strings contain a control " +
                            "character.");
                    }

                    if (value != '\\')
                    {
                        builder.Append(value);
                        continue;
                    }

                    if (index >= json.Length)
                    {
                        Fail(
                            ManifestStage,
                            "Identity manifest string escape is incomplete.");
                    }

                    char escaped = json[index++];
                    switch (escaped)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            builder.Append(escaped);
                            break;
                        case 'b':
                            builder.Append('\b');
                            break;
                        case 'f':
                            builder.Append('\f');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        case 'u':
                            builder.Append(ParseUnicodeEscape());
                            break;
                        default:
                            Fail(
                                ManifestStage,
                                "Identity manifest contains an invalid " +
                                "string escape.");
                            break;
                    }
                }

                Fail(
                    ManifestStage,
                    "Identity manifest string is unterminated.");
                return string.Empty;
            }

            private char ParseUnicodeEscape()
            {
                if (index + 4 > json.Length)
                {
                    Fail(
                        ManifestStage,
                        "Identity manifest Unicode escape is incomplete.");
                }

                int codePoint = 0;
                for (int offset = 0; offset < 4; offset++)
                {
                    char value = json[index++];
                    int digit =
                        value >= '0' && value <= '9'
                            ? value - '0'
                            : value >= 'a' && value <= 'f'
                                ? value - 'a' + 10
                                : value >= 'A' && value <= 'F'
                                    ? value - 'A' + 10
                                    : -1;
                    if (digit < 0)
                    {
                        Fail(
                            ManifestStage,
                            "Identity manifest Unicode escape is invalid.");
                    }

                    codePoint = (codePoint << 4) | digit;
                }

                if (codePoint >= 0xd800 && codePoint <= 0xdfff)
                {
                    Fail(
                        ManifestStage,
                        "Identity manifest cannot contain a surrogate " +
                        "Unicode escape.");
                }

                return (char)codePoint;
            }

            private void SkipWhitespace()
            {
                while (index < json.Length &&
                       (json[index] == ' ' ||
                        json[index] == '\t' ||
                        json[index] == '\r' ||
                        json[index] == '\n'))
                {
                    index++;
                }
            }

            private bool Peek(char value)
            {
                return index < json.Length && json[index] == value;
            }

            private bool TryConsume(char value)
            {
                if (!Peek(value))
                {
                    return false;
                }

                index++;
                return true;
            }

            private void Expect(char value)
            {
                if (!TryConsume(value))
                {
                    Fail(
                        ManifestStage,
                        "Identity manifest JSON expected '" +
                        value +
                        "'.");
                }
            }
        }

        private sealed class ResolvedPaths
        {
            internal string ProjectRoot;
            internal string TargetPath;
            internal string TargetMetaPath;
            internal string TemplatePath;
            internal string TemplateMetaPath;
        }

        private sealed class LoadedSceneState
        {
            internal string[] Paths;
            internal string ActivePath;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileTime
        {
            internal uint Low;
            internal uint High;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleFileInformation
        {
            internal uint FileAttributes;
            internal FileTime CreationTime;
            internal FileTime LastAccessTime;
            internal FileTime LastWriteTime;
            internal uint VolumeSerialNumber;
            internal uint FileSizeHigh;
            internal uint FileSizeLow;
            internal uint NumberOfLinks;
            internal uint FileIndexHigh;
            internal uint FileIndexLow;
        }

        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            FileShare shareMode,
            IntPtr securityAttributes,
            FileMode creationDisposition,
            int flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(
            SafeFileHandle file,
            out ByHandleFileInformation information);
    }
}
