using MediaPipeUnityDots.Runtime.Ecs;
using NUnit.Framework;

namespace MediaPipeUnityDots.Tests.EditMode
{
    /// <summary>
    /// 대상별 Z 오프셋 전이 계약 검증. XY 필터와 분리된 보정 상태만 다룬다.
    /// </summary>
    public sealed class DepthCorrectorTests
    {
        private static DepthSettings Settings()
        {
            return new DepthSettings
            {
                Enabled = 1,
                Weight = 1f,
                DepthGain = 1f,
                MaxOffset = 0.1f,
                MaxSampleAgeUs = 200000L,
                MaxAlignmentDeltaUs = 100000L,
            };
        }

        [Test]
        public void FirstSample_BaselinesWithoutJump()
        {
            var state = new LandmarkDepthCorrection();
            LandmarkRender.UpdateDepthCorrection(ref state, true, 2f, 1000L, 1L, 0, Settings(), out var correction, out var use);
            Assert.AreEqual(0f, correction, 1e-6f);
            Assert.AreEqual(0, use);
            Assert.AreEqual(1, state.Initialized);
            Assert.AreEqual(2f, state.Baseline, 1e-6f);
        }

        [Test]
        public void SameTimestamp_HoldsPrevious()
        {
            var state = new LandmarkDepthCorrection();
            var settings = Settings();
            LandmarkRender.UpdateDepthCorrection(ref state, true, 2f, 1000L, 1L, 0, settings, out _, out _);
            LandmarkRender.UpdateDepthCorrection(ref state, true, 2.2f, 2000L, 1L, 0, settings, out var first, out _);
            LandmarkRender.UpdateDepthCorrection(ref state, true, 2.4f, 2000L, 1L, 0, settings, out var held, out var use);
            Assert.AreEqual(1, use);
            Assert.AreEqual(first, held, 1e-6f);
        }

        [Test]
        public void ApproachGoesNegative_RetreatGoesPositive()
        {
            var state = new LandmarkDepthCorrection();
            var settings = Settings();
            LandmarkRender.UpdateDepthCorrection(ref state, true, 2f, 1000L, 1L, 0, settings, out _, out _);
            LandmarkRender.UpdateDepthCorrection(ref state, true, 2.5f, 2000L, 1L, 0, settings, out var closer, out _);
            Assert.Less(closer, 0f, "larger depth (closer) must offset toward camera");
            LandmarkRender.UpdateDepthCorrection(ref state, true, 1f, 3000L, 1L, 0, settings, out var farther, out _);
            Assert.Greater(farther, 0f);
        }

        [Test]
        public void HugeJump_ClampedThenSmoothed()
        {
            var state = new LandmarkDepthCorrection();
            var settings = Settings();
            LandmarkRender.UpdateDepthCorrection(ref state, true, 2f, 1000L, 1L, 0, settings, out _, out _);
            LandmarkRender.UpdateDepthCorrection(ref state, true, 20f, 2000L, 1L, 0, settings, out var clamped, out _);
            Assert.AreEqual(-0.05f, clamped, 1e-6f);
        }

        [Test]
        public void IdentityOrEpochChange_Rebaselines()
        {
            var state = new LandmarkDepthCorrection();
            var settings = Settings();
            LandmarkRender.UpdateDepthCorrection(ref state, true, 2f, 1000L, 1L, 0, settings, out _, out _);
            LandmarkRender.UpdateDepthCorrection(ref state, true, 5f, 2000L, 1L, 1, settings, out var changed, out var use);
            Assert.AreEqual(0f, changed, 1e-6f);
            Assert.AreEqual(0, use);
            Assert.AreEqual(5f, state.Baseline, 1e-6f);
            LandmarkRender.UpdateDepthCorrection(ref state, true, 5f, 3000L, 2L, 1, settings, out var epochChanged, out _);
            Assert.AreEqual(0f, epochChanged, 1e-6f);
        }

        [Test]
        public void InvalidOrOff_ResetsToZero()
        {
            var state = new LandmarkDepthCorrection();
            var settings = Settings();
            LandmarkRender.UpdateDepthCorrection(ref state, true, 2f, 1000L, 1L, 0, settings, out _, out _);
            LandmarkRender.UpdateDepthCorrection(ref state, false, 2.5f, 2000L, 1L, 0, settings, out var invalid, out var useInvalid);
            Assert.AreEqual(0f, invalid, 1e-6f);
            Assert.AreEqual(0, useInvalid);
            Assert.AreEqual(0, state.Initialized);
            var off = Settings();
            off.Enabled = 0;
            LandmarkRender.UpdateDepthCorrection(ref state, true, 2f, 1000L, 1L, 0, off, out var offCorrection, out _);
            Assert.AreEqual(0f, offCorrection, 1e-6f);
            var zeroWeight = Settings();
            zeroWeight.Weight = 0f;
            LandmarkRender.UpdateDepthCorrection(ref state, true, 2f, 1000L, 1L, 0, zeroWeight, out var zeroCorrection, out _);
            Assert.AreEqual(0f, zeroCorrection, 1e-6f);
        }

        [Test]
        public void BackwardTimestamp_Rebaselines()
        {
            var state = new LandmarkDepthCorrection();
            var settings = Settings();
            LandmarkRender.UpdateDepthCorrection(ref state, true, 2f, 2000L, 1L, 0, settings, out _, out _);
            LandmarkRender.UpdateDepthCorrection(ref state, true, 9f, 1000L, 1L, 0, settings, out var correction, out _);
            Assert.AreEqual(0f, correction, 1e-6f);
            Assert.AreEqual(9f, state.Baseline, 1e-6f);
        }
    }
}
