using System;
using System.Runtime.InteropServices;
using MediaPipeUnityDots.Runtime.Input;
using MediaPipeUnityDots.Runtime.Interop;
using UnityEngine;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// 네이티브 holistic tracker의 유일한 핸들 소유자.
    /// create -> submit/poll 반복 -> destroy 순서를 보장한다.
    /// </summary>
    public sealed class HolisticTrackingService : IDisposable
    {
        private const float MinDetectionConfidence = 0.5f;
        private const float MinPresenceConfidence = 0.5f;

        private readonly string _modelPath;
        private readonly float _minDetectionConfidence;
        private readonly float _minPresenceConfidence;
        private readonly HolisticTrackingSnapshot _snapshot;
        private readonly MonotonicTimestampGenerator _timestampGenerator;

        private IntPtr _trackerHandle;
        private Color32[] _flipBuffer;
        private bool _disposed;

        public HolisticTrackingService(string modelPath, float minDetectionConfidence = 0.5f, float minPresenceConfidence = 0.5f)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                throw new ArgumentException("modelPath must not be null or empty.", nameof(modelPath));
            }

            _modelPath = modelPath;
            _minDetectionConfidence = minDetectionConfidence;
            _minPresenceConfidence = minPresenceConfidence;
            _snapshot = new HolisticTrackingSnapshot();
            _timestampGenerator = new MonotonicTimestampGenerator();

            CreateTracker();
        }

        public bool IsCreated => _trackerHandle != IntPtr.Zero;

        public bool LatestIsValid => _snapshot.IsValid;

        public int LatestFaceLandmarkCount => _snapshot.FaceLandmarkCount;

        public int LatestPoseLandmarkCount => _snapshot.PoseLandmarkCount;

        public int LatestLeftHandLandmarkCount => _snapshot.LeftHandLandmarkCount;

        public int LatestRightHandLandmarkCount => _snapshot.RightHandLandmarkCount;

        public long LatestTimestampUs => _snapshot.TimestampUs;

        public long LatestFrameCount => _snapshot.FrameCount;

        /// <summary>
        /// 프레임을 제출하고 결과를 폴링한다.
        /// flipVertically=true이면 내부 flip 버퍼에 상하 반전 후 submit.
        /// submit 성공 시 즉시 poll하여 스냅샷을 갱신한다.
        /// </summary>
        public void SubmitAndPoll(Color32[] pixels, int width, int height, bool flipVertically = true)
        {
            ThrowIfDisposed();

            if (!IsCreated)
            {
                MpudLog.Error("[MPUD] holistic submit skipped because tracker is not created.");
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

                var submitStatus = MpudHolisticBridge.mpud_submit_holistic_frame(_trackerHandle, ref frame);
                if (submitStatus != MpudStatus.Ok)
                {
                    MpudLog.Error($"[MPUD] submit_holistic_frame failed ({submitStatus}): {MpudHolisticBridge.GetLastHolisticError()}");
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

            var pollStatus = MpudHolisticBridge.mpud_try_get_latest_holistic_result(_trackerHandle, out var result);
            if (pollStatus == MpudStatus.Ok)
            {
                _snapshot.UpdateFrom(ref result);
                return;
            }

            if (pollStatus == MpudStatus.NoResult)
            {
                MpudLog.Warning("[MPUD] try_get_latest_holistic_result returned MPUD_NO_RESULT immediately after a successful submit.");
                return;
            }

            MpudLog.Error($"[MPUD] try_get_latest_holistic_result failed ({pollStatus}): {MpudHolisticBridge.GetLastHolisticError()}");
        }

        public int CopyLatestFaceTo(MpudNormalizedLandmark[] destination)
        {
            ThrowIfDisposed();
            return _snapshot.CopyFaceTo(destination);
        }

        public int CopyLatestPoseTo(MpudNormalizedLandmark[] destination)
        {
            ThrowIfDisposed();
            return _snapshot.CopyPoseTo(destination);
        }

        public int CopyLatestLeftHandTo(MpudNormalizedLandmark[] destination)
        {
            ThrowIfDisposed();
            return _snapshot.CopyLeftHandTo(destination);
        }

        public int CopyLatestRightHandTo(MpudNormalizedLandmark[] destination)
        {
            ThrowIfDisposed();
            return _snapshot.CopyRightHandTo(destination);
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
                Marshal.SizeOf<MpudHolisticResult>() == MpudHolisticResult.ExpectedSize,
                "MpudHolisticResult ABI mismatch with native bridge.");
            var modelPathNative = MarshalStringToUtf8(_modelPath);
            try
            {
                var config = new MpudHolisticTrackerConfig
                {
                    modelAssetPath = modelPathNative,
                    minDetectionConfidence = _minDetectionConfidence,
                    minPresenceConfidence = _minPresenceConfidence,
                };

                var createStatus = MpudHolisticBridge.mpud_create_holistic_tracker(ref config, out var trackerHandle);
                if (createStatus != MpudStatus.Ok)
                {
                    throw new InvalidOperationException($"[MPUD] create_holistic_tracker failed ({createStatus}): {MpudHolisticBridge.GetLastHolisticError()}");
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

            MpudHolisticBridge.mpud_destroy_holistic_tracker(_trackerHandle);
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
                throw new ObjectDisposedException(nameof(HolisticTrackingService));
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
