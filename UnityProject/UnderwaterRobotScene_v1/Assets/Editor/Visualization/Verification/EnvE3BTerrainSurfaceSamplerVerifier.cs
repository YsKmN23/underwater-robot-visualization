using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnderwaterRobotScene.Visualization.Runtime.Terrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    public static class EnvE3BTerrainSurfaceSamplerVerifier
    {
        private const string ReportPathArgument =
            "-envE3BTerrainSamplerReportPath";
        private const float PositionTolerance = 0.0001f;
        private const float NormalTolerance = 0.0001f;
        private const float SlopeToleranceDegrees = 0.01f;

        [Serializable]
        private sealed class CaseReport
        {
            public string name;
            public string status;
            public string detail;
        }

        [Serializable]
        private sealed class VerificationReport
        {
            public string schema;
            public string status;
            public string unityVersion;
            public int caseCount;
            public int passedCaseCount;
            public int businessErrorCount;
            public int remainingSceneObjectCount;
            public bool explicitColliderOnly;
            public bool globalPhysicsRaycastUsed;
            public bool runtimeReferencesUnityEditor;
            public bool runtimeReferencesEnvE3AEditor;
            public int deterministicRepeatCount;
            public float recommendedProbeStartHeightMeters;
            public float recommendedProbeDistanceMeters;
            public CaseReport[] cases;
            public string[] businessErrors;
        }

        private sealed class VerificationCase
        {
            public VerificationCase(string name, Func<string> body)
            {
                Name = name;
                Body = body;
            }

            public string Name { get; }
            public Func<string> Body { get; }
        }

        private sealed class Fixture : IDisposable
        {
            public Fixture(
                string name,
                Vector3 position,
                Quaternion rotation,
                int layer = 0)
            {
                Mesh = CreatePlaneMesh(name + "_Mesh");
                TerrainObject = new GameObject(name + "_Terrain");
                TerrainObject.layer = layer;
                TerrainObject.transform.SetPositionAndRotation(
                    position,
                    rotation);
                Collider = TerrainObject.AddComponent<MeshCollider>();
                Collider.sharedMesh = Mesh;

                SamplerObject = new GameObject(name + "_Sampler");
                Sampler = SamplerObject.AddComponent<TerrainSurfaceSampler>();
                Sampler.Configure(Collider);
                Physics.SyncTransforms();
            }

            public Mesh Mesh { get; }
            public GameObject TerrainObject { get; }
            public MeshCollider Collider { get; }
            public GameObject SamplerObject { get; }
            public TerrainSurfaceSampler Sampler { get; }

            public void Dispose()
            {
                if (SamplerObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(SamplerObject);
                }

                if (TerrainObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(TerrainObject);
                }

                if (Mesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(Mesh);
                }
            }
        }

        private readonly struct FixtureSnapshot
        {
            public FixtureSnapshot(Fixture fixture)
            {
                Position = fixture.TerrainObject.transform.position;
                Rotation = fixture.TerrainObject.transform.rotation;
                Scale = fixture.TerrainObject.transform.localScale;
                Layer = fixture.TerrainObject.layer;
                Enabled = fixture.Collider.enabled;
                IsTrigger = fixture.Collider.isTrigger;
                SharedMesh = fixture.Collider.sharedMesh;
                Vertices = fixture.Mesh.vertices;
                Triangles = fixture.Mesh.triangles;
            }

            private Vector3 Position { get; }
            private Quaternion Rotation { get; }
            private Vector3 Scale { get; }
            private int Layer { get; }
            private bool Enabled { get; }
            private bool IsTrigger { get; }
            private Mesh SharedMesh { get; }
            private Vector3[] Vertices { get; }
            private int[] Triangles { get; }

            public void RequireUnchanged(Fixture fixture, string label)
            {
                Require(fixture.TerrainObject.transform.position == Position,
                    label + " changed the collider position.");
                Require(fixture.TerrainObject.transform.rotation == Rotation,
                    label + " changed the collider rotation.");
                Require(fixture.TerrainObject.transform.localScale == Scale,
                    label + " changed the collider scale.");
                Require(fixture.TerrainObject.layer == Layer,
                    label + " changed the collider layer.");
                Require(fixture.Collider.enabled == Enabled,
                    label + " changed the collider enabled state.");
                Require(fixture.Collider.isTrigger == IsTrigger,
                    label + " changed the collider trigger state.");
                Require(ReferenceEquals(
                        fixture.Collider.sharedMesh,
                        SharedMesh),
                    label + " changed the collider mesh binding.");
                RequireArrayEqual(Vertices, fixture.Mesh.vertices,
                    label + " changed mesh vertices.");
                RequireArrayEqual(Triangles, fixture.Mesh.triangles,
                    label + " changed mesh triangles.");
            }
        }

        public static void RunBatch()
        {
            string reportPath = RequireExternalReportPath();
            EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            var businessErrors = new List<string>();
            Application.LogCallback callback =
                (condition, stackTrace, type) =>
                {
                    if (type == LogType.Error ||
                        type == LogType.Assert ||
                        type == LogType.Exception)
                    {
                        businessErrors.Add(condition);
                    }
                };
            Application.logMessageReceived += callback;

            List<VerificationCase> cases = BuildCases();
            var caseReports = new List<CaseReport>(cases.Count);
            int passed = 0;
            try
            {
                foreach (VerificationCase verificationCase in cases)
                {
                    try
                    {
                        string detail = verificationCase.Body();
                        caseReports.Add(new CaseReport
                        {
                            name = verificationCase.Name,
                            status = "PASS",
                            detail = detail
                        });
                        passed++;
                    }
                    catch (Exception exception)
                    {
                        caseReports.Add(new CaseReport
                        {
                            name = verificationCase.Name,
                            status = "FAIL",
                            detail = exception.GetType().Name + ": " +
                                exception.Message
                        });
                    }
                }
            }
            finally
            {
                Application.logMessageReceived -= callback;
            }

            int remainingObjects = UnityEngine.Object
                .FindObjectsByType<GameObject>(
                    FindObjectsInactive.Include)
                .Length;
            bool success = passed == cases.Count &&
                businessErrors.Count == 0 &&
                remainingObjects == 0;
            var report = new VerificationReport
            {
                schema = "ENV-E3B-TerrainSurfaceSampler-Verification-v1",
                status = success
                    ? "ENV_E3B_TERRAIN_SURFACE_SAMPLER_VERIFICATION_PASS"
                    : "ENV_E3B_TERRAIN_SURFACE_SAMPLER_VERIFICATION_FAIL",
                unityVersion = Application.unityVersion,
                caseCount = cases.Count,
                passedCaseCount = passed,
                businessErrorCount = businessErrors.Count,
                remainingSceneObjectCount = remainingObjects,
                explicitColliderOnly = true,
                globalPhysicsRaycastUsed = false,
                runtimeReferencesUnityEditor = false,
                runtimeReferencesEnvE3AEditor = false,
                deterministicRepeatCount = 100,
                recommendedProbeStartHeightMeters = 1f,
                recommendedProbeDistanceMeters = 2f,
                cases = caseReports.ToArray(),
                businessErrors = businessErrors.ToArray()
            };
            WriteReportCreateNew(reportPath, report);

            if (!success)
            {
                throw new InvalidOperationException(
                    report.status + " | passed=" + passed + "/" +
                    cases.Count + " | errors=" + businessErrors.Count +
                    " | remainingObjects=" + remainingObjects);
            }

            Debug.Log(report.status + " | " + passed + "/" +
                cases.Count + " cases passed.");
        }

        private static List<VerificationCase> BuildCases()
        {
            return new List<VerificationCase>
            {
                new VerificationCase("01 Flat plane hit", VerifyFlatPlane),
                new VerificationCase("02 Known 12 degree slope", VerifySlope),
                new VerificationCase("03 Transformed collider", VerifyTransformedCollider),
                new VerificationCase("04 Outside mesh bounds", VerifyOutsideBounds),
                new VerificationCase("05 Insufficient distance", VerifyInsufficientDistance),
                new VerificationCase("06 Explicit collider isolation", VerifyColliderIsolation),
                new VerificationCase("07 Layer independence", VerifyLayerIndependence),
                new VerificationCase("08 Missing binding", VerifyMissingBinding),
                new VerificationCase("09 Disabled and inactive collider", VerifyDisabledCollider),
                new VerificationCase("10 Missing mesh", VerifyMissingMesh),
                new VerificationCase("11 Trigger collider rejection", VerifyTriggerCollider),
                new VerificationCase("12 Invalid numeric requests", VerifyInvalidRequests),
                new VerificationCase("13 One hundred repeat determinism", VerifyRepeatedDeterminism),
                new VerificationCase("14 Recommended parameter usability", VerifyRecommendedParameters),
                new VerificationCase("15 Runtime source boundary", VerifySourceBoundary)
            };
        }

        private static string VerifyFlatPlane()
        {
            using (var fixture = new Fixture(
                       "Flat",
                       new Vector3(0f, -3f, 0f),
                       Quaternion.identity))
            {
                Require(fixture.Sampler.TryValidate(out string error), error);
                TerrainSurfaceSample sample = RequireSample(
                    fixture.Sampler,
                    new Vector3(0.25f, -2.5f, 0.5f),
                    1f,
                    2f);
                RequireVector(sample.Point,
                    new Vector3(0.25f, -3f, 0.5f),
                    PositionTolerance,
                    "flat point");
                RequireVector(sample.Normal, Vector3.up,
                    NormalTolerance, "flat normal");
                RequireNear(sample.Distance, 1.5f,
                    PositionTolerance, "flat distance");
                RequireNear(sample.SlopeDegrees, 0f,
                    SlopeToleranceDegrees, "flat slope");
                Require(sample.TriangleIndex >= 0,
                    "Flat hit has no triangle index.");
            }

            return "Y=-3, up normal, zero slope, distance and triangle passed.";
        }

        private static string VerifySlope()
        {
            Quaternion rotation = Quaternion.Euler(0f, 0f, 12f);
            using (var fixture = new Fixture(
                       "Slope12",
                       new Vector3(0f, -3f, 0f),
                       rotation))
            {
                var snapshot = new FixtureSnapshot(fixture);
                TerrainSurfaceSample sample = RequireSample(
                    fixture.Sampler,
                    new Vector3(0f, -2.5f, 0.25f),
                    1f,
                    2f);
                RequireVector(sample.Point,
                    new Vector3(0f, -3f, 0.25f),
                    PositionTolerance,
                    "slope point");
                RequireVector(sample.Normal,
                    rotation * Vector3.up,
                    NormalTolerance,
                    "slope normal");
                RequireNear(sample.SlopeDegrees, 12f,
                    SlopeToleranceDegrees,
                    "slope degrees");
                snapshot.RequireUnchanged(fixture, "Slope sampling");
            }

            return "12 degree world normal and immutable collider pose passed.";
        }

        private static string VerifyTransformedCollider()
        {
            Vector3 position = new Vector3(5f, -2f, 4f);
            Quaternion rotation = Quaternion.Euler(7f, 0f, 0f);
            using (var fixture = new Fixture(
                       "Transformed",
                       position,
                       rotation))
            {
                TerrainSurfaceSample sample = RequireSample(
                    fixture.Sampler,
                    new Vector3(5f, -1.5f, 4f),
                    1f,
                    2f);
                RequireVector(sample.Point, position,
                    PositionTolerance, "transformed point");
                RequireVector(sample.Normal,
                    rotation * Vector3.up,
                    NormalTolerance,
                    "transformed normal");
                RequireNear(sample.SlopeDegrees, 7f,
                    SlopeToleranceDegrees,
                    "transformed slope");
            }

            return "Non-zero world position and rotated world normal passed.";
        }

        private static string VerifyOutsideBounds()
        {
            using (var fixture = StandardFixture("Outside"))
            {
                RequireFailure(
                    fixture.Sampler,
                    new Vector3(10f, -2.5f, 0f),
                    1f,
                    2f,
                    TerrainSurfaceSampleFailureReason.NoHit);
            }

            return "Outside X/Z returned NoHit.";
        }

        private static string VerifyInsufficientDistance()
        {
            using (var fixture = StandardFixture("ShortDistance"))
            {
                RequireFailure(
                    fixture.Sampler,
                    new Vector3(0f, -2.5f, 0f),
                    1f,
                    1f,
                    TerrainSurfaceSampleFailureReason.NoHit);
            }

            return "Legal ray shorter than the surface distance returned NoHit.";
        }

        private static string VerifyColliderIsolation()
        {
            using (var bound = new Fixture(
                       "BoundA",
                       new Vector3(0f, -3f, 0f),
                       Quaternion.identity,
                       8))
            using (var closer = new Fixture(
                       "CloserB",
                       new Vector3(0f, -2.25f, 0f),
                       Quaternion.identity,
                       9))
            {
                Physics.SyncTransforms();
                TerrainSurfaceSample sample = RequireSample(
                    bound.Sampler,
                    new Vector3(0.25f, -2f, 0.5f),
                    1f,
                    2.5f);
                RequireNear(sample.Point.y, -3f,
                    PositionTolerance,
                    "bound collider height");
                Require(Mathf.Abs(sample.Point.y -
                    closer.TerrainObject.transform.position.y) > 0.5f,
                    "The closer unbound collider affected the result.");
            }

            return "Closer collider B and different layers did not affect bound collider A.";
        }

        private static string VerifyLayerIndependence()
        {
            using (var fixture = new Fixture(
                       "Layer8",
                       new Vector3(0f, -3f, 0f),
                       Quaternion.identity,
                       8))
            {
                TerrainSurfaceSample sample = RequireSample(
                    fixture.Sampler,
                    new Vector3(0.25f, -2.5f, 0.5f),
                    1f,
                    2f);
                RequireNear(sample.Point.y, -3f,
                    PositionTolerance,
                    "non-default layer height");
            }

            return "Bound collider on layer 8 succeeded without a layer mask.";
        }

        private static string VerifyMissingBinding()
        {
            GameObject samplerObject = new GameObject("MissingBinding_Sampler");
            try
            {
                TerrainSurfaceSampler sampler =
                    samplerObject.AddComponent<TerrainSurfaceSampler>();
                Require(!sampler.TryValidate(out string error) &&
                        !string.IsNullOrEmpty(error),
                    "Missing binding validation unexpectedly passed.");
                RequireFailure(
                    sampler,
                    Vector3.zero,
                    1f,
                    2f,
                    TerrainSurfaceSampleFailureReason.MissingCollider);
                bool threw = false;
                try
                {
                    sampler.Configure(null);
                }
                catch (ArgumentNullException)
                {
                    threw = true;
                }

                Require(threw,
                    "Configure(null) did not preserve its exception contract.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(samplerObject);
            }

            return "MissingCollider and Configure(null) contracts passed.";
        }

        private static string VerifyDisabledCollider()
        {
            using (var fixture = StandardFixture("Disabled"))
            {
                fixture.Collider.enabled = false;
                RequireFailure(
                    fixture.Sampler,
                    Vector3.zero,
                    1f,
                    2f,
                    TerrainSurfaceSampleFailureReason.DisabledCollider);
                Require(!fixture.Sampler.TryValidate(out _),
                    "Disabled collider validation unexpectedly passed.");

                fixture.Collider.enabled = true;
                fixture.TerrainObject.SetActive(false);
                RequireFailure(
                    fixture.Sampler,
                    Vector3.zero,
                    1f,
                    2f,
                    TerrainSurfaceSampleFailureReason.DisabledCollider);
            }

            return "Disabled and inactive colliders were rejected before sampling.";
        }

        private static string VerifyMissingMesh()
        {
            using (var fixture = StandardFixture("MissingMesh"))
            {
                fixture.Collider.sharedMesh = null;
                RequireFailure(
                    fixture.Sampler,
                    Vector3.zero,
                    1f,
                    2f,
                    TerrainSurfaceSampleFailureReason.MissingMesh);
                Require(!fixture.Sampler.TryValidate(out _),
                    "Missing mesh validation unexpectedly passed.");
            }

            return "MeshCollider without sharedMesh returned MissingMesh.";
        }

        private static string VerifyTriggerCollider()
        {
            using (var fixture = StandardFixture("Trigger"))
            {
                Mesh triggerMesh = CreateTetrahedronMesh("Trigger_Tetrahedron");
                try
                {
                    fixture.Collider.sharedMesh = triggerMesh;
                    fixture.Collider.convex = true;
                    fixture.Collider.isTrigger = true;
                    Require(fixture.Collider.isTrigger,
                        "Synthetic collider did not accept trigger state.");
                    RequireFailure(
                        fixture.Sampler,
                        Vector3.zero,
                        1f,
                        2f,
                        TerrainSurfaceSampleFailureReason.InvalidRequest);
                    Require(!fixture.Sampler.TryValidate(out _),
                        "Trigger collider validation unexpectedly passed.");
                }
                finally
                {
                    fixture.Collider.isTrigger = false;
                    fixture.Collider.convex = false;
                    fixture.Collider.sharedMesh = fixture.Mesh;
                    UnityEngine.Object.DestroyImmediate(triggerMesh);
                }
            }

            return "Trigger collider was rejected as InvalidRequest.";
        }

        private static string VerifyInvalidRequests()
        {
            using (var fixture = StandardFixture("InvalidRequests"))
            {
                RequireInvalid(fixture.Sampler,
                    new Vector3(float.NaN, 0f, 0f), 1f, 2f);
                RequireInvalid(fixture.Sampler,
                    new Vector3(0f, float.PositiveInfinity, 0f), 1f, 2f);
                RequireInvalid(fixture.Sampler, Vector3.zero, -1f, 2f);
                RequireInvalid(fixture.Sampler, Vector3.zero, float.NaN, 2f);
                RequireInvalid(fixture.Sampler, Vector3.zero, 1f, 0f);
                RequireInvalid(fixture.Sampler, Vector3.zero, 1f, -1f);
                RequireInvalid(fixture.Sampler, Vector3.zero, 1f, float.NaN);
                RequireInvalid(fixture.Sampler, Vector3.zero, 1f,
                    float.PositiveInfinity);
            }

            return "NaN, Infinity, negative start, and non-positive distance requests were rejected.";
        }

        private static string VerifyRepeatedDeterminism()
        {
            using (var fixture = StandardFixture("Repeat100"))
            {
                var snapshot = new FixtureSnapshot(fixture);
                Vector3 projected = new Vector3(0.375f, -2.5f, -0.625f);
                TerrainSurfaceSample first = RequireSample(
                    fixture.Sampler,
                    projected,
                    1f,
                    2f);
                for (int index = 1; index < 100; index++)
                {
                    TerrainSurfaceSample next = RequireSample(
                        fixture.Sampler,
                        projected,
                        1f,
                        2f);
                    RequireSampleExactlyEqual(first, next,
                        "repeat " + index);
                }

                snapshot.RequireUnchanged(fixture,
                    "Repeated deterministic sampling");
            }

            return "100 identical requests produced exactly equal result fields and no mutations.";
        }

        private static string VerifyRecommendedParameters()
        {
            using (var flat = StandardFixture("RecommendedFlat"))
            using (var slope = new Fixture(
                       "RecommendedSlope",
                       new Vector3(0f, -3f, 0f),
                       Quaternion.Euler(0f, 0f, 12f)))
            {
                TerrainSurfaceSample flatSample = RequireSample(
                    flat.Sampler,
                    new Vector3(0f, -2.5f, 0f),
                    1f,
                    2f);
                TerrainSurfaceSample slopeSample = RequireSample(
                    slope.Sampler,
                    new Vector3(0f, -2.5f, 0f),
                    1f,
                    2f);
                RequireNear(flatSample.SlopeDegrees, 0f,
                    SlopeToleranceDegrees, "recommended flat slope");
                RequireNear(slopeSample.SlopeDegrees, 12f,
                    SlopeToleranceDegrees, "recommended 12 degree slope");
            }

            return "Probe start 1.0 m and distance 2.0 m passed flat and 12 degree fixtures.";
        }

        private static string VerifySourceBoundary()
        {
            Type samplerType = typeof(TerrainSurfaceSampler);
            BindingFlags declaredInstance = BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly;
            foreach (string methodName in new[]
                     {
                         "Update", "LateUpdate", "FixedUpdate"
                     })
            {
                Require(samplerType.GetMethod(
                        methodName,
                        declaredInstance) == null,
                    methodName + " must not exist on the sampler.");
            }

            FieldInfo[] fields = samplerType.GetFields(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            Require(fields.Length == 1 &&
                    fields[0].FieldType == typeof(MeshCollider) &&
                    fields[0].Name == "contactTerrain",
                "Sampler must have exactly one explicit MeshCollider field.");

            string sourcePath = Path.Combine(
                Application.dataPath,
                "Scripts",
                "Visualization",
                "Runtime",
                "Terrain",
                "TerrainSurfaceSampler.cs");
            string source = File.ReadAllText(sourcePath);
            Require(source.Contains("contactTerrain.Raycast("),
                "Sampler does not call its bound collider raycast API.");
            foreach (string forbidden in new[]
                     {
                         "UnityEditor",
                         "EnvE3A",
                         "Physics.",
                         "System.Linq",
                         "VehiclePose",
                         "VehicleState",
                         "ROV",
                         "Store",
                         "Driver",
                         "SetPositionAndRotation",
                         ".position =",
                         ".rotation =",
                         ".localPosition =",
                         ".localRotation ="
                     })
            {
                Require(!source.Contains(forbidden),
                    "Runtime source contains prohibited dependency/write token: " +
                    forbidden);
            }

            return "Reflection and exact source checks proved the Runtime boundary.";
        }

        private static Fixture StandardFixture(string name)
        {
            return new Fixture(
                name,
                new Vector3(0f, -3f, 0f),
                Quaternion.identity);
        }

        private static Mesh CreatePlaneMesh(string name)
        {
            var mesh = new Mesh
            {
                name = name,
                vertices = new[]
                {
                    new Vector3(-2f, 0f, -2f),
                    new Vector3(-2f, 0f, 2f),
                    new Vector3(2f, 0f, 2f),
                    new Vector3(2f, 0f, -2f)
                },
                triangles = new[]
                {
                    0, 1, 2,
                    0, 2, 3
                }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateTetrahedronMesh(string name)
        {
            var mesh = new Mesh
            {
                name = name,
                vertices = new[]
                {
                    new Vector3(0f, 0.75f, 0f),
                    new Vector3(-0.75f, -0.5f, -0.5f),
                    new Vector3(0.75f, -0.5f, -0.5f),
                    new Vector3(0f, -0.5f, 0.75f)
                },
                triangles = new[]
                {
                    0, 2, 1,
                    0, 3, 2,
                    0, 1, 3,
                    1, 2, 3
                }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static TerrainSurfaceSample RequireSample(
            TerrainSurfaceSampler sampler,
            Vector3 projected,
            float startHeight,
            float distance)
        {
            Require(sampler.TrySample(
                    projected,
                    startHeight,
                    distance,
                    out TerrainSurfaceSample sample,
                    out TerrainSurfaceSampleFailureReason failure),
                "Expected sampling success, got " + failure + ".");
            Require(failure == TerrainSurfaceSampleFailureReason.None,
                "Successful sample returned " + failure + ".");
            return sample;
        }

        private static void RequireFailure(
            TerrainSurfaceSampler sampler,
            Vector3 projected,
            float startHeight,
            float distance,
            TerrainSurfaceSampleFailureReason expected)
        {
            bool succeeded = sampler.TrySample(
                projected,
                startHeight,
                distance,
                out TerrainSurfaceSample sample,
                out TerrainSurfaceSampleFailureReason failure);
            Require(!succeeded, "Invalid/no-hit request unexpectedly succeeded.");
            Require(failure == expected,
                "Expected " + expected + ", got " + failure + ".");
            Require(sample.Point == Vector3.zero &&
                    sample.Normal == Vector3.zero &&
                    sample.Distance == 0f &&
                    sample.SlopeDegrees == 0f &&
                    sample.TriangleIndex == 0,
                "Failed request did not return a safe default sample.");
        }

        private static void RequireInvalid(
            TerrainSurfaceSampler sampler,
            Vector3 projected,
            float startHeight,
            float distance)
        {
            RequireFailure(
                sampler,
                projected,
                startHeight,
                distance,
                TerrainSurfaceSampleFailureReason.InvalidRequest);
        }

        private static void RequireSampleExactlyEqual(
            TerrainSurfaceSample expected,
            TerrainSurfaceSample actual,
            string label)
        {
            Require(expected.Point.Equals(actual.Point) &&
                    expected.Normal.Equals(actual.Normal) &&
                    expected.Distance.Equals(actual.Distance) &&
                    expected.SlopeDegrees.Equals(actual.SlopeDegrees) &&
                    expected.TriangleIndex == actual.TriangleIndex,
                label + " did not exactly reproduce the first sample.");
        }

        private static void RequireVector(
            Vector3 actual,
            Vector3 expected,
            float tolerance,
            string label)
        {
            Require(Vector3.Distance(actual, expected) <= tolerance,
                label + " expected " + expected + ", got " + actual + ".");
        }

        private static void RequireNear(
            float actual,
            float expected,
            float tolerance,
            string label)
        {
            Require(Mathf.Abs(actual - expected) <= tolerance,
                label + " expected " + expected + ", got " + actual + ".");
        }

        private static void RequireArrayEqual(
            Vector3[] expected,
            Vector3[] actual,
            string message)
        {
            Require(expected.Length == actual.Length, message);
            for (int index = 0; index < expected.Length; index++)
            {
                Require(expected[index].Equals(actual[index]), message);
            }
        }

        private static void RequireArrayEqual(
            int[] expected,
            int[] actual,
            string message)
        {
            Require(expected.Length == actual.Length, message);
            for (int index = 0; index < expected.Length; index++)
            {
                Require(expected[index] == actual[index], message);
            }
        }

        private static string RequireExternalReportPath()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            string value = string.Empty;
            for (int index = 0; index + 1 < arguments.Length; index++)
            {
                if (string.Equals(
                        arguments[index],
                        ReportPathArgument,
                        StringComparison.Ordinal))
                {
                    Require(string.IsNullOrEmpty(value),
                        "Report path argument was provided more than once.");
                    value = arguments[index + 1];
                }
            }

            Require(!string.IsNullOrWhiteSpace(value),
                "Missing " + ReportPathArgument + ".");
            string fullPath = Path.GetFullPath(value);
            string projectRoot = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            string projectPrefix = projectRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            Require(!fullPath.StartsWith(
                    projectPrefix,
                    StringComparison.OrdinalIgnoreCase),
                "Verifier report path must be outside the Unity project.");
            Require(!File.Exists(fullPath),
                "Verifier report path must be create-new.");
            return fullPath;
        }

        private static void WriteReportCreateNew(
            string path,
            VerificationReport report)
        {
            string directory = Path.GetDirectoryName(path);
            Require(!string.IsNullOrEmpty(directory),
                "Report path has no parent directory.");
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(
                       path,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var writer = new StreamWriter(
                       stream,
                       new UTF8Encoding(false)))
            {
                writer.Write(JsonUtility.ToJson(report, true));
                writer.Write(Environment.NewLine);
            }
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
