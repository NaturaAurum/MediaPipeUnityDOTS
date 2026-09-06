using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 트래커 공통 렌더 계산. 트래커별 시스템은 버퍼에서 원시값만 뽑아 여기로 넘긴다.
    /// 필터 입력은 (정규화 X, 정규화 Y, 상대 월드 Z)이며 월드 미지원이면 useDepth=0으로 2D 폴백한다.
    /// 깊이 보정은 기존 필터 이후 표시용 깊이에 대상별 오프셋을 더하며 XY 필터를 건드리지 않는다.
    /// </summary>
    [BurstCompile]
    public static class LandmarkRender
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ResolvePoint(
            float imageX,
            float imageY,
            float worldZ,
            float depthScale,
            float depthFarZ,
            int useDepth,
            ref LandmarkFilterState filter,
            int filterEnabled,
            float3 minCutoff,
            float3 beta,
            float derivativeCutoffHz,
            long timestampUs,
            float depthCorrection,
            int useCorrection,
            in LandmarkOverlayMapping mapping,
            out float3 targetPos)
        {
            if (filter.Mode != useDepth)
            {
                filter.Initialized = 0;
                filter.Mode = useDepth;
            }

            // 원점·앵커 변화 대신 최후방 점 기준의 상대 깊이를 필터링한다.
            var relativeDepth = useDepth != 0 ? worldZ - depthFarZ : 0f;
            var filtered = OneEuroFilter.Filter(
                new float3(imageX, imageY, relativeDepth),
                ref filter,
                filterEnabled,
                minCutoff,
                beta,
                derivativeCutoffHz,
                timestampUs);
            var depth = useDepth != 0 ? math.min(filtered.z, 0f) * depthScale : 0f;
            if (useDepth != 0 && useCorrection != 0)
            {
                depth += depthCorrection;
            }

            targetPos = LandmarkOverlayMapping.MapWithDepth(filtered.x, filtered.y, depth, in mapping);
        }

        /// <summary>
        /// 대상별 Z 오프셋 갱신. 대표값이 커지면(가까워지면) 음수 표시 오프셋을 낸다.
        /// 무효·OFF·세대 변경·대상 변경 시 상태를 초기화하고 보정 0으로 즉시 원복한다.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateDepthCorrection(
            ref LandmarkDepthCorrection state,
            bool sampleValid,
            float representative,
            long depthTimestampUs,
            long depthEpoch,
            int identity,
            DepthSettings settings,
            out float correction,
            out int useCorrection)
        {
            correction = 0f;
            useCorrection = 0;
            if (!sampleValid || settings.Enabled == 0 || settings.Weight == 0f)
            {
                state.Initialized = 0;
                return;
            }

            if (state.Initialized == 0 || state.Identity != identity || state.DepthEpoch != depthEpoch
                || depthTimestampUs < state.LastDepthTimestampUs)
            {
                state.Initialized = 1;
                state.Identity = identity;
                state.Baseline = representative;
                state.Filtered = 0f;
                state.LastDepthTimestampUs = depthTimestampUs;
                state.DepthEpoch = depthEpoch;
                return;
            }

            if (depthTimestampUs == state.LastDepthTimestampUs)
            {
                correction = state.Filtered;
                useCorrection = 1;
                return;
            }

            state.LastDepthTimestampUs = depthTimestampUs;
            var target = -(representative - state.Baseline) * settings.DepthGain;
            target = math.clamp(target, -settings.MaxOffset, settings.MaxOffset);
            // ponytail: 새 깊이 입력마다 0.5 추종(약 15Hz 입력에 2프레임 시정수). P2 평가에서 조정.
            state.Filtered += (target - state.Filtered) * 0.5f;
            correction = state.Filtered;
            useCorrection = 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void HidePoint(ref LocalTransform transform, ref LandmarkFilterState filter)
        {
            filter.Initialized = 0;
            var hidden = transform;
            hidden.Scale = 0f;
            transform = hidden;
        }
    }
}
