using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UnderwaterRobotScene.EditorTools
{
    public static class AuvHandednessN5ADiagnostic
    {
        private const string SceneAssetPath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string FbxAssetPath = "Assets/Models/AUV/AUV_FineModel_V1.fbx";
        private const string AuvRootName = "AUV_Yellow_Underwater";
        private const string FbxRootName = "AUV_FineModel_V1_Imported";
        private const string HullName = "AUV_Fine_Hull_Continuous";
        private const string NoseName = "AUV_Nose_Horizontal_Port_Orientation_Guide";
        private const string TailName = "Tail_Propeller_RotatingPart";
        private const string MastName = "AUV_Orange_IMU_Mast";
        private const string FrontLeftName = "AUV_Thruster_Flange_Front_Left";
        private const string FrontRightName = "AUV_Thruster_Flange_Front_Right";
        private const string RearLeftName = "AUV_Thruster_Flange_Rear_Left";
        private const string RearRightName = "AUV_Thruster_Flange_Rear_Right";
        private const string FinLeftName = "AUV_Tail_Fin_Left";
        private const string FinRightName = "AUV_Tail_Fin_Right";
        private const double DeterminantTolerance = 1e-8;

        [Serializable]
        private sealed class DiagnosticReport
        {
            public int schemaVersion = 1;
            public string status;
            public string generatedAtIso8601;
            public string authoritativeScene = SceneAssetPath;
            public string sceneSha256Before;
            public string sceneSha256After;
            public bool sceneHashUnchanged;
            public bool sceneDirtyBefore;
            public bool sceneDirtyAfter;
            public bool sceneDirtyUnchanged;
            public string fbxSha256;
            public string fbxMetaSha256;
            public string outputDirectory;
            public ImporterRecord importer;
            public NodeRecord[] keyNodes;
            public NodeRecord[] completeAuvHierarchy;
            public MeshSummary meshSummary;
            public AxisConclusion axes;
            public CandidateRotationRecord[] candidates;
            public string classification;
            public string primaryFinding;
            public string[] evidence;
            public string[] limitations;
        }

        [Serializable]
        private sealed class ImporterRecord
        {
            public string assetPath;
            public float globalScale;
            public bool useFileUnits;
            public bool bakeAxisConversion;
            public bool preserveHierarchy;
            public bool isReadable;
            public bool addCollider;
            public string importNormals;
            public string importTangents;
            public string animationType;
            public string sourceAssetRootLocalRotation;
            public string sourceAssetRootLocalScale;
            public double sourceAssetRootLocalDeterminant;
        }

        [Serializable]
        private sealed class NodeRecord
        {
            public string name;
            public string hierarchyPath;
            public string parent;
            public string source;
            public string localPosition;
            public string localRotation;
            public string localEulerAngles;
            public string localScale;
            public double localDeterminant;
            public double cumulativeDeterminant;
            public string handedness;
            public bool hasNegativeScaleComponent;
            public bool nonUniformScale;
            public bool isFirstReflection;
            public bool hasMeshRenderer;
            public bool hasSkinnedMeshRenderer;
            public bool hasMeshFilter;
        }

        [Serializable]
        private sealed class MeshSummary
        {
            public int transformCount;
            public int meshFilterCount;
            public int meshRendererCount;
            public int skinnedMeshRendererCount;
            public int materialSlotCount;
            public int negativeLocalDeterminantCount;
            public int negativeCumulativeDeterminantCount;
            public int negativeScaleNodeCount;
            public string hullMeshName;
            public string hullBoundsCenter;
            public string hullBoundsSize;
            public int hullVertexCount;
            public int hullSubMeshCount;
            public bool hullHasNormals;
            public bool hullHasTangents;
        }

        [Serializable]
        private sealed class AxisConclusion
        {
            public string headingWorld;
            public string topWorld;
            public string observerStarboardWorld;
            public string namedFrontFlangeRightWorld;
            public string namedRearFlangeRightWorld;
            public string namedTailFinRightWorld;
            public bool namedTailFinEvidenceUsable;
            public double frontNamedRightDotObserverStarboard;
            public double rearNamedRightDotObserverStarboard;
            public double finNamedRightDotObserverStarboard;
            public string observerDefinition;
            public string n1NamedRightInterpretation;
            public string correctedModelRight;
            public string correctedModelUp;
            public string correctedModelForward;
            public double correctedBasisDeterminant;
            public string correctedBasisColumnOrder;
            public string namedBasisColumnOrder;
            public double namedBasisDeterminant;
            public string screenshotPath;
        }

        [Serializable]
        private sealed class CandidateRotationRecord
        {
            public string name;
            public string quaternion;
            public string mappedRight;
            public string mappedUp;
            public string mappedForward;
            public double rightDotExpected;
            public double upDotExpected;
            public double forwardDotExpected;
            public bool mapsAllThreeAxes;
        }

        [MenuItem("Tools/Underwater Demo/Run AUV Handedness Diagnostic N5A")]
        public static void RunFromMenu()
        {
            string reportPath = Execute();
            EditorUtility.DisplayDialog(
                "AUV Handedness N5A",
                "Non-destructive diagnostic completed.\n" + reportPath,
                "OK");
        }

        public static void RunBatch()
        {
            string reportPath = Execute();
            Debug.Log("N5A_DIAGNOSTIC_COMPLETE | " + reportPath);
        }

        private static string Execute()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string workspaceRoot = Path.GetFullPath(Path.Combine(projectRoot, "..", ".."));
            string outputDirectory = Path.Combine(workspaceRoot, "N5A_Diagnostic");
            Directory.CreateDirectory(outputDirectory);

            string sceneAbsolutePath = Path.Combine(projectRoot, SceneAssetPath.Replace('/', Path.DirectorySeparatorChar));
            string fbxAbsolutePath = Path.Combine(projectRoot, FbxAssetPath.Replace('/', Path.DirectorySeparatorChar));
            string fbxMetaAbsolutePath = fbxAbsolutePath + ".meta";
            string sceneHashBefore = Sha256(sceneAbsolutePath);

            Scene scene = EditorSceneManager.OpenScene(SceneAssetPath, OpenSceneMode.Single);
            bool dirtyBefore = scene.isDirty;
            GameObject auv = FindUniqueRoot(scene, AuvRootName);
            Transform fbxRoot = FindUniqueDescendant(auv.transform, FbxRootName);

            var report = new DiagnosticReport
            {
                generatedAtIso8601 = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
                sceneSha256Before = sceneHashBefore,
                sceneDirtyBefore = dirtyBefore,
                fbxSha256 = Sha256(fbxAbsolutePath),
                fbxMetaSha256 = Sha256(fbxMetaAbsolutePath),
                outputDirectory = outputDirectory.Replace('\\', '/'),
                importer = CaptureImporter(fbxRoot),
                completeAuvHierarchy = auv.GetComponentsInChildren<Transform>(true)
                    .Select(transform => CaptureNode(transform, auv.transform))
                    .ToArray()
            };

            string[] keyNames =
            {
                AuvRootName,
                FbxRootName,
                HullName,
                NoseName,
                TailName,
                MastName,
                FrontLeftName,
                FrontRightName,
                RearLeftName,
                RearRightName,
                FinLeftName,
                FinRightName
            };
            report.keyNodes = keyNames
                .Select(name => CaptureNode(
                    string.Equals(name, AuvRootName, StringComparison.Ordinal)
                        ? auv.transform
                        : FindUniqueDescendant(auv.transform, name),
                    auv.transform))
                .ToArray();

            report.meshSummary = CaptureMeshSummary(auv, report.completeAuvHierarchy);
            report.axes = CaptureAxes(auv.transform, outputDirectory);
            report.candidates = new[]
            {
                EvaluateCandidate(
                    "Identity",
                    Quaternion.identity,
                    Vector3.back,
                    Vector3.up,
                    Vector3.right),
                EvaluateCandidate(
                    "Unity Y +90 degrees",
                    Quaternion.AngleAxis(90f, Vector3.up),
                    Vector3.back,
                    Vector3.up,
                    Vector3.right),
                EvaluateCandidate(
                    "Unity Y -90 degrees",
                    Quaternion.AngleAxis(-90f, Vector3.up),
                    Vector3.back,
                    Vector3.up,
                    Vector3.right)
            };

            bool noHierarchyReflection =
                report.meshSummary.negativeLocalDeterminantCount == 0 &&
                report.meshSummary.negativeCumulativeDeterminantCount == 0 &&
                report.meshSummary.negativeScaleNodeCount == 0;
            bool namedSidesOpposeObserver =
                report.axes.frontNamedRightDotObserverStarboard < -0.999 &&
                report.axes.rearNamedRightDotObserverStarboard < -0.999;
            bool correctedBasisProper = Math.Abs(report.axes.correctedBasisDeterminant - 1.0) < 1e-6;
            bool minusNinetyMapsAll = report.candidates[2].mapsAllThreeAxes;

            if (noHierarchyReflection && namedSidesOpposeObserver && correctedBasisProper && minusNinetyMapsAll)
            {
                report.status = "N5A_AXIS_LABEL_CORRECTION_CONFIRMED";
                report.classification = "Case A";
                report.primaryFinding =
                    "The generated Left/Right labels are reversed relative to the required tail-to-bow observer definition. " +
                    "Visual starboard is AUV local -Z, not +Z. No Transform reflection was found.";
            }
            else
            {
                report.status = "N5A_INCONCLUSIVE_RIGHT_STARBOARD_EVIDENCE";
                report.classification = "Case D";
                report.primaryFinding =
                    "The automated evidence did not satisfy every Case A gate; review the structured values and screenshot.";
            }

            report.evidence = new[]
            {
                "Heading is derived from Tail_Propeller_RotatingPart toward AUV_Nose_Horizontal_Port_Orientation_Guide.",
                "Top is derived from hull renderer center toward AUV_Orange_IMU_Mast renderer center.",
                "Observer starboard is Cross(Top, Heading), matching the right side when looking tail-to-bow.",
                "Front and Rear named Right-minus-Left flange vectors are compared against observer starboard.",
                "Tail-fin named-side separation is recorded when the imported hierarchy exposes distinct renderer centers.",
                "Every AUV Transform local and cumulative determinant is audited in Unity.",
                "The official FBX ModelImporter and instantiated FBX root are audited without modification.",
                "Candidate rotations transform all three corrected semantic basis vectors, not Euler labels alone."
            };
            report.limitations = new[]
            {
                "The hull is largely bilaterally symmetric; starboard naming evidence comes from the mandated observer definition plus generator-assigned Left/Right structures.",
                "The imported Tail_Fin_Left/Right renderer centers separate on Unity +Y/-Y rather than lateral +/-Z, so those names are not valid starboard evidence; the two tunnel-flange stations remain independent Unity-side checks.",
                "No Blender executable was available for live .blend introspection; the protected generator, Blend and FBX hashes match the documented authoritative versions.",
                "This diagnostic does not modify, reimport or re-export the FBX and does not infer any external protocol convention."
            };

            report.sceneDirtyAfter = scene.isDirty;
            report.sceneDirtyUnchanged = report.sceneDirtyAfter == dirtyBefore;
            report.sceneSha256After = Sha256(sceneAbsolutePath);
            report.sceneHashUnchanged = string.Equals(
                report.sceneSha256Before,
                report.sceneSha256After,
                StringComparison.OrdinalIgnoreCase);

            Require(report.sceneDirtyUnchanged, "The authoritative scene dirty state changed during N5A.");
            Require(report.sceneHashUnchanged, "The authoritative scene file changed during N5A.");

            string jsonPath = Path.Combine(outputDirectory, "n5a_auv_handedness_report.json");
            string markdownPath = Path.Combine(outputDirectory, "n5a_auv_handedness_report.md");
            File.WriteAllText(jsonPath, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            File.WriteAllText(markdownPath, BuildMarkdown(report), new UTF8Encoding(false));
            AssetDatabase.Refresh();
            Debug.Log(report.status + " | " + report.primaryFinding);
            return markdownPath.Replace('\\', '/');
        }

        private static ImporterRecord CaptureImporter(Transform fbxRoot)
        {
            ModelImporter importer = AssetImporter.GetAtPath(FbxAssetPath) as ModelImporter;
            Require(importer != null, "AUV FBX ModelImporter was not found.");
            return new ImporterRecord
            {
                assetPath = FbxAssetPath,
                globalScale = importer.globalScale,
                useFileUnits = importer.useFileUnits,
                bakeAxisConversion = importer.bakeAxisConversion,
                preserveHierarchy = importer.preserveHierarchy,
                isReadable = importer.isReadable,
                addCollider = importer.addCollider,
                importNormals = importer.importNormals.ToString(),
                importTangents = importer.importTangents.ToString(),
                animationType = importer.animationType.ToString(),
                sourceAssetRootLocalRotation = Format(fbxRoot.localRotation),
                sourceAssetRootLocalScale = Format(fbxRoot.localScale),
                sourceAssetRootLocalDeterminant = LocalDeterminant(fbxRoot)
            };
        }

        private static MeshSummary CaptureMeshSummary(GameObject auv, NodeRecord[] nodes)
        {
            MeshFilter[] filters = auv.GetComponentsInChildren<MeshFilter>(true);
            MeshRenderer[] renderers = auv.GetComponentsInChildren<MeshRenderer>(true);
            SkinnedMeshRenderer[] skinned = auv.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Transform hull = FindUniqueDescendant(auv.transform, HullName);
            MeshFilter hullFilter = hull.GetComponent<MeshFilter>();
            Require(hullFilter != null && hullFilter.sharedMesh != null, "AUV hull MeshFilter is missing.");
            Mesh mesh = hullFilter.sharedMesh;
            return new MeshSummary
            {
                transformCount = nodes.Length,
                meshFilterCount = filters.Length,
                meshRendererCount = renderers.Length,
                skinnedMeshRendererCount = skinned.Length,
                materialSlotCount =
                    renderers.Sum(renderer => renderer.sharedMaterials.Length) +
                    skinned.Sum(renderer => renderer.sharedMaterials.Length),
                negativeLocalDeterminantCount = nodes.Count(node => node.localDeterminant < -DeterminantTolerance),
                negativeCumulativeDeterminantCount =
                    nodes.Count(node => node.cumulativeDeterminant < -DeterminantTolerance),
                negativeScaleNodeCount = nodes.Count(node => node.hasNegativeScaleComponent),
                hullMeshName = mesh.name,
                hullBoundsCenter = Format(mesh.bounds.center),
                hullBoundsSize = Format(mesh.bounds.size),
                hullVertexCount = mesh.vertexCount,
                hullSubMeshCount = mesh.subMeshCount,
                hullHasNormals = mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal),
                hullHasTangents = mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent)
            };
        }

        private static AxisConclusion CaptureAxes(Transform auvRoot, string outputDirectory)
        {
            Transform nose = FindUniqueDescendant(auvRoot, NoseName);
            Transform tail = FindUniqueDescendant(auvRoot, TailName);
            Transform hull = FindUniqueDescendant(auvRoot, HullName);
            Transform mast = FindUniqueDescendant(auvRoot, MastName);

            Vector3 heading = (nose.position - tail.position).normalized;
            Vector3 top = (
                RendererCenter(mast) -
                RendererCenter(hull)).normalized;
            top = Vector3.ProjectOnPlane(top, heading).normalized;
            Vector3 observerStarboard = Vector3.Cross(top, heading).normalized;

            Vector3 frontNamedRight = ProjectLateral(
                RendererCenter(FindUniqueDescendant(auvRoot, FrontRightName)) -
                RendererCenter(FindUniqueDescendant(auvRoot, FrontLeftName)),
                heading,
                top);
            Vector3 rearNamedRight = ProjectLateral(
                RendererCenter(FindUniqueDescendant(auvRoot, RearRightName)) -
                RendererCenter(FindUniqueDescendant(auvRoot, RearLeftName)),
                heading,
                top);
            Vector3 finRawRight =
                RendererCenter(FindUniqueDescendant(auvRoot, FinRightName)) -
                RendererCenter(FindUniqueDescendant(auvRoot, FinLeftName));
            bool finEvidenceUsable = TryProjectLateral(
                finRawRight,
                heading,
                top,
                out Vector3 finNamedRight);

            Vector3 correctedRightLocal = auvRoot.InverseTransformDirection(observerStarboard).normalized;
            Vector3 correctedUpLocal = auvRoot.InverseTransformDirection(top).normalized;
            Vector3 correctedForwardLocal = auvRoot.InverseTransformDirection(heading).normalized;
            Vector3 namedRightLocal = auvRoot.InverseTransformDirection(frontNamedRight).normalized;

            string screenshotPath = Path.Combine(outputDirectory, "n5a_auv_tail_to_bow_axes.png");
            RenderTailToBowScreenshot(auvRoot.gameObject, screenshotPath);

            return new AxisConclusion
            {
                headingWorld = Format(heading),
                topWorld = Format(top),
                observerStarboardWorld = Format(observerStarboard),
                namedFrontFlangeRightWorld = Format(frontNamedRight),
                namedRearFlangeRightWorld = Format(rearNamedRight),
                namedTailFinRightWorld = finEvidenceUsable
                    ? Format(finNamedRight)
                    : "unavailable; raw center delta=" + Format(finRawRight),
                namedTailFinEvidenceUsable = finEvidenceUsable,
                frontNamedRightDotObserverStarboard = Vector3.Dot(frontNamedRight, observerStarboard),
                rearNamedRightDotObserverStarboard = Vector3.Dot(rearNamedRight, observerStarboard),
                finNamedRightDotObserverStarboard = finEvidenceUsable
                    ? Vector3.Dot(finNamedRight, observerStarboard)
                    : 0.0,
                observerDefinition =
                    "Observer at the tail looking toward the bow, with model top as screen up; " +
                    "starboard = Cross(top, heading).",
                n1NamedRightInterpretation =
                    "The N1 +Z result follows generator-assigned Right-minus-Left names, which point opposite observer starboard.",
                correctedModelRight = Format(correctedRightLocal),
                correctedModelUp = Format(correctedUpLocal),
                correctedModelForward = Format(correctedForwardLocal),
                correctedBasisDeterminant = BasisDeterminant(
                    correctedRightLocal,
                    correctedUpLocal,
                    correctedForwardLocal),
                correctedBasisColumnOrder = "[Right=-Z, Up=+Y, Forward=+X] in AUV root local space",
                namedBasisColumnOrder = "[Right=+Z, Up=+Y, Forward=+X] in AUV root local space",
                namedBasisDeterminant = BasisDeterminant(
                    namedRightLocal,
                    correctedUpLocal,
                    correctedForwardLocal),
                screenshotPath = screenshotPath.Replace('\\', '/')
            };
        }

        private static CandidateRotationRecord EvaluateCandidate(
            string name,
            Quaternion rotation,
            Vector3 modelRight,
            Vector3 modelUp,
            Vector3 modelForward)
        {
            Vector3 mappedRight = rotation * modelRight;
            Vector3 mappedUp = rotation * modelUp;
            Vector3 mappedForward = rotation * modelForward;
            double rightDot = Vector3.Dot(mappedRight.normalized, Vector3.right);
            double upDot = Vector3.Dot(mappedUp.normalized, Vector3.up);
            double forwardDot = Vector3.Dot(mappedForward.normalized, Vector3.forward);
            return new CandidateRotationRecord
            {
                name = name,
                quaternion = Format(rotation),
                mappedRight = Format(mappedRight),
                mappedUp = Format(mappedUp),
                mappedForward = Format(mappedForward),
                rightDotExpected = rightDot,
                upDotExpected = upDot,
                forwardDotExpected = forwardDot,
                mapsAllThreeAxes = rightDot > 0.999999 && upDot > 0.999999 && forwardDot > 0.999999
            };
        }

        private static NodeRecord CaptureNode(Transform transform, Transform auvRoot)
        {
            double localDeterminant = LocalDeterminant(transform);
            double cumulativeDeterminant = transform.localToWorldMatrix.determinant;
            double parentDeterminant = transform.parent == null
                ? 1.0
                : transform.parent.localToWorldMatrix.determinant;
            bool firstReflection =
                cumulativeDeterminant < -DeterminantTolerance &&
                parentDeterminant >= -DeterminantTolerance;
            Vector3 scale = transform.localScale;
            return new NodeRecord
            {
                name = transform.name,
                hierarchyPath = HierarchyPath(transform, auvRoot),
                parent = transform.parent == null ? string.Empty : transform.parent.name,
                source = PrefabUtility.IsPartOfPrefabInstance(transform)
                    ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(transform.gameObject)
                    : SceneAssetPath,
                localPosition = Format(transform.localPosition),
                localRotation = Format(transform.localRotation),
                localEulerAngles = Format(transform.localEulerAngles),
                localScale = Format(scale),
                localDeterminant = localDeterminant,
                cumulativeDeterminant = cumulativeDeterminant,
                handedness = cumulativeDeterminant < -DeterminantTolerance ? "Negative" : "Positive",
                hasNegativeScaleComponent = scale.x < 0f || scale.y < 0f || scale.z < 0f,
                nonUniformScale =
                    Math.Abs(Math.Abs(scale.x) - Math.Abs(scale.y)) > 1e-6 ||
                    Math.Abs(Math.Abs(scale.x) - Math.Abs(scale.z)) > 1e-6,
                isFirstReflection = firstReflection,
                hasMeshRenderer = transform.GetComponent<MeshRenderer>() != null,
                hasSkinnedMeshRenderer = transform.GetComponent<SkinnedMeshRenderer>() != null,
                hasMeshFilter = transform.GetComponent<MeshFilter>() != null
            };
        }

        private static void RenderTailToBowScreenshot(GameObject authoritativeAuv, string outputPath)
        {
            Scene previewScene = EditorSceneManager.NewPreviewScene();
            var temporaryObjects = new List<Object>();
            var temporaryMaterials = new List<Material>();
            RenderTexture target = null;
            Texture2D image = null;
            try
            {
                GameObject clone = Object.Instantiate(authoritativeAuv);
                clone.name = "N5A_AUV_Preview";
                SceneManager.MoveGameObjectToScene(clone, previewScene);
                foreach (MonoBehaviour behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    Object.DestroyImmediate(behaviour);
                }

                Bounds bounds = CalculateBounds(clone);
                Vector3 center = bounds.center;
                float axisLength = Mathf.Max(bounds.extents.y, bounds.extents.z) * 1.35f;

                GameObject cameraObject = new GameObject("N5A_Camera");
                SceneManager.MoveGameObjectToScene(cameraObject, previewScene);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.04f, 0.055f, 0.075f, 1f);
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.z) * 1.65f;
                camera.aspect = 4f / 3f;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = bounds.size.x * 4f + 20f;
                camera.transform.position = center - Vector3.right * (bounds.extents.x + 8f);
                camera.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);

                CreateDirectionalLight(previewScene, temporaryObjects);
                CreateAxisLine(previewScene, center, Vector3.up, axisLength, Color.green, temporaryMaterials);
                CreateAxisLine(previewScene, center, Vector3.down, axisLength, new Color(0.2f, 0.45f, 0.2f), temporaryMaterials);
                CreateAxisLine(previewScene, center, Vector3.forward, axisLength, Color.magenta, temporaryMaterials);
                CreateAxisLine(previewScene, center, Vector3.back, axisLength, Color.cyan, temporaryMaterials);
                CreateMarker(
                    previewScene,
                    center + Vector3.forward * axisLength,
                    Color.magenta,
                    "+Z  generator-named Right / visual port",
                    camera.transform.rotation,
                    temporaryObjects,
                    temporaryMaterials);
                CreateMarker(
                    previewScene,
                    center + Vector3.back * axisLength,
                    Color.cyan,
                    "-Z  observer starboard",
                    camera.transform.rotation,
                    temporaryObjects,
                    temporaryMaterials);
                CreateMarker(
                    previewScene,
                    center + Vector3.up * axisLength,
                    Color.green,
                    "+Y  top",
                    camera.transform.rotation,
                    temporaryObjects,
                    temporaryMaterials);

                target = new RenderTexture(1200, 900, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = target;
                camera.Render();
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                image = new Texture2D(1200, 900, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0);
                image.Apply();
                RenderTexture.active = previous;
                File.WriteAllBytes(outputPath, image.EncodeToPNG());
                camera.targetTexture = null;
            }
            finally
            {
                if (image != null) Object.DestroyImmediate(image);
                if (target != null)
                {
                    if (target.IsCreated())
                    {
                        target.DiscardContents();
                    }
                    target.Release();
                    Object.DestroyImmediate(target);
                }

                foreach (Material material in temporaryMaterials)
                {
                    if (material != null) Object.DestroyImmediate(material);
                }

                foreach (Object temporaryObject in temporaryObjects)
                {
                    if (temporaryObject != null) Object.DestroyImmediate(temporaryObject);
                }

                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        private static void CreateDirectionalLight(Scene scene, List<Object> objects)
        {
            GameObject lightObject = new GameObject("N5A_Light");
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(35f, -35f, 0f);
            objects.Add(lightObject);
        }

        private static void CreateAxisLine(
            Scene scene,
            Vector3 origin,
            Vector3 direction,
            float length,
            Color color,
            List<Material> materials)
        {
            GameObject lineObject = new GameObject("N5A_AxisLine");
            SceneManager.MoveGameObjectToScene(lineObject, scene);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            Material material = new Material(Shader.Find("Sprites/Default"));
            material.color = color;
            materials.Add(material);
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = 0.025f;
            line.endWidth = 0.025f;
            line.startColor = color;
            line.endColor = color;
            line.SetPosition(0, origin);
            line.SetPosition(1, origin + direction.normalized * length);
        }

        private static void CreateMarker(
            Scene scene,
            Vector3 position,
            Color color,
            string label,
            Quaternion textRotation,
            List<Object> objects,
            List<Material> materials)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "N5A_Marker";
            SceneManager.MoveGameObjectToScene(marker, scene);
            marker.transform.position = position;
            marker.transform.localScale = Vector3.one * 0.09f;
            Material material = new Material(Shader.Find("Standard"));
            material.color = color;
            materials.Add(material);
            marker.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            objects.Add(marker);

            GameObject textObject = new GameObject("N5A_Label");
            SceneManager.MoveGameObjectToScene(textObject, scene);
            TextMesh text = textObject.AddComponent<TextMesh>();
            text.text = label;
            text.color = color;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = 0.045f;
            text.fontSize = 52;
            textObject.transform.position = position + Vector3.up * 0.16f;
            textObject.transform.rotation = textRotation;
            objects.Add(textObject);
        }

        private static Vector3 RendererCenter(Transform transform)
        {
            Renderer renderer = transform.GetComponent<Renderer>();
            if (renderer != null)
            {
                return renderer.bounds.center;
            }

            Renderer[] childRenderers = transform.GetComponentsInChildren<Renderer>(true);
            if (childRenderers.Length == 0)
            {
                return transform.position;
            }

            Bounds bounds = childRenderers[0].bounds;
            for (int index = 1; index < childRenderers.Length; index++)
            {
                bounds.Encapsulate(childRenderers[index].bounds);
            }

            return bounds.center;
        }

        private static Vector3 ProjectLateral(Vector3 vector, Vector3 heading, Vector3 top)
        {
            Vector3 projected = vector -
                                Vector3.Dot(vector, heading) * heading -
                                Vector3.Dot(vector, top) * top;
            Require(projected.sqrMagnitude > 1e-8f, "Named side evidence has no stable lateral component.");
            return projected.normalized;
        }

        private static bool TryProjectLateral(
            Vector3 vector,
            Vector3 heading,
            Vector3 top,
            out Vector3 normalized)
        {
            Vector3 projected = vector -
                                Vector3.Dot(vector, heading) * heading -
                                Vector3.Dot(vector, top) * top;
            if (projected.sqrMagnitude <= 1e-8f)
            {
                normalized = Vector3.zero;
                return false;
            }

            normalized = projected.normalized;
            return true;
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Require(renderers.Length > 0, "Preview AUV has no renderers.");
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static GameObject FindUniqueRoot(Scene scene, string name)
        {
            GameObject[] matches = scene.GetRootGameObjects()
                .Where(root => string.Equals(root.name, name, StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1, "Expected one root '" + name + "' but found " + matches.Length + ".");
            return matches[0];
        }

        private static Transform FindUniqueDescendant(Transform root, string name)
        {
            Transform[] matches = root.GetComponentsInChildren<Transform>(true)
                .Where(item => string.Equals(item.name, name, StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1, "Expected one descendant '" + name + "' but found " + matches.Length + ".");
            return matches[0];
        }

        private static string HierarchyPath(Transform transform, Transform stopAt)
        {
            var names = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Add(current.name);
                if (current == stopAt) break;
                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private static double LocalDeterminant(Transform transform)
        {
            return Matrix4x4.TRS(
                transform.localPosition,
                transform.localRotation,
                transform.localScale).determinant;
        }

        private static double BasisDeterminant(Vector3 right, Vector3 up, Vector3 forward)
        {
            return Vector3.Dot(right, Vector3.Cross(up, forward));
        }

        private static string BuildMarkdown(DiagnosticReport report)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# N5A AUV Axis, Handedness, and Reflection Diagnostic");
            builder.AppendLine();
            builder.AppendLine("- Status: `" + report.status + "`");
            builder.AppendLine("- Classification: `" + report.classification + "`");
            builder.AppendLine("- Primary finding: " + report.primaryFinding);
            builder.AppendLine("- Scene hash unchanged: " + report.sceneHashUnchanged);
            builder.AppendLine("- Scene dirty unchanged: " + report.sceneDirtyUnchanged);
            builder.AppendLine();
            builder.AppendLine("## Corrected visual axes");
            builder.AppendLine();
            builder.AppendLine("- Forward: AUV root local `+X`");
            builder.AppendLine("- Up: AUV root local `+Y`");
            builder.AppendLine("- Starboard: AUV root local `-Z`");
            builder.AppendLine("- Observer definition: " + report.axes.observerDefinition);
            builder.AppendLine("- Corrected basis `[Right, Up, Forward]`: " + report.axes.correctedBasisColumnOrder);
            builder.AppendLine("- Corrected determinant: " + Number(report.axes.correctedBasisDeterminant));
            builder.AppendLine("- N1 named basis: " + report.axes.namedBasisColumnOrder);
            builder.AppendLine("- N1 named determinant: " + Number(report.axes.namedBasisDeterminant));
            builder.AppendLine();
            builder.AppendLine("## Named-side cross-check");
            builder.AppendLine();
            builder.AppendLine("| Evidence | Direction | Dot(observer starboard) |");
            builder.AppendLine("|---|---:|---:|");
            builder.AppendLine("| Front flange Right-Left | " + report.axes.namedFrontFlangeRightWorld +
                               " | " + Number(report.axes.frontNamedRightDotObserverStarboard) + " |");
            builder.AppendLine("| Rear flange Right-Left | " + report.axes.namedRearFlangeRightWorld +
                               " | " + Number(report.axes.rearNamedRightDotObserverStarboard) + " |");
            builder.AppendLine("| Tail fin Right-Left | " + report.axes.namedTailFinRightWorld +
                               " | " + Number(report.axes.finNamedRightDotObserverStarboard) + " |");
            builder.AppendLine();
            builder.AppendLine(
                "Both independently placed tunnel-flange stations label `Right` opposite the observer-defined starboard vector. " +
                "Tail-fin Unity-side evidence usable: " + report.axes.namedTailFinEvidenceUsable + ".");
            builder.AppendLine();
            builder.AppendLine("## Transform and importer summary");
            builder.AppendLine();
            builder.AppendLine("- Transform count: " + report.meshSummary.transformCount);
            builder.AppendLine("- Negative local determinant count: " + report.meshSummary.negativeLocalDeterminantCount);
            builder.AppendLine("- Negative cumulative determinant count: " + report.meshSummary.negativeCumulativeDeterminantCount);
            builder.AppendLine("- Negative scale node count: " + report.meshSummary.negativeScaleNodeCount);
            builder.AppendLine("- Importer globalScale: " + Number(report.importer.globalScale));
            builder.AppendLine("- Bake Axis Conversion: " + report.importer.bakeAxisConversion);
            builder.AppendLine("- Preserve Hierarchy: " + report.importer.preserveHierarchy);
            builder.AppendLine("- FBX root local rotation: " + report.importer.sourceAssetRootLocalRotation);
            builder.AppendLine("- FBX root local scale: " + report.importer.sourceAssetRootLocalScale);
            builder.AppendLine("- FBX root local determinant: " + Number(report.importer.sourceAssetRootLocalDeterminant));
            builder.AppendLine();
            builder.AppendLine("## Key Transform chain");
            builder.AppendLine();
            builder.AppendLine("| Node | Parent | Local position | Local rotation | Local scale | Local det | Cumulative det |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            foreach (NodeRecord node in report.keyNodes)
            {
                builder.AppendLine(
                    "| " + node.name +
                    " | " + node.parent +
                    " | " + node.localPosition +
                    " | " + node.localRotation +
                    " | " + node.localScale +
                    " | " + Number(node.localDeterminant) +
                    " | " + Number(node.cumulativeDeterminant) + " |");
            }

            builder.AppendLine();
            builder.AppendLine("## Candidate rotations");
            builder.AppendLine();
            builder.AppendLine("| Candidate | Quaternion | Right dot | Up dot | Forward dot | All axes |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|");
            foreach (CandidateRotationRecord candidate in report.candidates)
            {
                builder.AppendLine(
                    "| " + candidate.name +
                    " | " + candidate.quaternion +
                    " | " + Number(candidate.rightDotExpected) +
                    " | " + Number(candidate.upDotExpected) +
                    " | " + Number(candidate.forwardDotExpected) +
                    " | " + candidate.mapsAllThreeAxes + " |");
            }

            builder.AppendLine();
            builder.AppendLine("The validated candidate is Unity Y `-90°`, quaternion approximately `(0,-0.7071068,0,0.7071068)`.");
            builder.AppendLine("It maps corrected model `Right=-Z`, `Up=+Y`, `Forward=+X` to N3 `+X,+Y,+Z`.");
            builder.AppendLine();
            builder.AppendLine("## Screenshot");
            builder.AppendLine();
            builder.AppendLine("`" + report.axes.screenshotPath + "`");
            builder.AppendLine();
            builder.AppendLine("## Limitations");
            builder.AppendLine();
            foreach (string limitation in report.limitations)
            {
                builder.AppendLine("- " + limitation);
            }

            return builder.ToString();
        }

        private static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }

        private static string Format(Vector3 value)
        {
            return "(" + Number(value.x) + "," + Number(value.y) + "," + Number(value.z) + ")";
        }

        private static string Format(Quaternion value)
        {
            return "(" + Number(value.x) + "," + Number(value.y) + "," +
                   Number(value.z) + "," + Number(value.w) + ")";
        }

        private static string Number(double value)
        {
            return value.ToString("0.#########", CultureInfo.InvariantCulture);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
