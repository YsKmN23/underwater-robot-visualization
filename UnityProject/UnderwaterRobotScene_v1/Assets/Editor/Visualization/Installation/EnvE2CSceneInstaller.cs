using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderwaterRobotScene.EditorTools
{
    internal sealed class EnvE2CInstallResult
    {
        internal bool Changed;
        internal string DirectionalLightName;
        internal string WaterMaterialName;
        internal string SeabedMaterialName;
    }

    internal static class EnvE2CSceneInstaller
    {
        private const float Epsilon = 0.00001f;

        internal static EnvE2CInstallResult Apply(
            Scene scene,
            EnvE2CProfile profile)
        {
            Require(scene.IsValid() && scene.isLoaded,
                "A valid loaded Scene is required.");
            Require(profile != null, "E2C profile is null.");

            Light mainLight = AllComponents<Light>(scene)
                .Single(light => light.type == LightType.Directional);
            Renderer waterRenderer = RequireRoot(scene, "Water_Surface")
                .GetComponent<Renderer>();
            Renderer seabedRenderer = RequireRoot(scene, "Seabed")
                .GetComponent<Renderer>();
            GameObject environment = RequireRoot(scene, "ENV_E2_Environment");
            Transform distantRoot = environment.transform.Cast<Transform>()
                .Single(child => child.name == "E2B_DistantEnvironment");
            Renderer distantRenderer =
                distantRoot.GetComponentInChildren<Renderer>(true);

            Require(waterRenderer != null && seabedRenderer != null &&
                    distantRenderer != null,
                "Required visual Renderer is missing.");
            Material water = waterRenderer.sharedMaterial;
            Material seabed = seabedRenderer.sharedMaterial;
            Require(water != null && seabed != null &&
                    ReferenceEquals(seabed, distantRenderer.sharedMaterial),
                "Expected shared Scene-local seabed/distant material binding.");
            Require(string.IsNullOrEmpty(AssetDatabase.GetAssetPath(water)) &&
                    string.IsNullOrEmpty(AssetDatabase.GetAssetPath(seabed)),
                "E2C may only modify Scene-local material instances.");
            Require(water.HasProperty("_Color") &&
                    water.HasProperty("_Glossiness") &&
                    seabed.HasProperty("_Color") &&
                    seabed.HasProperty("_Glossiness"),
                "Current material shader lacks required exposed properties.");

            bool changed = false;
            changed |= Set(ref changed,
                RenderSettings.fog != profile.FogEnabled,
                () => RenderSettings.fog = profile.FogEnabled);
            changed |= Set(ref changed,
                RenderSettings.fogMode != profile.FogMode,
                () => RenderSettings.fogMode = profile.FogMode);
            changed |= Set(ref changed,
                !ColorEqual(RenderSettings.fogColor, profile.FogColor),
                () => RenderSettings.fogColor = profile.FogColor);
            changed |= Set(ref changed,
                !FloatEqual(RenderSettings.fogDensity, profile.FogDensity),
                () => RenderSettings.fogDensity = profile.FogDensity);
            changed |= Set(ref changed,
                !FloatEqual(RenderSettings.fogStartDistance,
                    profile.FogStartDistance),
                () => RenderSettings.fogStartDistance =
                    profile.FogStartDistance);
            changed |= Set(ref changed,
                !FloatEqual(RenderSettings.fogEndDistance,
                    profile.FogEndDistance),
                () => RenderSettings.fogEndDistance =
                    profile.FogEndDistance);
            changed |= Set(ref changed,
                RenderSettings.ambientMode != profile.AmbientMode,
                () => RenderSettings.ambientMode = profile.AmbientMode);
            changed |= Set(ref changed,
                !ColorEqual(RenderSettings.ambientLight,
                    profile.AmbientColor),
                () => RenderSettings.ambientLight = profile.AmbientColor);
            changed |= Set(ref changed,
                !FloatEqual(RenderSettings.ambientIntensity,
                    profile.AmbientIntensity),
                () => RenderSettings.ambientIntensity =
                    profile.AmbientIntensity);
            changed |= Set(ref changed,
                !FloatEqual(RenderSettings.reflectionIntensity,
                    profile.ReflectionIntensity),
                () => RenderSettings.reflectionIntensity =
                    profile.ReflectionIntensity);

            changed |= Set(ref changed,
                !ColorEqual(mainLight.color,
                    profile.DirectionalLightColor),
                () => mainLight.color = profile.DirectionalLightColor);
            changed |= Set(ref changed,
                !FloatEqual(mainLight.intensity,
                    profile.DirectionalLightIntensity),
                () => mainLight.intensity =
                    profile.DirectionalLightIntensity);
            changed |= Set(ref changed,
                !FloatEqual(mainLight.shadowStrength,
                    profile.DirectionalLightShadowStrength),
                () => mainLight.shadowStrength =
                    profile.DirectionalLightShadowStrength);

            changed |= SetMaterialColor(
                water, "_Color", profile.WaterColor);
            changed |= SetMaterialFloat(
                water, "_Glossiness", profile.WaterSmoothness);
            changed |= SetMaterialColor(
                seabed, "_Color", profile.SeabedColor);
            changed |= SetMaterialFloat(
                seabed, "_Glossiness", profile.SeabedSmoothness);

            if (changed)
            {
                EditorUtility.SetDirty(mainLight);
                EditorUtility.SetDirty(water);
                EditorUtility.SetDirty(seabed);
                EditorSceneManager.MarkSceneDirty(scene);
            }

            return new EnvE2CInstallResult
            {
                Changed = changed,
                DirectionalLightName = mainLight.gameObject.name,
                WaterMaterialName = water.name,
                SeabedMaterialName = seabed.name
            };
        }

        internal static GameObject RequireRoot(Scene scene, string name)
        {
            GameObject[] matches = scene.GetRootGameObjects()
                .Where(root => root.name == name).ToArray();
            Require(matches.Length == 1,
                "Expected exactly one root named " + name + ".");
            return matches[0];
        }

        internal static T[] AllComponents<T>(Scene scene)
            where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<T>(true)).ToArray();
        }

        private static bool Set(
            ref bool changed,
            bool required,
            Action apply)
        {
            if (!required)
            {
                return false;
            }
            apply();
            changed = true;
            return true;
        }

        private static bool SetMaterialColor(
            Material material,
            string property,
            Color value)
        {
            if (ColorEqual(material.GetColor(property), value))
            {
                return false;
            }
            material.SetColor(property, value);
            return true;
        }

        private static bool SetMaterialFloat(
            Material material,
            string property,
            float value)
        {
            if (FloatEqual(material.GetFloat(property), value))
            {
                return false;
            }
            material.SetFloat(property, value);
            return true;
        }

        private static bool ColorEqual(Color left, Color right)
        {
            return FloatEqual(left.r, right.r) &&
                   FloatEqual(left.g, right.g) &&
                   FloatEqual(left.b, right.b) &&
                   FloatEqual(left.a, right.a);
        }

        private static bool FloatEqual(float left, float right)
        {
            return Math.Abs(left - right) <= Epsilon;
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
