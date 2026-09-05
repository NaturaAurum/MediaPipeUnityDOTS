using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    [InternalBufferCapacity(66)]
    public struct PoseLandmarkElement : IBufferElementData
    {
        public float X;
        public float Y;
        public float Z;
        public int PoseIndex;
    }
}
