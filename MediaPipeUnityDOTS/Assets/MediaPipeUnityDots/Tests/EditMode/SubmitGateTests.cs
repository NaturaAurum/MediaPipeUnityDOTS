using System;
using MediaPipeUnityDots.Runtime.Tracking;
using NUnit.Framework;
using UnityEngine;

namespace MediaPipeUnityDots.Tests.EditMode
{
    /// <summary>
    /// 제출 경계 계약 검증: 중복 거부·입력 보존·리사이즈·리셋.
    /// </summary>
    public sealed class SubmitGateTests
    {
        [Test]
        public void Offer_RejectsDuplicateCapture()
        {
            var gate = new SubmitGate();
            Assert.IsTrue(gate.Offer(new CaptureStamp(1L, 100L, 1L)));
            Assert.IsFalse(gate.Offer(new CaptureStamp(1L, 100L, 1L)));
            Assert.IsTrue(gate.Offer(new CaptureStamp(2L, 200L, 1L)));
            Assert.IsTrue(gate.Offer(new CaptureStamp(2L, 200L, 2L)), "epoch change reopens");
        }

        [Test]
        public void Offer_RejectsZeroAndOlderCaptures()
        {
            var gate = new SubmitGate();
            Assert.IsFalse(gate.Offer(new CaptureStamp(0L, 0L, 0L)), "zero capture id is invalid");
            Assert.IsFalse(gate.Offer(new CaptureStamp(1L, 100L, -1L)), "negative epoch is invalid");

            Assert.IsTrue(gate.Offer(new CaptureStamp(2L, 200L, 3L)));
            Assert.IsFalse(gate.Offer(new CaptureStamp(1L, 100L, 3L)), "older id in the current epoch");
            Assert.IsFalse(gate.Offer(new CaptureStamp(3L, 300L, 2L)), "older epoch");
            Assert.IsTrue(gate.Offer(new CaptureStamp(3L, 300L, 4L)), "newer epoch");
        }

        [Test]
        public void CopyInput_PreservesAgainstSourceMutation()
        {
            var gate = new SubmitGate();
            var source = new[] { new Color32(10, 20, 30, 255), new Color32(40, 50, 60, 255) };
            gate.CopyInput(source, 2);
            source[0] = new Color32(0, 0, 0, 255);
            Assert.AreEqual(10, gate.Input[0].r);
            Assert.AreEqual(60, gate.Input[1].b);
        }

        [Test]
        public void CopyInput_SameSizeKeepsBuffer_DifferentSizeReallocates()
        {
            var gate = new SubmitGate();
            gate.CopyInput(new Color32[4], 4);
            var kept = gate.Input;
            gate.CopyInput(new Color32[4], 4);
            Assert.AreSame(kept, gate.Input);
            gate.CopyInput(new Color32[6], 6);
            Assert.AreEqual(6, gate.Input.Length);
        }

        [Test]
        public void CopyInput_NullThrows()
        {
            var gate = new SubmitGate();
            Assert.Throws<ArgumentNullException>(() => gate.CopyInput(null, 2));
        }

        [Test]
        public void Reset_ReopensSameStamp()
        {
            var gate = new SubmitGate();
            var stamp = new CaptureStamp(5L, 500L, 1L);
            Assert.IsTrue(gate.Offer(stamp));
            Assert.IsFalse(gate.Offer(stamp));
            gate.Reset();
            Assert.IsTrue(gate.Offer(stamp));
        }
    }
}
