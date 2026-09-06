using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 깊이 보정 설정. UI(App)에서 푸시하고 프로바이더·렌더가 읽는다.
    /// 싱글턴 미존재 시 기본값(Enabled=0)으로 폴백한다.
    /// </summary>
    public struct DepthSettings : IComponentData
    {
        public int Enabled;
        public float Weight;
        public float DepthGain;
        public float MaxOffset;
        public long MaxSampleAgeUs;
        public long MaxAlignmentDeltaUs;

        public static DepthSettings Default => new()
        {
            Enabled = 0,
            Weight = 0f,
            DepthGain = 1f,
            MaxOffset = 0.1f,
            MaxSampleAgeUs = 200000L,
            MaxAlignmentDeltaUs = 100000L,
        };
    }
}
