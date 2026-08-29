using System;
using System.Linq;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Usv;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    public static class UsvSurfaceVisualM2CSceneInstaller
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string UsvName = "USV_Blue_Surface";
        private const string VisualRootName = "USV_SurfaceVisualRoot";
        private const string ModelName = "USV_FineModel_V1_Imported";
        private const string WaterName = "Water_Surface";
        private const string DriverName = "USV_PublicPoseDriver";
        private const ulong UsvTransformFileId = 858291741UL;
        private const ulong ModelPrefabInstanceFileId = 411090510UL;
        private const ulong WaterTransformFileId = 915388752UL;

        [MenuItem("Tools/Underwater Demo/M2-C1/Install USV Surface Visual")]
        public static void InstallFromMenu()
        {
            Install();
        }

        public static void RunBatch()
        {
            bool changed = Install();
            Debug.Log(
                "M2C1_USV_SURFACE_VISUAL_SCENE_INSTALL_COMPLETE | changed=" +
                (changed ? "true" : "false") +
                " | " + ScenePath);
        }

        public static bool Install()
        {
            return Install(true);
        }

        public static bool InstallForCanonicalPostBuildChain()
        {
            return InstallIntoLoadedScene(RequireComposableScene(), false);
        }

        private static bool Install(bool requireAuthoritativeLocalFileIds)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Require(scene.IsValid() && scene.isLoaded,
                "The authoritative M2-C1 scene could not be loaded.");
            Require(!scene.isDirty, "M2-C1 scene was dirty before installation.");
            bool changed = InstallIntoLoadedScene(
                scene,
                requireAuthoritativeLocalFileIds);
            if (changed)
            {
                Require(EditorSceneManager.SaveScene(scene),
                    "Failed to save the M2-C1 scene integration.");
                AssetDatabase.SaveAssets();
            }
            else
            {
                Require(!scene.isDirty,
                    "Idempotent M2-C1 install unexpectedly dirtied the Scene.");
            }

            return changed;
        }

        private static bool InstallIntoLoadedScene(
            Scene scene,
            bool requireAuthoritativeLocalFileIds)
        {
            GameObject usv = FindUniqueRoot(scene, UsvName);
            GameObject water = FindUniqueRoot(scene, WaterName);
            GameObject driverObject = FindUniqueRoot(scene, DriverName);
            if (requireAuthoritativeLocalFileIds)
            {
                Require(FileId(usv.transform) == UsvTransformFileId,
                    "USV business root Transform fileID changed.");
                Require(FileId(water.transform) == WaterTransformFileId,
                    "Water_Surface Transform fileID changed.");
            }

            VehiclePoseControlAuthority authority =
                RequireComponent<VehiclePoseControlAuthority>(usv, "USV authority");
            VehiclePoseDriver driver =
                RequireComponent<VehiclePoseDriver>(driverObject, "USV pose Driver");
            Require(driver.TargetRoot == usv.transform,
                "USV Driver target is not USV_Blue_Surface.");
            Require(driver.ControlAuthority == authority,
                "USV Driver and business root do not share one Authority.");

            Transform[] models = usv.GetComponentsInChildren<Transform>(true)
                .Where(item => string.Equals(item.name, ModelName, StringComparison.Ordinal))
                .ToArray();
            Require(models.Length == 1, "Expected exactly one imported USV model root.");
            Transform model = models[0];
            GlobalObjectId modelId = GlobalObjectId.GetGlobalObjectIdSlow(model);
            if (requireAuthoritativeLocalFileIds)
            {
                Require(modelId.targetPrefabId == ModelPrefabInstanceFileId,
                    "Imported USV model PrefabInstance fileID changed: " +
                    modelId + ".");
            }

            Transform[] visualRoots = usv.GetComponentsInChildren<Transform>(true)
                .Where(item => string.Equals(
                    item.name,
                    VisualRootName,
                    StringComparison.Ordinal))
                .ToArray();
            Require(visualRoots.Length <= 1,
                "Multiple USV_SurfaceVisualRoot objects were found.");

            bool changed = false;
            Transform visualRoot;
            if (visualRoots.Length == 0)
            {
                Require(model.parent == usv.transform,
                    "Imported model root has an unknown legacy parent.");
                Vector3 modelLocalPosition = model.localPosition;
                Quaternion modelLocalRotation = model.localRotation;
                Vector3 modelLocalScale = model.localScale;
                Matrix4x4 modelWorld = model.localToWorldMatrix;

                var created = new GameObject(VisualRootName);
                SceneManager.MoveGameObjectToScene(created, scene);
                visualRoot = created.transform;
                visualRoot.SetParent(usv.transform, false);
                visualRoot.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                visualRoot.localScale = Vector3.one;
                model.SetParent(visualRoot, false);

                Require(Near(model.localPosition, modelLocalPosition, 0.000001f) &&
                        Near(model.localRotation, modelLocalRotation, 0.000001f) &&
                        Near(model.localScale, modelLocalScale, 0.00001f),
                    "Reparent changed the imported model local Transform.");
                Require(Near(model.localToWorldMatrix, modelWorld, 0.0001f),
                    "Reparent changed the imported model world appearance.");
                changed = true;
            }
            else
            {
                visualRoot = visualRoots[0];
                Require(visualRoot.parent == usv.transform &&
                        model.parent == visualRoot,
                    "Existing USV visual hierarchy is not canonical.");
                Require(IsIdentity(visualRoot),
                    "Existing USV_SurfaceVisualRoot is not identity.");
            }

            Require(usv.transform.childCount == 1 &&
                    usv.transform.GetChild(0) == visualRoot,
                "USV business root must contain only the visual root.");
            Require(visualRoot.childCount == 1 &&
                    visualRoot.GetChild(0) == model,
                "USV visual root must contain only the imported model root.");

            FlatWaterSurfaceProvider[] providers =
                UnityEngine.Object.FindObjectsByType<FlatWaterSurfaceProvider>(
                        FindObjectsInactive.Include)
                    .Where(item => item.gameObject.scene == scene)
                    .ToArray();
            Require(providers.Length <= 1,
                "Scene contains duplicate FlatWaterSurfaceProvider components.");
            FlatWaterSurfaceProvider provider;
            if (providers.Length == 0)
            {
                provider = water.AddComponent<FlatWaterSurfaceProvider>();
                changed = true;
            }
            else
            {
                provider = providers[0];
                Require(provider.gameObject == water,
                    "FlatWaterSurfaceProvider is not mounted on Water_Surface.");
            }

            UsvSurfaceVisualController[] controllers =
                UnityEngine.Object.FindObjectsByType<UsvSurfaceVisualController>(
                        FindObjectsInactive.Include)
                    .Where(item => item.gameObject.scene == scene)
                    .ToArray();
            Require(controllers.Length <= 1,
                "Scene contains duplicate UsvSurfaceVisualController components.");
            UsvSurfaceVisualController controller;
            if (controllers.Length == 0)
            {
                controller = visualRoot.gameObject.AddComponent<UsvSurfaceVisualController>();
                changed = true;
            }
            else
            {
                controller = controllers[0];
                Require(controller.transform == visualRoot,
                    "UsvSurfaceVisualController is not mounted on the visual root.");
            }

            if (!provider.enabled)
            {
                provider.enabled = true;
                changed = true;
            }
            if (!controller.enabled)
            {
                controller.enabled = true;
                changed = true;
            }

            var serialized = new SerializedObject(controller);
            serialized.Update();
            changed |= SetObject(serialized, "businessRoot", usv.transform);
            changed |= SetObject(serialized, "importedModelRoot", model);
            changed |= SetObject(serialized, "waterSurfaceProvider", provider);
            changed |= SetObject(serialized, "poseDriver", driver);
            changed |= SetObject(serialized, "controlAuthority", authority);
            changed |= SetEnum(
                serialized,
                "mode",
                (int)UsvSurfaceVisualMode.LocalDiagnosticPublicData);
            changed |= SetFloat(
                serialized,
                "diagnosticNeutralRootHeightAboveSurface",
                0.18f);
            changed |= SetFloat(serialized, "maxSurfaceCorrection", 0.05f);
            changed |= SetFloat(serialized, "diagnosticPeriodSeconds", 8f);
            changed |= SetFloat(serialized, "heaveAmplitudeMeters", 0.015f);
            changed |= SetFloat(serialized, "pitchAmplitudeDegrees", 0.8f);
            changed |= SetFloat(serialized, "rollAmplitudeDegrees", 1.2f);
            changed |= SetFloat(serialized, "activationFadeSeconds", 0.75f);
            changed |= SetFloat(serialized, "teleportDistanceMeters", 0.25f);
            changed |= SetFloat(serialized, "teleportAngleDegrees", 30f);
            changed |= SetFloat(serialized, "maxAcceptedDeltaTimeSeconds", 0.25f);

            if (changed)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(provider);
                EditorUtility.SetDirty(controller);
                EditorSceneManager.MarkSceneDirty(scene);
            }
            else
            {
                serialized.Dispose();
            }

            Require(
                    (!requireAuthoritativeLocalFileIds ||
                     GlobalObjectId.GetGlobalObjectIdSlow(model).targetPrefabId ==
                        ModelPrefabInstanceFileId) &&
                    Near(model.localPosition, Vector3.zero, 0.000001f) &&
                    Near(model.localRotation, Quaternion.Euler(-90f, 180f, 0f), 0.000001f) &&
                    Near(model.localScale, Vector3.one * 100f, 0.00001f),
                "Imported model identity or local Transform changed.");
            return changed;
        }

        private static Scene RequireComposableScene()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                "The M2-C canonical installer cannot run in Play Mode.");
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded,
                "The authoritative M2-C Scene is not loaded.");
            Require(string.Equals(scene.path, ScenePath, StringComparison.Ordinal),
                "The active Scene is not the authoritative M2-C Scene.");
            Require(SceneManager.sceneCount == 1,
                "The authoritative M2-C Scene must be uniquely loaded.");
            return scene;
        }

        private static bool SetObject(
            SerializedObject serialized,
            string name,
            UnityEngine.Object value)
        {
            SerializedProperty property = serialized.FindProperty(name);
            Require(property != null, "Missing serialized field " + name + ".");
            if (ReferenceEquals(property.objectReferenceValue, value))
            {
                return false;
            }
            property.objectReferenceValue = value;
            return true;
        }

        private static bool SetFloat(
            SerializedObject serialized,
            string name,
            float value)
        {
            SerializedProperty property = serialized.FindProperty(name);
            Require(property != null, "Missing serialized field " + name + ".");
            if (property.floatValue.Equals(value))
            {
                return false;
            }
            property.floatValue = value;
            return true;
        }

        private static bool SetEnum(
            SerializedObject serialized,
            string name,
            int value)
        {
            SerializedProperty property = serialized.FindProperty(name);
            Require(property != null, "Missing serialized field " + name + ".");
            if (property.enumValueIndex == value)
            {
                return false;
            }
            property.enumValueIndex = value;
            return true;
        }

        private static ulong FileId(UnityEngine.Object value)
        {
            return GlobalObjectId.GetGlobalObjectIdSlow(value).targetObjectId;
        }

        private static GameObject FindUniqueRoot(Scene scene, string name)
        {
            GameObject[] matches = scene.GetRootGameObjects()
                .Where(root => string.Equals(root.name, name, StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1,
                "Expected exactly one root named " + name + ".");
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

        private static bool Near(Vector3 left, Vector3 right, float tolerance)
        {
            return (left - right).sqrMagnitude <= tolerance * tolerance;
        }

        private static bool Near(Quaternion left, Quaternion right, float tolerance)
        {
            return Mathf.Abs(Quaternion.Dot(left.normalized, right.normalized)) >=
                   1f - tolerance;
        }

        private static bool Near(Matrix4x4 left, Matrix4x4 right, float tolerance)
        {
            for (int index = 0; index < 16; index++)
            {
                if (Mathf.Abs(left[index] - right[index]) > tolerance)
                {
                    return false;
                }
            }
            return true;
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
