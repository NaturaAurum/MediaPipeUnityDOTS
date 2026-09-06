using MediaPipeUnityDots.Runtime.Ecs;
using MediaPipeUnityDots.Runtime.Tracking;
using NUnit.Framework;

namespace MediaPipeUnityDots.Tests.EditMode
{
    /// <summary>
    /// 캡처 스냅샷 보관·조회와 샘플 사용 판정 검증.
    /// </summary>
    public sealed class CaptureSnapshotRingTests
    {
        [Test]
        public void AddGet_HitMissAndEpochMismatch()
        {
            var ring = new CaptureSnapshotRing();
            ring.Add(7L, 1L, 640, 480, 1, new[] { 0, -1 }, new float[84], 1, new float[66]);
            Assert.IsTrue(ring.TryGet(7L, 1L, out var hit));
            Assert.AreEqual(1, hit.HandCount);
            Assert.IsFalse(ring.TryGet(8L, 1L, out _));
            Assert.IsFalse(ring.TryGet(7L, 2L, out _));
            Assert.IsFalse(ring.TryGet(0L, 1L, out _));
        }

        [Test]
        public void OverCapacity_EvictsOldest()
        {
            var ring = new CaptureSnapshotRing();
            for (var i = 1; i <= 33; i++)
            {
                ring.Add(i, 1L, 640, 480, 0, null, null, 0, null);
            }

            Assert.IsFalse(ring.TryGet(1L, 1L, out _));
            Assert.IsTrue(ring.TryGet(33L, 1L, out _));
        }

        [Test]
        public void Gate_Boundaries()
        {
            Assert.IsTrue(DepthSampleGate.IsStamped(1L));
            Assert.IsFalse(DepthSampleGate.IsStamped(0L));
            Assert.IsTrue(DepthSampleGate.IsSameEpoch(2L, 2L));
            Assert.IsFalse(DepthSampleGate.IsSameEpoch(2L, 3L));
            Assert.IsTrue(DepthSampleGate.IsFresh(1000L, 900L, 100L));
            Assert.IsFalse(DepthSampleGate.IsFresh(1001L, 900L, 100L));
            Assert.IsFalse(DepthSampleGate.IsFresh(800L, 900L, 100L), "future stamp rejected");
            Assert.IsTrue(DepthSampleGate.IsAligned(1000L, 950L, 50L));
            Assert.IsFalse(DepthSampleGate.IsAligned(1000L, 949L, 50L));
            Assert.IsFalse(DepthSampleGate.IsFresh(1000L, 900L, -1L));
        }
    }
}
