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
        private readonly HolisticTrackingSnapshot _snapshot;
        private readonly MonotonicTimestampGenerator _timestampGenerator;
        private readonly SubmitStampMap _stampMap = new();
        private readonly SubmitGate _submitGate = new();
        private readonly TrackerWorker<MpudHolisticResult> _worker;
        private bool _disposed;

        public HolisticTrackingService(
            string modelPath,
            float minDetectionConfidence = 0.5f,
            float minPresenceConfidence = 0.5f)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                throw new ArgumentException("modelPath must not be null or empty.", nameof(modelPath));
            }

            Debug.Assert(
                Marshal.SizeOf<MpudHolisticResult>() == MpudHolisticResult.ExpectedSize,
                "MpudHolisticResult ABI mismatch with native bridge.");
            _snapshot = new HolisticTrackingSnapshot();
            _timestampGenerator = new MonotonicTimestampGenerator();
            _worker = new TrackerWorker<MpudHolisticResult>(
                "HolisticTracker",
                new HolisticWorkerBody(modelPath, minDetectionConfidence, minPresenceConfidence));
        }

        public bool LatestIsValid => _snapshot.IsValid;

        public int LatestFaceLandmarkCount => _snapshot.FaceLandmarkCount;

        public int LatestPoseLandmarkCount => _snapshot.PoseLandmarkCount;

        public int LatestLeftHandLandmarkCount => _snapshot.LeftHandLandmarkCount;

        public int LatestRightHandLandmarkCount => _snapshot.RightHandLandmarkCount;

        public long LatestTimestampUs => _snapshot.TimestampUs;

        public long LatestFrameCount => _snapshot.FrameCount;

        public long LatestCaptureId => _snapshot.CaptureId;

        public long LatestCaptureTimestampUs => _snapshot.CaptureTimestampUs;

        public long LatestCaptureEpoch => _snapshot.CaptureEpoch;

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
                MpudLog.Error(completion.Error ?? "[MPUD] holistic worker failed.");
                return false;
            }

            var result = completion.Result;
            _snapshot.UpdateFrom(ref result);
            _stampMap.TryTake(result.timestampUs, out var stamp);
            _snapshot.SetCaptureStamp(stamp);
            return true;
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

        public int CopyLatestPoseWorldTo(MpudNormalizedLandmark[] destination)
        {
            ThrowIfDisposed();
            return _snapshot.CopyPoseWorldTo(destination);
        }

        public int CopyLatestLeftHandWorldTo(MpudNormalizedLandmark[] destination)
        {
            ThrowIfDisposed();
            return _snapshot.CopyLeftHandWorldTo(destination);
        }

        public int CopyLatestRightHandWorldTo(MpudNormalizedLandmark[] destination)
        {
            ThrowIfDisposed();
            return _snapshot.CopyRightHandWorldTo(destination);
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
                MpudLog.Error($"[MPUD] holistic worker shutdown: {_worker.ShutdownError}");
            }
        }

        // 네이티브 호출 전담. 모든 메서드는 워커 스레드에서 실행된다(Unity API 호출 금지).
        private sealed class HolisticWorkerBody : ITrackerWorkerBody<MpudHolisticResult>
        {
            private readonly string _modelPath;
            private readonly float _minDetectionConfidence;
            private readonly float _minPresenceConfidence;
            private IntPtr _trackerHandle;
            private Color32[] _flipBuffer;

            public HolisticWorkerBody(string modelPath, float minDetectionConfidence, float minPresenceConfidence)
            {
                _modelPath = modelPath;
                _minDetectionConfidence = minDetectionConfidence;
                _minPresenceConfidence = minPresenceConfidence;
            }

            public void Create()
            {
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
                        throw new InvalidOperationException(
                            $"[MPUD] create_holistic_tracker failed ({createStatus}): {MpudHolisticBridge.GetLastHolisticError()}");
                    }

                    _trackerHandle = trackerHandle;
                }
                finally
                {
                    Marshal.FreeHGlobal(modelPathNative);
                }
            }

            public bool Invoke(in TrackerWorkItem item, out MpudHolisticResult completed, out string error)
            {
                completed = default;
                error = null;

                var pixelCount = checked(item.Width * item.Height);
                if (item.Pixels == null || item.Pixels.Length < pixelCount)
                {
                    error = "[MPUD] holistic submit skipped: invalid input buffer.";
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

                    var submitStatus = MpudHolisticBridge.mpud_submit_holistic_frame(_trackerHandle, ref frame);
                    if (submitStatus != MpudStatus.Ok)
                    {
                        error = $"[MPUD] submit_holistic_frame failed ({submitStatus}): {MpudHolisticBridge.GetLastHolisticError()}";
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

                var pollStatus = MpudHolisticBridge.mpud_try_get_latest_holistic_result(_trackerHandle, out var result);
                if (pollStatus == MpudStatus.Ok)
                {
                    completed = result;
                    return true;
                }

                if (pollStatus != MpudStatus.NoResult)
                {
                    error = $"[MPUD] try_get_latest_holistic_result failed ({pollStatus}): {MpudHolisticBridge.GetLastHolisticError()}";
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

                MpudHolisticBridge.mpud_destroy_holistic_tracker(_trackerHandle);
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
                throw new ObjectDisposedException(nameof(HolisticTrackingService));
            }
        }
    }
}
