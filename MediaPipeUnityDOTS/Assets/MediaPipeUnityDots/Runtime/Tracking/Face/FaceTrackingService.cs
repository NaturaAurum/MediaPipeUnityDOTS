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
        private readonly SubmitStampMap _stampMap = new();
        private readonly SubmitGate _submitGate = new();
        private readonly TrackerWorker<MpudFaceResult> _worker;
        private bool _disposed;

        public FaceTrackingService(
            string modelPath,
            int numFaces = 1,
            float minDetectionConfidence = 0.5f,
            float minTrackingConfidence = 0.5f)
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

            Debug.Assert(
                Marshal.SizeOf<MpudFaceResult>() == MpudFaceResult.ExpectedSize,
                "MpudFaceResult ABI mismatch with native bridge.");
            _modelPath = modelPath;
            _numFaces = numFaces;
            _minDetectionConfidence = minDetectionConfidence;
            _minTrackingConfidence = minTrackingConfidence;
            _snapshot = new FaceTrackingSnapshot();
            _timestampGenerator = new MonotonicTimestampGenerator();
            _worker = new TrackerWorker<MpudFaceResult>(
                "FaceTracker",
                new FaceWorkerBody(modelPath, numFaces, minDetectionConfidence, minTrackingConfidence));
        }

        public bool LatestIsValid => _snapshot.IsValid;

        public int LatestFaceCount => _snapshot.FaceCount;

        public int LatestLandmarkCount => _snapshot.LandmarkCount;

        public long LatestTimestampUs => _snapshot.TimestampUs;

        public long LatestFrameCount => _snapshot.FrameCount;

        public long LatestCaptureId => _snapshot.CaptureId;

        public long LatestCaptureTimestampUs => _snapshot.CaptureTimestampUs;

        public long LatestCaptureEpoch => _snapshot.CaptureEpoch;

        public int LatestBlendshapeCount => _snapshot.FaceCount > 0 ? _snapshot.GetBlendshapeCount(0) : 0;

        /// <summary>
        /// 새 캡처를 제출한다. 준비·유휴 상태이고 새로운 CaptureStamp일 때만 접수한다.
        /// 호출자 픽셀은 워커 소유 슬롯에 복사한다.
        /// </summary>
        public bool TrySubmit(Color32[] pixels, int width, int height, bool flipVertically, CaptureStamp stamp)
        {
            ThrowIfDisposed();

            // IsAccepting 확인 전에는 호출자 버퍼를 읽거나 복사하지 않는다.
            if (!_worker.IsAccepting)
            {
                return false;
            }

            if (stamp.CaptureId == 0)
            {
                return false;
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

            if (!_submitGate.Offer(stamp))
            {
                return false;
            }

            _submitGate.CopyInput(pixels, pixelCount);
            var submitTimestampUs = _timestampGenerator.NextTimestampUs();
            var item = new TrackerWorkItem
            {
                Stamp = stamp,
                Pixels = _submitGate.Input,
                Width = width,
                Height = height,
                FlipVertically = flipVertically,
                SubmitTimestampUs = submitTimestampUs,
            };

            if (!_worker.TrySubmit(in item))
            {
                _submitGate.Reset();
                return false;
            }

            _stampMap.Register(submitTimestampUs, stamp);
            return true;
        }

        /// <summary>
        /// 완료된 결과를 한 번만 가져온다. 오류 문자열은 워커에서 복사되어 메인에서 보고한다.
        /// </summary>
        public bool TryTakeCompleted()
        {
            ThrowIfDisposed();

            if (!_worker.TryTake(out var completion))
            {
                return false;
            }

            if (!completion.Ok)
            {
                MpudLog.Error(completion.Error ?? "[MPUD] face worker failed.");
                return false;
            }

            var result = completion.Result;
            _snapshot.UpdateFrom(ref result);
            _stampMap.TryTake(result.timestampUs, out var stamp);
            _snapshot.SetCaptureStamp(stamp);
            return true;
        }

        public int CopyLatestFaceLandmarksTo(int face, MpudNormalizedLandmark[] destination)
        {
            ThrowIfDisposed();
            return _snapshot.CopyFaceLandmarksTo(face, destination);
        }

        public int CopyLatestFaceBlendshapesTo(int face, float[] destination)
        {
            ThrowIfDisposed();
            return _snapshot.CopyFaceBlendshapesTo(face, destination);
        }

        /// <summary>
        /// 워커 세대를 먼저 무효화한 뒤 메인 스레드 스냅샷을 비운다.
        /// </summary>
        public void ResetTracker()
        {
            ThrowIfDisposed();
            _worker.RequestReset();
            _snapshot.ResetToEmpty();
            _stampMap.Clear();
            _submitGate.Reset();
            _timestampGenerator.ResetForRecreate();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _worker.Dispose();
            if (_worker.ShutdownError != null)
            {
                MpudLog.Error($"[MPUD] face worker shutdown: {_worker.ShutdownError}");
            }
        }

        // 네이티브 호출 전담. 모든 메서드는 워커 스레드에서 실행된다(Unity API 호출 금지).
        private sealed class FaceWorkerBody : ITrackerWorkerBody<MpudFaceResult>
        {
            private readonly string _modelPath;
            private readonly int _numFaces;
            private readonly float _minDetectionConfidence;
            private readonly float _minTrackingConfidence;
            private IntPtr _trackerHandle;
            private Color32[] _flipBuffer;

            public FaceWorkerBody(string modelPath, int numFaces, float minDetectionConfidence, float minTrackingConfidence)
            {
                _modelPath = modelPath;
                _numFaces = numFaces;
                _minDetectionConfidence = minDetectionConfidence;
                _minTrackingConfidence = minTrackingConfidence;
            }

            public void Create()
            {
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
                        throw new InvalidOperationException(
                            $"[MPUD] create_face_tracker failed ({createStatus}): {MpudFaceBridge.GetLastFaceError()}");
                    }

                    _trackerHandle = trackerHandle;
                }
                finally
                {
                    Marshal.FreeHGlobal(modelPathNative);
                }
            }

            public bool Invoke(in TrackerWorkItem item, out MpudFaceResult completed, out string error)
            {
                completed = default;
                error = null;

                var pixelCount = checked(item.Width * item.Height);
                if (item.Pixels == null || item.Pixels.Length < pixelCount)
                {
                    error = "[MPUD] face submit skipped: invalid input buffer.";
                    return false;
                }

                var submitPixels = item.Pixels;
                if (item.FlipVertically)
                {
                    if (_flipBuffer == null || _flipBuffer.Length != pixelCount)
                    {
                        _flipBuffer = new Color32[pixelCount];
                    }

                    ImageFrameConverter.FlipVertical(item.Pixels, _flipBuffer, item.Width, item.Height);
                    submitPixels = _flipBuffer;
                }

                GCHandle pinnedHandle = default;
                try
                {
                    pinnedHandle = GCHandle.Alloc(submitPixels, GCHandleType.Pinned);
                    var frame = ImageFrameConverter.CreateFrame(
                        pinnedHandle,
                        item.Width,
                        item.Height,
                        item.SubmitTimestampUs);

                    var submitStatus = MpudFaceBridge.mpud_submit_face_frame(_trackerHandle, ref frame);
                    if (submitStatus != MpudStatus.Ok)
                    {
                        error = $"[MPUD] submit_face_frame failed ({submitStatus}): {MpudFaceBridge.GetLastFaceError()}";
                        return false;
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
                    completed = result;
                    return true;
                }

                if (pollStatus != MpudStatus.NoResult)
                {
                    error = $"[MPUD] try_get_latest_face_result failed ({pollStatus}): {MpudFaceBridge.GetLastFaceError()}";
                }

                return false;
            }

            public void ResetBody()
            {
                DestroyTracker();
                _flipBuffer = null;
                Create();
            }

            public void Destroy()
            {
                DestroyTracker();
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

            private static IntPtr MarshalStringToUtf8(string value)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(value);
                var ptr = Marshal.AllocHGlobal(bytes.Length + 1);
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                Marshal.WriteByte(ptr, bytes.Length, 0);
                return ptr;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(FaceTrackingService));
            }
        }
    }
}
