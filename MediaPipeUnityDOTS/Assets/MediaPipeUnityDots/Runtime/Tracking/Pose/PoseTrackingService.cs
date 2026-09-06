using System;
using System.Runtime.InteropServices;
using MediaPipeUnityDots.Runtime.Input;
using MediaPipeUnityDots.Runtime.Interop;
using UnityEngine;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// 네이티브 pose tracker의 유일한 핸들 소유자.
    /// create -> submit/poll 반복 -> destroy 순서를 보장한다.
    /// </summary>
    public sealed class PoseTrackingService : IDisposable
    {
        private const float MinDetectionConfidence = 0.5f;
        private const float MinTrackingConfidence = 0.5f;

        private readonly string _modelPath;
        private readonly int _numPoses;
        private readonly float _minDetectionConfidence;
        private readonly float _minTrackingConfidence;
        private readonly PoseTrackingSnapshot _snapshot;
        private readonly MonotonicTimestampGenerator _timestampGenerator;

        private IntPtr _trackerHandle;
        private Color32[] _flipBuffer;
        private bool _disposed;

        public PoseTrackingService(string modelPath, int numPoses = 1, float minDetectionConfidence = 0.5f, float minTrackingConfidence = 0.5f)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                throw new ArgumentException("modelPath must not be null or empty.", nameof(modelPath));
            }

            if (numPoses < 1)
            {
                numPoses = 1;
            }
            else if (numPoses > MpudPoseResult.MaxPoses)
            {
                numPoses = MpudPoseResult.MaxPoses;
            }

            _modelPath = modelPath;
            _numPoses = numPoses;
            _minDetectionConfidence = minDetectionConfidence;
            _minTrackingConfidence = minTrackingConfidence;
            _snapshot = new PoseTrackingSnapshot();
            _timestampGenerator = new MonotonicTimestampGenerator();

            CreateTracker();
        }

        public bool IsCreated => _trackerHandle != IntPtr.Zero;

        public bool LatestIsValid => _snapshot.IsValid;

        public int LatestPoseCount => _snapshot.PoseCount;

        public int LatestLandmarkCount => _snapshot.LandmarkCount;

        public long LatestTimestampUs => _snapshot.TimestampUs;

        public long LatestFrameCount => _snapshot.FrameCount;

        /// <summary>
        /// 포즈 프레임을 제출하고 결과를 폴링한다.
        /// flipVertically=true이면 내부 flip 버퍼에 상하 반전 후 submit.
        /// submit 성공 시 즉시 poll하여 스냅샷을 갱신한다.
        /// </summary>
        public void SubmitAndPoll(Color32[] pixels, int width, int height, bool flipVertically = true)
        {
            ThrowIfDisposed();

            if (!IsCreated)
            {
                MpudLog.Error("[MPUD] pose submit skipped because tracker is not created.");
                return;
            }

            if (pixels == null)
            {
                throw new ArgumentNullException(nameof(pixels));
            }

            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            var pixelCount = checked(width * height);
            if (pixels.Length != pixelCount)
            {
                throw new ArgumentException("pixels length must match width * height.", nameof(pixels));
            }

            var submitPixels = pixels;
            if (flipVertically)
            {
                EnsureFlipBuffer(pixelCount);
                ImageFrameConverter.FlipVertical(pixels, _flipBuffer, width, height);
                submitPixels = _flipBuffer;
            }

            GCHandle pinnedHandle = default;
            try
            {
                pinnedHandle = GCHandle.Alloc(submitPixels, GCHandleType.Pinned);
                var frame = ImageFrameConverter.CreateFrame(
                    pinnedHandle,
                    width,
                    height,
                    _timestampGenerator.NextTimestampUs());

                var submitStatus = MpudPoseBridge.mpud_submit_pose_frame(_trackerHandle, ref frame);
                if (submitStatus != MpudStatus.Ok)
                {
                    MpudLog.Error($"[MPUD] submit_pose_frame failed ({submitStatus}): {MpudPoseBridge.GetLastPoseError()}");
                    return;
                }
            }
            finally
            {
                if (pinnedHandle.IsAllocated)
                {
                    pinnedHandle.Free();
                }
            }

            var pollStatus = MpudPoseBridge.mpud_try_get_latest_pose_result(_trackerHandle, out var result);
            if (pollStatus == MpudStatus.Ok)
            {
                _snapshot.UpdateFrom(ref result);
                return;
            }

            if (pollStatus == MpudStatus.NoResult)
            {
                MpudLog.Warning("[MPUD] try_get_latest_pose_result returned MPUD_NO_RESULT immediately after a successful submit.");
                return;
            }

            MpudLog.Error($"[MPUD] try_get_latest_pose_result failed ({pollStatus}): {MpudPoseBridge.GetLastPoseError()}");
        }

        /// <summary>
        /// 지정 포즈의 최신 landmark를 caller-owned destination에 복사한다.
        /// </summary>
        public int CopyLatestPoseLandmarksTo(int pose, MpudNormalizedLandmark[] destination)
        {
            ThrowIfDisposed();
            return _snapshot.CopyPoseLandmarksTo(pose, destination);
        }

        /// <summary>
        /// 지정 포즈의 최신 월드 landmark(미터)를 caller-owned destination에 복사한다.
        /// </summary>
        public int CopyLatestPoseWorldLandmarksTo(int pose, MpudNormalizedLandmark[] destination)
        {
            ThrowIfDisposed();
            return _snapshot.CopyPoseWorldLandmarksTo(pose, destination);
        }

        /// <summary>
        /// tracker를 destroy + recreate한다.
        /// snapshot, timestampGen, flipBuffer를 모두 초기화한다.
        /// </summary>
        public void ResetTracker()
        {
            ThrowIfDisposed();
            DestroyTracker();
            _snapshot.ResetToEmpty();
            _timestampGenerator.ResetForRecreate();
            _flipBuffer = null;
            CreateTracker();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            DestroyTracker();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private void CreateTracker()
        {
            Debug.Assert(
                Marshal.SizeOf<MpudPoseResult>() == MpudPoseResult.ExpectedSize,
                "MpudPoseResult ABI mismatch with native bridge.");
            var modelPathNative = MarshalStringToUtf8(_modelPath);
            try
            {
                var config = new MpudPoseTrackerConfig
                {
                    modelAssetPath = modelPathNative,
                    numPoses = _numPoses,
                    minDetectionConfidence = _minDetectionConfidence,
                    minTrackingConfidence = _minTrackingConfidence,
                };

                var createStatus = MpudPoseBridge.mpud_create_pose_tracker(ref config, out var trackerHandle);
                if (createStatus != MpudStatus.Ok)
                {
                    throw new InvalidOperationException($"[MPUD] create_pose_tracker failed ({createStatus}): {MpudPoseBridge.GetLastPoseError()}");
                }

                _trackerHandle = trackerHandle;
                _snapshot.ResetToEmpty();
            }
            finally
            {
                Marshal.FreeHGlobal(modelPathNative);
            }
        }

        private void DestroyTracker()
        {
            if (_trackerHandle == IntPtr.Zero)
            {
                return;
            }

            MpudPoseBridge.mpud_destroy_pose_tracker(_trackerHandle);
            _trackerHandle = IntPtr.Zero;
        }

        private void EnsureFlipBuffer(int pixelCount)
        {
            if (_flipBuffer == null || _flipBuffer.Length != pixelCount)
            {
                _flipBuffer = new Color32[pixelCount];
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PoseTrackingService));
            }
        }

        private static IntPtr MarshalStringToUtf8(string value)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            var ptr = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            Marshal.WriteByte(ptr, bytes.Length, 0);
            return ptr;
        }
    }
}
