using System;
using System.Collections.Generic;
using System.Linq;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Usv;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    public static class UsvActuatorVisualM2DSceneInstaller
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string UsvName = "USV_Blue_Surface";
        private const string VisualRootName = "USV_SurfaceVisualRoot";
        private const string ModelName = "USV_FineModel_V1_Imported";
        private const string DriverName = "USV_PublicPoseDriver";
        private const string MainName = "USV_Rudder_Main";
        private const string PivotName = "USV_Rudder_VisualPivot";
        private const long PivotGameObjectSourceFileId = -4973267675681198602L;
        private const long PivotTransformSourceFileId = 6121043653356691839L;
        private const long PortGameObjectSourceFileId = 7358421799430578395L;
        private const long PortTransformSourceFileId = -6237791876691009839L;
        private const long StarboardGameObjectSourceFileId = -2726587477367591226L;
        private const long StarboardTransformSourceFileId = -5767855332689671421L;

        private static readonly string[] MovingNames =
        {
            "USV_Rudder_Blade",
            "USV_Rudder_Blade_Hinge_Lug_Lower",
            "USV_Rudder_Blade_Hinge_Lug_Upper",
            "USV_Rudder_Shaft",
            "USV_Rudder_Head",
            "USV_Rudder_TillerArm",
            "USV_Rudder_Hinge_Pin_Lower",
            "USV_Rudder_Hinge_Pin_Upper",
            "USV_Rudder_Shaft_Collar_Lower",
            "USV_Rudder_Shaft_Collar_Upper"
        };

        private static readonly string[] FixedNames =
        {
            "USV_Rudder_Bracket",
            "USV_Rudder_Bracket_Cheek_L",
            "USV_Rudder_Bracket_Cheek_R",
            "USV_Rudder_Bracket_Pin_Lower",
            "USV_Rudder_Bracket_Pin_Upper",
            "USV_Rudder_CenterSupport_Pad",
            "USV_Rudder_Mount",
            "USV_Rudder_MountPlate"
        };

        [MenuItem("Tools/Underwater Demo/M2-D/Install USV Actuator Visual")]
        public static void InstallFromMenu()
        {
            Install();
        }

        public static void RunBatch()
        {
            bool changed = Install();
            Debug.Log(
                "M2D_USV_ACTUATOR_VISUAL_SCENE_INSTALL_COMPLETE | changed=" +
                (changed ? "true" : "false") +
                " | " +
                ScenePath);
        }

        public static bool Install()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Require(scene.IsValid() && scene.isLoaded,
                "The authoritative M2-D Scene could not be loaded.");
            Require(!scene.isDirty, "M2-D Scene was dirty before installation.");
            bool changed = InstallIntoLoadedScene(scene);
            if (changed)
            {
                Require(EditorSceneManager.SaveScene(scene),
                    "Failed to save M2-D Scene integration.");
                AssetDatabase.SaveAssets();
            }
            else
            {
                Require(!scene.isDirty,
                    "Idempotent M2-D install unexpectedly dirtied the Scene.");
            }

            return changed;
        }

        public static bool InstallForCanonicalPostBuildChain()
        {
            return InstallIntoLoadedScene(RequireComposableScene());
        }

        private static bool InstallIntoLoadedScene(Scene scene)
        {
            GameObject usv = FindUniqueRoot(scene, UsvName);
            GameObject driverObject = FindUniqueRoot(scene, DriverName);
            Transform visualRoot = RequireDirectChild(usv.transform, VisualRootName);
            Transform model = RequireDirectChild(visualRoot, ModelName);
            Require(usv.transform.childCount == 1 &&
                    visualRoot.childCount == 1 &&
                    IsIdentity(visualRoot),
                "M2-D requires the canonical M2-C visual hierarchy.");

            Transform main = RequireUniqueDescendant(model, MainName);
            Transform pivot = RequireDirectChild(main, PivotName);
            ValidateRudderHierarchy(main, pivot);
            Require(Near(pivot.localRotation, Quaternion.identity, 0.000001f) &&
                    Near(pivot.localScale, Vector3.one, 0.000001f),
                "Rudder Pivot neutral rotation or scale changed.");
            Require(SourceFileId(pivot.gameObject) == PivotGameObjectSourceFileId &&
                    SourceFileId(pivot) == PivotTransformSourceFileId,
                "Rudder Pivot source fileIDs changed.");

            PropellerSpinner[] spinners =
                model.GetComponentsInChildren<PropellerSpinner>(true);
            Require(spinners.Length == 2,
                "Expected exactly two USV PropellerSpinner components.");
            PropellerSpinner port = RequireSpinnerRole(
                usv.transform,
                model,
                spinners,
                "USV_Right_Surface_Thruster/USV_Right_Propeller_RotatingPart",
                PortGameObjectSourceFileId,
                PortTransformSourceFileId,
                true);
            PropellerSpinner starboard = RequireSpinnerRole(
                usv.transform,
                model,
                spinners,
                "USV_Left_Surface_Thruster/USV_Left_Propeller_RotatingPart",
                StarboardGameObjectSourceFileId,
                StarboardTransformSourceFileId,
                false);
            Require(!ReferenceEquals(port, starboard) &&
                    port.enabled &&
                    starboard.enabled &&
                    Near(port.localAxis.normalized, Vector3.right, 0.000001f) &&
                    Near(starboard.localAxis.normalized, Vector3.right, 0.000001f) &&
                    Near(port.rpm, 740f) &&
                    Near(starboard.rpm, 740f),
                "USV Spinner role, axis, enabled state or serialized 740 rpm changed.");

            VehiclePoseControlAuthority authority =
                RequireComponent<VehiclePoseControlAuthority>(
                    usv,
                    "USV Authority");
            VehiclePoseDriver driver =
                RequireComponent<VehiclePoseDriver>(
                    driverObject,
                    "USV pose Driver");
            Require(driver.TargetRoot == usv.transform &&
                    driver.ControlAuthority == authority,
                "Driver/Authority does not target the USV business root.");

            Vector3 pivotPosition = pivot.localPosition;
            Quaternion pivotRotation = pivot.localRotation;
            Vector3 pivotScale = pivot.localScale;
            Vector3 portPosition = port.transform.localPosition;
            Vector3 portScale = port.transform.localScale;
            Vector3 starboardPosition = starboard.transform.localPosition;
            Vector3 starboardScale = starboard.transform.localScale;

            UsvActuatorVisualCoordinator[] coordinators =
                UnityEngine.Object.FindObjectsByType<UsvActuatorVisualCoordinator>(
                        FindObjectsInactive.Include)
                    .Where(item => item.gameObject.scene == scene)
                    .ToArray();
            Require(coordinators.Length <= 1,
                "Scene contains duplicate UsvActuatorVisualCoordinator components.");
            bool changed = false;
            UsvActuatorVisualCoordinator coordinator;
            if (coordinators.Length == 0)
            {
                coordinator = usv.AddComponent<UsvActuatorVisualCoordinator>();
                changed = true;
            }
            else
            {
                coordinator = coordinators[0];
                Require(coordinator.transform == usv.transform,
                    "Coordinator is not mounted on USV_Blue_Surface.");
            }

            if (!coordinator.enabled)
            {
                coordinator.enabled = true;
                changed = true;
            }

            var serialized = new SerializedObject(coordinator);
            serialized.Update();
            changed |= SetObject(serialized, "businessRoot", usv.transform);
            changed |= SetObject(serialized, "portVisualThruster", port);
            changed |= SetObject(serialized, "starboardVisualThruster", starboard);
            changed |= SetObject(serialized, "rudderVisualPivot", pivot);
            changed |= SetObject(serialized, "poseDriver", driver);
            changed |= SetObject(serialized, "controlAuthority", authority);
            changed |= SetEnum(
                serialized,
                "mode",
                (int)UsvActuatorVisualMode.DemoAndLocalDiagnosticPublicData);
            changed |= SetFloat(serialized, "speedDeadbandMetersPerSecond", 0.02f);
            changed |= SetFloat(serialized, "speedFullScaleMetersPerSecond", 0.60f);
            changed |= SetFloat(serialized, "yawDeadbandDegreesPerSecond", 2f);
            changed |= SetFloat(serialized, "yawFullScaleDegreesPerSecond", 90f);
            changed |= SetFloat(serialized, "minVisibleRpm", 120f);
            changed |= SetFloat(serialized, "cruiseRpm", 520f);
            changed |= SetFloat(serialized, "maxVisualRpm", 740f);
            changed |= SetFloat(serialized, "maxDifferentialRpm", 220f);
            changed |= SetFloat(serialized, "lowSpeedOffMetersPerSecond", 0.03f);
            changed |= SetFloat(serialized, "lowSpeedFullMetersPerSecond", 0.08f);
            changed |= SetFloat(serialized, "maxVisualRudderDegrees", 25f);
            changed |= SetFloat(serialized, "rpmRiseRate", 1600f);
            changed |= SetFloat(serialized, "rpmFallRate", 2200f);
            changed |= SetFloat(
                serialized,
                "rudderSlewRateDegreesPerSecond",
                90f);
            changed |= SetFloat(serialized, "maxAcceptedDeltaTimeSeconds", 0.25f);
            changed |= SetFloat(
                serialized,
                "teleportDistanceThresholdMeters",
                0.25f);
            changed |= SetFloat(
                serialized,
                "rotationJumpThresholdDegrees",
                30f);

            if (changed)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(coordinator);
                EditorSceneManager.MarkSceneDirty(scene);
            }
            else
            {
                serialized.Dispose();
            }

            Require(Near(port.rpm, 740f) &&
                    Near(starboard.rpm, 740f) &&
                    Near(pivot.localPosition, pivotPosition, 0.000001f) &&
                    Near(pivot.localRotation, pivotRotation, 0.000001f) &&
                    Near(pivot.localScale, pivotScale, 0.000001f) &&
                    Near(port.transform.localPosition, portPosition, 0.000001f) &&
                    Near(port.transform.localScale, portScale, 0.000001f) &&
                    Near(
                        starboard.transform.localPosition,
                        starboardPosition,
                        0.000001f) &&
                    Near(starboard.transform.localScale, starboardScale, 0.000001f),
                "Installer changed serialized RPM or protected actuator Transform.");
            return changed;
        }

        private static Scene RequireComposableScene()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                "The M2-D canonical installer cannot run in Play Mode.");
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded,
                "The authoritative M2-D Scene is not loaded.");
            Require(string.Equals(scene.path, ScenePath, StringComparison.Ordinal),
                "The active Scene is not the authoritative M2-D Scene.");
            Require(SceneManager.sceneCount == 1,
                "The authoritative M2-D Scene must be uniquely loaded.");
            return scene;
        }

        private static void ValidateRudderHierarchy(Transform main, Transform pivot)
        {
            Require(main.GetComponent<PropellerSpinner>() == null &&
                    main.GetComponent<Animator>() == null,
                "USV_Rudder_Main received an unintended writer.");
            Require(pivot.childCount == MovingNames.Length,
                "Rudder Pivot does not contain exactly ten moving objects.");
            var moving = new HashSet<string>(
                Enumerable.Range(0, pivot.childCount)
                    .Select(index => pivot.GetChild(index).name),
                StringComparer.Ordinal);
            Require(moving.SetEquals(MovingNames),
                "Rudder Pivot moving subset differs from the frozen ten objects.");

            Transform[] fixedChildren = Enumerable.Range(0, main.childCount)
                .Select(index => main.GetChild(index))
                .Where(child => child != pivot)
                .ToArray();
            Require(fixedChildren.Length == FixedNames.Length &&
                    new HashSet<string>(
                            fixedChildren.Select(child => child.name),
                            StringComparer.Ordinal)
                        .SetEquals(FixedNames),
                "USV_Rudder_Main fixed subset differs from the frozen eight objects.");
        }

        private static PropellerSpinner RequireSpinnerRole(
            Transform businessRoot,
            Transform model,
            IEnumerable<PropellerSpinner> spinners,
            string expectedRelativePath,
            long expectedGameObjectSourceId,
            long expectedTransformSourceId,
            bool port)
        {
            PropellerSpinner[] matches = spinners
                .Where(item =>
                    string.Equals(
                        RelativePath(model, item.transform),
                        expectedRelativePath,
                        StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1,
                "Spinner full-path role is missing or ambiguous: " +
                expectedRelativePath + ".");
            PropellerSpinner spinner = matches[0];
            Vector3 rootPosition =
                businessRoot.InverseTransformPoint(spinner.transform.position);
            long gameObjectSourceId = SourceFileId(spinner.gameObject);
            long transformSourceId = SourceFileId(spinner.transform);
            Require(gameObjectSourceId == expectedGameObjectSourceId &&
                    transformSourceId == expectedTransformSourceId &&
                    (port ? rootPosition.z > 0f : rootPosition.z < 0f),
                "Spinner role conflict: path=" +
                expectedRelativePath +
                ", GameObject source=" +
                gameObjectSourceId +
                ", Transform source=" +
                transformSourceId +
                ", root-space position=" +
                rootPosition +
                ", expected port=" +
                port +
                ".");
            return spinner;
        }

        private static string RelativePath(Transform root, Transform value)
        {
            var names = new Stack<string>();
            Transform current = value;
            while (current != null && current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }
            Require(current == root, "Transform is not below the imported model root.");
            return string.Join("/", names);
        }

        private static long SourceFileId(UnityEngine.Object instance)
        {
            UnityEngine.Object source =
                PrefabUtility.GetCorrespondingObjectFromSource(instance);
            Require(source != null, "Imported object has no corresponding FBX source.");
            return unchecked((long)GlobalObjectId.GetGlobalObjectIdSlow(source).targetObjectId);
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

        private static GameObject FindUniqueRoot(Scene scene, string name)
        {
            GameObject[] matches = scene.GetRootGameObjects()
                .Where(item => string.Equals(item.name, name, StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1, "Expected one Scene root named " + name + ".");
            return matches[0];
        }

        private static Transform RequireDirectChild(Transform parent, string name)
        {
            Transform[] matches = Enumerable.Range(0, parent.childCount)
                .Select(index => parent.GetChild(index))
                .Where(child => string.Equals(child.name, name, StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1,
                "Expected one direct child named " + name + ".");
            return matches[0];
        }

        private static Transform RequireUniqueDescendant(Transform root, string name)
        {
            Transform[] matches = root.GetComponentsInChildren<Transform>(true)
                .Where(item => string.Equals(item.name, name, StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1,
                "Expected one descendant named " + name + ".");
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

        private static bool Near(float left, float right)
        {
            return Mathf.Abs(left - right) <= 0.000001f;
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

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
