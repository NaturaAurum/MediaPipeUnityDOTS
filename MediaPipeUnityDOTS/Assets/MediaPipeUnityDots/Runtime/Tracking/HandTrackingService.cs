using System;
using System.Runtime.InteropServices;
using System.Text;
using MediaPipeUnityDots.Runtime.Input;
using MediaPipeUnityDots.Runtime.Interop;
using UnityEngine;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// 네이티브 hand tracker의 유일한 핸들 소유자.
    /// create -> start -> submit/poll 반복 -> destroy 순서를 보장한다.
    /// </summary>
    public sealed class HandTrackingService : IDisposable
    {
        private const int NumHands = 1;
        private const float MinDetectionConfidence = 0.5f;
        private const float MinTrackingConfidence = 0.5f;
        private const int RunningModeVideo = 1;

        private readonly string _modelPath;
        private readonly HandTrackingSnapshot _snapshot;
        private readonly MonotonicTimestampGenerator _timestampGenerator;

        private IntPtr _trackerHandle;
        private Color32[] _flipBuffer;
        private bool _disposed;

        public HandTrackingService(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                throw new ArgumentException("modelPath must not be null or empty.", nameof(modelPath));
            }

            _modelPath = modelPath;
            _snapshot = new HandTrackingSnapshot();
            _timestampGenerator = new MonotonicTimestampGenerator();

            CreateAndStartTracker();
        }

        public bool IsCreated => _trackerHandle != IntPtr.Zero;

        public bool LatestIsValid => _snapshot.IsValid;

        public int LatestHandedness => _snapshot.Handedness;

        public float LatestScore => _snapshot.Score;

        public int LatestLandmarkCount => _snapshot.LandmarkCount;

        public long LatestTimestampUs => _snapshot.TimestampUs;

        public long LatestFrameCount => _snapshot.FrameCount;

        /// <summary>
        /// 웹캠 프레임을 제출하고 결과를 폴링한다.
        /// flipVertically=true이면 내부 flip 버퍼에 상하 반전 후 submit.
        /// submit 성공 시 즉시 poll하여 스냅샷을 갱신한다.
        /// </summary>
        public void SubmitAndPoll(Color32[] pixels, int width, int height, bool flipVertically = true)
        {
            ThrowIfDisposed();

            if (!IsCreated)
            {
                Debug.LogError("[MPUD] submit skipped because tracker is not created.");
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

                var submitStatus = MpudBridge.mpud_submit_frame(_trackerHandle, ref frame);
                if (submitStatus != MpudStatus.Ok)
                {
                    Debug.LogError($"[MPUD] submit_frame failed ({submitStatus}): {MpudBridge.GetLastError()}");
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

            var pollStatus = MpudBridge.mpud_try_get_latest_result(_trackerHandle, out var result);
            if (pollStatus == MpudStatus.Ok)
            {
                _snapshot.UpdateFrom(ref result);
                return;
            }

            if (pollStatus == MpudStatus.NoResult)
            {
                Debug.LogWarning("[MPUD] try_get_latest_result returned MPUD_NO_RESULT immediately after a successful submit.");
                return;
            }

            Debug.LogError($"[MPUD] try_get_latest_result failed ({pollStatus}): {MpudBridge.GetLastError()}");
        }

        /// <summary>
        /// 최신 스냅샷의 landmark를 caller-owned destination에 복사한다.
        /// HandTrackingSnapshot.CopyLandmarksTo 위임.
        /// </summary>
        public int CopyLatestLandmarksTo(MpudNormalizedLandmark[] destination)
        {
            ThrowIfDisposed();
            return _snapshot.CopyLandmarksTo(destination);
        }

        /// <summary>
        /// tracker를 destroy + recreate한다.
        /// snapshot, timestampGen, flipBuffer를 모두 초기화한다.
        /// timestamp generator reset은 이 경로에서만 수행한다.
        /// </summary>
        public void ResetTracker()
        {
            ThrowIfDisposed();

            DestroyTracker();
            _snapshot.ResetToEmpty();
            _flipBuffer = null;

            CreateAndStartTracker();
            _timestampGenerator.ResetForRecreate();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            DestroyTracker();
            _flipBuffer = null;
            _disposed = true;
        }

        private void CreateAndStartTracker()
        {
            var modelPathNative = MarshalStringToUtf8(_modelPath);
            try
            {
                var config = new MpudHandTrackerConfig
                {
                    modelAssetPath = modelPathNative,
                    numHands = NumHands,
                    minDetectionConfidence = MinDetectionConfidence,
                    minTrackingConfidence = MinTrackingConfidence,
                    runningMode = RunningModeVideo,
                };

                var createStatus = MpudBridge.mpud_create_hand_tracker(ref config, out var trackerHandle);
                if (createStatus != MpudStatus.Ok)
                {
                    throw new InvalidOperationException($"[MPUD] create_hand_tracker failed ({createStatus}): {MpudBridge.GetLastError()}");
                }

                var startStatus = MpudBridge.mpud_start_hand_tracker(trackerHandle);
                if (startStatus != MpudStatus.Ok)
                {
                    var error = MpudBridge.GetLastError();
                    MpudBridge.mpud_destroy_hand_tracker(trackerHandle);
                    throw new InvalidOperationException($"[MPUD] start_hand_tracker failed ({startStatus}): {error}");
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

            MpudBridge.mpud_destroy_hand_tracker(_trackerHandle);
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
                throw new ObjectDisposedException(nameof(HandTrackingService));
            }
        }

        private static IntPtr MarshalStringToUtf8(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var ptr = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            Marshal.WriteByte(ptr, bytes.Length, 0);
            return ptr;
        }
    }
}
