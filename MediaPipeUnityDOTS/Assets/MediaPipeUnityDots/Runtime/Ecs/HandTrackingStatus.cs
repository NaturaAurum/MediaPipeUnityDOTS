using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    public struct HandTrackingStatus : IComponentData
    {
        public bool IsValid;
        public int Handedness;
        public float Score;
        public int LandmarkCount;
        public long TimestampUs;
        public long FrameCount;
    }
}
