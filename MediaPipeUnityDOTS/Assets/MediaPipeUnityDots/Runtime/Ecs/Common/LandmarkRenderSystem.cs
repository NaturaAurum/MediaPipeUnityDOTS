using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 트래커 공통 렌더 시스템. Hand/Face/Pose 렌더 3종을 대체한다.
    /// 포인트의 Tracker로 기존 싱글턴·버퍼 중 하나를 고르고, 계산은 LandmarkRender 공유 코어가 한다.
    /// 2D/3D 모두 영상 XY에 정합하고, 3D는 월드 Z의 상대 깊이를 카메라 광선 위에 배치한다.
    /// Face는 월드 미지원이라 2D 폴백이다.
    /// 필터는 입력 타임스탬프가 바뀔 때만 전진하므로 렌더 FPS와 무관하다.
    /// 무효 상태나 버퍼 부족 인덱스는 필터 상태를 리셋하고 스케일 0으로 숨긴다.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct LandmarkRenderSystem : ISystem
    {
        private const float HandPointScale = 0.05f;
        private const float FacePointScale = 0.02f;
        private const float PosePointScale = 0.04f;
        private const int HandLandmarks = 21;
        private const int FaceLandmarks = 478;
        private const int PoseLandmarks = 33;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<LandmarkPoint>();
            state.RequireForUpdate<LandmarkOverlayMapping>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var mapping = SystemAPI.GetSingleton<LandmarkOverlayMapping>();
            var filterSettings = SystemAPI.HasSingleton<OneEuroFilterSettings>()
                ? SystemAPI.GetSingleton<OneEuroFilterSettings>()
                : OneEuroFilterSettings.Default;
            var renderMode = filterSettings.RenderMode;

            DynamicBuffer<LandmarkElement> handLandmarks = default;
            DynamicBuffer<HandWorldLandmarkElement> handWorld = default;
            DynamicBuffer<FaceLandmarkElement> faceLandmarks = default;
            DynamicBuffer<PoseLandmarkElement> poseLandmarks = default;
            DynamicBuffer<PoseWorldLandmarkElement> poseWorld = default;
            var hasHand = SystemAPI.HasSingleton<HandTrackingStatus>()
                && SystemAPI.TryGetSingletonBuffer<LandmarkElement>(out handLandmarks, true)
                && SystemAPI.TryGetSingletonBuffer<HandWorldLandmarkElement>(out handWorld, true);
            var hasFace = SystemAPI.HasSingleton<FaceTrackingStatus>()
                && SystemAPI.TryGetSingletonBuffer<FaceLandmarkElement>(out faceLandmarks, true);
            var hasPose = SystemAPI.HasSingleton<PoseTrackingStatus>()
                && SystemAPI.TryGetSingletonBuffer<PoseWorldLandmarkElement>(out poseWorld, true)
                && SystemAPI.TryGetSingletonBuffer<PoseLandmarkElement>(out poseLandmarks, true);

            var handStatus = hasHand ? SystemAPI.GetSingleton<HandTrackingStatus>() : default;
            var faceStatus = hasFace ? SystemAPI.GetSingleton<FaceTrackingStatus>() : default;
            var poseStatus = hasPose ? SystemAPI.GetSingleton<PoseTrackingStatus>() : default;

            var handDepths = new FixedList128Bytes<float2>();
            if (renderMode != 0 && hasHand && mapping.IsValid != 0 && handStatus.IsValid)
            {
                var handCount = math.min(handStatus.HandCount, handDepths.Capacity);
                for (var hand = 0; hand < handCount; hand++)
                {
                    var bounds = new LandmarkDepthBounds();
                    var start = hand * HandLandmarks;
                    var end = math.min(start + HandLandmarks, math.min(handLandmarks.Length, handWorld.Length));
                    for (var i = start; i < end; i++)
                    {
                        var image = handLandmarks[i];
                        var world = handWorld[i];
                        var worldPosition = new float3(world.X, world.Y, world.Z);
                        var imagePosition = new float2(image.X, image.Y);
                        if (image.HandIndex == hand && world.HandIndex == hand
                            && math.all(math.isfinite(imagePosition)) && math.all(math.isfinite(worldPosition)))
                        {
                            bounds.Add(imagePosition, worldPosition);
                        }
                    }

                    handDepths.Add(bounds.Resolve(in mapping));
                }
            }

            var poseDepths = new FixedList128Bytes<float2>();
            if (renderMode != 0 && hasPose && mapping.IsValid != 0 && poseStatus.IsValid)
            {
                var poseCount = math.min(poseStatus.PoseCount, poseDepths.Capacity);
                for (var pose = 0; pose < poseCount; pose++)
                {
                    var bounds = new LandmarkDepthBounds();
                    var start = pose * PoseLandmarks;
                    var end = math.min(start + PoseLandmarks, math.min(poseLandmarks.Length, poseWorld.Length));
                    for (var i = start; i < end; i++)
                    {
                        var image = poseLandmarks[i];
                        var world = poseWorld[i];
                        var worldPosition = new float3(world.X, world.Y, world.Z);
                        var imagePosition = new float2(image.X, image.Y);
                        if (image.PoseIndex == pose && world.PoseIndex == pose
                            && math.all(math.isfinite(imagePosition)) && math.all(math.isfinite(worldPosition)))
                        {
                            bounds.Add(imagePosition, worldPosition);
                        }
                    }

                    poseDepths.Add(bounds.Resolve(in mapping));
                }
            }

            var handCutoff = new float3(filterSettings.HandMinCutoff, filterSettings.HandMinCutoff, filterSettings.ZMinCutoff);
            var handBeta = new float3(filterSettings.HandBeta, filterSettings.HandBeta, filterSettings.ZBeta);
            var faceCutoff = new float3(filterSettings.FaceMinCutoff, filterSettings.FaceMinCutoff, filterSettings.ZMinCutoff);
            var faceBeta = new float3(filterSettings.FaceBeta, filterSettings.FaceBeta, filterSettings.ZBeta);
            var poseCutoff = new float3(filterSettings.PoseMinCutoff, filterSettings.PoseMinCutoff, filterSettings.ZMinCutoff);
            var poseBeta = new float3(filterSettings.PoseBeta, filterSettings.PoseBeta, filterSettings.ZBeta);

            foreach (var (transform, point, filter)
                in SystemAPI.Query<RefRW<LocalTransform>, RefRO<LandmarkPoint>, RefRW<LandmarkFilterState>>())
            {
                var tracker = point.ValueRO.Tracker;
                var target = point.ValueRO.Target;
                var index = point.ValueRO.Index;
                var valid = mapping.IsValid != 0 && index >= 0;
                var imageX = 0f;
                var imageY = 0f;
                var worldZ = 0f;
                var useDepth = 0;
                var depthScale = 0f;
                var depthFarZ = 0f;
                var minCutoff = handCutoff;
                var beta = handBeta;
                var timestampUs = 0L;
                var pointScale = HandPointScale;

                if (valid)
                {
                    switch (tracker)
                    {
                        case LandmarkTracker.Hand when hasHand:
                            pointScale = HandPointScale;
                            minCutoff = handCutoff;
                            beta = handBeta;
                            timestampUs = handStatus.TimestampUs;
                            valid = target >= 0 && target < handStatus.HandCount && handStatus.IsValid
                                && index < HandLandmarks
                                && target * HandLandmarks + index < handLandmarks.Length
                                && handLandmarks[target * HandLandmarks + index].HandIndex == target;
                            if (valid)
                            {
                                var element = handLandmarks[target * HandLandmarks + index];
                                imageX = element.X;
                                imageY = element.Y;
                                var worldIndex = target * HandLandmarks + index;
                                if (target < handDepths.Length && worldIndex < handWorld.Length
                                    && handWorld[worldIndex].HandIndex == target
                                    && math.isfinite(handWorld[worldIndex].Z)
                                    && math.all(math.isfinite(handDepths[target])))
                                {
                                    useDepth = 1;
                                    worldZ = handWorld[worldIndex].Z;
                                    depthScale = handDepths[target].x;
                                    depthFarZ = handDepths[target].y;
                                }
                            }

                            break;
                        case LandmarkTracker.Face when hasFace:
                            pointScale = FacePointScale;
                            minCutoff = faceCutoff;
                            beta = faceBeta;
                            timestampUs = faceStatus.TimestampUs;
                            valid = target >= 0 && target < faceStatus.FaceCount && faceStatus.IsValid
                                && index < FaceLandmarks
                                && target * FaceLandmarks + index < faceLandmarks.Length
                                && faceLandmarks[target * FaceLandmarks + index].FaceIndex == target;
                            if (valid)
                            {
                                var element = faceLandmarks[target * FaceLandmarks + index];
                                imageX = element.X;
                                imageY = element.Y;
                            }

                            break;
                        case LandmarkTracker.Pose when hasPose:
                            pointScale = PosePointScale;
                            minCutoff = poseCutoff;
                            beta = poseBeta;
                            timestampUs = poseStatus.TimestampUs;
                            valid = target >= 0 && target < poseStatus.PoseCount && poseStatus.IsValid
                                && index < PoseLandmarks
                                && target * PoseLandmarks + index < poseLandmarks.Length
                                && poseLandmarks[target * PoseLandmarks + index].PoseIndex == target;
                            if (valid)
                            {
                                var element = poseLandmarks[target * PoseLandmarks + index];
                                imageX = element.X;
                                imageY = element.Y;
                                var worldIndex = target * PoseLandmarks + index;
                                if (target < poseDepths.Length && worldIndex < poseWorld.Length
                                    && poseWorld[worldIndex].PoseIndex == target
                                    && math.isfinite(poseWorld[worldIndex].Z)
                                    && math.all(math.isfinite(poseDepths[target])))
                                {
                                    useDepth = 1;
                                    worldZ = poseWorld[worldIndex].Z;
                                    depthScale = poseDepths[target].x;
                                    depthFarZ = poseDepths[target].y;
                                }
                            }

                            break;
                        default:
                            valid = false;
                            break;
                    }
                }

                if (valid)
                {
                    LandmarkRender.ResolvePoint(
                        imageX, imageY, worldZ, depthScale, depthFarZ, useDepth,
                        ref filter.ValueRW, filterSettings.Enabled,
                        minCutoff, beta, filterSettings.DerivativeCutoffHz, timestampUs,
                        in mapping, out var targetPos);
                    transform.ValueRW = LocalTransform.FromPositionRotationScale(
                        targetPos, quaternion.identity, pointScale);
                }
                else
                {
                    LandmarkRender.HidePoint(ref transform.ValueRW, ref filter.ValueRW);
                }
            }
        }
    }
}
