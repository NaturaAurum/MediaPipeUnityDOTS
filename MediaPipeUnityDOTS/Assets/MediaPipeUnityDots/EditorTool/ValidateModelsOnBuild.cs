using System;
using System.Collections.Generic;
using System.IO;
using MediaPipeUnityDots.Runtime.Models;
using MediaPipeUnityDots.Runtime.Tracking;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MediaPipeUnityDots.EditorTool
{
    /// <summary>
    /// Player 빌드에 실제로 선택된 프로바이더 모델만 검증합니다.
    /// 모든 카탈로그 모델을 요구하지 않으며, 비활성 프로바이더는 선택으로 보지 않습니다.
    /// </summary>
    public sealed class ValidateModelsOnBuild : IProcessSceneWithReport
    {
        public int callbackOrder => -1000;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null)
            {
                return;
            }

            var failures = ModelBuildValidation.Validate(scene);
            if (failures.Count == 0)
            {
                return;
            }

            throw new BuildFailedException(
                "[MPUD] 선택된 프로바이더 모델 검증 실패:\n- " + string.Join("\n- ", failures));
        }
    }

    internal static class ModelBuildValidation
    {
        internal static List<string> Validate(Scene scene)
        {
            var failures = new List<string>();
            // 빌드 파이프라인이 전달한 장면만 검사하여 열린 Editor 장면을 변경하지 않는다.
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var provider in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (provider == null || !provider.isActiveAndEnabled)
                    {
                        continue;
                    }

                    switch (provider)
                    {
                        case HandFrameProvider:
                            ValidateTaskProvider(provider, TrackingModel.Hand, scene.path, failures);
                            break;
                        case FaceFrameProvider:
                            ValidateTaskProvider(provider, TrackingModel.Face, scene.path, failures);
                            break;
                        case PoseFrameProvider:
                            ValidateTaskProvider(provider, TrackingModel.Pose, scene.path, failures);
                            break;
                        case HolisticFrameProvider:
                            ValidateTaskProvider(provider, TrackingModel.Holistic, scene.path, failures);
                            break;
                        case DepthFrameProvider depth:
                            ValidateDepthProvider(depth, scene.path, failures);
                            break;
                    }
                }
            }

            return failures;
        }

        private static void ValidateTaskProvider(
            MonoBehaviour provider,
            TrackingModel model,
            string scenePath,
            List<string> failures)
        {
            var serializedObject = new SerializedObject(provider);
            var modelProperty = serializedObject.FindProperty("_modelPath");
            var configuredPath = modelProperty == null ? null : modelProperty.stringValue;
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                if (!ModelCatalog.TryGetEntry(model, out var entry, out var catalogError))
                {
                    failures.Add($"{scenePath}/{provider.name}: {model} manifest를 읽지 못했습니다 ({catalogError})");
                    return;
                }

                var defaultPath = ModelCatalog.GetTargetPath(model);
                if (!ModelCatalog.IsValidFile(defaultPath, entry.Sha256))
                {
                    failures.Add(
                        $"{scenePath}/{provider.name}: 기본 {model} 모델이 없거나 SHA-256이 다릅니다 ({defaultPath})");
                }

                return;
            }

            var resolvedPath = ResolveConfiguredPath(configuredPath);
            if (!File.Exists(resolvedPath))
            {
                failures.Add(
                    $"{scenePath}/{provider.name}: 설정한 {model} 모델 경로가 없습니다 ({configuredPath})");
                return;
            }

            if (new FileInfo(resolvedPath).Length == 0)
            {
                failures.Add(
                    $"{scenePath}/{provider.name}: 설정한 {model} 모델 파일이 비어 있습니다 ({resolvedPath})");
            }
        }

        private static void ValidateDepthProvider(
            DepthFrameProvider provider,
            string scenePath,
            List<string> failures)
        {
            var serializedObject = new SerializedObject(provider);
            var modelProperty = serializedObject.FindProperty("_modelAsset");
            var modelAsset = modelProperty == null ? null : modelProperty.objectReferenceValue;
            if (modelAsset == null)
            {
                failures.Add(
                    $"{scenePath}/{provider.name}: DepthFrameProvider에 ModelAsset이 연결되지 않았습니다 " +
                    "(MediaPipe/Models/Assign Depth Model To Selected Providers 실행)");
                return;
            }

            var assetPath = AssetDatabase.GetAssetPath(modelAsset);
            if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{scenePath}/{provider.name}: Depth ModelAsset 경로가 ONNX가 아닙니다 ({assetPath})");
                return;
            }

            var absolutePath = ModelCatalog.ToAbsoluteProjectPath(assetPath);
            if (!File.Exists(absolutePath))
            {
                failures.Add($"{scenePath}/{provider.name}: Depth ModelAsset 파일이 없습니다 ({assetPath})");
            }
        }

        private static string ResolveConfiguredPath(string configuredPath)
        {
            if (Path.IsPathRooted(configuredPath))
            {
                return configuredPath;
            }

            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var projectRelativePath = Path.Combine(projectRoot, configuredPath);
            if (File.Exists(projectRelativePath))
            {
                return projectRelativePath;
            }

            return configuredPath;
        }
    }
}
