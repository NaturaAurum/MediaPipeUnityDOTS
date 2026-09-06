namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 깊이 샘플 사용 판정. FrameCount 차이는 쓰지 않고 실제 시간·세대·스탬프로만 검사한다.
    /// 렌더 Burst 시스템에서 호출되므로 managed API를 쓰지 않는다.
    /// </summary>
    public static class DepthSampleGate
    {
        public static bool IsStamped(long captureId) => captureId != 0;

        public static bool IsSameEpoch(long epochA, long epochB) => epochA == epochB;

        public static bool IsFresh(long nowUs, long sampleCaptureUs, long maxAgeUs)
        {
            return maxAgeUs >= 0 && sampleCaptureUs <= nowUs && nowUs - sampleCaptureUs <= maxAgeUs;
        }

        public static bool IsAligned(long landmarkCaptureUs, long depthCaptureUs, long maxDeltaUs)
        {
            if (maxDeltaUs < 0)
            {
                return false;
            }

            var delta = landmarkCaptureUs - depthCaptureUs;
            if (delta < 0)
            {
                delta = -delta;
            }

            return delta <= maxDeltaUs;
        }
    }
}
