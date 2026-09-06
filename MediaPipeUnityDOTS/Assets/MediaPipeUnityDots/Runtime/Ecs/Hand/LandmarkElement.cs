using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    [InternalBufferCapacity(84)]
    public struct LandmarkElement : IBufferElementData
    {
        public float X;
        public float Y;
        public float Z;
        public float Visibility;
        public float Presence;
        public int HandIndex;
    }
}
