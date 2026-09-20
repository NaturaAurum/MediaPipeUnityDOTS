using MediaPipeUnityDots.Runtime.Tracking;
using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    public struct FaceTrackingStatus : IComponentData
    {
        public bool IsValid;
        public int FaceCount;
        public int LandmarkCount;
        public long TimestampUs;
        public long FrameCount;
        public long CaptureId;
        public long CaptureTimestampUs;
        public long CaptureEpoch;

        public TrackingResultStatus ResultStatus;
    }
}
