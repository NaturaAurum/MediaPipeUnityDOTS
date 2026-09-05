using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 싱글턴 상태+버퍼를 읽어 포즈 포인트 엔티티의 LocalTransform을 기록한다.
    /// 2D(정규화 오버레이)/3D(월드 미터, 고관절 앵커)를 RenderMode로 전환한다.
    /// 필터는 입력 타임스탬프가 바뀔 때만 전진하므로 렌더 FPS와 무관하다.
    /// 무효 상태나 버퍼 부족 인덱스는 필터 상태를 리셋하고 스케일 0으로 숨긴다.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct PoseLandmarkRenderSystem : ISystem
    {
        private const float PointScale = 0.04f;
        private const int MaxLandmarksPerPose = 33;
        private const int LeftHipIndex = 23;
        private const int RightHipIndex = 24;
        private const float WorldScale = 1f;

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
            var worldLandmarks = SystemAPI.GetSingletonBuffer<PoseWorldLandmarkElement>();
            var mapping = SystemAPI.GetSingleton<LandmarkOverlayMapping>();
            var filterSettings = SystemAPI.HasSingleton<OneEuroFilterSettings>()
                ? SystemAPI.GetSingleton<OneEuroFilterSettings>()
                : OneEuroFilterSettings.Default;

            var inputTimestampUs = status.TimestampUs;
            var renderMode = filterSettings.RenderMode;
            var minCutoff = new float3(filterSettings.PoseMinCutoff, filterSettings.PoseMinCutoff, filterSettings.ZMinCutoff);
            var beta = new float3(filterSettings.PoseBeta, filterSettings.PoseBeta, filterSettings.ZBeta);
            var worldRight = math.normalize(mapping.AxisX);
            var worldUp = math.normalize(mapping.AxisY);

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
                    if (filter.ValueRW.Mode != renderMode)
                    {
                        filter.ValueRW.Initialized = 0;
                        filter.ValueRW.Mode = renderMode;
                    }

                    var hipLeft = pose * MaxLandmarksPerPose + LeftHipIndex;
                    var hipRight = pose * MaxLandmarksPerPose + RightHipIndex;
                    float3 targetPos;
                    if (renderMode != 0 && bufferIndex < worldLandmarks.Length
                        && worldLandmarks[bufferIndex].PoseIndex == pose
                        && hipLeft >= 0 && hipRight < landmarks.Length
                        && hipRight < worldLandmarks.Length
                        && landmarks[hipLeft].PoseIndex == pose
                        && landmarks[hipRight].PoseIndex == pose
                        && worldLandmarks[hipLeft].PoseIndex == pose
                        && worldLandmarks[hipRight].PoseIndex == pose)
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
                        // 앵커는 원시 고관절 중점(필터 상태는 포인트별 소유라 공유 불가).
                        var anchorNorm = (new float2(landmarks[hipLeft].X, landmarks[hipLeft].Y)
                            + new float2(landmarks[hipRight].X, landmarks[hipRight].Y)) * 0.5f;
                        var anchor = LandmarkOverlayMapping.Map(anchorNorm.x, anchorNorm.y, in mapping);
                        var hipLeftWorld = worldLandmarks[hipLeft];
                        var hipRightWorld = worldLandmarks[hipRight];
                        var center = new float3(
                            (hipLeftWorld.X + hipRightWorld.X) * 0.5f,
                            (hipLeftWorld.Y + hipRightWorld.Y) * 0.5f,
                            (hipLeftWorld.Z + hipRightWorld.Z) * 0.5f);
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
