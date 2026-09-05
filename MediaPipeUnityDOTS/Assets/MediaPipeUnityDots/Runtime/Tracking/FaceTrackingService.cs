using System;
using System.Runtime.InteropServices;
using MediaPipeUnityDots.Runtime.Input;
using MediaPipeUnityDots.Runtime.Interop;
using UnityEngine;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// 네이티브 face tracker의 유일한 핸들 소유자.
    /// create -> submit/poll 반복 -> destroy 순서를 보장한다.
    /// </summary>
    public sealed class FaceTrackingService : IDisposable
    {
        private readonly string _modelPath;
        private readonly int _numFaces;
        private readonly float _minDetectionConfidence;
        private readonly float _minTrackingConfidence;
        private readonly FaceTrackingSnapshot _snapshot;
        private readonly MonotonicTimestampGenerator _timestampGenerator;

        private IntPtr _trackerHandle;
        private Color32[] _flipBuffer;
        private bool _disposed;

        public FaceTrackingService(string modelPath, int numFaces = 1, float minDetectionConfidence = 0.5f, float minTrackingConfidence = 0.5f)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                throw new ArgumentException("modelPath must not be null or empty.", nameof(modelPath));
            }

            if (numFaces < 1)
            {
                numFaces = 1;
            }
            else if (numFaces > MpudFaceResult.MaxFaces)
            {
                numFaces = MpudFaceResult.MaxFaces;
            }

            _modelPath = modelPath;
            _numFaces = numFaces;
            _minDetectionConfidence = minDetectionConfidence;
            _minTrackingConfidence = minTrackingConfidence;
            _snapshot = new FaceTrackingSnapshot();
            _timestampGenerator = new MonotonicTimestampGenerator();

            CreateTracker();
        }

        public bool IsCreated => _trackerHandle != IntPtr.Zero;

        public bool LatestIsValid => _snapshot.IsValid;

        public int LatestFaceCount => _snapshot.FaceCount;

        public int LatestLandmarkCount => _snapshot.LandmarkCount;

        public long LatestTimestampUs => _snapshot.TimestampUs;

        public long LatestFrameCount => _snapshot.FrameCount;

        /// <summary>
        /// 얼굴 프레임을 제출하고 결과를 폴링한다.
        /// flipVertically=true이면 내부 flip 버퍼에 상하 반전 후 submit.
        /// submit 성공 시 즉시 poll하여 스냅샷을 갱신한다.
        /// </summary>
        public void SubmitAndPoll(Color32[] pixels, int width, int height, bool flipVertically = true)
        {
            ThrowIfDisposed();

            if (!IsCreated)
            {
                Debug.LogError("[MPUD] face submit skipped because tracker is not created.");
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

                var submitStatus = MpudFaceBridge.mpud_submit_face_frame(_trackerHandle, ref frame);
                if (submitStatus != MpudStatus.Ok)
                {
                    Debug.LogError($"[MPUD] submit_face_frame failed ({submitStatus}): {MpudFaceBridge.GetLastFaceError()}");
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

            var pollStatus = MpudFaceBridge.mpud_try_get_latest_face_result(_trackerHandle, out var result);
            if (pollStatus == MpudStatus.Ok)
            {
                _snapshot.UpdateFrom(ref result);
                return;
            }

            if (pollStatus == MpudStatus.NoResult)
            {
                Debug.LogWarning("[MPUD] try_get_latest_face_result returned MPUD_NO_RESULT immediately after a successful submit.");
                return;
            }

            Debug.LogError($"[MPUD] try_get_latest_face_result failed ({pollStatus}): {MpudFaceBridge.GetLastFaceError()}");
        }

        /// <summary>
        /// 지정 얼굴의 최신 landmark를 caller-owned destination에 복사한다.
        /// </summary>
        public int CopyLatestFaceLandmarksTo(int face, MpudNormalizedLandmark[] destination)
        {
            ThrowIfDisposed();
            return _snapshot.CopyFaceLandmarksTo(face, destination);
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
                Marshal.SizeOf<MpudFaceResult>() == MpudFaceResult.ExpectedSize,
                "MpudFaceResult ABI mismatch with native bridge.");
            var modelPathNative = MarshalStringToUtf8(_modelPath);
            try
            {
                var config = new MpudFaceTrackerConfig
                {
                    modelAssetPath = modelPathNative,
                    numFaces = _numFaces,
                    minDetectionConfidence = _minDetectionConfidence,
                    minTrackingConfidence = _minTrackingConfidence,
                };

                var createStatus = MpudFaceBridge.mpud_create_face_tracker(ref config, out var trackerHandle);
                if (createStatus != MpudStatus.Ok)
                {
                    throw new InvalidOperationException($"[MPUD] create_face_tracker failed ({createStatus}): {MpudFaceBridge.GetLastFaceError()}");
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

            MpudFaceBridge.mpud_destroy_face_tracker(_trackerHandle);
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
                throw new ObjectDisposedException(nameof(FaceTrackingService));
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
