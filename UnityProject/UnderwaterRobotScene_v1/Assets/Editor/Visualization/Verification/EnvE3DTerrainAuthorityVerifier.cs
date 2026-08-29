using System;
using System.Collections.Generic;
using System.Linq;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.RouteFollowing;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Auv;
using UnderwaterRobotScene.Visualization.Runtime.Terrain;
using UnderwaterRobotScene.Visualization.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    public static class EnvE3DTerrainAuthorityVerifier
    {
        private const string FormalScenePath =
            "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const float PositionTolerance = 0.0002f;
        private const float NormalToleranceDegrees = 0.05f;

        [MenuItem(
            "Underwater Robot Scene/Verification/AUV Terrain Authority Pure Tests")]
        public static void RunPureTests()
        {
            VerifyPhase2BMigrationSourceMeshContract();
            VerifyRegularGridAndBothTriangles();
            VerifyFaceNormalAndRenderNormalIsolation();
            VerifyOutsideAndExactMaximumBoundary();
            VerifyInvalidGeometryFailures();
            VerifyUnsupportedTransform();
            VerifyBindingFailures();
            VerifyStaleCache();
            VerifyNonFiniteRequest();
            VerifyDepthIndependenceAndLegacyBoundary();
            Debug.Log("ENV_E3D_TERRAIN_AUTHORITY_PURE_PASS");
        }

        [MenuItem(
            "Underwater Robot Scene/Verification/Phase 2B Migration Source Mesh Pure Tests")]
        public static void RunPhase2BMigrationSourceMeshPureTests()
        {
            VerifyPhase2BMigrationSourceMeshContract();
            Debug.Log("ENV_E3D_PHASE_2B_MIGRATION_SOURCE_MESH_PURE_PASS");
        }

        public static void RunCurrentSceneEquivalenceHeadless()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,
                "Terrain authority equivalence cannot bootstrap during Play Mode.");
            Scene scene = EditorSceneManager.OpenScene(
                FormalScenePath,
                OpenSceneMode.Single);
            Require(scene.IsValid() && scene.isLoaded &&
                    string.Equals(
                        scene.path,
                        FormalScenePath,
                        StringComparison.Ordinal) &&
                    !EditorSceneManager.IsPreviewScene(scene),
                "Headless bootstrap did not open the formal Scene.");
            if (SceneManager.GetActiveScene() != scene)
            {
                Require(SceneManager.SetActiveScene(scene),
                    "Headless bootstrap could not activate the formal Scene.");
            }
            Require(SceneManager.GetActiveScene() == scene,
                "Headless bootstrap did not preserve the formal Scene as Active.");
            Require(!scene.isDirty,
                "Headless bootstrap requires a clean formal Scene.");

            RunCurrentSceneEquivalence();
        }

        [MenuItem(
            "Underwater Robot Scene/Verification/AUV Terrain Authority Equivalence")]
        public static void RunCurrentSceneEquivalence()
        {
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded &&
                    string.Equals(
                        scene.path,
                        FormalScenePath,
                        StringComparison.Ordinal) &&
                    !EditorSceneManager.IsPreviewScene(scene),
                "Open UnderwaterRobotDemo as the active Scene before running this verifier.");
            var initialHandle = scene.handle;
            bool initialDirty = scene.isDirty;

            GameObject seabed = scene.GetRootGameObjects().Single(value =>
                string.Equals(value.name, "Seabed", StringComparison.Ordinal));
            MeshFilter filter = seabed.GetComponent<MeshFilter>();
            MeshCollider collider = seabed.GetComponent<MeshCollider>();
            GameObject water = scene.GetRootGameObjects().Single(value =>
                string.Equals(
                    value.name, "Water_Surface", StringComparison.Ordinal));
            Require(filter != null && collider != null &&
                    filter.sharedMesh != null &&
                    ReferenceEquals(filter.sharedMesh, collider.sharedMesh),
                "Formal Seabed does not have one shared visual/collision Mesh.");

            VehiclePoseDriver auvDriver = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    VehiclePoseDriver>(true))
                .Single(value => value.RuntimeHost != null &&
                    value.RuntimeHost.IntegrationConfiguration.VehicleType ==
                        VehicleType.Auv);
            var constraint = auvDriver.PoseConstraintProvider as
                AuvTerrainClearanceConstraint;
            Require(constraint != null &&
                    constraint.SurfaceSampler != null &&
                    ReferenceEquals(
                        constraint.SurfaceSampler.ContactTerrain,
                        collider) &&
                    ReferenceEquals(constraint.WaterSurface, water.transform) &&
                    water.activeInHierarchy,
                "AUV authority is not bound to the formal Seabed and Water_Surface authorities.");
            TerrainSurfaceSampler sampler = constraint.SurfaceSampler;
            Require(sampler.TryValidateAuthority(
                    out TerrainAuthorityFailure validationFailure),
                "Formal authority validation failed: " + validationFailure);
            Require(sampler.AuthorityGridXCount == 201 &&
                    sampler.AuthorityGridZCount == 145 &&
                    Near(sampler.AuthorityGridSpacing, 0.5f,
                        PositionTolerance) &&
                    sampler.AuthorityGeometryHash.Length == 64,
                "Current Scene authority grid identity drifted.");

            CompareAuthorityAndCollider(sampler, collider);
            VerifyOutsideCoverage(sampler, collider.transform);
            VerifyCurrentRouteCases(auvDriver, constraint);

            Scene after = SceneManager.GetActiveScene();
            Require(after.IsValid() && after.isLoaded &&
                    after.handle == initialHandle &&
                    string.Equals(
                        after.path,
                        FormalScenePath,
                        StringComparison.Ordinal),
                "Authority verifier changed the active formal Scene identity.");
            Require(after.isDirty == initialDirty,
                "Authority verifier changed the formal Scene dirty state.");
            Debug.Log(
                "ENV_E3D_TERRAIN_AUTHORITY_EQUIVALENCE_PASS | " +
                "grid=201x145 | spacing=0.5m | geometryHash=" +
                sampler.AuthorityGeometryHash);
        }

        private static void VerifyRegularGridAndBothTriangles()
        {
            using (var fixture = new MeshFixture(
                       "Authority_Regular",
                       3,
                       3,
                       1f,
                       (x, z) => x + 2f * z))
            {
                ValidatedContactMeshTerrainAuthority authority =
                    RequireAuthority(fixture.Mesh, IdentityTransform());
                Require(authority.XCount == 3 && authority.ZCount == 3 &&
                        Near(authority.Spacing, 1f, PositionTolerance),
                    "The 2x2-cell grid was not discovered from Mesh data.");
                TerrainAuthoritySample first = RequireSample(
                    authority, 0.75f, 0.25f);
                TerrainAuthoritySample second = RequireSample(
                    authority, 0.25f, 0.75f);
                TerrainAuthoritySample diagonal = RequireSample(
                    authority, 0.5f, 0.5f);
                Require(first.TriangleIndex == 0 &&
                        second.TriangleIndex == 1 &&
                        diagonal.TriangleIndex == 0 &&
                        Near(first.WorldPoint.y, 1.25f,
                            PositionTolerance) &&
                        Near(second.WorldPoint.y, 1.75f,
                            PositionTolerance),
                    "Triangle selection or barycentric height is incorrect.");
            }
        }

        private static void VerifyPhase2BMigrationSourceMeshContract()
        {
            TerrainAuthorityTransformSignature transform = IdentityTransform();
            using (var legacy = new MeshFixture(
                       "ENV_E3A_ContactTerrainMesh",
                       101,
                       73,
                       0.5f,
                       -25f,
                       -18f,
                       (x, z) => -3.4f))
            {
                Require(EnvE3ATerrainGeometry
                        .TrySampleMigrationSourceContactMesh(
                            legacy.Mesh,
                            transform,
                            10f,
                            0f,
                            out float height,
                            out Vector3 normal,
                            out string legacyFailure) &&
                        Near(height, -3.4f, PositionTolerance) &&
                        Vector3.Angle(normal, Vector3.up) <=
                            NormalToleranceDegrees,
                    "Legacy migration-source sampling failed: " +
                    legacyFailure);

                bool legacyRejectedAsAuthority = false;
                try
                {
                    EnvE3ATerrainGeometry.ValidateContactMesh(
                        legacy.Mesh,
                        EnvE3AContinuousSeabedConfiguration.CreateApproved());
                }
                catch (InvalidOperationException exception)
                {
                    legacyRejectedAsAuthority = string.Equals(
                        exception.Message,
                        "Contact terrain mesh does not match the authority grid.",
                        StringComparison.Ordinal);
                }
                Require(legacyRejectedAsAuthority,
                    "Legacy migration source was accepted as new authority.");

                Require(!EnvE3ATerrainGeometry
                        .TrySampleMigrationSourceContactMesh(
                            legacy.Mesh,
                            transform,
                            25.01f,
                            0f,
                            out _,
                            out _,
                            out string outsideFailure) &&
                        string.Equals(
                            outsideFailure,
                            "ENV_E3D_MIGRATION_SOURCE_OUTSIDE_COVERAGE",
                            StringComparison.Ordinal),
                    "Migration-source sampling did not fail closed outside coverage.");
            }

            Mesh authoritative = null;
            try
            {
                EnvE3AContinuousSeabedConfiguration configuration =
                    EnvE3AContinuousSeabedConfiguration.CreateApproved();
                authoritative =
                    EnvE3ATerrainGeometry.BuildContactMesh(configuration);
                EnvE3ATerrainGeometry.ValidateContactMesh(
                    authoritative,
                    configuration);
            }
            finally
            {
                if (authoritative != null)
                {
                    UnityEngine.Object.DestroyImmediate(authoritative);
                }
            }

            using (var malformed = new MeshFixture(
                       "ENV_E3A_ContactTerrainMesh",
                       3,
                       3,
                       0.5f,
                       (x, z) => -3.4f))
            {
                int[] invalidTopology = malformed.Mesh.triangles;
                invalidTopology[0] = invalidTopology[1];
                malformed.Mesh.triangles = invalidTopology;
                Require(!EnvE3ATerrainGeometry
                        .TrySampleMigrationSourceContactMesh(
                            malformed.Mesh,
                            transform,
                            0.5f,
                            0.5f,
                            out _,
                            out _,
                            out string malformedFailure) &&
                        string.Equals(
                            malformedFailure,
                            "ENV_E3D_MIGRATION_SOURCE_TOPOLOGY_INVALID",
                            StringComparison.Ordinal),
                    "Malformed migration-source topology did not fail closed.");
            }
        }

        private static void VerifyFaceNormalAndRenderNormalIsolation()
        {
            using (var first = new MeshFixture(
                       "Authority_FaceNormal_A",
                       2,
                       2,
                       1f,
                       (x, z) => 0.5f * x,
                       Vector3.forward))
            using (var second = new MeshFixture(
                       "Authority_FaceNormal_B",
                       2,
                       2,
                       1f,
                       (x, z) => 0.5f * x,
                       Vector3.left))
            {
                ValidatedContactMeshTerrainAuthority authority =
                    RequireAuthority(first.Mesh, IdentityTransform());
                ValidatedContactMeshTerrainAuthority comparison =
                    RequireAuthority(second.Mesh, IdentityTransform());
                TerrainAuthoritySample sample = RequireSample(
                    authority, 0.75f, 0.25f);
                Vector3 expected = new Vector3(-0.5f, 1f, 0f).normalized;
                Require(Vector3.Angle(sample.WorldNormal, expected) <=
                        NormalToleranceDegrees &&
                        Near(sample.SlopeDegrees,
                            Mathf.Atan(0.5f) * Mathf.Rad2Deg,
                            NormalToleranceDegrees),
                    "Safety normal did not follow the geometric face.");
                Require(Vector3.Angle(sample.WorldNormal, Vector3.forward) >
                        10f &&
                        string.Equals(
                            authority.GeometryHash,
                            comparison.GeometryHash,
                            StringComparison.Ordinal),
                    "Render normals affected authority sampling or identity.");
            }
        }

        private static void VerifyOutsideAndExactMaximumBoundary()
        {
            using (var fixture = new MeshFixture(
                       "Authority_Bounds", 3, 3, 1f, (x, z) => -3f))
            {
                ValidatedContactMeshTerrainAuthority authority =
                    RequireAuthority(fixture.Mesh, IdentityTransform());
                Require(!authority.TrySampleAtXZ(
                        -0.001f,
                        1f,
                        out _,
                        out TerrainAuthorityFailure outside) &&
                        outside == TerrainAuthorityFailure.OutsideCoverage,
                    "Outside X/Z was not rejected distinctly.");
                TerrainAuthoritySample maximum = RequireSample(
                    authority, 2f, 2f);
                Require(maximum.CellX == 1 && maximum.CellZ == 1 &&
                        Near(maximum.WorldPoint.y, -3f,
                            PositionTolerance),
                    "Exact max-X/max-Z boundary was not sampled.");
            }
        }

        private static void VerifyInvalidGeometryFailures()
        {
            Require(!ValidatedContactMeshTerrainAuthority
                    .TryComputeUpwardFaceNormal(
                        Vector3.zero,
                        Vector3.right,
                        Vector3.right * 2f,
                        out _,
                        out TerrainAuthorityFailure degenerate) &&
                    degenerate == TerrainAuthorityFailure.InvalidTriangle,
                "A degenerate triangle did not fail closed.");
            Require(!ValidatedContactMeshTerrainAuthority
                    .TryComputeUpwardFaceNormal(
                        Vector3.zero,
                        Vector3.right,
                        Vector3.forward,
                        out _,
                        out TerrainAuthorityFailure downward) &&
                    downward == TerrainAuthorityFailure.InvalidNormal,
                "A downward triangle normal did not fail closed.");

            Vector3[] vertices = BuildVertices(
                2, 2, 1f, (x, z) => 0f);
            int[] indices = BuildTriangles(2, 2);
            indices[0] = vertices.Length;
            Require(!ValidatedContactMeshTerrainAuthority.TryValidateMeshData(
                    vertices,
                    indices,
                    BoundsFor(vertices),
                    out TerrainAuthorityFailure invalidIndex) &&
                    invalidIndex == TerrainAuthorityFailure.InvalidTriangle,
                "An out-of-range triangle index did not fail closed.");
            int[] missingTriangle = BuildTriangles(2, 2).Take(3).ToArray();
            Require(!ValidatedContactMeshTerrainAuthority.TryValidateMeshData(
                    vertices,
                    missingTriangle,
                    BoundsFor(vertices),
                    out TerrainAuthorityFailure hole) &&
                    hole == TerrainAuthorityFailure.TopologyHole,
                "A missing canonical triangle was not reported as a topology hole.");
        }

        private static void VerifyUnsupportedTransform()
        {
            using (var fixture = new MeshFixture(
                       "Authority_Transform", 2, 2, 1f, (x, z) => 0f))
            {
                var translated = new TerrainAuthorityTransformSignature(
                    new Vector3(10f, -7f, 20f),
                    Quaternion.identity,
                    Vector3.one);
                ValidatedContactMeshTerrainAuthority translatedAuthority =
                    RequireAuthority(fixture.Mesh, translated);
                TerrainAuthoritySample translatedSample = RequireSample(
                    translatedAuthority, 10.5f, 20.5f);
                Require(Vector3.Distance(
                            translatedSample.WorldPoint,
                            new Vector3(10.5f, -7f, 20.5f)) <=
                        PositionTolerance,
                    "Finite terrain translation was not applied correctly.");

                var unsupported = new TerrainAuthorityTransformSignature(
                    Vector3.zero,
                    Quaternion.Euler(1f, 0f, 0f),
                    Vector3.one);
                Require(!ValidatedContactMeshTerrainAuthority.TryCreate(
                        fixture.Mesh,
                        in unsupported,
                        out _,
                        out TerrainAuthorityFailure failure) &&
                        failure ==
                            TerrainAuthorityFailure.UnsupportedTransform,
                    "Rotated terrain did not fail closed.");
            }
        }

        private static void VerifyBindingFailures()
        {
            var missing = new TerrainAuthorityBindingState(
                false, false, false, false, null, null,
                IdentityTransform());
            Require(!TerrainAuthorityBindingValidator.TryValidate(
                    in missing,
                    out TerrainAuthorityFailure missingFailure) &&
                    missingFailure ==
                        TerrainAuthorityFailure.MissingAuthority,
                "Missing collider did not fail closed.");

            using (var fixture = new MeshFixture(
                       "Authority_Binding", 2, 2, 1f, (x, z) => 0f))
            using (var other = new MeshFixture(
                       "Authority_Binding_Other", 2, 2, 1f,
                       (x, z) => 0f))
            {
                var disabled = new TerrainAuthorityBindingState(
                    true,
                    false,
                    true,
                    false,
                    fixture.Mesh,
                    fixture.Mesh,
                    IdentityTransform());
                Require(!TerrainAuthorityBindingValidator.TryValidate(
                        in disabled,
                        out TerrainAuthorityFailure disabledFailure) &&
                        disabledFailure ==
                            TerrainAuthorityFailure.InvalidAuthority,
                    "Disabled collider did not fail closed.");
                var mismatch = new TerrainAuthorityBindingState(
                    true,
                    true,
                    true,
                    false,
                    fixture.Mesh,
                    other.Mesh,
                    IdentityTransform());
                Require(!TerrainAuthorityBindingValidator.TryValidate(
                        in mismatch,
                        out TerrainAuthorityFailure mismatchFailure) &&
                        mismatchFailure ==
                            TerrainAuthorityFailure.IdentityMismatch,
                    "Different MeshFilter/MeshCollider geometry was accepted.");
                fixture.Mesh.UploadMeshData(true);
                var unreadable = new TerrainAuthorityBindingState(
                    true,
                    true,
                    true,
                    false,
                    fixture.Mesh,
                    fixture.Mesh,
                    IdentityTransform());
                Require(!TerrainAuthorityBindingValidator.TryValidate(
                        in unreadable,
                        out TerrainAuthorityFailure unreadableFailure) &&
                        unreadableFailure ==
                            TerrainAuthorityFailure.InvalidAuthority,
                    "An unreadable authority Mesh was accepted.");
            }
        }

        private static void VerifyStaleCache()
        {
            using (var first = new MeshFixture(
                       "Authority_Stale_A", 2, 2, 1f, (x, z) => 0f))
            using (var second = new MeshFixture(
                       "Authority_Stale_B", 2, 2, 1f, (x, z) => 0f))
            {
                TerrainAuthorityTransformSignature transform =
                    IdentityTransform();
                ValidatedContactMeshTerrainAuthority authority =
                    RequireAuthority(first.Mesh, transform);
                Require(!authority.TryValidateCurrent(
                        second.Mesh,
                        in transform,
                        out TerrainAuthorityFailure failure) &&
                        failure == TerrainAuthorityFailure.StaleCache,
                    "A sharedMesh replacement did not stale the cache.");
            }
        }

        private static void VerifyNonFiniteRequest()
        {
            using (var fixture = new MeshFixture(
                       "Authority_NonFinite", 2, 2, 1f, (x, z) => 0f))
            {
                ValidatedContactMeshTerrainAuthority authority =
                    RequireAuthority(fixture.Mesh, IdentityTransform());
                Require(!authority.TrySampleAtXZ(
                        float.NaN,
                        0f,
                        out _,
                        out TerrainAuthorityFailure failure) &&
                        failure == TerrainAuthorityFailure.InvalidRequest,
                    "A non-finite request did not fail closed.");
            }
        }

        private static void VerifyDepthIndependenceAndLegacyBoundary()
        {
            const float legacySurfaceY = -4.056071f;
            using (var fixture = new MeshFixture(
                       "Authority_DepthIndependent",
                       2,
                       2,
                       10f,
                       (x, z) => legacySurfaceY))
            {
                ValidatedContactMeshTerrainAuthority authority =
                    RequireAuthority(fixture.Mesh, IdentityTransform());
                TerrainAuthoritySample baseline = default;
                float[] projectedY = { -1f, 5f, 50f };
                for (int index = 0; index < projectedY.Length; index++)
                {
                    Vector3 projected = new Vector3(
                        2f,
                        projectedY[index],
                        3f);
                    TerrainAuthoritySample current = RequireSample(
                        authority, projected.x, projected.z);
                    if (index == 0)
                    {
                        baseline = current;
                    }
                    else
                    {
                        Require(Vector3.Distance(
                                    baseline.WorldPoint,
                                    current.WorldPoint) <=
                                PositionTolerance &&
                                Vector3.Angle(
                                    baseline.WorldNormal,
                                    current.WorldNormal) <=
                                NormalToleranceDegrees,
                            "Terrain authority changed with projected Y=" +
                            projectedY[index] + ".");
                    }
                }
                Require(Near(
                        baseline.WorldPoint.y,
                        legacySurfaceY,
                        PositionTolerance),
                    "Authority did not resolve terrain at the historical 4.056071m X/Z boundary.");
            }
        }

        private static void CompareAuthorityAndCollider(
            TerrainSurfaceSampler sampler,
            MeshCollider collider)
        {
            int[] xCells =
            {
                0,
                (sampler.AuthorityGridXCount - 2) / 4,
                (sampler.AuthorityGridXCount - 2) / 2,
                (sampler.AuthorityGridXCount - 2) * 3 / 4,
                sampler.AuthorityGridXCount - 2
            };
            int[] zCells =
            {
                0,
                (sampler.AuthorityGridZCount - 2) / 4,
                (sampler.AuthorityGridZCount - 2) / 2,
                (sampler.AuthorityGridZCount - 2) * 3 / 4,
                sampler.AuthorityGridZCount - 2
            };
            Vector2 localMin = sampler.AuthorityLocalMin;
            float spacing = sampler.AuthorityGridSpacing;
            Bounds worldBounds = TransformBounds(
                collider.transform.localToWorldMatrix,
                collider.sharedMesh.bounds);
            foreach (int cellX in xCells.Distinct())
            foreach (int cellZ in zCells.Distinct())
            foreach (Vector2 uv in new[]
                     {
                         new Vector2(0.75f, 0.25f),
                         new Vector2(0.25f, 0.75f)
                     })
            {
                float localX = localMin.x + (cellX + uv.x) * spacing;
                float localZ = localMin.y + (cellZ + uv.y) * spacing;
                float worldX = localX + collider.transform.position.x;
                float worldZ = localZ + collider.transform.position.z;
                Require(sampler.TrySampleAtXZ(
                        worldX,
                        worldZ,
                        out TerrainAuthoritySample authority,
                        out TerrainAuthorityFailure failure),
                    "Authority sample failed: " + failure);
                var ray = new Ray(
                    new Vector3(worldX, worldBounds.max.y + 100f, worldZ),
                    Vector3.down);
                Require(collider.Raycast(
                        ray,
                        out RaycastHit hit,
                        worldBounds.size.y + 200f),
                    "Diagnostic collider ray missed an in-bounds authority sample.");
                Vector3 hitNormal = hit.normal.normalized;
                Require(Mathf.Abs(hit.point.y - authority.WorldPoint.y) <=
                        PositionTolerance,
                    "Authority and collider height differ.");
                Require(Vector3.Angle(
                            hitNormal,
                            authority.WorldNormal) <=
                        NormalToleranceDegrees,
                    "Authority and collider face normals differ.");
                float hitSlope = Vector3.Angle(hitNormal, Vector3.up);
                Require(Mathf.Abs(hitSlope - authority.SlopeDegrees) <=
                        NormalToleranceDegrees,
                    "Authority and collider slopes differ.");
                Vector3 reachableProjected = new Vector3(
                    worldX,
                    authority.WorldPoint.y + 1f,
                    worldZ);
                Require(sampler.TrySample(
                        reachableProjected,
                        2f,
                        5f,
                        out TerrainSurfaceSample legacy,
                        out TerrainSurfaceSampleFailureReason legacyFailure),
                    "Legacy reachable-pose ray failed: " + legacyFailure);
                Require(Mathf.Abs(
                            legacy.Point.y - authority.WorldPoint.y) <=
                        PositionTolerance &&
                        Vector3.Angle(
                            legacy.Normal,
                            authority.WorldNormal) <=
                        NormalToleranceDegrees &&
                        Mathf.Abs(
                            legacy.SlopeDegrees - authority.SlopeDegrees) <=
                        NormalToleranceDegrees,
                    "Legacy reachable-pose ray and authority classification inputs differ.");
            }

            Vector2 localMax = sampler.AuthorityLocalMax;
            Require(sampler.TrySampleAtXZ(
                    localMax.x + collider.transform.position.x,
                    localMax.y + collider.transform.position.z,
                    out _,
                    out TerrainAuthorityFailure maximumFailure),
                "Exact formal max boundary failed: " + maximumFailure);
        }

        private static void VerifyOutsideCoverage(
            TerrainSurfaceSampler sampler,
            Transform terrain)
        {
            Vector2 min = sampler.AuthorityLocalMin;
            Vector2 max = sampler.AuthorityLocalMax;
            float spacing = sampler.AuthorityGridSpacing;
            Vector2[] outside =
            {
                new Vector2(min.x - spacing, min.y),
                new Vector2(max.x + spacing, min.y),
                new Vector2(min.x, min.y - spacing),
                new Vector2(min.x, max.y + spacing)
            };
            foreach (Vector2 value in outside)
            {
                Require(!sampler.TrySampleAtXZ(
                        value.x + terrain.position.x,
                        value.y + terrain.position.z,
                        out _,
                        out TerrainAuthorityFailure failure) &&
                        failure == TerrainAuthorityFailure.OutsideCoverage,
                    "Formal authority did not reject an outside X/Z distinctly.");
            }
        }

        private static void VerifyCurrentRouteCases(
            VehiclePoseDriver driver,
            AuvTerrainClearanceConstraint constraint)
        {
            VehicleDataRuntimeHost host = driver.RuntimeHost;
            bool initializedBefore = host.IsInitialized;
            ActiveRouteSnapshot runtimeActiveBefore = host.ActiveRouteSnapshot;
            DataSourceStatus sourceStatusBefore = host.SourceStatus;
            ulong routeVersionBefore = host.RouteVersion;
            ulong routeEpochBefore = host.RouteEpoch;

            if (runtimeActiveBefore == null)
            {
                Require(!initializedBefore &&
                        sourceStatusBefore == DataSourceStatus.Stopped &&
                        routeVersionBefore == 0UL &&
                        routeEpochBefore == 0UL,
                    "The unavailable Edit Mode runtime route did not belong to a stopped host.");
            }

            ActiveRouteSnapshot active = BuildSerializedFormalRoute(host);
            Require(active != null && active.WaypointCount >= 3,
                "The serialized formal AUV route is unavailable.");
            Require(host.ProfileConfiguration.TryBuildProfile(
                    out CoordinateTransformProfile transformProfile,
                    out string profileError),
                profileError);
            Require(constraint.TryValidateRoute(
                    active,
                    in transformProfile,
                    out string defaultError),
                "Default route rejected: " + defaultError);
            Require(constraint.TryValidateRoute(
                    BuildAdjusted(active, 1, 0.25, false, false),
                    in transformProfile,
                    out string up025Error),
                "index1 +0.25m rejected: " + up025Error);
            Require(constraint.TryValidateRoute(
                    BuildAdjusted(active, 1, 0.50, false, false),
                    in transformProfile,
                    out string up050Error),
                "index1 +0.50m rejected: " + up050Error);
            Require(constraint.TryValidateRoute(
                    BuildAdjusted(active, -1, -1.25, true, false),
                    in transformProfile,
                    out string downError),
                "uniform -1.25m rejected: " + downError);
            Require(!constraint.TryValidateRoute(
                    BuildAdjusted(active, -1, 10000.0, false, true),
                    in transformProfile,
                    out _),
                "A route outside contact terrain was accepted.");

            Require(host.IsInitialized == initializedBefore &&
                    ReferenceEquals(host.ActiveRouteSnapshot, runtimeActiveBefore) &&
                    host.SourceStatus == sourceStatusBefore &&
                    host.RouteVersion == routeVersionBefore &&
                    host.RouteEpoch == routeEpochBefore,
                "Edit Mode route verification changed the runtime host lifecycle.");
        }

        private static ActiveRouteSnapshot BuildSerializedFormalRoute(
            VehicleDataRuntimeHost host)
        {
            Require(host != null && host.IntegrationConfiguration != null,
                "The formal AUV host configuration is unavailable.");
            VehiclePoseIntegrationConfiguration integration =
                host.IntegrationConfiguration;
            Require(integration.VehicleType == VehicleType.Auv,
                "The formal route host is not configured for the AUV.");

            VehicleRouteConfig config = VehicleRouteConfig.Load();
            Require(config != null,
                "VehicleRouteConfig is required for formal route verification.");
            IReadOnlyList<Vector3> offsets = config.GetLocalWaypoints(
                integration.VehicleType);
            Require(offsets != null,
                "The serialized formal AUV waypoints are unavailable.");

            float cruiseSpeed = config.GetCruiseSpeed(
                integration.VehicleType);
            Require(!float.IsNaN(cruiseSpeed) &&
                    !float.IsInfinity(cruiseSpeed) &&
                    cruiseSpeed > 0f,
                "The serialized formal AUV cruise speed is invalid.");

            Vector3 origin = integration.TestOrigin;
            var points = new List<Vector3d>(offsets.Count + 1)
            {
                new Vector3d(origin.x, origin.y, origin.z)
            };
            for (int index = 0; index < offsets.Count; index++)
            {
                Vector3 offset = offsets[index];
                double y = integration.VehicleType == VehicleType.Usv
                    ? origin.y
                    : origin.y + offset.y;
                points.Add(new Vector3d(
                    origin.x + offset.x,
                    y,
                    origin.z + offset.z));
            }

            string routeId = "E3C-" + integration.VehicleType + "-INITIAL";
            VehicleRouteOrientationPolicy orientationPolicy =
                config.GetOrientationPolicy(integration.VehicleType);
            Require(ActiveRouteSnapshotBuilder.TryBuild(
                    integration.VehicleId,
                    integration.VehicleType,
                    routeId,
                    1UL,
                    points,
                    orientationPolicy,
                    0.0,
                    out ActiveRouteSnapshot snapshot,
                    out string error),
                error);
            Require(snapshot.WaypointCount == points.Count &&
                    snapshot.VehicleId == integration.VehicleId &&
                    snapshot.VehicleType == integration.VehicleType &&
                    snapshot.RouteId == routeId &&
                    snapshot.RouteVersion == 1UL &&
                    snapshot.OrientationPolicy == orientationPolicy,
                "The deterministic formal route identity drifted from the runtime contract.");
            for (int index = 0; index < points.Count; index++)
            {
                Vector3d expected = points[index];
                Vector3d actual = snapshot.GetWaypoint(index);
                Require(actual.X == expected.X &&
                        actual.Y == expected.Y &&
                        actual.Z == expected.Z,
                    "The deterministic formal route waypoint order or position drifted.");
            }
            return snapshot;
        }

        private static ActiveRouteSnapshot BuildAdjusted(
            ActiveRouteSnapshot source,
            int selectedIndex,
            double deltaY,
            bool uniformY,
            bool outside)
        {
            var points = new List<Vector3d>(source.WaypointCount);
            for (int index = 0; index < source.WaypointCount; index++)
            {
                Vector3d point = source.GetWaypoint(index);
                if (outside)
                {
                    point = new Vector3d(
                        point.X + 10000.0,
                        point.Y,
                        point.Z + 10000.0);
                }
                else if (uniformY || index == selectedIndex)
                {
                    point = new Vector3d(
                        point.X,
                        point.Y + deltaY,
                        point.Z);
                }
                points.Add(point);
            }
            Require(ActiveRouteSnapshotBuilder.TryBuild(
                    source.VehicleId,
                    source.VehicleType,
                    source.RouteId + "-AUTHORITY-VERIFY",
                    source.RouteVersion + 1000UL,
                    points,
                    source.OrientationPolicy,
                    source.PublishedAtMonotonicSeconds,
                    out ActiveRouteSnapshot result,
                    out string error),
                error);
            return result;
        }

        private static ValidatedContactMeshTerrainAuthority RequireAuthority(
            Mesh mesh,
            TerrainAuthorityTransformSignature transform)
        {
            Require(ValidatedContactMeshTerrainAuthority.TryCreate(
                    mesh,
                    in transform,
                    out ValidatedContactMeshTerrainAuthority authority,
                    out TerrainAuthorityFailure failure),
                "Authority creation failed: " + failure);
            return authority;
        }

        private static TerrainAuthoritySample RequireSample(
            ValidatedContactMeshTerrainAuthority authority,
            float worldX,
            float worldZ)
        {
            Require(authority.TrySampleAtXZ(
                    worldX,
                    worldZ,
                    out TerrainAuthoritySample sample,
                    out TerrainAuthorityFailure failure),
                "Authority query failed: " + failure);
            return sample;
        }

        private static TerrainAuthorityTransformSignature IdentityTransform()
        {
            return new TerrainAuthorityTransformSignature(
                Vector3.zero,
                Quaternion.identity,
                Vector3.one);
        }

        private sealed class MeshFixture : IDisposable
        {
            public MeshFixture(
                string name,
                int xCount,
                int zCount,
                float spacing,
                Func<float, float, float> height,
                Vector3? renderNormal = null)
                : this(
                    name,
                    xCount,
                    zCount,
                    spacing,
                    0f,
                    0f,
                    height,
                    renderNormal)
            {
            }

            public MeshFixture(
                string name,
                int xCount,
                int zCount,
                float spacing,
                float minX,
                float minZ,
                Func<float, float, float> height,
                Vector3? renderNormal = null)
            {
                Vector3[] vertices = BuildVertices(
                    xCount, zCount, spacing, minX, minZ, height);
                Mesh = new Mesh
                {
                    name = name,
                    vertices = vertices,
                    triangles = BuildTriangles(xCount, zCount),
                    uv = vertices
                        .Select(value => new Vector2(value.x, value.z))
                        .ToArray(),
                    normals = Enumerable.Repeat(
                            renderNormal ?? Vector3.up,
                            vertices.Length)
                        .ToArray()
                };
                Mesh.RecalculateBounds();
            }

            public Mesh Mesh { get; }

            public void Dispose()
            {
                if (Mesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(Mesh);
                }
            }
        }

        private static Vector3[] BuildVertices(
            int xCount,
            int zCount,
            float spacing,
            Func<float, float, float> height)
        {
            return BuildVertices(
                xCount,
                zCount,
                spacing,
                0f,
                0f,
                height);
        }

        private static Vector3[] BuildVertices(
            int xCount,
            int zCount,
            float spacing,
            float minX,
            float minZ,
            Func<float, float, float> height)
        {
            var vertices = new Vector3[xCount * zCount];
            for (int x = 0; x < xCount; x++)
            for (int z = 0; z < zCount; z++)
            {
                float px = minX + x * spacing;
                float pz = minZ + z * spacing;
                vertices[x * zCount + z] =
                    new Vector3(px, height(px, pz), pz);
            }
            return vertices;
        }

        private static int[] BuildTriangles(int xCount, int zCount)
        {
            var triangles = new int[(xCount - 1) * (zCount - 1) * 6];
            int cursor = 0;
            for (int x = 0; x < xCount - 1; x++)
            for (int z = 0; z < zCount - 1; z++)
            {
                int a = x * zCount + z;
                int b = (x + 1) * zCount + z;
                int c = (x + 1) * zCount + z + 1;
                int d = x * zCount + z + 1;
                triangles[cursor++] = a;
                triangles[cursor++] = c;
                triangles[cursor++] = b;
                triangles[cursor++] = a;
                triangles[cursor++] = d;
                triangles[cursor++] = c;
            }
            return triangles;
        }

        private static Bounds BoundsFor(Vector3[] vertices)
        {
            var bounds = new Bounds(vertices[0], Vector3.zero);
            for (int index = 1; index < vertices.Length; index++)
            {
                bounds.Encapsulate(vertices[index]);
            }
            return bounds;
        }

        private static Bounds TransformBounds(Matrix4x4 matrix, Bounds local)
        {
            Vector3 center = matrix.MultiplyPoint3x4(local.center);
            Vector3 extents = local.extents;
            Vector3 axisX = matrix.MultiplyVector(
                new Vector3(extents.x, 0f, 0f));
            Vector3 axisY = matrix.MultiplyVector(
                new Vector3(0f, extents.y, 0f));
            Vector3 axisZ = matrix.MultiplyVector(
                new Vector3(0f, 0f, extents.z));
            extents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) +
                    Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) +
                    Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) +
                    Mathf.Abs(axisZ.z));
            return new Bounds(center, extents * 2f);
        }

        private static bool Near(float actual, float expected, float tolerance)
        {
            return Mathf.Abs(actual - expected) <= tolerance;
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
