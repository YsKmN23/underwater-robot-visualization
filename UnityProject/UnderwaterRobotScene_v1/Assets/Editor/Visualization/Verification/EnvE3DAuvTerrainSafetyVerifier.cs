using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Auv;
using UnderwaterRobotScene.Visualization.Runtime.Constraints;
using UnderwaterRobotScene.Visualization.Runtime.Rov;
using UnderwaterRobotScene.Visualization.Runtime.Terrain;
using UnderwaterRobotScene.Visualization.Transforms;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    public static class EnvE3DAuvTerrainSafetyVerifier
    {
        private const string ScenePath = "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string TemplatePath = "Assets/Editor/Visualization/Installation/UnderwaterRobotDemo_Canonical.unity";
        private const string ReportArgument = "-envE3DVerifierReportPath";
        private const float Epsilon = 0.0001f;
        private const float SafeWaterY = 100f;

        [Serializable]
        private sealed class VerificationReport
        {
            public string schema;
            public string status;
            public string unityVersion;
            public int caseCount;
            public int passedCaseCount;
            public string[] cases;
            public int routeSampleCalls;
            public bool sceneTemplateByteIdentical;
            public int gameObjectCount;
            public int componentCount;
            public int missingReferenceCount;
        }

        private sealed class FlatProvider :
            IAuthoritativeTerrainSurfaceProvider
        {
            public float SurfaceY;
            public float SlopeDegrees;
            public bool Miss;
            public TerrainAuthorityFailure Failure =
                TerrainAuthorityFailure.OutsideCoverage;
            public int Calls;

            public bool TryValidateAuthority(
                out TerrainAuthorityFailure failure)
            {
                failure = TerrainAuthorityFailure.None;
                return true;
            }

            public bool TrySampleAtXZ(
                float worldX,
                float worldZ,
                out TerrainAuthoritySample sample,
                out TerrainAuthorityFailure failureReason)
            {
                Calls++;
                if (Miss)
                {
                    sample = default;
                    failureReason = Failure;
                    return false;
                }
                sample = new TerrainAuthoritySample(
                    new Vector3(worldX, SurfaceY, worldZ),
                    Quaternion.AngleAxis(SlopeDegrees, Vector3.forward) *
                        Vector3.up,
                    SlopeDegrees,
                    0,
                    0,
                    0);
                failureReason = TerrainAuthorityFailure.None;
                return true;
            }
        }

        public static void RunBatch()
        {
            string reportPath = RequireExternalCreateNewPath();
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath, OpenSceneMode.Single);
            var passed = new List<string>();
            AuvTerrainClearanceProfile profile =
                AuvTerrainClearanceProfile.CreateApprovedDefault();

            Run(passed, "profile", () => VerifyProfile(profile));
            Run(passed, "probe_transform", () => VerifyProbeTransform(profile));
            Run(passed, "accept_correct_hold", () => VerifyDecisions(profile));
            Run(passed, "terrain_failures", () => VerifyFailures(profile));
            Run(passed, "water_envelope", () =>
                VerifyWaterEnvelope(scene, profile));
            Run(passed, "angle_envelope", () =>
                VerifyAngleEnvelope(scene, profile));
            FlatProvider routeProvider = RunRouteCases(
                passed, scene, profile);
            Run(passed, "ui_layout", VerifyUiLayouts);
            Run(passed, "scene_binding", () => VerifySceneBinding(scene));
            Run(passed, "installer_no_op", () =>
            {
                EnvE3DAuvTerrainInstallResult result =
                    EnvE3DAuvTerrainSafetySceneInstaller.Execute(
                        scene, EnvE3DAuvTerrainInstallMode.RequireNoOp);
                Require(result.Success && !result.Changed && !result.SceneSaved,
                    result.FailureStage + ": " + result.FailureMessage);
            });
            Run(passed, "scene_template_parity", () =>
                Require(File.ReadAllBytes(AbsolutePath(ScenePath))
                        .SequenceEqual(File.ReadAllBytes(AbsolutePath(TemplatePath))),
                    "Formal Scene and canonical template differ."));

            CountScene(scene, out int objects, out int components,
                out int missing);
            Require(missing == 0, "Formal Scene contains missing references.");
            const int expectedCases = 12;
            Require(passed.Count == expectedCases,
                "Unexpected E3D verification case count.");
            var report = new VerificationReport
            {
                schema = "ENV-E3D-AuvTerrainSafety-Verification-v1",
                status = "ENV_E3D_AUV_TERRAIN_SAFETY_STATIC_AND_LOGIC_PASS",
                unityVersion = Application.unityVersion,
                caseCount = expectedCases,
                passedCaseCount = passed.Count,
                cases = passed.ToArray(),
                routeSampleCalls = routeProvider.Calls,
                sceneTemplateByteIdentical = true,
                gameObjectCount = objects,
                componentCount = components,
                missingReferenceCount = missing
            };
            WriteCreateNew(reportPath,
                JsonUtility.ToJson(report, true) + Environment.NewLine);
            Debug.Log(report.status + " | " + passed.Count + "/" +
                expectedCases + " cases passed.");
        }

        private static void VerifyProfile(AuvTerrainClearanceProfile profile)
        {
            Require(profile.TryValidate(out string error), error);
            Require(profile.ProbeCount == 7,
                "Approved AUV profile must contain seven frozen probes.");
            Require(profile.HullEnvelopeCornerCount == 8,
                "Approved AUV profile must contain eight hull-envelope corners.");
            Require(Near(profile.MinimumHullClearanceMeters, 0.18f) &&
                    Near(profile.MinimumHullSubmergenceMeters, 0.18f) &&
                    Near(profile.MaximumUpwardCorrectionMeters, 0.50f) &&
                    Near(profile.MaximumSlopeDegrees, 12f) &&
                    Near(profile.SegmentValidationSpacingMeters, 0.50f) &&
                    Near(profile.MaximumClimbAngleDegrees, 45f) &&
                    Near(profile.MaximumDescentAngleDegrees, 45f),
                "Approved AUV safety scalars drifted.");
            float[] longitudinal = profile.LowerEnvelopeProbeOffsets
                .Take(5).Select(value => value.x).ToArray();
            for (int index = 1; index < longitudinal.Length; index++)
                Require(longitudinal[index] > longitudinal[index - 1],
                    "Longitudinal probes are not nose-to-tail ordered.");
        }

        private static void VerifyProbeTransform(
            AuvTerrainClearanceProfile profile)
        {
            Quaternion pitch = Quaternion.Euler(0f, 0f, -25f);
            var provider = new FlatProvider { SurfaceY = -20f };
            AuvTerrainClearanceResult result =
                AuvTerrainClearanceEvaluator.Evaluate(
                    new Vector3(3f, 4f, 5f), pitch, profile, provider,
                    SafeWaterY);
            Require(result.MayApply && result.Observations.Length == 7,
                "Pitched probe evaluation failed.");
            for (int index = 0; index < profile.ProbeCount; index++)
            {
                Vector3 expected = new Vector3(3f, 4f, 5f) +
                    pitch * profile.LowerEnvelopeProbeOffsets[index];
                Require(Vector3.Distance(expected,
                        result.Observations[index].ProjectedPoint) <= Epsilon,
                    "Local-to-world probe transform drifted.");
            }
            Require(Mathf.Abs(result.Observations[0].ProjectedPoint.y -
                    result.Observations[4].ProjectedPoint.y) > 1f,
                "Pitch does not distinguish the hull nose and tail.");
        }

        private static void VerifyDecisions(
            AuvTerrainClearanceProfile profile)
        {
            var provider = new FlatProvider { SurfaceY = 0f };
            AuvTerrainClearanceResult accepted =
                AuvTerrainClearanceEvaluator.Evaluate(
                    new Vector3(0f, 1f, 0f), Quaternion.identity,
                    profile, provider, SafeWaterY);
            Require(accepted.State == AuvTerrainClearanceState.Supported &&
                    accepted.OutputPosition == new Vector3(0f, 1f, 0f),
                "Safe pose was not accepted without downward adhesion.");

            Vector3 candidate = new Vector3(0f, 0.55f, 0f);
            Quaternion rotation = Quaternion.Euler(0f, 0f, 2f);
            AuvTerrainClearanceResult corrected =
                AuvTerrainClearanceEvaluator.Evaluate(
                    candidate, rotation, profile, provider, SafeWaterY);
            Require(corrected.State == AuvTerrainClearanceState.Corrected &&
                    corrected.OutputPosition.x == candidate.x &&
                    corrected.OutputPosition.z == candidate.z &&
                    corrected.OutputPosition.y > candidate.y &&
                    corrected.OutputRotation == rotation,
                "Small correction did not preserve XZ and rotation.");
            foreach (AuvTerrainProbeObservation observation in
                     corrected.Observations)
            {
                float correctedProbeY = observation.ProjectedPoint.y +
                    corrected.CorrectionMeters;
                Require(correctedProbeY +
                        profile.SamplingToleranceMeters >=
                        observation.Sample.WorldPoint.y +
                        profile.MinimumHullClearanceMeters,
                    "A corrected probe remains below minimum clearance.");
            }

            AuvTerrainClearanceResult held =
                AuvTerrainClearanceEvaluator.Evaluate(
                    new Vector3(0f, -0.1f, 0f), Quaternion.identity,
                    profile, provider, SafeWaterY);
            Require(held.State ==
                    AuvTerrainClearanceState.CorrectionRejected &&
                    !held.MayApply,
                "Above-maximum correction was not held.");
        }

        private static void VerifyFailures(
            AuvTerrainClearanceProfile profile)
        {
            var miss = new FlatProvider { Miss = true };
            Require(AuvTerrainClearanceEvaluator.Evaluate(
                    Vector3.up, Quaternion.identity, profile, miss,
                    SafeWaterY).State ==
                    AuvTerrainClearanceState.OutOfTerrainBounds,
                "Terrain miss was not held.");
            miss.Failure = TerrainAuthorityFailure.InvalidTriangle;
            Require(AuvTerrainClearanceEvaluator.Evaluate(
                    Vector3.up, Quaternion.identity, profile, miss,
                    SafeWaterY).State ==
                    AuvTerrainClearanceState.NoValidTerrainSample,
                "Invalid hit was not held.");
            miss.Failure = TerrainAuthorityFailure.MissingAuthority;
            Require(AuvTerrainClearanceEvaluator.Evaluate(
                    Vector3.up, Quaternion.identity, profile, miss,
                    SafeWaterY).State ==
                    AuvTerrainClearanceState.NoValidTerrainSample,
                "Missing terrain authority was not held.");
            miss.Failure = TerrainAuthorityFailure.InvalidAuthority;
            Require(AuvTerrainClearanceEvaluator.Evaluate(
                    Vector3.up, Quaternion.identity, profile, miss,
                    SafeWaterY).State ==
                    AuvTerrainClearanceState.NoValidTerrainSample,
                "Disabled or invalid terrain authority was not held.");
            var slope = new FlatProvider
                { SurfaceY = 0f, SlopeDegrees = 12.01f };
            Require(AuvTerrainClearanceEvaluator.Evaluate(
                    Vector3.up, Quaternion.identity, profile, slope,
                    SafeWaterY).State ==
                    AuvTerrainClearanceState.SlopeRejected,
                "Over-limit slope was not held.");
        }

        private static void VerifyWaterEnvelope(
            Scene scene,
            AuvTerrainClearanceProfile profile)
        {
            var deepTerrain = new FlatProvider { SurfaceY = -20f };
            float highestLocalY = profile.HullEnvelopeCornerOffsets
                .Max(value => value.y);
            float boundaryRootY = -profile.MinimumHullSubmergenceMeters -
                highestLocalY;

            AuvTerrainClearanceResult nominal =
                AuvTerrainClearanceEvaluator.Evaluate(
                    new Vector3(0f, boundaryRootY - 0.25f, 0f),
                    Quaternion.identity,
                    profile,
                    deepTerrain,
                    0f);
            Require(nominal.MayApply,
                "Nominal submerged hull was rejected.");

            AuvTerrainClearanceResult boundary =
                AuvTerrainClearanceEvaluator.Evaluate(
                    new Vector3(0f, boundaryRootY, 0f),
                    Quaternion.identity,
                    profile,
                    deepTerrain,
                    0f);
            Require(boundary.MayApply &&
                    Near(boundary.MaximumHullWorldY,
                        boundary.AllowedMaximumHullWorldY),
                "Exact hull-submergence boundary was rejected.");

            AuvTerrainClearanceResult toleranceEdge =
                AuvTerrainClearanceEvaluator.Evaluate(
                    new Vector3(
                        0f,
                        boundaryRootY + profile.SamplingToleranceMeters,
                        0f),
                    Quaternion.identity,
                    profile,
                    deepTerrain,
                    0f);
            Require(toleranceEdge.MayApply,
                "Existing positional-tolerance edge was rejected.");

            AuvTerrainClearanceResult overLimit =
                AuvTerrainClearanceEvaluator.Evaluate(
                    new Vector3(
                        0f,
                        boundaryRootY + profile.SamplingToleranceMeters +
                            0.0001f,
                        0f),
                    Quaternion.identity,
                    profile,
                    deepTerrain,
                    0f);
            Require(overLimit.State ==
                    AuvTerrainClearanceState.WaterSurfaceBreach &&
                    !overLimit.MayApply &&
                    overLimit.Reason.Contains("WaterSurfaceBreach"),
                "Just-over-limit surface breach was not rejected distinctly.");

            Vector3 pitchedRoot = new Vector3(0f, -1.1f, 0f);
            Require(pitchedRoot.y <= -profile.MinimumHullSubmergenceMeters,
                "Pitched corner-breach fixture root is not submerged.");
            AuvTerrainClearanceResult pitched =
                AuvTerrainClearanceEvaluator.Evaluate(
                    pitchedRoot,
                    Quaternion.Euler(0f, 0f, -30f),
                    profile,
                    deepTerrain,
                    0f);
            Require(pitched.State ==
                    AuvTerrainClearanceState.WaterSurfaceBreach,
                "Pitched root-safe corner breach was not rejected.");

            float lowestProbeY = profile.LowerEnvelopeProbeOffsets
                .Min(value => value.y);
            var correctingTerrain = new FlatProvider
            {
                SurfaceY = boundaryRootY + lowestProbeY + 0.10f -
                    profile.MinimumHullClearanceMeters
            };
            AuvTerrainClearanceResult correctedBreach =
                AuvTerrainClearanceEvaluator.Evaluate(
                    new Vector3(0f, boundaryRootY, 0f),
                    Quaternion.identity,
                    profile,
                    correctingTerrain,
                    0f);
            Require(correctedBreach.State ==
                    AuvTerrainClearanceState.WaterSurfaceBreach &&
                    correctedBreach.CorrectionMeters >
                        profile.SamplingToleranceMeters,
                "Terrain correction did not precede the surface-envelope check.");

            Require(AuvTerrainClearanceEvaluator.Evaluate(
                        Vector3.zero,
                        Quaternion.identity,
                        profile,
                        deepTerrain,
                        float.NaN).State ==
                    AuvTerrainClearanceState.InvalidWaterAuthority,
                "Invalid water authority did not fail closed.");

            AuvTerrainClearanceConstraint formalConstraint =
                scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<
                        AuvTerrainClearanceConstraint>(true))
                    .Single();
            float formalWaterY = formalConstraint.WaterSurface.position.y;
            var request = new UnityPoseConstraintRequest(
                new Vector3(0f, formalWaterY, 0f),
                Quaternion.identity,
                1UL);
            UnityPoseConstraintResult runtime =
                formalConstraint.Constrain(in request);
            Require(runtime.Decision ==
                    UnityPoseConstraintDecision.HoldCurrent &&
                    runtime.Reason.Contains("WaterSurfaceBreach") &&
                    runtime.Position.y >= request.Position.y,
                "Runtime surface breach did not HoldCurrent without downward correction.");
        }

        private static void VerifyAngleEnvelope(
            Scene scene,
            AuvTerrainClearanceProfile profile)
        {
            VehicleDataRuntimeHost host = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    VehicleDataRuntimeHost>(true))
                .Single(value => value.IntegrationConfiguration.VehicleType ==
                    VehicleType.Auv);
            Require(host.ProfileConfiguration.TryBuildProfile(
                    out CoordinateTransformProfile transformProfile,
                    out string profileError), profileError);
            var terrain = new FlatProvider { SurfaceY = -20f };

            RequireRouteDecision("shallow-climb",
                new Vector3d(0, 0, 0), new Vector3d(10, 1, 0),
                true, AuvTerrainClearanceState.Supported);
            RequireRouteDecision("shallow-descent",
                new Vector3d(0, 0, 0), new Vector3d(10, -1, 0),
                true, AuvTerrainClearanceState.Supported);
            RequireRouteDecision("exact-climb-45",
                new Vector3d(0, 0, 0), new Vector3d(1, 1, 0),
                true, AuvTerrainClearanceState.Supported);
            RequireRouteDecision("exact-descent-45",
                new Vector3d(0, 0, 0), new Vector3d(1, -1, 0),
                true, AuvTerrainClearanceState.Supported);
            RequireRouteDecision("over-climb-45",
                new Vector3d(0, 0, 0), new Vector3d(1, 1.001, 0),
                false, AuvTerrainClearanceState.ClimbAngleExceeded);
            RequireRouteDecision("over-descent-45",
                new Vector3d(0, 0, 0), new Vector3d(1, -1.001, 0),
                false, AuvTerrainClearanceState.DescentAngleExceeded);
            RequireRouteDecision("pure-vertical",
                new Vector3d(0, 0, 0), new Vector3d(0, 1, 0),
                false, AuvTerrainClearanceState.ClimbAngleExceeded);
            RequireRouteDecision("near-vertical",
                new Vector3d(0, 0, 0), new Vector3d(0.001, 2, 0),
                false, AuvTerrainClearanceState.ClimbAngleExceeded);
            RequireRouteDecision("long-horizontal-large-delta-y",
                new Vector3d(0, 0, 0), new Vector3d(10, 4, 0),
                true, AuvTerrainClearanceState.Supported);
            RequireRouteDecision("short-horizontal-same-delta-y",
                new Vector3d(0, 0, 0), new Vector3d(1, 4, 0),
                false, AuvTerrainClearanceState.ClimbAngleExceeded);
            RequireRouteDecision("running-replan-connection",
                new Vector3d(0, 0, 0), new Vector3d(1, 2, 0),
                false, AuvTerrainClearanceState.ClimbAngleExceeded);

            void RequireRouteDecision(
                string id,
                Vector3d start,
                Vector3d end,
                bool expectedAccepted,
                AuvTerrainClearanceState expectedFailure)
            {
                ActiveRouteSnapshot route = BuildRoute(id, start, end);
                bool accepted = AuvTerrainClearanceEvaluator.TryValidateRoute(
                    route,
                    in transformProfile,
                    profile,
                    terrain,
                    SafeWaterY,
                    out string error,
                    out RouteSafetyFailureDiagnostic diagnostic);
                Require(accepted == expectedAccepted,
                    id + " angle-policy decision drifted: " + error);
                if (!expectedAccepted)
                {
                    Require(diagnostic.HasFailure &&
                            diagnostic.TerrainState ==
                                expectedFailure.ToString(),
                        id + " angle-policy diagnostic drifted.");
                }
            }
        }

        private static FlatProvider RunRouteCases(
            ICollection<string> passed,
            Scene scene,
            AuvTerrainClearanceProfile profile)
        {
            VehicleDataRuntimeHost host = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    VehicleDataRuntimeHost>(true))
                .Single(value => value.IntegrationConfiguration.VehicleType ==
                    VehicleType.Auv);
            Require(host.ProfileConfiguration.TryBuildProfile(
                    out CoordinateTransformProfile transformProfile,
                    out string profileError), profileError);
            var safeProvider = new FlatProvider { SurfaceY = -2f };
            ActiveRouteSnapshot safe = BuildRoute(
                "safe", new Vector3d(0, 0, 0),
                new Vector3d(1, 0, 0));
            Run(passed, "route_sampling", () =>
            {
                Require(AuvTerrainClearanceEvaluator.TryValidateRoute(
                        safe, in transformProfile, profile, safeProvider,
                        SafeWaterY,
                        out string error), error);
                Require(safeProvider.Calls == 21,
                    "A one-metre segment must sample endpoints and midpoint with all seven probes.");
            });
            Run(passed, "unsafe_route_rejection", () =>
            {
                var unsafeProvider = new FlatProvider { SurfaceY = 0f };
                ActiveRouteSnapshot unsafeRoute = BuildRoute(
                    "unsafe", new Vector3d(0, -2, 0),
                    new Vector3d(1, -2, 0));
                    Require(!AuvTerrainClearanceEvaluator.TryValidateRoute(
                        unsafeRoute, in transformProfile, profile,
                        unsafeProvider, SafeWaterY, out string error) &&
                        error.Contains("segment"),
                    "Unsafe route was not rejected with segment feedback.");
            });
            return safeProvider;
        }

        private static ActiveRouteSnapshot BuildRoute(
            string id, Vector3d a, Vector3d b)
        {
            Require(ActiveRouteSnapshotBuilder.TryBuild(
                    "AUV", VehicleType.Auv, id, 10UL,
                    new[] { a, b },
                    VehicleRouteOrientationPolicy.AuvThreeDimensional,
                    0.0, out ActiveRouteSnapshot route,
                    out string error), error);
            return route;
        }

        private static void VerifyUiLayouts()
        {
            int[,] sizes = { { 1280, 720 }, { 1920, 1080 }, { 1366, 768 } };
            for (int index = 0; index < sizes.GetLength(0); index++)
            for (int depth = 0; depth < 2; depth++)
            foreach (float feedback in new[] { 32f, 120f, 420f })
            {
                RouteEditorPanelLayout layout =
                    RouteEditorPanelLayout.Calculate(
                        sizes[index, 0], sizes[index, 1], depth == 1,
                        feedback);
                Require(layout.PanelRect.xMin >= 0f &&
                        layout.PanelRect.yMin >= 0f &&
                        layout.PanelRect.xMax <= sizes[index, 0] &&
                        layout.PanelRect.yMax <= sizes[index, 1],
                    "Panel escaped the Game View.");
                Rect localPanel = new Rect(
                    0f, 0f, layout.PanelRect.width,
                    layout.PanelRect.height);
                Rect[] visible =
                {
                    layout.TitleRect, layout.StatusRect, layout.HoldRect,
                    layout.DraftRect,
                    layout.HelpRect, layout.ApplyRect, layout.DeleteRect,
                    layout.ClearRect, layout.CancelRect, layout.PauseRect,
                    layout.ResumeRect, layout.RestartRect,
                    layout.CompleteRect, layout.FeedbackRect,
                    layout.LastOutcomeRect
                };
                foreach (Rect rect in visible)
                    Require(Contains(localPanel, rect),
                        "A visible UI element escaped the authoritative panel.");
                if (depth == 1)
                    Require(Contains(localPanel, layout.DepthRect),
                        "AUV depth help escaped the panel.");
            }
        }

        private static void VerifySceneBinding(Scene scene)
        {
            VehiclePoseDriver[] drivers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    VehiclePoseDriver>(true)).ToArray();
            Require(drivers.Length == 3,
                "Formal Scene must contain exactly three Drivers.");
            VehiclePoseDriver auv = drivers.Single(value =>
                value.RuntimeHost.IntegrationConfiguration.VehicleType ==
                VehicleType.Auv);
            VehiclePoseDriver rov = drivers.Single(value =>
                value.RuntimeHost.IntegrationConfiguration.VehicleType ==
                VehicleType.Rov);
            VehiclePoseDriver usv = drivers.Single(value =>
                value.RuntimeHost.IntegrationConfiguration.VehicleType ==
                VehicleType.Usv);
            Require(auv.PoseConstraintProvider is
                    AuvTerrainClearanceConstraint &&
                    auv.GetComponents<AuvTerrainClearanceConstraint>()
                        .Length == 1 &&
                    auv.GetComponents<TerrainSurfaceSampler>().Length == 1,
                "AUV terrain safety binding is not exact and unique.");
            var constraint =
                (AuvTerrainClearanceConstraint)auv.PoseConstraintProvider;
            Require(constraint.WaterSurface != null &&
                    constraint.WaterSurface.name == "Water_Surface" &&
                    constraint.WaterSurface.gameObject.activeInHierarchy,
                "AUV Water_Surface authority binding is not exact and active.");
            Require(rov.PoseConstraintProvider is
                    RovTerrainContactConstraint,
                "ROV provider changed.");
            Require(usv.PoseConstraintProvider == null,
                "USV gained a terrain provider.");
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin - Epsilon &&
                inner.yMin >= outer.yMin - Epsilon &&
                inner.xMax <= outer.xMax + Epsilon &&
                inner.yMax <= outer.yMax + Epsilon;
        }

        private static void CountScene(
            Scene scene, out int objects, out int components,
            out int missing)
        {
            GameObject[] values = scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<Transform>(true))
                .Select(value => value.gameObject).Distinct().ToArray();
            objects = values.Length;
            components = 0;
            missing = 0;
            foreach (GameObject value in values)
            {
                Component[] current = value.GetComponents<Component>();
                components += current.Length;
                missing += current.Count(component => component == null);
            }
        }

        private static void Run(
            ICollection<string> passed, string name, Action body)
        {
            body();
            passed.Add(name + "=PASS");
        }

        private static bool Near(float a, float b)
        {
            return Mathf.Abs(a - b) <= Epsilon;
        }

        private static string RequireExternalCreateNewPath()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            string value = string.Empty;
            for (int index = 0; index + 1 < arguments.Length; index++)
            {
                if (arguments[index] == ReportArgument)
                    value = arguments[index + 1];
            }
            Require(!string.IsNullOrWhiteSpace(value),
                "Missing " + ReportArgument + ".");
            string path = Path.GetFullPath(value);
            Require(!path.StartsWith(ProjectRoot() +
                    Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase),
                "Report path must be outside the Unity project.");
            Require(!File.Exists(path),
                "Report path must be create-new.");
            return path;
        }

        private static string AbsolutePath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(ProjectRoot(), assetPath));
        }

        private static string ProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static void WriteCreateNew(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = new FileStream(
                       path, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None))
            using (var writer = new StreamWriter(
                       stream, new UTF8Encoding(false)))
                writer.Write(content);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
