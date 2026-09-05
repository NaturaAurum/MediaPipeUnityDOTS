using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 랜드마크 포인트별 1 Euro Filter 상태. 포인트 엔티티에 상주하므로 버퍼 리사이징이 필요 없다.
    /// </summary>
    public struct LandmarkFilterState : IComponentData
    {
        public float3 PrevFiltered;
        public float3 PrevDerivative;
        public long LastTimestampUs;
        public int Initialized;
    }

    /// <summary>
    /// 속도 적응형 1차 저주파 통과 필터. 정지 시 떨림을 잡고 고속 이동 시 지연 없이 통과시킨다.
    /// 입력 타임라인으로만 전진한다. 같은 타임스탬프가 다시 들어오면 상태를 건드리지 않고
    /// 이전 값을 그대로 돌려주므로 렌더 FPS와 무관하게 같은 입력에는 같은 출력이 나온다.
    /// </summary>
    [BurstCompile]
    public static class OneEuroFilter
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 Filter(
            float3 current,
            ref LandmarkFilterState state,
            float3 minCutoffHz,
            float3 beta,
            float derivativeCutoffHz,
            long timestampUs)
        {
            if (state.Initialized != 0 && timestampUs == state.LastTimestampUs)
            {
                return state.PrevFiltered;
            }

            if (state.Initialized == 0 || timestampUs < state.LastTimestampUs)
            {
                state.PrevFiltered = current;
                state.PrevDerivative = float3.zero;
                state.Initialized = 1;
                state.LastTimestampUs = timestampUs;
                return current;
            }

            var dt = (timestampUs - state.LastTimestampUs) / 1000000f;
            var dAlpha = ComputeAlpha(derivativeCutoffHz, dt);
            var dx = (current - state.PrevFiltered) / dt;
            var hatDx = math.lerp(state.PrevDerivative, dx, dAlpha);

            var cutoff = minCutoffHz + beta * math.abs(hatDx);
            var alpha = ComputeAlpha(cutoff, dt);
            var filtered = math.lerp(state.PrevFiltered, current, alpha);

            state.PrevFiltered = filtered;
            state.PrevDerivative = hatDx;
            state.LastTimestampUs = timestampUs;
            return filtered;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeAlpha(float cutoffHz, float dt) => 1f / (1f + (1f / (2f * math.PI * cutoffHz)) / dt);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float3 ComputeAlpha(float3 cutoffHz, float dt) => 1f / (1f + (1f / (2f * math.PI * cutoffHz)) / dt);
    }
}
