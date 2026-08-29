using System;
using System.Linq;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Rov;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    public static class RovThrusterVisualM1CSceneInstaller
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string RovName = "ROV_Box_Seabed";
        private const string RovModelName = "ROV_FineModel_V1_Imported";
        private const string RovDriverName = "ROV_PublicPoseDriver";
        private const string RovModelAssetPath =
            "Assets/Models/ROV/ROV_FineModel_V1.fbx";
        private const string RovModelAssetGuid =
            "81496ffad80dd2d43aeec8986511d0a9";
        private const ulong RovTransformFileId = 656059028UL;
        private const long RovModelSourceGameObjectId = 919132149155446097L;
        private const long RovModelSourceTransformId = -8679921383154817045L;
        private static readonly Vector3 CanonicalRovRootPosition =
            new Vector3(3.45f, -8.358999f, -1.45f);

        private enum RovIdentityMode
        {
            AuthoritativeLocalFileIds,
            CanonicalSemanticIdentity
        }

        private sealed class ExpectedBinding
        {
            public string SerializedField;
            public string RelativePath;
            public ulong SpinnerFileId;
            public long SourceGameObjectId;
            public long SourceTransformId;
            public Vector3 RootPosition;
            public Vector3 LocalAxis;
            public float OriginalRpm;
        }

        [MenuItem("Tools/Underwater Demo/M1-C1/Install ROV Visual Thruster Linkage")]
        public static void InstallFromMenu()
        {
            Install(RovIdentityMode.AuthoritativeLocalFileIds);
        }

        public static void RunBatch()
        {
            bool changed = Install(RovIdentityMode.AuthoritativeLocalFileIds);
            Debug.Log(
                "M1C_ROV_THRUSTER_SCENE_INSTALL_COMPLETE | changed=" +
                (changed ? "true" : "false") +
                " | " + ScenePath);
        }

        public static bool InstallForCanonicalSceneRebuild()
        {
            return InstallIntoLoadedScene(
                RequireComposableScene(),
                RovIdentityMode.CanonicalSemanticIdentity);
        }

        private static bool Install(RovIdentityMode identityMode)
        {
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            Require(scene.IsValid() && scene.isLoaded,
                "The authoritative M1-C scene could not be loaded.");
            Require(!scene.isDirty,
                "The M1-C scene must be clean before installation.");
            bool changed = InstallIntoLoadedScene(scene, identityMode);
            if (changed)
            {
                Require(EditorSceneManager.SaveScene(scene),
                    "Failed to save the M1-C1 scene integration.");
                Require(!scene.isDirty,
                    "Saved M1-C1 scene remained dirty.");
            }
            else
            {
                Require(!scene.isDirty,
                    "Idempotent M1-C1 install unexpectedly dirtied the scene.");
            }

            return changed;
        }

        private static bool InstallIntoLoadedScene(
            Scene scene,
            RovIdentityMode identityMode)
        {
            GameObject rov = identityMode ==
                             RovIdentityMode.AuthoritativeLocalFileIds
                ? FindUniqueRoot(scene, RovName)
                : FindUniqueSceneObject(scene, RovName);

            if (identityMode == RovIdentityMode.AuthoritativeLocalFileIds)
            {
                Require(
                    GlobalObjectId.GetGlobalObjectIdSlow(rov.transform)
                        .targetObjectId == RovTransformFileId,
                    "ROV movement root Transform fileID changed.");
            }
            else
            {
                ValidateCanonicalRootAndPublicAccess(scene, rov);
            }

            ExpectedBinding[] expected = ExpectedBindings();
            PropellerSpinner[] resolved = expected
                .Select(item => RequireSpinner(
                    rov.transform,
                    item,
                    identityMode))
                .ToArray();
            Require(resolved.Distinct().Count() == expected.Length,
                "Canonical ROV Spinner roles do not resolve to six unique components.");
            Require(resolved.Select(item => item.gameObject).Distinct().Count() ==
                    expected.Length,
                "Canonical ROV Spinner roles do not resolve to six unique GameObjects.");

            RovThrusterVisualCoordinator[] allCoordinators =
                UnityEngine.Object.FindObjectsByType<RovThrusterVisualCoordinator>(
                        FindObjectsInactive.Include)
                    .Where(item => item.gameObject.scene == scene)
                    .ToArray();
            Require(allCoordinators.Length <= 1,
                "ROV Scene contains duplicate visual thruster Coordinators.");
            if (allCoordinators.Length == 1)
            {
                Require(allCoordinators[0].gameObject == rov,
                    "ROV visual thruster Coordinator must be mounted on the movement root.");
            }

            bool changed = false;
            RovThrusterVisualCoordinator coordinator;
            if (allCoordinators.Length == 0)
            {
                coordinator = rov.AddComponent<RovThrusterVisualCoordinator>();
                changed = true;
            }
            else
            {
                coordinator = allCoordinators[0];
            }

            using (var serialized = new SerializedObject(coordinator))
            {
                serialized.Update();
                for (int index = 0; index < expected.Length; index++)
                {
                    changed |= SetObjectReference(
                        serialized,
                        expected[index].SerializedField,
                        resolved[index]);
                }

                changed |= SetFloat(serialized, "visualIdleRpm", 0f);
                changed |= SetFloat(serialized, "surgeMaxVisualRpm", 720f);
                changed |= SetFloat(serialized, "heaveMaxVisualRpm", 680f);
                changed |= SetFloat(serialized, "swayMaxVisualRpm", 700f);
                changed |= SetFloat(serialized, "linearDeadZone", 0.005f);
                changed |= SetFloat(serialized, "surgeFullScaleSpeed", 0.35f);
                changed |= SetFloat(serialized, "heaveFullScaleSpeed", 0.15f);
                changed |= SetFloat(serialized, "swayFullScaleSpeed", 0.27f);
                changed |= SetFloat(
                    serialized,
                    "angularDeadZoneDegreesPerSecond",
                    0.5f);
                changed |= SetFloat(
                    serialized,
                    "angularFullScaleDegreesPerSecond",
                    30f);
                changed |= SetFloat(serialized, "angularGlobalWeight", 0.20f);
                changed |= SetFloat(serialized, "rpmRiseRatePerSecond", 1800f);
                changed |= SetFloat(serialized, "rpmFallRatePerSecond", 2400f);
                changed |= SetFloat(serialized, "maxValidDeltaTime", 0.25f);
                changed |= SetFloat(
                    serialized,
                    "teleportDistanceThreshold",
                    0.25f);
                changed |= SetFloat(
                    serialized,
                    "teleportAngleThresholdDegrees",
                    30f);

                if (changed)
                {
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(coordinator);
                EditorSceneManager.MarkSceneDirty(scene);
            }

            Require(
                rov.GetComponents<RovThrusterVisualCoordinator>().Length == 1 &&
                UnityEngine.Object.FindObjectsByType<RovThrusterVisualCoordinator>(
                        FindObjectsInactive.Include)
                    .Count(item => item.gameObject.scene == scene) == 1,
                "M1-C1 install did not leave exactly one root Coordinator.");
            return changed;
        }

        private static Scene RequireComposableScene()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                "The M1-C canonical installer cannot run in Play Mode.");
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded,
                "The authoritative M1-C scene is not loaded.");
            Require(string.Equals(scene.path, ScenePath, StringComparison.Ordinal),
                "The active Scene is not the authoritative M1-C scene.");
            Require(SceneManager.sceneCount == 1,
                "The authoritative M1-C scene must be uniquely loaded.");
            return scene;
        }

        private static void ValidateCanonicalRootAndPublicAccess(
            Scene scene,
            GameObject rov)
        {
            Require(rov.transform.parent == null,
                "Canonical ROV movement root must not have a parent.");
            Require(Near(
                        rov.transform.localPosition,
                        CanonicalRovRootPosition,
                        0.000001f) &&
                    Near(
                        rov.transform.localRotation,
                        Quaternion.identity,
                        0.000001f) &&
                    Near(
                        rov.transform.localScale,
                        Vector3.one,
                        0.000001f),
                "Canonical ROV movement root local TRS changed.");

            Component[] rootComponents = rov.GetComponents<Component>();
            Require(rootComponents.All(component =>
                    component is Transform ||
                    component is VehiclePoseControlAuthority ||
                    component is RovThrusterVisualCoordinator),
                "Canonical ROV movement root contains an unexpected component.");

            VehiclePoseControlAuthority[] directAuthorities =
                rov.GetComponents<VehiclePoseControlAuthority>();
            VehiclePoseControlAuthority[] subtreeAuthorities =
                rov.GetComponentsInChildren<VehiclePoseControlAuthority>(true);
            Require(directAuthorities.Length == 1 &&
                    subtreeAuthorities.Length == 1,
                "Canonical ROV requires exactly one direct control Authority.");
            VehiclePoseControlAuthority authority = directAuthorities[0];

            Require(rov.transform.childCount == 1,
                "Canonical ROV movement root must contain only one model child.");
            Transform model = rov.transform.GetChild(0);
            Require(string.Equals(
                        model.name,
                        RovModelName,
                        StringComparison.Ordinal),
                "Canonical ROV direct model child name changed.");
            Require(Near(model.localPosition, Vector3.zero, 0.000001f) &&
                    Near(model.localRotation, Quaternion.identity, 0.000001f) &&
                    Near(model.localScale, Vector3.one * 100f, 0.00001f),
                "Canonical ROV imported model local TRS changed.");
            Require(PrefabSourceId(model.gameObject) ==
                    RovModelSourceGameObjectId &&
                    PrefabSourceId(model) == RovModelSourceTransformId,
                "Canonical ROV model source identity changed.");
            UnityEngine.Object modelSource =
                PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject);
            string modelAssetPath = AssetDatabase.GetAssetPath(modelSource);
            Require(modelAssetPath == RovModelAssetPath &&
                    AssetDatabase.AssetPathToGUID(modelAssetPath) ==
                    RovModelAssetGuid,
                "Canonical ROV model asset path or GUID changed.");

            GameObject driverObject = FindUniqueRoot(scene, RovDriverName);
            VehiclePoseDriver[] directDrivers =
                driverObject.GetComponents<VehiclePoseDriver>();
            Require(directDrivers.Length == 1,
                "Canonical ROV requires exactly one Driver component.");
            VehiclePoseDriver driver = directDrivers[0];
            VehiclePoseDriver[] targetDrivers =
                UnityEngine.Object.FindObjectsByType<VehiclePoseDriver>(
                        FindObjectsInactive.Include)
                    .Where(item =>
                        item.gameObject.scene == scene &&
                        item.TargetRoot == rov.transform)
                    .ToArray();
            Require(targetDrivers.Length == 1 &&
                    ReferenceEquals(targetDrivers[0], driver),
                "Canonical ROV requires exactly one Driver targeting its root.");
            Require(driver.ControlAuthority == authority,
                "Canonical ROV Driver control Authority is incorrect.");
        }

        private static ExpectedBinding[] ExpectedBindings()
        {
            const string Model = "ROV_FineModel_V1_Imported/";
            return new[]
            {
                new ExpectedBinding
                {
                    SerializedField = "surgeVisualRightSpinner",
                    RelativePath = Model +
                        "ROV_HorizontalLeftThruster/" +
                        "ROV_HorizontalLeftThruster_Propeller_RotatingPart",
                    SpinnerFileId = 609084609UL,
                    SourceGameObjectId = -5841059239152539864L,
                    SourceTransformId = -2746362728952546319L,
                    RootPosition = new Vector3(-0.34f, -0.36f, -0.42f),
                    LocalAxis = Vector3.right,
                    OriginalRpm = 720f
                },
                new ExpectedBinding
                {
                    SerializedField = "surgeVisualLeftSpinner",
                    RelativePath = Model +
                        "ROV_HorizontalRightThruster/" +
                        "ROV_HorizontalRightThruster_Propeller_RotatingPart",
                    SpinnerFileId = 609084608UL,
                    SourceGameObjectId = 7463216812353552472L,
                    SourceTransformId = 8740200944343168653L,
                    RootPosition = new Vector3(-0.34f, -0.36f, 0.42f),
                    LocalAxis = Vector3.right,
                    OriginalRpm = 720f
                },
                new ExpectedBinding
                {
                    SerializedField = "heaveVisualRightSpinner",
                    RelativePath = Model +
                        "ROV_VerticalLeftThruster/" +
                        "ROV_VerticalLeftThruster_Propeller_RotatingPart",
                    SpinnerFileId = 609084605UL,
                    SourceGameObjectId = -2777219179595836863L,
                    SourceTransformId = -8992824921500355942L,
                    RootPosition = new Vector3(0f, 0.02f, -1.14f),
                    LocalAxis = Vector3.up,
                    OriginalRpm = 680f
                },
                new ExpectedBinding
                {
                    SerializedField = "heaveVisualLeftSpinner",
                    RelativePath = Model +
                        "ROV_VerticalRightThruster/" +
                        "ROV_VerticalRightThruster_Propeller_RotatingPart",
                    SpinnerFileId = 1100687659UL,
                    SourceGameObjectId = -259751000718330646L,
                    SourceTransformId = 1295331870416032435L,
                    RootPosition = new Vector3(0f, 0.02f, 1.14f),
                    LocalAxis = Vector3.up,
                    OriginalRpm = 680f
                },
                new ExpectedBinding
                {
                    SerializedField = "swayFrontSpinner",
                    RelativePath = Model +
                        "ROV_LateralFrontThruster/" +
                        "ROV_LateralFrontThruster_Propeller_RotatingPart",
                    SpinnerFileId = 609084607UL,
                    SourceGameObjectId = 835290176552072498L,
                    SourceTransformId = -4484295508612249953L,
                    RootPosition = new Vector3(0.36f, -0.30f, 0f),
                    LocalAxis = Vector3.forward,
                    OriginalRpm = 700f
                },
                new ExpectedBinding
                {
                    SerializedField = "swayRearSpinner",
                    RelativePath = Model +
                        "ROV_LateralRearThruster/" +
                        "ROV_LateralRearThruster_Propeller_RotatingPart",
                    SpinnerFileId = 609084606UL,
                    SourceGameObjectId = -3106911608081397382L,
                    SourceTransformId = 9137691344707157062L,
                    RootPosition = new Vector3(-0.36f, -0.30f, 0f),
                    LocalAxis = Vector3.forward,
                    OriginalRpm = 700f
                }
            };
        }

        private static PropellerSpinner RequireSpinner(
            Transform root,
            ExpectedBinding expected,
            RovIdentityMode identityMode)
        {
            Transform rotatingPart = root.Find(expected.RelativePath);
            Require(rotatingPart != null,
                "Missing exact ROV rotating-part path: " +
                expected.RelativePath);
            PropellerSpinner[] spinners =
                rotatingPart.GetComponents<PropellerSpinner>();
            Require(spinners.Length == 1,
                "Expected exactly one Spinner at " +
                expected.RelativePath + ".");
            PropellerSpinner spinner = spinners[0];
            if (identityMode == RovIdentityMode.AuthoritativeLocalFileIds)
            {
                Require(
                    GlobalObjectId.GetGlobalObjectIdSlow(spinner)
                        .targetObjectId == expected.SpinnerFileId,
                    "Spinner fileID changed at " +
                    expected.RelativePath + ".");
            }
            else
            {
                Require(PrefabSourceId(rotatingPart.gameObject) ==
                        expected.SourceGameObjectId &&
                        PrefabSourceId(rotatingPart) ==
                        expected.SourceTransformId,
                    "Spinner source identity changed at " +
                    expected.RelativePath + ".");
            }

            Require(
                Near(
                    root.InverseTransformPoint(rotatingPart.position),
                    expected.RootPosition,
                    0.002f),
                "Spinner root-space position changed at " +
                expected.RelativePath + ".");
            Require(Near(
                    spinner.localAxis,
                    expected.LocalAxis,
                    0.000001f),
                "Spinner localAxis changed at " +
                expected.RelativePath + ".");
            Require(Mathf.Abs(spinner.rpm - expected.OriginalRpm) <= 0.0001f,
                "Spinner original Scene RPM changed at " +
                expected.RelativePath + ".");
            return spinner;
        }

        private static long PrefabSourceId(UnityEngine.Object value)
        {
            UnityEngine.Object source =
                PrefabUtility.GetCorrespondingObjectFromSource(value);
            Require(source != null,
                value.name + " has no prefab source identity.");
            return unchecked((long)
                GlobalObjectId.GetGlobalObjectIdSlow(source).targetObjectId);
        }

        private static bool SetObjectReference(
            SerializedObject serialized,
            string propertyName,
            UnityEngine.Object value)
        {
            SerializedProperty property =
                serialized.FindProperty(propertyName);
            Require(property != null,
                "Missing Coordinator serialized field " +
                propertyName + ".");
            if (ReferenceEquals(property.objectReferenceValue, value))
            {
                return false;
            }

            property.objectReferenceValue = value;
            return true;
        }

        private static bool SetFloat(
            SerializedObject serialized,
            string propertyName,
            float value)
        {
            SerializedProperty property =
                serialized.FindProperty(propertyName);
            Require(property != null,
                "Missing Coordinator serialized field " +
                propertyName + ".");
            if (property.floatValue.Equals(value))
            {
                return false;
            }

            property.floatValue = value;
            return true;
        }

        private static GameObject FindUniqueRoot(Scene scene, string name)
        {
            GameObject[] matches = scene.GetRootGameObjects()
                .Where(root => string.Equals(
                    root.name,
                    name,
                    StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1,
                "Expected exactly one Scene root named " +
                name + ".");
            return matches[0];
        }

        private static GameObject FindUniqueSceneObject(
            Scene scene,
            string name)
        {
            GameObject[] matches = scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<Transform>(true))
                .Where(item => string.Equals(
                    item.name,
                    name,
                    StringComparison.Ordinal))
                .Select(item => item.gameObject)
                .ToArray();
            Require(matches.Length == 1,
                "Expected exactly one Scene GameObject named " +
                name + ".");
            return matches[0];
        }

        private static bool Near(
            Vector3 left,
            Vector3 right,
            float tolerance)
        {
            return (left - right).sqrMagnitude <=
                   tolerance * tolerance;
        }

        private static bool Near(
            Quaternion left,
            Quaternion right,
            float tolerance)
        {
            return 1f - Mathf.Abs(Quaternion.Dot(left, right)) <=
                   tolerance;
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
