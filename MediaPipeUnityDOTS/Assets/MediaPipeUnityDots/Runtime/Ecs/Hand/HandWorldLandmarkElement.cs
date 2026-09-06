using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// MediaPipe 손 월드 landmark 원본(미터). Unity 월드 위치가 아니며 가공 없이 보존한다.
    /// 정규화 LandmarkElement와 같은 인덱스를 공유한다. 영상 정합 변환은 렌더 시스템에서 수행한다.
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
