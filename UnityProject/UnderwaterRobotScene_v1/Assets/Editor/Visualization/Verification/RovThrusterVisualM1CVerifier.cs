using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnderwaterRobotScene.Visualization.Runtime.Rov;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    public static class RovThrusterVisualM1CVerifier
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string CoordinatorSourcePath =
            "Assets/Scripts/Visualization/Runtime/Rov/RovThrusterVisualCoordinator.cs";
        private const string SpinnerSourcePath = "Assets/Scripts/PropellerSpinner.cs";
        private const string RovName = "ROV_Box_Seabed";
        private const ulong RovTransformFileId = 656059028UL;

        private sealed class ExpectedBinding
        {
            public RovThrusterVisualRole Role;
            public string Path;
            public ulong FileId;
            public Vector3 RootPosition;
            public Vector3 Axis;
            public float Rpm;
            public Func<RovThrusterVisualCoordinator, PropellerSpinner> GetSpinner;
        }

        private sealed class Check
        {
            public string Name;
            public string Detail;
        }

        [MenuItem("Tools/Underwater Demo/M1-C1/Verify ROV Visual Thruster Linkage")]
        public static void RunFromMenu()
        {
            Execute();
        }

        public static void RunBatch()
        {
            Execute();
            Debug.Log("M1C_ROV_THRUSTER_STATIC_VALIDATION_PASS");
        }

        private static void Execute()
        {
            var checks = new List<Check>();
            VerifyTypeContract(checks);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Require(!scene.isDirty, "Scene became dirty when opened for M1-C verification.");
            GameObject rov = FindUniqueRoot(scene, RovName);
            Require(
                GlobalObjectId.GetGlobalObjectIdSlow(rov.transform).targetObjectId ==
                RovTransformFileId,
                "ROV movement root Transform fileID changed.");

            RovThrusterVisualCoordinator[] coordinators =
                UnityEngine.Object.FindObjectsByType<RovThrusterVisualCoordinator>(
                        FindObjectsInactive.Include)
                    .Where(item => item.gameObject.scene == scene)
                    .ToArray();
            Require(coordinators.Length == 1,
                "Scene must contain exactly one ROV visual thruster Coordinator.");
            RovThrusterVisualCoordinator coordinator = coordinators[0];
            Require(coordinator.gameObject == rov,
                "Coordinator must be mounted directly on ROV_Box_Seabed.");
            Add(checks, "Coordinator cardinality and root",
                "Exactly one Coordinator is mounted on Transform fileID 656059028.");

            VerifyBindings(rov.transform, coordinator, checks);
            VerifyConfiguration(coordinator, checks);
            VerifySceneBoundary(rov, coordinator, checks);
            VerifySourceBoundary(checks);
            VerifyCalculation(coordinator, checks);
            Require(!scene.isDirty, "Static M1-C verification dirtied the Scene.");

            foreach (Check check in checks)
            {
                Debug.Log("M1C_STATIC_CHECK_PASS | " + check.Name + " | " + check.Detail);
            }
        }

        private static void VerifyTypeContract(ICollection<Check> checks)
        {
            Require((int)RovThrusterVisualRole.SurgeVisualRight == 0 &&
                    (int)RovThrusterVisualRole.SurgeVisualLeft == 1 &&
                    (int)RovThrusterVisualRole.HeaveVisualRight == 2 &&
                    (int)RovThrusterVisualRole.HeaveVisualLeft == 3 &&
                    (int)RovThrusterVisualRole.SwayFront == 4 &&
                    (int)RovThrusterVisualRole.SwayRear == 5 &&
                    Enum.GetValues(typeof(RovThrusterVisualRole))
                        .Cast<RovThrusterVisualRole>()
                        .Select(item => (int)item)
                        .Distinct()
                        .Count() == 6,
                "RovThrusterVisualRole values changed or are not unique.");
            Type type = typeof(RovThrusterVisualCoordinator);
            Require(type.GetCustomAttribute<DisallowMultipleComponent>() != null,
                "Coordinator is missing DisallowMultipleComponent.");
            DefaultExecutionOrder executionOrder =
                type.GetCustomAttribute<DefaultExecutionOrder>();
            Require(executionOrder != null && executionOrder.order == 1000,
                "Coordinator DefaultExecutionOrder must be 1000.");
            Add(checks, "Stable role and execution contract",
                "Six explicit enum values, DisallowMultipleComponent and execution order 1000.");
        }

        private static void VerifyBindings(
            Transform rov,
            RovThrusterVisualCoordinator coordinator,
            ICollection<Check> checks)
        {
            ExpectedBinding[] expected = ExpectedBindings();
            PropellerSpinner[] actual = expected
                .Select(item => item.GetSpinner(coordinator))
                .ToArray();
            Require(actual.All(item => item != null) &&
                    actual.Distinct().Count() == 6,
                "Six Coordinator Spinner references must be non-null and unique.");
            Require(rov.GetComponentsInChildren<PropellerSpinner>(true).Length == 6,
                "ROV hierarchy must contain exactly six PropellerSpinner components.");

            foreach (ExpectedBinding item in expected)
            {
                Transform rotatingPart = rov.Find(item.Path);
                Require(rotatingPart != null,
                    "Missing exact rotating-part path " + item.Path + ".");
                PropellerSpinner spinner = item.GetSpinner(coordinator);
                Require(spinner.transform == rotatingPart,
                    item.Role + " is not bound to its exact rotating part.");
                Require(rotatingPart.GetComponents<PropellerSpinner>().Length == 1,
                    item.Role + " rotating part does not have exactly one Spinner.");
                Require(
                    GlobalObjectId.GetGlobalObjectIdSlow(spinner).targetObjectId ==
                    item.FileId,
                    item.Role + " Spinner fileID changed.");
                Require(Near(
                        rov.InverseTransformPoint(rotatingPart.position),
                        item.RootPosition,
                        0.002f),
                    item.Role + " root-space position changed.");
                Require(Near(spinner.localAxis, item.Axis, 0.000001f),
                    item.Role + " localAxis changed.");
                Require(Mathf.Abs(spinner.rpm - item.Rpm) <= 0.0001f,
                    item.Role + " original Scene RPM changed.");
            }

            Require(expected[0].Path.Contains("HorizontalLeft") &&
                    expected[0].RootPosition.z < 0f &&
                    expected[1].Path.Contains("HorizontalRight") &&
                    expected[1].RootPosition.z > 0f &&
                    expected[2].Path.Contains("VerticalLeft") &&
                    expected[2].RootPosition.z < 0f &&
                    expected[3].Path.Contains("VerticalRight") &&
                    expected[3].RootPosition.z > 0f,
                "The four known Left/Right visual-role reversals are not frozen.");
            Add(checks, "Six explicit role bindings",
                "Unique refs, exact paths/fileIDs/geometry/axes/RPM and four known reversals passed.");
        }

        private static void VerifyConfiguration(
            RovThrusterVisualCoordinator value,
            ICollection<Check> checks)
        {
            Require(Near(value.VisualIdleRpm, 0f) &&
                    Near(value.SurgeMaxVisualRpm, 720f) &&
                    Near(value.HeaveMaxVisualRpm, 680f) &&
                    Near(value.SwayMaxVisualRpm, 700f) &&
                    Near(value.LinearDeadZone, 0.005f) &&
                    Near(value.SurgeFullScaleSpeed, 0.35f) &&
                    Near(value.HeaveFullScaleSpeed, 0.15f) &&
                    Near(value.SwayFullScaleSpeed, 0.27f) &&
                    Near(value.AngularDeadZoneDegreesPerSecond, 0.5f) &&
                    Near(value.AngularFullScaleDegreesPerSecond, 30f) &&
                    Near(value.AngularGlobalWeight, 0.20f) &&
                    Near(value.RpmRiseRatePerSecond, 1800f) &&
                    Near(value.RpmFallRatePerSecond, 2400f) &&
                    Near(value.MaxValidDeltaTime, 0.25f) &&
                    Near(value.TeleportDistanceThreshold, 0.25f) &&
                    Near(value.TeleportAngleThresholdDegrees, 30f),
                "Serialized VISUAL_ONLY configuration differs from the frozen M1-C1 values.");
            Add(checks, "Frozen VISUAL_ONLY configuration",
                "RPM, motion, smoothing, time and discontinuity values match M1-C1.");
        }

        private static void VerifySceneBoundary(
            GameObject rov,
            RovThrusterVisualCoordinator coordinator,
            ICollection<Check> checks)
        {
            Require(rov.GetComponents<RovThrusterVisualCoordinator>().Length == 1,
                "ROV root contains duplicate Coordinators.");
            Require(rov.GetComponentsInChildren<Animator>(true).Length == 0 &&
                    rov.GetComponentsInChildren<PlayableDirector>(true).Length == 0,
                "ROV hierarchy unexpectedly contains Animator or Timeline control.");
            Transform[] transforms = rov.GetComponentsInChildren<Transform>(true);
            Require(transforms.All(item =>
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                        item.gameObject) == 0),
                "ROV hierarchy contains a Missing Script.");
            Require(coordinator.GetComponents<RovThrusterVisualCoordinator>().Length == 1,
                "Coordinator component cardinality is invalid.");
            Add(checks, "Scene writer boundary",
                "No duplicate Coordinator, Missing Script, Animator or Timeline under the ROV.");
        }

        private static void VerifySourceBoundary(ICollection<Check> checks)
        {
            string source = File.ReadAllText(CoordinatorSourcePath);
            string spinnerSource = File.ReadAllText(SpinnerSourcePath);
            string[] forbidden =
            {
                "GameObject.Find",
                "GetComponentsInChildren<PropellerSpinner>",
                "Transform.Rotate",
                ".localPosition =",
                ".localRotation =",
                ".localScale =",
                "SetPositionAndRotation",
                "transform.position =",
                "transform.rotation =",
                ".localAxis =",
                "VehiclePoseDriver",
                "VehicleStateStore",
                "VehicleRenderSampler",
                "VehiclePoseConverter",
                "VehiclePoseControlAuthority",
                "DemoMotionController",
                "HorizontalLeft",
                "HorizontalRight",
                "VerticalLeft",
                "VerticalRight"
            };
            Require(forbidden.All(token => !source.Contains(token)),
                "Coordinator source crosses the runtime writer or name-inference boundary.");
            Require(source.Contains(".rpm =") &&
                    spinnerSource.Contains("transform.Rotate") &&
                    spinnerSource.Contains("Space.Self"),
                "RPM writer or unique PropellerSpinner local-rotation contract changed.");
            Add(checks, "Runtime source boundary",
                "Coordinator contains no Transform writes, discovery, role-name inference or core-chain coupling.");
        }

        private static void VerifyCalculation(
            RovThrusterVisualCoordinator value,
            ICollection<Check> checks)
        {
            Vector3 origin = new Vector3(2f, -3f, 4f);
            Quaternion identity = Quaternion.identity;
            Require(Evaluate(value, origin, identity, origin, identity, 0.1f,
                    out Vector3 stationary, out bool stationaryJump) &&
                    !stationaryJump &&
                    Near(stationary, Vector3.zero, 0.00001f),
                "Stationary calculation did not return VISUAL_ONLY idle.");

            Require(Evaluate(value, origin, identity, origin + Vector3.right * 0.02f,
                        identity, 0.1f, out Vector3 surgePositive, out bool surgeJump) &&
                    !surgeJump &&
                    surgePositive.x > 0f &&
                    Near(surgePositive.y, 0f) &&
                    Near(surgePositive.z, 0f),
                "Pure surge did not affect only the Surge group.");
            Require(Evaluate(value, origin, identity, origin - Vector3.right * 0.02f,
                        identity, 0.1f, out Vector3 surgeNegative, out _) &&
                    Near(surgePositive, surgeNegative, 0.0001f),
                "Equal positive/negative surge magnitudes produced different activity.");

            Require(Evaluate(value, origin, identity, origin + Vector3.up * 0.01f,
                        identity, 0.1f, out Vector3 heave, out _) &&
                    heave.y > 0f &&
                    Near(heave.x, 0f) &&
                    Near(heave.z, 0f),
                "Pure heave did not affect only the Heave group.");
            Require(Evaluate(value, origin, identity, origin - Vector3.up * 0.01f,
                        identity, 0.1f, out Vector3 heaveNegative, out _) &&
                    Near(heave, heaveNegative, 0.0001f),
                "Equal positive/negative heave magnitudes produced different activity.");
            Require(Evaluate(value, origin, identity, origin + Vector3.forward * 0.02f,
                        identity, 0.1f, out Vector3 sway, out _) &&
                    sway.z > 0f &&
                    Near(sway.x, 0f) &&
                    Near(sway.y, 0f),
                "Pure sway did not affect only the Sway group.");
            Require(Evaluate(value, origin, identity, origin - Vector3.forward * 0.02f,
                        identity, 0.1f, out Vector3 swayNegative, out _) &&
                    Near(sway, swayNegative, 0.0001f),
                "Equal positive/negative sway magnitudes produced different activity.");

            Require(Evaluate(value, origin, identity, origin,
                        Quaternion.Euler(0f, 10f, 0f), 0.1f,
                        out Vector3 angular, out bool angularJump) &&
                    !angularJump &&
                    Near(angular.x / value.SurgeMaxVisualRpm, 0.20f, 0.0001f) &&
                    Near(angular.y / value.HeaveMaxVisualRpm, 0.20f, 0.0001f) &&
                    Near(angular.z / value.SwayMaxVisualRpm, 0.20f, 0.0001f),
                "Pure angular motion did not produce equal normalized global gain.");

            bool invalidTime = Evaluate(value, origin, identity, origin, identity, 0f,
                out Vector3 invalidTimeTarget, out _);
            bool negativeTime = Evaluate(value, origin, identity, origin, identity, -0.1f,
                out Vector3 negativeTimeTarget, out _);
            bool nonFiniteTime = Evaluate(
                value,
                origin,
                identity,
                origin,
                identity,
                float.NaN,
                out Vector3 nonFiniteTimeTarget,
                out _);
            bool excessiveTime = Evaluate(value, origin, identity, origin, identity, 0.3f,
                out Vector3 excessiveTimeTarget, out _);
            bool invalidFinite = Evaluate(value, origin, identity,
                new Vector3(float.NaN, 0f, 0f), identity, 0.1f,
                out Vector3 invalidFiniteTarget, out _);
            Require(!invalidTime &&
                    !negativeTime &&
                    !nonFiniteTime &&
                    !excessiveTime &&
                    !invalidFinite &&
                    Near(invalidTimeTarget, Vector3.zero, 0.00001f) &&
                    Near(negativeTimeTarget, Vector3.zero, 0.00001f) &&
                    Near(nonFiniteTimeTarget, Vector3.zero, 0.00001f) &&
                    Near(excessiveTimeTarget, Vector3.zero, 0.00001f) &&
                    Near(invalidFiniteTarget, Vector3.zero, 0.00001f),
                "Invalid time or non-finite pose did not return a safe idle result.");

            Require(Evaluate(value, origin, identity, origin + Vector3.one, identity,
                        0.1f, out Vector3 teleport, out bool teleportDetected) &&
                    teleportDetected &&
                    Near(teleport, Vector3.zero, 0.00001f),
                "Teleport did not reset to safe idle.");
            Require(Evaluate(value, origin, identity, origin,
                        Quaternion.Euler(0f, 45f, 0f), 0.1f,
                        out Vector3 angularTeleport, out bool angularTeleportDetected) &&
                    angularTeleportDetected &&
                    Near(angularTeleport, Vector3.zero, 0.00001f),
                "Angular teleport did not reset to safe idle.");
            Require(AllFiniteAndBounded(value, surgePositive) &&
                    AllFiniteAndBounded(value, surgeNegative) &&
                    AllFiniteAndBounded(value, heave) &&
                    AllFiniteAndBounded(value, heaveNegative) &&
                    AllFiniteAndBounded(value, sway) &&
                    AllFiniteAndBounded(value, swayNegative) &&
                    AllFiniteAndBounded(value, angular),
                "A VISUAL_ONLY target was negative, non-finite, or above its group maximum.");
            Add(checks, "Pure VISUAL_ONLY calculation",
                "Stationary, signed axes, angular gain, invalid input, teleport and clamps passed.");
        }

        private static bool Evaluate(
            RovThrusterVisualCoordinator value,
            Vector3 fromPosition,
            Quaternion fromRotation,
            Vector3 toPosition,
            Quaternion toRotation,
            float deltaTime,
            out Vector3 target,
            out bool discontinuity)
        {
            return value.TryEvaluatePoseForDiagnostics(
                fromPosition,
                fromRotation,
                toPosition,
                toRotation,
                deltaTime,
                out target,
                out discontinuity);
        }

        private static bool AllFiniteAndBounded(
            RovThrusterVisualCoordinator value,
            Vector3 target)
        {
            return IsFinite(target.x) &&
                   IsFinite(target.y) &&
                   IsFinite(target.z) &&
                   target.x >= 0f &&
                   target.y >= 0f &&
                   target.z >= 0f &&
                   target.x <= value.SurgeMaxVisualRpm &&
                   target.y <= value.HeaveMaxVisualRpm &&
                   target.z <= value.SwayMaxVisualRpm;
        }

        private static ExpectedBinding[] ExpectedBindings()
        {
            const string Model = "ROV_FineModel_V1_Imported/";
            return new[]
            {
                new ExpectedBinding
                {
                    Role = RovThrusterVisualRole.SurgeVisualRight,
                    Path = Model + "ROV_HorizontalLeftThruster/" +
                        "ROV_HorizontalLeftThruster_Propeller_RotatingPart",
                    FileId = 609084609UL,
                    RootPosition = new Vector3(-0.34f, -0.36f, -0.42f),
                    Axis = Vector3.right,
                    Rpm = 720f,
                    GetSpinner = value => value.SurgeVisualRightSpinner
                },
                new ExpectedBinding
                {
                    Role = RovThrusterVisualRole.SurgeVisualLeft,
                    Path = Model + "ROV_HorizontalRightThruster/" +
                        "ROV_HorizontalRightThruster_Propeller_RotatingPart",
                    FileId = 609084608UL,
                    RootPosition = new Vector3(-0.34f, -0.36f, 0.42f),
                    Axis = Vector3.right,
                    Rpm = 720f,
                    GetSpinner = value => value.SurgeVisualLeftSpinner
                },
                new ExpectedBinding
                {
                    Role = RovThrusterVisualRole.HeaveVisualRight,
                    Path = Model + "ROV_VerticalLeftThruster/" +
                        "ROV_VerticalLeftThruster_Propeller_RotatingPart",
                    FileId = 609084605UL,
                    RootPosition = new Vector3(0f, 0.02f, -1.14f),
                    Axis = Vector3.up,
                    Rpm = 680f,
                    GetSpinner = value => value.HeaveVisualRightSpinner
                },
                new ExpectedBinding
                {
                    Role = RovThrusterVisualRole.HeaveVisualLeft,
                    Path = Model + "ROV_VerticalRightThruster/" +
                        "ROV_VerticalRightThruster_Propeller_RotatingPart",
                    FileId = 1100687659UL,
                    RootPosition = new Vector3(0f, 0.02f, 1.14f),
                    Axis = Vector3.up,
                    Rpm = 680f,
                    GetSpinner = value => value.HeaveVisualLeftSpinner
                },
                new ExpectedBinding
                {
                    Role = RovThrusterVisualRole.SwayFront,
                    Path = Model + "ROV_LateralFrontThruster/" +
                        "ROV_LateralFrontThruster_Propeller_RotatingPart",
                    FileId = 609084607UL,
                    RootPosition = new Vector3(0.36f, -0.30f, 0f),
                    Axis = Vector3.forward,
                    Rpm = 700f,
                    GetSpinner = value => value.SwayFrontSpinner
                },
                new ExpectedBinding
                {
                    Role = RovThrusterVisualRole.SwayRear,
                    Path = Model + "ROV_LateralRearThruster/" +
                        "ROV_LateralRearThruster_Propeller_RotatingPart",
                    FileId = 609084606UL,
                    RootPosition = new Vector3(-0.36f, -0.30f, 0f),
                    Axis = Vector3.forward,
                    Rpm = 700f,
                    GetSpinner = value => value.SwayRearSpinner
                }
            };
        }

        private static GameObject FindUniqueRoot(Scene scene, string name)
        {
            GameObject[] matches = scene.GetRootGameObjects()
                .Where(item => string.Equals(item.name, name, StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1,
                "Expected exactly one Scene root named " + name + ".");
            return matches[0];
        }

        private static bool Near(Vector3 left, Vector3 right, float tolerance)
        {
            return (left - right).sqrMagnitude <= tolerance * tolerance;
        }

        private static bool Near(float left, float right, float tolerance = 0.000001f)
        {
            return Mathf.Abs(left - right) <= tolerance;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
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
