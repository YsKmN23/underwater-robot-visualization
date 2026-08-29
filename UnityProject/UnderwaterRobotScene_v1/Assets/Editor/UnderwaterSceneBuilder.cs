using System;
using System.IO;
using System.Collections.Generic;
using UnderwaterRobotScene;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotSceneEditor
{
    public static class UnderwaterSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string AuvFineModelAssetPath = "Assets/Models/AUV/AUV_FineModel_V1.fbx";
        private const string UsvFineModelAssetPath = "Assets/Models/USV/USV_FineModel_V1.fbx";
        private const string RovFineModelAssetPath = "Assets/Models/ROV/ROV_FineModel_V1.fbx";
        private const float AuvFineModelUnityScale = 100f;
        private const float UsvFineModelUnityScale = 100f;
        private const float RovFineModelUnityScale = 100f;
        private const string ROV_PROP_SPINNER_HOOKUP_VERSION = "find_all_named_six_thruster_parts_v6";
        private const string USV_WATERLINE_FLOAT_STYLE_VERSION = "raised_surface_float_v14";
        private const string USV_UNITY_MATERIAL_BINDING_VERSION = "v26_apply_independent_unity_materials_after_import";
        private static readonly string RovV6AllPropellerRotatingPartMarker = "ROV V6 attaches PropellerSpinner to six article-based ROV propeller rotating parts.";
        private static readonly Vector3 AuvScenePosition = new Vector3(-1.85f, -1.35f, -1.65f);
        private static readonly Vector3 UsvScenePosition = new Vector3(0.15f, 0.18f, 2.05f);
        private static readonly Quaternion UsvFineModelImportRotation = Quaternion.Euler(-90f, 180f, 0f);

        [MenuItem("Tools/Underwater Demo/Build First Version Scene")]
        public static void BuildFirstVersionScene()
        {
            BuildEleventhVersionScene();
        }

        [MenuItem("Tools/Underwater Demo/Build Second Version Scene")]
        public static void BuildSecondVersionScene()
        {
            BuildEleventhVersionScene();
        }

        [MenuItem("Tools/Underwater Demo/Build Third Version Scene")]
        public static void BuildThirdVersionScene()
        {
            BuildEleventhVersionScene();
        }

        [MenuItem("Tools/Underwater Demo/Build Fourth Version Scene")]
        public static void BuildFourthVersionScene()
        {
            BuildEleventhVersionScene();
        }

        [MenuItem("Tools/Underwater Demo/Build Fifth Version Scene")]
        public static void BuildFifthVersionScene()
        {
            BuildEleventhVersionScene();
        }

        [MenuItem("Tools/Underwater Demo/Build Sixth Version Scene")]
        public static void BuildSixthVersionScene()
        {
            BuildEleventhVersionScene();
        }

        [MenuItem("Tools/Underwater Demo/Build Seventh Version Scene")]
        public static void BuildSeventhVersionScene()
        {
            BuildEleventhVersionScene();
        }

        [MenuItem("Tools/Underwater Demo/Build Eighth Version Scene")]
        public static void BuildEighthVersionScene()
        {
            BuildEleventhVersionScene();
        }

        [MenuItem("Tools/Underwater Demo/Build Ninth Version Scene")]
        public static void BuildNinthVersionScene()
        {
            BuildEleventhVersionScene();
        }

        [MenuItem("Tools/Underwater Demo/Build Tenth Version Scene")]
        public static void BuildTenthVersionScene()
        {
            BuildEleventhVersionScene();
        }

        [MenuItem("Tools/Underwater Demo/Build Eleventh Version Scene")]
        public static void BuildEleventhVersionScene()
        {
            AssetDatabase.Refresh();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "UnderwaterRobotDemo";

            Material yellow = MakeMaterial("Demo_AUV_Yellow", new Color(1.0f, 0.78f, 0.08f, 1f));
            Material blue = MakeMaterial("Demo_Blue", new Color(0.03f, 0.35f, 0.85f, 1f));
            Material cyan = MakeMaterial("Demo_Cyan", new Color(0.0f, 0.75f, 0.95f, 1f));
            Material black = MakeMaterial("Demo_Black", new Color(0.02f, 0.025f, 0.03f, 1f));
            Material dark = MakeMaterial("Demo_DarkGrey", new Color(0.08f, 0.09f, 0.1f, 1f));
            Material light = MakeMaterial("Demo_Light", new Color(1f, 0.92f, 0.55f, 1f));
            Material sand = MakeMaterial("Demo_Seabed", new Color(0.42f, 0.36f, 0.26f, 1f));
            Material water = MakeTransparentMaterial("Demo_Water", new Color(0.0f, 0.35f, 0.58f, 0.18f));
            Material waterWall = MakeTransparentMaterial("Demo_WaterWall", new Color(0.0f, 0.35f, 0.58f, 0.12f));
            Material foam = MakeTransparentMaterial("Demo_SurfaceWake", new Color(0.85f, 0.96f, 1f, 0.42f));
            Material shadow = MakeTransparentMaterial("Demo_Shadow", new Color(0f, 0f, 0f, 0.22f));
            Material red = MakeMaterial("Demo_RedMarker", new Color(0.95f, 0.08f, 0.05f, 1f));
            Material orange = MakeMaterial("Demo_AUV_Orange", new Color(1f, 0.38f, 0.05f, 1f));
            Material white = MakeMaterial("Demo_White", new Color(0.92f, 0.96f, 0.96f, 1f));

            BuildEnvironment(sand, water, waterWall, dark, light);
            GameObject auv = BuildAuv(yellow, black, dark, red, orange);
            GameObject usv = BuildUsv(blue, black, dark, light, foam, white);
            GameObject rov = BuildRov(cyan, black, dark, light, shadow);
            BuildLabels(black);

            GameObject controller = new GameObject("DemoMotionController");
            DemoMotionController motion = controller.AddComponent<DemoMotionController>();
            motion.auv = auv.transform;
            motion.usv = usv.transform;
            motion.rov = rov.transform;
            motion.dataPanel = GameObject.Find("DataPanelText").GetComponent<TextMesh>();

            BuildLightingAndCamera();

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Underwater demo scene created at " + ScenePath);
        }

        public static void BuildFromCommandLine()
        {
            BuildEleventhVersionScene();
        }

        private static void BuildEnvironment(Material sand, Material water, Material waterWall, Material dark, Material light)
        {
            GameObject seabed = Cube("Seabed", null, new Vector3(0f, -3.2f, 0f), Quaternion.identity, new Vector3(10f, 0.08f, 8f), sand);
            seabed.isStatic = true;

            GameObject surface = Cube("Water_Surface", null, new Vector3(0f, 0f, 0f), Quaternion.identity, new Vector3(10f, 0.02f, 8f), water);
            surface.isStatic = true;

            GameObject backdrop = Cube("Water_Backdrop", null, new Vector3(0f, -1.6f, 3.95f), Quaternion.identity, new Vector3(10f, 3.2f, 0.025f), waterWall);
            backdrop.isStatic = true;

            GameObject leftWall = Cube("Water_Left_Wall", null, new Vector3(-5f, -1.6f, 0f), Quaternion.identity, new Vector3(0.025f, 3.2f, 8f), waterWall);
            leftWall.isStatic = true;

            GameObject rightWall = Cube("Water_Right_Wall", null, new Vector3(5f, -1.6f, 0f), Quaternion.identity, new Vector3(0.025f, 3.2f, 8f), waterWall);
            rightWall.isStatic = true;

            GameObject particles = new GameObject("Suspended_Particles");
            ParticleSystem ps = particles.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = 8f;
            main.startSpeed = 0.12f;
            main.startSize = 0.035f;
            main.maxParticles = 600;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 55f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(9f, 2.8f, 7f);
            particles.transform.position = new Vector3(0f, -1.6f, 0f);

            Cube("Seabed_Ridge_Back", null, new Vector3(0f, -3.08f, 2.9f), Quaternion.identity, new Vector3(9.5f, 0.18f, 0.28f), sand);
            Cube("Seabed_Ridge_Front", null, new Vector3(0f, -3.07f, -3.15f), Quaternion.identity, new Vector3(8.6f, 0.16f, 0.22f), sand);
            Sphere("Seabed_Rock_Left", null, new Vector3(-2.65f, -3.05f, -2.55f), Quaternion.identity, new Vector3(0.52f, 0.2f, 0.34f), sand);
            Sphere("Seabed_Rock_Right", null, new Vector3(2.2f, -3.04f, 1.95f), Quaternion.identity, new Vector3(0.42f, 0.16f, 0.28f), sand);

        }

        private static GameObject BuildAuv(Material yellow, Material black, Material dark, Material red, Material orange)
        {
            GameObject root = new GameObject("AUV_Yellow_Underwater");
            root.transform.position = AuvScenePosition;

            if (TryBuildAuvFineModel(root.transform))
            {
                return root;
            }

            TorpedoHull("AUV_V9_RoundedHead_TorpedoHull", root.transform, Vector3.zero, Quaternion.identity, 6.04f, 0.36f, yellow);
            Sphere("AUV_V9_RoundedHead", root.transform, new Vector3(-2.91f, 0f, 0f), Quaternion.identity, new Vector3(0.38f, 0.36f, 0.36f), yellow);
            Cylinder("AUV_V9_Nose_Black_Rim", root.transform, new Vector3(-3.08f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.16f, 0.009f, 0.16f), black);
            Cube("AUV_V9_SmallFrontWindow", root.transform, new Vector3(-3.1f, 0.005f, 0f), Quaternion.identity, new Vector3(0.022f, 0.06f, 0.18f), black);
            Cylinder("AUV_V9_ShortTailCone", root.transform, new Vector3(2.9f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.19f, 0.16f, 0.19f), yellow);

            Cylinder("Hull_SegmentLine_1", root.transform, new Vector3(-1.85f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.39f, 0.012f, 0.39f), black);
            Cylinder("Hull_SegmentLine_2", root.transform, new Vector3(-0.55f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.39f, 0.012f, 0.39f), black);
            Cylinder("Hull_SegmentLine_3", root.transform, new Vector3(0.9f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.39f, 0.012f, 0.39f), black);
            Cylinder("Hull_SegmentLine_4", root.transform, new Vector3(2.18f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.35f, 0.012f, 0.35f), black);

            Cube("AUV_Payload_Bay", root.transform, new Vector3(-0.15f, -0.34f, 0f), Quaternion.identity, new Vector3(1.1f, 0.03f, 0.18f), dark);
            Cylinder("Top_Sensor_Bump", root.transform, new Vector3(-0.55f, 0.4f, 0f), Quaternion.identity, new Vector3(0.06f, 0.055f, 0.06f), dark);
            Cylinder("AUV_Top_Fitting_1", root.transform, new Vector3(-1.75f, 0.4f, 0f), Quaternion.identity, new Vector3(0.038f, 0.038f, 0.038f), dark);
            Cylinder("AUV_Top_Fitting_2", root.transform, new Vector3(1.62f, 0.4f, 0f), Quaternion.identity, new Vector3(0.038f, 0.038f, 0.038f), dark);
            Cylinder("AUV_Orange_Mast", root.transform, new Vector3(0.65f, 0.52f, 0f), Quaternion.identity, new Vector3(0.045f, 0.16f, 0.045f), orange);
            Cube("AUV_Orange_Mast_Cap", root.transform, new Vector3(0.65f, 0.7f, 0f), Quaternion.identity, new Vector3(0.15f, 0.05f, 0.1f), orange);
            Cylinder("IMU_Test_Module", root.transform, new Vector3(0.98f, 0.41f, 0f), Quaternion.identity, new Vector3(0.07f, 0.05f, 0.07f), red);
            Cylinder("Side_Port_Left", root.transform, new Vector3(-1.9f, 0f, -0.37f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.06f, 0.018f, 0.06f), black);
            Cylinder("Side_Port_Right", root.transform, new Vector3(1.55f, 0f, 0.37f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.06f, 0.018f, 0.06f), black);
            Cylinder("AUV_V9_Nose_Lower_Port", root.transform, new Vector3(-2.82f, -0.1f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.045f, 0.012f, 0.06f), black);
            Cube("AUV_Lifting_Eye_Front", root.transform, new Vector3(-1.45f, 0.35f, 0f), Quaternion.identity, new Vector3(0.13f, 0.028f, 0.028f), dark);
            Cube("AUV_Lifting_Eye_Front_LeftPost", root.transform, new Vector3(-1.51f, 0.3f, 0f), Quaternion.identity, new Vector3(0.028f, 0.1f, 0.028f), dark);
            Cube("AUV_Lifting_Eye_Front_RightPost", root.transform, new Vector3(-1.39f, 0.3f, 0f), Quaternion.identity, new Vector3(0.028f, 0.1f, 0.028f), dark);
            Cube("AUV_Lifting_Eye_Rear", root.transform, new Vector3(1.72f, 0.35f, 0f), Quaternion.identity, new Vector3(0.13f, 0.028f, 0.028f), dark);
            Cube("AUV_Lifting_Eye_Rear_LeftPost", root.transform, new Vector3(1.66f, 0.3f, 0f), Quaternion.identity, new Vector3(0.028f, 0.1f, 0.028f), dark);
            Cube("AUV_Lifting_Eye_Rear_RightPost", root.transform, new Vector3(1.78f, 0.3f, 0f), Quaternion.identity, new Vector3(0.028f, 0.1f, 0.028f), dark);
            Cylinder("AUV_Rivet_Row_Left_1", root.transform, new Vector3(-0.95f, 0.18f, -0.25f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.018f, 0.008f, 0.018f), dark);
            Cylinder("AUV_Rivet_Row_Left_2", root.transform, new Vector3(-0.35f, 0.18f, -0.25f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.018f, 0.008f, 0.018f), dark);
            Cylinder("AUV_Rivet_Row_Left_3", root.transform, new Vector3(0.25f, 0.18f, -0.25f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.018f, 0.008f, 0.018f), dark);
            Cylinder("AUV_Rivet_Row_Right_1", root.transform, new Vector3(-0.95f, 0.18f, 0.25f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.018f, 0.008f, 0.018f), dark);
            Cylinder("AUV_Rivet_Row_Right_2", root.transform, new Vector3(-0.35f, 0.18f, 0.25f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.018f, 0.008f, 0.018f), dark);
            Cylinder("AUV_Rivet_Row_Right_3", root.transform, new Vector3(0.25f, 0.18f, 0.25f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.018f, 0.008f, 0.018f), dark);
            Cube("AUV_Tail_Fin_Top", root.transform, new Vector3(2.42f, 0.47f, 0f), Quaternion.Euler(0f, 0f, -12f), new Vector3(0.52f, 0.055f, 0.028f), yellow);
            Cube("AUV_Tail_Fin_Bottom", root.transform, new Vector3(2.42f, -0.47f, 0f), Quaternion.Euler(0f, 0f, 12f), new Vector3(0.52f, 0.055f, 0.028f), yellow);
            Cube("AUV_Tail_Fin_Left", root.transform, new Vector3(2.42f, 0f, -0.47f), Quaternion.Euler(0f, 12f, 0f), new Vector3(0.52f, 0.028f, 0.055f), yellow);
            Cube("AUV_Tail_Fin_Right", root.transform, new Vector3(2.42f, 0f, 0.47f), Quaternion.Euler(0f, -12f, 0f), new Vector3(0.52f, 0.028f, 0.055f), yellow);

            GameObject thruster = new GameObject("TailThruster");
            thruster.transform.SetParent(root.transform, false);
            thruster.transform.localPosition = new Vector3(3.1f, 0f, 0f);
            Cylinder("AUV_V9_Tail_Ring", thruster.transform, Vector3.zero, Quaternion.Euler(0f, 0f, 90f), new Vector3(0.22f, 0.028f, 0.22f), black);
            Cylinder("AUV_V9_Tail_Nozzle", thruster.transform, new Vector3(-0.06f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.23f, 0.04f, 0.23f), black);
            Cube("AUV_V9_Tail_Support_Top", thruster.transform, new Vector3(-0.04f, 0.165f, 0f), Quaternion.identity, new Vector3(0.1f, 0.022f, 0.022f), black);
            Cube("AUV_V9_Tail_Support_Bottom", thruster.transform, new Vector3(-0.04f, -0.165f, 0f), Quaternion.identity, new Vector3(0.1f, 0.022f, 0.022f), black);
            Cube("AUV_V9_Tail_Support_Left", thruster.transform, new Vector3(-0.04f, 0f, -0.165f), Quaternion.identity, new Vector3(0.1f, 0.022f, 0.022f), black);
            Cube("AUV_V9_Tail_Support_Right", thruster.transform, new Vector3(-0.04f, 0f, 0.165f), Quaternion.identity, new Vector3(0.1f, 0.022f, 0.022f), black);
            Cylinder("AUV_V9_Tail_Duct", thruster.transform, Vector3.zero, Quaternion.Euler(0f, 0f, 90f), new Vector3(0.17f, 0.075f, 0.17f), black);
            BuildPropeller("Tail_Propeller", thruster.transform, Vector3.zero, Vector3.right, 900f, dark);

            return root;
        }

        private static bool TryBuildAuvFineModel(Transform root)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AuvFineModelAssetPath);
            if (prefab == null)
            {
                return false;
            }

            GameObject model = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (model == null)
            {
                model = UnityEngine.Object.Instantiate(prefab);
            }

            model.name = "AUV_FineModel_V1_Imported";
            model.transform.SetParent(root, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * AuvFineModelUnityScale;

            Transform propeller = FindChildRecursive(model.transform, "Tail_Propeller_RotatingPart");
            if (propeller != null && propeller.GetComponent<PropellerSpinner>() == null)
            {
                PropellerSpinner spinner = propeller.gameObject.AddComponent<PropellerSpinner>();
                spinner.localAxis = Vector3.right;
                spinner.rpm = 900f;
            }

            return true;
        }

        private static Transform FindChildRecursive(Transform parent, string childName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == childName)
                {
                    return child;
                }

                Transform found = FindChildRecursive(child, childName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Transform FindChildRecursiveContaining(Transform parent, string childNamePart)
        {
            foreach (Transform child in parent)
            {
                if (child.name.IndexOf(childNamePart, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return child;
                }

                Transform found = FindChildRecursiveContaining(child, childNamePart);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static void AttachSpinnerToNamedPropellers(Transform model, string[] propellerNames, Vector3 localAxis, float rpm)
        {
            foreach (string propellerName in propellerNames)
            {
                Transform propeller = FindChildRecursive(model, propellerName);
                if (propeller == null)
                {
                    propeller = FindChildRecursiveContaining(model, propellerName);
                }

                if (propeller != null && propeller.GetComponent<PropellerSpinner>() == null)
                {
                    PropellerSpinner spinner = propeller.gameObject.AddComponent<PropellerSpinner>();
                    spinner.localAxis = localAxis;
                    spinner.rpm = rpm;
                }
            }
        }

        private static GameObject BuildUsv(Material blue, Material black, Material dark, Material light, Material foam, Material white)
        {
            GameObject root = new GameObject("USV_Blue_Surface");
            root.transform.position = UsvScenePosition;

            if (TryBuildUsvFineModel(root.transform))
            {
                return root;
            }

            BoatHull("USV_V8_LowPoly_LeftHull", root.transform, new Vector3(0f, -0.22f, -0.52f), Quaternion.identity, 2.95f, 0.48f, 0.78f, blue);
            BoatHull("USV_V8_LowPoly_RightHull", root.transform, new Vector3(0f, -0.22f, 0.52f), Quaternion.identity, 2.95f, 0.48f, 0.78f, blue);
            Cube("USV_V8_Left_Upper_RubRail", root.transform, new Vector3(0.04f, 0.02f, -0.78f), Quaternion.identity, new Vector3(2.55f, 0.045f, 0.045f), black);
            Cube("USV_V8_Right_Upper_RubRail", root.transform, new Vector3(0.04f, 0.02f, 0.78f), Quaternion.identity, new Vector3(2.55f, 0.045f, 0.045f), black);
            Cube("USV_V8_Left_Inner_Chine", root.transform, new Vector3(-0.08f, -0.42f, -0.26f), Quaternion.Euler(0f, 0f, -5f), new Vector3(2.55f, 0.05f, 0.035f), blue);
            Cube("USV_V8_Right_Inner_Chine", root.transform, new Vector3(-0.08f, -0.42f, 0.26f), Quaternion.Euler(0f, 0f, -5f), new Vector3(2.55f, 0.05f, 0.035f), blue);
            Cube("USV_V8_Stern_Flat_Left", root.transform, new Vector3(1.48f, -0.2f, -0.52f), Quaternion.identity, new Vector3(0.06f, 0.46f, 0.38f), blue);
            Cube("USV_V8_Stern_Flat_Right", root.transform, new Vector3(1.48f, -0.2f, 0.52f), Quaternion.identity, new Vector3(0.06f, 0.46f, 0.38f), blue);

            Cube("USV_V9_ThinTopFrame_LeftRail", root.transform, new Vector3(0f, 0.18f, -0.46f), Quaternion.identity, new Vector3(2.35f, 0.035f, 0.045f), black);
            Cube("USV_V9_ThinTopFrame_RightRail", root.transform, new Vector3(0f, 0.18f, 0.46f), Quaternion.identity, new Vector3(2.35f, 0.035f, 0.045f), black);
            Cube("USV_V9_ThinTopFrame_FrontBeam", root.transform, new Vector3(-0.95f, 0.2f, 0f), Quaternion.identity, new Vector3(0.085f, 0.055f, 1.04f), black);
            Cube("USV_V9_ThinTopFrame_RearBeam", root.transform, new Vector3(0.9f, 0.2f, 0f), Quaternion.identity, new Vector3(0.085f, 0.055f, 1.04f), black);
            Cube("USV_V9_ThinTopFrame_CenterBrace", root.transform, new Vector3(-0.02f, 0.22f, 0f), Quaternion.identity, new Vector3(1.2f, 0.032f, 0.04f), black);
            Cube("USV_V9_SmallSensor_Box", root.transform, new Vector3(-0.55f, 0.29f, 0f), Quaternion.identity, new Vector3(0.28f, 0.1f, 0.28f), dark);
            Cube("USV_V9_Rear_LowModule", root.transform, new Vector3(0.68f, 0.29f, 0f), Quaternion.identity, new Vector3(0.36f, 0.08f, 0.5f), dark);
            Cylinder("Antenna", root.transform, new Vector3(-0.82f, 0.68f, 0f), Quaternion.identity, new Vector3(0.012f, 0.32f, 0.012f), black);
            Sphere("Antenna_Top", root.transform, new Vector3(-0.82f, 0.99f, 0f), Quaternion.identity, new Vector3(0.06f, 0.06f, 0.06f), light);
            Cylinder("USV_V9_Top_Device_1", root.transform, new Vector3(-0.12f, 0.29f, -0.3f), Quaternion.identity, new Vector3(0.04f, 0.025f, 0.04f), dark);
            Cylinder("USV_V9_Top_Device_2", root.transform, new Vector3(0.18f, 0.29f, 0.3f), Quaternion.identity, new Vector3(0.04f, 0.025f, 0.04f), dark);
            Cylinder("USV_Left_Side_Dot_1", root.transform, new Vector3(-0.95f, -0.18f, -0.72f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.035f, 0.01f, 0.035f), white);
            Cylinder("USV_Left_Side_Dot_2", root.transform, new Vector3(-0.35f, -0.18f, -0.72f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.035f, 0.01f, 0.035f), white);
            Cylinder("USV_Left_Side_Dot_3", root.transform, new Vector3(0.35f, -0.18f, -0.72f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.035f, 0.01f, 0.035f), white);
            Cylinder("USV_Right_Side_Dot_1", root.transform, new Vector3(-0.95f, -0.18f, 0.72f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.035f, 0.01f, 0.035f), white);
            Cylinder("USV_Right_Side_Dot_2", root.transform, new Vector3(-0.35f, -0.18f, 0.72f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.035f, 0.01f, 0.035f), white);
            Cylinder("USV_Right_Side_Dot_3", root.transform, new Vector3(0.35f, -0.18f, 0.72f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.035f, 0.01f, 0.035f), white);
            Cube("USV_Stern_Cutout_Left", root.transform, new Vector3(1.52f, -0.12f, -0.73f), Quaternion.identity, new Vector3(0.2f, 0.14f, 0.035f), dark);
            Cube("USV_Stern_Cutout_Right", root.transform, new Vector3(1.52f, -0.12f, 0.73f), Quaternion.identity, new Vector3(0.2f, 0.14f, 0.035f), dark);
            BuildThrusterUnit("Left_Surface_Thruster", root.transform, new Vector3(1.55f, -0.18f, -0.52f), Vector3.right, 740f, dark, light);
            BuildThrusterUnit("Right_Surface_Thruster", root.transform, new Vector3(1.55f, -0.18f, 0.52f), Vector3.right, 740f, dark, light);
            Cylinder("USV_Stern_Yellow_Nozzle_Left", root.transform, new Vector3(1.73f, -0.18f, -0.52f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.16f, 0.045f, 0.16f), light);
            Cylinder("USV_Stern_Yellow_Nozzle_Right", root.transform, new Vector3(1.73f, -0.18f, 0.52f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.16f, 0.045f, 0.16f), light);
            Cube("USV_Yellow_Prop_Left", root.transform, new Vector3(1.78f, -0.18f, -0.52f), Quaternion.Euler(0f, 0f, 18f), new Vector3(0.05f, 0.24f, 0.08f), light);
            Cube("USV_Yellow_Prop_Right", root.transform, new Vector3(1.78f, -0.18f, 0.52f), Quaternion.Euler(0f, 0f, 18f), new Vector3(0.05f, 0.24f, 0.08f), light);

            return root;
        }

        private static bool TryBuildUsvFineModel(Transform root)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UsvFineModelAssetPath);
            if (prefab == null)
            {
                return false;
            }

            GameObject model = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (model == null)
            {
                model = UnityEngine.Object.Instantiate(prefab);
            }

            model.name = "USV_FineModel_V1_Imported";
            model.transform.SetParent(root, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = UsvFineModelImportRotation;
            model.transform.localScale = Vector3.one * UsvFineModelUnityScale;

            string[] propellerNames =
            {
                "USV_Left_Propeller_RotatingPart",
                "USV_Right_Propeller_RotatingPart"
            };

            foreach (string propellerName in propellerNames)
            {
                Transform propeller = FindChildRecursive(model.transform, propellerName);
                if (propeller != null && propeller.GetComponent<PropellerSpinner>() == null)
                {
                    PropellerSpinner spinner = propeller.gameObject.AddComponent<PropellerSpinner>();
                    spinner.localAxis = Vector3.right;
                    spinner.rpm = 740f;
                }
            }

            return true;
        }

        private static GameObject BuildRov(Material cyan, Material black, Material dark, Material light, Material shadow)
        {
            GameObject root = new GameObject("ROV_Box_Seabed");
            root.transform.position = new Vector3(3.45f, -2.42f, -1.45f);

            if (TryBuildRovFineModel(root.transform))
            {
                return root;
            }

            Cube("ROV_Seabed_Shadow", root.transform, new Vector3(0f, -0.68f, 0f), Quaternion.identity, new Vector3(2.7f, 0.018f, 2.12f), shadow);
            RoundedTopShell("ROV_V9_Wide_Rounded_TopShell", root.transform, new Vector3(0f, 0.28f, 0f), Quaternion.identity, 2.0f, 1.34f, 0.42f, cyan);
            Cube("ROV_V9_Top_Black_Rail_Left", root.transform, new Vector3(0f, 0.52f, -0.64f), Quaternion.identity, new Vector3(2.02f, 0.045f, 0.045f), black);
            Cube("ROV_V9_Top_Black_Rail_Right", root.transform, new Vector3(0f, 0.52f, 0.64f), Quaternion.identity, new Vector3(2.02f, 0.045f, 0.045f), black);
            Cube("ROV_V9_Top_Black_Rail_Front", root.transform, new Vector3(-0.86f, 0.52f, 0f), Quaternion.identity, new Vector3(0.045f, 0.045f, 1.28f), black);
            Cube("ROV_V9_Top_Black_Rail_Rear", root.transform, new Vector3(0.86f, 0.52f, 0f), Quaternion.identity, new Vector3(0.045f, 0.045f, 1.28f), black);
            Cube("ROV_Top_Handle", root.transform, new Vector3(0f, 0.63f, 0f), Quaternion.identity, new Vector3(0.48f, 0.045f, 0.085f), black);
            Cube("ROV_Lifting_Ring_Top", root.transform, new Vector3(0f, 0.82f, 0f), Quaternion.identity, new Vector3(0.22f, 0.035f, 0.035f), black);
            Cube("ROV_Lifting_Ring_Bottom", root.transform, new Vector3(0f, 0.7f, 0f), Quaternion.identity, new Vector3(0.22f, 0.035f, 0.035f), black);
            Cube("ROV_Lifting_Ring_Left", root.transform, new Vector3(-0.11f, 0.76f, 0f), Quaternion.identity, new Vector3(0.035f, 0.14f, 0.035f), black);
            Cube("ROV_Lifting_Ring_Right", root.transform, new Vector3(0.11f, 0.76f, 0f), Quaternion.identity, new Vector3(0.035f, 0.14f, 0.035f), black);
            Cube("ROV_Lifting_Ring", root.transform, new Vector3(0f, 0.76f, 0f), Quaternion.identity, new Vector3(0.02f, 0.02f, 0.02f), black);
            BuildRovFrame(root.transform, black);
            Cube("ROV_Left_Side_Plate_Upper", root.transform, new Vector3(0f, 0.2f, -0.96f), Quaternion.identity, new Vector3(2.1f, 0.13f, 0.04f), black);
            Cube("ROV_Left_Side_Plate_Lower", root.transform, new Vector3(0f, -0.34f, -0.96f), Quaternion.identity, new Vector3(2.1f, 0.13f, 0.04f), black);
            Cube("ROV_Left_Side_Plate_Window", root.transform, new Vector3(0f, -0.07f, -0.985f), Quaternion.identity, new Vector3(0.52f, 0.2f, 0.02f), cyan);
            Cube("ROV_Right_Side_Plate_Upper", root.transform, new Vector3(0f, 0.2f, 0.96f), Quaternion.identity, new Vector3(2.1f, 0.13f, 0.04f), black);
            Cube("ROV_Right_Side_Plate_Lower", root.transform, new Vector3(0f, -0.34f, 0.96f), Quaternion.identity, new Vector3(2.1f, 0.13f, 0.04f), black);
            Cube("ROV_Right_Side_Plate_Window", root.transform, new Vector3(0f, -0.07f, 0.985f), Quaternion.identity, new Vector3(0.52f, 0.2f, 0.02f), cyan);

            Cube("ROV_V9_WideFrontFrame_Plate", root.transform, new Vector3(-1.18f, -0.06f, 0f), Quaternion.identity, new Vector3(0.075f, 0.78f, 1.52f), black);
            Cube("ROV_Front_Plate_Window_Left", root.transform, new Vector3(-1.225f, 0.18f, -0.52f), Quaternion.identity, new Vector3(0.025f, 0.2f, 0.22f), cyan);
            Cube("ROV_Front_Plate_Window_Right", root.transform, new Vector3(-1.225f, 0.18f, 0.52f), Quaternion.identity, new Vector3(0.025f, 0.2f, 0.22f), cyan);
            Cylinder("FrontCameraHousing", root.transform, new Vector3(-1.12f, -0.02f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.28f, 0.095f, 0.28f), dark);
            Sphere("ROV_V9_Center_Camera_Dome", root.transform, new Vector3(-1.27f, -0.02f, 0f), Quaternion.identity, new Vector3(0.38f, 0.38f, 0.38f), MakeMaterial("Demo_CameraGlass", new Color(0.03f, 0.07f, 0.1f, 1f)));
            Cylinder("Camera_Ring", root.transform, new Vector3(-1.24f, -0.02f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.32f, 0.022f, 0.32f), black);
            Sphere("LeftLight", root.transform, new Vector3(-1.17f, -0.32f, -0.68f), Quaternion.identity, new Vector3(0.13f, 0.13f, 0.13f), light);
            Sphere("RightLight", root.transform, new Vector3(-1.17f, -0.32f, 0.68f), Quaternion.identity, new Vector3(0.13f, 0.13f, 0.13f), light);
            Sphere("ROV_Four_Light_TopLeft", root.transform, new Vector3(-1.26f, 0.23f, -0.65f), Quaternion.identity, new Vector3(0.1f, 0.1f, 0.1f), light);
            Sphere("ROV_Four_Light_TopRight", root.transform, new Vector3(-1.26f, 0.23f, 0.65f), Quaternion.identity, new Vector3(0.1f, 0.1f, 0.1f), light);
            Sphere("ROV_Four_Light_BottomLeft", root.transform, new Vector3(-1.26f, -0.43f, -0.65f), Quaternion.identity, new Vector3(0.1f, 0.1f, 0.1f), light);
            Sphere("ROV_Four_Light_BottomRight", root.transform, new Vector3(-1.26f, -0.43f, 0.65f), Quaternion.identity, new Vector3(0.1f, 0.1f, 0.1f), light);
            Cube("Left_Light_Bracket", root.transform, new Vector3(-1.05f, -0.32f, -0.68f), Quaternion.identity, new Vector3(0.16f, 0.045f, 0.045f), black);
            Cube("Right_Light_Bracket", root.transform, new Vector3(-1.05f, -0.32f, 0.68f), Quaternion.identity, new Vector3(0.16f, 0.045f, 0.045f), black);
            Cylinder("BottomPayload", root.transform, new Vector3(0.2f, -0.48f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.2f, 0.46f, 0.2f), dark);
            Cube("ROV_Bottom_Plate", root.transform, new Vector3(0f, -0.64f, 0f), Quaternion.identity, new Vector3(1.58f, 0.04f, 0.82f), dark);
            Cylinder("ROV_Bottom_Plate_Hole_1", root.transform, new Vector3(-0.46f, -0.605f, -0.22f), Quaternion.identity, new Vector3(0.08f, 0.012f, 0.08f), black);
            Cylinder("ROV_Bottom_Plate_Hole_2", root.transform, new Vector3(0.46f, -0.605f, 0.22f), Quaternion.identity, new Vector3(0.08f, 0.012f, 0.08f), black);
            Cube("Left_Seabed_Skid", root.transform, new Vector3(0f, -0.58f, -0.82f), Quaternion.identity, new Vector3(2.1f, 0.065f, 0.09f), black);
            Cube("Right_Seabed_Skid", root.transform, new Vector3(0f, -0.58f, 0.82f), Quaternion.identity, new Vector3(2.1f, 0.065f, 0.09f), black);

            Cube("ROV_V9_Left_ExternalThruster_Pad", root.transform, new Vector3(0f, 0.56f, -1.04f), Quaternion.identity, new Vector3(2.05f, 0.07f, 0.28f), black);
            Cube("ROV_V9_Right_ExternalThruster_Pad", root.transform, new Vector3(0f, 0.56f, 1.04f), Quaternion.identity, new Vector3(2.05f, 0.07f, 0.28f), black);
            BuildThrusterUnit("ROV_V9_ExternalTopThruster_1_LeftFront", root.transform, new Vector3(-0.72f, 0.62f, -1.04f), Vector3.up, 650f, dark, black);
            Sphere("ROV_V9_Top_Thruster_Cone_1", root.transform, new Vector3(-0.72f, 0.82f, -1.04f), Quaternion.identity, new Vector3(0.22f, 0.18f, 0.22f), dark);
            BuildThrusterUnit("ROV_V9_ExternalTopThruster_2_RightFront", root.transform, new Vector3(-0.72f, 0.62f, 1.04f), Vector3.up, 650f, dark, black);
            Sphere("ROV_V9_Top_Thruster_Cone_2", root.transform, new Vector3(-0.72f, 0.82f, 1.04f), Quaternion.identity, new Vector3(0.22f, 0.18f, 0.22f), dark);
            BuildThrusterUnit("ROV_V9_ExternalTopThruster_3_LeftRear", root.transform, new Vector3(0.72f, 0.62f, -1.04f), Vector3.up, 650f, dark, black);
            Sphere("ROV_V9_Top_Thruster_Cone_3", root.transform, new Vector3(0.72f, 0.82f, -1.04f), Quaternion.identity, new Vector3(0.22f, 0.18f, 0.22f), dark);
            BuildThrusterUnit("ROV_V9_ExternalTopThruster_4_RightRear", root.transform, new Vector3(0.72f, 0.62f, 1.04f), Vector3.up, 650f, dark, black);
            Sphere("ROV_V9_Top_Thruster_Cone_4", root.transform, new Vector3(0.72f, 0.82f, 1.04f), Quaternion.identity, new Vector3(0.22f, 0.18f, 0.22f), dark);
            BuildThrusterUnit("Thruster_5_LowerLeft", root.transform, new Vector3(0.25f, -0.28f, -0.96f), Vector3.right, 720f, dark, black);
            Cylinder("ROV_V9_Left_Side_Thruster_Ring", root.transform, new Vector3(0.25f, -0.28f, -1.08f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.27f, 0.026f, 0.27f), black);
            BuildThrusterUnit("Thruster_6_LowerRight", root.transform, new Vector3(0.25f, -0.28f, 0.96f), Vector3.right, 720f, dark, black);
            Cylinder("ROV_V9_Right_Side_Thruster_Ring", root.transform, new Vector3(0.25f, -0.28f, 1.08f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.27f, 0.026f, 0.27f), black);

            return root;
        }

        private static bool TryBuildRovFineModel(Transform root)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RovFineModelAssetPath);
            if (prefab == null)
            {
                return false;
            }

            GameObject model = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (model == null)
            {
                model = UnityEngine.Object.Instantiate(prefab);
            }

            model.name = "ROV_FineModel_V1_Imported";
            model.transform.SetParent(root, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * RovFineModelUnityScale;

            string[] horizontalPropellerNames =
            {
                "ROV_HorizontalLeftThruster_Propeller_RotatingPart",
                "ROV_HorizontalRightThruster_Propeller_RotatingPart"
            };

            string[] verticalPropellerNames =
            {
                "ROV_VerticalLeftThruster_Propeller_RotatingPart",
                "ROV_VerticalRightThruster_Propeller_RotatingPart"
            };

            string[] lateralPropellerNames =
            {
                "ROV_LateralFrontThruster_Propeller_RotatingPart",
                "ROV_LateralRearThruster_Propeller_RotatingPart"
            };

            AttachSpinnerToNamedPropellers(model.transform, horizontalPropellerNames, Vector3.right, 720f);
            AttachSpinnerToNamedPropellers(model.transform, verticalPropellerNames, Vector3.up, 680f);
            AttachSpinnerToNamedPropellers(model.transform, lateralPropellerNames, Vector3.forward, 700f);
            return true;
        }

        private static void BuildRovFrame(Transform root, Material black)
        {
            Cube("ROV_V9_WideFrontFrame_TopFront", root, new Vector3(-1.08f, 0.58f, 0f), Quaternion.identity, new Vector3(0.075f, 0.075f, 1.85f), black);
            Cube("ROV_V9_WideFrontFrame_TopRear", root, new Vector3(1.08f, 0.58f, 0f), Quaternion.identity, new Vector3(0.075f, 0.075f, 1.85f), black);
            Cube("ROV_V9_WideFrontFrame_BottomFront", root, new Vector3(-1.08f, -0.58f, 0f), Quaternion.identity, new Vector3(0.075f, 0.075f, 1.85f), black);
            Cube("ROV_V9_WideFrontFrame_BottomRear", root, new Vector3(1.08f, -0.58f, 0f), Quaternion.identity, new Vector3(0.075f, 0.075f, 1.85f), black);
            Cube("Frame_LeftTop", root, new Vector3(0f, 0.58f, -0.92f), Quaternion.identity, new Vector3(2.25f, 0.075f, 0.075f), black);
            Cube("Frame_RightTop", root, new Vector3(0f, 0.58f, 0.92f), Quaternion.identity, new Vector3(2.25f, 0.075f, 0.075f), black);
            Cube("Frame_LeftBottom", root, new Vector3(0f, -0.58f, -0.92f), Quaternion.identity, new Vector3(2.25f, 0.075f, 0.075f), black);
            Cube("Frame_RightBottom", root, new Vector3(0f, -0.58f, 0.92f), Quaternion.identity, new Vector3(2.25f, 0.075f, 0.075f), black);
            Cube("Frame_Post_FrontLeft", root, new Vector3(-1.08f, 0f, -0.92f), Quaternion.identity, new Vector3(0.075f, 1.16f, 0.075f), black);
            Cube("Frame_Post_FrontRight", root, new Vector3(-1.08f, 0f, 0.92f), Quaternion.identity, new Vector3(0.075f, 1.16f, 0.075f), black);
            Cube("Frame_Post_RearLeft", root, new Vector3(1.08f, 0f, -0.92f), Quaternion.identity, new Vector3(0.075f, 1.16f, 0.075f), black);
            Cube("Frame_Post_RearRight", root, new Vector3(1.08f, 0f, 0.92f), Quaternion.identity, new Vector3(0.075f, 1.16f, 0.075f), black);
        }

        private static void BuildThrusterUnit(string name, Transform parent, Vector3 localPosition, Vector3 axis, float rpm, Material casing, Material propellerMaterial)
        {
            GameObject unit = new GameObject(name);
            unit.transform.SetParent(parent, false);
            unit.transform.localPosition = localPosition;

            Quaternion ductRotation = axis == Vector3.up ? Quaternion.identity : Quaternion.Euler(0f, 0f, 90f);
            Cylinder("Duct", unit.transform, Vector3.zero, ductRotation, new Vector3(0.2f, 0.14f, 0.2f), casing);
            if (axis == Vector3.up)
            {
                Cube("GuardBar_A", unit.transform, Vector3.zero, Quaternion.identity, new Vector3(0.38f, 0.035f, 0.035f), casing);
                Cube("GuardBar_B", unit.transform, Vector3.zero, Quaternion.identity, new Vector3(0.035f, 0.035f, 0.38f), casing);
            }
            else
            {
                Cube("GuardBar_A", unit.transform, Vector3.zero, Quaternion.identity, new Vector3(0.035f, 0.38f, 0.035f), casing);
                Cube("GuardBar_B", unit.transform, Vector3.zero, Quaternion.identity, new Vector3(0.035f, 0.035f, 0.38f), casing);
            }
            BuildPropeller("Propeller", unit.transform, Vector3.zero, axis, rpm, propellerMaterial);
        }

        private static void BuildPropeller(string name, Transform parent, Vector3 localPosition, Vector3 localAxis, float rpm, Material mat)
        {
            GameObject prop = new GameObject(name);
            prop.transform.SetParent(parent, false);
            prop.transform.localPosition = localPosition;
            PropellerSpinner spinner = prop.AddComponent<PropellerSpinner>();
            spinner.localAxis = localAxis;
            spinner.rpm = rpm;

            for (int i = 0; i < 3; i++)
            {
                float angle = i * 120f;
                GameObject blade = Cube("Blade_" + (i + 1), prop.transform, Vector3.zero, Quaternion.identity, new Vector3(0.04f, 0.28f, 0.07f), mat);
                if (localAxis == Vector3.up)
                {
                    blade.transform.localRotation = Quaternion.Euler(0f, angle, 18f);
                    blade.transform.localPosition = Quaternion.Euler(0f, angle, 0f) * new Vector3(0.12f, 0f, 0f);
                }
                else
                {
                    blade.transform.localRotation = Quaternion.Euler(angle, 0f, 18f);
                    blade.transform.localPosition = Quaternion.Euler(angle, 0f, 0f) * new Vector3(0f, 0.12f, 0f);
                }
            }
        }

        private static void BuildLabels(Material black)
        {
            GameObject statusBoard = Cube(
                "Scene_Status_Board",
                null,
                new Vector3(-1.60f, 1.15f, -3.20f),
                Quaternion.identity,
                new Vector3(2.80f, 0.80f, 0.04f),
                black);
            statusBoard.GetComponent<MeshRenderer>().enabled = false;
            TextMesh dataPanel = Text(
                "DataPanelText",
                "Visual Demo V11\nAUV depth: 1.35 m\nROV altitude: 0.05 m\nMode: seabed inspection",
                new Vector3(-2.85f, 1.10f, -3.24f),
                0.024f);
            dataPanel.lineSpacing = 0.9f;
            dataPanel.anchor = TextAnchor.MiddleLeft;
            dataPanel.alignment = TextAlignment.Left;
            dataPanel.GetComponent<CameraFacingText>().SetBillboardMode(
                CameraFacingText.BillboardMode.ScreenParallel);
            Text("AUV_Yellow_Label", "AUV", new Vector3(AuvScenePosition.x, -0.55f, AuvScenePosition.z), 0.07f);
            Text("USV_Blue_Label", "USV", new Vector3(UsvScenePosition.x, 0.92f, UsvScenePosition.z), 0.07f);
            Text("ROV_Box_Label", "ROV", new Vector3(3.45f, -1.5f, -1.45f), 0.07f);
        }

        private static void BuildLightingAndCamera()
        {
            GameObject sun = new GameObject("Directional Light");
            Light light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.35f;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

            GameObject fill = new GameObject("Underwater Fill Light");
            Light fillLight = fill.AddComponent<Light>();
            fillLight.type = LightType.Point;
            fillLight.intensity = 4f;
            fillLight.range = 7f;
            fill.transform.position = new Vector3(-2f, -1f, 2f);

            GameObject camObj = new GameObject("Main Camera");
            Camera cam = camObj.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.transform.position = new Vector3(0.1f, 0.85f, -7.35f);
            cam.transform.rotation = Quaternion.Euler(9f, 0f, 0f);
            cam.fieldOfView = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.16f, 0.23f, 1f);
        }

        private static Material MakeMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Diffuse");

            Material mat = new Material(shader);
            mat.name = name;
            SetColor(mat, color);
            return mat;
        }

        private static Material MakeTransparentMaterial(string name, Color color)
        {
            Material mat = MakeMaterial(name, color);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Mode", 3f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = 3000;
            SetColor(mat, color);
            return mat;
        }

        private static void SetColor(Material mat, Color color)
        {
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }
            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", color);
            }
        }

        private static GameObject MeshObject(string name, Transform parent, Vector3 localPosition, Quaternion localRotation, Mesh mesh, Material mat)
        {
            GameObject go = new GameObject(name);
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;

            MeshFilter filter = go.AddComponent<MeshFilter>();
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = mat;
            return go;
        }

        private static GameObject BoatHull(string name, Transform parent, Vector3 localPosition, Quaternion localRotation, float length, float width, float height, Material mat)
        {
            return MeshObject(name, parent, localPosition, localRotation, CreateBoatHullMesh(name + "_Mesh", length, width, height), mat);
        }

        private static GameObject RoundedTopShell(string name, Transform parent, Vector3 localPosition, Quaternion localRotation, float length, float width, float height, Material mat)
        {
            return MeshObject(name, parent, localPosition, localRotation, CreateRoundedTopShellMesh(name + "_Mesh", length, width, height), mat);
        }

        private static GameObject TorpedoHull(string name, Transform parent, Vector3 localPosition, Quaternion localRotation, float length, float maxRadius, Material mat)
        {
            return MeshObject(name, parent, localPosition, localRotation, CreateTorpedoHullMesh(name + "_Mesh", length, maxRadius, 22, 28), mat);
        }

        private static Mesh CreateBoatHullMesh(string meshName, float length, float width, float height)
        {
            const int sectionCount = 5;
            const int profileCount = 7;
            Vector3[] vertices = new Vector3[sectionCount * profileCount];
            float[] xs = { -0.5f, -0.36f, 0.05f, 0.42f, 0.5f };
            float[] beam = { 0.2f, 0.9f, 1f, 0.95f, 0.62f };
            float[] draft = { 0.52f, 0.9f, 1f, 0.92f, 0.74f };

            for (int i = 0; i < sectionCount; i++)
            {
                float x = xs[i] * length;
                float halfWidth = width * 0.5f * beam[i];
                float topY = height * 0.28f;
                float sideY = -height * 0.1f * draft[i];
                float chineY = -height * 0.38f * draft[i];
                float keelY = -height * 0.56f * draft[i];

                int start = i * profileCount;
                vertices[start] = new Vector3(x, topY, -halfWidth);
                vertices[start + 1] = new Vector3(x, sideY, -halfWidth);
                vertices[start + 2] = new Vector3(x, chineY, -halfWidth * 0.62f);
                vertices[start + 3] = new Vector3(x, keelY, 0f);
                vertices[start + 4] = new Vector3(x, chineY, halfWidth * 0.62f);
                vertices[start + 5] = new Vector3(x, sideY, halfWidth);
                vertices[start + 6] = new Vector3(x, topY, halfWidth);
            }

            List<int> triangles = new List<int>();
            AddExtrudedSurfaceTriangles(triangles, sectionCount, profileCount);

            Mesh mesh = new Mesh();
            mesh.name = meshName;
            mesh.vertices = vertices;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateRoundedTopShellMesh(string meshName, float length, float width, float height)
        {
            const int sectionCount = 4;
            const int profileCount = 7;
            Vector3[] vertices = new Vector3[sectionCount * profileCount];
            float[] xs = { -0.5f, -0.38f, 0.38f, 0.5f };
            float[] widthScale = { 0.88f, 1f, 1f, 0.88f };
            float[] heightScale = { 0.86f, 1f, 1f, 0.86f };

            for (int i = 0; i < sectionCount; i++)
            {
                float x = xs[i] * length;
                float halfWidth = width * 0.5f * widthScale[i];
                float bottomY = -height * 0.5f;
                float sideY = -height * 0.12f;
                float shoulderY = height * 0.28f * heightScale[i];
                float crownY = height * 0.5f * heightScale[i];

                int start = i * profileCount;
                vertices[start] = new Vector3(x, bottomY, -halfWidth);
                vertices[start + 1] = new Vector3(x, sideY, -halfWidth);
                vertices[start + 2] = new Vector3(x, shoulderY, -halfWidth * 0.72f);
                vertices[start + 3] = new Vector3(x, crownY, 0f);
                vertices[start + 4] = new Vector3(x, shoulderY, halfWidth * 0.72f);
                vertices[start + 5] = new Vector3(x, sideY, halfWidth);
                vertices[start + 6] = new Vector3(x, bottomY, halfWidth);
            }

            List<int> triangles = new List<int>();
            AddExtrudedSurfaceTriangles(triangles, sectionCount, profileCount);

            Mesh mesh = new Mesh();
            mesh.name = meshName;
            mesh.vertices = vertices;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddExtrudedSurfaceTriangles(List<int> triangles, int sectionCount, int profileCount)
        {
            for (int i = 0; i < sectionCount - 1; i++)
            {
                for (int j = 0; j < profileCount; j++)
                {
                    int nextJ = (j + 1) % profileCount;
                    int a = i * profileCount + j;
                    int b = i * profileCount + nextJ;
                    int c = (i + 1) * profileCount + j;
                    int d = (i + 1) * profileCount + nextJ;
                    AddQuad(triangles, a, b, c, d);
                }
            }

            int rearStart = (sectionCount - 1) * profileCount;
            for (int j = 1; j < profileCount - 1; j++)
            {
                triangles.Add(0);
                triangles.Add(j + 1);
                triangles.Add(j);

                triangles.Add(rearStart);
                triangles.Add(rearStart + j);
                triangles.Add(rearStart + j + 1);
            }
        }

        private static void AddQuad(List<int> triangles, int a, int b, int c, int d)
        {
            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(b);
            triangles.Add(b);
            triangles.Add(c);
            triangles.Add(d);
        }

        private static Mesh CreateTorpedoHullMesh(string meshName, float length, float maxRadius, int lengthSegments, int radialSegments)
        {
            int ringCount = lengthSegments + 1;
            int capVertexCount = 2;
            Vector3[] vertices = new Vector3[ringCount * radialSegments + capVertexCount];
            int[] triangles = new int[lengthSegments * radialSegments * 6 + radialSegments * 6];

            for (int i = 0; i < ringCount; i++)
            {
                float t = i / (float)lengthSegments;
                float x = Mathf.Lerp(-length * 0.5f, length * 0.5f, t);
                float radius = TorpedoRadiusProfile(t, maxRadius);

                for (int j = 0; j < radialSegments; j++)
                {
                    float angle = j * Mathf.PI * 2f / radialSegments;
                    float y = Mathf.Cos(angle) * radius;
                    float z = Mathf.Sin(angle) * radius;
                    vertices[i * radialSegments + j] = new Vector3(x, y, z);
                }
            }

            int tri = 0;
            for (int i = 0; i < lengthSegments; i++)
            {
                for (int j = 0; j < radialSegments; j++)
                {
                    int nextJ = (j + 1) % radialSegments;
                    int a = i * radialSegments + j;
                    int b = i * radialSegments + nextJ;
                    int c = (i + 1) * radialSegments + j;
                    int d = (i + 1) * radialSegments + nextJ;

                    triangles[tri++] = a;
                    triangles[tri++] = c;
                    triangles[tri++] = b;
                    triangles[tri++] = b;
                    triangles[tri++] = c;
                    triangles[tri++] = d;
                }
            }

            int noseCenter = ringCount * radialSegments;
            int tailCenter = noseCenter + 1;
            vertices[noseCenter] = new Vector3(-length * 0.5f, 0f, 0f);
            vertices[tailCenter] = new Vector3(length * 0.5f, 0f, 0f);

            for (int j = 0; j < radialSegments; j++)
            {
                int nextJ = (j + 1) % radialSegments;

                triangles[tri++] = noseCenter;
                triangles[tri++] = j;
                triangles[tri++] = nextJ;

                int tailA = lengthSegments * radialSegments + j;
                int tailB = lengthSegments * radialSegments + nextJ;
                triangles[tri++] = tailCenter;
                triangles[tri++] = tailB;
                triangles[tri++] = tailA;
            }

            Mesh mesh = new Mesh();
            mesh.name = meshName;
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float TorpedoRadiusProfile(float t, float maxRadius)
        {
            if (t < 0.085f)
            {
                float s = Mathf.Clamp01(t / 0.085f);
                return Mathf.Lerp(maxRadius * 0.74f, maxRadius, Mathf.SmoothStep(0f, 1f, s));
            }

            if (t > 0.86f)
            {
                float s = Mathf.Clamp01((1f - t) / 0.14f);
                return Mathf.Lerp(maxRadius * 0.3f, maxRadius, Mathf.SmoothStep(0f, 1f, s));
            }

            return maxRadius;
        }

        private static GameObject Cube(string name, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Material mat)
        {
            return Primitive(name, PrimitiveType.Cube, parent, localPosition, localRotation, localScale, mat);
        }

        private static GameObject Sphere(string name, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Material mat)
        {
            return Primitive(name, PrimitiveType.Sphere, parent, localPosition, localRotation, localScale, mat);
        }

        private static GameObject Cylinder(string name, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Material mat)
        {
            return Primitive(name, PrimitiveType.Cylinder, parent, localPosition, localRotation, localScale, mat);
        }

        private static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = localScale;

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = mat;
            }

            return go;
        }

        private static TextMesh Text(string name, string value, Vector3 position, float size)
        {
            GameObject go = new GameObject(name);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(18f, 180f, 0f);
            TextMesh text = go.AddComponent<TextMesh>();
            text.text = value;
            text.fontSize = 36;
            text.characterSize = size;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            go.AddComponent<CameraFacingText>();
            return text;
        }
    }
}
