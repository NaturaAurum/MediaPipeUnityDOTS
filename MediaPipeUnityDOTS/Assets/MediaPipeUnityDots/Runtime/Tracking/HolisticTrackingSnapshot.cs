using System;
using MediaPipeUnityDots.Runtime.Interop;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// 단일 holistic 추론의 얼굴/포즈/양손 결과를 보관하는 Unity-owned 스냅샷.
    /// ECS 푸시는 기존 Face/Pose/Hand 싱글턴을 재사용하므로 여기서 버퍼를 직접 들고 있지 않고,
    /// 프로바이더가 부위별 copy API로 읽어 각 싱글턴에 쓴다.
    /// </summary>
    public sealed class HolisticTrackingSnapshot
    {
        private readonly MpudNormalizedLandmark[] _faceLandmarks;
        private readonly MpudNormalizedLandmark[] _poseLandmarks;
        private readonly MpudNormalizedLandmark[] _leftHandLandmarks;
        private readonly MpudNormalizedLandmark[] _rightHandLandmarks;

        public HolisticTrackingSnapshot()
        {
            _faceLandmarks = new MpudNormalizedLandmark[MpudHolisticResult.FaceLandmarks];
            _poseLandmarks = new MpudNormalizedLandmark[MpudHolisticResult.PoseLandmarks];
            _leftHandLandmarks = new MpudNormalizedLandmark[MpudHolisticResult.HandLandmarks];
            _rightHandLandmarks = new MpudNormalizedLandmark[MpudHolisticResult.HandLandmarks];
            ResetToEmpty();
        }

        public int FaceLandmarkCount { get; private set; }

        public int PoseLandmarkCount { get; private set; }

        public int LeftHandLandmarkCount { get; private set; }

        public int RightHandLandmarkCount { get; private set; }

        public bool IsValid => FaceLandmarkCount > 0 || PoseLandmarkCount > 0
            || LeftHandLandmarkCount > 0 || RightHandLandmarkCount > 0;

        public long TimestampUs { get; private set; }

        public long FrameCount { get; private set; }

        /// <summary>
        /// MpudHolisticResult로부터 스냅샷을 갱신한다.
        /// 부위별 개수를 capacity로 클램프하고 landmark를 언팩한다.
        /// FrameCount를 1 증가시킨다.
        /// </summary>
        internal void UpdateFrom(ref MpudHolisticResult nativeResult)
        {
            FrameCount++;

            TimestampUs = nativeResult.timestampUs;

            FaceLandmarkCount = ClampCount(nativeResult.faceLandmarkCount, MpudHolisticResult.FaceLandmarks);
            Array.Clear(_faceLandmarks, 0, _faceLandmarks.Length);
            for (var i = 0; i < FaceLandmarkCount; i++)
            {
                _faceLandmarks[i] = nativeResult.GetFaceLandmark(i);
            }

            PoseLandmarkCount = ClampCount(nativeResult.poseLandmarkCount, MpudHolisticResult.PoseLandmarks);
            Array.Clear(_poseLandmarks, 0, _poseLandmarks.Length);
            for (var i = 0; i < PoseLandmarkCount; i++)
            {
                _poseLandmarks[i] = nativeResult.GetPoseLandmark(i);
            }

            LeftHandLandmarkCount = ClampCount(nativeResult.leftHandLandmarkCount, MpudHolisticResult.HandLandmarks);
            Array.Clear(_leftHandLandmarks, 0, _leftHandLandmarks.Length);
            for (var i = 0; i < LeftHandLandmarkCount; i++)
            {
                _leftHandLandmarks[i] = nativeResult.GetLeftHandLandmark(i);
            }

            RightHandLandmarkCount = ClampCount(nativeResult.rightHandLandmarkCount, MpudHolisticResult.HandLandmarks);
            Array.Clear(_rightHandLandmarks, 0, _rightHandLandmarks.Length);
            for (var i = 0; i < RightHandLandmarkCount; i++)
            {
                _rightHandLandmarks[i] = nativeResult.GetRightHandLandmark(i);
            }
        }

        private static int ClampCount(int count, int capacity)
        {
            if (count < 0)
            {
                return 0;
            }

            return count > capacity ? capacity : count;
        }

        public int CopyFaceTo(MpudNormalizedLandmark[] destination)
        {
            return CopyOut(_faceLandmarks, FaceLandmarkCount, destination);
        }

        public int CopyPoseTo(MpudNormalizedLandmark[] destination)
        {
            return CopyOut(_poseLandmarks, PoseLandmarkCount, destination);
        }

        public int CopyLeftHandTo(MpudNormalizedLandmark[] destination)
        {
            return CopyOut(_leftHandLandmarks, LeftHandLandmarkCount, destination);
        }

        public int CopyRightHandTo(MpudNormalizedLandmark[] destination)
        {
            return CopyOut(_rightHandLandmarks, RightHandLandmarkCount, destination);
        }

        /// <summary>
        /// reset/recreate 직후 empty state로 초기화한다.
        /// </summary>
        public void ResetToEmpty()
        {
            Array.Clear(_faceLandmarks, 0, _faceLandmarks.Length);
            Array.Clear(_poseLandmarks, 0, _poseLandmarks.Length);
            Array.Clear(_leftHandLandmarks, 0, _leftHandLandmarks.Length);
            Array.Clear(_rightHandLandmarks, 0, _rightHandLandmarks.Length);

            FaceLandmarkCount = 0;
            PoseLandmarkCount = 0;
            LeftHandLandmarkCount = 0;
            RightHandLandmarkCount = 0;
            TimestampUs = 0;
            FrameCount = 0;
        }

        private static int CopyOut(MpudNormalizedLandmark[] storage, int count, MpudNormalizedLandmark[] destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (destination.Length < storage.Length)
            {
                throw new ArgumentException("destination is too small.", nameof(destination));
            }

            if (count > 0)
            {
                Array.Copy(storage, destination, count);
            }

            if (count < storage.Length)
            {
                Array.Clear(destination, count, storage.Length - count);
            }

            return count;
        }
    }
}
