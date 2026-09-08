using System;
using System.Threading;
using UnityEngine;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// 모든 MediaPipe tracker 공통 작업 단위. 워커 소유 슬롯 참조를 전달한다.
    /// </summary>
    public struct TrackerWorkItem
    {
        public CaptureStamp Stamp;
        public Color32[] Pixels;
        public int Width;
        public int Height;
        public bool FlipVertically;
        // SubmitTimestampUs는 서비스가 제출 시점에 부여한다(네이티브 echo 매핑용).
        public long SubmitTimestampUs;
        // Generation은 하네스가 제출 시점에 부여한다. 호출자가 채우지 않는다.
        public long Generation;
    }

    /// <summary>
    /// tracker 전용 워커 본체. 모든 메서드는 워커 스레드에서 실행된다(Unity API 호출 금지).
    /// </summary>
    public interface ITrackerWorkerBody<TCompletion> where TCompletion : unmanaged
    {
        void Create();

        /// <summary>
        /// true = 새 결과. false + error null = 결과 없음. false + error = 실패(메인에서 보고 후 폐기).
        /// </summary>
        bool Invoke(in TrackerWorkItem item, out TCompletion completed, out string error);

        void ResetBody();

        void Destroy();
    }

    /// <summary>
    /// tracker 전용 워커의 수명주기·슬롯 관리. 완료 결과는 수신 전까지 보존한다.
    /// 대기 신호 사용, 프레임당 Task 생성 없음. 잠금은 전달·상태 전환에만 쓴다.
    /// </summary>
    public sealed class TrackerWorker<TCompletion> : IDisposable where TCompletion : unmanaged
    {
        public struct Completion
        {
            public bool Ok;
            public TCompletion Result;
            public string Error;
            public CaptureStamp Stamp;
            public long Generation;
        }

        private readonly ITrackerWorkerBody<TCompletion> _body;
        private readonly Thread _thread;
        private readonly AutoResetEvent _signal = new(false);
        private readonly ManualResetEventSlim _readyEvent = new(false);
        private readonly object _gate = new();
        private bool _hasPending;
        private TrackerWorkItem _pending;
        private bool _hasCompleted;
        private Completion _completed;
        private bool _workerBusy;
        private bool _resetRequested;
        private bool _stopRequested;
        private bool _faulted;
        private bool _disposed;
        private bool _started;
        private long _generation;
        private Exception _createError;
        private string _shutdownError;

        public TrackerWorker(string name, ITrackerWorkerBody<TCompletion> body, int createTimeoutMs = 10000)
        {
            _body = body ?? throw new ArgumentNullException(nameof(body));
            _thread = new Thread(WorkLoop) { IsBackground = true, Name = name };
            _started = true;
            _thread.Start();
            try
            {
                if (!_readyEvent.Wait(createTimeoutMs))
                {
                    throw new TimeoutException($"tracker worker '{name}' create timed out.");
                }

                if (_createError != null)
                {
                    throw new InvalidOperationException($"tracker worker '{name}' create failed: {_createError.Message}", _createError);
                }
            }
            catch
            {
                StopAndJoin();
                _signal.Dispose();
                _readyEvent.Dispose();
                throw;
            }
        }

        public bool IsAccepting
        {
            get
            {
                lock (_gate)
                {
                    return !_disposed && !_stopRequested && !_faulted && !_resetRequested
                        && _thread.IsAlive && !_hasPending && !_workerBusy && !_hasCompleted;
                }
            }
        }

        public long Generation
        {
            get
            {
                lock (_gate)
                {
                    return _generation;
                }
            }
        }

        public string ShutdownError
        {
            get
            {
                lock (_gate)
                {
                    return _shutdownError;
                }
            }
        }

        public bool TrySubmit(in TrackerWorkItem item)
        {
            lock (_gate)
            {
                if (_disposed || _stopRequested || _faulted || _resetRequested
                    || !_thread.IsAlive || _hasPending || _workerBusy || _hasCompleted)
                {
                    return false;
                }

                _hasPending = true;
                _pending = item;
                _pending.Generation = _generation;
            }

            _signal.Set();
            return true;
        }
        public bool TryTake(out Completion completion)
        {
            lock (_gate)
            {
                if (_disposed || !_hasCompleted)
                {
                    completion = default;
                    return false;
                }

                completion = _completed;
                _hasCompleted = false;
                return true;
            }
        }

        public void RequestReset()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _generation++;
                _resetRequested = true;
                _hasPending = false;
                _hasCompleted = false;
            }

            _signal.Set();
        }

        public void Dispose()
        {
            if (Thread.CurrentThread == _thread)
            {
                throw new InvalidOperationException("TrackerWorker must not be disposed from its own worker thread.");
            }

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _stopRequested = true;
            }

            StopAndJoin();
            _signal.Dispose();
            _readyEvent.Dispose();
        }

        private void StopAndJoin()
        {
            lock (_gate)
            {
                _stopRequested = true;
            }

            _signal.Set();
            if (_started)
            {
                try
                {
                    _thread.Join();
                }
                catch (ThreadStateException)
                {
                    // 시작하지 않은 스레드는 Join할 수 없다.
                }
            }
        }

        private void WorkLoop()
        {
            try
            {
                _body.Create();
            }
            catch (Exception exception)
            {
                _createError = exception;
                _readyEvent.Set();
                return;
            }

            _readyEvent.Set();

            while (true)
            {
                var item = default(TrackerWorkItem);
                var hasItem = false;
                var doReset = false;
                var doStop = false;
                lock (_gate)
                {
                    if (_stopRequested)
                    {
                        doStop = true;
                    }
                    else if (_resetRequested)
                    {
                        _resetRequested = false;
                        _workerBusy = true;
                        item.Generation = _generation;
                        doReset = true;
                    }
                    else if (_hasPending)
                    {
                        item = _pending;
                        _hasPending = false;
                        _workerBusy = true;
                        hasItem = true;
                    }
                }

                if (doStop)
                {
                    break;
                }

                if (doReset)
                {
                    string resetError = null;
                    try
                    {
                        _body.ResetBody();
                    }
                    catch (Exception exception)
                    {
                        resetError = "reset: " + exception.Message;
                    }

                    lock (_gate)
                    {
                        _workerBusy = false;
                        if (resetError != null)
                        {
                            _faulted = true;
                            if (!_stopRequested && item.Generation == _generation)
                            {
                                _completed = new Completion { Error = resetError, Generation = item.Generation };
                                _hasCompleted = true;
                            }
                        }
                    }
                    continue;
                }

                if (!hasItem)
                {
                    _signal.WaitOne();
                    continue;
                }

                var ok = false;
                var result = default(TCompletion);
                string error = null;
                try
                {
                    ok = _body.Invoke(in item, out result, out error);
                }
                catch (Exception exception)
                {
                    ok = false;
                    error = "exception: " + exception.Message;
                }

                lock (_gate)
                {
                    _workerBusy = false;
                    // 검사와 게시를 같은 임계 구역에서 수행해 Reset과의 경쟁을 막는다.
                    if (!_stopRequested && item.Generation == _generation && (ok || error != null))
                    {
                        _completed = new Completion
                        {
                            Ok = ok,
                            Result = result,
                            Error = error,
                            Stamp = item.Stamp,
                            Generation = item.Generation,
                        };
                        _hasCompleted = true;
                    }
                }
            }

            try
            {
                _body.Destroy();
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    _shutdownError = exception.Message;
                }
            }
        }

    }
}
