using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
using MediaPipeUnityDots.Runtime.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Unity.InferenceEngine;

namespace MediaPipeUnityDots.EditorTool
{
    /// <summary>
    /// 고정된 모델 목록에 따라 소비자 프로젝트의 모델을 명시적으로 준비합니다.
    /// 패키지 캐시에는 쓰지 않고, Task는 StreamingAssets, Depth는 Assets에 저장합니다.
    /// 기존 개발 프로젝트는 같은 Assets 상대 경로와 .meta GUID를 유지하므로 재배선하지 않아도 됩니다.
    /// </summary>
    public static class DownloadDepthModel
    {
        private static UnityWebRequest _activeRequest;
        private static ModelCatalog.Entry _activeEntry;
        private static string _activeTargetPath;
        private static string _activeTempPath;

        [MenuItem("MediaPipe/Models/Download Hand Task Model")]
        public static void DownloadHand()
        {
            Download(TrackingModel.Hand);
        }

        [MenuItem("MediaPipe/Models/Download Face Task Model")]
        public static void DownloadFace()
        {
            Download(TrackingModel.Face);
        }

        [MenuItem("MediaPipe/Models/Download Pose Task Model")]
        public static void DownloadPose()
        {
            Download(TrackingModel.Pose);
        }

        [MenuItem("MediaPipe/Models/Download Holistic Task Model")]
        public static void DownloadHolistic()
        {
            Download(TrackingModel.Holistic);
        }

        [MenuItem("MediaPipe/Download Depth Model (DA-V2 Small)")]
        public static void Download()
        {
            Download(TrackingModel.Depth);
        }

        /// <summary>
        /// 메뉴와 외부 Editor 자동화에서 공통으로 사용하는 단일 모델 다운로드 진입점입니다.
        /// </summary>
        public static void Download(TrackingModel model)
        {
            if (_activeRequest != null)
            {
                Debug.LogWarning("[MPUD] 다른 모델 다운로드가 진행 중입니다. 완료 후 다시 시도하세요.");
                return;
            }

            if (!ModelCatalog.TryGetEntry(model, out var entry, out var catalogError))
            {
                Debug.LogError($"[MPUD] {model} 모델 manifest를 읽지 못했습니다: {catalogError}");
                return;
            }

            var targetPath = ModelCatalog.GetTargetPath(model);
            var expectedKind = model == TrackingModel.Depth ? "onnx" : "task";
            if (!string.Equals(entry.Kind, expectedKind, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(targetPath), entry.FileName, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(entry.Revision))
            {
                Debug.LogError($"[MPUD] {model} 모델 manifest와 기본 경로가 일치하지 않습니다: {targetPath}");
                return;
            }

            if (ModelCatalog.IsValidFile(targetPath, entry.Sha256))
            {
                Debug.Log($"[MPUD] {model} 모델이 이미 준비되어 있습니다: {targetPath}");
                RefreshImportedAsset(model, targetPath);
                return;
            }


            var directory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrEmpty(directory))
            {
                Debug.LogError($"[MPUD] {model} 모델 대상 경로가 올바르지 않습니다: {targetPath}");
                return;
            }

            string tempPath = null;
            UnityWebRequest request = null;
            try
            {
                Directory.CreateDirectory(directory);
                tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".download";
                request = UnityWebRequest.Get(entry.Url);
                request.downloadHandler = new DownloadHandlerFile(tempPath);
                _activeRequest = request;
                _activeEntry = entry;
                _activeTargetPath = targetPath;
                _activeTempPath = tempPath;
                request.SendWebRequest();
                EditorApplication.update += PollDownload;
                Debug.Log($"[MPUD] {model} 모델 다운로드 시작: {entry.Url}");
            }
            catch (Exception exception)
            {
                request?.Dispose();
                DeleteIfExists(tempPath);
                Debug.LogError($"[MPUD] {model} 모델 다운로드를 시작하지 못했습니다. URL={entry.Url}, 대상={targetPath}, 오류={exception.Message}");
                ClearDownloadState();
            }
        }

        /// <summary>
        /// 다운로드 후 import된 Depth ModelAsset을 UI/Provider 배선에 전달할 때 사용합니다.
        /// </summary>
        public static bool TryGetDepthModelAsset(out ModelAsset modelAsset)
        {
            modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPaths.GetAssetPath(TrackingModel.Depth));
            if (modelAsset != null)
            {
                return true;
            }

            Debug.LogError(
                $"[MPUD] Depth ModelAsset을 찾지 못했습니다. MediaPipe/Download Depth Model (DA-V2 Small)을 실행하고 " +
                $"{ModelPaths.GetAssetPath(TrackingModel.Depth)}를 확인하세요.");
            return false;
        }
        /// <summary>
        /// Unity -executeMethod에서 선택 모델을 동기적으로 준비합니다.
        /// 예: -batchmode -quit -executeMethod MediaPipeUnityDots.EditorTool.DownloadDepthModel.PrepareBatch
        /// -mpudModels Hand,Face,Pose,Holistic
        /// </summary>
        public static void PrepareBatch()
        {
            var exitCode = 1;
            try
            {
                var models = ParseBatchModels(Environment.GetCommandLineArgs());
                foreach (var model in models)
                {
                    PrepareSynchronously(model);
                }

                Debug.Log("[MPUD] Batch model preparation complete");
                exitCode = 0;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[MPUD] Batch model preparation failed: {exception.Message}");
            }
            finally
            {
                Environment.ExitCode = exitCode;
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(exitCode);
                }
            }
        }

        private static List<TrackingModel> ParseBatchModels(string[] arguments)
        {
            var models = new List<TrackingModel>();
            var seen = new HashSet<TrackingModel>();
            for (var index = 0; index < arguments.Length; index++)
            {
                if (!string.Equals(arguments[index], "-mpudModels", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (index + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index + 1]))
                {
                    throw new ArgumentException("-mpudModels 뒤에 Hand,Face,Pose,Holistic,Depth 중 하나 이상을 지정해야 합니다.");
                }

                foreach (var token in arguments[++index].Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!Enum.TryParse(token.Trim(), true, out TrackingModel model)
                        || !Enum.IsDefined(typeof(TrackingModel), model))
                    {
                        throw new ArgumentException($"알 수 없는 모델입니다: {token}");
                    }

                    if (seen.Add(model))
                    {
                        models.Add(model);
                    }
                }
            }

            if (models.Count == 0)
            {
                throw new ArgumentException("선택 모델이 없습니다. -mpudModels Hand,Face,... 를 지정하세요.");
            }

            return models;
        }

        private static void PrepareSynchronously(TrackingModel model)
        {
            if (!ModelCatalog.TryGetEntry(model, out var entry, out var catalogError))
            {
                throw new InvalidOperationException($"{model} 모델 manifest를 읽지 못했습니다: {catalogError}");
            }

            var targetPath = ModelCatalog.GetTargetPath(model);
            var expectedKind = model == TrackingModel.Depth ? "onnx" : "task";
            if (!string.Equals(entry.Kind, expectedKind, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(targetPath), entry.FileName, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(entry.Revision))
            {
                throw new InvalidDataException($"{model} manifest와 기본 경로/리비전이 일치하지 않습니다: {targetPath}");
            }

            if (ModelCatalog.IsValidFile(targetPath, entry.Sha256))
            {
                EnsureImportedMetadata(model, targetPath);
                Debug.Log($"[MPUD] {model} model already ready: {targetPath}");
                return;
            }

            var directory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidDataException($"{model} 모델 대상 경로가 올바르지 않습니다: {targetPath}");
            }

            Directory.CreateDirectory(directory);
            var tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".download";
            try
            {
                Debug.Log($"[MPUD] Downloading {model}: {entry.Url}");
                using (var client = new HttpClient())
                using (var response = client.GetAsync(entry.Url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
                {
                    response.EnsureSuccessStatusCode();
                    using (var source = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                    using (var destination = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        source.CopyTo(destination);
                    }
                }

                ModelCatalog.CommitVerifiedFile(tempPath, targetPath, entry.Sha256);
                EnsureImportedMetadata(model, targetPath);
                Debug.Log($"[MPUD] {model} model ready: {targetPath}");
            }
            finally
            {
                DeleteIfExists(tempPath);
            }
        }

        private static void PollDownload()
        {
            var request = _activeRequest;
            if (request == null)
            {
                EditorApplication.update -= PollDownload;
                return;
            }

            EditorUtility.DisplayProgressBar(
                "MediaPipe Model",
                $"Downloading {_activeEntry.Model}",
                request.downloadProgress);
            if (!request.isDone)
            {
                return;
            }

            EditorApplication.update -= PollDownload;
            EditorUtility.ClearProgressBar();

            var entry = _activeEntry;
            var targetPath = _activeTargetPath;
            var tempPath = _activeTempPath;
            var result = request.result;
            var requestError = request.error;
            request.Dispose();
            ClearDownloadState();

            try
            {
                if (result != UnityWebRequest.Result.Success)
                {
                    throw new InvalidOperationException(string.IsNullOrEmpty(requestError) ? "네트워크 요청 실패" : requestError);
                }

                if (!File.Exists(tempPath))
                {
                    throw new InvalidOperationException("다운로드 임시 파일이 생성되지 않았습니다.");
                }

                ModelCatalog.CommitVerifiedFile(tempPath, targetPath, entry.Sha256);
                EnsureImportedMetadata(entry.Model, targetPath);
                RefreshImportedAsset(entry.Model, targetPath);
                Debug.Log($"[MPUD] {entry.Model} 모델 준비 완료: {targetPath}");
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[MPUD] {entry.Model} 모델 다운로드 실패. URL={entry.Url}, 대상={targetPath}, " +
                    $"expectedSha256={entry.Sha256}, 오류={exception.Message}");
                DeleteIfExists(tempPath);
            }
            finally
            {
                DeleteIfExists(tempPath);
            }
        }

        private static void EnsureImportedMetadata(TrackingModel model, string targetPath)
        {
            if (model != TrackingModel.Depth)
            {
                return;
            }

            var metadataSource = ModelCatalog.GetEditorFile(ModelCatalog.DepthModelMetadataFileName);
            var metadata = File.ReadAllText(metadataSource);
            var hasGuid = false;
            foreach (var line in metadata.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("guid:", StringComparison.Ordinal))
                {
                    hasGuid = line.Substring("guid:".Length).Trim().Length == 32;
                    break;
                }
            }

            if (!hasGuid)
            {
                throw new InvalidDataException($"Depth metadata에 32자리 GUID가 없습니다: {metadataSource}");
            }

            var targetMetadata = targetPath + ".meta";
            var tempMetadata = targetMetadata + "." + Guid.NewGuid().ToString("N") + ".download";
            File.Copy(metadataSource, tempMetadata, true);
            ReplaceAtomically(tempMetadata, targetMetadata);
        }

        private static void RefreshImportedAsset(TrackingModel model, string targetPath)
        {
            try
            {
                EnsureImportedMetadata(model, targetPath);
                if (model != TrackingModel.Depth)
                {
                    return;
                }

                AssetDatabase.Refresh();
                AssetDatabase.ImportAsset(ModelPaths.GetAssetPath(model), ImportAssetOptions.ForceUpdate);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[MPUD] {model} 모델 import 준비에 실패했습니다: {exception.Message}");
            }
        }

        private static void ReplaceAtomically(string tempPath, string targetPath)
        {
            if (File.Exists(targetPath))
            {
                File.Replace(tempPath, targetPath, null);
            }
            else
            {
                File.Move(tempPath, targetPath);
            }
        }

        private static void ClearDownloadState()
        {
            _activeRequest = null;
            _activeEntry = null;
            _activeTargetPath = null;
            _activeTempPath = null;
        }

        private static void DeleteIfExists(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }

    }
}
