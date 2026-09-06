using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 손 월드 landmark(미터) 버퍼 원소. 정규화 LandmarkElement와 같은 인덱스를 공유한다.
    /// </summary>
    public struct WorldLandmarkElement : IBufferElementData
    {
        public float X;
        public float Y;
        public float Z;
        public float Visibility;
        public int HandIndex;
    }
}
