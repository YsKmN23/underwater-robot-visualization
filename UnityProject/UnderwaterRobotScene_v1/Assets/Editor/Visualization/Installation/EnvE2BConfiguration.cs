using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace UnderwaterRobotScene.EditorTools
{
    internal sealed class EnvE2BConfiguration
    {
        private EnvE2BConfiguration(
            EnvE3AContinuousSeabedConfiguration continuousSeabed)
        {
            ContinuousSeabedConfiguration = continuousSeabed ??
                throw new ArgumentNullException(nameof(continuousSeabed));
        }

        internal const string EnvironmentRootName = "ENV_E2_Environment";
        internal const string RootName = "E2B_DistantEnvironment";
        internal const string MeshObjectName = "Continuous_Enclosure";
        [Obsolete("ENV-E3A owns legacy removal; this compatibility view is empty.")]
        internal static readonly string[] LegacyBoundaryNames =
            Array.Empty<string>();

        internal static int SegmentCount =>
            ApprovedAuthority().ContactPerimeterCount;
        internal static float InnerRadiusX =>
            ApprovedAuthority().ContactBounds.Width * 0.5f;
        internal static float InnerRadiusZ =>
            ApprovedAuthority().ContactBounds.Depth * 0.5f;
        internal static float OuterRadiusX =>
            ApprovedAuthority().FarMidHalfExtentX;
        internal static float OuterRadiusZ =>
            ApprovedAuthority().FarMidHalfExtentZ;
        internal static float CrestRadiusX =>
            ApprovedAuthority().FarOuterHalfExtentX;
        internal static float CrestRadiusZ =>
            ApprovedAuthority().FarOuterHalfExtentZ;
        internal static float HeightVariation =>
            ApprovedAuthority().FarReliefMaximumMeters;
        internal static string MeshName =>
            ApprovedAuthority().FarRenderMeshName;

        internal EnvE3AContinuousSeabedConfiguration
            ContinuousSeabedConfiguration { get; }

        internal static EnvE2BConfiguration CreateApproved()
        {
            var configuration = new EnvE2BConfiguration(
                ApprovedAuthority());
            configuration.Validate();
            return configuration;
        }

        internal string CanonicalJson()
        {
            EnvE3AContinuousSeabedConfiguration authority =
                ContinuousSeabedConfiguration;
            return "{" +
                   "\"schema\":\"ENV-E2B-E3A-Projection-v2\"," +
                   "\"authoritySha256\":\"" + authority.Sha256() +
                   "\"," +
                   "\"perimeterSamples\":" + SegmentCount + "," +
                   "\"ringCount\":" + authority.FarRingCount + "," +
                   "\"midHalfExtentX\":" +
                   Number(authority.FarMidHalfExtentX) + "," +
                   "\"midHalfExtentZ\":" +
                   Number(authority.FarMidHalfExtentZ) + "," +
                   "\"outerHalfExtentX\":" +
                   Number(authority.FarOuterHalfExtentX) + "," +
                   "\"outerHalfExtentZ\":" +
                   Number(authority.FarOuterHalfExtentZ) + "," +
                   "\"reliefMaximumMeters\":" +
                   Number(authority.FarReliefMaximumMeters) + "," +
                   "\"vertexCount\":" + authority.FarVertexCount + "," +
                   "\"indexCount\":" + authority.FarIndexCount + "," +
                   "\"rendererCount\":1," +
                   "\"colliderCount\":0," +
                   "\"materialAuthority\":\"/Seabed\"" +
                   "}";
        }

        internal string Sha256()
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(CanonicalJson());
                return BitConverter.ToString(sha.ComputeHash(bytes))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        internal void Validate()
        {
            EnvE3AContinuousSeabedConfiguration authority =
                ContinuousSeabedConfiguration;
            authority.Validate();
            Require(SegmentCount == authority.ContactPerimeterCount &&
                    authority.FarRingCount == 3 &&
                    authority.FarVertexCount == SegmentCount * 3 &&
                    authority.FarIndexCount == SegmentCount * 12,
                "Far ring/count projection drifted.");
            Require(OuterRadiusX == authority.FarMidHalfExtentX &&
                    OuterRadiusZ == authority.FarMidHalfExtentZ &&
                    CrestRadiusX == authority.FarOuterHalfExtentX &&
                    CrestRadiusZ == authority.FarOuterHalfExtentZ,
                "Far extent projection drifted.");
            Require(HeightVariation ==
                    authority.FarReliefMaximumMeters,
                "Far relief projection drifted.");
        }

        private static EnvE3AContinuousSeabedConfiguration
            ApprovedAuthority()
        {
            return EnvE3AContinuousSeabedConfiguration.CreateApproved();
        }

        private static string Number(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
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
