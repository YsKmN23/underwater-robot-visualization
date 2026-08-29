using System;
using System.Collections.Generic;
using System.IO;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Rov;
using UnderwaterRobotScene.Visualization.Runtime.Terrain;
using UnderwaterRobotScene.Visualization.Runtime.Usv;
using UnderwaterRobotScene.Visualization.Transforms;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    public static class RovVerticalMotionBatch3Verifier
    {
        private const string ReportArgument =
            "-rovVerticalBatch3VerifierReportPath";
        private const float SurfaceY = -3f;
        private const float ScreenTolerancePixels = 0.5f;
        private const int ProjectionWidth = 1280;
        private const int ProjectionHeight = 720;
        private static float screenSpaceMaximumPixelError;
        private static float legacyCorePixelError;
        private static float nearBottomCenterPixelError;
        private static float nearBottomOffCenterPixelError;
        private static float highCenterPixelError;
        private static float highOffCenterPixelError;
        private static float secondPitchPixelError;
        private static float dragPixelError;

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
            public float screenSpaceMaximumPixelError;
            public float legacyCorePixelError;
            public float nearBottomCenterPixelError;
            public float nearBottomOffCenterPixelError;
            public float highCenterPixelError;
            public float highOffCenterPixelError;
            public float secondPitchPixelError;
            public float dragPixelError;
            public CaseResult[] cases;
        }

        private sealed class SafetyFixture : IDisposable
        {
            internal SafetyFixture(string name)
            {
                Profile = RovContactProfile.CreateApprovedDefault();
                Mesh = CreateGridMesh(name + "_Mesh");
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
                Water.transform.position = new Vector3(0f, 100f, 0f);

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
            internal float SupportedY => SurfaceY + Profile.GroundClearance -
                Profile.LeftFrontOffset.y;

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
            screenSpaceMaximumPixelError = 0f;
            legacyCorePixelError = 0f;
            nearBottomCenterPixelError = 0f;
            nearBottomOffCenterPixelError = 0f;
            highCenterPixelError = 0f;
            highOffCenterPixelError = 0f;
            secondPitchPixelError = 0f;
            dragPixelError = 0f;
            var cases = new List<KeyValuePair<string, Func<string>>>
            {
                Case("01 ROV PageUp", VerifyRovPageUp),
                Case("02 ROV PageDown", VerifyRovPageDown),
                Case("03 Repeated vertical adjustment", VerifyRepeatedAdjustment),
                Case("04 ROV screen alignment and drag preserves Y",
                    VerifyRovScreenAlignmentAndDrag),
                Case("05 ROV next waypoint height inheritance", VerifyRovHeightInheritance),
                Case("06 AUV editor regression", VerifyAuvRegression),
                Case("07 USV editor isolation", VerifyUsvIsolation),
                Case("08 Draft and Active isolation", VerifyDraftActiveIsolation),
                Case("09 Safe ROV Apply integration", VerifySafeApplyIntegration),
                Case("10 Unsafe water edit", VerifyWaterReject),
                Case("11 Unsafe seabed and authority edits", VerifySeabedAndAuthorityReject),
                Case("12 Running Apply vertical continuity", VerifyRunningApply)
            };
            var results = new List<CaseResult>();
            int passed = 0;
            foreach (KeyValuePair<string, Func<string>> item in cases)
            {
                try
                {
                    results.Add(new CaseResult
                    {
                        name = item.Key,
                        status = "PASS",
                        detail = item.Value()
                    });
                    passed++;
                }
                catch (Exception exception)
                {
                    results.Add(new CaseResult
                    {
                        name = item.Key,
                        status = "FAIL",
                        detail = exception.GetType().Name + ": " + exception.Message
                    });
                }
            }

            bool success = passed == cases.Count;
            var report = new Report
            {
                schema = "ROV-VerticalMotion-Batch3-Verifier-v1",
                status = success
                    ? "ROV_VERTICAL_MOTION_BATCH3_ROUTE_EDITOR_EXPOSURE_PASS"
                    : "ROV_VERTICAL_MOTION_BATCH3_ROUTE_EDITOR_EXPOSURE_FAIL",
                unityVersion = Application.unityVersion,
                caseCount = cases.Count,
                passedCaseCount = passed,
                screenSpaceMaximumPixelError = screenSpaceMaximumPixelError,
                legacyCorePixelError = legacyCorePixelError,
                nearBottomCenterPixelError = nearBottomCenterPixelError,
                nearBottomOffCenterPixelError = nearBottomOffCenterPixelError,
                highCenterPixelError = highCenterPixelError,
                highOffCenterPixelError = highOffCenterPixelError,
                secondPitchPixelError = secondPitchPixelError,
                dragPixelError = dragPixelError,
                cases = results.ToArray()
            };
            File.WriteAllText(reportPath,
                JsonUtility.ToJson(report, true) + Environment.NewLine);
            if (!success)
                throw new InvalidOperationException(report.status + " | " +
                    passed + "/" + cases.Count + " cases passed.");
            Debug.Log(report.status + " | " + passed + "/" +
                cases.Count + " cases passed.");
        }

        private static string VerifyRovPageUp()
        {
            Vector3d original = new Vector3d(2, -4, 7);
            Vector3d raised = VehicleRouteEditingController.AdjustWaypointVertical(
                original, VehicleRouteEditingController.VerticalWaypointStepMetres);
            Require(RouteEditorVehiclePolicy.SupportsVerticalWaypointEditing(
                    VehicleSelectionKind.Rov) &&
                    Near(raised.X, original.X) && Near(raised.Z, original.Z) &&
                    Near(raised.Y, original.Y + 0.25),
                "ROV PageUp did not add exactly +0.25 world-Y metres.");
            return "ROV PageUp changes only Y by +0.25 m.";
        }

        private static string VerifyRovPageDown()
        {
            Vector3d original = new Vector3d(2, -4, 7);
            Vector3d lowered = VehicleRouteEditingController.AdjustWaypointVertical(
                original, -VehicleRouteEditingController.VerticalWaypointStepMetres);
            Require(Near(lowered.X, original.X) && Near(lowered.Z, original.Z) &&
                    Near(lowered.Y, original.Y - 0.25),
                "ROV PageDown did not add exactly -0.25 world-Y metres.");
            return "ROV PageDown changes only Y by -0.25 m.";
        }

        private static string VerifyRepeatedAdjustment()
        {
            Vector3d value = new Vector3d(-1, -3, 4);
            for (int index = 0; index < 7; index++)
                value = VehicleRouteEditingController.AdjustWaypointVertical(
                    value, VehicleRouteEditingController.VerticalWaypointStepMetres);
            Require(Near(value.X, -1) && Near(value.Z, 4) &&
                    Near(value.Y, -1.25),
                "Repeated vertical adjustment drifted or reset.");
            return "Seven PageUp operations accumulate deterministically to +1.75 m.";
        }

        private static string VerifyRovScreenAlignmentAndDrag()
        {
            GameObject cameraObject = new GameObject("Batch3_RovProjectionCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            var target = new RenderTexture(
                ProjectionWidth, ProjectionHeight, 0,
                RenderTextureFormat.ARGB32);
            try
            {
                camera.orthographic = false;
                camera.fieldOfView = 50f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 1000f;
                camera.targetTexture = target;
                ConfigureProjectionCamera(camera,
                    new Vector3(0f, 10f, -15f),
                    new Vector3(0f, 0f, 5f));

                float maximumError = 0f;
                nearBottomCenterPixelError = VerifyScreenRoundTrip(
                    camera, new Vector2(640f, 360f), -4.8f,
                    "near-bottom center", ref maximumError);
                nearBottomOffCenterPixelError = VerifyScreenRoundTrip(
                    camera, new Vector2(980f, 250f), -4.8f,
                    "near-bottom off-center", ref maximumError);
                highCenterPixelError = VerifyScreenRoundTrip(
                    camera, new Vector2(640f, 360f), 2f,
                    "high free-water center", ref maximumError);
                Vector2 corePoint = new Vector2(980f, 250f);
                highOffCenterPixelError = VerifyScreenRoundTrip(
                    camera, corePoint, 2f,
                    "high free-water off-center", ref maximumError);

                Ray legacyRay = camera.ScreenPointToRay(corePoint);
                Require(TryProjectLegacySeabedSubstitution(
                        legacyRay, -5f, 2f, out Vector3 legacyWorld),
                    "Legacy sensitivity fixture did not reach the synthetic seabed.");
                Vector3 legacyScreen = camera.WorldToScreenPoint(legacyWorld);
                legacyCorePixelError = Vector2.Distance(
                    corePoint, (Vector2)legacyScreen);
                Require(legacyScreen.z > 0f && legacyCorePixelError > 5f,
                    "The high/off-center fixture is not sensitive to the legacy ROV projection defect.");

                ConfigureProjectionCamera(camera,
                    new Vector3(8f, 14f, -12f),
                    new Vector3(0f, 0f, 5f));
                secondPitchPixelError = VerifyScreenRoundTrip(
                    camera, new Vector2(360f, 300f), 2f,
                    "second camera pitch", ref maximumError);

                Vector2 pointerDown = new Vector2(700f, 330f);
                Vector2 pointerCurrent = new Vector2(900f, 280f);
                Require(TryProjectScreenPoint(camera, pointerDown, 2f,
                        out Vector3 original),
                    "ROV drag pointer-down projection failed.");
                Require(TryProjectScreenPoint(camera, pointerCurrent, 2f,
                        out Vector3 currentProjection),
                    "ROV drag current projection failed.");
                var gesture = new RoutePointerGesture();
                gesture.Begin(pointerDown, original, true, original);
                Require(gesture.TryGetDragTarget(
                        pointerCurrent, true, currentProjection,
                        RouteEditorVehiclePolicy.SupportsVerticalWaypointEditing(
                            VehicleSelectionKind.Rov),
                        out Vector3 moved),
                    "ROV screen-space drag did not start.");
                Vector3 movedScreen = camera.WorldToScreenPoint(moved);
                dragPixelError = Vector2.Distance(
                    pointerCurrent, (Vector2)movedScreen);
                maximumError = Mathf.Max(maximumError, dragPixelError);
                Require(movedScreen.z > 0f &&
                        dragPixelError <= ScreenTolerancePixels &&
                        Near(moved.y, original.y),
                    "ROV drag lost pointer alignment or changed the edited Y.");

                screenSpaceMaximumPixelError = maximumError;
                return "Six ROV screen/drag cases close within " +
                    maximumError.ToString("0.0000") +
                    " px; legacy core error=" +
                    legacyCorePixelError.ToString("0.00") +
                    " px; drag preserves Y.";
            }
            finally
            {
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private static void ConfigureProjectionCamera(
            Camera camera,
            Vector3 position,
            Vector3 lookAt)
        {
            camera.transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(lookAt - position, Vector3.up));
        }

        private static float VerifyScreenRoundTrip(
            Camera camera,
            Vector2 screenPoint,
            float routeY,
            string label,
            ref float maximumError)
        {
            Require(TryProjectScreenPoint(
                    camera, screenPoint, routeY, out Vector3 world),
                label + " ROV route-editor projection failed.");
            Vector3 projected = camera.WorldToScreenPoint(world);
            float error = Vector2.Distance(screenPoint, (Vector2)projected);
            maximumError = Mathf.Max(maximumError, error);
            Require(projected.z > 0f && error <= ScreenTolerancePixels &&
                    Near(world.y, routeY),
                label + " screen round-trip error was " +
                error.ToString("0.0000") + " px.");
            return error;
        }

        private static bool TryProjectScreenPoint(
            Camera camera,
            Vector2 screenPoint,
            float routeY,
            out Vector3 world)
        {
            return VehicleRouteProjection.TryProjectRouteEditorPointer(
                VehicleSelectionKind.Rov,
                camera.ScreenPointToRay(screenPoint),
                routeY,
                out world);
        }

        private static bool TryProjectLegacySeabedSubstitution(
            Ray ray,
            float seabedY,
            float routeY,
            out Vector3 world)
        {
            world = default;
            if (Mathf.Abs(ray.direction.y) < 0.00001f)
                return false;
            float distance = (seabedY - ray.origin.y) / ray.direction.y;
            if (distance <= 0f || float.IsNaN(distance) ||
                float.IsInfinity(distance))
                return false;
            Vector3 hit = ray.GetPoint(distance);
            world = new Vector3(hit.x, routeY, hit.z);
            return true;
        }

        private static string VerifyRovHeightInheritance()
        {
            ActiveRouteSnapshot active = BuildRov("INHERIT", 1UL,
                new Vector3d(0, -3, 0), new Vector3d(2, -3, 2));
            var draft = new DraftRouteSession();
            draft.Begin(active);
            draft.Select(1);
            draft.MoveSelected(VehicleRouteEditingController.AdjustWaypointVertical(
                draft.Waypoints[1], 1.0));
            draft.ExitPreservingDraft();
            Require(RouteEditHeightResolver.TryResolve(
                    VehicleSelectionKind.Rov, draft, active, -9f,
                    out RouteEditHeightResolution height) &&
                    height.Source == RouteEditHeightSource.PreviousWaypoint &&
                    Near(height.WorldY, -2.0),
                "ROV new-point resolver did not inherit the previous Draft Y.");
            Require(draft.Add(new Vector3d(4, height.WorldY, 4)) &&
                    Near(draft.Waypoints[2].Y, -2.0),
                "ROV appended waypoint did not keep inherited Y.");
            return "ROV append inherits the previous Draft waypoint Y.";
        }

        private static string VerifyAuvRegression()
        {
            Require(RouteEditorVehiclePolicy.SupportsVerticalWaypointEditing(
                    VehicleSelectionKind.Auv),
                "AUV lost vertical editor eligibility.");
            Vector3d original = new Vector3d(1, -2, 3);
            Vector3d raised = VehicleRouteEditingController.AdjustAuvWaypointDepth(
                original, 0.25);
            var gesture = new RoutePointerGesture();
            gesture.Begin(Vector2.zero, new Vector3(1, -1.75f, 3), true,
                new Vector3(1, 0, 3));
            Require(Near(raised.Y, -1.75) &&
                    gesture.TryGetDragTarget(new Vector2(6, 0), true,
                        new Vector3(3, 7, 5), true, out Vector3 moved) &&
                    Near(moved.y, -1.75f),
                "AUV PageUp or drag-preserve-Y behavior changed.");
            return "AUV PageUp compatibility helper and drag preserve-Y remain intact.";
        }

        private static string VerifyUsvIsolation()
        {
            Require(!RouteEditorVehiclePolicy.SupportsVerticalWaypointEditing(
                    VehicleSelectionKind.Usv),
                "USV incorrectly gained vertical editor eligibility.");
            ActiveRouteSnapshot usv = Build(
                "USV", VehicleType.Usv,
                VehicleRouteOrientationPolicy.UsvSurfaceYaw,
                new Vector3d(0, 0.2, 0), new Vector3d(2, 0.2, 2));
            var draft = new DraftRouteSession();
            draft.Begin(usv);
            draft.ExitPreservingDraft();
            Require(RouteEditHeightResolver.TryResolve(
                    VehicleSelectionKind.Usv, draft, usv, 0.2f,
                    out RouteEditHeightResolution height) &&
                    height.Source == RouteEditHeightSource.ActiveRoute &&
                    Near(height.WorldY, 0.2),
                "USV height fallback changed from Active Route surface semantics.");
            return "USV has no vertical keys and retains Active Route surface height.";
        }

        private static string VerifyDraftActiveIsolation()
        {
            ActiveRouteSnapshot active = BuildRov("ISOLATION", 3UL,
                new Vector3d(0, -3, 0), new Vector3d(2, -3, 2));
            var draft = new DraftRouteSession();
            draft.Begin(active);
            draft.Select(1);
            draft.MoveSelected(VehicleRouteEditingController.AdjustWaypointVertical(
                draft.Waypoints[1], 0.75));
            Require(draft.Add(new Vector3d(2, -1.75, 2)),
                "Y-only distinct Draft point was not added.");
            Require(draft.TryValidate(active, 1.0, out string error), error);
            Require(Near(draft.Waypoints[1].Y, -2.25) &&
                    draft.Waypoints.Count == 3 &&
                    Near(draft.Waypoints[2].X, draft.Waypoints[1].X) &&
                    Near(draft.Waypoints[2].Z, draft.Waypoints[1].Z) &&
                    !Near(draft.Waypoints[2].Y, draft.Waypoints[1].Y) &&
                    Near(active.GetWaypoint(1).Y, -3.0) &&
                    active.RouteVersion == 3UL,
                "Draft vertical edit mutated Active Route or compressed a Y-only distinct point.");
            return "Draft Y changes and Y-only distinct points remain isolated from immutable Active Route.";
        }

        private static string VerifySafeApplyIntegration()
        {
            using (var fixture = new SafetyFixture("Batch3SafeApply"))
            {
                float y = fixture.SupportedY + 0.5f;
                ActiveRouteSnapshot active = BuildRov("SAFE-APPLY", 1UL,
                    new Vector3d(0, y, -1), new Vector3d(0, y, 1));
                var draft = new DraftRouteSession();
                draft.Begin(active);
                draft.Select(1);
                draft.MoveSelected(VehicleRouteEditingController
                    .AdjustWaypointVertical(draft.Waypoints[1], 0.5));
                Require(draft.TryValidate(active, 1.0, out string error), error);
                ActiveRouteSnapshot candidate = BuildRov("SAFE-APPLY", 2UL,
                    draft.Waypoints[0], draft.Waypoints[1]);
                Require(fixture.Constraint.TryValidateRoute(
                        candidate, TransformProfile(), out error), error);

                var runtime = new VehicleRouteRuntime(active, 1.0);
                Require(runtime.Pause(), "Safe Apply harness could not pause.");
                using (var source = new RouteFollowingSource(
                           "batch3-safe", 0.1, runtime,
                           WorldFrame.UnityWorld, BodyFrame.UnityBody))
                using (var store = Store())
                {
                    source.Start(store);
                    Require(source.TryActivateWhenNotRunning(candidate, out error), error);
                    Require(ReferenceEquals(runtime.ActiveSnapshot, candidate) &&
                            Near(candidate.GetWaypoint(1).Y, y + 0.5f),
                        "Safe vertical Draft did not publish its XYZ Active Route.");
                }
            }
            return "Safe ROV Y-changing Draft validates and publishes through the existing route chain.";
        }

        private static string VerifyWaterReject()
        {
            using (var fixture = new SafetyFixture("Batch3WaterReject"))
            {
                float y = fixture.SupportedY + 0.5f;
                fixture.Water.transform.position = new Vector3(
                    0f, y + fixture.Profile.UpperEnvelopeMaximum.y + 0.2f, 0f);
                ActiveRouteSnapshot route = BuildRov("WATER-REJECT", 1UL,
                    new Vector3d(0, y, -1),
                    new Vector3d(0, y + 0.5f, 1));
                Require(!fixture.Constraint.TryValidateRoute(
                        route, TransformProfile(), out string error) &&
                        error.Contains("WaterSurfaceBreach"),
                    "Water-breaching vertical edit was not rejected by Batch 1 safety.");
            }
            return "Water-envelope breach rejects through the existing ROV validator.";
        }

        private static string VerifySeabedAndAuthorityReject()
        {
            using (var fixture = new SafetyFixture("Batch3SeabedReject"))
            {
                float y = fixture.SupportedY;
                ActiveRouteSnapshot seabed = BuildRov("SEABED-REJECT", 1UL,
                    new Vector3d(0, y, -1),
                    new Vector3d(0, y - 0.5f, 1));
                Require(!fixture.Constraint.TryValidateRoute(
                        seabed, TransformProfile(), out string seabedError) &&
                        seabedError.Contains("CorrectionRejected"),
                    "Unsafe seabed vertical edit was not rejected.");
                fixture.Collider.enabled = false;
                ActiveRouteSnapshot authority = BuildRov("AUTHORITY-REJECT", 1UL,
                    new Vector3d(0, y, -1), new Vector3d(0, y + 0.2f, 1));
                Require(!fixture.Constraint.TryValidateRoute(
                        authority, TransformProfile(), out string authorityError) &&
                        !string.IsNullOrWhiteSpace(authorityError),
                    "Invalid terrain authority did not fail closed.");
            }
            return "Seabed penetration and invalid terrain authority both reject.";
        }

        private static string VerifyRunningApply()
        {
            ActiveRouteSnapshot active = BuildRov("RUNNING", 4UL,
                new Vector3d(0, -3, 0), new Vector3d(0, -3, 5));
            var runtime = new VehicleRouteRuntime(active, 1.0);
            using (var store = Store())
            using (var source = new RouteFollowingSource(
                       "batch3-running", 0.1, runtime,
                       WorldFrame.UnityWorld, BodyFrame.UnityBody))
            {
                source.Start(store);
                Require(source.Step(0.0) == 1, "Running Apply harness did not start.");
                VehicleRoutePose accepted = runtime.SampleCurrentPose();
                Require(AtomicRouteReplanCandidate.TryBuild(
                        active,
                        new[]
                        {
                            new Vector3d(accepted.Position.X,
                                accepted.Position.Y + 0.5, accepted.Position.Z),
                            new Vector3d(2, accepted.Position.Y + 0.5, 3)
                        },
                        in accepted, 0.5,
                        out ActiveRouteSnapshot candidate, out string error), error);
                ulong routeEpoch = runtime.RouteEpoch;
                ulong sourceEpoch = source.SourceEpoch;
                Require(source.TryPublishRunningReplan(
                        candidate, in accepted, 0.5, out error), error);
                Require(candidate.WaypointCount == 3 &&
                        runtime.RouteEpoch == routeEpoch + 1UL &&
                        source.SourceEpoch == sourceEpoch + 1UL &&
                        Near(runtime.SampleCurrentPose().Position, accepted.Position),
                    "Running Apply lost vertical waypoint, epochs, or accepted pose.");
            }
            return "Running Apply retains vertical waypoint and accepted-pose/epoch continuity.";
        }

        private static ActiveRouteSnapshot BuildRov(
            string id, ulong version, params Vector3d[] points)
        {
            Require(ActiveRouteSnapshotBuilder.TryBuild(
                    "ROV-" + id, VehicleType.Rov, id, version, points,
                    VehicleRouteOrientationPolicy.RovLevelYaw, 0.0,
                    Quaterniond.Identity,
                    out ActiveRouteSnapshot route, out string error), error);
            return route;
        }

        private static ActiveRouteSnapshot Build(
            string id, VehicleType type,
            VehicleRouteOrientationPolicy policy,
            params Vector3d[] points)
        {
            Require(ActiveRouteSnapshotBuilder.TryBuild(
                    id, type, id, 1UL, points, policy, 0.0,
                    out ActiveRouteSnapshot route, out string error), error);
            return route;
        }

        private static CoordinateTransformProfile TransformProfile()
        {
            return CoordinateTransformProfiles.UnityNative(
                "BATCH3-UNITY", 1.0,
                AttitudeDirection.BodyToWorld, Quaterniond.Identity);
        }

        private static VehicleStateStore Store()
        {
            return new VehicleStateStore(new VehicleStateStorePolicy(
                capacityPerVehicle: 16,
                timeoutSeconds: 2.0));
        }

        private static Mesh CreateGridMesh(string name)
        {
            const int count = 9;
            const float minimum = -4f;
            var vertices = new Vector3[count * count];
            for (int x = 0; x < count; x++)
            {
                for (int z = 0; z < count; z++)
                    vertices[x * count + z] = new Vector3(
                        minimum + x, 0f, minimum + z);
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

        private static bool Near(Vector3d a, Vector3d b)
        {
            return Near(a.X, b.X) && Near(a.Y, b.Y) && Near(a.Z, b.Z);
        }

        private static bool Near(double a, double b)
        {
            return Math.Abs(a - b) <= 1e-5;
        }

        private static KeyValuePair<string, Func<string>> Case(
            string name, Func<string> body)
        {
            return new KeyValuePair<string, Func<string>>(name, body);
        }

        private static string RequireExternalCreateNewPath()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index + 1 < arguments.Length; index++)
            {
                if (!string.Equals(arguments[index], ReportArgument,
                        StringComparison.Ordinal))
                    continue;
                string path = Path.GetFullPath(arguments[index + 1]);
                Require(!File.Exists(path), "Verifier report path already exists.");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                return path;
            }
            throw new InvalidOperationException(ReportArgument + " is required.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
