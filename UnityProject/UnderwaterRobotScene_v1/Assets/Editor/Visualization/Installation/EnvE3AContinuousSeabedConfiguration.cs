using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace UnderwaterRobotScene.EditorTools
{
    internal sealed class EnvE3AContinuousSeabedConfiguration
    {
        private EnvE3AContinuousSeabedConfiguration()
        {
            WaterBounds =
                new EnvE2APlanarBounds(-56f, 56f, -42f, 42f);
            ContactBounds =
                new EnvE2APlanarBounds(-50f, 50f, -36f, 36f);
            CenterShelfBounds =
                new EnvE2APlanarBounds(-6f, 6f, -5f, 5f);
            ActivityBounds =
                new EnvE2APlanarBounds(-10f, 10f, -7f, 7f);
            LeftT1Reserve =
                new EnvE2APlanarBounds(-9f, -1f, -4f, 4f);
            RightT1Reserve =
                new EnvE2APlanarBounds(1f, 9f, -4f, 4f);
            ContactGridSpacingMeters = 0.5f;
            ContactGridXCount =
                DeriveGridCount(ContactBounds.Width, ContactGridSpacingMeters);
            ContactGridZCount =
                DeriveGridCount(ContactBounds.Depth, ContactGridSpacingMeters);
            ContactVertexCount = ContactGridXCount * ContactGridZCount;
            ContactIndexCount =
                (ContactGridXCount - 1) * (ContactGridZCount - 1) * 6;
            ContactPerimeterCount =
                2 * ContactGridXCount + 2 * (ContactGridZCount - 2);
            ContactMeshName = "ENV_E3A_ContactTerrainMesh";
            FarRenderMeshName = "ENV_E3A_FarRenderExtensionMesh";
            WaterDatumY = 0f;
            // The generator's stable bulk contact reference is HoldingTerrainY.
            // Phase 2B translates the complete height function by -5.939 m;
            // relief amplitudes and slopes are intentionally unchanged.
            OuterNominalSeabedDatumY = -13.939f;
            FarTerrainEdgeRiseMinMeters = 2f;
            FarTerrainEdgeRiseMaxMeters = 4f;
            CenterShelfNominalY = -9.099f;
            HoldingTerrainY = -9.339f;
            ShelfTransitionOuterMultiplier = 1.50f;
            MaximumContactSlopeDegrees = 12f;
            TrajectoryObstacleMarginMeters = 2f;
            VehicleRestObstacleMarginMeters = 3f;
            MaximumObstacleRiseMeters = 0.4f;
            MinimumRovClearanceMeters = 0f;
            MaximumRovClearanceMeters = 0.03f;
            TargetRovClearanceMeters = 0.015f;
            FarRingCount = 3;
            FarMidHalfExtentX = 60f;
            FarMidHalfExtentZ = 45f;
            FarOuterHalfExtentX = 180f;
            FarOuterHalfExtentZ = 140f;
            FarVertexCount = ContactPerimeterCount * FarRingCount;
            FarIndexCount = ContactPerimeterCount *
                (FarRingCount - 1) * 6;
            FarReliefMaximumMeters = 0.12f;
        }

        internal static EnvE3AContinuousSeabedConfiguration CreateApproved()
        {
            var configuration =
                new EnvE3AContinuousSeabedConfiguration();
            configuration.Validate();
            return configuration;
        }

        internal EnvE2APlanarBounds WaterBounds { get; }
        internal EnvE2APlanarBounds ContactBounds { get; }
        internal EnvE2APlanarBounds CenterShelfBounds { get; }
        internal EnvE2APlanarBounds ActivityBounds { get; }
        internal EnvE2APlanarBounds LeftT1Reserve { get; }
        internal EnvE2APlanarBounds RightT1Reserve { get; }
        internal int ContactGridXCount { get; }
        internal int ContactGridZCount { get; }
        internal float ContactGridSpacingMeters { get; }
        internal int ContactVertexCount { get; }
        internal int ContactIndexCount { get; }
        internal int ContactPerimeterCount { get; }
        internal string ContactMeshName { get; }
        internal string FarRenderMeshName { get; }
        internal float WaterDatumY { get; }
        internal float OuterNominalSeabedDatumY { get; }
        internal float FarTerrainEdgeRiseMinMeters { get; }
        internal float FarTerrainEdgeRiseMaxMeters { get; }
        internal float CenterShelfNominalY { get; }
        internal float HoldingTerrainY { get; }
        internal float ShelfTransitionOuterMultiplier { get; }
        internal float MaximumContactSlopeDegrees { get; }
        internal float TrajectoryObstacleMarginMeters { get; }
        internal float VehicleRestObstacleMarginMeters { get; }
        internal float MaximumObstacleRiseMeters { get; }
        internal float MinimumRovClearanceMeters { get; }
        internal float MaximumRovClearanceMeters { get; }
        internal float TargetRovClearanceMeters { get; }
        internal int FarRingCount { get; }
        internal float FarMidHalfExtentX { get; }
        internal float FarMidHalfExtentZ { get; }
        internal float FarOuterHalfExtentX { get; }
        internal float FarOuterHalfExtentZ { get; }
        internal int FarVertexCount { get; }
        internal int FarIndexCount { get; }
        internal float FarReliefMaximumMeters { get; }

        internal string CanonicalJson()
        {
            var builder = new StringBuilder(1536);
            builder.Append('{');
            AppendBounds(builder, "WaterBounds", WaterBounds, false);
            AppendBounds(builder, "ContactBounds", ContactBounds, true);
            AppendBounds(builder, "CenterShelfBounds", CenterShelfBounds, true);
            AppendBounds(builder, "ActivityBounds", ActivityBounds, true);
            AppendBounds(builder, "LeftT1Reserve", LeftT1Reserve, true);
            AppendBounds(builder, "RightT1Reserve", RightT1Reserve, true);
            AppendInt(builder, "ContactGridXCount", ContactGridXCount);
            AppendInt(builder, "ContactGridZCount", ContactGridZCount);
            AppendFloat(builder, "ContactGridSpacingMeters",
                ContactGridSpacingMeters);
            AppendInt(builder, "ContactVertexCount", ContactVertexCount);
            AppendInt(builder, "ContactIndexCount", ContactIndexCount);
            AppendInt(builder, "ContactPerimeterCount", ContactPerimeterCount);
            AppendString(builder, "ContactMeshName", ContactMeshName);
            AppendString(builder, "FarRenderMeshName", FarRenderMeshName);
            AppendFloat(builder, "WaterDatumY", WaterDatumY);
            AppendFloat(builder, "OuterNominalSeabedDatumY",
                OuterNominalSeabedDatumY);
            AppendFloat(builder, "FarTerrainEdgeRiseMinMeters",
                FarTerrainEdgeRiseMinMeters);
            AppendFloat(builder, "FarTerrainEdgeRiseMaxMeters",
                FarTerrainEdgeRiseMaxMeters);
            AppendFloat(builder, "CenterShelfNominalY", CenterShelfNominalY);
            AppendFloat(builder, "HoldingTerrainY", HoldingTerrainY);
            AppendFloat(builder, "ShelfTransitionOuterMultiplier",
                ShelfTransitionOuterMultiplier);
            AppendFloat(builder, "MaximumContactSlopeDegrees",
                MaximumContactSlopeDegrees);
            AppendFloat(builder, "TrajectoryObstacleMarginMeters",
                TrajectoryObstacleMarginMeters);
            AppendFloat(builder, "VehicleRestObstacleMarginMeters",
                VehicleRestObstacleMarginMeters);
            AppendFloat(builder, "MaximumObstacleRiseMeters",
                MaximumObstacleRiseMeters);
            AppendFloat(builder, "MinimumRovClearanceMeters",
                MinimumRovClearanceMeters);
            AppendFloat(builder, "MaximumRovClearanceMeters",
                MaximumRovClearanceMeters);
            AppendFloat(builder, "TargetRovClearanceMeters",
                TargetRovClearanceMeters);
            AppendInt(builder, "FarRingCount", FarRingCount);
            AppendFloat(builder, "FarMidHalfExtentX", FarMidHalfExtentX);
            AppendFloat(builder, "FarMidHalfExtentZ", FarMidHalfExtentZ);
            AppendFloat(builder, "FarOuterHalfExtentX", FarOuterHalfExtentX);
            AppendFloat(builder, "FarOuterHalfExtentZ", FarOuterHalfExtentZ);
            AppendInt(builder, "FarVertexCount", FarVertexCount);
            AppendInt(builder, "FarIndexCount", FarIndexCount);
            AppendFloat(builder, "FarReliefMaximumMeters",
                FarReliefMaximumMeters);
            builder.Append('}');
            return builder.ToString();
        }

        internal string Sha256()
        {
            byte[] bytes =
                new UTF8Encoding(false).GetBytes(CanonicalJson());
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        internal void Validate()
        {
            ValidateBounds(WaterBounds, -56f, 56f, -42f, 42f,
                "WaterBounds");
            ValidateBounds(ContactBounds, -50f, 50f, -36f, 36f,
                "ContactBounds");
            ValidateBounds(CenterShelfBounds, -6f, 6f, -5f, 5f,
                "CenterShelfBounds");
            ValidateBounds(ActivityBounds, -10f, 10f, -7f, 7f,
                "ActivityBounds");
            ValidateBounds(LeftT1Reserve, -9f, -1f, -4f, 4f,
                "LeftT1Reserve");
            ValidateBounds(RightT1Reserve, 1f, 9f, -4f, 4f,
                "RightT1Reserve");
            RequireExact(ContactGridSpacingMeters, 0.5f,
                "ContactGridSpacingMeters");
            Require(ContactGridXCount ==
                    DeriveGridCount(ContactBounds.Width,
                        ContactGridSpacingMeters) &&
                    ContactGridZCount ==
                    DeriveGridCount(ContactBounds.Depth,
                        ContactGridSpacingMeters),
                "Contact grid dimensions are not derived from bounds.");
            Require(ContactVertexCount ==
                    ContactGridXCount * ContactGridZCount,
                "Contact vertex count is inconsistent.");
            Require(ContactIndexCount ==
                    (ContactGridXCount - 1) *
                    (ContactGridZCount - 1) * 6,
                "Contact index count is inconsistent.");
            Require(ContactPerimeterCount ==
                    2 * ContactGridXCount +
                    2 * (ContactGridZCount - 2),
                "Contact perimeter count is inconsistent.");
            Require(string.Equals(
                    ContactMeshName,
                    "ENV_E3A_ContactTerrainMesh",
                    StringComparison.Ordinal) &&
                string.Equals(
                    FarRenderMeshName,
                    "ENV_E3A_FarRenderExtensionMesh",
                    StringComparison.Ordinal),
                "Terrain mesh names drifted.");
            RequireExact(CenterShelfNominalY, -9.099f,
                "CenterShelfNominalY");
            RequireExact(HoldingTerrainY, -9.339f, "HoldingTerrainY");
            RequireExact(ShelfTransitionOuterMultiplier, 1.50f,
                "ShelfTransitionOuterMultiplier");
            RequireExact(MaximumContactSlopeDegrees, 12f,
                "MaximumContactSlopeDegrees");
            RequireExact(WaterDatumY, 0f, "WaterDatumY");
            RequireExact(OuterNominalSeabedDatumY, -13.939f,
                "OuterNominalSeabedDatumY");
            RequireExact(FarTerrainEdgeRiseMinMeters, 2f,
                "FarTerrainEdgeRiseMinMeters");
            RequireExact(FarTerrainEdgeRiseMaxMeters, 4f,
                "FarTerrainEdgeRiseMaxMeters");
            RequireExact(TrajectoryObstacleMarginMeters, 2f,
                "TrajectoryObstacleMarginMeters");
            RequireExact(VehicleRestObstacleMarginMeters, 3f,
                "VehicleRestObstacleMarginMeters");
            RequireExact(MaximumObstacleRiseMeters, 0.4f,
                "MaximumObstacleRiseMeters");
            RequireExact(MinimumRovClearanceMeters, 0f,
                "MinimumRovClearanceMeters");
            RequireExact(MaximumRovClearanceMeters, 0.03f,
                "MaximumRovClearanceMeters");
            RequireExact(TargetRovClearanceMeters, 0.015f,
                "TargetRovClearanceMeters");
            Require(FarRingCount == 3,
                "Approved far ring count drifted.");
            Require(FarRingCount >= 2 &&
                    FarVertexCount == ContactPerimeterCount * FarRingCount &&
                    FarIndexCount == ContactPerimeterCount *
                    (FarRingCount - 1) * 6,
                "Reserved far mesh counts are inconsistent.");
            RequireExact(FarMidHalfExtentX, 60f, "FarMidHalfExtentX");
            RequireExact(FarMidHalfExtentZ, 45f, "FarMidHalfExtentZ");
            RequireExact(FarOuterHalfExtentX, 180f,
                "FarOuterHalfExtentX");
            RequireExact(FarOuterHalfExtentZ, 140f,
                "FarOuterHalfExtentZ");
            RequireExact(FarReliefMaximumMeters, 0.12f,
                "FarReliefMaximumMeters");
            Require(MinimumRovClearanceMeters <=
                    TargetRovClearanceMeters &&
                    TargetRovClearanceMeters <=
                    MaximumRovClearanceMeters,
                "ROV clearance target is outside its interval.");
        }

        private static void AppendBounds(
            StringBuilder builder,
            string key,
            EnvE2APlanarBounds bounds,
            bool comma)
        {
            if (comma)
            {
                builder.Append(',');
            }

            builder.Append('"').Append(key).Append("\":{");
            builder.Append("\"MinX\":").Append(Format(bounds.MinX));
            builder.Append(",\"MaxX\":").Append(Format(bounds.MaxX));
            builder.Append(",\"MinZ\":").Append(Format(bounds.MinZ));
            builder.Append(",\"MaxZ\":").Append(Format(bounds.MaxZ));
            builder.Append('}');
        }

        private static void AppendFloat(
            StringBuilder builder,
            string key,
            float value)
        {
            builder.Append(",\"").Append(key).Append("\":")
                .Append(Format(value));
        }

        private static void AppendInt(
            StringBuilder builder,
            string key,
            int value)
        {
            builder.Append(",\"").Append(key).Append("\":")
                .Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendString(
            StringBuilder builder,
            string key,
            string value)
        {
            builder.Append(",\"").Append(key).Append("\":\"")
                .Append(value).Append('"');
        }

        private static string Format(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static void ValidateBounds(
            EnvE2APlanarBounds bounds,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            string label)
        {
            Require(bounds.MinX < bounds.MaxX &&
                    bounds.MinZ < bounds.MaxZ,
                label + " is inverted.");
            RequireExact(bounds.MinX, minX, label + ".MinX");
            RequireExact(bounds.MaxX, maxX, label + ".MaxX");
            RequireExact(bounds.MinZ, minZ, label + ".MinZ");
            RequireExact(bounds.MaxZ, maxZ, label + ".MaxZ");
        }

        private static int DeriveGridCount(
            float extent,
            float spacing)
        {
            Require(extent > 0f && spacing > 0f,
                "Grid extent and spacing must be positive.");
            double intervals = extent / spacing;
            int roundedIntervals = (int)Math.Round(
                intervals,
                MidpointRounding.AwayFromZero);
            Require(Math.Abs(intervals - roundedIntervals) <= 0.00001,
                "Grid extent must be an integer multiple of spacing.");
            return roundedIntervals + 1;
        }

        private static void RequireExact(
            float actual,
            float expected,
            string label)
        {
            Require(!float.IsNaN(actual) && !float.IsInfinity(actual),
                label + " must be finite.");
            Require(Math.Abs(actual - expected) <= 0.00001f,
                label + " drifted.");
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
