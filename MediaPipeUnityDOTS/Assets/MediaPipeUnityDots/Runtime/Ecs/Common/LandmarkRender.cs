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
            targetPos = LandmarkOverlayMapping.MapWithDepth(filtered.x, filtered.y, depth, in mapping);
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
