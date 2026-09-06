using System;
using MediaPipeUnityDots.Runtime.Interop;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// 최대 2얼굴의 최신 추적 결과를 보관하는 Unity-owned 스냅샷.
    /// 내부 배열은 외부에 직접 노출하지 않고 copy API만 제공한다.
    /// </summary>
    public sealed class FaceTrackingSnapshot
    {
        public const int MaxFaces = MpudFaceResult.MaxFaces;

        private const int LandmarkCapacity = MpudFaceResult.LandmarksPerFace;

        private readonly MpudNormalizedLandmark[] _landmarks;
        private readonly int[] _landmarkCounts;

        public FaceTrackingSnapshot()
        {
            _landmarks = new MpudNormalizedLandmark[MaxFaces * LandmarkCapacity];
            _landmarkCounts = new int[MaxFaces];
            ResetToEmpty();
        }

        public int FaceCount { get; private set; }

        public bool IsValid => FaceCount > 0;

        public int LandmarkCount => FaceCount > 0 ? _landmarkCounts[0] : 0;

        public long TimestampUs { get; private set; }

        public long FrameCount { get; private set; }

        public int GetLandmarkCount(int face) => IsValidFace(face) ? _landmarkCounts[face] : 0;

        /// <summary>
        /// MpudFaceResult로부터 스냅샷을 갱신한다.
        /// face_count를 MaxFaces로 클램프하고 얼굴별 landmark를 언팩한다.
        /// face_count=0이면 empty state 정규화를 적용한다.
        /// FrameCount를 1 증가시킨다.
        /// </summary>
        internal void UpdateFrom(ref MpudFaceResult nativeResult)
        {
            FrameCount++;

            var faceCount = nativeResult.faceCount;
            if (faceCount < 0)
            {
                faceCount = 0;
            }
            else if (faceCount > MaxFaces)
            {
                faceCount = MaxFaces;
            }

            FaceCount = faceCount;
            TimestampUs = nativeResult.timestampUs;
            Array.Clear(_landmarks, 0, _landmarks.Length);

            for (var f = 0; f < MaxFaces; f++)
            {
                if (f >= faceCount)
                {
                    _landmarkCounts[f] = 0;
                    continue;
                }

                var landmarkCount = nativeResult.GetFaceLandmarkCount(f);
                if (landmarkCount < 0)
                {
                    landmarkCount = 0;
                }
                else if (landmarkCount > LandmarkCapacity)
                {
                    landmarkCount = LandmarkCapacity;
                }

                _landmarkCounts[f] = landmarkCount;

                for (var i = 0; i < landmarkCount; i++)
                {
                    _landmarks[f * LandmarkCapacity + i] = nativeResult.GetFaceLandmark(f, i);
                }
            }
        }

        /// <summary>
        /// 지정 얼굴의 landmark를 caller-owned destination에 복사한다.
        /// destination은 최소 478 capacity여야 한다.
        /// 반환값은 복사된 landmark 수.
        /// </summary>
        public int CopyFaceLandmarksTo(int face, MpudNormalizedLandmark[] destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (destination.Length < LandmarkCapacity)
            {
                throw new ArgumentException("destination length must be at least 478.", nameof(destination));
            }

            if (face < 0 || face >= MaxFaces)
            {
                throw new ArgumentOutOfRangeException(nameof(face));
            }

            var landmarkCount = GetLandmarkCount(face);
            if (landmarkCount > 0)
            {
                Array.Copy(_landmarks, face * LandmarkCapacity, destination, 0, landmarkCount);
            }

            if (landmarkCount < LandmarkCapacity)
            {
                Array.Clear(destination, landmarkCount, LandmarkCapacity - landmarkCount);
            }

            return landmarkCount;
        }

        /// <summary>
        /// reset/recreate 직후 empty state로 초기화한다.
        /// TimestampUs=0, FaceCount=0, IsValid=false, FrameCount=0
        /// </summary>
        public void ResetToEmpty()
        {
            Array.Clear(_landmarks, 0, _landmarks.Length);

            FaceCount = 0;
            TimestampUs = 0;
            FrameCount = 0;

            for (var f = 0; f < MaxFaces; f++)
            {
                _landmarkCounts[f] = 0;
            }
        }

        private bool IsValidFace(int face) => face >= 0 && face < FaceCount;
    }
}
