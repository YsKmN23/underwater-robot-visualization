using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    public static class UsvPostBuildInstallerChainVerifier
    {
        private const string ScenePath =
            "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string HistoricalAuthoritativeSceneSha =
            "dc45dfced896a271dc0eb21e506b4f1e23fa3b9705b45cf549d6dd5af7b01f0a";
        private const string ExpectedSceneShaArgument =
            "-expectedAuthoritativeSceneSha";

        [Serializable]
        private sealed class NoOpReport
        {
            public string Status;
            public string ScenePath;
            public string ExpectedSceneSha;
            public string SceneShaBefore;
            public UsvPostBuildInstallerChainResult FirstRun;
            public UsvPostBuildInstallerChainResult SecondRun;
            public string SceneShaAfter;
            public bool SceneBytesIdentical;
            public bool SceneDirty;
            public bool SourceContractPassed;
            public bool ExistingM2DVerifierPassed;
        }

        [Serializable]
        private sealed class SemanticSignature
        {
            public string Status;
            public string ScenePath;
            public string SceneSha;
            public string CanonicalSemanticSha;
            public int GameObjectCount;
            public int ComponentCount;
            public int MissingReferenceCount;
            public string[] Hierarchy;
            public string[] Components;
            public string[] Transforms;
            public string[] SerializedProperties;
            public string[] PrefabSources;
        }

        [MenuItem(
            "Tools/Underwater Demo/M2-CD/Verify Canonical USV Post-Build Installer Chain")]
        public static void RunFromMenu()
        {
            RunBatch();
        }

        public static void RunBatch()
        {
            string expectedSceneSha = ResolveExpectedAuthoritativeSceneSha();
            VerifySourceContract();
            Scene scene =
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Require(scene.IsValid() && scene.isLoaded && !scene.isDirty,
                "The authoritative formal Scene is not clean and loaded.");
            VerifyCanonicalSceneSemanticIdentity();
            scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded && !scene.isDirty,
                "The canonical semantic identity checks did not leave a clean Scene loaded.");

            string absoluteScenePath = GetAbsoluteScenePath();
            byte[] before = File.ReadAllBytes(absoluteScenePath);
            string shaBefore = Sha256(before);
            Require(
                string.Equals(
                    shaBefore,
                    expectedSceneSha,
                    StringComparison.Ordinal),
                "The authoritative Scene SHA does not match the approved baseline.");

            UsvPostBuildInstallerChainResult first =
                UsvPostBuildInstallerChain.InstallCanonicalUsvPostBuildChain();
            VerifyNoOpResult(
                first,
                "first",
                expectedSceneSha,
                Sha256(File.ReadAllBytes(absoluteScenePath)));

            UsvPostBuildInstallerChainResult second =
                UsvPostBuildInstallerChain.InstallCanonicalUsvPostBuildChain();
            VerifyNoOpResult(
                second,
                "second",
                expectedSceneSha,
                Sha256(File.ReadAllBytes(absoluteScenePath)));

            byte[] after = File.ReadAllBytes(absoluteScenePath);
            string shaAfter = Sha256(after);
            Scene finalScene = SceneManager.GetActiveScene();
            Require(before.SequenceEqual(after),
                "Two canonical chain runs changed authoritative Scene bytes.");
            Require(
                string.Equals(shaBefore, shaAfter, StringComparison.Ordinal),
                "Two canonical chain runs changed authoritative Scene SHA.");
            Require(!finalScene.isDirty,
                "Two canonical chain runs dirtied the authoritative Scene.");

            UsvActuatorVisualM2DVerifier.RunBatch();

            WriteReport(new NoOpReport
            {
                Status = "M2_CD_USV_POST_BUILD_INSTALLER_CHAIN_VERIFICATION_PASS",
                ScenePath = ScenePath,
                ExpectedSceneSha = expectedSceneSha,
                SceneShaBefore = shaBefore,
                FirstRun = first,
                SecondRun = second,
                SceneShaAfter = shaAfter,
                SceneBytesIdentical = true,
                SceneDirty = false,
                SourceContractPassed = true,
                ExistingM2DVerifierPassed = true
            });
            Debug.Log(
                "M2_CD_USV_POST_BUILD_INSTALLER_CHAIN_VERIFICATION_PASS");
        }

        public static void WriteSemanticSignatureBatch()
        {
            Scene scene =
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Require(scene.IsValid() && scene.isLoaded && !scene.isDirty,
                "Cannot export a semantic signature from an invalid or dirty Scene.");
            SemanticSignature signature = BuildSemanticSignature(scene);
            string path = GetCommandLineArgument("-usvSemanticSignaturePath");
            Require(!string.IsNullOrWhiteSpace(path),
                "Missing -usvSemanticSignaturePath command-line argument.");
            string absolutePath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllText(
                absolutePath,
                JsonUtility.ToJson(signature, true),
                new UTF8Encoding(false));
            Debug.Log(
                "M2_CD_USV_SEMANTIC_SIGNATURE_WRITE_PASS | sha=" +
                signature.CanonicalSemanticSha);
        }

        private static SemanticSignature BuildSemanticSignature(Scene scene)
        {
            var hierarchy = new List<string>();
            var components = new List<string>();
            var transforms = new List<string>();
            var serialized = new List<string>();
            var prefabSources = new List<string>();
            int missingReferenceCount = 0;

            GameObject[] gameObjects = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
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
                    string prefabPath = AssetDatabase.GetAssetPath(prefabSource);
                    prefabSources.Add(
                        objectPath +
                        "|" + prefabPath +
                        "|guid=" + AssetDatabase.AssetPathToGUID(prefabPath));
                }

                Component[] objectComponents = gameObject.GetComponents<Component>();
                foreach (Component component in objectComponents)
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
                    SerializedProperty property = serializedObject.GetIterator();
                    bool enterChildren = true;
                    while (property.NextVisible(enterChildren))
                    {
                        enterChildren = false;
                        if (ShouldSkipProperty(property.propertyPath))
                        {
                            continue;
                        }

                        string value;
                        if (!TryFormatProperty(
                                property,
                                out value,
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
            string canonical = string.Join("\n", hierarchy) + "\n--components--\n" +
                               string.Join("\n", components) + "\n--transforms--\n" +
                               string.Join("\n", transforms) + "\n--serialized--\n" +
                               string.Join("\n", serialized) + "\n--prefabs--\n" +
                               string.Join("\n", prefabSources) +
                               "\n--missing--\n" + missingReferenceCount;

            return new SemanticSignature
            {
                Status = "M2_CD_USV_SEMANTIC_SIGNATURE_COMPLETE",
                ScenePath = scene.path,
                SceneSha = Sha256(File.ReadAllBytes(GetAbsoluteScenePath())),
                CanonicalSemanticSha =
                    Sha256(Encoding.UTF8.GetBytes(canonical)),
                GameObjectCount = gameObjects.Length,
                ComponentCount = components.Count,
                MissingReferenceCount = missingReferenceCount,
                Hierarchy = hierarchy.ToArray(),
                Components = components.ToArray(),
                Transforms = transforms.ToArray(),
                SerializedProperties = serialized.ToArray(),
                PrefabSources = prefabSources.ToArray()
            };
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
                            ? property.enumDisplayNames[property.enumValueIndex]
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
                    "scene:" + GetHierarchyPath(component.gameObject) +
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
                string.Equals(propertyPath, "m_CorrespondingSourceObject") ||
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
                Format(value.x) + "," + Format(value.y) + "," + Format(value.z);
        }

        private static string Format(Vector4 value)
        {
            return
                Format(value.x) + "," + Format(value.y) + "," +
                Format(value.z) + "," + Format(value.w);
        }

        private static string Format(Quaternion value)
        {
            return
                Format(value.x) + "," + Format(value.y) + "," +
                Format(value.z) + "," + Format(value.w);
        }

        private static string Format(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static void VerifySourceContract()
        {
            string sourcePath = Path.GetFullPath(Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Assets/Editor/Visualization/Installation/" +
                "UsvPostBuildInstallerChain.cs"));
            string source = File.ReadAllText(sourcePath)
                .Replace("\r\n", "\n");
            string structuralSource =
                MaskCommentsAndLiteralContents(source);
            const string RegistrationPrefix =
                @"new\s+InstallerStage\s*\(\s*" +
                @"\x22\s*\x22\s*,\s*";
            const string RegistrationSuffix =
                @"\s*\.\s*InstallForCanonicalPostBuildChain" +
                @"\s*(?:\(\s*\))?\s*\)";
            string n6d =
                RegistrationPrefix +
                @"UsvRootPoseN6DSceneInstaller" +
                RegistrationSuffix;
            string m2c =
                RegistrationPrefix +
                @"UsvSurfaceVisualM2CSceneInstaller" +
                RegistrationSuffix;
            string m2d =
                RegistrationPrefix +
                @"UsvActuatorVisualM2DSceneInstaller" +
                RegistrationSuffix;
            MatchCollection allRegistrations = Regex.Matches(
                structuralSource,
                @"new\s+InstallerStage\s*\(",
                RegexOptions.CultureInvariant);
            Match n6dRegistration = RequireSingleRegistration(
                structuralSource,
                n6d,
                "N6-D");
            Match m2cRegistration = RequireSingleRegistration(
                structuralSource,
                m2c,
                "M2-C");
            Match m2dRegistration = RequireSingleRegistration(
                structuralSource,
                m2d,
                "M2-D");
            Require(allRegistrations.Count == 3,
                "The Chain must register exactly three stages.");
            Require(n6dRegistration.Success,
                "The Chain must explicitly invoke N6-D exactly once.");
            Require(m2cRegistration.Success,
                "The Chain must explicitly invoke M2-C exactly once.");
            Require(m2dRegistration.Success,
                "The Chain must explicitly invoke M2-D exactly once.");
            Require(
                n6dRegistration.Index < m2cRegistration.Index &&
                m2cRegistration.Index < m2dRegistration.Index,
                "The Chain order must be N6-D, M2-C, then M2-D.");
            Require(Regex.Matches(
                    structuralSource,
                    @"\bbool\s+changed\s*=\s*stage\s*\.\s*" +
                    @"Install\s*\(\s*\)\s*;",
                    RegexOptions.CultureInvariant).Count == 1,
                "The Chain must invoke each registered method group " +
                "through one exact stage.Install() execution point.");
            Require(
                !structuralSource.Contains("UnderwaterSceneBuilder") &&
                !structuralSource.Contains("System.Reflection") &&
                !structuralSource.Contains("AddComponent<") &&
                !structuralSource.Contains("new GameObject"),
                "The Chain crossed its orchestration-only source boundary.");
        }

        private static Match RequireSingleRegistration(
            string structuralSource,
            string pattern,
            string label)
        {
            MatchCollection matches = Regex.Matches(
                structuralSource,
                pattern,
                RegexOptions.CultureInvariant);
            Require(matches.Count == 1,
                "The Chain must register the exact " + label +
                " declaring-type/method identity exactly once.");
            return matches[0];
        }

        private static string MaskCommentsAndLiteralContents(
            string source)
        {
            var result = new StringBuilder(source.Length);
            bool lineComment = false;
            bool blockComment = false;
            bool normalString = false;
            bool verbatimString = false;
            bool characterLiteral = false;
            bool escaped = false;
            for (int index = 0; index < source.Length; index++)
            {
                char current = source[index];
                char next = index + 1 < source.Length
                    ? source[index + 1]
                    : '\0';
                if (lineComment)
                {
                    if (current == '\n')
                    {
                        lineComment = false;
                        result.Append('\n');
                    }
                    else
                    {
                        result.Append(' ');
                    }

                    continue;
                }

                if (blockComment)
                {
                    if (current == '*' && next == '/')
                    {
                        result.Append("  ");
                        index++;
                        blockComment = false;
                    }
                    else
                    {
                        result.Append(current == '\n' ? '\n' : ' ');
                    }

                    continue;
                }

                if (normalString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        result.Append(' ');
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                        result.Append(' ');
                    }
                    else if (current == '"')
                    {
                        normalString = false;
                        result.Append('"');
                    }
                    else
                    {
                        result.Append(current == '\n' ? '\n' : ' ');
                    }

                    continue;
                }

                if (verbatimString)
                {
                    if (current == '"' && next == '"')
                    {
                        result.Append("  ");
                        index++;
                    }
                    else if (current == '"')
                    {
                        verbatimString = false;
                        result.Append('"');
                    }
                    else
                    {
                        result.Append(current == '\n' ? '\n' : ' ');
                    }

                    continue;
                }

                if (characterLiteral)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == '\'')
                    {
                        characterLiteral = false;
                    }

                    result.Append(current == '\n' ? '\n' : ' ');
                    continue;
                }

                if (current == '/' && next == '/')
                {
                    result.Append("  ");
                    index++;
                    lineComment = true;
                }
                else if (current == '/' && next == '*')
                {
                    result.Append("  ");
                    index++;
                    blockComment = true;
                }
                else if (current == '@' && next == '"')
                {
                    result.Append(" \"");
                    index++;
                    verbatimString = true;
                }
                else if (current == '"')
                {
                    result.Append('"');
                    normalString = true;
                }
                else if (current == '\'')
                {
                    result.Append(' ');
                    characterLiteral = true;
                }
                else
                {
                    result.Append(current);
                }
            }

            Require(!blockComment &&
                    !normalString &&
                    !verbatimString &&
                    !characterLiteral,
                "The Chain source contains an unterminated comment or literal.");
            return result.ToString();
        }

        private static void VerifyCanonicalSceneSemanticIdentity()
        {
            UsvRootPoseN6DVerifier.RunBatch();
            UsvSurfaceVisualM2CVerifier.RunBatch();
            UsvActuatorVisualM2DVerifier.RunBatch();
        }

        private static void VerifyNoOpResult(
            UsvPostBuildInstallerChainResult result,
            string label,
            string expectedSceneSha,
            string measuredSceneSha)
        {
            Require(result.Success,
                "The " + label + " Chain run failed: " +
                result.FailureStage + " / " + result.FailureMessage);
            Require(
                !result.N6DChanged &&
                !result.M2CChanged &&
                !result.M2DChanged &&
                !result.AnyChanged &&
                !result.SceneSaved,
                "The " + label + " Chain run was not a complete no-op.");
            Require(
                string.Equals(
                    measuredSceneSha,
                    expectedSceneSha,
                    StringComparison.Ordinal),
                "The " + label + " Chain run did not preserve Scene SHA.");
            bool composableResultOmitsSha =
                string.IsNullOrEmpty(result.SceneShaBefore) &&
                string.IsNullOrEmpty(result.SceneShaAfter);
            bool wrappedResultBindsSha =
                string.Equals(
                    result.SceneShaBefore,
                    expectedSceneSha,
                    StringComparison.Ordinal) &&
                string.Equals(
                    result.SceneShaAfter,
                    expectedSceneSha,
                    StringComparison.Ordinal);
            Require(composableResultOmitsSha || wrappedResultBindsSha,
                "The " + label +
                " Chain result contains a partial or mismatched Scene SHA.");
            Require(
                result.InvocationOrder != null &&
                result.InvocationOrder.SequenceEqual(new[]
                {
                    typeof(UsvRootPoseN6DSceneInstaller).FullName +
                        "." +
                        nameof(UsvRootPoseN6DSceneInstaller
                            .InstallForCanonicalPostBuildChain),
                    typeof(UsvSurfaceVisualM2CSceneInstaller).FullName +
                        "." +
                        nameof(UsvSurfaceVisualM2CSceneInstaller
                            .InstallForCanonicalPostBuildChain),
                    typeof(UsvActuatorVisualM2DSceneInstaller).FullName +
                        "." +
                        nameof(UsvActuatorVisualM2DSceneInstaller
                            .InstallForCanonicalPostBuildChain)
                }),
                "The " + label + " Chain result reported the wrong order.");
        }

        private static int Count(string text, string token)
        {
            int count = 0;
            int offset = 0;
            while ((offset = text.IndexOf(
                       token,
                       offset,
                       StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += token.Length;
            }

            return count;
        }

        private static string GetAbsoluteScenePath()
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

        private static string GetCommandLineArgument(string name)
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

        private static string ResolveExpectedAuthoritativeSceneSha()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int argumentIndex = -1;
            for (int index = 0; index < arguments.Length; index++)
            {
                if (!string.Equals(
                        arguments[index],
                        ExpectedSceneShaArgument,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                Require(argumentIndex < 0,
                    "The expected authoritative Scene SHA argument was provided more than once.");
                argumentIndex = index;
            }

            if (argumentIndex < 0)
            {
                return HistoricalAuthoritativeSceneSha;
            }

            Require(
                argumentIndex + 1 < arguments.Length &&
                !arguments[argumentIndex + 1].StartsWith(
                    "-",
                    StringComparison.Ordinal),
                "The expected authoritative Scene SHA argument is missing its value.");
            string value = arguments[argumentIndex + 1];
            Require(IsSha256(value),
                "The expected authoritative Scene SHA must be exactly 64 hexadecimal characters.");
            return value.ToLowerInvariant();
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64)
            {
                return false;
            }

            foreach (char character in value)
            {
                bool hexadecimal =
                    character >= '0' && character <= '9' ||
                    character >= 'a' && character <= 'f' ||
                    character >= 'A' && character <= 'F';
                if (!hexadecimal)
                {
                    return false;
                }
            }

            return true;
        }

        private static void WriteReport(NoOpReport report)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            string reportPath = string.Empty;
            for (int index = 0; index + 1 < arguments.Length; index++)
            {
                if (string.Equals(
                        arguments[index],
                        "-usvPostBuildVerifierReportPath",
                        StringComparison.Ordinal))
                {
                    reportPath = arguments[index + 1];
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            string absolutePath = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllText(
                absolutePath,
                JsonUtility.ToJson(report, true));
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
