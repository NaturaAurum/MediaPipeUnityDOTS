using System;
using MediaPipeUnityDots.Runtime.Interop;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// 최대 4손의 최신 추적 결과를 보관하는 Unity-owned 스냅샷.
    /// 내부 배열은 외부에 직접 노출하지 않고 copy API만 제공한다.
    /// hand 0 접근자(IsValid/Handedness/Score/LandmarkCount)는 단일 손 시절 API 호환용이다.
    /// </summary>
    public sealed class HandTrackingSnapshot
    {
        public const int MaxHands = MpudHandResult.MaxHands;

        private const int LandmarkCapacity = MpudHandResult.LandmarksPerHand;

        private readonly MpudNormalizedLandmark[] _landmarks;
        private readonly int[] _handedness;
        private readonly float[] _scores;
        private readonly int[] _landmarkCounts;

        public HandTrackingSnapshot()
        {
            _landmarks = new MpudNormalizedLandmark[MaxHands * LandmarkCapacity];
            _handedness = new int[MaxHands];
            _scores = new float[MaxHands];
            _landmarkCounts = new int[MaxHands];
            ResetToEmpty();
        }

        public int HandCount { get; private set; }

        public bool IsValid => HandCount > 0;

        public int Handedness => HandCount > 0 ? _handedness[0] : -1;

        public float Score => HandCount > 0 ? _scores[0] : 0f;

        public int LandmarkCount => HandCount > 0 ? _landmarkCounts[0] : 0;

        public long TimestampUs { get; private set; }

        public long FrameCount { get; private set; }

        public int GetHandedness(int hand) => IsValidHand(hand) ? _handedness[hand] : -1;

        public float GetScore(int hand) => IsValidHand(hand) ? _scores[hand] : 0f;

        public int GetLandmarkCount(int hand) => IsValidHand(hand) ? _landmarkCounts[hand] : 0;

        /// <summary>
        /// MpudHandResult로부터 스냅샷을 갱신한다.
        /// hand_count를 MaxHands로 클램프하고 손별 landmark를 언팩한다.
        /// hand_count=0이면 empty state 정규화를 적용한다.
        /// FrameCount를 1 증가시킨다.
        /// </summary>
        internal void UpdateFrom(ref MpudHandResult nativeResult)
        {
            FrameCount++;

            var handCount = nativeResult.handCount;
            if (handCount < 0)
            {
                handCount = 0;
            }
            else if (handCount > MaxHands)
            {
                handCount = MaxHands;
            }

            HandCount = handCount;
            TimestampUs = nativeResult.timestampUs;
            Array.Clear(_landmarks, 0, _landmarks.Length);

            for (var h = 0; h < MaxHands; h++)
            {
                if (h >= handCount)
                {
                    _handedness[h] = -1;
                    _scores[h] = 0f;
                    _landmarkCounts[h] = 0;
                    continue;
                }

                var landmarkCount = nativeResult.GetHandLandmarkCount(h);
                if (landmarkCount < 0)
                {
                    landmarkCount = 0;
                }
                else if (landmarkCount > LandmarkCapacity)
                {
                    landmarkCount = LandmarkCapacity;
                }

                _handedness[h] = nativeResult.GetHandedness(h);
                _scores[h] = nativeResult.GetHandScore(h);
                _landmarkCounts[h] = landmarkCount;

                for (var i = 0; i < landmarkCount; i++)
                {
                    _landmarks[h * LandmarkCapacity + i] = nativeResult.GetHandLandmark(h, i);
                }
            }
        }

        /// <summary>
        /// hand 0의 landmark를 caller-owned destination에 복사한다.
        /// destination은 최소 21 capacity여야 한다.
        /// 반환값은 복사된 landmark 수.
        /// </summary>
        public int CopyLandmarksTo(MpudNormalizedLandmark[] destination)
        {
            return CopyHandLandmarksTo(0, destination);
        }

        /// <summary>
        /// 지정 손의 landmark를 caller-owned destination에 복사한다.
        /// destination은 최소 21 capacity여야 한다.
        /// 반환값은 복사된 landmark 수.
        /// </summary>
        public int CopyHandLandmarksTo(int hand, MpudNormalizedLandmark[] destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (destination.Length < LandmarkCapacity)
            {
                throw new ArgumentException("destination length must be at least 21.", nameof(destination));
            }

            if (hand < 0 || hand >= MaxHands)
            {
                throw new ArgumentOutOfRangeException(nameof(hand));
            }

            var landmarkCount = GetLandmarkCount(hand);
            if (landmarkCount > 0)
            {
                Array.Copy(_landmarks, hand * LandmarkCapacity, destination, 0, landmarkCount);
            }

            if (landmarkCount < LandmarkCapacity)
            {
                Array.Clear(destination, landmarkCount, LandmarkCapacity - landmarkCount);
            }

            return landmarkCount;
        }

        /// <summary>
        /// reset/recreate 직후 empty state로 초기화한다.
        /// TimestampUs=0, HandCount=0, IsValid=false, FrameCount=0
        /// </summary>
        public void ResetToEmpty()
        {
            Array.Clear(_landmarks, 0, _landmarks.Length);

            HandCount = 0;
            TimestampUs = 0;
            FrameCount = 0;

            for (var h = 0; h < MaxHands; h++)
            {
                _handedness[h] = -1;
                _scores[h] = 0f;
                _landmarkCounts[h] = 0;
            }
        }

        private bool IsValidHand(int hand) => hand >= 0 && hand < HandCount;
    }
}
