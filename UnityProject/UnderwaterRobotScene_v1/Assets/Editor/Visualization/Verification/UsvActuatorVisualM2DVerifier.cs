using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Usv;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    public static class UsvActuatorVisualM2DVerifier
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
        private const string RuntimeDirectory =
            "Assets/Scripts/Visualization/Runtime/Usv/";
        private const string BuilderPath =
            "Assets/Editor/UnderwaterSceneBuilder.cs";

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

        private sealed class Check
        {
            public string Name;
            public string Detail;
        }

        [MenuItem("Tools/Underwater Demo/M2-D/Verify USV Actuator Visual")]
        public static void RunFromMenu()
        {
            Execute();
        }

        public static void RunBatch()
        {
            Execute();
            Debug.Log("M2D_USV_ACTUATOR_VISUAL_STATIC_VALIDATION_PASS");
        }

        private static void Execute()
        {
            var checks = new List<Check>();
            VerifyMapper(checks);
            VerifyTypeAndSourceBoundaries(checks);

            string sceneShaBefore = Sha256(ScenePath);
            bool installerChanged = UsvActuatorVisualM2DSceneInstaller.Install();
            string sceneShaAfter = Sha256(ScenePath);
            Require(!installerChanged &&
                    string.Equals(
                        sceneShaBefore,
                        sceneShaAfter,
                        StringComparison.Ordinal),
                "Idempotent M2-D Installer changed the authoritative Scene.");
            Add(checks, "Installer idempotency",
                "Existing Scene returned changed=false and retained SHA " +
                sceneShaAfter + ".");

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Require(scene.IsValid() && scene.isLoaded && !scene.isDirty,
                "The authoritative M2-D Scene is invalid, unloaded, or dirty.");
            GameObject usv = FindUniqueRoot(scene, UsvName);
            GameObject driverObject = FindUniqueRoot(scene, DriverName);
            Transform visualRoot = RequireDirectChild(usv.transform, VisualRootName);
            Transform model = RequireDirectChild(visualRoot, ModelName);
            Require(usv.transform.childCount == 1 &&
                    visualRoot.childCount == 1 &&
                    IsIdentity(visualRoot),
                "USV canonical business/visual/model hierarchy changed.");
            Add(checks, "Canonical M2-C hierarchy",
                "Business root has one identity visual root and one imported model.");

            Transform main = RequireUniqueDescendant(model, MainName);
            Transform pivot = RequireDirectChild(main, PivotName);
            Transform[] moving = DirectChildren(pivot);
            Transform[] fixedChildren = DirectChildren(main)
                .Where(child => child != pivot)
                .ToArray();
            Require(moving.Length == MovingNames.Length &&
                    SetEquals(moving, MovingNames) &&
                    fixedChildren.Length == FixedNames.Length &&
                    SetEquals(fixedChildren, FixedNames),
                "Frozen 10 moving / 8 fixed rudder hierarchy changed.");
            Require(Near(pivot.localRotation, Quaternion.identity, 0.000001f) &&
                    Near(pivot.localScale, Vector3.one, 0.000001f) &&
                    SourceFileId(pivot.gameObject) == PivotGameObjectSourceFileId &&
                    SourceFileId(pivot) == PivotTransformSourceFileId,
                "Pivot neutral Transform or source fileIDs changed.");
            Add(checks, "Rudder asset foundation",
                "Pivot source GO/Transform -4973267675681198602/6121043653356691839; ten moving and eight fixed objects are exact.");

            PropellerSpinner[] spinners =
                model.GetComponentsInChildren<PropellerSpinner>(true);
            Require(spinners.Length == 2,
                "Expected exactly two USV PropellerSpinner components.");
            PropellerSpinner port = RequireSpinner(
                usv.transform,
                model,
                spinners,
                "USV_Right_Surface_Thruster/USV_Right_Propeller_RotatingPart",
                PortGameObjectSourceFileId,
                PortTransformSourceFileId,
                true);
            PropellerSpinner starboard = RequireSpinner(
                usv.transform,
                model,
                spinners,
                "USV_Left_Surface_Thruster/USV_Left_Propeller_RotatingPart",
                StarboardGameObjectSourceFileId,
                StarboardTransformSourceFileId,
                false);
            Require(port.enabled &&
                    starboard.enabled &&
                    Near(port.localAxis.normalized, Vector3.right, 0.000001f) &&
                    Near(starboard.localAxis.normalized, Vector3.right, 0.000001f) &&
                    Near(port.rpm, 740f) &&
                    Near(starboard.rpm, 740f),
                "USV Spinner enabled/axis/serialized baseline contract changed.");
            Add(checks, "Stable Port/Starboard roles",
                "Port is root +Z/right-named source, starboard is root -Z/left-named source; both retain local +X and serialized 740 rpm.");

            UsvActuatorVisualCoordinator[] coordinators =
                FindSceneComponents<UsvActuatorVisualCoordinator>(scene);
            Require(coordinators.Length == 1 &&
                    coordinators[0].transform == usv.transform,
                "Expected one Coordinator on USV_Blue_Surface.");
            UsvActuatorVisualCoordinator coordinator = coordinators[0];
            VehiclePoseControlAuthority authority =
                RequireComponent<VehiclePoseControlAuthority>(
                    usv,
                    "USV Authority");
            VehiclePoseDriver driver =
                RequireComponent<VehiclePoseDriver>(
                    driverObject,
                    "USV pose Driver");
            Require(coordinator.enabled &&
                    coordinator.BusinessRoot == usv.transform &&
                    coordinator.PortVisualThruster == port &&
                    coordinator.StarboardVisualThruster == starboard &&
                    coordinator.RudderVisualPivot == pivot &&
                    coordinator.PoseDriver == driver &&
                    coordinator.ControlAuthority == authority &&
                    driver.TargetRoot == usv.transform &&
                    driver.ControlAuthority == authority,
                "Coordinator explicit binding contract changed.");
            VerifyTuning(coordinator);
            Add(checks, "Coordinator Scene contract",
                "Unique root-mounted Coordinator has six explicit bindings, mode 1 and all frozen VisualOnly tuning values.");

            Require(usv.GetComponents<Component>().All(component =>
                    component is Transform ||
                    component is VehiclePoseControlAuthority ||
                    component is UsvActuatorVisualCoordinator),
                "Business root contains an unexpected component or writer.");
            UsvSurfaceVisualController[] visualControllers =
                FindSceneComponents<UsvSurfaceVisualController>(scene);
            Require(visualControllers.Length == 1 &&
                    visualControllers[0].transform == visualRoot &&
                    visualRoot.GetComponents<Component>().All(component =>
                        component is Transform ||
                        component is UsvSurfaceVisualController),
                "Visual root single-writer boundary changed.");
            Require(model.GetComponent<UsvActuatorVisualCoordinator>() == null &&
                    model.GetComponent<UsvSurfaceVisualController>() == null &&
                    main.GetComponent<PropellerSpinner>() == null &&
                    main.GetComponent<Animator>() == null &&
                    pivot.GetComponents<Component>().All(component =>
                        component is Transform) &&
                    fixedChildren.All(child =>
                        child.GetComponent<PropellerSpinner>() == null &&
                        child.GetComponent<Animator>() == null &&
                        child.GetComponent<UsvActuatorVisualCoordinator>() == null),
                "Imported root, Main, Pivot or fixed subset received another writer.");
            Add(checks, "Single-writer boundaries",
                "Business, visual, Spinner rotation, Spinner rpm and Pivot rotation ownership remain disjoint.");

            foreach (string siblingName in new[]
                     {
                         "AUV_Yellow_Underwater",
                         "ROV_Box_Seabed"
                     })
            {
                GameObject sibling = FindUniqueRoot(scene, siblingName);
                Require(
                    sibling.GetComponentInChildren<UsvActuatorVisualCoordinator>(true) ==
                    null,
                    siblingName + " received an M2-D component.");
            }
            Require(!HasMissingScripts(scene),
                "Scene contains a Missing Script.");
            Require(!scene.isDirty,
                "M2-D Static verification dirtied the Scene.");
            Add(checks, "Scene integrity and isolation",
                "No Missing Script, no M2-D component on AUV/ROV, and Scene remained clean.");

            WriteReport(checks, scene, sceneShaAfter);
            foreach (Check check in checks)
            {
                Debug.Log(
                    "M2D_STATIC_CHECK_PASS | " +
                    check.Name +
                    " | " +
                    check.Detail);
            }
        }

        private static void VerifyMapper(ICollection<Check> checks)
        {
            UsvActuatorVisualConfig config = Config();
            Require(Map(0f, 0f, config, out UsvActuatorVisualTargets stationary) &&
                    Equal(stationary, default),
                "Mapper stationary output is not zero.");
            Require(Map(0.02f, 0f, config, out UsvActuatorVisualTargets deadband) &&
                    Equal(deadband, default),
                "Mapper speed deadband output is not zero.");
            Require(Map(0.6f, 0f, config, out UsvActuatorVisualTargets cruise) &&
                    Near(cruise.PortRpm, 520f) &&
                    Near(cruise.StarboardRpm, 520f) &&
                    Near(cruise.RudderDegrees, 0f),
                "Mapper full-scale cruise output is incorrect.");
            Require(Map(0.6f, 90f, config, out UsvActuatorVisualTargets positive) &&
                    Near(positive.PortRpm, 740f) &&
                    Near(positive.StarboardRpm, 300f) &&
                    Near(positive.RudderDegrees, 25f),
                "Mapper positive-yaw sign or clamp is incorrect.");
            Require(Map(0.6f, -90f, config, out UsvActuatorVisualTargets negative) &&
                    Near(negative.PortRpm, 300f) &&
                    Near(negative.StarboardRpm, 740f) &&
                    Near(negative.RudderDegrees, -25f),
                "Mapper negative-yaw sign or clamp is incorrect.");
            Require(Map(0.6f, 2f, config, out UsvActuatorVisualTargets yawDeadband) &&
                    Near(yawDeadband.PortRpm, 520f) &&
                    Near(yawDeadband.StarboardRpm, 520f) &&
                    Near(yawDeadband.RudderDegrees, 0f),
                "Mapper yaw deadband output is incorrect.");
            Require(Map(0.6f, 900f, config, out UsvActuatorVisualTargets yawClamp) &&
                    Equal(yawClamp, positive),
                "Mapper yaw activity did not clamp.");
            Require(Map(0.025f, 90f, config, out UsvActuatorVisualTargets lowSpeed) &&
                    Near(lowSpeed.PortRpm, lowSpeed.StarboardRpm) &&
                    Near(lowSpeed.RudderDegrees, 0f),
                "Mapper low-speed gate did not suppress differential and rudder.");
            Require(Map(-0.6f, 90f, config, out UsvActuatorVisualTargets reverse) &&
                    Equal(reverse, positive),
                "Mapper did not apply abs(forwardSpeed).");
            Require(!Map(float.NaN, 0f, config, out UsvActuatorVisualTargets invalidInput) &&
                    Equal(invalidInput, default),
                "Mapper accepted non-finite input or returned non-zero targets.");
            var invalidConfig = new UsvActuatorVisualConfig(
                0.02f,
                0.02f,
                2f,
                90f,
                120f,
                520f,
                740f,
                220f,
                0.03f,
                0.08f,
                25f);
            Require(!Map(0.6f, 90f, invalidConfig, out UsvActuatorVisualTargets invalid) &&
                    Equal(invalid, default),
                "Mapper accepted invalid configuration.");
            Require(Map(0.37f, -41f, config, out UsvActuatorVisualTargets first) &&
                    Map(0.37f, -41f, config, out UsvActuatorVisualTargets second) &&
                    Equal(first, second),
                "Mapper repeated call is not deterministic.");
            Add(checks, "Pure deterministic Mapper",
                "Stationary/deadbands/full scale/±yaw/low-speed gate/clamps/nonfinite/invalid config/repeat all passed.");
        }

        private static void VerifyTypeAndSourceBoundaries(
            ICollection<Check> checks)
        {
            Type mapper = typeof(DeterministicUsvActuatorVisualMapper);
            Type coordinator = typeof(UsvActuatorVisualCoordinator);
            DefaultExecutionOrder order =
                coordinator.GetCustomAttribute<DefaultExecutionOrder>();
            Require(!typeof(MonoBehaviour).IsAssignableFrom(mapper) &&
                    coordinator.GetCustomAttribute<DisallowMultipleComponent>() != null &&
                    order != null &&
                    order.order == 1050 &&
                    (int)UsvActuatorVisualMode.Disabled == 0 &&
                    (int)UsvActuatorVisualMode
                        .DemoAndLocalDiagnosticPublicData == 1,
                "M2-D type, execution-order or mode contract changed.");

            string mapperSource = File.ReadAllText(
                RuntimeDirectory +
                "DeterministicUsvActuatorVisualMapper.cs");
            Require(!mapperSource.Contains("UnityEngine") &&
                    !mapperSource.Contains("MonoBehaviour") &&
                    !mapperSource.Contains("Transform") &&
                    !mapperSource.Contains("Time.") &&
                    !mapperSource.Contains("Authority") &&
                    !mapperSource.Contains("VehiclePoseDriver") &&
                    !mapperSource.Contains("PropellerSpinner"),
                "Mapper crossed a frozen purity boundary.");

            string coordinatorSource = File.ReadAllText(
                RuntimeDirectory +
                "UsvActuatorVisualCoordinator.cs");
            Require(!coordinatorSource.Contains("GameObject.Find") &&
                    !coordinatorSource.Contains("FindObjects") &&
                    !coordinatorSource.Contains("GetComponents") &&
                    !coordinatorSource.Contains("System.Linq") &&
                    !coordinatorSource.Contains("VehicleState") &&
                    !coordinatorSource.Contains("GeneratorKind") &&
                    coordinatorSource.Contains("businessRoot.right") &&
                    coordinatorSource.Contains(
                        "Quaternion.AngleAxis(currentRudderDegrees, Vector3.up)") &&
                    coordinatorSource.Contains(
                        "UsvActuatorVisualMode.Disabled;"),
                "Coordinator crossed hot-path, input-axis or default-gate boundary.");

            Require(File.Exists(BuilderPath) &&
                    !File.ReadAllText(BuilderPath)
                        .Contains("UsvActuatorVisualM2D"),
                "Complete Scene Builder was modified to include M2-D.");
            Add(checks, "Type/source/Builder boundary",
                "Pure Mapper, order 1050, explicit Disabled default, +X observer, local +Y Pivot and unchanged Builder passed.");
        }

        private static void VerifyTuning(
            UsvActuatorVisualCoordinator value)
        {
            Require(value.Mode ==
                        UsvActuatorVisualMode
                            .DemoAndLocalDiagnosticPublicData &&
                    Near(value.SpeedDeadbandMetersPerSecond, 0.02f) &&
                    Near(value.SpeedFullScaleMetersPerSecond, 0.60f) &&
                    Near(value.YawDeadbandDegreesPerSecond, 2f) &&
                    Near(value.YawFullScaleDegreesPerSecond, 90f) &&
                    Near(value.MinVisibleRpm, 120f) &&
                    Near(value.CruiseRpm, 520f) &&
                    Near(value.MaxVisualRpm, 740f) &&
                    Near(value.MaxDifferentialRpm, 220f) &&
                    Near(value.LowSpeedOffMetersPerSecond, 0.03f) &&
                    Near(value.LowSpeedFullMetersPerSecond, 0.08f) &&
                    Near(value.MaxVisualRudderDegrees, 25f) &&
                    Near(value.RpmRiseRate, 1600f) &&
                    Near(value.RpmFallRate, 2200f) &&
                    Near(value.RudderSlewRateDegreesPerSecond, 90f) &&
                    Near(value.MaxAcceptedDeltaTimeSeconds, 0.25f) &&
                    Near(value.TeleportDistanceThresholdMeters, 0.25f) &&
                    Near(value.RotationJumpThresholdDegrees, 30f),
                "M2-D serialized mode or tuning differs from the frozen contract.");
        }

        private static UsvActuatorVisualConfig Config()
        {
            return new UsvActuatorVisualConfig(
                0.02f,
                0.60f,
                2f,
                90f,
                120f,
                520f,
                740f,
                220f,
                0.03f,
                0.08f,
                25f);
        }

        private static bool Map(
            float speed,
            float yawRate,
            in UsvActuatorVisualConfig config,
            out UsvActuatorVisualTargets targets)
        {
            return DeterministicUsvActuatorVisualMapper.TryMap(
                speed,
                yawRate,
                in config,
                out targets);
        }

        private static PropellerSpinner RequireSpinner(
            Transform businessRoot,
            Transform model,
            IEnumerable<PropellerSpinner> spinners,
            string path,
            long gameObjectSourceId,
            long transformSourceId,
            bool port)
        {
            PropellerSpinner[] matches = spinners
                .Where(item => string.Equals(
                    RelativePath(model, item.transform),
                    path,
                    StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1,
                "Spinner role path is missing or ambiguous: " + path + ".");
            PropellerSpinner spinner = matches[0];
            Vector3 rootPosition =
                businessRoot.InverseTransformPoint(spinner.transform.position);
            Require(SourceFileId(spinner.gameObject) == gameObjectSourceId &&
                    SourceFileId(spinner.transform) == transformSourceId &&
                    (port ? rootPosition.z > 0f : rootPosition.z < 0f),
                "Spinner source fileID or root-space role changed.");
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
            Require(current == root,
                "Transform is not below the imported model root.");
            return string.Join("/", names);
        }

        private static long SourceFileId(UnityEngine.Object instance)
        {
            UnityEngine.Object source =
                PrefabUtility.GetCorrespondingObjectFromSource(instance);
            Require(source != null,
                "Imported object has no corresponding FBX source.");
            return unchecked((long)GlobalObjectId
                .GetGlobalObjectIdSlow(source)
                .targetObjectId);
        }

        private static bool HasMissingScripts(Scene scene)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<Transform>(true))
                .Any(item =>
                    item.GetComponents<Component>()
                        .Any(component => component == null));
        }

        private static T[] FindSceneComponents<T>(Scene scene)
            where T : Component
        {
            return UnityEngine.Object.FindObjectsByType<T>(
                    FindObjectsInactive.Include)
                .Where(item => item.gameObject.scene == scene)
                .ToArray();
        }

        private static GameObject FindUniqueRoot(Scene scene, string name)
        {
            GameObject[] matches = scene.GetRootGameObjects()
                .Where(item => string.Equals(
                    item.name,
                    name,
                    StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1,
                "Expected one Scene root named " + name + ".");
            return matches[0];
        }

        private static Transform RequireDirectChild(
            Transform parent,
            string name)
        {
            Transform[] matches = DirectChildren(parent)
                .Where(child => string.Equals(
                    child.name,
                    name,
                    StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1,
                "Expected one direct child named " + name + ".");
            return matches[0];
        }

        private static Transform RequireUniqueDescendant(
            Transform root,
            string name)
        {
            Transform[] matches = root.GetComponentsInChildren<Transform>(true)
                .Where(item => string.Equals(
                    item.name,
                    name,
                    StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1,
                "Expected one descendant named " + name + ".");
            return matches[0];
        }

        private static Transform[] DirectChildren(Transform parent)
        {
            return Enumerable.Range(0, parent.childCount)
                .Select(index => parent.GetChild(index))
                .ToArray();
        }

        private static bool SetEquals(
            IEnumerable<Transform> values,
            IEnumerable<string> expected)
        {
            return new HashSet<string>(
                    values.Select(item => item.name),
                    StringComparer.Ordinal)
                .SetEquals(expected);
        }

        private static T RequireComponent<T>(
            GameObject value,
            string label)
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
            UsvActuatorVisualTargets left,
            UsvActuatorVisualTargets right)
        {
            return left.PortRpm.Equals(right.PortRpm) &&
                   left.StarboardRpm.Equals(right.StarboardRpm) &&
                   left.RudderDegrees.Equals(right.RudderDegrees);
        }

        private static bool Near(
            float left,
            float right,
            float tolerance = 0.0001f)
        {
            return Mathf.Abs(left - right) <= tolerance;
        }

        private static bool Near(
            Vector3 left,
            Vector3 right,
            float tolerance)
        {
            return (left - right).sqrMagnitude <= tolerance * tolerance;
        }

        private static bool Near(
            Quaternion left,
            Quaternion right,
            float tolerance)
        {
            return Mathf.Abs(Quaternion.Dot(
                       left.normalized,
                       right.normalized)) >=
                   1f - tolerance;
        }

        private static string Sha256(string path)
        {
            using var stream = File.OpenRead(path);
            using SHA256 hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(stream))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        private static void WriteReport(
            IReadOnlyCollection<Check> checks,
            Scene scene,
            string sceneSha)
        {
            string configured =
                Environment.GetEnvironmentVariable("M2D_EVIDENCE_DIR");
            string directory = string.IsNullOrWhiteSpace(configured)
                ? Path.GetFullPath(Path.Combine(
                    Directory.GetParent(Application.dataPath).FullName,
                    "..",
                    "..",
                    "M2D_Validation"))
                : Path.GetFullPath(configured);
            Directory.CreateDirectory(directory);
            var text = new StringBuilder();
            text.AppendLine("# M2-D USV Actuator Visual Static Verification");
            text.AppendLine();
            text.AppendLine(
                "- Status: `M2D_USV_ACTUATOR_VISUAL_STATIC_VALIDATION_PASS`");
            text.AppendLine("- Scene: `" + scene.path + "`");
            text.AppendLine("- Scene SHA-256: `" + sceneSha + "`");
            text.AppendLine(
                "- Checks: `" + checks.Count + "/" + checks.Count + "`");
            text.AppendLine();
            foreach (Check check in checks)
            {
                text.AppendLine(
                    "- PASS — " + check.Name + ": " + check.Detail);
            }
            File.WriteAllText(
                Path.Combine(directory, "m2d_static_report.md"),
                text.ToString(),
                new UTF8Encoding(false));
        }

        private static void Add(
            ICollection<Check> checks,
            string name,
            string detail)
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
