using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 싱글턴 상태+버퍼를 읽어 21개 포인트 엔티티의 LocalTransform을 기록한다.
    /// 배경 Quad 정합 매핑(LandmarkOverlayMapping)을 공유하고 1 Euro Filter로 지터를 잡는다.
    /// 필터는 입력 타임스탬프가 바뀔 때만 전진하므로 렌더 FPS와 무관하다.
    /// 무효 상태나 버퍼 부족 인덱스는 필터 상태를 리셋하고 스케일 0으로 숨긴다.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct HandLandmarkRenderSystem : ISystem
    {
        private const float PointScale = 0.05f;
        private const int MaxLandmarksPerHand = 21;
        private const float DerivativeCutoffHz = 1f;

        private static readonly float3 FilterMinCutoffHz = new(1f, 1f, 0.3f);
        private static readonly float3 FilterBeta = new(0.007f, 0.007f, 0.002f);

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
            var mapping = SystemAPI.GetSingleton<LandmarkOverlayMapping>();
            var inputTimestampUs = status.TimestampUs;

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
                    var filtered = OneEuroFilter.Filter(
                        new float3(element.X, element.Y, element.Z),
                        ref filter.ValueRW,
                        FilterMinCutoffHz,
                        FilterBeta,
                        DerivativeCutoffHz,
                        inputTimestampUs);
                    transform.ValueRW = LocalTransform.FromPositionRotationScale(
                        LandmarkOverlayMapping.Map(filtered.x, filtered.y, in mapping),
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
