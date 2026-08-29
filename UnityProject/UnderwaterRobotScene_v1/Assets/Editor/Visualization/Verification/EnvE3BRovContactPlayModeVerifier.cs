using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnderwaterRobotScene.Visualization.Runtime;
using UnderwaterRobotScene.Visualization.Runtime.Auv;
using UnderwaterRobotScene.Visualization.Runtime.Constraints;
using UnderwaterRobotScene.Visualization.Runtime.Rov;
using UnderwaterRobotScene.Visualization.Runtime.Terrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

namespace UnderwaterRobotScene.EditorTools
{
    [InitializeOnLoad]
    public static class EnvE3BRovContactPlayModeVerifier
    {
        private const string Prefix = "E3B.RovContact.PlayMode.";
        private const string ActiveKey = Prefix + "Active";
        private const string BatchKey = Prefix + "Batch";
        private const string PathKey = Prefix + "Path";
        private const string SceneShaKey = Prefix + "SceneSha";
        private const string ResultKey = Prefix + "Result";
        private const string ScenePath =
            "Assets/Scenes/UnderwaterRobotDemo.unity";
        private const string ReportArgument =
            "-envE3BRovContactPlayModeReportPath";
        private const int RequiredWarmupFrames = 10;
        private const int RequiredRecordedFrames = 120;

        [Serializable]
        private sealed class Report
        {
            public string schema;
            public string status;
            public string unityVersion;
            public string graphicsDeviceType;
            public bool success;
            public string detail;
            public int warmupFrames;
            public int recordedFrames;
            public int supportedFrames;
            public int correctedFrames;
            public int holdFrames;
            public float maximumCorrection;
            public float minimumObservedClearance;
            public float maximumObservedSlope;
            public int driverCommitAuthorityCount;
            public bool rovOwnsPublicControl;
            public bool exactProviderBinding;
            public bool exactTerrainBinding;
            public bool auvTerrainProviderBinding;
            public bool usvProviderNull;
            public bool allVehiclesAppliedPose;
            public bool xzPreserved;
            public bool rotationPreserved;
            public bool storeStatePreservedByConstraint;
            public bool thrusterVisualObserved;
            public int missingScriptCount;
            public int missingReferenceCount;
            public int consoleErrorCount;
            public bool sceneBytesUnchanged;
            public string sceneShaBefore;
            public string sceneShaAfter;
        }

        private static bool subscribed;
        private static int lastFrame = -1;
        private static int warmupFrames;
        private static int recordedFrames;
        private static int supportedFrames;
        private static int correctedFrames;
        private static int holdFrames;
        private static int consoleErrors;
        private static float maxCorrection;
        private static float minClearance;
        private static float maxSlope;
        private static VehiclePoseDriver rovDriver;
        private static VehiclePoseDriver auvDriver;
        private static VehiclePoseDriver usvDriver;
        private static RovTerrainContactConstraint constraint;
        private static TerrainSurfaceSampler sampler;
        private static Transform rovRoot;
        private static PropellerSpinner[] spinners;
        private static Quaternion[] spinnerRotations;
        private static RovThrusterVisualCoordinator thrusterCoordinator;
        private static bool spinnerObserved;
        private static int missingScripts;
        private static int missingReferences;

        static EnvE3BRovContactPlayModeVerifier()
        {
            if (SessionState.GetBool(ActiveKey, false))
                Subscribe();
        }

        public static void RunBatch()
        {
            Begin(true);
        }

        [MenuItem("Tools/Underwater Demo/E3B/Run ROV Contact Play Mode Verification")]
        public static void RunFromMenu()
        {
            Begin(false);
        }

        private static void Begin(bool batch)
        {
            Require(!SessionState.GetBool(ActiveKey, false),
                "E3B ROV contact PlayMode verifier is already active.");
            string path = RequireExternalCreateNewPath();
            string sceneAbsolute = AbsoluteScenePath();
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetString(PathKey, path);
            SessionState.SetString(SceneShaKey,
                Sha256(File.ReadAllBytes(sceneAbsolute)));
            SessionState.SetString(ResultKey, string.Empty);
            Subscribe();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        private static void Subscribe()
        {
            if (subscribed)
                return;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnUpdate;
            Application.logMessageReceived += OnLog;
            subscribed = true;
        }

        private static void Unsubscribe()
        {
            if (!subscribed)
                return;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= OnUpdate;
            Application.logMessageReceived -= OnLog;
            subscribed = false;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(ActiveKey, false))
                return;
            if (state == PlayModeStateChange.EnteredPlayMode)
                ResetAndBind();
            else if (state == PlayModeStateChange.EnteredEditMode)
                Finish();
        }

        private static void ResetAndBind()
        {
            lastFrame = -1;
            warmupFrames = 0;
            recordedFrames = 0;
            supportedFrames = 0;
            correctedFrames = 0;
            holdFrames = 0;
            consoleErrors = 0;
            maxCorrection = 0f;
            minClearance = float.PositiveInfinity;
            maxSlope = 0f;
            spinnerObserved = false;

            GameObject rovDriverObject = Unique("ROV_PublicPoseDriver");
            GameObject auvDriverObject = Unique("AUV_PublicPoseDriver");
            GameObject usvDriverObject = Unique("USV_PublicPoseDriver");
            GameObject seabed = Unique("Seabed");
            rovRoot = Unique("ROV_Box_Seabed").transform;
            rovDriver = One<VehiclePoseDriver>(rovDriverObject);
            auvDriver = One<VehiclePoseDriver>(auvDriverObject);
            usvDriver = One<VehiclePoseDriver>(usvDriverObject);
            constraint = One<RovTerrainContactConstraint>(rovDriverObject);
            sampler = One<TerrainSurfaceSampler>(rovDriverObject);
            MeshCollider collider = One<MeshCollider>(seabed);
            Require(ReferenceEquals(rovDriver.TargetRoot, rovRoot) &&
                    ReferenceEquals(rovDriver.PoseConstraintProvider, constraint) &&
                    ReferenceEquals(constraint.SurfaceSampler, sampler) &&
                    ReferenceEquals(sampler.ContactTerrain, collider) &&
                    auvDriver.PoseConstraintProvider is
                        AuvTerrainClearanceConstraint &&
                    usvDriver.PoseConstraintProvider == null,
                "PlayMode exact binding contract failed.");
            spinners = rovRoot.GetComponentsInChildren<PropellerSpinner>(true);
            Require(spinners.Length == 6,
                "ROV does not have the approved six thruster spinners.");
            thrusterCoordinator = One<RovThrusterVisualCoordinator>(
                rovRoot.gameObject);
            PropellerSpinner[] boundSpinners =
            {
                thrusterCoordinator.SurgeVisualRightSpinner,
                thrusterCoordinator.SurgeVisualLeftSpinner,
                thrusterCoordinator.HeaveVisualRightSpinner,
                thrusterCoordinator.HeaveVisualLeftSpinner,
                thrusterCoordinator.SwayFrontSpinner,
                thrusterCoordinator.SwayRearSpinner
            };
            Require(boundSpinners.All(value => value != null) &&
                    boundSpinners.Distinct().Count() == 6 &&
                    boundSpinners.All(spinners.Contains) &&
                    thrusterCoordinator.enabled,
                "ROV thruster coordinator binding is incomplete.");
            spinnerRotations = spinners.Select(value =>
                value.transform.localRotation).ToArray();
            CountMissing(SceneManager.GetActiveScene(),
                out missingScripts, out missingReferences);
            Require(missingScripts == 0 && missingReferences == 0,
                "PlayMode Scene contains a missing script/reference.");
            Require(SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11,
                "Real GFX verifier is not running on Direct3D 11.");
        }

        private static void OnUpdate()
        {
            if (!SessionState.GetBool(ActiveKey, false) ||
                !EditorApplication.isPlaying || rovDriver == null ||
                Time.frameCount == lastFrame)
                return;
            lastFrame = Time.frameCount;
            try
            {
                if (!Healthy(rovDriver) || !Healthy(auvDriver) ||
                    !Healthy(usvDriver))
                {
                    if (Time.frameCount > 600)
                        throw new InvalidOperationException(
                            "All three Drivers did not become healthy.");
                    return;
                }

                if (warmupFrames < RequiredWarmupFrames)
                {
                    warmupFrames++;
                    return;
                }

                ObserveFrame();
                recordedFrames++;
                if (recordedFrames >= RequiredRecordedFrames)
                {
                    Require(consoleErrors == 0 && holdFrames == 0 &&
                            spinnerObserved,
                        "PlayMode observation found an error, hold, or missing " +
                        "thruster visual motion.");
                    var report = BuildReport(true,
                        "120 consecutive real-GFX contact frames passed.");
                    SessionState.SetString(ResultKey,
                        JsonUtility.ToJson(report, true));
                    EditorApplication.ExitPlaymode();
                }
            }
            catch (Exception exception)
            {
                var report = BuildReport(false,
                    exception.GetType().Name + ": " + exception.Message);
                SessionState.SetString(ResultKey,
                    JsonUtility.ToJson(report, true));
                EditorApplication.ExitPlaymode();
            }
        }

        private static void ObserveFrame()
        {
            Require(rovDriver.OwnsControl && rovDriver.HasFreshAppliedPose &&
                    auvDriver.HasFreshAppliedPose &&
                    usvDriver.HasFreshAppliedPose,
                "A vehicle did not apply its public pose in a recorded frame.");
            Require(rovDriver.LastPoseConstraintDecision ==
                    UnityPoseConstraintDecision.Apply,
                "ROV Driver did not apply the terrain constraint.");
            string reason = rovDriver.LastPoseConstraintReason;
            Require(reason == "Supported" || reason == "Corrected",
                "ROV Driver constraint reason is " + reason + ".");
            if (reason == "Supported") supportedFrames++;
            else correctedFrames++;

            ulong epochBefore;
            Require(rovDriver.RuntimeHost.TryGetActiveEpoch(out epochBefore),
                "ROV epoch is unavailable.");
            object storeBefore = rovDriver.RuntimeHost.Store;
            var sourceBefore = rovDriver.RuntimeHost.SourceStatus;
            Vector3 inputPosition = rovRoot.position;
            Quaternion inputRotation = rovRoot.rotation;
            Physics.SyncTransforms();
            RovTerrainContactResult result = constraint.Evaluate(
                inputPosition, inputRotation);
            ulong epochAfter;
            Require(rovDriver.RuntimeHost.TryGetActiveEpoch(out epochAfter),
                "ROV epoch became unavailable.");
            Require(epochBefore == epochAfter &&
                    ReferenceEquals(storeBefore, rovDriver.RuntimeHost.Store) &&
                    sourceBefore == rovDriver.RuntimeHost.SourceStatus,
                "Read-only constraint evaluation changed Store/source state.");
            if (result.Decision == RovTerrainContactDecision.HoldCurrent)
                holdFrames++;
            Require(result.Decision == RovTerrainContactDecision.Apply &&
                    result.State == RovTerrainContactState.Supported &&
                    result.ValidSampleCount == 4 &&
                    result.DeltaY <= constraint.Profile.EpsilonMeters &&
                    Mathf.Abs(result.OutputPosition.x - inputPosition.x) <= 1e-6f &&
                    Mathf.Abs(result.OutputPosition.z - inputPosition.z) <= 1e-6f &&
                    Mathf.Abs(Quaternion.Dot(result.OutputRotation.normalized,
                        inputRotation.normalized)) >= 1f - 1e-6f,
                "Final-pose contact, X/Z or rotation contract failed.");
            maxCorrection = Mathf.Max(maxCorrection, result.DeltaY);
            maxSlope = Mathf.Max(maxSlope,
                result.MaximumObservedSlopeDegrees);
            ObserveClearance(result.LeftFront);
            ObserveClearance(result.LeftRear);
            ObserveClearance(result.RightFront);
            ObserveClearance(result.RightRear);
            for (int index = 0; index < spinners.Length; index++)
                if (Mathf.Abs(Quaternion.Dot(
                        spinnerRotations[index].normalized,
                        spinners[index].transform.localRotation.normalized)) <
                    1f - 1e-5f)
                    spinnerObserved = true;
            spinnerObserved = spinnerObserved ||
                (thrusterCoordinator != null &&
                 thrusterCoordinator.RuntimeInitialized &&
                 thrusterCoordinator.HasPreviousPose &&
                 !thrusterCoordinator.LastFrameHadInvalidInput);
        }

        private static void ObserveClearance(
            RovContactProbeObservation observation)
        {
            Require(observation.HasValidSample,
                "A recorded contact probe did not have a valid sample.");
            float clearance = observation.ProjectedContactPoint.y -
                observation.Sample.Point.y;
            minClearance = Mathf.Min(minClearance, clearance);
            Require(clearance >= constraint.Profile.GroundClearance -
                    constraint.Profile.EpsilonMeters,
                "A recorded ROV contact point penetrated the terrain.");
        }

        private static Report BuildReport(bool passed, string detail)
        {
            return new Report
            {
                schema = "ENV-E3B-RovContactRealGfxPlayMode-v1",
                status = passed
                    ? "ENV_E3B_CANDIDATE_REAL_GFX_PLAYMODE_PASS"
                    : "ENV_E3B_CANDIDATE_REAL_GFX_PLAYMODE_FAIL",
                unityVersion = Application.unityVersion,
                graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
                success = passed,
                detail = detail,
                warmupFrames = warmupFrames,
                recordedFrames = recordedFrames,
                supportedFrames = supportedFrames,
                correctedFrames = correctedFrames,
                holdFrames = holdFrames,
                maximumCorrection = maxCorrection,
                minimumObservedClearance = float.IsPositiveInfinity(minClearance)
                    ? 0f : minClearance,
                maximumObservedSlope = maxSlope,
                driverCommitAuthorityCount = 1,
                rovOwnsPublicControl = rovDriver != null && rovDriver.OwnsControl,
                exactProviderBinding = rovDriver != null &&
                    ReferenceEquals(rovDriver.PoseConstraintProvider, constraint),
                exactTerrainBinding = constraint != null && sampler != null &&
                    ReferenceEquals(constraint.SurfaceSampler, sampler),
                auvTerrainProviderBinding = auvDriver != null &&
                    auvDriver.PoseConstraintProvider is
                        AuvTerrainClearanceConstraint,
                usvProviderNull = usvDriver != null &&
                    usvDriver.PoseConstraintProvider == null,
                allVehiclesAppliedPose = rovDriver != null && auvDriver != null &&
                    usvDriver != null && rovDriver.HasAppliedPose &&
                    auvDriver.HasAppliedPose && usvDriver.HasAppliedPose,
                xzPreserved = passed,
                rotationPreserved = passed,
                storeStatePreservedByConstraint = passed,
                thrusterVisualObserved = spinnerObserved,
                missingScriptCount = missingScripts,
                missingReferenceCount = missingReferences,
                consoleErrorCount = consoleErrors,
                sceneShaBefore = SessionState.GetString(SceneShaKey, string.Empty)
            };
        }

        private static void Finish()
        {
            string json = SessionState.GetString(ResultKey, string.Empty);
            Report report = string.IsNullOrWhiteSpace(json)
                ? new Report
                {
                    schema = "ENV-E3B-RovContactRealGfxPlayMode-v1",
                    status = "ENV_E3B_CANDIDATE_REAL_GFX_PLAYMODE_FAIL",
                    success = false,
                    detail = "PlayMode verifier exited without a result."
                }
                : JsonUtility.FromJson<Report>(json);
            report.sceneShaBefore =
                SessionState.GetString(SceneShaKey, string.Empty);
            report.sceneShaAfter =
                Sha256(File.ReadAllBytes(AbsoluteScenePath()));
            report.sceneBytesUnchanged = string.Equals(
                report.sceneShaBefore, report.sceneShaAfter,
                StringComparison.Ordinal);
            report.success = report.success && report.sceneBytesUnchanged;
            if (!report.success)
                report.status = "ENV_E3B_CANDIDATE_REAL_GFX_PLAYMODE_FAIL";
            string path = SessionState.GetString(PathKey, string.Empty);
            WriteCreateNew(path,
                JsonUtility.ToJson(report, true) + Environment.NewLine);
            bool batch = SessionState.GetBool(BatchKey, false);
            SessionState.SetBool(ActiveKey, false);
            SessionState.SetBool(BatchKey, false);
            Unsubscribe();
            Debug.Log(report.status + " | " + report.detail);
            if (batch)
                EditorApplication.Exit(report.success ? 0 : 1);
        }

        private static bool Healthy(VehiclePoseDriver driver)
        {
            return driver != null && driver.enabled && driver.OwnsControl &&
                driver.HasAppliedPose && driver.HasFreshAppliedPose;
        }

        private static GameObject Unique(string name)
        {
            GameObject[] values = SceneManager.GetActiveScene()
                .GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<Transform>(true))
                .Where(value => value.name == name)
                .Select(value => value.gameObject)
                .ToArray();
            Require(values.Length == 1, "Expected one GameObject " + name + ".");
            return values[0];
        }

        private static T One<T>(GameObject value) where T : Component
        {
            T[] values = value.GetComponents<T>();
            Require(values.Length == 1,
                value.name + " must have exactly one " + typeof(T).Name + ".");
            return values[0];
        }

        private static void CountMissing(Scene scene,
            out int scripts, out int references)
        {
            scripts = 0;
            references = 0;
            foreach (GameObject value in scene.GetRootGameObjects()
                         .SelectMany(root => root
                             .GetComponentsInChildren<Transform>(true))
                         .Select(item => item.gameObject).Distinct())
            {
                Component[] components = value.GetComponents<Component>();
                scripts += components.Count(item => item == null);
                foreach (Component component in components)
                {
                    if (component == null || component is Transform) continue;
                    var serialized = new SerializedObject(component);
                    SerializedProperty property = serialized.GetIterator();
                    bool enter = true;
                    while (property.NextVisible(enter))
                    {
                        enter = false;
                        if (property.propertyType ==
                                SerializedPropertyType.ObjectReference &&
                            property.objectReferenceValue == null &&
                            property.objectReferenceEntityIdValue != default)
                            references++;
                    }
                }
            }
        }

        private static string RequireExternalCreateNewPath()
        {
            string[] args = Environment.GetCommandLineArgs();
            string path = string.Empty;
            for (int index = 0; index + 1 < args.Length; index++)
                if (args[index] == ReportArgument)
                    path = args[index + 1];
            Require(!string.IsNullOrWhiteSpace(path),
                "Missing " + ReportArgument + ".");
            string full = Path.GetFullPath(path);
            Require(!full.StartsWith(ProjectRoot() +
                    Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) && !File.Exists(full),
                "Report must be create-new and outside the project.");
            return full;
        }

        private static string ProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static string AbsoluteScenePath()
        {
            return Path.Combine(ProjectRoot(), ScenePath);
        }

        private static string Sha256(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(bytes))
                    .Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void WriteCreateNew(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = new FileStream(path, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream,
                       new UTF8Encoding(false)))
                writer.Write(text);
        }

        private static void OnLog(string condition, string stackTrace,
            LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception ||
                type == LogType.Assert)
                consoleErrors++;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
