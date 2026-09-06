using System;
using System.Diagnostics;
using Unity.InferenceEngine;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// 깊이 모델의 유일한 핸들 소유자. 진행 중 작업 1건 + 비동기 리드백 완료 폴링.
    /// GPU 실패 시 CPU로 자동 전환하지 않고 비활성 상태를 유지한다.
    /// </summary>
    public sealed class DepthInferenceService : IDisposable
    {
        public struct CompletedMap
        {
            public long CaptureId;
            public long CaptureTimestampUs;
            public long CaptureEpoch;
            public int Width;
            public int Height;
            public float[] Values;
            public float LatencyMs;
        }

        private readonly Worker _worker;
        private readonly Stopwatch _stopwatch = new();

        private Tensor<float> _inputTensor;
        private bool _busy;
        private bool _stale;
        private bool _disposed;

        private long _pendingCaptureId;
        private long _pendingCaptureTimestampUs;
        private long _pendingCaptureEpoch;
        private long _completedCount;

        public DepthInferenceService(ModelAsset modelAsset, BackendType backendType)
        {
            if (modelAsset == null)
            {
                throw new ArgumentNullException(nameof(modelAsset));
            }


            try
            {
                var model = ModelLoader.Load(modelAsset);
                try
                {
                    _worker = new Worker(model, backendType);
                }
                finally
                {
                    (model as IDisposable)?.Dispose();
                }
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                MpudLog.Error($"[MPUD] Depth worker failed ({backendType}): {exception.Message}");
            }
        }
        public bool IsReady => _worker != null;

        public bool IsBusy => _busy;

        public string LastError { get; private set; }

        public float LastLatencyMs { get; private set; }

        public long CompletedCount => _completedCount;

        /// <summary>
        /// Idle일 때만 새 캡처를 제출한다. 호출자 배열은 복사되므로 재사용해도 된다.
        /// </summary>
        public bool TrySubmit(float[] nchw, int width, int height, long captureId, long captureTimestampUs, long captureEpoch)
        {
            ThrowIfDisposed();
            if (!IsReady || _busy || nchw == null || width <= 0 || height <= 0
                || nchw.Length != 3 * width * height)
            {
                return false;
            }

            _inputTensor = new Tensor<float>(new TensorShape(1, 3, height, width), nchw);
            _worker.Schedule(_inputTensor);
            (_worker.PeekOutput() as Tensor<float>)?.ReadbackRequest();
            _pendingCaptureId = captureId;
            _pendingCaptureTimestampUs = captureTimestampUs;
            _pendingCaptureEpoch = captureEpoch;
            _busy = true;
            _stale = false;
            _stopwatch.Restart();
            return true;
        }

        /// <summary>
        /// 완료된 결과 1건을 가져온다. 미완료·무효화분은 false. Values는 다음 결과까지 유효하다.
        /// </summary>
        public bool TryTakeCompleted(out CompletedMap completed)
        {
            completed = default;
            ThrowIfDisposed();
            if (!IsReady || !_busy)
            {
                return false;
            }

            var output = _worker.PeekOutput() as Tensor<float>;
            if (output == null || !output.IsReadbackRequestDone())
            {
                return false;
            }

            var values = output.DownloadToArray();
            var shape = output.shape;
            var (mapWidth, mapHeight) = shape.rank == 4
                ? (shape[3], shape[2])
                : (shape[shape.rank - 1], shape[shape.rank - 2]);
            ReleaseInput();
            _busy = false;
            LastLatencyMs = (float)_stopwatch.Elapsed.TotalMilliseconds;
            if (_stale)
            {
                return false;
            }

            _completedCount++;
            completed = new CompletedMap
            {
                CaptureId = _pendingCaptureId,
                CaptureTimestampUs = _pendingCaptureTimestampUs,
                CaptureEpoch = _pendingCaptureEpoch,
                Width = mapWidth,
                Height = mapHeight,
                Values = values,
                LatencyMs = LastLatencyMs,
            };
            return true;
        }

        /// <summary>
        /// 실행 세대를 폐기한다. 진행 중 결과는 게시하지 않고 공개 샘플은 호출자가 무효화한다.
        /// </summary>
        public void Invalidate()
        {
            _stale = true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseInput();
            _worker?.Dispose();
        }

        private void ReleaseInput()
        {
            _inputTensor?.Dispose();
            _inputTensor = null;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DepthInferenceService));
            }
        }
    }
}
