using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 얼굴 blendshape score 버퍼 원소. 얼굴당 52개, 정규화 버퍼와 같은 얼굴 인덱스를 공유한다.
    /// </summary>
    // 얼굴 싱글턴이 청크 한계(16320B) 근처라 청크 내장량을 0으로 둔다. 기존 버퍼 배치에 영향 없음.
    [InternalBufferCapacity(0)]
    public struct FaceBlendshapeElement : IBufferElementData
    {
        public float Score;
        public int FaceIndex;
        public int BlendshapeIndex;
    }
}
