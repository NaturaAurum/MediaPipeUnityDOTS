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
        private const float MinDetectionConfidence = 0.5f;
        private const float MinTrackingConfidence = 0.5f;
        private const int RunningModeVideo = 1;

        private readonly HandTrackingSnapshot _snapshot;
        private readonly MonotonicTimestampGenerator _timestampGenerator;
        private readonly SubmitStampMap _stampMap = new();
        private readonly SubmitGate _submitGate = new();
        private readonly TrackerWorker<MpudHandResult> _worker;
        private bool _disposed;

        public HandTrackingService(string modelPath, int numHands = 2)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                throw new ArgumentException("modelPath must not be null or empty.", nameof(modelPath));
            }

            if (numHands < 1)
            {
                numHands = 1;
            }
            else if (numHands > MpudHandResult.MaxHands)
            {
                numHands = MpudHandResult.MaxHands;
            }

            Debug.Assert(
                Marshal.SizeOf<MpudHandResult>() == MpudHandResult.ExpectedSize,
                "MpudHandResult ABI mismatch with native bridge.");
            _snapshot = new HandTrackingSnapshot();
            _timestampGenerator = new MonotonicTimestampGenerator();
            _worker = new TrackerWorker<MpudHandResult>("HandTracker", new HandWorkerBody(modelPath, numHands));
        }

        public bool LatestIsValid => _snapshot.IsValid;

        public int LatestHandedness => _snapshot.Handedness;

        public float LatestScore => _snapshot.Score;

        public int LatestLandmarkCount => _snapshot.LandmarkCount;

        public long LatestTimestampUs => _snapshot.TimestampUs;

        public long LatestFrameCount => _snapshot.FrameCount;

        public long LatestCaptureId => _snapshot.CaptureId;

        public long LatestCaptureTimestampUs => _snapshot.CaptureTimestampUs;

        public long LatestCaptureEpoch => _snapshot.CaptureEpoch;

        public int LatestHandCount => _snapshot.HandCount;

        public int GetLatestHandedness(int hand) => _snapshot.GetHandedness(hand);

        public float GetLatestScore(int hand) => _snapshot.GetScore(hand);

        public int GetLatestLandmarkCount(int hand) => _snapshot.GetLandmarkCount(hand);

        /// <summary>
        /// 새 캡처를 제출한다. 중복 캡처·미생성 시 false. 호출자 버퍼는 소유 슬롯에 복사된다.
        /// flipVertically=true이면 소유 슬롯에서 상하 반전 후 submit.
        /// </summary>
        public bool TrySubmit(Color32[] pixels, int width, int height, bool flipVertically, CaptureStamp stamp)
        {
            ThrowIfDisposed();

            if (!_worker.IsAccepting)
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
            _stampMap.Register(submitTimestampUs, stamp);

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

            return true;
        }

        /// <summary>
        /// 완료된 결과를 한 번만 가져온다. 새 프레임이 poll되면 true.
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
                MpudLog.Error(completion.Error);
                return false;
            }

            var result = completion.Result;
            _snapshot.UpdateFrom(ref result);
            _stampMap.TryTake(_snapshot.TimestampUs, out var resolved);
            _snapshot.SetCaptureStamp(resolved);
            return true;
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
        /// 지정 손의 최신 landmark를 caller-owned destination에 복사한다.
        /// </summary>
        public int CopyLatestHandLandmarksTo(int hand, MpudNormalizedLandmark[] destination)
        {
            ThrowIfDisposed();
            return _snapshot.CopyHandLandmarksTo(hand, destination);
        }

        /// <summary>
        /// 지정 손의 최신 월드 landmark(미터)를 caller-owned destination에 복사한다.
        /// </summary>
        public int CopyLatestHandWorldLandmarksTo(int hand, MpudNormalizedLandmark[] destination)
        {
            ThrowIfDisposed();
            return _snapshot.CopyHandWorldLandmarksTo(hand, destination);
        }

        /// <summary>
        /// tracker를 destroy + recreate한다.
        /// snapshot, timestampGen, flipBuffer를 모두 초기화한다.
        /// timestamp generator reset은 이 경로에서만 수행한다.
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
                MpudLog.Error($"[MPUD] hand worker shutdown: {_worker.ShutdownError}");
            }
        }

        // 네이티브 호출 전담. 모든 메서드는 워커 스레드에서 실행된다(Unity API 호출 금지).
        private sealed class HandWorkerBody : ITrackerWorkerBody<MpudHandResult>
        {
            private readonly string _modelPath;
            private readonly int _numHands;
            private IntPtr _trackerHandle;
            private Color32[] _flipBuffer;

            public HandWorkerBody(string modelPath, int numHands)
            {
                _modelPath = modelPath;
                _numHands = numHands;
            }

            public void Create()
            {
                var modelPathNative = MarshalStringToUtf8(_modelPath);
                try
                {
                    var config = new MpudHandTrackerConfig
                    {
                        modelAssetPath = modelPathNative,
                        numHands = _numHands,
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
                }
                finally
                {
                    Marshal.FreeHGlobal(modelPathNative);
                }
            }

            public bool Invoke(in TrackerWorkItem item, out MpudHandResult completed, out string error)
            {
                completed = default;
                error = null;

                var submitPixels = item.Pixels;
                var pixelCount = checked(item.Width * item.Height);
                if (submitPixels == null || submitPixels.Length < pixelCount)
                {
                    error = "[MPUD] submit_frame skipped: invalid input buffer.";
                    return false;
                }

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

                    var submitStatus = MpudBridge.mpud_submit_frame(_trackerHandle, ref frame);
                    if (submitStatus != MpudStatus.Ok)
                    {
                        error = $"[MPUD] submit_frame failed ({submitStatus}): {MpudBridge.GetLastError()}";
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

                var pollStatus = MpudBridge.mpud_try_get_latest_result(_trackerHandle, out var result);
                if (pollStatus == MpudStatus.Ok)
                {
                    completed = result;
                    return true;
                }

                if (pollStatus != MpudStatus.NoResult)
                {
                    error = $"[MPUD] try_get_latest_result failed ({pollStatus}): {MpudBridge.GetLastError()}";
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

                MpudBridge.mpud_destroy_hand_tracker(_trackerHandle);
                _trackerHandle = IntPtr.Zero;
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
