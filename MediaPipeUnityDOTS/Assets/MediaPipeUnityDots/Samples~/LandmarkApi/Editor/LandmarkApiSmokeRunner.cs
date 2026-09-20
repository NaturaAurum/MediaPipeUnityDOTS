using System;
using System.IO;
using MediaPipeUnityDots.Runtime.Models;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MediaPipeUnityDots.Sample.LandmarkApi.Editor
{
    /// <summary>
    /// Unity -executeMethod에서 호출하는 소비자 검증 진입점입니다.
    /// </summary>
    public static class LandmarkApiSmokeRunner
    {
        private const string SmokePrefix = "[MPUD CONSUMER SMOKE] ";
        private static LandmarkApiSmoke _activeSmoke;

        [MenuItem("MediaPipe/Landmark API/Run Consumer Smoke")]
        public static void Run()
        {
            if (_activeSmoke != null)
            {
                Debug.LogError(SmokePrefix + "ERROR smoke is already running");
                ExitIfBatch(false);
                return;
            }

            var timeout = ReadDouble("-mpudSmokeTimeoutSeconds", "MPUD_SMOKE_TIMEOUT_SECONDS", 120d);
            var fixture = ReadString("-mpudFixture", "MPUD_FIXTURE", LandmarkApiSmoke.ResolveFixturePath(null));
            var smoke = new LandmarkApiSmoke(
                fixture,
                ReadModelPath("-mpudHandModel", "MPUD_HAND_MODEL", TrackingModel.Hand),
                ReadModelPath("-mpudFaceModel", "MPUD_FACE_MODEL", TrackingModel.Face),
                ReadModelPath("-mpudPoseModel", "MPUD_POSE_MODEL", TrackingModel.Pose),
                ReadModelPath("-mpudHolisticModel", "MPUD_HOLISTIC_MODEL", TrackingModel.Holistic),
                timeout,
                Debug.Log,
                OnSmokeCompleted,
                () => EditorApplication.timeSinceStartup);
            _activeSmoke = smoke;
            EditorApplication.update += TickSmoke;
            smoke.Start();
            Debug.Log(SmokePrefix + "EDITOR_STARTED");
        }

        public static void BuildStandalone()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Player 검증은 별도 소비자 프로젝트에서 batchmode로 실행해야 합니다.");
            var architecture = PlayerSettings.GetArchitecture(BuildTargetGroup.Standalone);
            var cameraUsage = PlayerSettings.macOS.cameraUsageDescription;
            var targetOSVersion = PlayerSettings.macOS.targetOSVersion;
            string scenePath = null;
            var succeeded = false;
            try
            {
                CopyFixtureToStreamingAssets();
                AssetDatabase.Refresh();
                scenePath = CreatePlayerScene();
                PlayerSettings.SetArchitecture(BuildTargetGroup.Standalone, 2);
                PlayerSettings.macOS.cameraUsageDescription = "Camera frames are used for MediaPipe landmark tracking.";
                PlayerSettings.macOS.targetOSVersion = "26.0";
                var outputPath = ReadString(
                    "-mpudPlayerPath",
                    "MPUD_PLAYER_PATH",
                    Path.Combine(ProjectRoot, "Builds", "LandmarkApiSmoke.app"));
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { scenePath },
                    locationPathName = outputPath,
                    target = BuildTarget.StandaloneOSX,
                    options = BuildOptions.None,
                });
                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException("Player build failed: " + report.summary.result);
                }

                Debug.Log(SmokePrefix + "PLAYER_BUILD_PASS path=" + outputPath);
                succeeded = true;
            }
            catch (Exception exception)
            {
                Debug.LogError(SmokePrefix + "PLAYER_BUILD_FAIL " + exception);
            }
            finally
            {
                PlayerSettings.SetArchitecture(BuildTargetGroup.Standalone, architecture);
                PlayerSettings.macOS.cameraUsageDescription = cameraUsage;
                PlayerSettings.macOS.targetOSVersion = targetOSVersion;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (scenePath != null)
                    AssetDatabase.DeleteAsset(scenePath);
            }
            ExitIfBatch(succeeded);
        }

        private static void TickSmoke()
        {
            if (_activeSmoke == null)
            {
                return;
            }

            _activeSmoke.Tick();
        }

        private static void OnSmokeCompleted(bool success, string error)
        {
            EditorApplication.update -= TickSmoke;
            _activeSmoke?.Dispose();
            _activeSmoke = null;
            if (!success)
            {
                Debug.LogError(SmokePrefix + "EDITOR_FAIL " + error);
            }
            else
            {
                Debug.Log(SmokePrefix + "EDITOR_PASS");
            }

            ExitIfBatch(success);
        }

        private static string CreatePlayerScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorUtility.CreateGameObjectWithHideFlags(
                "LandmarkApiPlayerSmoke", HideFlags.None, typeof(LandmarkApiPlayerSmoke));
            var path = AssetDatabase.GenerateUniqueAssetPath("Assets/LandmarkApiSmoke.unity");
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }

        private static void CopyFixtureToStreamingAssets()
        {
            var bodyPath = ReadString("-mpudFixture", "MPUD_FIXTURE", LandmarkApiSmoke.ResolveFixturePath(null));
            foreach (var filename in new[] { LandmarkApiSmoke.FixtureFileName, LandmarkApiSmoke.FaceFixtureFileName })
            {
                var source = filename == LandmarkApiSmoke.FixtureFileName
                    ? bodyPath
                    : Path.Combine(Path.GetDirectoryName(bodyPath), filename);
                if (!File.Exists(source))
                    throw new FileNotFoundException("fixture source was not found", source);
                var destination = Path.Combine(Application.streamingAssetsPath, "MediaPipe", "Fixtures", filename);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                if (Path.GetFullPath(source) != Path.GetFullPath(destination))
                    File.Copy(source, destination, true);
            }
        }

        private static string ReadModelPath(string argument, string environment, TrackingModel model)
        {
            return ReadString(argument, environment, ModelPaths.GetPath(model));
        }

        private static string ReadString(string argument, string environment, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], argument, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            var value = Environment.GetEnvironmentVariable(environment);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static double ReadDouble(string argument, string environment, double fallback)
        {
            var value = ReadString(argument, environment, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed > 0d
                ? parsed
                : fallback;
        }

        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        private static void ExitIfBatch(bool success)
        {
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(success ? 0 : 1);
            }
        }
    }
}
