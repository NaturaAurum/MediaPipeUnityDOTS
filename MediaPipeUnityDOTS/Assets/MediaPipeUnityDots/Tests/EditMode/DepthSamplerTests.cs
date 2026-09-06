using MediaPipeUnityDots.Runtime.Tracking;
using NUnit.Framework;
using UnityEngine;

namespace MediaPipeUnityDots.Tests.EditMode
{
    /// <summary>
    /// 깊이 전처리·좌표 역변환·보간·대표값 계약 검증.
    /// </summary>
    public sealed class DepthSamplerTests
    {
        [Test]
        public void ComputeMap_KeepsAspectAndPadsToMultiple()
        {
            var map = DepthSampler.ComputeMap(640, 480, 518, 14);
            Assert.AreEqual(518, map.PaddedWidth);
            Assert.AreEqual(392, map.PaddedHeight);
            Assert.AreEqual(518f / 640f, map.Scale, 1e-6f);
        }

        [Test]
        public void Preprocess_NormalizesSolidRed()
        {
            var src = new[] { new Color32(255, 0, 0, 255), new Color32(255, 0, 0, 255) };
            var dst = new float[3 * 2 * 1];
            DepthSampler.Preprocess(src, 2, 1, false, dst, 2, 1, out _);
            Assert.AreEqual((1f - 0.485f) / 0.229f, dst[0], 1e-5f);
            Assert.AreEqual((0f - 0.456f) / 0.224f, dst[2], 1e-5f);
            Assert.AreEqual((0f - 0.406f) / 0.225f, dst[4], 1e-5f);
        }

        [Test]
        public void Preprocess_FlipSwapsRows()
        {
            var src = new[] { new Color32(255, 0, 0, 255), new Color32(0, 0, 255, 255) };
            var dst = new float[3 * 1 * 2];
            DepthSampler.Preprocess(src, 1, 2, true, dst, 1, 2, out _);
            Assert.Less(dst[0], 0f, "flipped first row must be original bottom (blue)");
            Assert.Greater(dst[1], 1f, "flipped second row must be original top (red)");
        }

        [Test]
        public void TryMapToDepth_CenterRoundtripsAndBorderRefused()
        {
            var map = DepthSampler.ComputeMap(640, 480, 518, 14);
            Assert.IsTrue(DepthSampler.TryMapToDepth(0.5f, 0.5f, 640, 480, in map, 518, 392, out var x, out var y));
            Assert.AreEqual(259f, x, 1e-3f);
            Assert.AreEqual(194.25f, y, 1e-3f);
            Assert.IsFalse(DepthSampler.TryMapToDepth(1f, 0.5f, 640, 480, in map, 518, 392, out _, out _));
            Assert.IsFalse(DepthSampler.TryMapToDepth(float.NaN, 0.5f, 640, 480, in map, 518, 392, out _, out _));
        }

        [Test]
        public void TrySample_BilinearAndInvalid()
        {
            var depth = new[] { 1f, 2f, 3f, 4f };
            Assert.IsTrue(DepthSampler.TrySample(depth, 2, 2, 0.5f, 0.5f, out var center));
            Assert.AreEqual(2.5f, center, 1e-6f);
            Assert.IsTrue(DepthSampler.TrySample(depth, 2, 2, 0f, 0f, out var corner));
            Assert.AreEqual(1f, corner, 1e-6f);
            Assert.IsFalse(DepthSampler.TrySample(depth, 2, 2, 2f, 0f, out _));
            var withNan = new[] { 1f, float.NaN, 3f, 4f };
            Assert.IsFalse(DepthSampler.TrySample(withNan, 2, 2, 0.5f, 0.5f, out _));
        }

        [Test]
        public void TryMedian_OddEvenAndNaNSkipped()
        {
            Assert.IsTrue(DepthSampler.TryMedian(new[] { 3f, 1f, 2f }, 3, out var odd));
            Assert.AreEqual(2f, odd, 1e-6f);
            Assert.IsTrue(DepthSampler.TryMedian(new[] { 4f, 1f, 3f, 2f }, 4, out var even));
            Assert.AreEqual(2.5f, even, 1e-6f);
            Assert.IsTrue(DepthSampler.TryMedian(new[] { float.NaN, 5f, 1f }, 3, out var skipped));
            Assert.AreEqual(3f, skipped, 1e-6f);
            Assert.IsFalse(DepthSampler.TryMedian(new[] { float.NaN }, 1, out _));
            Assert.IsFalse(DepthSampler.TryMedian(new float[0], 0, out _));
        }
    }
}
