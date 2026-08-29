using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime.Constraints;
using UnderwaterRobotScene.Visualization.Runtime.Rov;
using UnderwaterRobotScene.Visualization.Runtime.Terrain;
using UnderwaterRobotScene.Visualization.Runtime.Usv;
using UnderwaterRobotScene.Visualization.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    public static class RovVerticalMotionBatch1Verifier
    {
        private const string ReportArgument =
            "-rovVerticalBatch1VerifierReportPath";
        private const string ScenePath =
            "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string TemplatePath =
            "Assets/Editor/Visualization/Installation/" +
            "UnderwaterRobotDemo_Canonical.unity";
        private const float SurfaceY = -3f;

        [Serializable]
        private sealed class CaseResult
        {
            public string name;
            public string status;
            public string detail;
        }

        [Serializable]
        private sealed class Report
        {
            public string schema;
            public string status;
            public string unityVersion;
            public int caseCount;
            public int passedCaseCount;
            public int businessErrorCount;
            public CaseResult[] cases;
            public string[] businessErrors;
        }

        private sealed class Fixture : IDisposable
        {
            internal Fixture(string name, Mesh mesh = null)
            {
                Profile = RovContactProfile.CreateApprovedDefault();
                Mesh = mesh ?? CreateGridMesh(name + "_Mesh", false);
                Terrain = new GameObject(name + "_Terrain");
                Terrain.transform.position = new Vector3(0f, SurfaceY, 0f);
                MeshFilter filter = Terrain.AddComponent<MeshFilter>();
                filter.sharedMesh = Mesh;
                Collider = Terrain.AddComponent<MeshCollider>();
                Collider.sharedMesh = Mesh;
                Sampler = Terrain.AddComponent<TerrainSurfaceSampler>();
                Sampler.Configure(Collider);

                Water = new GameObject(name + "_Water");
                WaterProvider = Water.AddComponent<FlatWaterSurfaceProvider>();
                SetWaterY(100f);

                ConstraintObject = new GameObject(name + "_Constraint");
                Constraint = ConstraintObject
                    .AddComponent<RovTerrainContactConstraint>();
                Constraint.Configure(Sampler, Profile, WaterProvider);
            }

            internal RovContactProfile Profile { get; }
            internal Mesh Mesh { get; }
            internal GameObject Terrain { get; }
            internal MeshCollider Collider { get; }
            internal TerrainSurfaceSampler Sampler { get; }
            internal GameObject Water { get; }
            internal FlatWaterSurfaceProvider WaterProvider { get; }
            internal GameObject ConstraintObject { get; }
            internal RovTerrainContactConstraint Constraint { get; }

            internal void SetWaterY(float value)
            {
                Water.transform.position = new Vector3(0f, value, 0f);
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(ConstraintObject);
                UnityEngine.Object.DestroyImmediate(Water);
                UnityEngine.Object.DestroyImmediate(Terrain);
                UnityEngine.Object.DestroyImmediate(Mesh);
            }
        }

        public static void RunBatch()
        {
            string reportPath = RequireExternalCreateNewPath();
            var businessErrors = new List<string>();
            Application.LogCallback callback = (condition, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Assert ||
                    type == LogType.Exception)
                    businessErrors.Add(condition);
            };
            Application.logMessageReceived += callback;

            var cases = new List<KeyValuePair<string, Func<string>>>
            {
                Case("01 Formal Scene and canonical binding", VerifyFormalSceneBinding),
                Case("02 Approved upper envelope geometry", VerifyApprovedEnvelope),
                Case("03 Valid free-water ignores legacy ray range", VerifyValidFreeWater),
                Case("04 Near-seabed correction limits", VerifyNearSeabed),
                Case("05 Water boundary across rotations", VerifyWaterBoundaryAcrossRotations),
                Case("06 Terrain correction precedes water rejection", VerifyCorrectionThenWater),
                Case("07 Valid topology-derived route", VerifyValidRoute),
                Case("08 Endpoint-only miss rejected at topology breakpoint", VerifyTopologyBreakpoint),
                Case("09 Terrain coverage exit rejected", VerifyCoverageExit),
                Case("10 Invalid topology rejected", VerifyInvalidTopology),
                Case("11 Route water crossing rejected", VerifyRouteWaterCrossing),
                Case("12 Stable segment percentage diagnostics", VerifyStableDiagnostics),
                Case("13 Runtime and host source boundary", VerifySourceBoundary)
            };
            var results = new List<CaseResult>(cases.Count);
            int passed = 0;
            try
            {
                foreach (KeyValuePair<string, Func<string>> value in cases)
                {
                    try
                    {
                        results.Add(new CaseResult
                        {
                            name = value.Key,
                            status = "PASS",
                            detail = value.Value()
                        });
                        passed++;
                    }
                    catch (Exception exception)
                    {
                        results.Add(new CaseResult
                        {
                            name = value.Key,
                            status = "FAIL",
                            detail = exception.GetType().Name + ": " +
                                exception.Message
                        });
                    }
                    finally
                    {
                        EditorSceneManager.NewScene(
                            NewSceneSetup.EmptyScene,
                            NewSceneMode.Single);
                    }
                }
            }
            finally
            {
                Application.logMessageReceived -= callback;
            }

            bool success = passed == cases.Count && businessErrors.Count == 0;
            var report = new Report
            {
                schema = "ROV-VerticalMotion-Batch1-Verifier-v1",
                status = success
                    ? "ROV_VERTICAL_MOTION_BATCH1_SAFETY_FOUNDATION_PASS"
                    : "ROV_VERTICAL_MOTION_BATCH1_SAFETY_FOUNDATION_FAIL",
                unityVersion = Application.unityVersion,
                caseCount = cases.Count,
                passedCaseCount = passed,
                businessErrorCount = businessErrors.Count,
                cases = results.ToArray(),
                businessErrors = businessErrors.ToArray()
            };
            File.WriteAllText(reportPath,
                JsonUtility.ToJson(report, true) + Environment.NewLine);
            if (!success)
                throw new InvalidOperationException(report.status + " | " +
                    passed + "/" + cases.Count + " cases passed; errors=" +
                    businessErrors.Count + ".");
            Debug.Log(report.status + " | " + passed + "/" +
                cases.Count + " cases passed.");
        }

        private static KeyValuePair<string, Func<string>> Case(
            string name,
            Func<string> body)
        {
            return new KeyValuePair<string, Func<string>>(name, body);
        }

        private static string VerifyFormalSceneBinding()
        {
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            EnvE3BRovContactInstallResult result =
                EnvE3BRovContactSceneInstaller.Execute(
                    scene,
                    EnvE3BRovContactInstallMode.RequireNoOp);
            Require(result.Success && !result.Changed && !result.SceneSaved,
                "Formal Scene is not a persistent no-op Batch 1 binding: " +
                result.FailureMessage);
            Require(File.ReadAllBytes(AbsolutePath(ScenePath)).SequenceEqual(
                    File.ReadAllBytes(AbsolutePath(TemplatePath))),
                "Formal Scene and canonical template bytes differ.");
            return "Formal Scene binds terrain/profile/water and equals canonical bytes.";
        }

        private static string VerifyApprovedEnvelope()
        {
            const string modelPath =
                "Assets/Models/ROV/ROV_FineModel_V1.fbx";
            RovContactProfile profile = RovContactProfile.CreateApprovedDefault();
            Require(profile.UpperEnvelopeMinimum.Equals(
                        new Vector3(-0.767f, -0.816f, -1.426f)) &&
                    profile.UpperEnvelopeMaximum.Equals(
                        new Vector3(0.796f, 0.729f, 1.426f)) &&
                    profile.UpperEnvelopeCornerCount == 8,
                "Approved root-local AABB is not exact.");
            var corners = new HashSet<Vector3>();
            for (int index = 0; index < 8; index++)
                corners.Add(profile.GetUpperEnvelopeCorner(index));
            Require(corners.Count == 8,
                "Upper envelope does not expose eight unique corners.");
            Require(string.Equals(AssetDatabase.AssetPathToGUID(modelPath),
                        "81496ffad80dd2d43aeec8986511d0a9",
                        StringComparison.Ordinal) &&
                    string.Equals(Sha256File(AbsolutePath(modelPath)),
                        "28e197895ab2df837471eeb4c891a403e5080734a2c78cf75bc1981d1c7b44dd",
                        StringComparison.Ordinal),
                "Approved ROV model GUID or content identity drifted.");
            return "Approved root-local bounds, eight corners, FBX GUID, and FBX SHA are exact.";
        }

        private static string VerifyValidFreeWater()
        {
            using (var fixture = new Fixture("ValidFreeWater"))
            {
                Vector3 position = new Vector3(0f, 5f, 0f);
                RovTerrainContactResult result = fixture.Constraint.Evaluate(
                    position, Quaternion.Euler(0f, 17f, 0f));
                Require(result.Decision == RovTerrainContactDecision.Apply &&
                        result.State == RovTerrainContactState.Supported &&
                        result.OutputPosition.Equals(position) &&
                        result.DeltaY.Equals(0f),
                    "Valid free-water pose was not accepted unchanged.");
                float oldRayBottom = position.y +
                    fixture.Profile.LeftFrontOffset.y +
                    fixture.Profile.ProbeStartHeightMeters -
                    fixture.Profile.ProbeDistanceMeters;
                Require(oldRayBottom > SurfaceY,
                    "Fixture does not prove the legacy short ray would miss.");
            }
            return "A valid pose far above seabed is accepted although the legacy 2 m ray would miss.";
        }

        private static string VerifyNearSeabed()
        {
            using (var fixture = new Fixture("NearSeabed"))
            {
                float supportedY = SupportedRootY(fixture.Profile);
                RovTerrainContactResult above = fixture.Constraint.Evaluate(
                    new Vector3(0f, supportedY + 0.1f, 0f),
                    Quaternion.identity);
                RovTerrainContactResult corrected = fixture.Constraint.Evaluate(
                    new Vector3(0f, supportedY - 0.05f, 0f),
                    Quaternion.identity);
                RovTerrainContactResult excessive = fixture.Constraint.Evaluate(
                    new Vector3(0f, supportedY - 0.301f, 0f),
                    Quaternion.identity);
                Require(above.Decision == RovTerrainContactDecision.Apply &&
                        above.DeltaY.Equals(0f) &&
                        corrected.Decision == RovTerrainContactDecision.Apply &&
                        Mathf.Abs(corrected.DeltaY - 0.05f) <= 0.0001f &&
                        excessive.Decision ==
                            RovTerrainContactDecision.HoldCurrent &&
                        excessive.State ==
                            RovTerrainContactState.CorrectionRejected,
                    "Near-seabed upward-only correction contract drifted.");
            }
            return "Safe-above, 0.05 m correction, and above-0.30 m HOLD all pass.";
        }

        private static string VerifyWaterBoundaryAcrossRotations()
        {
            using (var fixture = new Fixture("WaterRotations"))
            {
                Quaternion[] rotations =
                {
                    Quaternion.identity,
                    Quaternion.Euler(0f, 43f, 0f),
                    Quaternion.Euler(8f, 31f, -7f)
                };
                Vector3 position = new Vector3(0f,
                    SupportedRootY(fixture.Profile) + 0.25f, 0f);
                foreach (Quaternion rotation in rotations)
                {
                    fixture.SetWaterY(100f);
                    RovTerrainContactResult preliminary =
                        fixture.Constraint.Evaluate(position, rotation);
                    Require(preliminary.Decision == RovTerrainContactDecision.Apply,
                        "High-water envelope evaluation did not apply.");
                    fixture.SetWaterY(preliminary.MaximumEnvelopeWorldY);
                    RovTerrainContactResult boundary =
                        fixture.Constraint.Evaluate(position, rotation);
                    Require(boundary.Decision == RovTerrainContactDecision.Apply,
                        "Exact water boundary was rejected for " + rotation + ".");
                    Require(boundary.OutputRotation.Equals(rotation),
                        "Accepted rotation changed for " + rotation + ".");
                    fixture.SetWaterY(
                        preliminary.MaximumEnvelopeWorldY - 0.0001f);
                    RovTerrainContactResult breach =
                        fixture.Constraint.Evaluate(position, rotation);
                    Require(breach.Decision ==
                                RovTerrainContactDecision.HoldCurrent &&
                            breach.State ==
                                RovTerrainContactState.WaterSurfaceBreach &&
                            breach.OutputPosition.Equals(position),
                        "Envelope breach did not hold current for " + rotation + ".");
                }
            }
            return "Identity, yaw, and pitch/roll use all corners at the exact water boundary.";
        }

        private static string VerifyCorrectionThenWater()
        {
            using (var fixture = new Fixture("CorrectionThenWater"))
            {
                Vector3 position = new Vector3(0f,
                    SupportedRootY(fixture.Profile) - 0.05f, 0f);
                float uncorrectedTop = position.y +
                    fixture.Profile.UpperEnvelopeMaximum.y;
                fixture.SetWaterY(uncorrectedTop + 0.025f);
                RovTerrainContactResult result = fixture.Constraint.Evaluate(
                    position, Quaternion.identity);
                Require(result.State == RovTerrainContactState.WaterSurfaceBreach &&
                        result.Decision == RovTerrainContactDecision.HoldCurrent &&
                        result.MaximumEnvelopeWorldY > result.WaterSurfaceY &&
                        result.OutputPosition.Equals(position),
                    "Water was not evaluated against the terrain-corrected pose.");
            }
            return "A correction that would breach water is rejected after terrain evaluation.";
        }

        private static string VerifyValidRoute()
        {
            using (var fixture = new Fixture("ValidRoute"))
            {
                ActiveRouteSnapshot route = BuildRoute(
                    new Vector3d(0.0, SupportedRootY(fixture.Profile), -1.0),
                    new Vector3d(0.0, SupportedRootY(fixture.Profile), 1.0));
                Require(fixture.Constraint.TryValidateRoute(
                        route, TransformProfile(), out string error), error);
                Require(!fixture.Constraint.LastRouteSafetyFailure.HasFailure,
                    "Valid route left a failure diagnostic.");
            }
            return "A covered flat-terrain ROV route passes the shared evaluator.";
        }

        private static string VerifyTopologyBreakpoint()
        {
            using (var fixture = new Fixture(
                       "TopologyBreakpoint",
                       CreateGridMesh("TopologyBreakpoint_Mesh", true)))
            {
                float y = SupportedRootY(fixture.Profile);
                Vector3 start = new Vector3(0f, y, -2.5f);
                Vector3 end = new Vector3(0f, y, 2.5f);
                Require(fixture.Constraint.Evaluate(start, Quaternion.identity)
                            .Decision == RovTerrainContactDecision.Apply &&
                        fixture.Constraint.Evaluate(end, Quaternion.identity)
                            .Decision == RovTerrainContactDecision.Apply,
                    "Topology fixture endpoints are not independently safe.");
                ActiveRouteSnapshot route = BuildRoute(
                    ToDouble(start), ToDouble(end));
                Require(!fixture.Constraint.TryValidateRoute(
                        route, TransformProfile(), out string error) &&
                        fixture.Constraint.LastRouteSafetyFailure.HasFailure &&
                        error.Contains("SlopeRejected"),
                    "Interior ridge was not rejected at a topology breakpoint.");
            }
            return "Safe endpoints cannot hide an interior grid/diagonal terrain hazard.";
        }

        private static string VerifyCoverageExit()
        {
            using (var fixture = new Fixture("CoverageExit"))
            {
                float y = SupportedRootY(fixture.Profile);
                ActiveRouteSnapshot route = BuildRoute(
                    new Vector3d(0.0, y, 0.0),
                    new Vector3d(0.0, y, 3.5));
                Require(!fixture.Constraint.TryValidateRoute(
                        route, TransformProfile(), out string error) &&
                        error.Contains("OutOfTerrainBounds"),
                    "Terrain coverage exit was not rejected.");
            }
            return "Any underside footprint leaving authority coverage rejects the route.";
        }

        private static string VerifyInvalidTopology()
        {
            Mesh mesh = CreateGridMesh("InvalidTopology_Mesh", false);
            int[] complete = mesh.triangles;
            var incomplete = new int[complete.Length - 6];
            Array.Copy(complete, incomplete, incomplete.Length);
            mesh.triangles = incomplete;
            mesh.RecalculateBounds();
            using (var fixture = new Fixture("InvalidTopology", mesh))
            {
                float y = SupportedRootY(fixture.Profile);
                ActiveRouteSnapshot route = BuildRoute(
                    new Vector3d(0.0, y, -1.0),
                    new Vector3d(0.0, y, 1.0));
                Require(!fixture.Constraint.TryValidateRoute(
                        route, TransformProfile(), out string error) &&
                        error.Contains("TopologyHole"),
                    "Topology-hole authority did not fail closed.");
            }
            return "An incomplete authoritative grid topology rejects before traversal.";
        }

        private static string VerifyRouteWaterCrossing()
        {
            using (var fixture = new Fixture("RouteWater"))
            {
                float startY = SupportedRootY(fixture.Profile);
                float waterY = startY +
                    fixture.Profile.UpperEnvelopeMaximum.y + 0.25f;
                fixture.SetWaterY(waterY);
                ActiveRouteSnapshot route = BuildRoute(
                    new Vector3d(0.0, startY, -1.0),
                    new Vector3d(0.0, startY + 0.5f, 1.0));
                Require(!fixture.Constraint.TryValidateRoute(
                        route, TransformProfile(), out string error) &&
                        error.Contains("WaterSurfaceBreach"),
                    "Route water crossing was not rejected.");
            }
            return "The same eight-corner water policy rejects an unsafe route pose.";
        }

        private static string VerifyStableDiagnostics()
        {
            using (var fixture = new Fixture(
                       "StableDiagnostics",
                       CreateGridMesh("StableDiagnostics_Mesh", true)))
            {
                float y = SupportedRootY(fixture.Profile);
                ActiveRouteSnapshot route = BuildRoute(
                    new Vector3d(0.0, y, -2.5),
                    new Vector3d(0.0, y, 2.5));
                Require(!fixture.Constraint.TryValidateRoute(
                    route, TransformProfile(), out string firstError),
                    "First diagnostic route unexpectedly passed.");
                RouteSafetyFailureDiagnostic first =
                    fixture.Constraint.LastRouteSafetyFailure;
                Require(!fixture.Constraint.TryValidateRoute(
                    route, TransformProfile(), out string secondError),
                    "Second diagnostic route unexpectedly passed.");
                RouteSafetyFailureDiagnostic second =
                    fixture.Constraint.LastRouteSafetyFailure;
                Require(first.SegmentIndex == 1 &&
                        first.SegmentIndex == second.SegmentIndex &&
                        first.Percentage.Equals(second.Percentage) &&
                        first.TerrainState == second.TerrainState &&
                        firstError == secondError,
                    "Repeated failure diagnostics are not stable.");
            }
            return "Segment index, percentage, state, and text repeat exactly.";
        }

        private static string VerifySourceBoundary()
        {
            string runtimeRoot = Path.Combine(
                Application.dataPath, "Scripts", "Visualization", "Runtime");
            string evaluator = File.ReadAllText(Path.Combine(
                runtimeRoot, "Rov", "RovSafetyEvaluator.cs"));
            string constraint = File.ReadAllText(Path.Combine(
                runtimeRoot, "Rov", "RovTerrainContactConstraint.cs"));
            string host = File.ReadAllText(Path.Combine(
                runtimeRoot, "VehicleDataRuntimeHost.cs"));
            Require(evaluator.Contains("provider.TrySampleAtXZ(") &&
                    evaluator.Contains("AddFootprintBreakpoints(") &&
                    evaluator.Contains("AddAxisCrossings(") &&
                    !evaluator.Contains("SegmentValidationSpacingMeters") &&
                    !evaluator.Contains("Renderer.bounds") &&
                    constraint.Contains("IRouteSafetyValidator") &&
                    constraint.Contains("RovSafetyEvaluator.Evaluate(") &&
                    constraint.Contains("RovSafetyEvaluator.TryValidateRoute(") &&
                    host.Contains("candidate.VehicleType == VehicleType.Usv") &&
                    !host.Contains("candidate.VehicleType != VehicleType.Auv"),
                "Runtime source no longer proves the Batch 1 authority boundary.");
            return "Runtime reuses one evaluator; route sampling is topology-derived; USV bypass is unchanged.";
        }

        private static ActiveRouteSnapshot BuildRoute(
            Vector3d start,
            Vector3d end)
        {
            Require(ActiveRouteSnapshotBuilder.TryBuild(
                    "ROV-01",
                    VehicleType.Rov,
                    "BATCH1-ROUTE",
                    1UL,
                    new[] { start, end },
                    VehicleRouteOrientationPolicy.RovLevelYaw,
                    0.0,
                    out ActiveRouteSnapshot route,
                    out string error), error);
            return route;
        }

        private static CoordinateTransformProfile TransformProfile()
        {
            return CoordinateTransformProfiles.UnityNative(
                "BATCH1-UNITY",
                1.0,
                AttitudeDirection.BodyToWorld,
                Quaterniond.Identity);
        }

        private static Vector3d ToDouble(Vector3 value)
        {
            return new Vector3d(value.x, value.y, value.z);
        }

        private static float SupportedRootY(RovContactProfile profile)
        {
            return SurfaceY + profile.GroundClearance -
                profile.LeftFrontOffset.y;
        }

        private static Mesh CreateGridMesh(string name, bool ridge)
        {
            const int count = 9;
            const float minimum = -4f;
            var vertices = new Vector3[count * count];
            for (int x = 0; x < count; x++)
            {
                for (int z = 0; z < count; z++)
                {
                    float height = ridge && z == 4 ? 0.6f : 0f;
                    vertices[x * count + z] = new Vector3(
                        minimum + x,
                        height,
                        minimum + z);
                }
            }
            var triangles = new int[(count - 1) * (count - 1) * 6];
            int cursor = 0;
            for (int x = 0; x < count - 1; x++)
            {
                for (int z = 0; z < count - 1; z++)
                {
                    int a = x * count + z;
                    int b = (x + 1) * count + z;
                    int c = (x + 1) * count + z + 1;
                    int d = x * count + z + 1;
                    triangles[cursor++] = a;
                    triangles[cursor++] = c;
                    triangles[cursor++] = b;
                    triangles[cursor++] = a;
                    triangles[cursor++] = d;
                    triangles[cursor++] = c;
                }
            }
            var mesh = new Mesh
            {
                name = name,
                vertices = vertices,
                triangles = triangles
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static string AbsolutePath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", assetPath));
        }

        private static string Sha256File(string path)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(File.ReadAllBytes(path));
                return BitConverter.ToString(hash).Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private static string RequireExternalCreateNewPath()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int index = 0; index < args.Length - 1; index++)
            {
                if (!string.Equals(args[index], ReportArgument,
                        StringComparison.Ordinal))
                    continue;
                string value = Path.GetFullPath(args[index + 1]);
                string projectRoot = Path.GetFullPath(Path.Combine(
                    Application.dataPath, ".."));
                Require(!value.StartsWith(projectRoot + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase),
                    "Verifier report path must be outside the Unity project.");
                Require(!File.Exists(value),
                    "Verifier report path already exists.");
                Directory.CreateDirectory(Path.GetDirectoryName(value));
                return value;
            }
            throw new InvalidOperationException(
                ReportArgument + " is required.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
