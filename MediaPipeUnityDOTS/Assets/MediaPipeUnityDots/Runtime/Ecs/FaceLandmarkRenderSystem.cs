using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 싱글턴 상태+버퍼를 읽어 얼굴 포인트 엔티티의 LocalTransform을 기록한다.
    /// 배경 Quad 정합 매핑(LandmarkOverlayMapping)을 공유하고 OneEuroFilterSettings 설정을 반영한다.
    /// 필터는 입력 타임스탬프가 바뀔 때만 전진하므로 렌더 FPS와 무관하다.
    /// 무효 상태나 버퍼 부족 인덱스는 필터 상태를 리셋하고 스케일 0으로 숨긴다.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct FaceLandmarkRenderSystem : ISystem
    {
        private const float PointScale = 0.02f;
        private const int MaxLandmarksPerFace = 478;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<FaceTrackingStatus>();
            state.RequireForUpdate<FaceLandmarkPoint>();
            state.RequireForUpdate<LandmarkOverlayMapping>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var status = SystemAPI.GetSingleton<FaceTrackingStatus>();
            var landmarks = SystemAPI.GetSingletonBuffer<FaceLandmarkElement>();
            var mapping = SystemAPI.GetSingleton<LandmarkOverlayMapping>();
            var filterSettings = SystemAPI.HasSingleton<OneEuroFilterSettings>()
                ? SystemAPI.GetSingleton<OneEuroFilterSettings>()
                : OneEuroFilterSettings.Default;

            var inputTimestampUs = status.TimestampUs;
            var minCutoff = new float3(filterSettings.FaceMinCutoff, filterSettings.FaceMinCutoff, filterSettings.ZMinCutoff);
            var beta = new float3(filterSettings.FaceBeta, filterSettings.FaceBeta, filterSettings.ZBeta);

            foreach ((var transform, var point, var filter)
                in SystemAPI.Query<RefRW<LocalTransform>, RefRO<FaceLandmarkPoint>, RefRW<LandmarkFilterState>>())
            {
                var face = point.ValueRO.FaceIndex;
                var index = point.ValueRO.Index;
                var bufferIndex = face * MaxLandmarksPerFace + index;
                if (mapping.IsValid != 0 && status.IsValid && face >= 0 && face < status.FaceCount
                    && index >= 0 && index < MaxLandmarksPerFace
                    && bufferIndex >= 0 && bufferIndex < landmarks.Length
                    && landmarks[bufferIndex].FaceIndex == face)
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
                    var targetPos = LandmarkOverlayMapping.Map(filtered.x, filtered.y, in mapping);

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
