using System.Diagnostics;

namespace MediaPipeUnityDots.Runtime.Input
{
    /// <summary>
    /// Stopwatch 기반 단조 증가 타임스탬프 생성기.
    /// 네이티브 브리지의 strict monotonic increasing 요구사항을 보장한다.
    /// </summary>
    public sealed class MonotonicTimestampGenerator
    {
        private readonly Stopwatch _stopwatch;

        private long _lastTimestampUs;

        public MonotonicTimestampGenerator()
        {
            _stopwatch = Stopwatch.StartNew();
            _lastTimestampUs = -1;
        }

        public long NextTimestampUs()
        {
            var timestampUs = _stopwatch.ElapsedTicks * 1000000L / Stopwatch.Frequency;
            if (timestampUs <= _lastTimestampUs)
            {
                timestampUs = _lastTimestampUs + 1;
            }

            _lastTimestampUs = timestampUs;
            return timestampUs;
        }

        public long PeekTimestampUs()
        {
            var timestampUs = _stopwatch.ElapsedTicks * 1000000L / Stopwatch.Frequency;
            return timestampUs <= _lastTimestampUs ? _lastTimestampUs : timestampUs;
        }

        internal void ResetForRecreate()
        {
            _stopwatch.Restart();
            _lastTimestampUs = -1;
        }
    }
}
