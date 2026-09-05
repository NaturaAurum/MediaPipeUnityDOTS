using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 싱글턴 상태+버퍼를 읽어 포즈 포인트 엔티티의 LocalTransform을 기록한다.
    /// 배경 Quad 정합 매핑(LandmarkOverlayMapping)을 공유하고 1 Euro Filter로 지터를 잡는다.
    /// 무효 상태나 버퍼 부족 인덱스는 필터 상태를 리셋하고 스케일 0으로 숨긴다.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct PoseLandmarkRenderSystem : ISystem
    {
        private const float PointScale = 0.04f;
        private const int MaxLandmarksPerPose = 33;
        private const float DerivativeCutoffHz = 1f;

        private static readonly float3 FilterMinCutoffHz = new(0.5f, 0.5f, 0.3f);
        private static readonly float3 FilterBeta = new(0.01f, 0.01f, 0.002f);

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PoseTrackingStatus>();
            state.RequireForUpdate<PoseLandmarkPoint>();
            state.RequireForUpdate<LandmarkOverlayMapping>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var status = SystemAPI.GetSingleton<PoseTrackingStatus>();
            var landmarks = SystemAPI.GetSingletonBuffer<PoseLandmarkElement>();
            var mapping = SystemAPI.GetSingleton<LandmarkOverlayMapping>();
            var dt = SystemAPI.Time.DeltaTime;

            foreach ((var transform, var point, var filter)
                in SystemAPI.Query<RefRW<LocalTransform>, RefRO<PoseLandmarkPoint>, RefRW<LandmarkFilterState>>())
            {
                var pose = point.ValueRO.PoseIndex;
                var index = point.ValueRO.Index;
                var bufferIndex = pose * MaxLandmarksPerPose + index;
                if (mapping.IsValid != 0 && status.IsValid && pose >= 0 && pose < status.PoseCount
                    && index >= 0 && index < MaxLandmarksPerPose
                    && bufferIndex >= 0 && bufferIndex < landmarks.Length
                    && landmarks[bufferIndex].PoseIndex == pose)
                {
                    var element = landmarks[bufferIndex];
                    var filtered = OneEuroFilter.Filter(
                        new float3(element.X, element.Y, element.Z),
                        ref filter.ValueRW,
                        FilterMinCutoffHz,
                        FilterBeta,
                        DerivativeCutoffHz,
                        dt);
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
