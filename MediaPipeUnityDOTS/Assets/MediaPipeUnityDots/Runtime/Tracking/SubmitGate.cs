using System;
using UnityEngine;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// tracker 제출 경계: 중복 캡처 거부 + 워커 소유 입력 슬롯.
    /// 네이티브 호출 없이 테스트 가능하며, Step 3 워커가 그대로 재사용한다.
    /// </summary>
    public sealed class SubmitGate
    {
        private long _acceptedEpoch;
        private long _acceptedId;
        private bool _hasAccepted;
        private Color32[] _input = Array.Empty<Color32>();

        public Color32[] Input => _input;

        /// <summary>
        /// 새 캡처면 true. 같은 (epoch, id) 반복이면 false.
        /// </summary>
        public bool Offer(CaptureStamp stamp)
        {
            if (stamp.CaptureId <= 0 || stamp.CaptureEpoch < 0
                || (_hasAccepted && (stamp.CaptureEpoch < _acceptedEpoch
                    || (stamp.CaptureEpoch == _acceptedEpoch && stamp.CaptureId <= _acceptedId))))
            {
                return false;
            }

            _hasAccepted = true;
            _acceptedEpoch = stamp.CaptureEpoch;
            _acceptedId = stamp.CaptureId;
            return true;
        }

        /// <summary>
        /// 호출자 버퍼를 소유 슬롯에 복사한다. 이후 호출자 배열 변경과 무관하다.
        /// </summary>
        public void CopyInput(Color32[] source, int pixelCount)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (_input.Length != pixelCount)
            {
                _input = new Color32[pixelCount];
            }

            Array.Copy(source, _input, pixelCount);
        }

        public void Reset()
        {
            _hasAccepted = false;
        }
    }
}
