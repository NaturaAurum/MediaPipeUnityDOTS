using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 포즈 랜드마크 포인트 엔티티의 포즈(0~)와 인덱스(0~32) 태그.
    /// </summary>
    public struct PoseLandmarkPoint : IComponentData
    {
        public int PoseIndex;
        public int Index;
    }
}
