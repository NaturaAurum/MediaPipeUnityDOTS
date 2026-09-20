using System;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// 트래커 폴링 결과의 의미. Waiting은 새 완료가 없다는 뜻이며 검출 실패와 다르다.
    /// </summary>
    public enum TrackingResultStatus
    {
        Waiting = 0,
        Success = 1,
        NoDetection = 2,
        Error = 3,
        Reset = 4,
        Disposed = 5,
        Stale = 6,
    }


    /// <summary>
    /// 최신 결과와 입력 캡처를 연결하는 값 타입 메타데이터.
    /// 배열 소유권은 서비스가 가지며, 읽기는 caller-owned 배열 복사 API로만 제공한다.
    /// </summary>
    public readonly struct TrackingResultMetadata
    {
        public TrackingResultMetadata(
            TrackingResultStatus status,
            long timestampUs,
            long captureId,
            long captureTimestampUs,
            long captureEpoch,
            long frameCount,
            long generation,
            long submittedTimestampUs)
        {
            Status = status;
            TimestampUs = timestampUs;
            CaptureId = captureId;
            CaptureTimestampUs = captureTimestampUs;
            CaptureEpoch = captureEpoch;
            FrameCount = frameCount;
            Generation = generation;
            SubmittedTimestampUs = submittedTimestampUs;
        }

        public TrackingResultStatus Status { get; }
        public long TimestampUs { get; }
        public long CaptureId { get; }
        public long CaptureTimestampUs { get; }
        public long CaptureEpoch { get; }
        public long FrameCount { get; }
        public long Generation { get; }
        public long SubmittedTimestampUs { get; }

        public bool HasResult => Status == TrackingResultStatus.Success
            || Status == TrackingResultStatus.NoDetection;

        public bool HasCapture => CaptureId > 0;
        public bool IsStale => Status == TrackingResultStatus.Stale;
        public bool IsTerminal => Status == TrackingResultStatus.Error
            || Status == TrackingResultStatus.Disposed;

        public static TrackingResultMetadata Empty(TrackingResultStatus status, long generation = 0)
        {
            return new TrackingResultMetadata(status, 0, 0, 0, 0, 0, generation, 0);
        }
    }
}
