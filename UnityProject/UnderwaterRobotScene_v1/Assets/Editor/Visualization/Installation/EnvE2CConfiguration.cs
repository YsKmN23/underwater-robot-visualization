using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnderwaterRobotScene.EditorTools
{
    [Serializable]
    internal sealed class EnvE2CProfile
    {
        internal const string ApprovedId = "B";
        internal const string ApprovedDisplayName = "Candidate B";
        internal const string ApprovedDirection = "Balanced Underwater";

        internal bool FogEnabled;
        internal FogMode FogMode;
        internal Color FogColor;
        internal float FogDensity;
        internal float FogStartDistance;
        internal float FogEndDistance;
        internal AmbientMode AmbientMode;
        internal Color AmbientColor;
        internal float AmbientIntensity;
        internal float ReflectionIntensity;
        internal Color DirectionalLightColor;
        internal float DirectionalLightIntensity;
        internal float DirectionalLightShadowStrength;
        internal Color WaterColor;
        internal float WaterSmoothness;
        internal Color SeabedColor;
        internal float SeabedSmoothness;

        internal static EnvE2CProfile CreateApproved()
        {
            return new EnvE2CProfile
            {
                FogEnabled = true,
                FogMode = FogMode.ExponentialSquared,
                FogColor = new Color(0.055f, 0.20f, 0.27f, 1f),
                FogDensity = 0.0080f,
                FogStartDistance = 0f,
                FogEndDistance = 120f,
                AmbientMode = AmbientMode.Flat,
                AmbientColor = new Color(0.10f, 0.23f, 0.28f, 1f),
                AmbientIntensity = 0.72f,
                ReflectionIntensity = 0.58f,
                DirectionalLightColor =
                    new Color(0.70f, 0.84f, 0.96f, 1f),
                DirectionalLightIntensity = 1.00f,
                DirectionalLightShadowStrength = 0.78f,
                WaterColor = new Color(0.00f, 0.26f, 0.40f, 0.20f),
                WaterSmoothness = 0.55f,
                SeabedColor = new Color(0.28f, 0.29f, 0.23f, 1f),
                SeabedSmoothness = 0.25f
            };
        }

        internal string Sha256()
        {
            string value = string.Join("|", new[]
            {
                ApprovedId,
                ApprovedDisplayName,
                ApprovedDirection,
                FogEnabled.ToString(),
                FogMode.ToString(),
                ColorValue(FogColor),
                Number(FogDensity),
                Number(FogStartDistance),
                Number(FogEndDistance),
                AmbientMode.ToString(),
                ColorValue(AmbientColor),
                Number(AmbientIntensity),
                Number(ReflectionIntensity),
                ColorValue(DirectionalLightColor),
                Number(DirectionalLightIntensity),
                Number(DirectionalLightShadowStrength),
                ColorValue(WaterColor),
                Number(WaterSmoothness),
                ColorValue(SeabedColor),
                Number(SeabedSmoothness)
            });
            using (SHA256 sha = SHA256.Create())
            {
                return string.Concat(sha.ComputeHash(
                        new UTF8Encoding(false).GetBytes(value))
                    .Select(item => item.ToString("x2")));
            }
        }

        private static string ColorValue(Color value)
        {
            return Number(value.r) + "," + Number(value.g) + "," +
                   Number(value.b) + "," + Number(value.a);
        }

        private static string Number(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
