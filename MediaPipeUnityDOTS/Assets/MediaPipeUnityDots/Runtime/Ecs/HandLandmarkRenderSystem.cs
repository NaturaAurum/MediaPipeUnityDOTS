using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 싱글턴 상태+버퍼를 읽어 21개 포인트 엔티티의 LocalTransform을 기록한다.
    /// 배경 Quad 정합 매핑(LandmarkOverlayMapping)을 공유한다. 무효 상태나 버퍼 부족 인덱스는 스케일 0으로 숨긴다.
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
            var mapping = SystemAPI.GetSingleton<LandmarkOverlayMapping>();

            foreach ((var transform, var point)
                in SystemAPI.Query<RefRW<LocalTransform>, RefRO<HandLandmarkPoint>>())
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
                    transform.ValueRW = LocalTransform.FromPositionRotationScale(
                        LandmarkOverlayMapping.Map(element.X, element.Y, in mapping),
                        quaternion.identity,
                        PointScale);
                }
                else
                {
                    var hidden = transform.ValueRO;
                    hidden.Scale = 0f;
                    transform.ValueRW = hidden;
                }
            }
        }
    }
}
