using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 얼굴 랜드마크 포인트 엔티티의 얼굴(0~)과 인덱스(0~477) 태그.
    /// </summary>
    public struct FaceLandmarkPoint : IComponentData
    {
        public int FaceIndex;
        public int Index;
    }
}
