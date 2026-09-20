using System;
using System.IO;
using UnityEngine;

namespace MediaPipeUnityDots.Runtime.Models
{
    /// <summary>
    /// 소비자 프로젝트의 기본 모델 위치를 계산합니다.
    /// Task 모델은 StreamingAssets에, Depth 모델은 Unity가 import하는 Assets에 둡니다.
    /// Depth 추론 런타임은 이 파일 경로 대신 직렬화된 ModelAsset을 사용합니다.
    /// </summary>
    public static class ModelPaths
    {
        public const string DepthAssetPath =
            "Assets/MediaPipeUnityDots/Models/depth_anything_v2_small.onnx";

        public static string GetPath(TrackingModel model)
        {
            switch (model)
            {
                case TrackingModel.Hand:
                    return GetTaskPath("hand_landmarker.task");
                case TrackingModel.Face:
                    return GetTaskPath("face_landmarker.task");
                case TrackingModel.Pose:
                    return GetTaskPath("pose_landmarker_full.task");
                case TrackingModel.Holistic:
                    return GetTaskPath("holistic_landmarker.task");
                case TrackingModel.Depth:
                    throw new NotSupportedException(
                        "Depth 모델은 Player에서 raw ONNX 경로로 열 수 없습니다. " +
                        "Editor에서 ModelAsset을 직렬화해 DepthFrameProvider에 연결하세요.");
                default:
                    throw new ArgumentOutOfRangeException(nameof(model), model, "지원하지 않는 추적 모델입니다.");
            }
        }

        /// <summary>
        /// Depth ModelAsset을 찾을 때 사용하는 Unity 프로젝트 상대 경로입니다.
        /// </summary>
        public static string GetAssetPath(TrackingModel model)
        {
            if (model != TrackingModel.Depth)
            {
                throw new ArgumentException("ModelAsset은 Depth 모델에만 사용할 수 있습니다.", nameof(model));
            }

#if !UNITY_EDITOR
            throw new NotSupportedException(
                "Depth ModelAsset 경로는 Editor에서만 사용할 수 있습니다. Player에는 직렬화된 ModelAsset을 연결하세요.");
#else
            return DepthAssetPath;
#endif
        }

        private static string GetTaskPath(string fileName)
        {
            return Path.Combine(Application.streamingAssetsPath, "MediaPipe", "Models", fileName);
        }
    }
}
