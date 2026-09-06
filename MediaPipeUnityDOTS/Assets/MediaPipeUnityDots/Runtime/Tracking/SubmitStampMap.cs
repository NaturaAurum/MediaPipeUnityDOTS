using System.Collections.Generic;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// 제출 시각→캡처 스탬프 매핑. 네이티브 결과가 echo한 timestampUs로 입력 캡처를 역추적한다.
    /// 미적중 시 기본 스탬프(CaptureId=0)로 떨어져 게이트에서 거부된다.
    /// </summary>
    public sealed class SubmitStampMap
    {
        private const int Capacity = 32;

        private readonly Dictionary<long, CaptureStamp> _map = new();
        private readonly Queue<long> _order = new();

        public void Register(long timestampUs, CaptureStamp stamp)
        {
            if (!_map.ContainsKey(timestampUs))
            {
                _order.Enqueue(timestampUs);
            }

            _map[timestampUs] = stamp;
            while (_map.Count > Capacity && _order.Count > 0)
            {
                _map.Remove(_order.Dequeue());
            }
        }

        public bool TryTake(long timestampUs, out CaptureStamp stamp)
        {
            if (_map.TryGetValue(timestampUs, out stamp))
            {
                _map.Remove(timestampUs);
                return true;
            }

            stamp = default;
            return false;
        }

        public void Clear()
        {
            _map.Clear();
            _order.Clear();
        }
    }
}
