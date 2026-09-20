using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 트래커 공통 표시 계산. LandmarkFilterSystem이 게시한 filtered 값을 받아 표시 좌표로만 변환한다.
    /// 깊이 보정은 filtered 결과 이후 표시용 깊이에 대상별 오프셋을 더하며 필터 상태를 소유하지 않는다.
    /// </summary>
    [BurstCompile]
    public static class LandmarkRender
    {
        /// <summary>
        /// 이미 필터링된 결과를 표시 좌표로만 변환한다. 여기서는 필터 상태를 전진시키지 않는다.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ResolveFilteredPoint(
            float imageX,
            float imageY,
            float worldZ,
            float depthScale,
            float depthFarZ,
            int useDepth,
            float depthCorrection,
            int useCorrection,
            in LandmarkOverlayMapping mapping,
            out float3 targetPos)
        {
            var depth = useDepth != 0 ? math.min(worldZ - depthFarZ, 0f) * depthScale : 0f;
            if (useDepth != 0 && useCorrection != 0)
            {
                depth += depthCorrection;
            }

            targetPos = useDepth != 0
                ? LandmarkOverlayMapping.MapShapePreserving(imageX, imageY, depth, in mapping)
                : LandmarkOverlayMapping.MapWithDepth(imageX, imageY, depth, in mapping);
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
                correction = state.Filtered * settings.Weight;
                useCorrection = 1;
                return;
            }

            state.LastDepthTimestampUs = depthTimestampUs;
            var target = -(representative - state.Baseline) * settings.DepthGain;
            target = math.clamp(target, -settings.MaxOffset, settings.MaxOffset);
            // ponytail: 새 깊이 입력마다 0.5 추종(약 15Hz 입력에 2프레임 시정수). P2 평가에서 조정.
            state.Filtered += (target - state.Filtered) * 0.5f;
            correction = state.Filtered * settings.Weight;
            useCorrection = 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void HidePoint(ref LocalTransform transform)
        {
            var hidden = transform;
            hidden.Scale = 0f;
            transform = hidden;
        }
    }
}
