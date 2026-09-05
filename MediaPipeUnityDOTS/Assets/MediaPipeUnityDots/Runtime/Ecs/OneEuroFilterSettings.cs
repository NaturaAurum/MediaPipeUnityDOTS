using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 1 Euro Filter의 런타임 튜닝 설정 싱글턴 컴포넌트.
    /// UI Toolkit 패널에서 값을 변경하면 이 컴포넌트에 반영되어 렌더 시스템들이 즉시 적용한다.
    /// </summary>
    public struct OneEuroFilterSettings : IComponentData
    {
        public int Enabled;

        // 0 = 2D overlay (정규화), 1 = 3D world (미터). Face는 월드 미지원이라 2D 폴백.
        public int RenderMode;

        // Hand
        public float HandMinCutoff;
        public float HandBeta;

        // Face
        public float FaceMinCutoff;
        public float FaceBeta;

        // Pose
        public float PoseMinCutoff;
        public float PoseBeta;

        // Common Z & Derivative
        public float ZMinCutoff;
        public float ZBeta;
        public float DerivativeCutoffHz;

        public static OneEuroFilterSettings Default => new()
        {
            Enabled = 1,
            RenderMode = 0,
            HandMinCutoff = 1.0f,
            HandBeta = 0.007f,
            FaceMinCutoff = 0.6f,
            FaceBeta = 0.004f,
            PoseMinCutoff = 0.5f,
            PoseBeta = 0.010f,
            ZMinCutoff = 0.3f,
            ZBeta = 0.002f,
            DerivativeCutoffHz = 1.0f,
        };
    }
}
