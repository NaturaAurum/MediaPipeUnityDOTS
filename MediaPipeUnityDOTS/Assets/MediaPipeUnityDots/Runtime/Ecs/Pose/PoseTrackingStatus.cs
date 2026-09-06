using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    public struct PoseTrackingStatus : IComponentData
    {
        public bool IsValid;
        public int PoseCount;
        public int LandmarkCount;
        public long TimestampUs;
        public long FrameCount;

        public long CaptureId;

        public long CaptureTimestampUs;

        public long CaptureEpoch;
    }
}
