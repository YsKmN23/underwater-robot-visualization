using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.LocalTesting;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    public static class ThreeVehiclePoseN6EVerifier
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";

        private sealed class ExpectedVehicle
        {
            public string Label;
            public string RootName;
            public string ModelName;
            public string HostName;
            public string DriverName;
            public string SourceId;
            public string VehicleId;
            public VehicleType VehicleType;
            public DeterministicVehicleStateGeneratorKind GeneratorKind;
            public string ProfileId;
            public SignedSemanticAxis Right;
            public SignedSemanticAxis Up;
            public SignedSemanticAxis Forward;
            public Vector3 Alignment;
            public int SpinnerCount;
            public Vector3 ModelPosition;
            public Quaternion ModelRotation;
            public Vector3 ModelScale;
        }

        private sealed class BoundVehicle
        {
            public ExpectedVehicle Expected;
            public GameObject Root;
            public Transform Model;
            public VehicleDataRuntimeHost Host;
            public VehiclePoseIntegrationConfiguration Configuration;
            public VehiclePoseProfileConfiguration Profile;
            public VehiclePoseDriver Driver;
            public VehiclePoseControlAuthority Authority;
            public PropellerSpinner[] Spinners;
        }

        private sealed class Check
        {
            public string Name;
            public string Detail;
        }

        [MenuItem("Tools/Underwater Demo/N6-E/Verify Three-Vehicle Coexistence")]
        public static void RunFromMenu()
        {
            Execute();
        }

        public static void RunBatch()
        {
            Execute();
            Debug.Log("N6E_THREE_VEHICLE_STATIC_VALIDATION_PASS");
        }

        private static void Execute()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Require(!scene.isDirty, "Scene became dirty when opened.");
            var checks = new List<Check>();
            DemoMotionController[] demos =
                UnityEngine.Object.FindObjectsByType<DemoMotionController>(
                    FindObjectsInactive.Include);
            Require(demos.Length == 1, "Expected one DemoMotionController.");
            DemoMotionController demo = demos[0];

            BoundVehicle[] vehicles = ExpectedVehicles()
                .Select(expected => Bind(scene, expected))
                .ToArray();
            BoundVehicle auv = vehicles.Single(item => item.Expected.VehicleType == VehicleType.Auv);
            BoundVehicle rov = vehicles.Single(item => item.Expected.VehicleType == VehicleType.Rov);
            BoundVehicle usv = vehicles.Single(item => item.Expected.VehicleType == VehicleType.Usv);

            Require(vehicles.Select(item => item.Configuration.SourceId).Distinct().Count() == 3,
                "SourceId collision detected.");
            Require(vehicles.Select(item => item.Configuration.VehicleId).Distinct().Count() == 3,
                "VehicleId collision detected.");
            Require(vehicles.Select(item => item.Host).Distinct().Count() == 3 &&
                    vehicles.Select(item => item.Configuration).Distinct().Count() == 3 &&
                    vehicles.Select(item => item.Profile).Distinct().Count() == 3 &&
                    vehicles.Select(item => item.Driver).Distinct().Count() == 3 &&
                    vehicles.Select(item => item.Authority).Distinct().Count() == 3 &&
                    vehicles.Select(item => item.Driver.TargetRoot).Distinct().Count() == 3,
                "One or more serialized runtime instances are shared.");
            Add(checks, "Unique identities and runtime instances",
                "Three unique SourceIds, VehicleIds, Hosts, Configurations, Profiles, Drivers, Authorities and targets.");

            VehiclePoseIntegrationConfiguration[] sceneConfigurations =
                UnityEngine.Object.FindObjectsByType<VehiclePoseIntegrationConfiguration>(
                        FindObjectsInactive.Include)
                    .Where(item => item.gameObject.scene == scene)
                    .ToArray();
            VehicleDataRuntimeHost[] sceneHosts =
                UnityEngine.Object.FindObjectsByType<VehicleDataRuntimeHost>(
                        FindObjectsInactive.Include)
                    .Where(item => item.gameObject.scene == scene)
                    .ToArray();
            VehiclePoseDriver[] sceneDrivers =
                UnityEngine.Object.FindObjectsByType<VehiclePoseDriver>(
                        FindObjectsInactive.Include)
                    .Where(item => item.gameObject.scene == scene)
                    .ToArray();
            Require(sceneConfigurations.Length == 3 &&
                    sceneHosts.Length == 3 &&
                    sceneDrivers.Length == 3,
                "Expected exactly three public pose Configurations, Hosts and Drivers.");
            Add(checks, "Scene composition cardinality",
                "Exactly three public pose Hosts, Configurations and Drivers are present.");

            Require(ReferenceEquals(demo.auvControlAuthority, auv.Authority) &&
                    ReferenceEquals(demo.rovControlAuthority, rov.Authority) &&
                    ReferenceEquals(demo.usvControlAuthority, usv.Authority) &&
                    !ReferenceEquals(demo.auvControlAuthority, demo.rovControlAuthority) &&
                    !ReferenceEquals(demo.auvControlAuthority, demo.usvControlAuthority) &&
                    !ReferenceEquals(demo.rovControlAuthority, demo.usvControlAuthority),
                "DemoMotionController authority references are not vehicle-local.");
            Require(auv.Authority.PublicDataOwnsControl &&
                    rov.Authority.PublicDataOwnsControl &&
                    usv.Authority.PublicDataOwnsControl &&
                    !demo.DrivesAuv &&
                    !demo.DrivesRov &&
                    !demo.DrivesUsv,
                "Initial PublicData ownership is not independent.");
            Add(checks, "Independent authority gates",
                "AUV, ROV and USV use separate authority fields and all start in PublicData mode.");

            foreach (BoundVehicle vehicle in vehicles)
            {
                ExpectedVehicle expected = vehicle.Expected;
                Require(vehicle.Configuration.SourceId == expected.SourceId &&
                        vehicle.Configuration.VehicleId == expected.VehicleId &&
                        vehicle.Configuration.VehicleType == expected.VehicleType &&
                        vehicle.Configuration.GeneratorKind == expected.GeneratorKind &&
                        vehicle.Configuration.TryValidate(out _),
                    expected.Label + " identity or generator configuration differs.");
                Require(vehicle.Profile.ProfileId == expected.ProfileId &&
                        vehicle.Profile.ModelRight == expected.Right &&
                        vehicle.Profile.ModelUp == expected.Up &&
                        vehicle.Profile.ModelForward == expected.Forward &&
                        Near(vehicle.Profile.ModelAlignmentEulerDegrees, expected.Alignment, 1e-6f),
                    expected.Label + " Profile differs.");
                Require(vehicle.Profile.TryBuildProfile(
                        out CoordinateTransformProfile profile,
                        out string profileError),
                    expected.Label + " Profile failed to build: " + profileError);
                Require(ReferenceEquals(vehicle.Host.IntegrationConfiguration, vehicle.Configuration) &&
                        ReferenceEquals(vehicle.Host.ProfileConfiguration, vehicle.Profile) &&
                        ReferenceEquals(vehicle.Driver.RuntimeHost, vehicle.Host) &&
                        ReferenceEquals(vehicle.Driver.IntegrationConfiguration, vehicle.Configuration) &&
                        ReferenceEquals(vehicle.Driver.ProfileConfiguration, vehicle.Profile) &&
                        ReferenceEquals(vehicle.Driver.ControlAuthority, vehicle.Authority) &&
                        ReferenceEquals(vehicle.Driver.TargetRoot, vehicle.Root.transform),
                    expected.Label + " serialized binding is incorrect.");
                Require(Near(vehicle.Model.localPosition, expected.ModelPosition, 1e-6f) &&
                        Near(vehicle.Model.localRotation, expected.ModelRotation, 1e-6f) &&
                        Near(vehicle.Model.localScale, expected.ModelScale, 1e-5f),
                    expected.Label + " imported model local Transform changed.");
                Require(vehicle.Spinners.Length == expected.SpinnerCount &&
                        vehicle.Spinners.All(spinner =>
                            spinner != null &&
                            spinner.enabled &&
                            spinner.transform.IsChildOf(vehicle.Model)),
                    expected.Label + " spinner count, enabled state or hierarchy changed.");
                ValidateZeroPoseAxes(vehicle, profile);
            }
            Require(!ReferenceEquals(auv.Profile, rov.Profile),
                "AUV and ROV incorrectly share one Profile instance.");
            Add(checks, "Profiles and targets isolated",
                "AUV/ROV retain independent Y -90 Profiles; USV uses its independent Y +90 Profile; each Driver targets its named vehicle root.");
            Add(checks, "Frozen model and actuator state",
                "AUV model/tail, ROV model/six spinners and USV model/two spinners match their accepted state.");

            Transform rudder = RequireUniqueDescendant(usv.Root.transform, "USV_Rudder_Main");
            Transform pivot =
                RequireUniqueDescendant(usv.Root.transform, "USV_Rudder_VisualPivot");
            Require(rudder.GetComponent<PropellerSpinner>() == null &&
                    rudder.GetComponent<Animator>() == null &&
                    pivot.parent == rudder,
                "USV rudder has an unexpected writer.");
            foreach (string environmentName in new[]
                     {
                         "USV_Blue_Label",
                         "USV_Local_ReflectionProbe",
                         "USV_Material_KeyLight"
                     })
            {
                GameObject environment = FindUniqueRoot(scene, environmentName);
                Require(!environment.transform.IsChildOf(usv.Root.transform),
                    environmentName + " is inside the USV movement root.");
            }
            Add(checks, "USV local/environment boundary",
                "Main and fixed rudder structure remain static; the approved Pivot may rotate, and environment objects remain outside the movement root.");

            Require(!HasMissingScripts(scene), "Scene contains a Missing Script.");
            Require(!scene.isDirty, "Static verification dirtied the scene.");
            Add(checks, "Scene integrity",
                "No Missing Script; verification left the authoritative Scene clean.");
            WriteReport(checks, scene, vehicles);
        }

        private static BoundVehicle Bind(Scene scene, ExpectedVehicle expected)
        {
            GameObject root = FindUniqueRoot(scene, expected.RootName);
            GameObject hostObject = FindUniqueRoot(scene, expected.HostName);
            GameObject driverObject = FindUniqueRoot(scene, expected.DriverName);
            Transform model = RequireUniqueDescendant(root.transform, expected.ModelName);
            var bound = new BoundVehicle
            {
                Expected = expected,
                Root = root,
                Model = model,
                Host = RequireComponent<VehicleDataRuntimeHost>(hostObject, expected.Label + " Host"),
                Configuration = RequireComponent<VehiclePoseIntegrationConfiguration>(
                    hostObject, expected.Label + " Configuration"),
                Profile = RequireComponent<VehiclePoseProfileConfiguration>(
                    driverObject, expected.Label + " Profile"),
                Driver = RequireComponent<VehiclePoseDriver>(
                    driverObject, expected.Label + " Driver"),
                Authority = RequireComponent<VehiclePoseControlAuthority>(
                    root, expected.Label + " Authority"),
                Spinners = root.GetComponentsInChildren<PropellerSpinner>(true)
            };
            return bound;
        }

        private static ExpectedVehicle[] ExpectedVehicles()
        {
            return new[]
            {
                new ExpectedVehicle
                {
                    Label = "AUV",
                    RootName = "AUV_Yellow_Underwater",
                    ModelName = "AUV_FineModel_V1_Imported",
                    HostName = "AUV_PublicData_RuntimeHost",
                    DriverName = "AUV_PublicPoseDriver",
                    SourceId = "local-test-n5",
                    VehicleId = "AUV-01",
                    VehicleType = VehicleType.Auv,
                    GeneratorKind = DeterministicVehicleStateGeneratorKind.AuvIntegrationTrajectory,
                    ProfileId = "N5_LOCAL_TEST_UNITY_AUV_Y_MINUS_90",
                    Right = SignedSemanticAxis.NegativeZ,
                    Up = SignedSemanticAxis.PositiveY,
                    Forward = SignedSemanticAxis.PositiveX,
                    Alignment = new Vector3(0f, -90f, 0f),
                    SpinnerCount = 1,
                    ModelPosition = Vector3.zero,
                    ModelRotation = Quaternion.identity,
                    ModelScale = Vector3.one * 100f
                },
                new ExpectedVehicle
                {
                    Label = "ROV",
                    RootName = "ROV_Box_Seabed",
                    ModelName = "ROV_FineModel_V1_Imported",
                    HostName = "ROV_PublicData_RuntimeHost",
                    DriverName = "ROV_PublicPoseDriver",
                    SourceId = "local-test-rov-n6b",
                    VehicleId = "ROV-01",
                    VehicleType = VehicleType.Rov,
                    GeneratorKind = DeterministicVehicleStateGeneratorKind.RovDiagnosticTrajectory,
                    ProfileId = "ROV_LOCAL_TEST_UNITY_Y_MINUS_90",
                    Right = SignedSemanticAxis.NegativeZ,
                    Up = SignedSemanticAxis.PositiveY,
                    Forward = SignedSemanticAxis.PositiveX,
                    Alignment = new Vector3(0f, -90f, 0f),
                    SpinnerCount = 6,
                    ModelPosition = Vector3.zero,
                    ModelRotation = Quaternion.identity,
                    ModelScale = Vector3.one * 100f
                },
                new ExpectedVehicle
                {
                    Label = "USV",
                    RootName = "USV_Blue_Surface",
                    ModelName = "USV_FineModel_V1_Imported",
                    HostName = "USV_PublicData_RuntimeHost",
                    DriverName = "USV_PublicPoseDriver",
                    SourceId = "local-test-usv-n6d",
                    VehicleId = "USV-01",
                    VehicleType = VehicleType.Usv,
                    GeneratorKind = DeterministicVehicleStateGeneratorKind.UsvDiagnosticTrajectory,
                    ProfileId = "USV_LOCAL_TEST_UNITY_Y_PLUS_90",
                    Right = SignedSemanticAxis.PositiveZ,
                    Up = SignedSemanticAxis.PositiveY,
                    Forward = SignedSemanticAxis.NegativeX,
                    Alignment = new Vector3(0f, 90f, 0f),
                    SpinnerCount = 2,
                    ModelPosition = Vector3.zero,
                    ModelRotation = Quaternion.Euler(-90f, 180f, 0f),
                    ModelScale = Vector3.one * 100f
                }
            };
        }

        private static void ValidateZeroPoseAxes(
            BoundVehicle vehicle,
            CoordinateTransformProfile profile)
        {
            var state = new VehicleState(
                vehicle.Expected.VehicleId,
                vehicle.Expected.VehicleType,
                0UL,
                0UL,
                Vector3d.Zero,
                Quaterniond.Identity,
                Vector3d.Zero,
                Vector3d.Zero,
                Vector3d.Zero,
                VehicleStateFields.Position | VehicleStateFields.Orientation,
                WorldFrame.UnityWorld,
                BodyFrame.UnityBody);
            Require(VehiclePoseConverter.TryConvert(
                    state,
                    profile,
                    out ConvertedVehiclePose converted,
                    out ConversionError error),
                vehicle.Expected.Label + " zero pose conversion failed: " + error.Message);
            Require(UnityPoseAdapter.TryConvert(
                    converted.Position,
                    converted.Orientation,
                    out _,
                    out Quaternion rotation),
                vehicle.Expected.Label + " zero pose adapter failed.");
            if (vehicle.Expected.VehicleType == VehicleType.Usv)
            {
                Require(Vector3.Dot(rotation * Vector3.left, Vector3.forward) > 0.999999f &&
                        Vector3.Dot(rotation * Vector3.up, Vector3.up) > 0.999999f &&
                        Vector3.Dot(rotation * Vector3.forward, Vector3.right) > 0.999999f,
                    "USV zero-pose semantic axes changed.");
            }
            else
            {
                Require(Vector3.Dot(rotation * Vector3.right, Vector3.forward) > 0.999999f &&
                        Vector3.Dot(rotation * Vector3.up, Vector3.up) > 0.999999f &&
                        Vector3.Dot(rotation * Vector3.back, Vector3.right) > 0.999999f,
                    vehicle.Expected.Label + " zero-pose semantic axes changed.");
            }
        }

        private static bool HasMissingScripts(Scene scene)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Any(item => item.GetComponents<Component>().Any(component => component == null));
        }

        private static void WriteReport(
            IReadOnlyCollection<Check> checks,
            Scene scene,
            IEnumerable<BoundVehicle> vehicles)
        {
            string directory = EvidenceDirectory();
            Directory.CreateDirectory(directory);
            string sceneHash = Sha256(Path.GetFullPath(ScenePath));
            var markdown = new StringBuilder();
            markdown.AppendLine("# N6-E Three-Vehicle Static Coexistence Verification");
            markdown.AppendLine();
            markdown.AppendLine("- Status: `N6E_THREE_VEHICLE_STATIC_VALIDATION_PASS`");
            markdown.AppendLine("- Scene: `" + scene.path + "`");
            markdown.AppendLine("- Scene SHA-256: `" + sceneHash + "`");
            markdown.AppendLine("- Checks: `" + checks.Count + "/" + checks.Count + "`");
            markdown.AppendLine();
            foreach (BoundVehicle vehicle in vehicles)
            {
                markdown.AppendLine("- " + vehicle.Expected.Label +
                                    ": `" + vehicle.Configuration.SourceId + "` / `" +
                                    vehicle.Configuration.VehicleId + "` / `" +
                                    vehicle.Expected.ProfileId + "` / target root `" +
                                    vehicle.Root.name + "`");
            }
            markdown.AppendLine();
            foreach (Check check in checks)
            {
                markdown.AppendLine("- PASS — " + check.Name + ": " + check.Detail);
            }
            File.WriteAllText(
                Path.Combine(directory, "n6e_three_vehicle_static_report.md"),
                markdown.ToString(),
                new UTF8Encoding(false));
        }

        private static string EvidenceDirectory()
        {
            string configured = Environment.GetEnvironmentVariable("N6E_EVIDENCE_DIR");
            return !string.IsNullOrWhiteSpace(configured)
                ? Path.GetFullPath(configured)
                : Path.Combine(Path.GetTempPath(), "UnderwaterRobotScene", "N6E_Validation");
        }

        private static string Sha256(string path)
        {
            using (var algorithm = System.Security.Cryptography.SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return string.Concat(algorithm.ComputeHash(stream)
                    .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
            }
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
