using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 대상별 깊이 샘플 게시 상태. 동일 캡처의 랜드마크로 샘플링한 대표값만 담는다.
    /// </summary>
    public struct DepthSampleStatus : IComponentData
    {
        public bool IsValid;
        public long CaptureId;
        public long CaptureTimestampUs;
        public long CaptureEpoch;
        public int HandCount;
        public int PoseCount;
        public int HandValidMask;
        public int PoseValid;
    }
}
