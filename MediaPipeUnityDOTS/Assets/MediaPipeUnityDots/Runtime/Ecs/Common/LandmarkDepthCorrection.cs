using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 포인트별 깊이 보정 상태. 대상 단위 오프셋을 유지하며 XY 필터와 분리한다.
    /// Identity는 Hand=handedness, Pose=0(식별 불가, 세대·유효성으로만 관리).
    /// </summary>
    // ponytail: 같은 대상의 포인트가 같은 보정을 중복 계산한다. 대상별 버퍼로 옮기면 절약.
    public struct LandmarkDepthCorrection : IComponentData
    {
        public int Initialized;
        public int Identity;
        public float Baseline;
        public float Filtered;
        public long LastDepthTimestampUs;
        public long DepthEpoch;
    }
}
