using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace UnderwaterRobotScene.EditorTools
{
    internal readonly struct EnvE2APlanarBounds
    {
        internal EnvE2APlanarBounds(
            float minX,
            float maxX,
            float minZ,
            float maxZ)
        {
            MinX = minX;
            MaxX = maxX;
            MinZ = minZ;
            MaxZ = maxZ;
        }

        internal float MinX { get; }
        internal float MaxX { get; }
        internal float MinZ { get; }
        internal float MaxZ { get; }
        internal float Width => MaxX - MinX;
        internal float Depth => MaxZ - MinZ;

        internal bool Contains(float x, float z)
        {
            return x >= MinX && x <= MaxX &&
                z >= MinZ && z <= MaxZ;
        }
    }

    internal sealed class EnvE2AConfiguration
    {
        private EnvE2AConfiguration(
            EnvE3AContinuousSeabedConfiguration authority)
        {
            ContinuousSeabedConfiguration = authority ??
                throw new ArgumentNullException(nameof(authority));
            WaterBounds = authority.WaterBounds;
            SeabedBounds = authority.ContactBounds;
            CenterShelfBounds = authority.CenterShelfBounds;
            ActivityBounds = authority.ActivityBounds;
            LeftT1Reserve = authority.LeftT1Reserve;
            RightT1Reserve = authority.RightT1Reserve;
            WaterDatumY = authority.WaterDatumY;
            OuterNominalSeabedDatumY = authority.OuterNominalSeabedDatumY;
            FarTerrainEdgeRiseMinMeters =
                authority.FarTerrainEdgeRiseMinMeters;
            FarTerrainEdgeRiseMaxMeters =
                authority.FarTerrainEdgeRiseMaxMeters;
            CenterShelfNominalY = authority.CenterShelfNominalY;
            HoldingTerrainY = authority.HoldingTerrainY;
            ShelfTransitionOuterMultiplier =
                authority.ShelfTransitionOuterMultiplier;
            MaximumCorridorSlopeDegrees =
                authority.MaximumContactSlopeDegrees;
            TrajectoryObstacleMarginMeters =
                authority.TrajectoryObstacleMarginMeters;
            VehicleRestObstacleMarginMeters =
                authority.VehicleRestObstacleMarginMeters;
            MaximumObstacleRiseMeters = authority.MaximumObstacleRiseMeters;
            MinimumRovClearanceMeters =
                authority.MinimumRovClearanceMeters;
            MaximumRovClearanceMeters =
                authority.MaximumRovClearanceMeters;
            TargetRovClearanceMeters = authority.TargetRovClearanceMeters;
        }

        internal static EnvE2AConfiguration CreateApproved()
        {
            var configuration = new EnvE2AConfiguration(
                EnvE3AContinuousSeabedConfiguration.CreateApproved());
            configuration.Validate();
            return configuration;
        }

        internal EnvE2APlanarBounds WaterBounds { get; }
        internal EnvE2APlanarBounds SeabedBounds { get; }
        internal EnvE2APlanarBounds CenterShelfBounds { get; }
        internal EnvE2APlanarBounds ActivityBounds { get; }
        internal EnvE2APlanarBounds LeftT1Reserve { get; }
        internal EnvE2APlanarBounds RightT1Reserve { get; }
        internal float WaterDatumY { get; }
        internal float OuterNominalSeabedDatumY { get; }
        internal float FarTerrainEdgeRiseMinMeters { get; }
        internal float FarTerrainEdgeRiseMaxMeters { get; }
        internal float CenterShelfNominalY { get; }
        internal float HoldingTerrainY { get; }
        internal float ShelfTransitionOuterMultiplier { get; }
        internal float MaximumCorridorSlopeDegrees { get; }
        internal float TrajectoryObstacleMarginMeters { get; }
        internal float VehicleRestObstacleMarginMeters { get; }
        internal float MaximumObstacleRiseMeters { get; }
        internal float MinimumRovClearanceMeters { get; }
        internal float MaximumRovClearanceMeters { get; }
        internal float TargetRovClearanceMeters { get; }
        internal EnvE3AContinuousSeabedConfiguration
            ContinuousSeabedConfiguration { get; }

        internal string CanonicalJson()
        {
            var builder = new StringBuilder(1024);
            builder.Append('{');
            AppendBounds(builder, "WaterBounds", WaterBounds);
            builder.Append(',');
            AppendBounds(builder, "SeabedBounds", SeabedBounds);
            builder.Append(',');
            AppendBounds(builder, "CenterShelfBounds", CenterShelfBounds);
            builder.Append(',');
            AppendBounds(builder, "ActivityBounds", ActivityBounds);
            builder.Append(',');
            AppendBounds(builder, "LeftT1Reserve", LeftT1Reserve);
            builder.Append(',');
            AppendBounds(builder, "RightT1Reserve", RightT1Reserve);
            AppendFloat(builder, "WaterDatumY", WaterDatumY);
            AppendFloat(builder, "OuterNominalSeabedDatumY",
                OuterNominalSeabedDatumY);
            AppendFloat(builder, "FarTerrainEdgeRiseMinMeters",
                FarTerrainEdgeRiseMinMeters);
            AppendFloat(builder, "FarTerrainEdgeRiseMaxMeters",
                FarTerrainEdgeRiseMaxMeters);
            AppendFloat(builder, "CenterShelfNominalY",
                CenterShelfNominalY);
            AppendFloat(builder, "HoldingTerrainY", HoldingTerrainY);
            AppendFloat(builder, "ShelfTransitionOuterMultiplier",
                ShelfTransitionOuterMultiplier);
            AppendFloat(builder, "MaximumCorridorSlopeDegrees",
                MaximumCorridorSlopeDegrees);
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
            ContinuousSeabedConfiguration.Validate();
            Require(SameBounds(
                    WaterBounds,
                    ContinuousSeabedConfiguration.WaterBounds) &&
                SameBounds(
                    SeabedBounds,
                    ContinuousSeabedConfiguration.ContactBounds) &&
                SameBounds(
                    CenterShelfBounds,
                    ContinuousSeabedConfiguration.CenterShelfBounds) &&
                SameBounds(
                    ActivityBounds,
                    ContinuousSeabedConfiguration.ActivityBounds) &&
                SameBounds(
                    LeftT1Reserve,
                    ContinuousSeabedConfiguration.LeftT1Reserve) &&
                SameBounds(
                    RightT1Reserve,
                    ContinuousSeabedConfiguration.RightT1Reserve),
                "ENV-E2A bounds do not project the E3A authority.");
            Require(WaterDatumY ==
                    ContinuousSeabedConfiguration.WaterDatumY &&
                OuterNominalSeabedDatumY ==
                    ContinuousSeabedConfiguration.OuterNominalSeabedDatumY &&
                FarTerrainEdgeRiseMinMeters ==
                    ContinuousSeabedConfiguration.FarTerrainEdgeRiseMinMeters &&
                FarTerrainEdgeRiseMaxMeters ==
                    ContinuousSeabedConfiguration.FarTerrainEdgeRiseMaxMeters &&
                CenterShelfNominalY ==
                    ContinuousSeabedConfiguration.CenterShelfNominalY &&
                HoldingTerrainY ==
                    ContinuousSeabedConfiguration.HoldingTerrainY &&
                ShelfTransitionOuterMultiplier ==
                    ContinuousSeabedConfiguration.ShelfTransitionOuterMultiplier &&
                MaximumCorridorSlopeDegrees ==
                    ContinuousSeabedConfiguration.MaximumContactSlopeDegrees &&
                TrajectoryObstacleMarginMeters ==
                    ContinuousSeabedConfiguration.TrajectoryObstacleMarginMeters &&
                VehicleRestObstacleMarginMeters ==
                    ContinuousSeabedConfiguration.VehicleRestObstacleMarginMeters &&
                MaximumObstacleRiseMeters ==
                    ContinuousSeabedConfiguration.MaximumObstacleRiseMeters &&
                MinimumRovClearanceMeters ==
                    ContinuousSeabedConfiguration.MinimumRovClearanceMeters &&
                MaximumRovClearanceMeters ==
                    ContinuousSeabedConfiguration.MaximumRovClearanceMeters &&
                TargetRovClearanceMeters ==
                    ContinuousSeabedConfiguration.TargetRovClearanceMeters,
                "ENV-E2A scalar values do not project the E3A authority.");
        }

        private static bool SameBounds(
            EnvE2APlanarBounds left,
            EnvE2APlanarBounds right)
        {
            return left.MinX == right.MinX && left.MaxX == right.MaxX &&
                left.MinZ == right.MinZ && left.MaxZ == right.MaxZ;
        }

        private static void AppendBounds(
            StringBuilder builder,
            string key,
            EnvE2APlanarBounds bounds)
        {
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

        private static string Format(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static void ValidateBounds(
            EnvE2APlanarBounds bounds,
            float width,
            float depth,
            string label)
        {
            RequireFinite(bounds.MinX, label + ".MinX");
            RequireFinite(bounds.MaxX, label + ".MaxX");
            RequireFinite(bounds.MinZ, label + ".MinZ");
            RequireFinite(bounds.MaxZ, label + ".MaxZ");
            Require(bounds.MinX < bounds.MaxX &&
                    bounds.MinZ < bounds.MaxZ,
                label + " is inverted.");
            RequireExact(bounds.Width, width, label + ".Width");
            RequireExact(bounds.Depth, depth, label + ".Depth");
        }

        private static void RequireExact(
            float actual,
            float expected,
            string label)
        {
            RequireFinite(actual, label);
            Require(Math.Abs(actual - expected) <= 0.00001f,
                label + " expected " +
                expected.ToString("R", CultureInfo.InvariantCulture) +
                " but was " +
                actual.ToString("R", CultureInfo.InvariantCulture) + ".");
        }

        private static void RequireFinite(float value, string label)
        {
            Require(!float.IsNaN(value) && !float.IsInfinity(value),
                label + " must be finite.");
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
