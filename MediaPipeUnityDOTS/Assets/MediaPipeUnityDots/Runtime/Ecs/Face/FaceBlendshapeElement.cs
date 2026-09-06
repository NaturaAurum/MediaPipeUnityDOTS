using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 얼굴 blendshape score 버퍼 원소. 얼굴당 52개, 정규화 버퍼와 같은 얼굴 인덱스를 공유한다.
    /// </summary>
    [InternalBufferCapacity(104)]
    public struct FaceBlendshapeElement : IBufferElementData
    {
        public float Score;
        public int FaceIndex;
        public int BlendshapeIndex;
    }
}
