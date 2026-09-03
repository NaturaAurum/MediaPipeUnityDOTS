using System;
using MediaPipeUnityDots.Runtime.Interop;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// 한 손의 최신 추적 결과를 보관하는 Unity-owned 스냅샷.
    /// 내부 배열은 외부에 직접 노출하지 않고 copy API만 제공한다.
    /// </summary>
    public sealed class HandTrackingSnapshot
    {
        private const int LandmarkCapacity = 21;

        private readonly MpudNormalizedLandmark[] _landmarks;

        public HandTrackingSnapshot()
        {
            _landmarks = new MpudNormalizedLandmark[LandmarkCapacity];
            ResetToEmpty();
        }

        public bool IsValid { get; private set; }

        public int Handedness { get; private set; }

        public float Score { get; private set; }

        public int LandmarkCount { get; private set; }

        public long TimestampUs { get; private set; }

        public long FrameCount { get; private set; }

        /// <summary>
        /// MpudHandResult로부터 스냅샷을 갱신한다.
        /// nativeResult.GetLandmark(i)로 fixed float landmarkData[105]를 언팩한다.
        /// isValid=0이면 empty state 정규화를 적용한다.
        /// FrameCount를 1 증가시킨다.
        /// </summary>
        internal void UpdateFrom(ref MpudHandResult nativeResult)
        {
            FrameCount++;

            if (nativeResult.isValid == 0)
            {
                SetInvalidState(nativeResult.timestampUs);
                Array.Clear(_landmarks, 0, _landmarks.Length);
                return;
            }

            var landmarkCount = nativeResult.landmarkCount;
            if (landmarkCount < 0)
            {
                landmarkCount = 0;
            }
            else if (landmarkCount > LandmarkCapacity)
            {
                landmarkCount = LandmarkCapacity;
            }

            IsValid = true;
            Handedness = nativeResult.handedness;
            Score = nativeResult.score;
            LandmarkCount = landmarkCount;
            TimestampUs = nativeResult.timestampUs;

            for (var i = 0; i < landmarkCount; i++)
            {
                _landmarks[i] = nativeResult.GetLandmark(i);
            }

            if (landmarkCount < LandmarkCapacity)
            {
                Array.Clear(_landmarks, landmarkCount, LandmarkCapacity - landmarkCount);
            }
        }

        /// <summary>
        /// caller-owned destination에 landmark를 복사한다.
        /// destination은 최소 21 capacity여야 한다.
        /// 반환값은 복사된 landmark 수.
        /// </summary>
        public int CopyLandmarksTo(MpudNormalizedLandmark[] destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (destination.Length < LandmarkCapacity)
            {
                throw new ArgumentException("destination length must be at least 21.", nameof(destination));
            }

            if (LandmarkCount > 0)
            {
                Array.Copy(_landmarks, destination, LandmarkCount);
            }

            if (LandmarkCount < LandmarkCapacity)
            {
                Array.Clear(destination, LandmarkCount, LandmarkCapacity - LandmarkCount);
            }

            return LandmarkCount;
        }

        /// <summary>
        /// reset/recreate 직후 empty state로 초기화한다.
        /// TimestampUs=0, LandmarkCount=0, IsValid=false, Handedness=-1, Score=0, FrameCount=0
        /// </summary>
        public void ResetToEmpty()
        {
            Array.Clear(_landmarks, 0, _landmarks.Length);

            IsValid = false;
            Handedness = -1;
            Score = 0f;
            LandmarkCount = 0;
            TimestampUs = 0;
            FrameCount = 0;
        }

        private void SetInvalidState(long timestampUs)
        {
            IsValid = false;
            Handedness = -1;
            Score = 0f;
            LandmarkCount = 0;
            TimestampUs = timestampUs;
        }
    }
}
