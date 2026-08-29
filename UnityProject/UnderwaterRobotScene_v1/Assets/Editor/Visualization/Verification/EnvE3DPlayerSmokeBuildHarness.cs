using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    public static class EnvE3DPlayerSmokeBuildHarness
    {
        private const string ScenePath =
            "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string OutputArgument =
            "-envE3DPlayerSmokeBuildOutput";
        private const string BuildPassMarker =
            "ENV_E3D_PLAYER_BUILD_PASS";
        private const string BuildFailMarker =
            "ENV_E3D_PLAYER_BUILD_FAIL";

        [MenuItem(
            "Tools/Underwater Demo/E3D/Build Player Terrain Readability Smoke %#F12")]
        public static void RunFromMenu()
        {
            Run(false);
        }

        public static void RunBatch()
        {
            Run(true);
        }

        private static void Run(bool batch)
        {
            try
            {
                string outputPath = ResolveOutputPath();
                string outputDirectory = Path.GetDirectoryName(outputPath);
                if (string.IsNullOrEmpty(outputDirectory))
                    throw new InvalidOperationException(
                        "The Player output directory is unavailable.");
                Directory.CreateDirectory(outputDirectory);

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = outputPath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development |
                        BuildOptions.StrictMode
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report == null ||
                    report.summary.result != BuildResult.Succeeded)
                {
                    string result = report == null
                        ? "NoReport"
                        : report.summary.result.ToString();
                    throw new InvalidOperationException(
                        "StandaloneWindows64 build result was " + result + ".");
                }

                Debug.Log(BuildPassMarker +
                    " | scene=" + ScenePath +
                    " | target=" + BuildTarget.StandaloneWindows64 +
                    " | output=" + outputPath +
                    " | totalBytes=" + report.summary.totalSize);
                if (batch)
                    EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError(BuildFailMarker + " | " +
                    exception.GetType().Name + ": " + exception.Message);
                if (batch)
                    EditorApplication.Exit(1);
                else
                    throw;
            }
        }

        private static string ResolveOutputPath()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(
                        arguments[index], OutputArgument,
                        StringComparison.Ordinal))
                {
                    return RequireExternalExecutablePath(
                        arguments[index + 1]);
                }
            }

            string projectRoot = Path.GetFullPath(
                Directory.GetCurrentDirectory());
            string evidenceRoot = Path.GetFullPath(Path.Combine(
                projectRoot, "..", "..", "..", "..", "_evidence"));
            string timestamp = DateTime.UtcNow.ToString(
                "yyyyMMddTHHmmssfffZ");
            return RequireExternalExecutablePath(Path.Combine(
                evidenceRoot,
                "e3d_player_smoke",
                timestamp,
                "UnderwaterRobotDemo_E3D_PlayerSmoke.exe"));
        }

        private static string RequireExternalExecutablePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(
                    "A Player build output path is required.", nameof(value));
            string path = Path.GetFullPath(value);
            if (!string.Equals(
                    Path.GetExtension(path), ".exe",
                    StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    "The Player build output must be a .exe file.",
                    nameof(value));

            string projectRoot = Path.GetFullPath(
                Directory.GetCurrentDirectory())
                .TrimEnd(Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (path.StartsWith(projectRoot,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Player build output must remain outside the Unity project.");
            return path;
        }
    }
}
