using MediaPipeUnityDots.Runtime.Tracking;
using NUnit.Framework;

namespace MediaPipeUnityDots.Tests.EditMode
{
    /// <summary>
    /// 제출→결과 스탬프 매핑 계약 검증.
    /// </summary>
    public sealed class SubmitStampMapTests
    {
        [Test]
        public void RegisterTake_RoundtripsAndConsumes()
        {
            var map = new SubmitStampMap();
            map.Register(100L, new CaptureStamp(7L, 7000L, 1L));
            Assert.IsTrue(map.TryTake(100L, out var stamp));
            Assert.AreEqual(7L, stamp.CaptureId);
            Assert.AreEqual(7000L, stamp.CaptureTimestampUs);
            Assert.AreEqual(1L, stamp.CaptureEpoch);
            Assert.IsFalse(map.TryTake(100L, out _));
        }

        [Test]
        public void Miss_ReturnsEmptyStamp()
        {
            var map = new SubmitStampMap();
            Assert.IsFalse(map.TryTake(999L, out var stamp));
            Assert.AreEqual(0L, stamp.CaptureId);
        }

        [Test]
        public void Clear_DropsAll()
        {
            var map = new SubmitStampMap();
            map.Register(1L, new CaptureStamp(1L, 10L, 1L));
            map.Clear();
            Assert.IsFalse(map.TryTake(1L, out _));
        }

        [Test]
        public void OverCapacity_EvictsOldest()
        {
            var map = new SubmitStampMap();
            for (var i = 0; i < 33; i++)
            {
                map.Register(i, new CaptureStamp(i, i, 1L));
            }

            Assert.IsFalse(map.TryTake(0L, out _));
            Assert.IsTrue(map.TryTake(32L, out _));
        }
    }
}
