using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 손 대상별 깊이 대표값. 큰 값=가까움(DA-V2 규약).
    /// </summary>
    public struct HandDepthSampleElement : IBufferElementData
    {
        public float Depth;
        public int HandIndex;
    }
}
