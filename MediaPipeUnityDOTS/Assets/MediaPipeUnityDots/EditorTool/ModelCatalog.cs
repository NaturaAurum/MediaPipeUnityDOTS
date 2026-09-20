using System;
using System.IO;
using System.Security.Cryptography;
using MediaPipeUnityDots.Runtime.Models;
using UnityEditor;
using UnityEngine;

namespace MediaPipeUnityDots.EditorTool
{
    /// <summary>
    /// Editor 모델 manifest의 단일 해석기입니다. 다운로드와 Player 검증이 같은 목록을 사용합니다.
    /// </summary>
    internal static class ModelCatalog
    {
        internal const string ManifestFileName = "ModelManifest.txt";
        internal const string DepthModelMetadataFileName = "DepthModelMetadata.txt";

        internal static bool TryGetEntry(TrackingModel model, out Entry entry, out string error)
        {
            try
            {
                entry = GetEntry(model);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                entry = null;
                error = exception.Message;
                return false;
            }
        }

        internal static Entry GetEntry(TrackingModel model)
        {
            var manifestPath = GetEditorFile(ManifestFileName);
            foreach (var line in File.ReadAllLines(manifestPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line[0] == '#')
                {
                    continue;
                }

                var fields = line.Split('\t');
                if (fields.Length != 6)
                {
                    throw new FormatException($"manifest 행의 열 수가 6개가 아닙니다: {line}");
                }

                if (!Enum.TryParse(fields[0], true, out TrackingModel parsedModel) || parsedModel != model)
                {
                    continue;
                }

                if (!Uri.TryCreate(fields[4], UriKind.Absolute, out var parsedUrl)
                    || !string.Equals(parsedUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(fields[1])
                    || string.IsNullOrWhiteSpace(fields[2])
                    || string.IsNullOrWhiteSpace(fields[3])
                    || fields[5].Length != 64)
                {
                    throw new FormatException($"{fields[0]} manifest 값이 올바르지 않습니다 (HTTPS URL/SHA-256 필요).");
                }

                return new Entry(parsedModel, fields[1], fields[2], fields[3], fields[4], fields[5]);
            }

            throw new FileNotFoundException($"manifest에 {model} 모델 항목이 없습니다.", manifestPath);
        }

        internal static string GetTargetPath(TrackingModel model)
        {
            var entry = GetEntry(model);
            if (model == TrackingModel.Depth)
            {
                return Path.Combine(Application.dataPath, "MediaPipeUnityDots", "Models", entry.FileName);
            }

            return ModelPaths.GetPath(model);
        }

        internal static string GetEditorFile(string fileName)
        {
            foreach (var package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
            {
                if (package.name == "com.natura-aurum.mediapipe-unity-dots")
                {
                    return Path.Combine(package.resolvedPath, "EditorTool", fileName);
                }
            }

            var fallbackPath = Path.Combine(Application.dataPath, "MediaPipeUnityDots", "EditorTool", fileName);
            if (File.Exists(fallbackPath))
            {
                return fallbackPath;
            }

            throw new FileNotFoundException($"{fileName}을 패키지에서 찾지 못했습니다.", fileName);
        }

        internal static bool IsValidFile(string path, string expectedSha256)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                return string.Equals(ComputeSha256(path), expectedSha256, StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        internal static string ComputeSha256(string path)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(path);
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }
        /// <summary>
        /// 검증이 끝난 임시 파일만 대상에 atomic하게 교체합니다. 실패하면 대상 파일을 보존합니다.
        /// </summary>
        internal static void CommitVerifiedFile(string temporaryPath, string targetPath, string expectedSha256)
        {
            try
            {
                if (!File.Exists(temporaryPath))
                {
                    throw new FileNotFoundException("검증할 임시 모델 파일이 없습니다.", temporaryPath);
                }

                var actualSha256 = ComputeSha256(temporaryPath);
                if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"SHA-256 불일치 (expected={expectedSha256}, actual={actualSha256})");
                }

                if (File.Exists(targetPath))
                {
                    File.Replace(temporaryPath, targetPath, null);
                }
                else
                {
                    File.Move(temporaryPath, targetPath);
                }
            }
            catch
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                throw;
            }
        }


        internal static string ToAbsoluteProjectPath(string assetPath)
        {
            if (Path.IsPathRooted(assetPath))
            {
                return assetPath;
            }

            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        internal sealed class Entry
        {
            internal readonly TrackingModel Model;
            internal readonly string Kind;
            internal readonly string FileName;
            internal readonly string Revision;
            internal readonly string Url;
            internal readonly string Sha256;

            internal Entry(
                TrackingModel model,
                string kind,
                string fileName,
                string revision,
                string url,
                string sha256)
            {
                Model = model;
                Kind = kind;
                FileName = fileName;
                Revision = revision;
                Url = url;
                Sha256 = sha256;
            }
        }
    }
}
