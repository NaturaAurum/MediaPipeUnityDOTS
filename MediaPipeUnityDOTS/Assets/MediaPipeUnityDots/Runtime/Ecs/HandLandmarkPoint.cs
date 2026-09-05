using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 랜드마크 포인트 엔티티의 손(0~)과 인덱스(0~20) 태그.
    /// </summary>
    public struct HandLandmarkPoint : IComponentData
    {
        public int HandIndex;
        public int Index;
    }
}
