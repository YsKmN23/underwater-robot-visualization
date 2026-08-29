using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Usv;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    public static class UsvSurfaceVisualM2CVerifier
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string UsvName = "USV_Blue_Surface";
        private const string VisualRootName = "USV_SurfaceVisualRoot";
        private const string ModelName = "USV_FineModel_V1_Imported";
        private const string WaterName = "Water_Surface";
        private const string DriverName = "USV_PublicPoseDriver";
        private const ulong UsvTransformFileId = 735866181UL;
        private const ulong ModelPrefabInstanceFileId = 748965566UL;
        private const ulong ModelStrippedTransformFileId = 748965567UL;
        private const ulong WaterTransformFileId = 59938803UL;
        private const string ModelAssetGuid = "a54e024e9e2694149b630b36a3886faf";
        private const string RuntimeDirectory =
            "Assets/Scripts/Visualization/Runtime/Usv/";

        private sealed class Check
        {
            public string Name;
            public string Detail;
        }

        [MenuItem("Tools/Underwater Demo/M2-C1/Verify USV Surface Visual")]
        public static void RunFromMenu()
        {
            Execute();
        }

        public static void RunBatch()
        {
            Execute();
            Debug.Log("M2C1_USV_SURFACE_VISUAL_STATIC_VALIDATION_PASS");
        }

        public static void RunFoundationBatch()
        {
            var checks = new List<Check>();
            VerifyTypeContracts(checks);
            VerifyPureMotion(checks);
            VerifySourceBoundaries(checks);
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var surface = new GameObject("M2C1_FlatSurfacePreview");
                SceneManager.MoveGameObjectToScene(surface, preview);
                FlatWaterSurfaceProvider provider =
                    surface.AddComponent<FlatWaterSurfaceProvider>();
                VerifyProvider(provider, checks);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
            foreach (Check check in checks)
            {
                Debug.Log(
                    "M2C1_FOUNDATION_CHECK_PASS | " +
                    check.Name +
                    " | " +
                    check.Detail);
            }
            Debug.Log("M2C1_USV_SURFACE_VISUAL_FOUNDATION_VALIDATION_PASS");
        }

        private static void Execute()
        {
            var checks = new List<Check>();
            VerifyTypeContracts(checks);
            VerifyPureMotion(checks);
            VerifySourceBoundaries(checks);

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Require(scene.IsValid() && scene.isLoaded && !scene.isDirty,
                "The authoritative M2-C1 scene is invalid, unloaded, or dirty.");
            GameObject usv = FindUniqueRoot(scene, UsvName);
            GameObject water = FindUniqueRoot(scene, WaterName);
            GameObject driverObject = FindUniqueRoot(scene, DriverName);
            Transform visualRoot = RequireUniqueDescendant(usv.transform, VisualRootName);
            Transform model = RequireUniqueDescendant(usv.transform, ModelName);

            Require(FileId(usv.transform) == UsvTransformFileId &&
                    usv.transform.childCount == 1 &&
                    visualRoot.parent == usv.transform &&
                    IsIdentity(visualRoot) &&
                    visualRoot.childCount == 1 &&
                    model.parent == visualRoot,
                "USV business/visual/model hierarchy is not canonical.");
            Add(checks, "Canonical visual hierarchy",
                "Business root fileID 735866181 has one identity visual root and one imported model child.");

            GlobalObjectId modelId = GlobalObjectId.GetGlobalObjectIdSlow(model);
            Require(modelId.targetPrefabId == ModelPrefabInstanceFileId,
                "Imported USV model PrefabInstance fileID changed. Expected " +
                ModelPrefabInstanceFileId + ", actual " + modelId.targetPrefabId + ".");
            Require(Near(model.localPosition, Vector3.zero, 0.000001f),
                "Imported USV model localPosition changed. Actual " +
                model.localPosition + ".");
            Require(Near(model.localRotation, Quaternion.Euler(-90f, 180f, 0f), 0.000001f),
                "Imported USV model localRotation changed. Actual " +
                model.localRotation + ".");
            Require(Near(model.localScale, Vector3.one * 100f, 0.00001f),
                "Imported USV model localScale changed. Actual " +
                model.localScale + ".");
            UnityEngine.Object source = PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject);
            Require(source != null,
                "Imported USV model no longer resolves to a source asset.");
            string sourcePath = AssetDatabase.GetAssetPath(source);
            Require(!string.IsNullOrEmpty(sourcePath),
                "Imported USV model source asset path is empty.");
            string sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);
            Require(sourceGuid == ModelAssetGuid,
                "Imported USV model source asset GUID changed. Expected " +
                ModelAssetGuid + ", actual " + sourceGuid + ".");
            string sceneYaml = File.ReadAllText(ScenePath);
            Require(sceneYaml.Contains(
                    "--- !u!4 &" + ModelStrippedTransformFileId + " stripped"),
                "Imported model stripped Transform YAML fileID changed. Expected " +
                ModelStrippedTransformFileId + ".");
            Require(sceneYaml.Contains(
                    "m_PrefabInstance: {fileID: " + ModelPrefabInstanceFileId + "}"),
                "Imported model PrefabInstance YAML fileID changed. Expected " +
                ModelPrefabInstanceFileId + ".");
            Add(checks, "Imported model preservation",
                "Stripped Transform 748965567, PrefabInstance 748965566, local pose/scale and FBX GUID are unchanged.");

            Require(FileId(water.transform) == WaterTransformFileId,
                "Water_Surface Transform fileID changed. Expected " +
                WaterTransformFileId + ", actual " + FileId(water.transform) + ".");
            Require(Near(water.transform.position, Vector3.zero, 0.000001f),
                "Water_Surface position changed. Actual " +
                water.transform.position + ".");
            Require(Near(water.transform.rotation, Quaternion.identity, 0.000001f),
                "Water_Surface rotation changed. Actual " +
                water.transform.rotation + ".");
            Require(Near(
                    water.transform.localScale,
                    new Vector3(112f, 0.02f, 84f),
                    0.000001f),
                "Water_Surface Phase-2B scale changed. Actual " +
                water.transform.localScale + ".");
            BoxCollider waterCollider = RequireComponent<BoxCollider>(water, "Water collider");
            MeshRenderer waterRenderer =
                RequireComponent<MeshRenderer>(water, "Water renderer");
            Require(waterCollider.enabled,
                "Water_Surface BoxCollider is disabled.");
            Require(Near(waterCollider.center, Vector3.zero, 0.000001f),
                "Water_Surface BoxCollider center changed. Actual " +
                waterCollider.center + ".");
            Require(Near(waterCollider.size, Vector3.one, 0.000001f),
                "Water_Surface BoxCollider size changed. Actual " +
                waterCollider.size + ".");
            Require(waterRenderer.sharedMaterial != null,
                "Water_Surface Material binding is missing.");
            Add(checks, "Water asset preservation",
                "Transform fileID 59938803, Phase-2B scale (112, 0.02, 84), thin cube Collider and assigned Material are unchanged.");

            FlatWaterSurfaceProvider[] providers =
                FindSceneComponents<FlatWaterSurfaceProvider>(scene);
            Require(providers.Length == 1 && providers[0].gameObject == water,
                "Expected one FlatWaterSurfaceProvider on Water_Surface.");
            VerifyProvider(providers[0], checks);

            UsvSurfaceVisualController[] controllers =
                FindSceneComponents<UsvSurfaceVisualController>(scene);
            Require(controllers.Length == 1 && controllers[0].transform == visualRoot,
                "Expected one UsvSurfaceVisualController on the visual root.");
            UsvSurfaceVisualController controller = controllers[0];
            VehiclePoseControlAuthority authority =
                RequireComponent<VehiclePoseControlAuthority>(usv, "USV Authority");
            VehiclePoseDriver driver =
                RequireComponent<VehiclePoseDriver>(driverObject, "USV Driver");
            Require(controller.BusinessRoot == usv.transform &&
                    controller.ImportedModelRoot == model &&
                    controller.WaterSurfaceProvider == providers[0] &&
                    controller.PoseDriver == driver &&
                    controller.ControlAuthority == authority &&
                    driver.TargetRoot == usv.transform &&
                    driver.ControlAuthority == authority,
                "M2-C1 Controller, Driver, Provider or Authority binding is incorrect.");
            Require(controller.Mode == UsvSurfaceVisualMode.LocalDiagnosticPublicData &&
                    Near(controller.DiagnosticNeutralRootHeightAboveSurface, 0.18f) &&
                    Near(controller.MaxSurfaceCorrection, 0.05f) &&
                    Near(controller.DiagnosticPeriodSeconds, 8f) &&
                    Near(controller.HeaveAmplitudeMeters, 0.015f) &&
                    Near(controller.PitchAmplitudeDegrees, 0.8f) &&
                    Near(controller.RollAmplitudeDegrees, 1.2f) &&
                    Near(controller.ActivationFadeSeconds, 0.75f) &&
                    Near(controller.TeleportDistanceMeters, 0.25f) &&
                    Near(controller.TeleportAngleDegrees, 30f) &&
                    Near(controller.MaxAcceptedDeltaTimeSeconds, 0.25f),
                "M2-C1 serialized diagnostic values differ from the approved contract.");
            Add(checks, "Explicit bindings and frozen values",
                "Scene explicitly enables LocalDiagnosticPublicData with 0.18 m calibration and approved motion/reset limits.");

            Require(visualRoot.GetComponents<Component>().All(component =>
                    component is Transform ||
                    component is UsvSurfaceVisualController),
                "Visual root contains another component or Transform writer.");
            Require(usv.GetComponents<Component>().All(component =>
                    component is Transform ||
                    component is VehiclePoseControlAuthority ||
                    component is UsvActuatorVisualCoordinator),
                "Business root contains an unapproved component/writer.");
            Require(model.GetComponent<UsvSurfaceVisualController>() == null,
                "Imported model root received a visual Controller.");
            Require(typeof(VehiclePoseDriver).GetProperty("HasFreshAppliedPose") != null &&
                    typeof(VehiclePoseDriver).GetProperty("LastAppliedSourceEpoch") != null,
                "Driver minimum fresh/epoch observation contract is missing.");
            Add(checks, "Single-writer boundary",
                "Only the Controller writes the visual root; business and imported roots retain historical ownership.");

            PropellerSpinner[] spinners = usv.GetComponentsInChildren<PropellerSpinner>(true);
            Require(spinners.Length == 2 &&
                    spinners.All(item =>
                        item.enabled &&
                        item.transform.IsChildOf(model) &&
                        Near(item.localAxis.normalized, Vector3.right, 0.000001f) &&
                        Near(item.rpm, 740f)),
                "USV propeller Spinner contract changed.");
            Transform rudder = RequireUniqueDescendant(usv.transform, "USV_Rudder_Main");
            Transform pivot =
                RequireUniqueDescendant(usv.transform, "USV_Rudder_VisualPivot");
            Require(rudder.GetComponent<PropellerSpinner>() == null &&
                    rudder.GetComponent<Animator>() == null &&
                    pivot.parent == rudder,
                "USV main rudder received an unintended writer.");
            Add(checks, "Actuator boundary",
                "Two +X Spinners retain serialized 740 rpm; Main/fixed remain static and the approved Pivot may be M2-D controlled.");

            foreach (string siblingName in new[]
                     {
                         "AUV_Yellow_Underwater",
                         "ROV_Box_Seabed"
                     })
            {
                GameObject sibling = FindUniqueRoot(scene, siblingName);
                Require(sibling.GetComponentInChildren<UsvSurfaceVisualController>(true) == null &&
                        sibling.GetComponentInChildren<FlatWaterSurfaceProvider>(true) == null,
                    siblingName + " received an M2-C1 component.");
            }
            Add(checks, "Three-vehicle isolation",
                "No M2-C1 component is attached to AUV or ROV.");

            Require(!HasMissingScripts(scene), "Scene contains a Missing Script.");
            Require(!scene.isDirty, "Static M2-C1 verification dirtied the Scene.");
            Add(checks, "Scene integrity", "No Missing Script; verification left the Scene clean.");
            WriteReport(checks, scene);
            foreach (Check check in checks)
            {
                Debug.Log("M2C1_STATIC_CHECK_PASS | " + check.Name + " | " + check.Detail);
            }
        }

        private static void VerifyTypeContracts(ICollection<Check> checks)
        {
            Type controllerType = typeof(UsvSurfaceVisualController);
            DefaultExecutionOrder order =
                controllerType.GetCustomAttribute<DefaultExecutionOrder>();
            Require(controllerType.GetCustomAttribute<DisallowMultipleComponent>() != null &&
                    order != null &&
                    order.order == 1100 &&
                    typeof(FlatWaterSurfaceProvider)
                        .GetCustomAttribute<DisallowMultipleComponent>() != null &&
                    !typeof(MonoBehaviour).IsAssignableFrom(
                        typeof(DeterministicUsvDiagnosticVisualMotion)) &&
                    (int)UsvSurfaceVisualMode.Disabled == 0 &&
                    (int)UsvSurfaceVisualMode.LocalDiagnosticPublicData == 1,
                "M2-C1 type, execution-order, or explicit-mode contract changed.");
            Add(checks, "Type and mode contract",
                "DisallowMultipleComponent, execution order 1100, pure motion and explicit Disabled default are fixed.");
        }

        private static void VerifyPureMotion(ICollection<Check> checks)
        {
            Require(Evaluate(0.0, out UsvDiagnosticVisualMotionSample zero) &&
                    Near(zero.HeightOffsetMeters, 0f) &&
                    Near(zero.PitchDegrees, 0f) &&
                    Near(zero.RollDegrees, 0f),
                "Diagnostic motion t=0 is not identity.");
            Require(!Evaluate(double.NaN, out _) &&
                    !Evaluate(double.PositiveInfinity, out _) &&
                    !Evaluate(-0.1, out _),
                "Diagnostic motion accepted invalid elapsed time.");
            Require(Evaluate(1.25, out UsvDiagnosticVisualMotionSample first) &&
                    Evaluate(1.25, out UsvDiagnosticVisualMotionSample repeated) &&
                    Equal(first, repeated) &&
                    Evaluate(9.25, out UsvDiagnosticVisualMotionSample periodic) &&
                    Near(first.HeightOffsetMeters, periodic.HeightOffsetMeters, 0.000001f) &&
                    Near(first.PitchDegrees, periodic.PitchDegrees, 0.000001f) &&
                    Near(first.RollDegrees, periodic.RollDegrees, 0.000001f),
                "Diagnostic motion is not deterministic or 8-second periodic.");

            for (int index = 0; index <= 512; index++)
            {
                double time = index * (16.0 / 512.0);
                Require(Evaluate(time, out UsvDiagnosticVisualMotionSample sample) &&
                        float.IsFinite(sample.HeightOffsetMeters) &&
                        float.IsFinite(sample.PitchDegrees) &&
                        float.IsFinite(sample.RollDegrees) &&
                        Math.Abs(sample.HeightOffsetMeters) <= 0.015001f &&
                        Math.Abs(sample.PitchDegrees) <= 0.800001f &&
                        Math.Abs(sample.RollDegrees) <= 1.200001f,
                    "Diagnostic motion produced an invalid or out-of-bounds sample.");
            }
            Add(checks, "Pure deterministic motion",
                "Explicit time, identity fade start, 8 s period and heave/pitch/roll bounds passed.");
        }

        private static bool Evaluate(
            double elapsedSeconds,
            out UsvDiagnosticVisualMotionSample sample)
        {
            return DeterministicUsvDiagnosticVisualMotion.TryEvaluate(
                elapsedSeconds,
                8f,
                0.015f,
                0.8f,
                1.2f,
                0.75f,
                out sample);
        }

        private static void VerifyProvider(
            FlatWaterSurfaceProvider provider,
            ICollection<Check> checks)
        {
            Require(provider.enabled &&
                    provider.TrySample(
                        new Vector3(2f, 3f, -4f),
                        out Vector3 point,
                        out Vector3 normal) &&
                    Near(point, new Vector3(2f, 0f, -4f), 0.000001f) &&
                    Near(normal, Vector3.up, 0.000001f) &&
                    Near(normal.magnitude, 1f) &&
                    !provider.TrySample(
                        new Vector3(float.NaN, 0f, 0f),
                        out _,
                        out _),
                "Flat surface projection, Y=0 normal, or invalid-input handling failed.");
            Add(checks, "Flat surface contract",
                "Transform plane projects to Y=0 with normalized +Y and rejects non-finite input.");
        }

        private static void VerifySourceBoundaries(ICollection<Check> checks)
        {
            string motion = File.ReadAllText(
                RuntimeDirectory + "DeterministicUsvDiagnosticVisualMotion.cs");
            Require(!motion.Contains("UnityEngine") &&
                    !motion.Contains("Time.") &&
                    !motion.Contains("Transform") &&
                    !motion.Contains("Authority") &&
                    !motion.Contains("VehiclePoseDriver"),
                "Pure diagnostic motion crossed a Unity/runtime boundary.");

            string provider = File.ReadAllText(
                RuntimeDirectory + "FlatWaterSurfaceProvider.cs");
            Require(!provider.Contains("Renderer") &&
                    !provider.Contains("Collider") &&
                    !provider.Contains("Mesh") &&
                    !provider.Contains("Raycast") &&
                    !provider.Contains("Physics"),
                "Flat Provider depends on geometry, rendering or physics.");

            string controller = File.ReadAllText(
                RuntimeDirectory + "UsvSurfaceVisualController.cs");
            Require(!controller.Contains("GameObject.Find") &&
                    !controller.Contains("FindObjects") &&
                    !controller.Contains("System.Linq") &&
                    !controller.Contains("Debug.Log") &&
                    !controller.Contains("VehicleStateStore") &&
                    !controller.Contains("VehicleRenderSampler") &&
                    !controller.Contains("SourceId") &&
                    !controller.Contains("GeneratorKind") &&
                    !controller.Contains("modelAlignment") &&
                    controller.Contains(
                        "mode = UsvSurfaceVisualMode.Disabled"),
                "Controller crossed a search, logging, Store/Sampler or implicit-gate boundary.");
            Add(checks, "Source dependency boundary",
                "No Unity time in pure motion; no geometry surface input; no per-frame search/LINQ/log or implicit gate.");
        }

        private static T[] FindSceneComponents<T>(Scene scene) where T : Component
        {
            return UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include)
                .Where(item => item.gameObject.scene == scene)
                .ToArray();
        }

        private static bool HasMissingScripts(Scene scene)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Any(item => item.GetComponents<Component>().Any(component => component == null));
        }

        private static void WriteReport(IReadOnlyCollection<Check> checks, Scene scene)
        {
            string configured = Environment.GetEnvironmentVariable("M2C1_EVIDENCE_DIR");
            string directory = string.IsNullOrWhiteSpace(configured)
                ? Path.GetFullPath(Path.Combine(
                    Directory.GetParent(Application.dataPath).FullName,
                    "..",
                    "..",
                    "M2C1_Validation"))
                : Path.GetFullPath(configured);
            Directory.CreateDirectory(directory);
            var text = new StringBuilder();
            text.AppendLine("# M2-C1 USV Surface Visual Static Verification");
            text.AppendLine();
            text.AppendLine("- Status: `M2C1_USV_SURFACE_VISUAL_STATIC_VALIDATION_PASS`");
            text.AppendLine("- Scene: `" + scene.path + "`");
            text.AppendLine("- Checks: `" + checks.Count + "/" + checks.Count + "`");
            text.AppendLine();
            foreach (Check check in checks)
            {
                text.AppendLine("- PASS — " + check.Name + ": " + check.Detail);
            }
            File.WriteAllText(
                Path.Combine(directory, "m2c1_static_report.md"),
                text.ToString(),
                new UTF8Encoding(false));
        }

        private static ulong FileId(UnityEngine.Object value)
        {
            return GlobalObjectId.GetGlobalObjectIdSlow(value).targetObjectId;
        }

        private static GameObject FindUniqueRoot(Scene scene, string name)
        {
            GameObject[] matches = scene.GetRootGameObjects()
                .Where(item => string.Equals(item.name, name, StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1, "Expected one Scene root named " + name + ".");
            return matches[0];
        }

        private static Transform RequireUniqueDescendant(Transform root, string name)
        {
            Transform[] matches = root.GetComponentsInChildren<Transform>(true)
                .Where(item => string.Equals(item.name, name, StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1, "Expected one descendant named " + name + ".");
            return matches[0];
        }

        private static T RequireComponent<T>(GameObject value, string label)
            where T : Component
        {
            T component = value.GetComponent<T>();
            Require(component != null, "Missing " + label + ".");
            return component;
        }

        private static bool IsIdentity(Transform value)
        {
            return Near(value.localPosition, Vector3.zero, 0.000001f) &&
                   Near(value.localRotation, Quaternion.identity, 0.000001f) &&
                   Near(value.localScale, Vector3.one, 0.000001f);
        }

        private static bool Equal(
            UsvDiagnosticVisualMotionSample left,
            UsvDiagnosticVisualMotionSample right)
        {
            return left.HeightOffsetMeters.Equals(right.HeightOffsetMeters) &&
                   left.PitchDegrees.Equals(right.PitchDegrees) &&
                   left.RollDegrees.Equals(right.RollDegrees);
        }

        private static bool Near(float left, float right, float tolerance = 0.000001f)
        {
            return Mathf.Abs(left - right) <= tolerance;
        }

        private static bool Near(Vector3 left, Vector3 right, float tolerance)
        {
            return (left - right).sqrMagnitude <= tolerance * tolerance;
        }

        private static bool Near(Quaternion left, Quaternion right, float tolerance)
        {
            return Mathf.Abs(Quaternion.Dot(left.normalized, right.normalized)) >=
                   1f - tolerance;
        }

        private static void Add(ICollection<Check> checks, string name, string detail)
        {
            checks.Add(new Check { Name = name, Detail = detail });
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
