using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 싱글턴 상태+버퍼를 읽어 21개 포인트 엔티티의 LocalTransform을 기록한다.
    /// 2D(정규화 오버레이)/3D(월드 미터, 손목 앵커)를 RenderMode로 전환한다.
    /// 필터는 입력 타임스탬프가 바뀔 때만 전진하므로 렌더 FPS와 무관하다.
    /// 무효 상태나 버퍼 부족 인덱스는 필터 상태를 리셋하고 스케일 0으로 숨긴다.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct HandLandmarkRenderSystem : ISystem
    {
        private const float PointScale = 0.05f;
        private const int MaxLandmarksPerHand = 21;
        private const int WristIndex = 0;
        private const float WorldScale = 4f;

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
            var worldRight = math.normalize(mapping.AxisX);
            var worldUp = math.normalize(mapping.AxisY);

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
                    if (filter.ValueRW.Mode != renderMode)
                    {
                        filter.ValueRW.Initialized = 0;
                        filter.ValueRW.Mode = renderMode;
                    }

                    var wristIndex = hand * MaxLandmarksPerHand + WristIndex;
                    float3 targetPos;
                    if (renderMode != 0 && bufferIndex < worldLandmarks.Length
                        && worldLandmarks[bufferIndex].HandIndex == hand
                        && wristIndex >= 0 && wristIndex < landmarks.Length
                        && wristIndex < worldLandmarks.Length
                        && landmarks[wristIndex].HandIndex == hand
                        && worldLandmarks[wristIndex].HandIndex == hand)
                    {
                        var w = worldLandmarks[bufferIndex];
                        var filtered = OneEuroFilter.Filter(
                            new float3(w.X, w.Y, w.Z),
                            ref filter.ValueRW,
                            filterSettings.Enabled,
                            minCutoff,
                            beta,
                            filterSettings.DerivativeCutoffHz,
                            inputTimestampUs);
                        // 앵커는 원시 손목 좌표(필터 상태는 포인트별 소유라 공유 불가).
                        var anchor = LandmarkOverlayMapping.Map(
                            landmarks[wristIndex].X, landmarks[wristIndex].Y, in mapping);
                        var wristWorld = worldLandmarks[wristIndex];
                        var center = new float3(wristWorld.X, wristWorld.Y, wristWorld.Z);
                        targetPos = LandmarkOverlayMapping.MapWorld(
                            filtered, center, anchor, worldRight, worldUp, mapping.Forward, WorldScale);
                    }
                    else
                    {
                        var element = landmarks[bufferIndex];
                        var filtered = OneEuroFilter.Filter(
                            new float3(element.X, element.Y, element.Z),
                            ref filter.ValueRW,
                            filterSettings.Enabled,
                            minCutoff,
                            beta,
                            filterSettings.DerivativeCutoffHz,
                            inputTimestampUs);
                        targetPos = LandmarkOverlayMapping.Map(filtered.x, filtered.y, in mapping);
                    }

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
