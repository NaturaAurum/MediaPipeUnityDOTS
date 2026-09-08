using System;
using System.Collections.Generic;
using System.Threading;
using MediaPipeUnityDots.Runtime.Tracking;
using NUnit.Framework;
using UnityEngine;

namespace MediaPipeUnityDots.Tests.EditMode
{
    /// <summary>
    /// tracker 워커 수명주기 검증. fake 본체로 네이티브 없이 수행한다.
    /// </summary>
    public sealed class TrackerWorkerTests
    {
        private const int WaitTimeoutMs = 5000;

        public struct FakeResult
        {
            public int Value;
        }

        private sealed class FakeBody : ITrackerWorkerBody<FakeResult>
        {
            private readonly object _sync = new();
            private readonly List<string> _calls = new();
            private readonly HashSet<int> _threads = new();
            private int _concurrent;
            private int _maxConcurrent;
            private int _createThreadId;
            private int _invokeThreadId;
            private int _destroyThreadId;

            public ManualResetEventSlim InvokeEntered = new(false);
            public ManualResetEventSlim InvokeRelease = new(true);
            public ManualResetEventSlim InvokeExited = new(false);
            public ManualResetEventSlim ResetEntered = new(false);
            public ManualResetEventSlim ResetRelease = new(true);
            public ManualResetEventSlim ResetExited = new(false);
            public ManualResetEventSlim DestroyEntered = new(false);
            public ManualResetEventSlim DestroyExited = new(false);
            public Func<TrackerWorkItem, int> ValueOf = item => (int)item.Stamp.CaptureId;
            public bool Ok = true;
            public string Error;
            public bool FailCreate;
            public bool FailReset;
            public bool FailDestroy;

            public IReadOnlyList<string> Calls
            {
                get { lock (_sync) { return _calls.ToArray(); } }
            }

            public int WorkerThreadCount
            {
                get { lock (_sync) { return _threads.Count; } }
            }

            public int CreateThreadId
            {
                get { lock (_sync) { return _createThreadId; } }
            }

            public int InvokeThreadId
            {
                get { lock (_sync) { return _invokeThreadId; } }
            }

            public int DestroyThreadId
            {
                get { lock (_sync) { return _destroyThreadId; } }
            }

            public bool RanOffMainThread(int mainId)
            {
                lock (_sync)
                {
                    foreach (var id in _threads)
                    {
                        if (id != mainId)
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }

            public int MaxConcurrent
            {
                get { lock (_sync) { return _maxConcurrent; } }
            }

            private void RecordCall(string call)
            {
                lock (_sync)
                {
                    _calls.Add(call);
                    _threads.Add(Thread.CurrentThread.ManagedThreadId);
                }
            }

            public void Create()
            {
                RecordCall("create");
                lock (_sync)
                {
                    _createThreadId = Thread.CurrentThread.ManagedThreadId;
                }

                if (FailCreate)
                {
                    throw new InvalidOperationException("boom-create");
                }
            }

            public bool Invoke(in TrackerWorkItem item, out FakeResult completed, out string error)
            {
                RecordCall($"invoke:{item.Stamp.CaptureId}");
                lock (_sync)
                {
                    _invokeThreadId = Thread.CurrentThread.ManagedThreadId;
                    _concurrent++;
                    if (_concurrent > _maxConcurrent)
                    {
                        _maxConcurrent = _concurrent;
                    }
                }

                try
                {
                    InvokeEntered.Set();
                    InvokeRelease.Wait();
                    completed = new FakeResult { Value = ValueOf(item) };
                    error = Error;
                    return Ok;
                }
                finally
                {
                    lock (_sync)
                    {
                        _concurrent--;
                    }

                    InvokeExited.Set();
                }
            }

            public void ResetBody()
            {
                RecordCall("reset");
                ResetEntered.Set();
                ResetRelease.Wait();
                ResetExited.Set();

                if (FailReset)
                {
                    throw new InvalidOperationException("boom-reset");
                }
            }

            public void Destroy()
            {
                RecordCall("destroy");
                lock (_sync)
                {
                    _destroyThreadId = Thread.CurrentThread.ManagedThreadId;
                }

                DestroyEntered.Set();
                try
                {
                    if (FailDestroy)
                    {
                        throw new InvalidOperationException("boom-destroy");
                    }
                }
                finally
                {
                    DestroyExited.Set();
                }
            }
        }

        private static TrackerWorkItem Item(long id)
        {
            return new TrackerWorkItem
            {
                Stamp = new CaptureStamp(id, id * 1000L, 1L),
                Pixels = new Color32[4],
                Width = 2,
                Height = 2,
                FlipVertically = false,
                SubmitTimestampUs = id * 1000L,
            };
        }

        private static int IndexOf(IReadOnlyList<string> calls, string call)
        {
            for (var i = 0; i < calls.Count; i++)
            {
                if (calls[i] == call)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool WaitFor(Func<bool> condition, int timeoutMs = WaitTimeoutMs)
        {
            return SpinWait.SpinUntil(condition, timeoutMs);
        }

        [Test]
        public void SubmitTake_RunsOnWorkerThread_DeliversOnce()
        {
            var mainId = Environment.CurrentManagedThreadId;
            var body = new FakeBody();
            using var worker = new TrackerWorker<FakeResult>("test", body);
            Assert.IsTrue(worker.TrySubmit(Item(7L)));
            TrackerWorker<FakeResult>.Completion taken = default;
            Assert.IsTrue(WaitFor(() => worker.TryTake(out taken)), "completion was not delivered");
            Assert.IsTrue(taken.Ok);
            Assert.AreEqual(7, taken.Result.Value);
            Assert.AreEqual(7L, taken.Stamp.CaptureId);
            Assert.IsFalse(worker.TryTake(out _), "single delivery");
            Assert.IsTrue(body.RanOffMainThread(mainId));
            Assert.LessOrEqual(body.MaxConcurrent, 1);
        }

        [Test]
        public void SecondSubmitWhileBusy_Refused()
        {
            var body = new FakeBody();
            using var worker = new TrackerWorker<FakeResult>("test", body);
            body.InvokeRelease.Reset();
            try
            {
                Assert.IsTrue(worker.TrySubmit(Item(1L)));
                Assert.IsTrue(body.InvokeEntered.Wait(WaitTimeoutMs), "invoke did not enter");
                Assert.IsFalse(worker.TrySubmit(Item(2L)));
            }
            finally
            {
                body.InvokeRelease.Set();
            }

            Assert.IsTrue(WaitFor(() => worker.TryTake(out _)), "first completion was not delivered");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ResetWhileInvokeRunning_DropsOldCompletion(bool ok)
        {
            var body = new FakeBody
            {
                Ok = ok,
                Error = ok ? null : "native boom",
            };
            using var worker = new TrackerWorker<FakeResult>("test", body);
            body.InvokeRelease.Reset();
            body.ResetRelease.Reset();
            try
            {
                Assert.IsTrue(worker.TrySubmit(Item(1L)));
                Assert.IsTrue(body.InvokeEntered.Wait(WaitTimeoutMs), "invoke did not enter");

                worker.RequestReset();
                Assert.IsFalse(worker.IsAccepting, "reset must close acceptance");
                Assert.IsFalse(worker.TrySubmit(Item(2L)), "submission must stay blocked during reset");

                body.InvokeRelease.Set();
                Assert.IsTrue(body.ResetEntered.Wait(WaitTimeoutMs), "reset did not enter");
                Assert.IsFalse(worker.TryTake(out _), "pre-reset completion must be dropped");
                Assert.IsFalse(worker.TrySubmit(Item(2L)), "submission must stay blocked while reset runs");

                body.ResetRelease.Set();
                Assert.IsTrue(body.ResetExited.Wait(WaitTimeoutMs), "reset did not finish");
                Assert.IsTrue(WaitFor(() => worker.IsAccepting), "worker did not reopen after reset");

                body.InvokeEntered.Reset();
                Assert.IsTrue(worker.TrySubmit(Item(2L)));
                TrackerWorker<FakeResult>.Completion taken = default;
                Assert.IsTrue(WaitFor(() => worker.TryTake(out taken)), "post-reset completion was not delivered");
                Assert.AreEqual(2L, taken.Stamp.CaptureId);
                Assert.AreEqual(ok, taken.Ok);
                if (ok)
                {
                    Assert.AreEqual(2, taken.Result.Value);
                }
                else
                {
                    Assert.AreEqual("native boom", taken.Error);
                }
            }
            finally
            {
                body.InvokeRelease.Set();
                body.ResetRelease.Set();
            }

            var calls = body.Calls;
            var invoke1 = IndexOf(calls, "invoke:1");
            var reset = IndexOf(calls, "reset");
            var invoke2 = IndexOf(calls, "invoke:2");
            Assert.GreaterOrEqual(invoke1, 0, "invoke:1 was not recorded");
            Assert.GreaterOrEqual(reset, 0, "reset was not recorded");
            Assert.GreaterOrEqual(invoke2, 0, "invoke:2 was not recorded");
            Assert.Less(invoke1, reset);
            Assert.Less(reset, invoke2);
        }

        [Test]
        public void SubmissionDuringReset_IsRefusedUntilResetCompletes()
        {
            var body = new FakeBody();
            using var worker = new TrackerWorker<FakeResult>("test", body);
            body.ResetRelease.Reset();
            try
            {
                worker.RequestReset();
                Assert.IsTrue(body.ResetEntered.Wait(WaitTimeoutMs), "reset did not enter");
                Assert.IsFalse(worker.IsAccepting);
                Assert.IsFalse(worker.TrySubmit(Item(1L)));

                body.ResetRelease.Set();
                Assert.IsTrue(body.ResetExited.Wait(WaitTimeoutMs), "reset did not finish");
                Assert.IsTrue(WaitFor(() => worker.IsAccepting), "worker did not reopen after reset");
                Assert.IsTrue(worker.TrySubmit(Item(1L)));
                Assert.IsTrue(WaitFor(() => worker.TryTake(out _)), "completion was not delivered");
            }
            finally
            {
                body.InvokeRelease.Set();
                body.ResetRelease.Set();
            }
        }

        [Test]
        public void ResetFailure_FaultsAcceptanceAndPublishesError()
        {
            var body = new FakeBody { FailReset = true };
            using var worker = new TrackerWorker<FakeResult>("test", body);
            body.ResetRelease.Reset();
            try
            {
                worker.RequestReset();
                Assert.IsTrue(body.ResetEntered.Wait(WaitTimeoutMs), "reset did not enter");
                Assert.IsFalse(worker.IsAccepting);
                Assert.IsFalse(worker.TrySubmit(Item(1L)));

                body.ResetRelease.Set();
                TrackerWorker<FakeResult>.Completion taken = default;
                Assert.IsTrue(WaitFor(() => worker.TryTake(out taken)), "reset failure was not delivered");
                Assert.IsFalse(taken.Ok);
                StringAssert.Contains("boom-reset", taken.Error);
                Assert.IsFalse(worker.TryTake(out _), "reset failure is a one-time receipt");
                Assert.IsFalse(worker.IsAccepting, "reset failure must permanently fault acceptance");
                Assert.IsFalse(worker.TrySubmit(Item(2L)));
            }
            finally
            {
                body.InvokeRelease.Set();
                body.ResetRelease.Set();
            }
        }

        [Test]
        public void FailedCompletion_SurfacesErrorOnce()
        {
            var body = new FakeBody { Ok = false, Error = "native boom" };
            using var worker = new TrackerWorker<FakeResult>("test", body);
            Assert.IsTrue(worker.TrySubmit(Item(3L)));
            TrackerWorker<FakeResult>.Completion taken = default;
            Assert.IsTrue(WaitFor(() => worker.TryTake(out taken)), "error completion was not delivered");
            Assert.IsFalse(taken.Ok);
            Assert.AreEqual("native boom", taken.Error);
            Assert.IsFalse(worker.TryTake(out _), "single delivery");
        }

        [Test]
        public void CompletionBlocksSubmitUntilTaken()
        {
            var body = new FakeBody();
            using var worker = new TrackerWorker<FakeResult>("test", body);
            Assert.IsTrue(worker.TrySubmit(Item(1L)));
            Assert.IsTrue(body.InvokeExited.Wait(WaitTimeoutMs), "invoke did not return");
            Assert.IsFalse(worker.IsAccepting, "untaken completion must close acceptance");
            Assert.IsFalse(worker.TrySubmit(Item(2L)), "must not overwrite untaken completion");

            TrackerWorker<FakeResult>.Completion taken = default;
            Assert.IsTrue(WaitFor(() => worker.TryTake(out taken)), "completion was not delivered");
            Assert.AreEqual(1L, taken.Stamp.CaptureId);
            Assert.IsFalse(worker.TryTake(out _), "single delivery");
            Assert.IsTrue(WaitFor(() => worker.IsAccepting), "worker did not reopen after take");

            Assert.IsTrue(worker.TrySubmit(Item(2L)));
            Assert.IsTrue(WaitFor(() => worker.TryTake(out taken)), "second completion was not delivered");
            Assert.AreEqual(2L, taken.Stamp.CaptureId);
        }

        [Test]
        public void Dispose_WaitsForRunningInvoke_AndDestroysOnWorkerThread()
        {
            var body = new FakeBody();
            var worker = new TrackerWorker<FakeResult>("test", body);
            body.InvokeRelease.Reset();
            var disposeReturned = new ManualResetEventSlim(false);
            var disposeEntered = new ManualResetEventSlim(false);
            Exception disposeError = null;
            Thread disposer = null;
            var disposerStarted = false;
            try
            {
                Assert.IsTrue(worker.TrySubmit(Item(1L)));
                Assert.IsTrue(body.InvokeEntered.Wait(WaitTimeoutMs), "invoke did not enter");

                disposer = new Thread(() =>
                {
                    disposeEntered.Set();
                    try
                    {
                        worker.Dispose();
                    }
                    catch (Exception exception)
                    {
                        disposeError = exception;
                    }
                    finally
                    {
                        disposeReturned.Set();
                    }
                });
                disposer.Start();
                disposerStarted = true;

                try
                {
                    Assert.IsTrue(disposeEntered.Wait(WaitTimeoutMs), "dispose did not enter");
                    Assert.IsFalse(disposeReturned.Wait(100), "dispose returned while invoke was blocked");
                    Assert.IsFalse(body.DestroyEntered.IsSet, "destroy must wait for invoke");
                }
                finally
                {
                    body.InvokeRelease.Set();
                }

                Assert.IsTrue(disposeReturned.Wait(WaitTimeoutMs), "dispose did not finish");
                Assert.IsTrue(disposer.Join(WaitTimeoutMs), "dispose thread did not join");
                Assert.IsNull(disposeError);
                Assert.IsTrue(body.DestroyExited.IsSet, "destroy was not called");
                Assert.AreEqual(body.CreateThreadId, body.DestroyThreadId);
                Assert.AreEqual(body.InvokeThreadId, body.DestroyThreadId);
            }
            finally
            {
                body.InvokeRelease.Set();
                if (disposerStarted)
                {
                    disposeReturned.Wait(WaitTimeoutMs);
                    disposer.Join(WaitTimeoutMs);
                }
                else
                {
                    worker.Dispose();
                }
            }
        }

        [Test]
        public void CreateFailure_ThrowsFromCtor()
        {
            var body = new FakeBody { FailCreate = true };
            Assert.Throws<InvalidOperationException>(() =>
            {
                using var _ = new TrackerWorker<FakeResult>("test", body);
            });
        }
    }
}
