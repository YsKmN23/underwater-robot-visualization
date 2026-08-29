using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnderwaterRobotScene.Visualization.Runtime.Rov;
using UnderwaterRobotScene.Visualization.Runtime.Terrain;
using UnderwaterRobotScene.Visualization.Runtime.Usv;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    public static class EnvE3BRovContactConstraintVerifier
    {
        private const string ReportPathArgument =
            "-envE3BRovContactConstraintReportPath";
        private const float SurfaceY = -3f;
        private const float Tolerance = 0.0001f;

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
            public string deterministicStatus;
            public string unityVersion;
            public int caseCount;
            public int passedCaseCount;
            public int businessErrorCount;
            public int remainingSceneObjectCount;
            public int deterministicRepeatCount;
            public float groundClearance;
            public float probeStartHeightMeters;
            public float probeDistanceMeters;
            public float maximumSlopeDegrees;
            public float maximumVerticalCorrectionMeters;
            public float epsilonMeters;
            public bool exactFourProbeContract;
            public bool terrainSurfaceSamplerOnly;
            public bool lifecycleMethodsAbsent;
            public bool transformWritesAbsent;
            public bool perEvaluationCollectionsAbsent;
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
                RovContactProfile contactProfile = null,
                Quaternion? terrainRotation = null,
                bool threeProbeTriangle = false)
            {
                Profile = contactProfile ?? RovContactProfile.CreateApprovedDefault();
                float slopeDegrees = terrainRotation.HasValue
                    ? terrainRotation.Value.eulerAngles.z
                    : 0f;
                Mesh = CreatePlaneMesh(name + "_Mesh", slopeDegrees);
                TerrainObject = new GameObject(name + "_Terrain");
                TerrainObject.transform.SetPositionAndRotation(
                    new Vector3(0f, SurfaceY, 0f),
                    Quaternion.identity);
                MeshFilter filter = TerrainObject.AddComponent<MeshFilter>();
                filter.sharedMesh = Mesh;
                Collider = TerrainObject.AddComponent<MeshCollider>();
                Collider.sharedMesh = Mesh;

                SamplerObject = new GameObject(name + "_Sampler");
                Sampler = SamplerObject.AddComponent<TerrainSurfaceSampler>();
                Sampler.Configure(Collider);
                WaterObject = new GameObject(name + "_Water");
                WaterObject.transform.position = new Vector3(0f, 100f, 0f);
                Water = WaterObject.AddComponent<FlatWaterSurfaceProvider>();

                ConstraintObject = new GameObject(name + "_Constraint");
                Constraint =
                    ConstraintObject.AddComponent<RovTerrainContactConstraint>();
                Constraint.Configure(Sampler, Profile, Water);
                Physics.SyncTransforms();
            }

            public RovContactProfile Profile { get; }
            public Mesh Mesh { get; }
            public GameObject TerrainObject { get; }
            public MeshCollider Collider { get; }
            public GameObject SamplerObject { get; }
            public TerrainSurfaceSampler Sampler { get; }
            public GameObject WaterObject { get; }
            public FlatWaterSurfaceProvider Water { get; }
            public GameObject ConstraintObject { get; }
            public RovTerrainContactConstraint Constraint { get; }

            public void Dispose()
            {
                if (ConstraintObject != null)
                    UnityEngine.Object.DestroyImmediate(ConstraintObject);
                if (SamplerObject != null)
                    UnityEngine.Object.DestroyImmediate(SamplerObject);
                if (WaterObject != null)
                    UnityEngine.Object.DestroyImmediate(WaterObject);
                if (TerrainObject != null)
                    UnityEngine.Object.DestroyImmediate(TerrainObject);
                if (Mesh != null)
                    UnityEngine.Object.DestroyImmediate(Mesh);
            }
        }

        private readonly struct FixtureSnapshot
        {
            public FixtureSnapshot(Fixture fixture)
            {
                TerrainPosition = fixture.TerrainObject.transform.position;
                TerrainRotation = fixture.TerrainObject.transform.rotation;
                ConstraintPosition = fixture.ConstraintObject.transform.position;
                ConstraintRotation = fixture.ConstraintObject.transform.rotation;
                Mesh = fixture.Collider.sharedMesh;
                Vertices = fixture.Mesh.vertices;
                Triangles = fixture.Mesh.triangles;
                Sampler = fixture.Constraint.SurfaceSampler;
                Profile = fixture.Constraint.Profile;
                LeftFront = fixture.Profile.LeftFrontOffset;
                LeftRear = fixture.Profile.LeftRearOffset;
                RightFront = fixture.Profile.RightFrontOffset;
                RightRear = fixture.Profile.RightRearOffset;
            }

            private Vector3 TerrainPosition { get; }
            private Quaternion TerrainRotation { get; }
            private Vector3 ConstraintPosition { get; }
            private Quaternion ConstraintRotation { get; }
            private Mesh Mesh { get; }
            private Vector3[] Vertices { get; }
            private int[] Triangles { get; }
            private TerrainSurfaceSampler Sampler { get; }
            private RovContactProfile Profile { get; }
            private Vector3 LeftFront { get; }
            private Vector3 LeftRear { get; }
            private Vector3 RightFront { get; }
            private Vector3 RightRear { get; }

            public void RequireUnchanged(Fixture fixture, string label)
            {
                Require(fixture.TerrainObject.transform.position == TerrainPosition,
                    label + " changed terrain position.");
                Require(fixture.TerrainObject.transform.rotation == TerrainRotation,
                    label + " changed terrain rotation.");
                Require(fixture.ConstraintObject.transform.position == ConstraintPosition,
                    label + " changed constraint object position.");
                Require(fixture.ConstraintObject.transform.rotation == ConstraintRotation,
                    label + " changed constraint object rotation.");
                Require(ReferenceEquals(fixture.Collider.sharedMesh, Mesh),
                    label + " changed collider mesh binding.");
                Require(ReferenceEquals(fixture.Constraint.SurfaceSampler, Sampler),
                    label + " changed sampler binding.");
                Require(ReferenceEquals(fixture.Constraint.Profile, Profile),
                    label + " changed profile binding.");
                RequireArrayExactly(Vertices, fixture.Mesh.vertices,
                    label + " changed mesh vertices.");
                RequireArrayExactly(Triangles, fixture.Mesh.triangles,
                    label + " changed mesh triangles.");
                Require(fixture.Profile.LeftFrontOffset.Equals(LeftFront) &&
                        fixture.Profile.LeftRearOffset.Equals(LeftRear) &&
                        fixture.Profile.RightFrontOffset.Equals(RightFront) &&
                        fixture.Profile.RightRearOffset.Equals(RightRear),
                    label + " changed profile offsets.");
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
                .FindObjectsByType<GameObject>(FindObjectsInactive.Include)
                .Length;
            bool success = passed == cases.Count &&
                cases.Count >= 26 &&
                businessErrors.Count == 0 &&
                remainingObjects == 0;
            RovContactProfile approved =
                RovContactProfile.CreateApprovedDefault();
            var report = new VerificationReport
            {
                schema = "ENV-E3B-RovContactConstraint-Verification-v1",
                status = success
                    ? "ENV_E3B_BATCH_2_CONTACT_PROFILE_AND_CONSTRAINT_MATH_PASS"
                    : "ENV_E3B_BATCH_2_CONTACT_PROFILE_AND_CONSTRAINT_MATH_FAIL",
                deterministicStatus = success
                    ? "ENV_E3B_ROV_CONTACT_CONSTRAINT_DETERMINISTIC_PASS"
                    : "ENV_E3B_ROV_CONTACT_CONSTRAINT_DETERMINISTIC_FAIL",
                unityVersion = Application.unityVersion,
                caseCount = cases.Count,
                passedCaseCount = passed,
                businessErrorCount = businessErrors.Count,
                remainingSceneObjectCount = remainingObjects,
                deterministicRepeatCount = 100,
                groundClearance = approved.GroundClearance,
                probeStartHeightMeters = approved.ProbeStartHeightMeters,
                probeDistanceMeters = approved.ProbeDistanceMeters,
                maximumSlopeDegrees = approved.MaximumSlopeDegrees,
                maximumVerticalCorrectionMeters =
                    approved.MaximumVerticalCorrectionMeters,
                epsilonMeters = approved.EpsilonMeters,
                exactFourProbeContract = true,
                terrainSurfaceSamplerOnly = true,
                lifecycleMethodsAbsent = true,
                transformWritesAbsent = true,
                perEvaluationCollectionsAbsent = true,
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

            Debug.Log(report.status + " | " +
                report.deterministicStatus + " | " +
                passed + "/" + cases.Count + " cases passed.");
        }

        private static List<VerificationCase> BuildCases()
        {
            return new List<VerificationCase>
            {
                new VerificationCase("01 Exact default profile and order", VerifyExactDefaultProfile),
                new VerificationCase("02 Approved profile validation", VerifyApprovedValidation),
                new VerificationCase("03 NaN and Infinity profile rejection", VerifyNonFiniteProfile),
                new VerificationCase("04 Duplicate and invalid offset rejection", VerifyInvalidOffsets),
                new VerificationCase("05 Already at clearance is Supported", VerifyAlreadySupported),
                new VerificationCase("06 Epsilon-positive Supported applies full correction", VerifyEpsilonPositive),
                new VerificationCase("07 Normal penetration is Corrected", VerifyNormalCorrection),
                new VerificationCase("08 Maximum of four probes drives delta", VerifyMaximumProbeWins),
                new VerificationCase("09 Above terrain never moves downward", VerifyNoDownwardMovement),
                new VerificationCase("10 Output XZ remain exact", VerifyExactHorizontalCoordinates),
                new VerificationCase("11 Yaw and raw quaternion remain exact", VerifyExactYaw),
                new VerificationCase("12 Pitch roll footprint uses normalized copy", VerifyPitchRoll),
                new VerificationCase("13 Exact 12 degree slope accepted", VerifySlopeBoundaryAccepted),
                new VerificationCase("14 Slope above 12 degrees rejected", VerifySlopeRejected),
                new VerificationCase("15 Exact maximum correction accepted", VerifyCorrectionBoundaryAccepted),
                new VerificationCase("16 Above maximum correction rejected without truncation", VerifyCorrectionRejected),
                new VerificationCase("17 One NoHit yields OutOfTerrainBounds", VerifyOneNoHit),
                new VerificationCase("18 Other sampler failure yields NoValidTerrainSample", VerifyOtherSamplerFailure),
                new VerificationCase("19 Missing sampler and profile configuration", VerifyMissingConfiguration),
                new VerificationCase("20 Non-finite position is InvalidPose", VerifyInvalidPosition),
                new VerificationCase("21 Zero and invalid quaternion are InvalidPose", VerifyInvalidQuaternion),
                new VerificationCase("22 All four samples required no three-point fallback", VerifyAllFourRequired),
                new VerificationCase("23 Evaluation is immutable", VerifyImmutability),
                new VerificationCase("24 One hundred exact result repeats", VerifyRepeatedDeterminism),
                new VerificationCase("25 No state accumulation", VerifyNoStateAccumulation),
                new VerificationCase("26 Runtime source boundary", VerifySourceBoundary)
            };
        }

        private static string VerifyExactDefaultProfile()
        {
            RovContactProfile value = RovContactProfile.CreateApprovedDefault();
            Require(value.LeftFrontOffset.Equals(
                new Vector3(-0.4700000f, -0.7774999f, 1.0150001f)),
                "Left-front offset or order changed.");
            Require(value.LeftRearOffset.Equals(
                new Vector3(-0.4700000f, -0.7774999f, -1.0150002f)),
                "Left-rear offset or order changed.");
            Require(value.RightFrontOffset.Equals(
                new Vector3(0.4700000f, -0.7774999f, 1.0150001f)),
                "Right-front offset or order changed.");
            Require(value.RightRearOffset.Equals(
                new Vector3(0.4700000f, -0.7774999f, -1.0150002f)),
                "Right-rear offset or order changed.");
            Require(value.GroundClearance.Equals(0.015f) &&
                    value.ProbeStartHeightMeters.Equals(1f) &&
                    value.ProbeDistanceMeters.Equals(2f) &&
                    value.MaximumSlopeDegrees.Equals(12f) &&
                    value.MaximumVerticalCorrectionMeters.Equals(0.30f) &&
                    value.EpsilonMeters.Equals(0.001f),
                "One or more approved scalar values changed.");
            return "Four ordered offsets and six scalar values are exact.";
        }

        private static string VerifyApprovedValidation()
        {
            RovContactProfile value = RovContactProfile.CreateApprovedDefault();
            Require(value.TryValidate(out string error), error);
            Require(!typeof(MonoBehaviour).IsAssignableFrom(typeof(RovContactProfile)),
                "Profile must remain serializable data, not a component.");
            Require(Attribute.IsDefined(typeof(RovContactProfile),
                    typeof(SerializableAttribute)),
                "Profile is not Serializable.");
            return "Approved serializable data profile validates without scene state.";
        }

        private static string VerifyNonFiniteProfile()
        {
            RovContactProfile nanOffset = MakeProfile(
                leftFront: new Vector3(float.NaN, -0.7774999f, 1.0150001f));
            RovContactProfile infiniteScalar = MakeProfile(
                maximumSlope: float.PositiveInfinity);
            Require(!nanOffset.TryValidate(out _),
                "NaN offset unexpectedly validated.");
            Require(!infiniteScalar.TryValidate(out _),
                "Infinite scalar unexpectedly validated.");
            return "NaN and Infinity are rejected without exceptions.";
        }

        private static string VerifyInvalidOffsets()
        {
            RovContactProfile approved = RovContactProfile.CreateApprovedDefault();
            RovContactProfile duplicate = MakeProfile(
                rightRear: approved.LeftFrontOffset);
            RovContactProfile negativeDistance = MakeProfile(probeDistance: -1f);
            RovContactProfile epsilonTooLarge = MakeProfile(
                maximumCorrection: 0.01f,
                epsilon: 0.02f);
            Require(!duplicate.TryValidate(out _),
                "Duplicate offsets unexpectedly validated.");
            Require(!negativeDistance.TryValidate(out _),
                "Negative distance unexpectedly validated.");
            Require(!epsilonTooLarge.TryValidate(out _),
                "Epsilon above maximum correction unexpectedly validated.");
            return "Duplicate offsets and invalid scalar ranges are rejected.";
        }

        private static string VerifyAlreadySupported()
        {
            using (var fixture = new Fixture("Supported"))
            {
                Vector3 input = new Vector3(0f,
                    SupportedRootY(fixture.Profile), 0f);
                RovTerrainContactResult result =
                    fixture.Constraint.Evaluate(input, Quaternion.identity);
                RequireApply(result, RovTerrainContactState.Supported);
                Require(result.DeltaY >= 0f && result.DeltaY <= Tolerance,
                    "Clearance pose produced a material correction.");
                Require(result.ValidSampleCount == 4,
                    "Clearance pose did not use all four samples.");
            }

            return "Clearance pose applies as Supported with four valid probes.";
        }

        private static string VerifyEpsilonPositive()
        {
            using (var fixture = new Fixture("EpsilonPositive"))
            {
                Vector3 input = new Vector3(0f,
                    SupportedRootY(fixture.Profile) - 0.0005f, 0f);
                RovTerrainContactResult result =
                    fixture.Constraint.Evaluate(input, Quaternion.identity);
                RequireApply(result, RovTerrainContactState.Supported);
                Require(result.DeltaY > 0f &&
                        result.DeltaY <= fixture.Profile.EpsilonMeters,
                    "Positive epsilon correction was not retained.");
                Require(result.OutputPosition.y.Equals(input.y + result.DeltaY),
                    "Positive epsilon correction was not fully applied.");
            }

            return "Positive delta within epsilon remains Supported and is fully applied.";
        }

        private static string VerifyNormalCorrection()
        {
            using (var fixture = new Fixture("Corrected"))
            {
                Vector3 input = new Vector3(0f,
                    SupportedRootY(fixture.Profile) - 0.05f, 0f);
                RovTerrainContactResult result =
                    fixture.Constraint.Evaluate(input, Quaternion.identity);
                RequireApply(result, RovTerrainContactState.Corrected);
                RequireNear(result.DeltaY, 0.05f, Tolerance,
                    "normal correction");
                Require(result.OutputPosition.y.Equals(input.y + result.DeltaY),
                    "Correction was not applied exactly once.");
            }

            return "Five-centimeter penetration returns an upward Corrected result.";
        }

        private static string VerifyMaximumProbeWins()
        {
            using (var fixture = new Fixture("MaximumProbe"))
            {
                Quaternion pose = Quaternion.Euler(5f, 17f, 4f);
                Vector3 input = new Vector3(0f,
                    SupportedRootY(fixture.Profile) + 0.08f, 0f);
                RovTerrainContactResult result = fixture.Constraint.Evaluate(input, pose);
                Require(result.Decision == RovTerrainContactDecision.Apply,
                    "Four-probe maximum fixture did not apply.");
                float expected = Mathf.Max(0f,
                    Mathf.Max(result.LeftFront.RequiredVerticalCorrectionMeters,
                    Mathf.Max(result.LeftRear.RequiredVerticalCorrectionMeters,
                    Mathf.Max(result.RightFront.RequiredVerticalCorrectionMeters,
                        result.RightRear.RequiredVerticalCorrectionMeters))));
                Require(result.DeltaY.Equals(expected),
                    "DeltaY is not the exact maximum of all four requirements.");
                Require(HasDifferentRequirements(result),
                    "Fixture did not produce distinct probe requirements.");
            }

            return "Distinct contact requirements prove the maximum probe drives DeltaY.";
        }

        private static string VerifyNoDownwardMovement()
        {
            using (var fixture = new Fixture("NoDownward"))
            {
                Vector3 input = new Vector3(0f,
                    SupportedRootY(fixture.Profile) + 0.25f, 0f);
                RovTerrainContactResult result =
                    fixture.Constraint.Evaluate(input, Quaternion.identity);
                RequireApply(result, RovTerrainContactState.Supported);
                Require(result.DeltaY.Equals(0f),
                    "Above-terrain pose produced nonzero DeltaY.");
                Require(result.OutputPosition.Equals(input),
                    "Above-terrain pose moved despite zero correction.");
            }

            return "Negative requirements clamp to zero; downward movement is impossible.";
        }

        private static string VerifyExactHorizontalCoordinates()
        {
            using (var fixture = new Fixture("ExactXZ"))
            {
                Vector3 input = new Vector3(0.1234567f,
                    SupportedRootY(fixture.Profile) - 0.04f, -0.2345678f);
                RovTerrainContactResult result =
                    fixture.Constraint.Evaluate(input, Quaternion.Euler(0f, 23f, 0f));
                Require(result.Decision == RovTerrainContactDecision.Apply,
                    "XZ fixture did not apply.");
                Require(result.OutputPosition.x.Equals(input.x) &&
                        result.OutputPosition.z.Equals(input.z),
                    "Output X or Z changed at the bitwise float level.");
            }

            return "Output X and Z are copied exactly from the input.";
        }

        private static string VerifyExactYaw()
        {
            using (var fixture = new Fixture("ExactYaw"))
            {
                Quaternion inputRotation = Quaternion.Euler(0f, 37f, 0f);
                Vector3 input = new Vector3(0f,
                    SupportedRootY(fixture.Profile) - 0.02f, 0f);
                RovTerrainContactResult result =
                    fixture.Constraint.Evaluate(input, inputRotation);
                Require(result.Decision == RovTerrainContactDecision.Apply,
                    "Yaw fixture did not apply.");
                Require(result.OutputRotation.Equals(inputRotation),
                    "Output quaternion differs from the original yaw quaternion.");
            }

            return "Yaw footprint evaluates and the original quaternion is returned exactly.";
        }

        private static string VerifyPitchRoll()
        {
            using (var fixture = new Fixture("PitchRoll"))
            {
                Quaternion unit = Quaternion.Euler(6f, 11f, -5f);
                Quaternion raw = new Quaternion(
                    unit.x * 2f, unit.y * 2f, unit.z * 2f, unit.w * 2f);
                Vector3 input = new Vector3(0f,
                    SupportedRootY(fixture.Profile) + 0.12f, 0f);
                RovTerrainContactResult result = fixture.Constraint.Evaluate(input, raw);
                Require(result.Decision == RovTerrainContactDecision.Apply,
                    "Pitch/roll fixture did not apply.");
                Require(result.OutputRotation.Equals(raw),
                    "Original non-unit quaternion was not preserved in output.");
                RequireVectorNear(result.LeftFront.ProjectedContactPoint,
                    input + unit * fixture.Profile.LeftFrontOffset,
                    Tolerance,
                    "normalized left-front projection");
            }

            return "Normalized-copy footprint math supports pitch/roll while preserving raw rotation.";
        }

        private static string VerifySlopeBoundaryAccepted()
        {
            using (var fixture = new Fixture(
                       "Slope12Accepted",
                       terrainRotation: Quaternion.Euler(0f, 0f, 12f)))
            {
                Vector3 input = new Vector3(0f,
                    SupportedRootY(fixture.Profile) + 0.12f, 0f);
                RovTerrainContactResult result =
                    fixture.Constraint.Evaluate(input, Quaternion.identity);
                Require(result.Decision == RovTerrainContactDecision.Apply,
                    "Exact 12-degree terrain was rejected.");
                RequireNear(result.MaximumObservedSlopeDegrees, 12f, 0.001f,
                    "accepted boundary slope");
                Require(result.MaximumObservedSlopeDegrees <=
                        fixture.Profile.MaximumSlopeDegrees,
                    "Sampler represented exact boundary above the contract value.");
            }

            return "Sampler-measured 12-degree boundary is accepted.";
        }

        private static string VerifySlopeRejected()
        {
            using (var fixture = new Fixture(
                       "SlopeRejected",
                       terrainRotation: Quaternion.Euler(0f, 0f, 12.5f)))
            {
                Vector3 input = new Vector3(0f,
                    SupportedRootY(fixture.Profile) + 0.12f, 0f);
                RovTerrainContactResult result =
                    fixture.Constraint.Evaluate(input, Quaternion.identity);
                RequireHold(result, RovTerrainContactState.SlopeRejected, input);
                Require(result.MaximumObservedSlopeDegrees > 12f,
                    "Rejected fixture did not exceed 12 degrees.");
            }

            return "A slope above 12 degrees holds the current pose.";
        }

        private static string VerifyCorrectionBoundaryAccepted()
        {
            RovContactProfile profile = ZeroHeightProfile();
            using (var fixture = new Fixture("Correction030", profile))
            {
                fixture.TerrainObject.transform.position = new Vector3(
                    0f, -profile.GroundClearance, 0f);
                Physics.SyncTransforms();
                Vector3 input = new Vector3(
                    0f, -profile.MaximumVerticalCorrectionMeters, 0f);
                RovTerrainContactResult result =
                    fixture.Constraint.Evaluate(input, Quaternion.identity);
                RequireApply(result, RovTerrainContactState.Corrected);
                Require(result.DeltaY.Equals(
                        profile.MaximumVerticalCorrectionMeters),
                    "Boundary fixture did not produce the exact stored maximum.");
            }

            return "The 0.30-meter correction boundary is accepted without truncation.";
        }

        private static string VerifyCorrectionRejected()
        {
            RovContactProfile profile = ZeroHeightProfile();
            using (var fixture = new Fixture("CorrectionRejected", profile))
            {
                Vector3 input = new Vector3(0f,
                    SurfaceY + profile.GroundClearance - 0.301f, 0f);
                RovTerrainContactResult result =
                    fixture.Constraint.Evaluate(input, Quaternion.identity);
                RequireHold(result, RovTerrainContactState.CorrectionRejected, input);
                Require(result.DeltaY.Equals(0f),
                    "Rejected result exposed a truncated correction.");
                Require(result.LeftFront.RequiredVerticalCorrectionMeters > 0.30f,
                    "Rejected fixture did not require more than 0.30 meters.");
            }

            return "Above-limit correction holds current with zero applied DeltaY.";
        }

        private static string VerifyOneNoHit()
        {
            using (var fixture = new Fixture("OneNoHit"))
            {
                Vector3 input = new Vector3(1.1f,
                    SupportedRootY(fixture.Profile), 0f);
                RovTerrainContactResult result =
                    fixture.Constraint.Evaluate(input,
                        Quaternion.Euler(0f, 45f, 0f));
                RequireHold(result,
                    RovTerrainContactState.OutOfTerrainBounds, input);
                Require(result.ValidSampleCount == 3,
                    "Triangle fixture did not produce exactly three valid probes.");
                Require(CountFailure(result,
                        TerrainSurfaceSampleFailureReason.NoHit) == 1,
                    "Triangle fixture did not produce exactly one NoHit.");
            }

            return "A single NoHit yields OutOfTerrainBounds with no partial apply.";
        }

        private static string VerifyOtherSamplerFailure()
        {
            using (var fixture = new Fixture("DisabledSampler"))
            {
                fixture.Collider.enabled = false;
                Vector3 input = new Vector3(0f,
                    SupportedRootY(fixture.Profile), 0f);
                RovTerrainContactResult result =
                    fixture.Constraint.Evaluate(input, Quaternion.identity);
                RequireHold(result,
                    RovTerrainContactState.NoValidTerrainSample, input);
                Require(result.ValidSampleCount == 0,
                    "Disabled collider unexpectedly yielded valid samples.");
            }

            return "Non-NoHit sampler failures map to NoValidTerrainSample.";
        }

        private static string VerifyMissingConfiguration()
        {
            GameObject terrain = new GameObject("MissingConfig_Terrain");
            GameObject constraintObject = new GameObject("MissingConfig_Constraint");
            Mesh mesh = CreatePlaneMesh("MissingConfig_Mesh");
            try
            {
                terrain.transform.position = new Vector3(0f, SurfaceY, 0f);
                MeshCollider collider = terrain.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                TerrainSurfaceSampler sampler =
                    terrain.AddComponent<TerrainSurfaceSampler>();
                sampler.Configure(collider);
                RovTerrainContactConstraint constraint = constraintObject
                    .AddComponent<RovTerrainContactConstraint>();
                RovTerrainContactResult bothMissing = constraint.Evaluate(
                    Vector3.zero, Quaternion.identity);
                Require(bothMissing.Decision == RovTerrainContactDecision.HoldCurrent &&
                        bothMissing.State == RovTerrainContactState.InvalidProfile,
                    "Unconfigured constraint did not fail closed.");
                SetPrivateField(constraint, "surfaceSampler", sampler);
                RovTerrainContactResult profileMissing = constraint.Evaluate(
                    Vector3.zero, Quaternion.identity);
                Require(profileMissing.State == RovTerrainContactState.InvalidProfile,
                    "Missing profile did not return InvalidProfile.");
                SetPrivateField(constraint, "surfaceSampler", null);
                SetPrivateField(constraint, "profile",
                    RovContactProfile.CreateApprovedDefault());
                RovTerrainContactResult samplerMissing = constraint.Evaluate(
                    Vector3.zero, Quaternion.identity);
                Require(samplerMissing.State ==
                        RovTerrainContactState.NoValidTerrainSample,
                    "Missing sampler did not return NoValidTerrainSample.");
                Require(!constraint.TryValidate(out string error) &&
                        !string.IsNullOrEmpty(error),
                    "Missing configuration validation unexpectedly passed.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(constraintObject);
                UnityEngine.Object.DestroyImmediate(terrain);
                UnityEngine.Object.DestroyImmediate(mesh);
            }

            return "Missing sampler/profile bindings fail closed with explicit validation.";
        }

        private static string VerifyInvalidPosition()
        {
            using (var fixture = new Fixture("InvalidPosition"))
            {
                RovTerrainContactResult nan = fixture.Constraint.Evaluate(
                    new Vector3(float.NaN, 0f, 0f), Quaternion.identity);
                RovTerrainContactResult infinity = fixture.Constraint.Evaluate(
                    new Vector3(0f, float.PositiveInfinity, 0f),
                    Quaternion.identity);
                RequireInvalidPose(nan);
                RequireInvalidPose(infinity);
            }

            return "NaN and Infinity positions return safe InvalidPose results.";
        }

        private static string VerifyInvalidQuaternion()
        {
            using (var fixture = new Fixture("InvalidQuaternion"))
            {
                RovTerrainContactResult zero = fixture.Constraint.Evaluate(
                    Vector3.zero, new Quaternion(0f, 0f, 0f, 0f));
                RovTerrainContactResult nan = fixture.Constraint.Evaluate(
                    Vector3.zero, new Quaternion(float.NaN, 0f, 0f, 1f));
                RequireInvalidPose(zero);
                RequireInvalidPose(nan);
            }

            return "Zero and non-finite quaternions return safe InvalidPose results.";
        }

        private static string VerifyAllFourRequired()
        {
            using (var fixture = new Fixture("NoThreePointFallback"))
            {
                Vector3 input = new Vector3(1.1f,
                    SupportedRootY(fixture.Profile) - 0.02f, 0f);
                RovTerrainContactResult result =
                    fixture.Constraint.Evaluate(input,
                        Quaternion.Euler(0f, 45f, 0f));
                Require(result.ValidSampleCount == 3,
                    "Expected exactly three successful samples.");
                Require(result.Decision == RovTerrainContactDecision.HoldCurrent &&
                        result.State == RovTerrainContactState.OutOfTerrainBounds,
                    "Three successful samples incorrectly produced an Apply decision.");
                Require(result.OutputPosition.Equals(input) && result.DeltaY.Equals(0f),
                    "Three-point result applied a partial correction.");
            }

            return "Three samples cannot substitute for the fixed four-probe contract.";
        }

        private static string VerifyImmutability()
        {
            using (var fixture = new Fixture("Immutable"))
            {
                var snapshot = new FixtureSnapshot(fixture);
                Vector3 input = new Vector3(0f,
                    SupportedRootY(fixture.Profile) - 0.04f, 0f);
                fixture.Constraint.Evaluate(input, Quaternion.Euler(3f, 19f, -2f));
                snapshot.RequireUnchanged(fixture, "Evaluate");
            }

            return "Evaluation leaves terrain, mesh, bindings, profile, and object poses unchanged.";
        }

        private static string VerifyRepeatedDeterminism()
        {
            using (var fixture = new Fixture("Repeat100"))
            {
                var snapshot = new FixtureSnapshot(fixture);
                Vector3 input = new Vector3(0.125f,
                    SupportedRootY(fixture.Profile) + 0.09f, -0.125f);
                Quaternion rotation = Quaternion.Euler(4f, 21f, -3f);
                RovTerrainContactResult first =
                    fixture.Constraint.Evaluate(input, rotation);
                for (int index = 1; index < 100; index++)
                {
                    RovTerrainContactResult next =
                        fixture.Constraint.Evaluate(input, rotation);
                    RequireResultExactlyEqual(first, next,
                        "repeat " + index);
                }

                snapshot.RequireUnchanged(fixture, "Repeated evaluation");
            }

            return "100 identical evaluations reproduce every result and observation field exactly.";
        }

        private static string VerifyNoStateAccumulation()
        {
            using (var fixture = new Fixture("NoState"))
            {
                Vector3 originalInput = new Vector3(0f,
                    SupportedRootY(fixture.Profile) - 0.03f, 0f);
                Quaternion originalRotation = Quaternion.Euler(0f, 9f, 0f);
                RovTerrainContactResult first = fixture.Constraint.Evaluate(
                    originalInput, originalRotation);
                fixture.Constraint.Evaluate(
                    new Vector3(0.2f,
                        SupportedRootY(fixture.Profile) + 0.2f, -0.2f),
                    Quaternion.Euler(5f, 31f, -4f));
                fixture.Constraint.Evaluate(
                    new Vector3(0f,
                        SupportedRootY(fixture.Profile) - 0.301f, 0f),
                    Quaternion.identity);
                RovTerrainContactResult after = fixture.Constraint.Evaluate(
                    originalInput, originalRotation);
                RequireResultExactlyEqual(first, after,
                    "alternating-input replay");
            }

            return "Intervening evaluations cannot alter a repeated input result.";
        }

        private static string VerifySourceBoundary()
        {
            Type constraintType = typeof(RovTerrainContactConstraint);
            BindingFlags declared = BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly;
            foreach (string lifecycle in new[]
                     {
                         "Awake", "Start", "OnEnable", "Update", "LateUpdate",
                         "FixedUpdate", "OnDisable", "OnDestroy"
                     })
            {
                Require(constraintType.GetMethod(lifecycle, declared) == null,
                    lifecycle + " must not exist on the constraint.");
            }

            FieldInfo[] fields = constraintType.GetFields(declared);
            Require(FindField(fields, "surfaceSampler",
                        typeof(TerrainSurfaceSampler)) &&
                    FindField(fields, "profile", typeof(RovContactProfile)) &&
                    FindField(fields, "waterSurfaceProvider",
                        typeof(FlatWaterSurfaceProvider)),
                "Constraint bindings differ from the terrain/profile/water contract.");

            string rovRoot = Path.Combine(
                Application.dataPath, "Scripts", "Visualization", "Runtime", "Rov");
            string profileSource = File.ReadAllText(Path.Combine(
                rovRoot, "RovContactProfile.cs"));
            string constraintSource = File.ReadAllText(Path.Combine(
                rovRoot, "RovTerrainContactConstraint.cs"));
            string evaluatorSource = File.ReadAllText(Path.Combine(
                rovRoot, "RovSafetyEvaluator.cs"));
            Require(constraintSource.Contains("RovSafetyEvaluator.Evaluate(") &&
                    evaluatorSource.Contains("provider.TrySampleAtXZ("),
                "Constraint does not consume the shared authoritative evaluator.");
            foreach (string forbidden in new[]
                     {
                         "UnityEditor", "EnvE3A", "VehiclePoseDriver",
                         "VehicleState", "DataSource", "Policy", "Store",
                         "Physics.", "Rigidbody", "Time.", "SmoothDamp",
                         "SetPositionAndRotation", ".position =", ".rotation =",
                         ".localPosition =", ".localRotation =", "RaycastHit"
                     })
            {
                Require(!profileSource.Contains(forbidden) &&
                        !constraintSource.Contains(forbidden),
                    "Runtime source contains prohibited token: " + forbidden);
            }
            foreach (string forbidden in new[]
                     {
                         "UnityEditor", "Physics.", "Rigidbody", "Time.",
                         "SetPositionAndRotation", ".position =", ".rotation =",
                         ".localPosition =", ".localRotation =", "RaycastHit",
                         "Renderer.bounds"
                     })
            {
                Require(!evaluatorSource.Contains(forbidden),
                    "Shared evaluator contains prohibited token: " + forbidden);
            }

            int evaluateStart = constraintSource.IndexOf(
                "public RovTerrainContactResult Evaluate(",
                StringComparison.Ordinal);
            int evaluateEnd = constraintSource.IndexOf(
                "public UnityPoseConstraintResult Constrain(",
                StringComparison.Ordinal);
            Require(evaluateStart >= 0 && evaluateEnd > evaluateStart,
                "Could not isolate Evaluate source body.");
            string evaluateSource = constraintSource.Substring(
                evaluateStart, evaluateEnd - evaluateStart);
            foreach (string forbidden in new[]
                     {
                         "new[]", "new []", "List<", "System.Linq",
                         ".ToArray(", ".Select(", ".Where("
                     })
            {
                Require(!evaluateSource.Contains(forbidden),
                    "Evaluate contains prohibited collection/LINQ token: " +
                    forbidden);
            }

            return "Reflection and exact source checks prove the bounded runtime authority.";
        }

        private static RovContactProfile MakeProfile(
            Vector3? leftFront = null,
            Vector3? leftRear = null,
            Vector3? rightFront = null,
            Vector3? rightRear = null,
            float? clearance = null,
            float? probeStart = null,
            float? probeDistance = null,
            float? maximumSlope = null,
            float? maximumCorrection = null,
            float? epsilon = null)
        {
            RovContactProfile approved =
                RovContactProfile.CreateApprovedDefault();
            return new RovContactProfile(
                leftFront ?? approved.LeftFrontOffset,
                leftRear ?? approved.LeftRearOffset,
                rightFront ?? approved.RightFrontOffset,
                rightRear ?? approved.RightRearOffset,
                approved.UpperEnvelopeMinimum,
                approved.UpperEnvelopeMaximum,
                clearance ?? approved.GroundClearance,
                probeStart ?? approved.ProbeStartHeightMeters,
                probeDistance ?? approved.ProbeDistanceMeters,
                maximumSlope ?? approved.MaximumSlopeDegrees,
                maximumCorrection ?? approved.MaximumVerticalCorrectionMeters,
                epsilon ?? approved.EpsilonMeters);
        }

        private static RovContactProfile ZeroHeightProfile()
        {
            RovContactProfile approved =
                RovContactProfile.CreateApprovedDefault();
            return new RovContactProfile(
                new Vector3(approved.LeftFrontOffset.x, 0f,
                    approved.LeftFrontOffset.z),
                new Vector3(approved.LeftRearOffset.x, 0f,
                    approved.LeftRearOffset.z),
                new Vector3(approved.RightFrontOffset.x, 0f,
                    approved.RightFrontOffset.z),
                new Vector3(approved.RightRearOffset.x, 0f,
                    approved.RightRearOffset.z),
                approved.UpperEnvelopeMinimum,
                approved.UpperEnvelopeMaximum,
                approved.GroundClearance,
                approved.ProbeStartHeightMeters,
                approved.ProbeDistanceMeters,
                approved.MaximumSlopeDegrees,
                approved.MaximumVerticalCorrectionMeters,
                approved.EpsilonMeters);
        }

        private static float SupportedRootY(RovContactProfile profile)
        {
            return SurfaceY + profile.GroundClearance -
                profile.LeftFrontOffset.y;
        }

        private static Mesh CreatePlaneMesh(
            string name,
            float slopeDegrees = 0f)
        {
            float slope = Mathf.Tan(slopeDegrees * Mathf.Deg2Rad);
            var mesh = new Mesh
            {
                name = name,
                vertices = new[]
                {
                    new Vector3(-2f, -2f * slope, -2f),
                    new Vector3(-2f, -2f * slope, 2f),
                    new Vector3(2f, 2f * slope, -2f),
                    new Vector3(2f, 2f * slope, 2f)
                },
                triangles = new[] { 0, 3, 2, 0, 1, 3 }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void RequireApply(
            RovTerrainContactResult result,
            RovTerrainContactState expectedState)
        {
            Require(result.Decision == RovTerrainContactDecision.Apply &&
                    result.State == expectedState,
                "Expected Apply/" + expectedState + ", got " +
                result.Decision + "/" + result.State + ".");
        }

        private static void RequireHold(
            RovTerrainContactResult result,
            RovTerrainContactState expectedState,
            Vector3 expectedPosition)
        {
            Require(result.Decision == RovTerrainContactDecision.HoldCurrent &&
                    result.State == expectedState,
                "Expected HoldCurrent/" + expectedState + ", got " +
                result.Decision + "/" + result.State + ".");
            Require(result.OutputPosition.Equals(expectedPosition),
                "Hold result did not preserve the current valid position.");
            Require(result.DeltaY.Equals(0f),
                "Hold result exposed a nonzero applied correction.");
        }

        private static void RequireInvalidPose(RovTerrainContactResult result)
        {
            Require(result.Decision == RovTerrainContactDecision.HoldCurrent &&
                    result.State == RovTerrainContactState.InvalidPose,
                "Invalid pose did not fail closed.");
            Require(result.OutputPosition.Equals(Vector3.zero) &&
                    result.OutputRotation.Equals(Quaternion.identity) &&
                    result.DeltaY.Equals(0f) &&
                    result.ValidSampleCount == 0,
                "Invalid pose result is not a safe default.");
        }

        private static bool HasDifferentRequirements(
            RovTerrainContactResult result)
        {
            float first = result.LeftFront.RequiredVerticalCorrectionMeters;
            return !first.Equals(result.LeftRear.RequiredVerticalCorrectionMeters) ||
                !first.Equals(result.RightFront.RequiredVerticalCorrectionMeters) ||
                !first.Equals(result.RightRear.RequiredVerticalCorrectionMeters);
        }

        private static int CountFailure(
            RovTerrainContactResult result,
            TerrainSurfaceSampleFailureReason reason)
        {
            int count = 0;
            if (!result.LeftFront.HasValidSample &&
                result.LeftFront.FailureReason == reason) count++;
            if (!result.LeftRear.HasValidSample &&
                result.LeftRear.FailureReason == reason) count++;
            if (!result.RightFront.HasValidSample &&
                result.RightFront.FailureReason == reason) count++;
            if (!result.RightRear.HasValidSample &&
                result.RightRear.FailureReason == reason) count++;
            return count;
        }

        private static void RequireResultExactlyEqual(
            RovTerrainContactResult expected,
            RovTerrainContactResult actual,
            string label)
        {
            Require(expected.Decision == actual.Decision &&
                    expected.State == actual.State &&
                    expected.OutputPosition.Equals(actual.OutputPosition) &&
                    expected.OutputRotation.Equals(actual.OutputRotation) &&
                    expected.DeltaY.Equals(actual.DeltaY) &&
                    expected.MaximumObservedSlopeDegrees.Equals(
                        actual.MaximumObservedSlopeDegrees) &&
                    expected.ValidSampleCount == actual.ValidSampleCount,
                label + " changed a result field.");
            RequireObservationExactlyEqual(expected.LeftFront,
                actual.LeftFront, label + " left-front");
            RequireObservationExactlyEqual(expected.LeftRear,
                actual.LeftRear, label + " left-rear");
            RequireObservationExactlyEqual(expected.RightFront,
                actual.RightFront, label + " right-front");
            RequireObservationExactlyEqual(expected.RightRear,
                actual.RightRear, label + " right-rear");
        }

        private static void RequireObservationExactlyEqual(
            RovContactProbeObservation expected,
            RovContactProbeObservation actual,
            string label)
        {
            Require(expected.ProjectedContactPoint.Equals(
                        actual.ProjectedContactPoint) &&
                    expected.HasValidSample == actual.HasValidSample &&
                    expected.FailureReason == actual.FailureReason &&
                    expected.RequiredVerticalCorrectionMeters.Equals(
                        actual.RequiredVerticalCorrectionMeters) &&
                    expected.Sample.Point.Equals(actual.Sample.Point) &&
                    expected.Sample.Normal.Equals(actual.Sample.Normal) &&
                    expected.Sample.Distance.Equals(actual.Sample.Distance) &&
                    expected.Sample.SlopeDegrees.Equals(
                        actual.Sample.SlopeDegrees) &&
                    expected.Sample.TriangleIndex == actual.Sample.TriangleIndex,
                label + " changed an observation/sample field.");
        }

        private static void RequireVectorNear(
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

        private static void RequireArrayExactly(
            Vector3[] expected,
            Vector3[] actual,
            string message)
        {
            Require(expected.Length == actual.Length, message);
            for (int index = 0; index < expected.Length; index++)
                Require(expected[index].Equals(actual[index]), message);
        }

        private static void RequireArrayExactly(
            int[] expected,
            int[] actual,
            string message)
        {
            Require(expected.Length == actual.Length, message);
            for (int index = 0; index < expected.Length; index++)
                Require(expected[index] == actual[index], message);
        }

        private static bool FindField(
            FieldInfo[] fields,
            string name,
            Type type)
        {
            foreach (FieldInfo field in fields)
            {
                if (field.Name == name && field.FieldType == type)
                    return true;
            }

            return false;
        }

        private static void SetPrivateField(
            object target,
            string name,
            object value)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, "Missing private field " + name + ".");
            field.SetValue(target, value);
        }

        private static string RequireExternalReportPath()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            string value = string.Empty;
            for (int index = 0; index + 1 < arguments.Length; index++)
            {
                if (string.Equals(arguments[index], ReportPathArgument,
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
            Require(!fullPath.StartsWith(projectPrefix,
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
                throw new InvalidOperationException(message);
        }
    }
}
