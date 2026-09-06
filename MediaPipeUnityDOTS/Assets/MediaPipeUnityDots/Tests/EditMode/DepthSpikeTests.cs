using System.Threading;
using MediaPipeUnityDots.Runtime.Tracking;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

namespace MediaPipeUnityDots.Tests.EditMode
{
    /// <summary>
    /// P0 스파이크: Sentis 모델 import·스케줄·비동기 리드백 경로 검증 (CPU, 140px).
    /// 모델이 없으면 다운로드 메뉴 안내 후 Ignore한다.
    /// </summary>
    public sealed class DepthSpikeTests
    {
        private const int SpikeSize = 140;

        [Test]
        public void ImportScheduleReadback_ProducesFiniteVaryingMap()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ModelAsset>(
                "Assets/MediaPipeUnityDots/Models/depth_anything_v2_small.onnx");
            if (asset == null)
            {
                Assert.Ignore("Run MediaPipe/Download Depth Model (DA-V2 Small) first.");
            }

            using var service = new DepthInferenceService(asset, BackendType.CPU);
            Assert.IsTrue(service.IsReady, $"worker not ready: {service.LastError}");

            var input = new float[3 * SpikeSize * SpikeSize];
            for (var y = 0; y < SpikeSize; y++)
            {
                for (var x = 0; x < SpikeSize; x++)
                {
                    var v = (x + y) / (float)(2 * SpikeSize);
                    input[y * SpikeSize + x] = (v - 0.485f) / 0.229f;
                    input[SpikeSize * SpikeSize + y * SpikeSize + x] = (v - 0.456f) / 0.224f;
                    input[2 * SpikeSize * SpikeSize + y * SpikeSize + x] = (v - 0.406f) / 0.225f;
                }
            }

            Assert.IsTrue(service.TrySubmit(input, SpikeSize, SpikeSize, 7L, 7000L, 1L));
            Assert.IsFalse(service.TrySubmit(input, SpikeSize, SpikeSize, 8L, 8000L, 1L), "single flight only");

            var completed = false;
            var deadline = System.DateTime.UtcNow.AddMinutes(10);
            while (System.DateTime.UtcNow < deadline)
            {
                if (service.TryTakeCompleted(out var map))
                {
                    Assert.AreEqual(7L, map.CaptureId);
                    Assert.AreEqual(SpikeSize, map.Width);
                    Assert.AreEqual(SpikeSize, map.Height);
                    var min = float.MaxValue;
                    var max = float.MinValue;
                    foreach (var value in map.Values)
                    {
                        Assert.IsFalse(float.IsNaN(value) || float.IsInfinity(value));
                        min = Mathf.Min(min, value);
                        max = Mathf.Max(max, value);
                    }

                    Assert.Greater(max - min, 0f, "gradient input must produce varying depth");
                    completed = true;
                    break;
                }

                Thread.Sleep(50);
            }

            Assert.IsTrue(completed, "readback did not complete in 10 minutes");
            Assert.AreEqual(1L, service.CompletedCount);
        }
    }
}
