using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.LocalTesting;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Auv;
using UnderwaterRobotScene.Visualization.Runtime.Constraints;
using UnderwaterRobotScene.Visualization.Runtime.Terrain;
using UnderwaterRobotScene.Visualization.Sampling;
using UnderwaterRobotScene.Visualization.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    public static class EnvE3DAuvManualRouteApplyFocusedVerifier
    {
        private const string MenuPath =
            "Underwater Robot Scene/Verification/AUV Manual Route Apply Focused";
        private const string ScenePath =
            "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string ReportArgument =
            "-envE3DManualRouteApplyFocusedReportPath";
        private const string PassMarker =
            "ENV_E3D_AUV_MANUAL_ROUTE_APPLY_FOCUSED_AUTOMATED_VERIFICATION_PASS";
        private const string FailMarker =
            "ENV_E3D_AUV_MANUAL_ROUTE_APPLY_FOCUSED_AUTOMATED_VERIFICATION_FAIL";
        private const float SyntheticSafeWaterY = 100f;

        [MenuItem(MenuPath)]
        private static void RunFromMenu()
        {
            string reportPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Temp",
                "env-e3d-auv-manual-route-apply-focused-report-" +
                DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + ".json");
            RunBatch(reportPath);
        }

        [Serializable]
        private sealed class Report
        {
            public string schema;
            public string status;
            public string unityVersion;
            public string[] cases;
            public string defaultRoute;
            public string invalidInitialRoute;
            public string connectionClassification;
            public string draftClassification;
            public bool noSourceSamplesOnInitialReject;
            public bool rovUsvProjectionRegressionCovered;
            public bool rovUsvPlayModeIsolationExecuted;
            public string index1Up050;
            public string index2Up050;
            public string index3Up050;
            public bool sceneDirtyGuardPassed;
            public string probeReach;
            public string pointerGesture;
        }

        private sealed class TemporaryFixtureSceneScope : IDisposable
        {
            private readonly Scene formalScene;
            private readonly bool formalSceneDirty;
            private bool disposed;

            public TemporaryFixtureSceneScope()
            {
                formalScene = SceneManager.GetActiveScene();
                Require(formalScene.IsValid() && formalScene.isLoaded &&
                        SceneManager.GetActiveScene() == formalScene &&
                        string.Equals(formalScene.path, ScenePath,
                            StringComparison.Ordinal) &&
                        !EditorSceneManager.IsPreviewScene(formalScene),
                    "The formal Scene is not the valid loaded Active Scene " +
                    "before fixture creation.");
                formalSceneDirty = formalScene.isDirty;

                Scene created = default;
                try
                {
                    created = EditorSceneManager.NewScene(
                        NewSceneSetup.EmptyScene,
                        NewSceneMode.Additive);
                    Scene = created;
                    Require(Scene.IsValid() && Scene.isLoaded &&
                            Scene != formalScene &&
                            !EditorSceneManager.IsPreviewScene(Scene),
                        "The temporary fixture is not an ordinary loaded " +
                        "Editor Scene.");
                    if (SceneManager.GetActiveScene() != formalScene)
                        Require(EditorSceneManager.SetActiveScene(formalScene),
                            "Could not restore the formal Scene as Active " +
                            "after fixture creation.");
                    RequireFormalSceneState("after fixture creation");
                }
                catch
                {
                    if (formalScene.IsValid() && formalScene.isLoaded &&
                        SceneManager.GetActiveScene() != formalScene)
                        EditorSceneManager.SetActiveScene(formalScene);
                    if (created.IsValid() && created.isLoaded)
                        EditorSceneManager.CloseScene(created, true);
                    throw;
                }
            }

            public Scene Scene { get; }

            public GameObject CreateGameObject(string name)
            {
                RequireFormalSceneState("before fixture object creation");
                Require(Scene.IsValid() && Scene.isLoaded,
                    "The fixture Scene was unavailable for object creation.");
                Require(EditorSceneManager.SetActiveScene(Scene),
                    "Could not activate the fixture Scene for object creation.");
                GameObject value = null;
                try
                {
                    value = new GameObject(name);
                    Require(value.scene == Scene,
                        "A verifier fixture object was not created in the " +
                        "fixture Scene.");
                }
                catch
                {
                    if (value != null)
                        UnityEngine.Object.DestroyImmediate(value);
                    throw;
                }
                finally
                {
                    Require(EditorSceneManager.SetActiveScene(formalScene),
                        "Could not restore the formal Scene after fixture " +
                        "object creation.");
                }
                RequireFormalSceneState("after fixture object creation");
                return value;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (formalScene.IsValid() && formalScene.isLoaded &&
                    SceneManager.GetActiveScene() != formalScene)
                    Require(EditorSceneManager.SetActiveScene(formalScene),
                        "Could not restore the formal Scene before fixture " +
                        "cleanup.");
                RequireFormalSceneState("before fixture cleanup");
                bool closed = !Scene.IsValid() || !Scene.isLoaded ||
                    EditorSceneManager.CloseScene(Scene, true);
                Require(closed,
                    "Could not close the temporary fixture Scene.");
                Require(!Scene.IsValid() || !Scene.isLoaded,
                    "The temporary fixture Scene remained loaded after cleanup.");
                RequireFormalSceneState("after fixture cleanup");
            }

            private void RequireFormalSceneState(string phase)
            {
                Require(formalScene.IsValid() && formalScene.isLoaded,
                    "The formal Scene became invalid or unloaded " + phase + ".");
                Require(SceneManager.GetActiveScene() == formalScene,
                    "The formal Scene was not Active " + phase + ".");
                Require(string.Equals(formalScene.path, ScenePath,
                        StringComparison.Ordinal) &&
                        !EditorSceneManager.IsPreviewScene(formalScene),
                    "The formal Scene identity changed " + phase + ".");
                Require(formalScene.isDirty == formalSceneDirty,
                    "The formal Scene dirty state changed " + phase + ".");
            }
        }

        private sealed class TerrainFixture : IDisposable
        {
            private readonly TemporaryFixtureSceneScope fixtureScene =
                new TemporaryFixtureSceneScope();

            public TerrainFixture(string name, float surfaceY)
            {
                try
                {
                    Mesh = CreatePlaneMesh(name + "_Mesh", 10f);
                    TerrainObject = fixtureScene.CreateGameObject(name + "_Terrain");
                    TerrainObject.transform.position =
                        new Vector3(0f, surfaceY, 0f);
                    MeshFilter filter =
                        TerrainObject.AddComponent<MeshFilter>();
                    filter.sharedMesh = Mesh;
                    Collider = TerrainObject.AddComponent<MeshCollider>();
                    Collider.sharedMesh = Mesh;
                    SamplerObject = fixtureScene.CreateGameObject(name + "_Sampler");
                    Sampler = SamplerObject.AddComponent<TerrainSurfaceSampler>();
                    Sampler.Configure(Collider);
                    Physics.SyncTransforms();
                }
                catch
                {
                    if (Mesh != null)
                        UnityEngine.Object.DestroyImmediate(Mesh);
                    fixtureScene.Dispose();
                    throw;
                }
            }

            public Mesh Mesh { get; }
            public GameObject TerrainObject { get; }
            public MeshCollider Collider { get; }
            public GameObject SamplerObject { get; }
            public TerrainSurfaceSampler Sampler { get; }

            public GameObject CreateGameObject(string name)
            {
                return fixtureScene.CreateGameObject(name);
            }

            public void Dispose()
            {
                try
                {
                    UnityEngine.Object.DestroyImmediate(SamplerObject);
                    UnityEngine.Object.DestroyImmediate(TerrainObject);
                    UnityEngine.Object.DestroyImmediate(Mesh);
                }
                finally
                {
                    fixtureScene.Dispose();
                }
            }
        }

        private sealed class MissProvider :
            IAuthoritativeTerrainSurfaceProvider
        {
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
                sample = default;
                failureReason = TerrainAuthorityFailure.OutsideCoverage;
                return false;
            }
        }

        private sealed class RejectingInitialValidator : MonoBehaviour,
            IUnityPoseConstraint,
            IRouteSafetyValidator
        {
            public UnityPoseConstraintResult Constrain(
                in UnityPoseConstraintRequest request)
            {
                return new UnityPoseConstraintResult(
                    UnityPoseConstraintDecision.Apply,
                    request.Position,
                    request.Rotation,
                    string.Empty);
            }

            public bool TryValidateRoute(
                ActiveRouteSnapshot candidate,
                in CoordinateTransformProfile transformProfile,
                out string error)
            {
                error = "Fixture unsafe initial route.";
                return false;
            }

            public void ResetObservation()
            {
            }
        }

        public static void RunBatch()
        {
            string reportPath = RequireExternalCreateNewPath();
            RunBatch(reportPath);
        }

        public static void RunHeadlessBatch()
        {
            try
            {
                string reportPath = RequireExternalCreateNewPath();
                Require(Application.isBatchMode,
                    "The focused headless bootstrap requires batch mode.");
                Require(!Application.isPlaying &&
                        !EditorApplication.isPlaying &&
                        !EditorApplication.isPlayingOrWillChangePlaymode,
                    "The focused headless bootstrap requires stable Edit Mode.");

                for (int index = 0; index < SceneManager.sceneCount; index++)
                {
                    Scene loaded = SceneManager.GetSceneAt(index);
                    Require(!loaded.IsValid() || !loaded.isLoaded ||
                            !loaded.isDirty,
                        "A pre-existing loaded Scene is dirty: " +
                        loaded.path);
                }

                Scene opened = EditorSceneManager.OpenScene(
                    ScenePath, OpenSceneMode.Single);
                Scene active = SceneManager.GetActiveScene();
                Require(opened.IsValid() && opened.isLoaded &&
                        opened == active &&
                        string.Equals(active.path, ScenePath,
                            StringComparison.Ordinal) &&
                        !EditorSceneManager.IsPreviewScene(active) &&
                        !active.isDirty &&
                        SceneManager.sceneCount == 1,
                    "The focused headless bootstrap did not open the clean " +
                    "formal Scene as the only loaded active Scene.");

                RunBatchCore(reportPath);
                Require(active.IsValid() && active.isLoaded &&
                        SceneManager.GetActiveScene() == active &&
                        string.Equals(active.path, ScenePath,
                            StringComparison.Ordinal) &&
                        !active.isDirty,
                    "Focused headless verification left the formal Scene dirty " +
                    "or changed its active identity.");
            }
            catch (Exception exception)
            {
                Debug.LogError(FailMarker + " | " + exception.Message);
                throw;
            }
        }

        private static void RunBatch(string reportPath)
        {
            try
            {
                RunBatchCore(reportPath);
            }
            catch (Exception exception)
            {
                Debug.LogError(FailMarker + " | " + exception.Message);
                throw;
            }
        }

        private static void RunBatchCore(string reportPath)
        {
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded &&
                    string.Equals(scene.path, ScenePath, StringComparison.Ordinal) &&
                    !EditorSceneManager.IsPreviewScene(scene),
                "Open UnderwaterRobotDemo as the active Scene before running this verifier.");
            bool initialSceneDirty = scene.isDirty;
            var passed = new List<string>();
            try
            {
                Run(passed, "default_active_v1", () =>
                    VerifyDefaultRoute(scene));
                Run(passed, "approved_probe_profile_and_scene_binding", () =>
                    VerifyApprovedProbeProfile(scene));
                Run(passed, "exact_4_056071m_ray_boundary",
                    VerifyExactRayBoundary);
                Run(passed, "formal_route_probe_reach_cases", () =>
                    VerifyFormalRouteProbeReachCases(scene));
                Run(passed, "terrain_fail_closed_and_clearance_decisions",
                    VerifyTerrainFailureDecisions);
                Run(passed, "anchored_pointer_gesture_contract",
                    VerifyPointerGestureContract);
                Run(passed, "three_vehicle_route_plane_projection_and_panel_contracts",
                    VerifyProjectionAndPanelContracts);
                Run(passed, "invalid_initial_route_fail_closed",
                    VerifyInvalidInitialRouteFailClosed);
                Run(passed, "structured_connection_and_draft_diagnostics",
                    VerifyStructuredDiagnostics);
                Run(passed, "diagnostic_isolation_after_draft_failure",
                    VerifyDiagnosticIsolationAfterDraftFailure);
                Run(passed, "ordinary_host_error_without_terrain_diagnostic",
                    VerifyOrdinaryHostErrorWithoutTerrainDiagnostic);
                Run(passed, "three_attempt_diagnostic_isolation",
                    VerifyThreeAttemptIsolation);
                Run(passed, "ui_layout_and_persistent_outcome_surface",
                    VerifyUiSurface);
                RequireSceneDirtyUnchanged(scene, initialSceneDirty);

                var report = new Report
                {
                    schema = "ENV-E3D-AuvManualRouteApply-Focused-v1",
                    status = PassMarker,
                    unityVersion = Application.unityVersion,
                    cases = passed.ToArray(),
                    defaultRoute = "formal Scene MeshCollider/profile/7 probes/0.5m spacing PASS",
                    invalidInitialRoute = "validator rejection before source.Start PASS",
                    connectionClassification = "Connection segment PASS",
                    draftClassification = "Draft segment N PASS",
                    noSourceSamplesOnInitialReject = true,
                    rovUsvProjectionRegressionCovered = true,
                    rovUsvPlayModeIsolationExecuted = false,
                    index1Up050 = "accepted",
                    index2Up050 = "accepted-or-legitimate-safety-rejection",
                    index3Up050 = "accepted-or-legitimate-safety-rejection",
                    sceneDirtyGuardPassed = true,
                    probeReach = "5.0m total / 3.0m effective downward reach",
                    pointerGesture = "Idle/PendingDrag/Dragging; 6px; anchored"
                };
                WriteCreateNew(reportPath,
                    JsonUtility.ToJson(report, true) + Environment.NewLine);
                Debug.Log(report.status + " | " + passed.Count + " cases passed.");
            }
            finally
            {
                RequireSceneDirtyUnchanged(scene, initialSceneDirty);
            }
        }

        private static void VerifyDefaultRoute(Scene scene)
        {
            VehicleDataRuntimeHost host = FindHost(scene, VehicleType.Auv);
            VehiclePoseDriver driver = FindDriver(scene, host);
            Require(driver.PoseConstraintProvider is
                    AuvTerrainClearanceConstraint,
                "Formal Scene AUV Driver is missing its terrain validator.");
            var constraint = (AuvTerrainClearanceConstraint)
                driver.PoseConstraintProvider;
            Require(host.ProfileConfiguration.TryBuildProfile(
                    out CoordinateTransformProfile profile,
                    out string profileError), profileError);
            ActiveRouteSnapshot route = BuildConfiguredRoute(host);
            Require(constraint.TryValidateRoute(route, in profile,
                    out string routeError), routeError);
            Require(!constraint.LastRouteSafetyFailure.HasFailure,
                "Default route retained a terrain failure diagnostic.");
        }

        private static void VerifyApprovedProbeProfile(Scene scene)
        {
            VehicleDataRuntimeHost host = FindHost(scene, VehicleType.Auv);
            var constraint = FindDriver(scene, host).PoseConstraintProvider as
                AuvTerrainClearanceConstraint;
            Require(constraint != null,
                "Formal Scene AUV terrain constraint is missing.");
            AuvTerrainClearanceProfile profile = constraint.Profile;
            Require(profile != null,
                "Formal Scene AUV profile is missing.");
            Require(profile.TryValidate(out string error), error);
            Require(profile.ProbeDistanceMeters.Equals(5.0f) &&
                    profile.ProbeStartHeightMeters.Equals(2.0f) &&
                    (profile.ProbeDistanceMeters -
                     profile.ProbeStartHeightMeters).Equals(3.0f),
                "Approved 5m probe reach was not loaded into the formal profile.");
            Require(profile.ProbeCount == 7 &&
                    profile.MinimumHullClearanceMeters.Equals(0.18f) &&
                    profile.MaximumUpwardCorrectionMeters.Equals(0.50f) &&
                    profile.MaximumSlopeDegrees.Equals(12.0f) &&
                    profile.SamplingToleranceMeters.Equals(0.002f) &&
                    profile.SegmentValidationSpacingMeters.Equals(0.50f),
                "A frozen AUV terrain safety threshold changed.");
            MeshCollider terrain = constraint.SurfaceSampler.ContactTerrain;
            Require(terrain != null &&
                    (profile.TerrainLayerMask.value &
                     (1 << terrain.gameObject.layer)) != 0,
                "The default AUV mask excludes the bound formal Seabed layer.");

            var zeroMask = new AuvTerrainClearanceProfile(
                profile.LowerEnvelopeProbeOffsets.ToArray(),
                profile.MinimumHullClearanceMeters,
                profile.MaximumUpwardCorrectionMeters,
                0,
                profile.ProbeStartHeightMeters,
                profile.ProbeDistanceMeters,
                profile.MaximumSlopeDegrees,
                profile.SamplingToleranceMeters,
                profile.SegmentValidationSpacingMeters);
            Require(!zeroMask.TryValidate(out _),
                "A zero terrain LayerMask passed profile validation.");
        }

        private static void VerifyExactRayBoundary()
        {
            const float hitDistance = 4.056071f;
            using (var fixture = new TerrainFixture(
                       "Focused_ProbeBoundary", -hitDistance))
            {
                Require(!fixture.Sampler.TrySample(
                        Vector3.zero, 0f, 4.0f,
                        out _,
                        out TerrainSurfaceSampleFailureReason oldFailure) &&
                        oldFailure == TerrainSurfaceSampleFailureReason.NoHit,
                    "The 4m request did not reproduce the exact NoHit boundary.");
                Require(fixture.Sampler.TrySample(
                        Vector3.zero, 0f, 5.0f,
                        out TerrainSurfaceSample sample,
                        out TerrainSurfaceSampleFailureReason newFailure) &&
                        newFailure == TerrainSurfaceSampleFailureReason.None &&
                        Mathf.Abs(sample.Distance - hitDistance) <= 0.0001f,
                    "The 5m request did not hit the explicit boundary MeshCollider.");
                Require(fixture.Sampler.TrySampleAtXZ(
                        0f,
                        0f,
                        out TerrainAuthoritySample authority,
                        out TerrainAuthorityFailure authorityFailure) &&
                        authorityFailure == TerrainAuthorityFailure.None &&
                        Mathf.Abs(authority.WorldPoint.y + hitDistance) <=
                            0.0001f,
                    "Authoritative X/Z lookup did not resolve the same 4.056071m terrain surface.");
            }
        }

        private static void VerifyFormalRouteProbeReachCases(Scene scene)
        {
            VehicleDataRuntimeHost host = FindHost(scene, VehicleType.Auv);
            var constraint = (AuvTerrainClearanceConstraint)
                FindDriver(scene, host).PoseConstraintProvider;
            Require(host.ProfileConfiguration.TryBuildProfile(
                    out CoordinateTransformProfile profile,
                    out string profileError), profileError);

            RequireRouteAccepted(constraint, in profile,
                BuildConfiguredRoute(host), "default route");
            RequireRouteAccepted(constraint, in profile,
                BuildConfiguredRoute(host, "index1-up-025", points =>
                    points[1] = OffsetY(points[1], 0.25)),
                "index 1 +0.25m");
            RequireRouteAccepted(constraint, in profile,
                BuildConfiguredRoute(host, "index1-up-050", points =>
                    points[1] = OffsetY(points[1], 0.50)),
                "index 1 +0.50m");
            RequireRouteHasValidTerrainDecision(constraint, in profile,
                BuildConfiguredRoute(host, "index2-up-050", points =>
                    points[2] = OffsetY(points[2], 0.50)),
                "index 2 +0.50m");
            RequireRouteHasValidTerrainDecision(constraint, in profile,
                BuildConfiguredRoute(host, "index3-up-050", points =>
                    points[3] = OffsetY(points[3], 0.50)),
                "index 3 +0.50m");
            RequireRouteAccepted(constraint, in profile,
                BuildConfiguredRoute(host, "uniform-down-125", points =>
                {
                    for (int index = 0; index < points.Count; index++)
                        points[index] = OffsetY(points[index], -1.25);
                }),
                "uniform -1.25m");
        }

        private static void VerifyTerrainFailureDecisions()
        {
            AuvTerrainClearanceProfile profile =
                AuvTerrainClearanceProfile.CreateApprovedDefault();
            using (var fixtureScene = new TemporaryFixtureSceneScope())
            {
                GameObject missingSamplerObject = fixtureScene.CreateGameObject(
                    "Focused_MissingTerrain_Sampler");
                try
                {
                    var missingSampler = missingSamplerObject.AddComponent<
                        TerrainSurfaceSampler>();
                    Require(AuvTerrainClearanceEvaluator.Evaluate(
                            Vector3.up, Quaternion.identity, profile,
                            missingSampler, SyntheticSafeWaterY).State ==
                            AuvTerrainClearanceState.NoValidTerrainSample,
                        "Missing terrain did not fail closed.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(missingSamplerObject);
                }
            }

            using (var fixture = new TerrainFixture(
                       "Focused_FailClosed", 0f))
            {
                AuvTerrainClearanceResult outside =
                    AuvTerrainClearanceEvaluator.Evaluate(
                        new Vector3(100f, 1f, 100f),
                        Quaternion.identity, profile, fixture.Sampler,
                        SyntheticSafeWaterY);
                Require(outside.State ==
                        AuvTerrainClearanceState.OutOfTerrainBounds,
                    "Outside Mesh coverage did not remain fail-closed.");

                fixture.Collider.enabled = false;
                Require(AuvTerrainClearanceEvaluator.Evaluate(
                        Vector3.up, Quaternion.identity, profile,
                        fixture.Sampler, SyntheticSafeWaterY).State ==
                        AuvTerrainClearanceState.NoValidTerrainSample,
                    "Disabled terrain did not fail closed.");
                fixture.Collider.enabled = true;
                fixture.Collider.sharedMesh = null;
                Require(AuvTerrainClearanceEvaluator.Evaluate(
                        Vector3.up, Quaternion.identity, profile,
                        fixture.Sampler, SyntheticSafeWaterY).State ==
                        AuvTerrainClearanceState.NoValidTerrainSample,
                    "Missing terrain Mesh did not fail closed.");
                fixture.Collider.sharedMesh = fixture.Mesh;
                Physics.SyncTransforms();

                AuvTerrainClearanceResult corrected =
                    AuvTerrainClearanceEvaluator.Evaluate(
                        new Vector3(0f, 0.4f, 0f),
                        Quaternion.identity, profile, fixture.Sampler,
                        SyntheticSafeWaterY);
                Require(corrected.State == AuvTerrainClearanceState.Corrected &&
                        corrected.CorrectionMeters > 0f,
                    "A correctable clearance violation was not corrected.");
                Require(AuvTerrainClearanceEvaluator.Evaluate(
                        new Vector3(0f, -0.1f, 0f),
                        Quaternion.identity, profile, fixture.Sampler,
                        SyntheticSafeWaterY).State ==
                        AuvTerrainClearanceState.CorrectionRejected,
                    "An over-cap clearance violation was not rejected.");

                GameObject water = fixture.CreateGameObject(
                    "Focused_InvalidWaterAuthority");
                try
                {
                    var constraint = fixture.SamplerObject.AddComponent<
                        AuvTerrainClearanceConstraint>();
                    constraint.Configure(fixture.Sampler, profile,
                        water.transform);
                    water.SetActive(false);
                    Require(!constraint.TryValidate(out string inactiveError) &&
                            inactiveError.Contains("active"),
                        "Inactive water authority did not fail validation.");
                    var request = new UnityPoseConstraintRequest(
                        Vector3.up, Quaternion.identity, 1UL);
                    Require(constraint.Constrain(in request).Decision ==
                            UnityPoseConstraintDecision.HoldCurrent,
                        "Inactive water authority did not HoldCurrent.");

                    UnityEngine.Object.DestroyImmediate(water);
                    water = null;
                    Require(!constraint.TryValidate(out string missingError) &&
                            missingError.Contains("Water_Surface"),
                        "Missing water authority did not fail validation.");
                    Require(constraint.Constrain(in request).Decision ==
                            UnityPoseConstraintDecision.HoldCurrent,
                        "Missing water authority did not HoldCurrent.");
                }
                finally
                {
                    if (water != null)
                        UnityEngine.Object.DestroyImmediate(water);
                }
            }
        }

        private static void VerifyPointerGestureContract()
        {
            var gesture = new RoutePointerGesture();
            var down = new Vector2(100f, 100f);
            var original = new Vector3(10f, 20f, 30f);
            var projectedDown = new Vector3(9f, 3f, 28f);
            Vector3 unchanged = original;
            gesture.Begin(down, original, true, projectedDown);
            Require(gesture.State == RoutePointerGestureState.PendingDrag &&
                    gesture.HasPointerAnchor &&
                    !gesture.TryGetDragTarget(
                        down, true, projectedDown, true, out _) &&
                    unchanged.Equals(original),
                "Pointer down moved the selected waypoint.");
            for (int pixels = 1; pixels <= 5; pixels++)
                Require(!gesture.TryGetDragTarget(
                        down + Vector2.right * pixels,
                        true,
                        projectedDown + new Vector3(pixels, 0f, pixels),
                        true,
                        out _) && unchanged.Equals(original),
                    pixels + "px jitter moved the selected waypoint.");

            Require(gesture.TryGetDragTarget(
                    down + Vector2.right * 6f,
                    true,
                    projectedDown,
                    true,
                    out Vector3 anchored) &&
                    gesture.State == RoutePointerGestureState.Dragging &&
                    anchored.Equals(original),
                "The 6px threshold or pointer anchor contract failed.");
            Require(gesture.TryGetDragTarget(
                    down + new Vector2(8f, 2f),
                    true,
                    projectedDown + new Vector3(2f, 99f, 3f),
                    true,
                    out Vector3 moved) &&
                    moved.x.Equals(original.x + 2f) &&
                    moved.y.Equals(original.y) &&
                    moved.z.Equals(original.z + 3f),
                "Anchored AUV drag did not preserve the original Y value.");

            gesture.Reset();
            Require(gesture.State == RoutePointerGestureState.Idle &&
                    !gesture.HasPointerAnchor &&
                    !gesture.TryGetDragTarget(
                        down + Vector2.right * 20f,
                        true, projectedDown, true, out _),
                "Gesture reset did not clear pending/dragging state.");
            gesture.Begin(down, original, false, default);
            Require(!gesture.TryGetDragTarget(
                    down + Vector2.right * 6f,
                    true, projectedDown, true, out _),
                "A drag without a pointer-down anchor moved the waypoint.");
            gesture.Reset();

            var originalDepth = new Vector3d(1.25, -2.5, 3.75);
            Vector3d raised = VehicleRouteEditingController
                .AdjustAuvWaypointDepth(originalDepth, 0.25);
            Vector3d lowered = VehicleRouteEditingController
                .AdjustAuvWaypointDepth(originalDepth, -0.25);
            Require(raised.X.Equals(originalDepth.X) &&
                    raised.Y.Equals(originalDepth.Y + 0.25) &&
                    raised.Z.Equals(originalDepth.Z) &&
                    lowered.X.Equals(originalDepth.X) &&
                    lowered.Y.Equals(originalDepth.Y - 0.25) &&
                    lowered.Z.Equals(originalDepth.Z),
                "PageUp/PageDown depth adjustment changed X or Z.");
        }

        private static void VerifyProjectionAndPanelContracts()
        {
            var ray = new Ray(
                new Vector3(1f, 10f, 3f),
                new Vector3(0.25f, -1f, 0.15f).normalized);
            Require(VehicleRouteProjection.TryProjectRouteEditorPointer(
                    VehicleSelectionKind.Rov, ray, -4f, out Vector3 rov) &&
                    Near(rov.y, -4f),
                "ROV route-height-plane projection failed.");
            Require(VehicleRouteProjection.TryProjectRouteEditorPointer(
                    VehicleSelectionKind.Auv, ray, -2f, out Vector3 auv) &&
                    Near(auv.y, -2f),
                "AUV route-height-plane projection contract changed.");
            Require(VehicleRouteProjection.TryProjectRouteEditorPointer(
                    VehicleSelectionKind.Usv, ray, 2f, out Vector3 usv) &&
                    Near(usv.y, 2f),
                "USV route-height-plane projection contract changed.");
            Require((rov - ray.GetPoint(
                        (-4f - ray.origin.y) / ray.direction.y)).sqrMagnitude <=
                    0.00000001f,
                "ROV route-editor projection left the original pointer ray.");

            RouteEditorPanelLayout layout = RouteEditorPanelLayout.Calculate(
                1280, 720, true, 120f, 120f);
            Vector2 panelScreenPoint = new Vector2(
                layout.PanelRect.center.x,
                720f - layout.PanelRect.center.y);
            Require(VehicleRouteEditingController.IsScreenPointOverPanel(
                    panelScreenPoint, 720f, layout.PanelRect) &&
                    !VehicleRouteEditingController.IsScreenPointOverPanel(
                        new Vector2(1279f, 0f), 720f, layout.PanelRect),
                "Panel pointer blocking did not preserve GUI coordinates.");
        }

        private static void VerifyInvalidInitialRouteFailClosed()
        {
            using (var fixtureScene = new TemporaryFixtureSceneScope())
            {
                GameObject hostObject = fixtureScene.CreateGameObject(
                    "Focused_InvalidInitial_Host");
                GameObject driverObject = fixtureScene.CreateGameObject(
                    "Focused_InvalidInitial_Driver");
                GameObject root = fixtureScene.CreateGameObject(
                    "Focused_InvalidInitial_Root");
                try
                {
                    var configuration = hostObject.AddComponent<
                        VehiclePoseIntegrationConfiguration>();
                    configuration.ConfigureLocalTest(
                        "focused-invalid", "AUV-INVALID", VehicleType.Auv,
                        DeterministicVehicleStateGeneratorKind.Default,
                        new Vector3(-1.85f, -1.35f, -1.65f),
                        0.1f, 64, 0.75f, 8, false,
                        0f, 0.25f, 0.25f, 0.000001f,
                        AfterLatestBehavior.HoldLatest, true);
                    var profile = driverObject.AddComponent<
                        VehiclePoseProfileConfiguration>();
                    profile.Configure(
                        "FOCUSED_INVALID_UNITY", CoordinateProfilePreset.UnityNative,
                        1f, AttitudeDirection.BodyToWorld,
                        SignedSemanticAxis.NegativeZ,
                        SignedSemanticAxis.PositiveY,
                        SignedSemanticAxis.PositiveX,
                        new Vector3(0f, -90f, 0f));
                    var authority = root.AddComponent<VehiclePoseControlAuthority>();
                    authority.Mode = VehiclePoseControlMode.PublicData;
                    var host = hostObject.AddComponent<VehicleDataRuntimeHost>();
                    host.Configure(configuration, profile);
                    host.ConfigureSourceMode(VehicleRuntimeSourceMode.RouteFollowing);
                    var driver = driverObject.AddComponent<VehiclePoseDriver>();
                    var validator = driverObject.AddComponent<RejectingInitialValidator>();
                    driver.Configure(host, configuration, profile, authority,
                        root.transform, validator);

                    bool rejected = false;
                    try
                    {
                        host.InitializeForDiagnostics(1.0);
                    }
                    catch (InvalidOperationException exception)
                    {
                        rejected = exception.Message.Contains(
                            "Initial AUV route rejected") &&
                            exception.Message.Contains("source was not started");
                    }
                    Require(rejected,
                        "Unsafe initial AUV route did not fail closed with a clear diagnostic.");
                    Require(!host.IsInitialized &&
                            !host.TryGetActiveEpoch(out _),
                        "Rejected initial route published source samples.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(driverObject);
                    UnityEngine.Object.DestroyImmediate(hostObject);
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        private static void VerifyStructuredDiagnostics()
        {
            ActiveRouteSnapshot route = BuildRoute(
                new Vector3d(0, 0, 0), new Vector3d(1, 0, 0));
            var profile = AuvTerrainClearanceProfile.CreateApprovedDefault();
            CoordinateTransformProfile transform =
                CoordinateTransformProfiles.UnityNative(
                    "FOCUSED", 1.0,
                    AttitudeDirection.BodyToWorld,
                    Quaterniond.Identity);
            Require(!AuvTerrainClearanceEvaluator.TryValidateRoute(
                    route, in transform, profile, new MissProvider(),
                    SyntheticSafeWaterY,
                    out string error,
                    out RouteSafetyFailureDiagnostic diagnostic) &&
                    diagnostic.SegmentIndex == 1 &&
                    Math.Abs(diagnostic.Percentage) < 1e-9 &&
                    diagnostic.TerrainState ==
                    AuvTerrainClearanceState.OutOfTerrainBounds.ToString(),
                error);

            string connection = VehicleRouteEditingController.FormatRejectedOutcome(
                true, 3UL, error, diagnostic);
            Require(connection.Contains("Connection segment") &&
                    connection.Contains("Active v3 unchanged") &&
                    connection.Contains("Route not published"),
                "Running connection failure classification is incomplete.");
            var draftDiagnostic = new RouteSafetyFailureDiagnostic(3, 12.5,
                AuvTerrainClearanceState.OutOfTerrainBounds.ToString());
            string draft = VehicleRouteEditingController.FormatRejectedOutcome(
                true, 3UL, error, draftDiagnostic);
            Require(draft.Contains("Draft segment 2") &&
                    draft.Contains("12.5%") &&
                    draft.Contains("OutOfTerrainBounds"),
                "Draft segment failure classification is incomplete.");

            string nonRunning = VehicleRouteEditingController.FormatRejectedOutcome(
                false, 3UL, error,
                new RouteSafetyFailureDiagnostic(2, 12.5,
                    AuvTerrainClearanceState.OutOfTerrainBounds.ToString()));
            Require(nonRunning.Contains("Draft segment 2"),
                "Non-running draft segment classification is incomplete.");
        }

        private static void VerifyDiagnosticIsolationAfterDraftFailure()
        {
            var prior = new RouteSafetyFailureDiagnostic(2, 12.5,
                AuvTerrainClearanceState.OutOfTerrainBounds.ToString());
            string outcome = VehicleRouteEditingController.FormatRejectedOutcome(
                false, 7UL,
                "Draft needs at least two distinct waypoints.",
                RouteSafetyFailureDiagnostic.None);
            Require(outcome.Contains("Draft needs at least two distinct waypoints") &&
                    outcome.Contains("Route not published") &&
                    outcome.Contains("Active v7 unchanged") &&
                    !outcome.Contains(prior.TerrainState) &&
                    !outcome.Contains("Draft segment 2") &&
                    !outcome.Contains("Connection segment") &&
                    !outcome.Contains("12.5%"),
                "Draft validation reused a stale terrain diagnostic.");
        }

        private static void VerifyOrdinaryHostErrorWithoutTerrainDiagnostic()
        {
            string outcome = VehicleRouteEditingController.FormatRejectedOutcome(
                true, 8UL,
                "The Driver accepted pose belongs to a retired SourceEpoch.",
                RouteSafetyFailureDiagnostic.None);
            Require(outcome.Contains("retired SourceEpoch") &&
                    !outcome.Contains("Connection segment") &&
                    !outcome.Contains("Draft segment"),
                "Ordinary Host failure included terrain diagnostic text.");
        }

        private static void VerifyThreeAttemptIsolation()
        {
            string first = VehicleRouteEditingController.FormatRejectedOutcome(
                true, 1UL, "terrain failure",
                new RouteSafetyFailureDiagnostic(1, 12.5,
                    AuvTerrainClearanceState.OutOfTerrainBounds.ToString()));
            string second = VehicleRouteEditingController.FormatRejectedOutcome(
                false, 1UL,
                "Draft needs at least two distinct waypoints.",
                RouteSafetyFailureDiagnostic.None);
            string third = VehicleRouteEditingController.FormatRejectedOutcome(
                true, 1UL,
                "The matching VehiclePoseDriver has not committed an accepted pose yet.",
                RouteSafetyFailureDiagnostic.None);
            Require(first.Contains("Connection segment") && first.Contains("12.5%") &&
                    second.Contains("Draft needs at least two distinct waypoints") &&
                    !second.Contains("Connection segment") && !second.Contains("12.5%") &&
                    third.Contains("has not committed an accepted pose") &&
                    !third.Contains("Connection segment") && !third.Contains("12.5%"),
                "Sequential Apply diagnostics were not isolated.");
        }

        private static void VerifyUiSurface()
        {
            RouteEditorPanelLayout layout = RouteEditorPanelLayout.Calculate(
                1280, 720, true, 120f, 120f);
            Rect local = new Rect(0f, 0f,
                layout.PanelRect.width, layout.PanelRect.height);
            Require(Contains(local, layout.HoldRect) &&
                    Contains(local, layout.LastOutcomeRect) &&
                    layout.LastOutcomeRect.yMin > layout.FeedbackRect.yMax,
                "Persistent Apply outcome or Hold surface escaped the panel.");
        }

        private static ActiveRouteSnapshot BuildConfiguredRoute(
            VehicleDataRuntimeHost host)
        {
            return BuildConfiguredRoute(host, "focused-default", null);
        }

        private static ActiveRouteSnapshot BuildConfiguredRoute(
            VehicleDataRuntimeHost host,
            string routeId,
            Action<List<Vector3d>> mutate)
        {
            VehicleRouteConfig config = VehicleRouteConfig.Load();
            Vector3 origin = host.IntegrationConfiguration.TestOrigin;
            var points = new List<Vector3d>
            {
                new Vector3d(origin.x, origin.y, origin.z)
            };
            foreach (Vector3 offset in config.GetLocalWaypoints(VehicleType.Auv))
                points.Add(new Vector3d(origin.x + offset.x,
                    origin.y + offset.y, origin.z + offset.z));
            Require(points.Count > 3,
                "Configured AUV route lacks interior indices 1/2/3.");
            mutate?.Invoke(points);
            Require(ActiveRouteSnapshotBuilder.TryBuild(
                    host.VehicleId, VehicleType.Auv, routeId, 1UL,
                    points, config.GetOrientationPolicy(VehicleType.Auv),
                    0.0, out ActiveRouteSnapshot route,
                    out string error), error);
            return route;
        }

        private static Vector3d OffsetY(Vector3d point, double delta)
        {
            return new Vector3d(point.X, point.Y + delta, point.Z);
        }

        private static void RequireRouteAccepted(
            AuvTerrainClearanceConstraint constraint,
            in CoordinateTransformProfile profile,
            ActiveRouteSnapshot route,
            string label)
        {
            bool accepted = constraint.TryValidateRoute(
                route, in profile, out string error);
            Require(accepted,
                label + " failed: " + error);
            Require(!constraint.LastRouteSafetyFailure.HasFailure,
                label + " was accepted but retained a failure diagnostic.");
        }

        private static void RequireRouteHasValidTerrainDecision(
            AuvTerrainClearanceConstraint constraint,
            in CoordinateTransformProfile profile,
            ActiveRouteSnapshot route,
            string label)
        {
            bool accepted = constraint.TryValidateRoute(
                route, in profile, out string error);
            RouteSafetyFailureDiagnostic diagnostic =
                constraint.LastRouteSafetyFailure;
            Require((accepted && !diagnostic.HasFailure) ||
                    (diagnostic.HasFailure &&
                     (diagnostic.TerrainState ==
                          AuvTerrainClearanceState.SlopeRejected.ToString() ||
                      diagnostic.TerrainState ==
                          AuvTerrainClearanceState.CorrectionRejected.ToString())),
                label + " retained a short-ray/invalid-sample failure: " + error);
        }

        private static ActiveRouteSnapshot BuildRoute(
            Vector3d first, Vector3d second)
        {
            Require(ActiveRouteSnapshotBuilder.TryBuild(
                    "focused", VehicleType.Auv, "focused-route", 1UL,
                    new[] { first, second },
                    VehicleRouteOrientationPolicy.AuvThreeDimensional,
                    0.0, out ActiveRouteSnapshot route,
                    out string error), error);
            return route;
        }

        private static VehicleDataRuntimeHost FindHost(Scene scene,
            VehicleType type)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    VehicleDataRuntimeHost>(true))
                .Single(host => host.IntegrationConfiguration.VehicleType == type);
        }

        private static VehiclePoseDriver FindDriver(Scene scene,
            VehicleDataRuntimeHost host)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    VehiclePoseDriver>(true))
                .Single(driver => ReferenceEquals(driver.RuntimeHost, host));
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin - 0.0001f &&
                inner.yMin >= outer.yMin - 0.0001f &&
                inner.xMax <= outer.xMax + 0.0001f &&
                inner.yMax <= outer.yMax + 0.0001f;
        }

        private static bool Near(float actual, float expected)
        {
            return Mathf.Abs(actual - expected) <= 0.00001f;
        }

        private static void RequireSceneDirtyUnchanged(
            Scene scene,
            bool initialSceneDirty)
        {
            Require(scene.IsValid() && scene.isLoaded,
                "The formal Scene was unloaded during focused verification.");
            Require(SceneManager.GetActiveScene() == scene,
                "The formal Scene Active identity changed during focused verification.");
            Require(string.Equals(
                    scene.path,
                    ScenePath,
                    StringComparison.Ordinal),
                "The formal Scene path changed during focused verification.");
            Require(scene.isDirty == initialSceneDirty,
                initialSceneDirty
                    ? "Focused verification cleared the user's existing Scene dirty state."
                    : "Focused verification dirtied the formal Scene.");
        }

        private static Mesh CreatePlaneMesh(string name, float halfExtent)
        {
            var mesh = new Mesh
            {
                name = name,
                vertices = new[]
                {
                    new Vector3(-halfExtent, 0f, -halfExtent),
                    new Vector3(-halfExtent, 0f, halfExtent),
                    new Vector3(halfExtent, 0f, -halfExtent),
                    new Vector3(halfExtent, 0f, halfExtent)
                },
                triangles = new[] { 0, 3, 2, 0, 1, 3 }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void Run(
            ICollection<string> passed,
            string name,
            Action action)
        {
            action();
            passed.Add(name);
        }

        private static string RequireExternalCreateNewPath()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index + 1 < arguments.Length; index++)
                if (arguments[index] == ReportArgument)
                {
                    string path = Path.GetFullPath(arguments[index + 1]);
                    Require(!File.Exists(path),
                        "Focused report path must be create-new.");
                    return path;
                }
            throw new InvalidOperationException(
                "Missing " + ReportArgument + ".");
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
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
