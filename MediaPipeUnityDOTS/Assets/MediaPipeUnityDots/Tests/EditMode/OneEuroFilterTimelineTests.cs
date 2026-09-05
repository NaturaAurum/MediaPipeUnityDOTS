using MediaPipeUnityDots.Runtime.Ecs;
using NUnit.Framework;
using Unity.Mathematics;

namespace MediaPipeUnityDots.Tests.EditMode
{
    /// <summary>
    /// 같은 입력 타임라인을 렌더 FPS와 무관하게 같은 출력으로 만드는지 검증한다.
    /// 입력 타임스탬프가 바뀌지 않으면 필터가 전진하지 않아야 한다.
    /// </summary>
    public sealed class OneEuroFilterTimelineTests
    {
        private const long StepUs = 33333; // 30Hz 입력
        private const float DerivativeCutoffHz = 1f;

        private static readonly float3 MinCutoffHz = new(1f, 1f, 0.3f);
        private static readonly float3 Beta = new(0.007f, 0.007f, 0.002f);

        private static float3 Sample(long frame)
        {
            var baseValue = frame < 30 ? 0f : 1f;
            var jitter = math.sin(frame * 12.9898f) * 0.01f;
            return new float3(baseValue + jitter, baseValue - jitter, baseValue * 0.5f);
        }

        [Test]
        public void SameInputTimeline_SameOutput_At30HzAnd120HzRender()
        {
            var state30 = new LandmarkFilterState();
            var expected = new float3[60];
            for (long frame = 0; frame < 60; frame++)
            {
                expected[frame] = OneEuroFilter.Filter(
                    Sample(frame), ref state30, MinCutoffHz, Beta, DerivativeCutoffHz, frame * StepUs);
            }

            var state120 = new LandmarkFilterState();
            for (long frame = 0; frame < 60; frame++)
            {
                for (var repeat = 0; repeat < 4; repeat++)
                {
                    var actual = OneEuroFilter.Filter(
                        Sample(frame), ref state120, MinCutoffHz, Beta, DerivativeCutoffHz, frame * StepUs);
                    Assert.AreEqual(expected[frame].x, actual.x, 1e-6f, $"frame={frame} repeat={repeat} x");
                    Assert.AreEqual(expected[frame].y, actual.y, 1e-6f, $"frame={frame} repeat={repeat} y");
                    Assert.AreEqual(expected[frame].z, actual.z, 1e-6f, $"frame={frame} repeat={repeat} z");
                }
            }
        }

        [Test]
        public void RepeatedTimestamp_DoesNotAdvanceFilterState()
        {
            var state = new LandmarkFilterState();
            var first = OneEuroFilter.Filter(new float3(1f), ref state, MinCutoffHz, Beta, DerivativeCutoffHz, StepUs);
            var snapshot = state;

            for (var i = 0; i < 10; i++)
            {
                var repeated = OneEuroFilter.Filter(
                    new float3(99f), ref state, MinCutoffHz, Beta, DerivativeCutoffHz, StepUs);
                Assert.AreEqual(first.x, repeated.x, 1e-6f);
            }

            Assert.AreEqual(snapshot.PrevFiltered.x, state.PrevFiltered.x, 1e-6f);
            Assert.AreEqual(snapshot.LastTimestampUs, state.LastTimestampUs);
        }

        [Test]
        public void TimestampReset_ReinitializesWithoutPull()
        {
            var state = new LandmarkFilterState();
            OneEuroFilter.Filter(new float3(1f), ref state, MinCutoffHz, Beta, DerivativeCutoffHz, 10 * StepUs);

            var afterReset = OneEuroFilter.Filter(new float3(5f), ref state, MinCutoffHz, Beta, DerivativeCutoffHz, 0L);
            Assert.AreEqual(5f, afterReset.x, 1e-6f);
            Assert.AreEqual(0L, state.LastTimestampUs);
        }
    }
}
