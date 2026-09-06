using Unity.Collections;
using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    public struct HandTrackingStatus : IComponentData
    {
        public bool IsValid;
        public int Handedness;
        public int HandCount;
        public FixedList32Bytes<int> HandednessList;
        public FixedList32Bytes<float> ScoreList;
        public float Score;
        public int LandmarkCount;
        public long TimestampUs;
        public long FrameCount;

        public long CaptureId;

        public long CaptureTimestampUs;

        public long CaptureEpoch;
    }
}
