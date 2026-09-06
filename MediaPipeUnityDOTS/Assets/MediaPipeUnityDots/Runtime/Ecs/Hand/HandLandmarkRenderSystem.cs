using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 싱글턴 상태+버퍼를 읽어 21개 포인트 엔티티의 LocalTransform을 기록한다.
    /// 2D/3D 모두 영상 XY에 정합하고, 3D는 월드 Z의 상대 깊이를 카메라 광선 위에 배치한다.
    /// 필터는 입력 타임스탬프가 바뀔 때만 전진하므로 렌더 FPS와 무관하다.
    /// 무효 상태나 버퍼 부족 인덱스는 필터 상태를 리셋하고 스케일 0으로 숨긴다.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct HandLandmarkRenderSystem : ISystem
    {
        private const float PointScale = 0.05f;
        private const int MaxLandmarksPerHand = 21;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<HandTrackingStatus>();
            state.RequireForUpdate<HandLandmarkPoint>();
            state.RequireForUpdate<LandmarkOverlayMapping>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var status = SystemAPI.GetSingleton<HandTrackingStatus>();
            var landmarks = SystemAPI.GetSingletonBuffer<LandmarkElement>();
            var worldLandmarks = SystemAPI.GetSingletonBuffer<WorldLandmarkElement>();
            var mapping = SystemAPI.GetSingleton<LandmarkOverlayMapping>();
            var filterSettings = SystemAPI.HasSingleton<OneEuroFilterSettings>()
                ? SystemAPI.GetSingleton<OneEuroFilterSettings>()
                : OneEuroFilterSettings.Default;

            var inputTimestampUs = status.TimestampUs;
            var renderMode = filterSettings.RenderMode;
            var minCutoff = new float3(filterSettings.HandMinCutoff, filterSettings.HandMinCutoff, filterSettings.ZMinCutoff);
            var beta = new float3(filterSettings.HandBeta, filterSettings.HandBeta, filterSettings.ZBeta);
            var depthParameters = new FixedList128Bytes<float2>();
            if (renderMode != 0 && mapping.IsValid != 0 && status.IsValid)
            {
                var handCount = math.min(status.HandCount, depthParameters.Capacity);
                for (var hand = 0; hand < handCount; hand++)
                {
                    var bounds = new LandmarkDepthBounds();
                    var start = hand * MaxLandmarksPerHand;
                    var end = math.min(start + MaxLandmarksPerHand, math.min(landmarks.Length, worldLandmarks.Length));
                    for (var i = start; i < end; i++)
                    {
                        var image = landmarks[i];
                        var world = worldLandmarks[i];
                        var worldPosition = new float3(world.X, world.Y, world.Z);
                        var imagePosition = new float2(image.X, image.Y);
                        if (image.HandIndex == hand && world.HandIndex == hand
                            && math.all(math.isfinite(imagePosition)) && math.all(math.isfinite(worldPosition)))
                        {
                            bounds.Add(imagePosition, worldPosition);
                        }
                    }

                    depthParameters.Add(bounds.Resolve(in mapping));
                }
            }

            foreach ((var transform, var point, var filter)
                in SystemAPI.Query<RefRW<LocalTransform>, RefRO<HandLandmarkPoint>, RefRW<LandmarkFilterState>>())
            {
                var hand = point.ValueRO.HandIndex;
                var index = point.ValueRO.Index;
                var bufferIndex = hand * MaxLandmarksPerHand + index;
                if (mapping.IsValid != 0 && status.IsValid && hand >= 0 && hand < status.HandCount
                    && index >= 0 && index < MaxLandmarksPerHand
                    && bufferIndex >= 0 && bufferIndex < landmarks.Length
                    && landmarks[bufferIndex].HandIndex == hand)
                {
                    var element = landmarks[bufferIndex];
                    var useDepth = hand < depthParameters.Length && bufferIndex < worldLandmarks.Length
                        && worldLandmarks[bufferIndex].HandIndex == hand
                        && math.isfinite(worldLandmarks[bufferIndex].Z)
                        && math.all(math.isfinite(depthParameters[hand]));
                    var mode = useDepth ? 1 : 0;
                    if (filter.ValueRW.Mode != mode)
                    {
                        filter.ValueRW.Initialized = 0;
                        filter.ValueRW.Mode = mode;
                    }

                    // 원점·손목 변화 대신 최후방 점 기준의 상대 깊이를 필터링한다.
                    var relativeDepth = useDepth ? worldLandmarks[bufferIndex].Z - depthParameters[hand].y : 0f;
                    var filtered = OneEuroFilter.Filter(
                        new float3(element.X, element.Y, relativeDepth),
                        ref filter.ValueRW,
                        filterSettings.Enabled,
                        minCutoff,
                        beta,
                        filterSettings.DerivativeCutoffHz,
                        inputTimestampUs);
                    var depth = useDepth ? math.min(filtered.z, 0f) * depthParameters[hand].x : 0f;
                    var targetPos = LandmarkOverlayMapping.MapWithDepth(filtered.x, filtered.y, depth, in mapping);

                    transform.ValueRW = LocalTransform.FromPositionRotationScale(
                        targetPos,
                        quaternion.identity,
                        PointScale);
                }
                else
                {
                    filter.ValueRW.Initialized = 0;
                    var hidden = transform.ValueRO;
                    hidden.Scale = 0f;
                    transform.ValueRW = hidden;
                }
            }
        }
    }
}
