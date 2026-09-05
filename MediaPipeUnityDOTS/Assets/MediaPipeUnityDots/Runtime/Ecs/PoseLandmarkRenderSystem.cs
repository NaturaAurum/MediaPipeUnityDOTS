using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 싱글턴 상태+버퍼를 읽어 포즈 포인트 엔티티의 LocalTransform을 기록한다.
    /// 정규화→월드 매핑의 유일한 소유자. 무효 상태나 버퍼 부족 인덱스는 스케일 0으로 숨긴다.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct PoseLandmarkRenderSystem : ISystem
    {
        private const float WorldWidth = 2f;
        private const float WorldHeight = 1.5f;
        private const float DepthScale = 1f;
        private const float PointScale = 0.04f;
        private const int MaxLandmarksPerPose = 33;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PoseTrackingStatus>();
            state.RequireForUpdate<PoseLandmarkPoint>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var status = SystemAPI.GetSingleton<PoseTrackingStatus>();
            var landmarks = SystemAPI.GetSingletonBuffer<PoseLandmarkElement>();

            foreach ((var transform, var point)
                in SystemAPI.Query<RefRW<LocalTransform>, RefRO<PoseLandmarkPoint>>())
            {
                var pose = point.ValueRO.PoseIndex;
                var index = point.ValueRO.Index;
                var bufferIndex = pose * MaxLandmarksPerPose + index;
                if (status.IsValid && pose >= 0 && pose < status.PoseCount
                    && index >= 0 && index < MaxLandmarksPerPose
                    && bufferIndex >= 0 && bufferIndex < landmarks.Length
                    && landmarks[bufferIndex].PoseIndex == pose)
                {
                    var element = landmarks[bufferIndex];
                    transform.ValueRW = LocalTransform.FromPositionRotationScale(
                        new float3(
                            (element.X - 0.5f) * WorldWidth,
                            (0.5f - element.Y) * WorldHeight,
                            -element.Z * DepthScale),
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
