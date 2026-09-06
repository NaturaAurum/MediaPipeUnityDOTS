namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// 웹캠 캡처 1건의 식별 스탬프. 제출→결과 매핑과 깊이 시간 정합에 쓴다.
    /// CaptureId=0은 스탬프 없음을 뜻하며 게이트에서 거부된다.
    /// </summary>
    public readonly struct CaptureStamp
    {
        public readonly long CaptureId;
        public readonly long CaptureTimestampUs;
        public readonly long CaptureEpoch;

        public CaptureStamp(long captureId, long captureTimestampUs, long captureEpoch)
        {
            CaptureId = captureId;
            CaptureTimestampUs = captureTimestampUs;
            CaptureEpoch = captureEpoch;
        }
    }
}
