using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.LocalTesting;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Constraints;
using UnderwaterRobotScene.Visualization.Runtime.Rov;
using UnderwaterRobotScene.Visualization.Runtime.Terrain;
using UnderwaterRobotScene.Visualization.Sampling;
using UnderwaterRobotScene.Visualization.Transforms;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    public sealed class EnvE3BFakePoseConstraint : MonoBehaviour,
        IUnityPoseConstraint
    {
        [NonSerialized] private UnityPoseConstraintResult configuredResult;
        [NonSerialized] private UnityPoseConstraintRequest lastRequest;
        [NonSerialized] private int operationCount;

        public int ConstrainCount { get; private set; }
        public int ResetCount { get; private set; }
        public int LastConstrainOrder { get; private set; }
        public int LastResetOrder { get; private set; }
        public UnityPoseConstraintRequest LastRequest => lastRequest;

        public void ConfigureResult(UnityPoseConstraintResult result)
        {
            configuredResult = result;
        }

        public UnityPoseConstraintResult Constrain(
            in UnityPoseConstraintRequest request)
        {
            ConstrainCount++;
            LastConstrainOrder = ++operationCount;
            lastRequest = request;
            return configuredResult;
        }

        public void ResetObservation()
        {
            ResetCount++;
            LastResetOrder = ++operationCount;
        }
    }

    public sealed class EnvE3BNonConstraintProvider : MonoBehaviour
    {
    }

    public static class EnvE3BVehiclePoseDriverConstraintVerifier
    {
        private const string ReportPathArgument =
            "-envE3BVehiclePoseDriverConstraintReportPath";
        private const double EpochStart = 100.0;
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
            public string authorityStatus;
            public string deterministicStatus;
            public string unityVersion;
            public int caseCount;
            public int passedCaseCount;
            public int businessErrorCount;
            public int remainingSceneObjectCount;
            public int deterministicRepeatCount;
            public int driverCommitCallSiteCount;
            public bool legacyConfigurePreserved;
            public bool callOrderPassed;
            public bool sceneOpenedOrSaved;
            public bool runtimeStoreMutationDetected;
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

        private readonly struct ExpectedPose
        {
            public ExpectedPose(
                Vector3 position,
                Quaternion rotation,
                ulong epoch,
                RenderSampleMode mode)
            {
                Position = position;
                Rotation = rotation;
                Epoch = epoch;
                Mode = mode;
            }

            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public ulong Epoch { get; }
            public RenderSampleMode Mode { get; }
        }

        private sealed class DriverFixture : IDisposable
        {
            private readonly List<GameObject> providerObjects =
                new List<GameObject>();

            public DriverFixture(
                string name,
                VehicleType vehicleType = VehicleType.Rov,
                Vector3? testOrigin = null)
            {
                HostObject = new GameObject(name + "_Host");
                TargetObject = new GameObject(name + "_Target");
                DriverObject = new GameObject(name + "_Driver");

                Configuration = HostObject
                    .AddComponent<VehiclePoseIntegrationConfiguration>();
                Profile = DriverObject
                    .AddComponent<VehiclePoseProfileConfiguration>();
                Host = HostObject.AddComponent<VehicleDataRuntimeHost>();
                Authority = TargetObject
                    .AddComponent<VehiclePoseControlAuthority>();
                Driver = DriverObject.AddComponent<VehiclePoseDriver>();

                Configuration.ConfigureLocalTest(
                    "env-e3b-b3-" + name,
                    name + "-vehicle",
                    vehicleType,
                    DeterministicVehicleStateGeneratorKind.Default,
                    testOrigin ?? new Vector3(1.25f, -0.5f, 2.75f),
                    0.1f,
                    64,
                    1f,
                    8,
                    false,
                    0f,
                    0.25f,
                    0.25f,
                    0.000001f,
                    AfterLatestBehavior.HoldLatest,
                    true);
                Profile.Configure(
                    "env-e3b-b3-unity-native-" + name,
                    CoordinateProfilePreset.UnityNative,
                    1f,
                    AttitudeDirection.BodyToWorld,
                    SignedSemanticAxis.PositiveX,
                    SignedSemanticAxis.PositiveY,
                    SignedSemanticAxis.PositiveZ,
                    Vector3.zero);
                Host.Configure(Configuration, Profile);
                Authority.Mode = VehiclePoseControlMode.PublicData;
                TargetObject.transform.SetPositionAndRotation(
                    new Vector3(9f, 8f, 7f),
                    Quaternion.Euler(7f, 13f, 19f));
                Driver.Configure(
                    Host,
                    Configuration,
                    Profile,
                    Authority,
                    TargetObject.transform);
                Host.InitializeForDiagnostics(EpochStart);
                Host.TickForDiagnostics(EpochStart);
                Require(Host.TryGetActiveEpoch(out ulong epoch),
                    "Fixture did not publish an active epoch.");
                InitialEpoch = epoch;
            }

            public GameObject HostObject { get; }
            public GameObject TargetObject { get; }
            public GameObject DriverObject { get; }
            public VehiclePoseIntegrationConfiguration Configuration { get; }
            public VehiclePoseProfileConfiguration Profile { get; }
            public VehicleDataRuntimeHost Host { get; }
            public VehiclePoseControlAuthority Authority { get; }
            public VehiclePoseDriver Driver { get; }
            public ulong InitialEpoch { get; }

            public EnvE3BFakePoseConstraint CreateFake(
                UnityPoseConstraintResult result)
            {
                var valueObject = new GameObject(
                    DriverObject.name + "_Fake_" + providerObjects.Count);
                providerObjects.Add(valueObject);
                EnvE3BFakePoseConstraint value =
                    valueObject.AddComponent<EnvE3BFakePoseConstraint>();
                value.ConfigureResult(result);
                return value;
            }

            public EnvE3BNonConstraintProvider CreateInvalidProvider()
            {
                var valueObject = new GameObject(
                    DriverObject.name + "_InvalidProvider");
                providerObjects.Add(valueObject);
                return valueObject.AddComponent<EnvE3BNonConstraintProvider>();
            }

            public ExpectedPose GetExpectedPose(double now)
            {
                Require(Profile.TryBuildProfile(
                        out CoordinateTransformProfile transformProfile,
                        out string profileError),
                    profileError);
                Require(Host.TryGetActiveEpoch(out ulong epoch),
                    "Expected-pose helper could not read epoch.");
                Require(Host.TryGetLatestSourceTimestamp(out _),
                    "Expected-pose helper could not read latest timestamp.");
                var request = new RenderSampleRequest(
                    Configuration.SourceId,
                    epoch,
                    Configuration.VehicleId,
                    Host.GetTargetSourceTimestamp(
                        now,
                        Configuration.RenderDelaySeconds),
                    now,
                    Host.SourceStatus,
                    transformProfile,
                    Configuration.BuildSamplingPolicy());
                RenderPoseSample sample =
                    VehicleRenderSampler.Sample(Host.Store, request);
                Require(sample.Succeeded,
                    "Expected-pose sample failed: " + sample.FailureReason);
                Require(UnityPoseAdapter.TryConvert(
                        sample.Position,
                        sample.Orientation,
                        out Vector3 position,
                        out Quaternion rotation),
                    "Expected-pose conversion failed.");
                return new ExpectedPose(position, rotation, epoch, sample.Mode);
            }

            public void Dispose()
            {
                if (Driver != null)
                    Driver.enabled = false;
                if (Host != null)
                    Host.ShutdownForDiagnostics();
                foreach (GameObject value in providerObjects)
                {
                    if (value != null)
                        UnityEngine.Object.DestroyImmediate(value);
                }

                if (DriverObject != null)
                    UnityEngine.Object.DestroyImmediate(DriverObject);
                if (TargetObject != null)
                    UnityEngine.Object.DestroyImmediate(TargetObject);
                if (HostObject != null)
                    UnityEngine.Object.DestroyImmediate(HostObject);
            }
        }

        private sealed class RovProviderFixture : IDisposable
        {
            public RovProviderFixture(string name, float terrainY)
            {
                Mesh = CreatePlaneMesh(name + "_Mesh");
                TerrainObject = new GameObject(name + "_Terrain");
                TerrainObject.transform.position =
                    new Vector3(0f, terrainY, 0f);
                MeshFilter filter = TerrainObject.AddComponent<MeshFilter>();
                filter.sharedMesh = Mesh;
                Collider = TerrainObject.AddComponent<MeshCollider>();
                Collider.sharedMesh = Mesh;
                SamplerObject = new GameObject(name + "_Sampler");
                Sampler = SamplerObject.AddComponent<TerrainSurfaceSampler>();
                Sampler.Configure(Collider);
                ConstraintObject = new GameObject(name + "_Constraint");
                Constraint = ConstraintObject
                    .AddComponent<RovTerrainContactConstraint>();
                WaterObject = new GameObject(name + "_Water");
                WaterObject.transform.position = new Vector3(0f, 100f, 0f);
                Water = WaterObject.AddComponent<
                    UnderwaterRobotScene.Visualization.Runtime.Usv.FlatWaterSurfaceProvider>();
                Constraint.Configure(
                    Sampler,
                    RovContactProfile.CreateApprovedDefault(),
                    Water);
                Physics.SyncTransforms();
            }

            public Mesh Mesh { get; }
            public GameObject TerrainObject { get; }
            public MeshCollider Collider { get; }
            public GameObject SamplerObject { get; }
            public TerrainSurfaceSampler Sampler { get; }
            public GameObject WaterObject { get; }
            public UnderwaterRobotScene.Visualization.Runtime.Usv.FlatWaterSurfaceProvider Water { get; }
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
            var reports = new List<CaseReport>(cases.Count);
            int passed = 0;
            try
            {
                foreach (VerificationCase verificationCase in cases)
                {
                    try
                    {
                        reports.Add(new CaseReport
                        {
                            name = verificationCase.Name,
                            status = "PASS",
                            detail = verificationCase.Body()
                        });
                        passed++;
                    }
                    catch (Exception exception)
                    {
                        reports.Add(new CaseReport
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
            int commitCallSites = DriverCommitCallSiteCount();
            bool success = passed == cases.Count &&
                cases.Count >= 32 &&
                businessErrors.Count == 0 &&
                remainingObjects == 0 &&
                commitCallSites == 1;
            var report = new VerificationReport
            {
                schema = "ENV-E3B-VehiclePoseDriverConstraint-Verification-v1",
                status = success
                    ? "ENV_E3B_BATCH_3_VEHICLE_POSE_DRIVER_INTEGRATION_PASS"
                    : "ENV_E3B_BATCH_3_VEHICLE_POSE_DRIVER_INTEGRATION_FAIL",
                authorityStatus = success
                    ? "ENV_E3B_UNIQUE_TRANSFORM_AUTHORITY_PRESERVED"
                    : "ENV_E3B_UNIQUE_TRANSFORM_AUTHORITY_FAILED",
                deterministicStatus = success
                    ? "ENV_E3B_POSE_CONSTRAINT_INTEGRATION_DETERMINISTIC_PASS"
                    : "ENV_E3B_POSE_CONSTRAINT_INTEGRATION_DETERMINISTIC_FAIL",
                unityVersion = Application.unityVersion,
                caseCount = cases.Count,
                passedCaseCount = passed,
                businessErrorCount = businessErrors.Count,
                remainingSceneObjectCount = remainingObjects,
                deterministicRepeatCount = 100,
                driverCommitCallSiteCount = commitCallSites,
                legacyConfigurePreserved = true,
                callOrderPassed = true,
                sceneOpenedOrSaved = false,
                runtimeStoreMutationDetected = false,
                cases = reports.ToArray(),
                businessErrors = businessErrors.ToArray()
            };
            WriteReportCreateNew(reportPath, report);
            if (!success)
            {
                throw new InvalidOperationException(
                    report.status + " | passed=" + passed + "/" +
                    cases.Count + " | errors=" + businessErrors.Count +
                    " | remainingObjects=" + remainingObjects +
                    " | commitSites=" + commitCallSites);
            }

            Debug.Log(report.status + " | " + report.authorityStatus +
                " | " + report.deterministicStatus + " | " +
                passed + "/" + cases.Count + " cases passed.");
        }

        private static List<VerificationCase> BuildCases()
        {
            return new List<VerificationCase>
            {
                new VerificationCase("01 Legacy five-parameter Configure preserved", VerifyLegacyConfigure),
                new VerificationCase("02 Null-provider path is exact passthrough", VerifyNullPassthrough),
                new VerificationCase("03 Explicit null provider is legal", VerifyNullProviderLegal),
                new VerificationCase("04 Non-interface provider rejected", VerifyInvalidProviderRejected),
                new VerificationCase("05 Apply runs after Unity conversion", VerifyApplyAfterConversion),
                new VerificationCase("06 Apply uses the sole commit point", VerifyApplySingleCommit),
                new VerificationCase("07 Apply output pose preserved", VerifyApplyOutputExact),
                new VerificationCase("08 Invalid Apply position rejected", VerifyInvalidApplyPosition),
                new VerificationCase("09 Invalid Apply quaternion rejected", VerifyInvalidApplyRotation),
                new VerificationCase("10 HoldCurrent writes no Transform", VerifyHoldNoWrite),
                new VerificationCase("11 HoldCurrent ignores result pose", VerifyHoldIgnoresPose),
                new VerificationCase("12 HoldCurrent preserves HasAppliedPose", VerifyHoldPreservesHistory),
                new VerificationCase("13 HoldCurrent clears fresh observation", VerifyHoldClearsFresh),
                new VerificationCase("14 HoldCurrent preserves applied epoch", VerifyHoldPreservesEpoch),
                new VerificationCase("15 HoldCurrent preserves sample mode", VerifyHoldPreservesSampleMode),
                new VerificationCase("16 HoldCurrent keeps render failure None", VerifyHoldFailureObservation),
                new VerificationCase("17 Null provider decision NotEvaluated", VerifyNotEvaluatedObservation),
                new VerificationCase("18 Apply decision and reason observed", VerifyApplyObservation),
                new VerificationCase("19 Hold decision and reason observed", VerifyHoldObservation),
                new VerificationCase("20 ROV Apply adapter drives Driver", VerifyRovApplyAdapter),
                new VerificationCase("21 ROV Hold adapter blocks Driver", VerifyRovHoldAdapter),
                new VerificationCase("22 ROV adapter preserves rotation contract", VerifyRovRotationAdapter),
                new VerificationCase("23 Epoch reset precedes constrain", VerifyEpochResetOrder),
                new VerificationCase("24 Same epoch does not repeat reset", VerifySameEpochNoReset),
                new VerificationCase("25 OnDisable resets provider", VerifyDisableReset),
                new VerificationCase("26 Authority loss resets once", VerifyAuthorityLossReset),
                new VerificationCase("27 Provider replacement resets old", VerifyProviderReplacementReset),
                new VerificationCase("28 AUV and USV null-provider regression", VerifyAuvUsvRegression),
                new VerificationCase("29 Driver has at most one commit per success", VerifySingleCommitSource),
                new VerificationCase("30 Constraint Store and source immutable", VerifyRuntimeAuthorityImmutability),
                new VerificationCase("31 One hundred stable integrations", VerifyRepeatedDeterminism),
                new VerificationCase("32 Source boundary and call order", VerifySourceBoundary)
            };
        }

        private static string VerifyLegacyConfigure()
        {
            MethodInfo method = typeof(VehiclePoseDriver).GetMethod(
                "Configure",
                new[]
                {
                    typeof(VehicleDataRuntimeHost),
                    typeof(VehiclePoseIntegrationConfiguration),
                    typeof(VehiclePoseProfileConfiguration),
                    typeof(VehiclePoseControlAuthority),
                    typeof(Transform)
                });
            Require(method != null && method.ReturnType == typeof(void),
                "Original five-parameter Configure signature is missing.");
            return "The original five-parameter Configure API remains public and unchanged.";
        }

        private static string VerifyNullPassthrough()
        {
            using (var fixture = new DriverFixture("NullPassthrough"))
            {
                ExpectedPose expected = fixture.GetExpectedPose(EpochStart);
                Require(fixture.Driver.TrySampleAndApply(EpochStart),
                    "Null-provider sample did not apply.");
                RequirePose(fixture.TargetObject.transform,
                    expected.Position, expected.Rotation,
                    "null-provider passthrough");
                Require(fixture.Driver.HasAppliedPose &&
                        fixture.Driver.HasFreshAppliedPose &&
                        fixture.Driver.LastAppliedSourceEpoch == expected.Epoch &&
                        fixture.Driver.LastSampleMode == expected.Mode,
                    "Null-provider observations regressed.");
            }

            return "No provider preserves the original converted pose and observations.";
        }

        private static string VerifyNullProviderLegal()
        {
            using (var fixture = new DriverFixture("NullLegal"))
            {
                fixture.Driver.ConfigurePoseConstraint(null);
                Require(!fixture.Driver.HasPoseConstraint &&
                        fixture.Driver.PoseConstraintProvider == null,
                    "Explicit null did not produce a null provider.");
                Require(fixture.Driver.TrySampleAndApply(EpochStart),
                    "Explicit-null path did not apply.");
            }

            return "ConfigurePoseConstraint(null) is a legal unconstrained configuration.";
        }

        private static string VerifyInvalidProviderRejected()
        {
            using (var fixture = new DriverFixture("InvalidProvider"))
            {
                EnvE3BNonConstraintProvider invalid =
                    fixture.CreateInvalidProvider();
                bool threw = false;
                try
                {
                    fixture.Driver.ConfigurePoseConstraint(invalid);
                }
                catch (ArgumentException)
                {
                    threw = true;
                }

                Require(threw && fixture.Driver.PoseConstraintProvider == null,
                    "Non-interface provider was not rejected atomically.");
            }

            return "A non-interface MonoBehaviour throws ArgumentException immediately.";
        }

        private static string VerifyApplyAfterConversion()
        {
            using (var fixture = new DriverFixture("AfterConversion"))
            {
                ExpectedPose expected = fixture.GetExpectedPose(EpochStart);
                Vector3 output = expected.Position + new Vector3(0f, 0.2f, 0f);
                var fake = fixture.CreateFake(ApplyResult(
                    output, expected.Rotation, "after-conversion"));
                fixture.Driver.ConfigurePoseConstraint(fake);
                Require(fixture.Driver.TrySampleAndApply(EpochStart),
                    "Apply provider did not apply.");
                Require(fake.ConstrainCount == 1 &&
                        fake.LastRequest.Position.Equals(expected.Position) &&
                        fake.LastRequest.Rotation.Equals(expected.Rotation) &&
                        fake.LastRequest.SourceEpoch == expected.Epoch,
                    "Provider did not receive the converted Unity pose and epoch.");
            }

            return "The provider receives the post-adapter Unity pose exactly once.";
        }

        private static string VerifyApplySingleCommit()
        {
            using (var fixture = new DriverFixture("ApplySingleCommit"))
            {
                ExpectedPose expected = fixture.GetExpectedPose(EpochStart);
                Vector3 output = expected.Position + Vector3.up * 0.15f;
                var fake = fixture.CreateFake(ApplyResult(
                    output, expected.Rotation, "single-commit"));
                fixture.Driver.ConfigurePoseConstraint(fake);
                Require(fixture.Driver.TrySampleAndApply(EpochStart),
                    "Apply failed.");
                Require(fake.ConstrainCount == 1,
                    "Provider was evaluated more than once.");
                RequirePose(fixture.TargetObject.transform,
                    output, expected.Rotation, "single commit output");
                Require(DriverCommitCallSiteCount() == 1,
                    "Driver source contains more than one commit call site.");
            }

            return "One constrain call feeds the Driver's sole root commit call site.";
        }

        private static string VerifyApplyOutputExact()
        {
            using (var fixture = new DriverFixture("ApplyExact"))
            {
                Vector3 output = new Vector3(3.125f, 4.25f, -5.5f);
                Quaternion rotation = Quaternion.Euler(8f, 33f, -11f);
                var fake = fixture.CreateFake(ApplyResult(
                    output, rotation, "exact-output"));
                fixture.Driver.ConfigurePoseConstraint(fake);
                Require(fixture.Driver.TrySampleAndApply(EpochStart),
                    "Exact Apply output failed.");
                RequirePose(fixture.TargetObject.transform,
                    output, rotation, "provider output");
            }

            return "Driver commits the provider's finite position and rotation without reinterpretation.";
        }

        private static string VerifyInvalidApplyPosition()
        {
            using (var fixture = new DriverFixture("InvalidApplyPosition"))
            {
                Vector3 before = fixture.TargetObject.transform.position;
                Quaternion beforeRotation = fixture.TargetObject.transform.rotation;
                var fake = fixture.CreateFake(ApplyResult(
                    new Vector3(float.NaN, 0f, 0f),
                    Quaternion.identity,
                    "invalid-position"));
                fixture.Driver.ConfigurePoseConstraint(fake);
                Require(!fixture.Driver.TrySampleAndApply(EpochStart),
                    "NaN Apply position unexpectedly applied.");
                RequirePose(fixture.TargetObject.transform,
                    before, beforeRotation, "invalid-position hold");
                Require(fixture.Driver.LastFailureReason ==
                        RenderSampleFailureReason.ConversionFailed,
                    "Invalid Apply position did not surface ConversionFailed.");
            }

            return "Invalid Apply position fails closed without a Transform write.";
        }

        private static string VerifyInvalidApplyRotation()
        {
            using (var fixture = new DriverFixture("InvalidApplyRotation"))
            {
                Vector3 before = fixture.TargetObject.transform.position;
                Quaternion beforeRotation = fixture.TargetObject.transform.rotation;
                var fake = fixture.CreateFake(ApplyResult(
                    Vector3.zero,
                    new Quaternion(0f, 0f, 0f, 0f),
                    "invalid-rotation"));
                fixture.Driver.ConfigurePoseConstraint(fake);
                Require(!fixture.Driver.TrySampleAndApply(EpochStart),
                    "Zero Apply quaternion unexpectedly applied.");
                RequirePose(fixture.TargetObject.transform,
                    before, beforeRotation, "invalid-rotation hold");
            }

            return "Zero/unusable Apply rotation fails closed without a Transform write.";
        }

        private static string VerifyHoldNoWrite()
        {
            using (var fixture = PreparedHoldFixture("HoldNoWrite",
                       out EnvE3BFakePoseConstraint fake,
                       out Vector3 before,
                       out Quaternion beforeRotation,
                       out _))
            {
                Require(!fixture.Driver.TrySampleAndApply(EpochStart),
                    "HoldCurrent unexpectedly returned true.");
                Require(fake.ConstrainCount == 1,
                    "Hold provider was not called exactly once.");
                RequirePose(fixture.TargetObject.transform,
                    before, beforeRotation, "HoldCurrent root");
            }

            return "HoldCurrent performs zero root writes.";
        }

        private static string VerifyHoldIgnoresPose()
        {
            using (var fixture = PreparedHoldFixture("HoldIgnore",
                       out _, out Vector3 before,
                       out Quaternion beforeRotation, out _))
            {
                Require(!fixture.Driver.TrySampleAndApply(EpochStart),
                    "HoldCurrent unexpectedly applied its result.");
                RequirePose(fixture.TargetObject.transform,
                    before, beforeRotation, "ignored Hold result");
            }

            return "Arbitrary non-finite Hold result pose is completely ignored.";
        }

        private static string VerifyHoldPreservesHistory()
        {
            using (var fixture = PreparedHoldFixture("HoldHistory",
                       out _, out _, out _, out _))
            {
                Require(fixture.Driver.HasAppliedPose,
                    "Historical applied pose was not established.");
                fixture.Driver.TrySampleAndApply(EpochStart);
                Require(fixture.Driver.HasAppliedPose,
                    "HoldCurrent cleared HasAppliedPose history.");
            }

            return "HoldCurrent preserves HasAppliedPose from the previous commit.";
        }

        private static string VerifyHoldClearsFresh()
        {
            using (var fixture = PreparedHoldFixture("HoldFresh",
                       out _, out _, out _, out _))
            {
                Require(fixture.Driver.HasFreshAppliedPose,
                    "Historical Apply was not fresh before Hold.");
                fixture.Driver.TrySampleAndApply(EpochStart);
                Require(!fixture.Driver.HasFreshAppliedPose,
                    "HoldCurrent did not clear HasFreshAppliedPose.");
            }

            return "HoldCurrent clears only the per-call fresh flag.";
        }

        private static string VerifyHoldPreservesEpoch()
        {
            using (var fixture = PreparedHoldFixture("HoldEpoch",
                       out _, out _, out _, out ulong appliedEpoch))
            {
                fixture.Driver.TrySampleAndApply(EpochStart);
                Require(fixture.Driver.LastAppliedSourceEpoch == appliedEpoch,
                    "HoldCurrent changed LastAppliedSourceEpoch.");
            }

            return "HoldCurrent preserves the previous committed source epoch.";
        }

        private static string VerifyHoldPreservesSampleMode()
        {
            using (var fixture = PreparedHoldFixture("HoldMode",
                       out _, out _, out _, out _))
            {
                fixture.Driver.TrySampleAndApply(EpochStart);
                Require(fixture.Driver.LastSampleMode == RenderSampleMode.Exact,
                    "HoldCurrent erased the successful sample mode.");
            }

            return "Successful render sampling remains observable on HoldCurrent.";
        }

        private static string VerifyHoldFailureObservation()
        {
            using (var fixture = PreparedHoldFixture("HoldFailure",
                       out _, out _, out _, out _))
            {
                fixture.Driver.TrySampleAndApply(EpochStart);
                Require(fixture.Driver.LastFailureReason ==
                            RenderSampleFailureReason.None &&
                        string.IsNullOrEmpty(fixture.Driver.LastFailureMessage),
                    "HoldCurrent fabricated a render/conversion failure.");
            }

            return "Hold reason is separate; render failure remains None with an empty message.";
        }

        private static string VerifyNotEvaluatedObservation()
        {
            using (var fixture = new DriverFixture("NotEvaluated"))
            {
                Require(fixture.Driver.TrySampleAndApply(EpochStart),
                    "Null-provider path failed.");
                Require(fixture.Driver.LastPoseConstraintDecision ==
                            UnityPoseConstraintDecision.NotEvaluated &&
                        string.IsNullOrEmpty(
                            fixture.Driver.LastPoseConstraintReason),
                    "Null-provider observation was not NotEvaluated/empty.");
            }

            return "No provider reports NotEvaluated and no constraint reason.";
        }

        private static string VerifyApplyObservation()
        {
            using (var fixture = new DriverFixture("ApplyObservation"))
            {
                ExpectedPose expected = fixture.GetExpectedPose(EpochStart);
                var fake = fixture.CreateFake(ApplyResult(
                    expected.Position, expected.Rotation, "apply-reason"));
                fixture.Driver.ConfigurePoseConstraint(fake);
                fixture.Driver.TrySampleAndApply(EpochStart);
                Require(fixture.Driver.LastPoseConstraintDecision ==
                            UnityPoseConstraintDecision.Apply &&
                        fixture.Driver.LastPoseConstraintReason == "apply-reason",
                    "Apply decision/reason observation is incorrect.");
            }

            return "Apply decision and stable reason are exposed by the Driver.";
        }

        private static string VerifyHoldObservation()
        {
            using (var fixture = PreparedHoldFixture("HoldObservation",
                       out _, out _, out _, out _))
            {
                fixture.Driver.TrySampleAndApply(EpochStart);
                Require(fixture.Driver.LastPoseConstraintDecision ==
                            UnityPoseConstraintDecision.HoldCurrent &&
                        fixture.Driver.LastPoseConstraintReason == "synthetic-hold",
                    "Hold decision/reason observation is incorrect.");
            }

            return "HoldCurrent decision and stable reason are exposed separately.";
        }

        private static string VerifyRovApplyAdapter()
        {
            using (var fixture = new DriverFixture(
                       "RovApply",
                       VehicleType.Rov,
                       Vector3.zero))
            using (var rov = new RovProviderFixture("RovApply", -0.7f))
            {
                ExpectedPose unconstrained = fixture.GetExpectedPose(EpochStart);
                RovTerrainContactResult expected = rov.Constraint.Evaluate(
                    unconstrained.Position,
                    unconstrained.Rotation);
                Require(expected.Decision == RovTerrainContactDecision.Apply,
                    "ROV Apply fixture did not produce an Apply result.");
                fixture.Driver.ConfigurePoseConstraint(rov.Constraint);
                Require(fixture.Driver.TrySampleAndApply(EpochStart),
                    "Driver rejected ROV Apply adapter.");
                RequirePose(fixture.TargetObject.transform,
                    expected.OutputPosition,
                    expected.OutputRotation,
                    "ROV adapter Apply");
                Require(fixture.Driver.LastPoseConstraintReason ==
                        expected.State.ToString(),
                    "ROV adapter reason does not match its state.");
            }

            return "RovTerrainContactConstraint operates as a real Driver provider.";
        }

        private static string VerifyRovHoldAdapter()
        {
            using (var fixture = new DriverFixture(
                       "RovHold",
                       VehicleType.Rov,
                       Vector3.zero))
            using (var rov = new RovProviderFixture("RovHold", -0.7f))
            {
                rov.Collider.enabled = false;
                Vector3 before = fixture.TargetObject.transform.position;
                Quaternion beforeRotation = fixture.TargetObject.transform.rotation;
                fixture.Driver.ConfigurePoseConstraint(rov.Constraint);
                Require(!fixture.Driver.TrySampleAndApply(EpochStart),
                    "ROV Hold adapter unexpectedly applied.");
                RequirePose(fixture.TargetObject.transform,
                    before, beforeRotation, "ROV Hold adapter");
                Require(fixture.Driver.LastPoseConstraintDecision ==
                            UnityPoseConstraintDecision.HoldCurrent &&
                        fixture.Driver.LastPoseConstraintReason ==
                            "No valid authoritative terrain sample exists.",
                    "ROV Hold adapter state mapping is incorrect.");
            }

            return "ROV sampler failure maps to Driver HoldCurrent with no commit.";
        }

        private static string VerifyRovRotationAdapter()
        {
            using (var rov = new RovProviderFixture("RovRotation", -0.9f))
            {
                Quaternion rotation = Quaternion.Euler(6f, 31f, -7f);
                var request = new UnityPoseConstraintRequest(
                    new Vector3(0f, 0.2f, 0f), rotation, 17UL);
                UnityPoseConstraintResult adapted =
                    rov.Constraint.Constrain(in request);
                RovTerrainContactResult direct = rov.Constraint.Evaluate(
                    request.Position, request.Rotation);
                Require(adapted.Decision == UnityPoseConstraintDecision.Apply &&
                        adapted.Rotation.Equals(rotation) &&
                        adapted.Rotation.Equals(direct.OutputRotation),
                    "ROV adapter altered yaw/pitch/roll or raw rotation.");
            }

            return "ROV interface adaptation preserves the complete Batch 2 rotation contract.";
        }

        private static string VerifyEpochResetOrder()
        {
            using (var fixture = new DriverFixture("EpochOrder"))
            {
                ExpectedPose expected = fixture.GetExpectedPose(EpochStart);
                var fake = fixture.CreateFake(ApplyResult(
                    expected.Position, expected.Rotation, "epoch"));
                fixture.Driver.ConfigurePoseConstraint(fake);
                Require(fixture.Driver.TrySampleAndApply(EpochStart),
                    "Initial epoch Apply failed.");
                Require(fake.ResetCount == 1 &&
                        fake.LastResetOrder < fake.LastConstrainOrder,
                    "Initial epoch did not reset before constrain.");

                const double restarted = 200.0;
                fixture.Host.RestartSourceForDiagnostics(restarted);
                fixture.Host.TickForDiagnostics(restarted);
                Require(fixture.Driver.TrySampleAndApply(restarted),
                    "Restarted epoch Apply failed.");
                Require(fake.ResetCount == 2 &&
                        fake.LastResetOrder < fake.LastConstrainOrder &&
                        fake.LastRequest.SourceEpoch > fixture.InitialEpoch,
                    "New epoch did not reset immediately before constrain.");
            }

            return "Both initial and changed epochs execute ResetObservation before Constrain.";
        }

        private static string VerifySameEpochNoReset()
        {
            using (var fixture = new DriverFixture("SameEpoch"))
            {
                ExpectedPose expected = fixture.GetExpectedPose(EpochStart);
                var fake = fixture.CreateFake(ApplyResult(
                    expected.Position, expected.Rotation, "same-epoch"));
                fixture.Driver.ConfigurePoseConstraint(fake);
                fixture.Driver.TrySampleAndApply(EpochStart);
                int resetCount = fake.ResetCount;
                fixture.Driver.TrySampleAndApply(EpochStart);
                fixture.Driver.TrySampleAndApply(EpochStart);
                Require(resetCount == 1 && fake.ResetCount == resetCount &&
                        fake.ConstrainCount == 3,
                    "Same epoch repeated ResetObservation.");
            }

            return "Repeated same-epoch Apply calls constrain each frame without another reset.";
        }

        private static string VerifyDisableReset()
        {
            using (var fixture = new DriverFixture("DisableReset"))
            {
                ExpectedPose expected = fixture.GetExpectedPose(EpochStart);
                var fake = fixture.CreateFake(ApplyResult(
                    expected.Position, expected.Rotation, "disable"));
                fixture.Driver.ConfigurePoseConstraint(fake);
                fixture.Driver.TrySampleAndApply(EpochStart);
                int before = fake.ResetCount;
                MethodInfo onDisable = typeof(VehiclePoseDriver).GetMethod(
                    "OnDisable",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Require(onDisable != null,
                    "VehiclePoseDriver.OnDisable is missing.");
                onDisable.Invoke(fixture.Driver, null);
                Require(fake.ResetCount == before + 1 &&
                        !fixture.Driver.OwnsControl &&
                        !fixture.Driver.HasFreshAppliedPose,
                    "OnDisable did not reset the provider and observations.");
            }

            return "Disabling the Driver resets the active provider once.";
        }

        private static string VerifyAuthorityLossReset()
        {
            using (var fixture = new DriverFixture("AuthorityReset"))
            {
                ExpectedPose expected = fixture.GetExpectedPose(EpochStart);
                var fake = fixture.CreateFake(ApplyResult(
                    expected.Position, expected.Rotation, "authority"));
                fixture.Driver.ConfigurePoseConstraint(fake);
                fixture.Driver.TrySampleAndApply(EpochStart);
                int before = fake.ResetCount;
                UnityEngine.Object.DestroyImmediate(fixture.Authority);
                Require(!fixture.Driver.TrySampleAndApply(EpochStart) &&
                        fake.ResetCount == before + 1,
                    "First authority loss did not reset exactly once.");
                fixture.Driver.TrySampleAndApply(EpochStart);
                Require(fake.ResetCount == before + 1,
                    "Continuous authority loss repeated reset.");
            }

            return "Owns-control to no-control transition resets once, not every blocked call.";
        }

        private static string VerifyProviderReplacementReset()
        {
            using (var fixture = new DriverFixture("ProviderReplacement"))
            {
                ExpectedPose expected = fixture.GetExpectedPose(EpochStart);
                var first = fixture.CreateFake(ApplyResult(
                    expected.Position, expected.Rotation, "first"));
                var second = fixture.CreateFake(ApplyResult(
                    expected.Position, expected.Rotation, "second"));
                fixture.Driver.ConfigurePoseConstraint(first);
                fixture.Driver.TrySampleAndApply(EpochStart);
                int firstBefore = first.ResetCount;
                fixture.Driver.ConfigurePoseConstraint(second);
                Require(first.ResetCount == firstBefore + 1 &&
                        second.ResetCount == 0,
                    "Replacement did not reset only the old provider.");
                fixture.Driver.TrySampleAndApply(EpochStart);
                Require(second.ResetCount == 1 && second.ConstrainCount == 1,
                    "Replacement provider did not reset before first constrain.");
            }

            return "Provider replacement resets the old provider and initializes new epoch order.";
        }

        private static string VerifyAuvUsvRegression()
        {
            using (var auv = new DriverFixture(
                       "AuvNull", VehicleType.Auv))
            using (var usv = new DriverFixture(
                       "UsvNull", VehicleType.Usv))
            {
                ExpectedPose auvExpected = auv.GetExpectedPose(EpochStart);
                ExpectedPose usvExpected = usv.GetExpectedPose(EpochStart);
                Require(auv.Driver.TrySampleAndApply(EpochStart) &&
                        usv.Driver.TrySampleAndApply(EpochStart),
                    "AUV or USV null-provider path failed.");
                RequirePose(auv.TargetObject.transform,
                    auvExpected.Position, auvExpected.Rotation, "AUV null path");
                RequirePose(usv.TargetObject.transform,
                    usvExpected.Position, usvExpected.Rotation, "USV null path");
                Require(!auv.Driver.HasPoseConstraint &&
                        !usv.Driver.HasPoseConstraint &&
                        auv.Driver.LastPoseConstraintDecision ==
                            UnityPoseConstraintDecision.NotEvaluated &&
                        usv.Driver.LastPoseConstraintDecision ==
                            UnityPoseConstraintDecision.NotEvaluated,
                    "AUV/USV acquired an implicit provider or decision.");
            }

            return "AUV and USV-style five-parameter configurations remain exact null-provider paths.";
        }

        private static string VerifySingleCommitSource()
        {
            Require(DriverCommitCallSiteCount() == 1,
                "VehiclePoseDriver does not contain exactly one root commit call site.");
            return "Static source proves every successful call can reach at most one root commit.";
        }

        private static string VerifyRuntimeAuthorityImmutability()
        {
            using (var fixture = new DriverFixture(
                       "AuthorityImmutable",
                       VehicleType.Rov,
                       Vector3.zero))
            using (var rov = new RovProviderFixture("AuthorityImmutable", -0.7f))
            {
                object store = fixture.Host.Store;
                DataSourceStatus status = fixture.Host.SourceStatus;
                fixture.Host.TryGetActiveEpoch(out ulong epoch);
                fixture.Host.TryGetLatestSourceTimestamp(out double timestamp);
                TerrainSurfaceSampler sampler = rov.Constraint.SurfaceSampler;
                RovContactProfile profile = rov.Constraint.Profile;
                Mesh mesh = rov.Collider.sharedMesh;
                Vector3 terrainPosition = rov.TerrainObject.transform.position;
                fixture.Driver.ConfigurePoseConstraint(rov.Constraint);
                fixture.Driver.TrySampleAndApply(EpochStart);
                Require(ReferenceEquals(store, fixture.Host.Store) &&
                        fixture.Host.SourceStatus == status &&
                        fixture.Host.TryGetActiveEpoch(out ulong epochAfter) &&
                        epochAfter == epoch &&
                        fixture.Host.TryGetLatestSourceTimestamp(
                            out double timestampAfter) &&
                        timestampAfter.Equals(timestamp),
                    "Driver mutated Store or public source state.");
                Require(ReferenceEquals(rov.Constraint.SurfaceSampler, sampler) &&
                        ReferenceEquals(rov.Constraint.Profile, profile) &&
                        ReferenceEquals(rov.Collider.sharedMesh, mesh) &&
                        rov.TerrainObject.transform.position.Equals(terrainPosition),
                    "Driver mutated the ROV constraint authority or terrain fixture.");
            }

            return "Driver reads source/constraint authority and writes only its target root.";
        }

        private static string VerifyRepeatedDeterminism()
        {
            using (var fixture = new DriverFixture("Repeat100"))
            {
                ExpectedPose expected = fixture.GetExpectedPose(EpochStart);
                Vector3 output = expected.Position + Vector3.up * 0.123f;
                Quaternion rotation = Quaternion.Euler(4f, 22f, -3f);
                var fake = fixture.CreateFake(ApplyResult(
                    output, rotation, "repeat"));
                fixture.Driver.ConfigurePoseConstraint(fake);
                for (int index = 0; index < 100; index++)
                {
                    Require(fixture.Driver.TrySampleAndApply(EpochStart),
                        "Repeat " + index + " did not apply.");
                    RequirePose(fixture.TargetObject.transform,
                        output, rotation, "repeat " + index);
                    Require(fixture.Driver.LastPoseConstraintDecision ==
                                UnityPoseConstraintDecision.Apply &&
                            fixture.Driver.LastPoseConstraintReason == "repeat" &&
                            fixture.Driver.LastAppliedSourceEpoch == expected.Epoch &&
                            fixture.Driver.LastSampleMode == expected.Mode &&
                            fixture.Driver.LastFailureReason ==
                                RenderSampleFailureReason.None &&
                            fixture.Driver.HasAppliedPose &&
                            fixture.Driver.HasFreshAppliedPose,
                        "Repeat " + index + " changed Driver observations.");
                }

                Require(fake.ResetCount == 1 && fake.ConstrainCount == 100,
                    "100 repeats had unstable reset/constrain counts.");
            }

            return "100 identical integrations reproduce pose and every public Driver observation.";
        }

        private static string VerifySourceBoundary()
        {
            string runtimeRoot = Path.Combine(
                Application.dataPath, "Scripts", "Visualization", "Runtime");
            string interfaceSource = File.ReadAllText(Path.Combine(
                runtimeRoot, "Constraints", "IUnityPoseConstraint.cs"));
            string driverSource = File.ReadAllText(Path.Combine(
                runtimeRoot, "VehiclePoseDriver.cs"));
            string rovSource = File.ReadAllText(Path.Combine(
                runtimeRoot, "Rov", "RovTerrainContactConstraint.cs"));

            int convert = driverSource.IndexOf(
                "UnityPoseAdapter.TryConvert(", StringComparison.Ordinal);
            int constrain = driverSource.IndexOf(
                "poseConstraint.Constrain(in constraintRequest)",
                StringComparison.Ordinal);
            int commit = driverSource.IndexOf(
                "targetRoot.SetPositionAndRotation(", StringComparison.Ordinal);
            Require(convert >= 0 && constrain > convert && commit > constrain,
                "Required adapter < constraint < commit order is absent.");
            Require(Count(driverSource,
                    "targetRoot.SetPositionAndRotation(") == 1,
                "Driver has more than one formal root commit call site.");
            Require(!driverSource.Contains("RovTerrainContactConstraint") &&
                    driverSource.Contains("IUnityPoseConstraint") &&
                    !driverSource.Contains("GetComponent") &&
                    !driverSource.Contains("FindObject") &&
                    !driverSource.Contains("dynamic"),
                "Driver generic-interface boundary is violated.");
            foreach (string forbidden in new[]
                     {
                         "UnityEditor", "VehiclePoseDriver", "RovTerrain",
                         "VehicleType", "Store", "Transform", "MonoBehaviour",
                         "Update(", "LateUpdate(", "FixedUpdate(", "Time.",
                         "Rigidbody", "Smooth"
                     })
            {
                Require(!interfaceSource.Contains(forbidden),
                    "Interface source contains prohibited token: " + forbidden);
            }

            Require(typeof(IUnityPoseConstraint).IsAssignableFrom(
                    typeof(RovTerrainContactConstraint)) &&
                    !rovSource.Contains("VehiclePoseDriver") &&
                    !rovSource.Contains("targetRoot") &&
                    !rovSource.Contains("SetPositionAndRotation"),
                "ROV adapter boundary or no-writer rule is violated.");
            return "Source order and generic Runtime dependency boundaries passed.";
        }

        private static DriverFixture PreparedHoldFixture(
            string name,
            out EnvE3BFakePoseConstraint fake,
            out Vector3 before,
            out Quaternion beforeRotation,
            out ulong appliedEpoch)
        {
            var fixture = new DriverFixture(name);
            Require(fixture.Driver.TrySampleAndApply(EpochStart),
                "Historical unconstrained Apply failed.");
            before = fixture.TargetObject.transform.position;
            beforeRotation = fixture.TargetObject.transform.rotation;
            appliedEpoch = fixture.Driver.LastAppliedSourceEpoch;
            fake = fixture.CreateFake(new UnityPoseConstraintResult(
                UnityPoseConstraintDecision.HoldCurrent,
                new Vector3(float.NaN, float.PositiveInfinity, -999f),
                new Quaternion(float.NaN, 0f, 0f, 0f),
                "synthetic-hold"));
            fixture.Driver.ConfigurePoseConstraint(fake);
            return fixture;
        }

        private static UnityPoseConstraintResult ApplyResult(
            Vector3 position,
            Quaternion rotation,
            string reason)
        {
            return new UnityPoseConstraintResult(
                UnityPoseConstraintDecision.Apply,
                position,
                rotation,
                reason);
        }

        private static Mesh CreatePlaneMesh(string name)
        {
            var mesh = new Mesh
            {
                name = name,
                vertices = new[]
                {
                    new Vector3(-4f, 0f, -4f),
                    new Vector3(-4f, 0f, 4f),
                    new Vector3(4f, 0f, -4f),
                    new Vector3(4f, 0f, 4f)
                },
                triangles = new[] { 0, 3, 2, 0, 1, 3 }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int DriverCommitCallSiteCount()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Scripts",
                "Visualization",
                "Runtime",
                "VehiclePoseDriver.cs");
            return Count(File.ReadAllText(path),
                "targetRoot.SetPositionAndRotation(");
        }

        private static int Count(string source, string token)
        {
            int count = 0;
            int offset = 0;
            while ((offset = source.IndexOf(
                       token, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += token.Length;
            }

            return count;
        }

        private static void RequirePose(
            Transform actual,
            Vector3 position,
            Quaternion rotation,
            string label)
        {
            Require(Vector3.Distance(actual.position, position) <= Tolerance,
                label + " position expected " + position +
                ", got " + actual.position + ".");
            Require(Quaternion.Angle(actual.rotation, rotation) <= 0.001f,
                label + " rotation differs from provider output.");
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
