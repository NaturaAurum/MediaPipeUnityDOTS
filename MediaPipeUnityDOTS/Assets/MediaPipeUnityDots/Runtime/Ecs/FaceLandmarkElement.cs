using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    [InternalBufferCapacity(956)]
    public struct FaceLandmarkElement : IBufferElementData
    {
        public float X;
        public float Y;
        public float Z;
        public int FaceIndex;
    }
}
