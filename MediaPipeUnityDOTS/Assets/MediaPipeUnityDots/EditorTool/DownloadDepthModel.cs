using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace MediaPipeUnityDots.EditorTool
{
    /// <summary>
    /// 단안 깊이(DA-V2 Small) ONNX 다운로드. 출처·리비전·SHA-256 고정, 실패 시 불완전 파일 삭제.
    /// 다운로드 후 Unity가 ModelAsset으로 import하므로 실행 전에 이 메뉴를 먼저 실행해야 한다.
    /// .onnx는 git에서 제외되지만 .onnx.meta(GUID)는 커밋해 씬 참조를 안정적으로 유지한다.
    /// </summary>
    public static class DownloadDepthModel
    {
        // onnx-community/depth-anything-v2-small, onnx/model.onnx, Apache-2.0.
        private const string ModelUrl =
            "https://huggingface.co/onnx-community/depth-anything-v2-small/resolve/main/onnx/model.onnx";

        private const string ExpectedSha256 =
            "afb6a5c28f3b6bf1618c6e43f02073ef9dfdc70e937502d51603e57b0a1df10c";

        private const string ModelFileName = "depth_anything_v2_small.onnx";

        private static UnityWebRequest _activeRequest;
        private static string _activeTargetPath;

        [MenuItem("MediaPipe/Download Depth Model (DA-V2 Small)")]
        public static void Download()
        {
            var targetPath = GetModelPath();
            if (File.Exists(targetPath))
            {
                if (VerifySha256(targetPath))
                {
                    Debug.Log($"[MPUD] Depth model already present: {targetPath}");
                    return;
                }

                Debug.LogWarning("[MPUD] Depth model hash mismatch, re-downloading.");
                File.Delete(targetPath);
            }

            if (_activeRequest != null)
            {
                Debug.Log("[MPUD] Depth model download already in progress.");
                return;
            }

            Debug.Log($"[MPUD] Downloading depth model to {targetPath}");
            _activeTargetPath = targetPath;
            _activeRequest = UnityWebRequest.Get(ModelUrl);
            _activeRequest.SendWebRequest();
            EditorApplication.update += PollDownload;
        }

        private static void PollDownload()
        {
            var request = _activeRequest;
            if (request == null)
            {
                EditorApplication.update -= PollDownload;
                return;
            }

            EditorUtility.DisplayProgressBar("Depth Model", "Downloading DA-V2 Small ONNX", request.downloadProgress);
            if (!request.isDone)
            {
                return;
            }

            EditorApplication.update -= PollDownload;
            EditorUtility.ClearProgressBar();
            _activeRequest = null;

            try
            {
                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new InvalidOperationException(request.error);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(_activeTargetPath));
                File.WriteAllBytes(_activeTargetPath, request.downloadHandler.data);
                if (!VerifySha256(_activeTargetPath))
                {
                    File.Delete(_activeTargetPath);
                    throw new InvalidOperationException("SHA-256 mismatch, deleted incomplete file.");
                }

                AssetDatabase.Refresh();
                Debug.Log($"[MPUD] Depth model ready: {_activeTargetPath}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[MPUD] Depth model download failed: {exception.Message}");
            }
            finally
            {
                request.Dispose();
                _activeTargetPath = null;
            }
        }

        private static string GetModelPath()
        {
            return Path.Combine(
                Application.dataPath, "MediaPipeUnityDots", "Models", ModelFileName);
        }

        private static bool VerifySha256(string path)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(path);
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant() == ExpectedSha256;
        }
    }
}
