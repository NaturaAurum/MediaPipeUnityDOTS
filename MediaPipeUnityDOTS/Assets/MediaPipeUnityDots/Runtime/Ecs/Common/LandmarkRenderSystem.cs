using Unity.Burst;
using MediaPipeUnityDots.Runtime.Interop;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 트래커 공통 렌더 시스템. 필터링은 LandmarkFilterSystem이 먼저 수행하며,
    /// 이 시스템은 filtered 버퍼를 표시 좌표로만 매핑한다.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(LandmarkFilterSystem))]
    public partial struct LandmarkRenderSystem : ISystem
    {
        private const float HandPointScale = 0.05f;
        private const float FacePointScale = 0.02f;
        private const float PosePointScale = 0.04f;
        private const int HandLandmarks = MpudHandResult.LandmarksPerHand;
        private const int FaceLandmarks = MpudFaceResult.LandmarksPerFace;
        private const int PoseLandmarks = MpudPoseResult.LandmarksPerPose;

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
            // RenderMode는 깊이 표시 방식만 바꾸며 filtered 값과 필터 상태에는 관여하지 않는다.
            var renderMode = filterSettings.RenderMode;

            DynamicBuffer<FilteredLandmarkElement> handLandmarks = default;
            DynamicBuffer<FilteredWorldLandmarkElement> handWorld = default;
            DynamicBuffer<FilteredLandmarkElement> faceLandmarks = default;
            DynamicBuffer<FilteredLandmarkElement> poseLandmarks = default;
            DynamicBuffer<FilteredWorldLandmarkElement> poseWorld = default;
            FilteredLandmarkStatus handFilteredStatus = default;
            FilteredLandmarkStatus faceFilteredStatus = default;
            FilteredLandmarkStatus poseFilteredStatus = default;
            var hasHand = false;
            var hasFace = false;
            var hasPose = false;

            foreach (var (currentStatus, entity) in SystemAPI.Query<RefRO<FilteredLandmarkStatus>>().WithEntityAccess())
            {
                var status = currentStatus.ValueRO;
                switch (status.Tracker)
                {
                    case LandmarkTracker.Hand:
                        handFilteredStatus = status;
                        handLandmarks = state.EntityManager.GetBuffer<FilteredLandmarkElement>(entity, true);
                        handWorld = state.EntityManager.GetBuffer<FilteredWorldLandmarkElement>(entity, true);
                        hasHand = true;
                        break;
                    case LandmarkTracker.Face:
                        faceFilteredStatus = status;
                        faceLandmarks = state.EntityManager.GetBuffer<FilteredLandmarkElement>(entity, true);
                        hasFace = true;
                        break;
                    case LandmarkTracker.Pose:
                        poseFilteredStatus = status;
                        poseLandmarks = state.EntityManager.GetBuffer<FilteredLandmarkElement>(entity, true);
                        poseWorld = state.EntityManager.GetBuffer<FilteredWorldLandmarkElement>(entity, true);
                        hasPose = true;
                        break;
                }
            }

            var hasRawHand = SystemAPI.HasSingleton<HandTrackingStatus>();
            var hasRawPose = SystemAPI.HasSingleton<PoseTrackingStatus>();
            var handStatus = hasRawHand ? SystemAPI.GetSingleton<HandTrackingStatus>() : default;
            var poseStatus = hasRawPose ? SystemAPI.GetSingleton<PoseTrackingStatus>() : default;

            DynamicBuffer<HandDepthSampleElement> handDepthSamples = default;
            DynamicBuffer<PoseDepthSampleElement> poseDepthSamples = default;
            var hasDepth = SystemAPI.HasSingleton<DepthSampleStatus>()
                && SystemAPI.TryGetSingletonBuffer<HandDepthSampleElement>(out handDepthSamples, true)
                && SystemAPI.TryGetSingletonBuffer<PoseDepthSampleElement>(out poseDepthSamples, true);
            var depthStatus = hasDepth ? SystemAPI.GetSingleton<DepthSampleStatus>() : default;
            var depthSettings = SystemAPI.HasSingleton<DepthSettings>()
                ? SystemAPI.GetSingleton<DepthSettings>()
                : DepthSettings.Default;

            var handDepths = new FixedList128Bytes<float3>();
            if (renderMode != 0 && hasHand && handFilteredStatus.IsValid != 0
                && mapping.IsValid != 0 && hasRawHand && handStatus.IsValid)
            {
                var handCount = math.min(handFilteredStatus.Count / HandLandmarks, handDepths.Capacity);
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
                        if (image.Target == hand && world.Target == hand
                            && math.all(math.isfinite(imagePosition)) && math.all(math.isfinite(worldPosition)))
                        {
                            bounds.Add(imagePosition, worldPosition);
                        }
                    }

                    handDepths.Add(bounds.Resolve(in mapping));
                }
            }

            var poseDepths = new FixedList128Bytes<float3>();
            if (renderMode != 0 && hasPose && poseFilteredStatus.IsValid != 0
                && mapping.IsValid != 0 && hasRawPose && poseStatus.IsValid)
            {
                var poseCount = math.min(poseFilteredStatus.Count / PoseLandmarks, poseDepths.Capacity);
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
                        if (image.Target == pose && world.Target == pose
                            && math.all(math.isfinite(imagePosition)) && math.all(math.isfinite(worldPosition)))
                        {
                            bounds.Add(imagePosition, worldPosition);
                        }
                    }

                    poseDepths.Add(bounds.Resolve(in mapping));
                }
            }

            foreach (var (transform, point, correctionState)
                in SystemAPI.Query<RefRW<LocalTransform>, RefRO<LandmarkPoint>, RefRW<LandmarkDepthCorrection>>())
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
                var pointScale = HandPointScale;

                var correction = 0f;
                var useCorrection = 0;

                if (valid)
                {
                    switch (tracker)
                    {
                        case LandmarkTracker.Hand when hasHand && handFilteredStatus.IsValid != 0:
                            pointScale = HandPointScale;
                            valid = target >= 0 && target < handFilteredStatus.Count / HandLandmarks
                                && index < HandLandmarks
                                && target * HandLandmarks + index < handLandmarks.Length
                                && handLandmarks[target * HandLandmarks + index].Target == target
                                && handLandmarks[target * HandLandmarks + index].Index == index;
                            if (valid)
                            {
                                var element = handLandmarks[target * HandLandmarks + index];
                                imageX = element.X;
                                imageY = element.Y;
                                var worldIndex = target * HandLandmarks + index;
                                if (target < handDepths.Length && worldIndex < handWorld.Length
                                    && handWorld[worldIndex].Target == target
                                    && handWorld[worldIndex].Index == index
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
                        case LandmarkTracker.Face when hasFace && faceFilteredStatus.IsValid != 0:
                            pointScale = FacePointScale;
                            valid = target >= 0 && target < faceFilteredStatus.Count / FaceLandmarks
                                && index < FaceLandmarks
                                && target * FaceLandmarks + index < faceLandmarks.Length
                                && faceLandmarks[target * FaceLandmarks + index].Target == target
                                && faceLandmarks[target * FaceLandmarks + index].Index == index;
                            if (valid)
                            {
                                var element = faceLandmarks[target * FaceLandmarks + index];
                                imageX = element.X;
                                imageY = element.Y;
                            }

                            break;
                        case LandmarkTracker.Pose when hasPose && poseFilteredStatus.IsValid != 0:
                            pointScale = PosePointScale;
                            valid = target >= 0 && target < poseFilteredStatus.Count / PoseLandmarks
                                && index < PoseLandmarks
                                && target * PoseLandmarks + index < poseLandmarks.Length
                                && poseLandmarks[target * PoseLandmarks + index].Target == target
                                && poseLandmarks[target * PoseLandmarks + index].Index == index;
                            if (valid)
                            {
                                var element = poseLandmarks[target * PoseLandmarks + index];
                                imageX = element.X;
                                imageY = element.Y;
                                var worldIndex = target * PoseLandmarks + index;
                                if (target < poseDepths.Length && worldIndex < poseWorld.Length
                                    && poseWorld[worldIndex].Target == target
                                    && poseWorld[worldIndex].Index == index
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

                if (valid && useDepth != 0 && renderMode != 0)
                {
                    if (tracker == LandmarkTracker.Hand && hasDepth && depthStatus.IsValid
                        && target < handDepthSamples.Length && handDepthSamples[target].HandIndex == target
                        && (depthStatus.HandValidMask & (1 << target)) != 0
                        && depthStatus.CaptureEpoch == handStatus.CaptureEpoch
                        && DepthSampleGate.IsAligned(handStatus.CaptureTimestampUs, depthStatus.CaptureTimestampUs, depthSettings.MaxAlignmentDeltaUs))
                    {
                        var identity = target < handStatus.HandednessList.Length ? handStatus.HandednessList[target] : -1;
                        LandmarkRender.UpdateDepthCorrection(ref correctionState.ValueRW, true, handDepthSamples[target].Depth, depthStatus.CaptureTimestampUs, depthStatus.CaptureEpoch, identity, depthSettings, out correction, out useCorrection);
                    }
                    else if (tracker == LandmarkTracker.Pose && hasDepth && depthStatus.IsValid && depthStatus.PoseValid != 0
                        && target < poseDepthSamples.Length && poseDepthSamples[target].PoseIndex == target
                        && depthStatus.CaptureEpoch == poseStatus.CaptureEpoch
                        && DepthSampleGate.IsAligned(poseStatus.CaptureTimestampUs, depthStatus.CaptureTimestampUs, depthSettings.MaxAlignmentDeltaUs))
                    {
                        LandmarkRender.UpdateDepthCorrection(ref correctionState.ValueRW, true, poseDepthSamples[target].Depth, depthStatus.CaptureTimestampUs, depthStatus.CaptureEpoch, 0, depthSettings, out correction, out useCorrection);
                    }
                    else
                    {
                        correctionState.ValueRW.Initialized = 0;
                    }
                }
                else
                {
                    correctionState.ValueRW.Initialized = 0;
                }

                if (valid)
                {
                    LandmarkRender.ResolveFilteredPoint(
                        imageX,
                        imageY,
                        worldZ,
                        depthScale,
                        depthFarZ,
                        useDepth,
                        correction,
                        useCorrection,
                        in mapping,
                        out var targetPos);
                    transform.ValueRW = LocalTransform.FromPositionRotationScale(
                        targetPos, quaternion.identity, pointScale);
                }
                else
                {
                    LandmarkRender.HidePoint(ref transform.ValueRW);
                }
            }
        }
    }
}
