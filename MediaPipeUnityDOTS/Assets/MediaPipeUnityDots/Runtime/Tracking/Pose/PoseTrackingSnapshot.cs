using System;
using MediaPipeUnityDots.Runtime.Interop;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// 최대 2포즈의 최신 추적 결과를 보관하는 Unity-owned 스냅샷.
    /// 내부 배열은 외부에 직접 노출하지 않고 copy API만 제공한다.
    /// </summary>
    public sealed class PoseTrackingSnapshot
    {
        public const int MaxPoses = MpudPoseResult.MaxPoses;

        private const int LandmarkCapacity = MpudPoseResult.LandmarksPerPose;

        private readonly MpudNormalizedLandmark[] _landmarks;
        private readonly MpudNormalizedLandmark[] _worldLandmarks;
        private readonly int[] _landmarkCounts;

        public PoseTrackingSnapshot()
        {
            _landmarks = new MpudNormalizedLandmark[MaxPoses * LandmarkCapacity];
            _worldLandmarks = new MpudNormalizedLandmark[MaxPoses * LandmarkCapacity];
            _landmarkCounts = new int[MaxPoses];
            ResetToEmpty();
        }

        public int PoseCount { get; private set; }

        public bool IsValid => PoseCount > 0;

        public int LandmarkCount => PoseCount > 0 ? _landmarkCounts[0] : 0;

        public long TimestampUs { get; private set; }

        public long FrameCount { get; private set; }

        public long CaptureId { get; private set; }

        public long CaptureTimestampUs { get; private set; }

        public long CaptureEpoch { get; private set; }

        internal void SetCaptureStamp(CaptureStamp stamp)
        {
            CaptureId = stamp.CaptureId;
            CaptureTimestampUs = stamp.CaptureTimestampUs;
            CaptureEpoch = stamp.CaptureEpoch;
        }

        public int GetLandmarkCount(int pose) => IsValidPose(pose) ? _landmarkCounts[pose] : 0;

        /// <summary>
        /// MpudPoseResult로부터 스냅샷을 갱신한다.
        /// pose_count를 MaxPoses로 클램프하고 포즈별 landmark를 언팩한다.
        /// pose_count=0이면 empty state 정규화를 적용한다.
        /// FrameCount를 1 증가시킨다.
        /// </summary>
        internal void UpdateFrom(ref MpudPoseResult nativeResult)
        {
            FrameCount++;

            var poseCount = nativeResult.poseCount;
            if (poseCount < 0)
            {
                poseCount = 0;
            }
            else if (poseCount > MaxPoses)
            {
                poseCount = MaxPoses;
            }

            PoseCount = poseCount;
            TimestampUs = nativeResult.timestampUs;
            Array.Clear(_landmarks, 0, _landmarks.Length);
            Array.Clear(_worldLandmarks, 0, _worldLandmarks.Length);

            for (var p = 0; p < MaxPoses; p++)
            {
                if (p >= poseCount)
                {
                    _landmarkCounts[p] = 0;
                    continue;
                }

                var landmarkCount = nativeResult.GetPoseLandmarkCount(p);
                if (landmarkCount < 0)
                {
                    landmarkCount = 0;
                }
                else if (landmarkCount > LandmarkCapacity)
                {
                    landmarkCount = LandmarkCapacity;
                }

                _landmarkCounts[p] = landmarkCount;

                for (var i = 0; i < landmarkCount; i++)
                {
                    _landmarks[p * LandmarkCapacity + i] = nativeResult.GetPoseLandmark(p, i);
                }
                for (var i = 0; i < landmarkCount; i++)
                {
                    _worldLandmarks[p * LandmarkCapacity + i] = nativeResult.GetPoseWorldLandmark(p, i);
                }
            }
        }

        /// <summary>
        /// 지정 포즈의 landmark를 caller-owned destination에 복사한다.
        /// destination은 최소 33 capacity여야 한다.
        /// 반환값은 복사된 landmark 수.
        /// </summary>
        public int CopyPoseLandmarksTo(int pose, MpudNormalizedLandmark[] destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (destination.Length < LandmarkCapacity)
            {
                throw new ArgumentException("destination length must be at least 33.", nameof(destination));
            }

            if (pose < 0 || pose >= MaxPoses)
            {
                throw new ArgumentOutOfRangeException(nameof(pose));
            }

            var landmarkCount = GetLandmarkCount(pose);
            if (landmarkCount > 0)
            {
                Array.Copy(_landmarks, pose * LandmarkCapacity, destination, 0, landmarkCount);
            }

            if (landmarkCount < LandmarkCapacity)
            {
                Array.Clear(destination, landmarkCount, LandmarkCapacity - landmarkCount);
            }

            return landmarkCount;
        }

        /// <summary>
        /// 지정 포즈의 월드 landmark(미터)를 caller-owned destination에 복사한다.
        /// destination은 최소 33 capacity여야 한다.
        /// 반환값은 복사된 landmark 수.
        /// </summary>
        public int CopyPoseWorldLandmarksTo(int pose, MpudNormalizedLandmark[] destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (destination.Length < LandmarkCapacity)
            {
                throw new ArgumentException("destination length must be at least 33.", nameof(destination));
            }

            if (pose < 0 || pose >= MaxPoses)
            {
                throw new ArgumentOutOfRangeException(nameof(pose));
            }

            var landmarkCount = GetLandmarkCount(pose);
            if (landmarkCount > 0)
            {
                Array.Copy(_worldLandmarks, pose * LandmarkCapacity, destination, 0, landmarkCount);
            }

            if (landmarkCount < LandmarkCapacity)
            {
                Array.Clear(destination, landmarkCount, LandmarkCapacity - landmarkCount);
            }

            return landmarkCount;
        }

        /// <summary>
        /// reset/recreate 직후 empty state로 초기화한다.
        /// TimestampUs=0, PoseCount=0, IsValid=false, FrameCount=0
        /// </summary>
        public void ResetToEmpty()
        {
            Array.Clear(_landmarks, 0, _landmarks.Length);
            Array.Clear(_worldLandmarks, 0, _worldLandmarks.Length);

            PoseCount = 0;
            TimestampUs = 0;
            FrameCount = 0;
            CaptureId = 0;
            CaptureTimestampUs = 0;
            CaptureEpoch = 0;

            for (var p = 0; p < MaxPoses; p++)
            {
                _landmarkCounts[p] = 0;
            }
        }

        private bool IsValidPose(int pose) => pose >= 0 && pose < PoseCount;
    }
}
