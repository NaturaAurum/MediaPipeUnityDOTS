using Unity.Burst;
using MediaPipeUnityDots.Runtime.Interop;
using MediaPipeUnityDots.Runtime.Tracking.Filtering;
using Unity.Entities;
using Unity.Mathematics;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// raw tracking buffer를 건드리지 않고 One Euro 결과를 별도 ECS stream entity에 게시한다.
    /// 이 시스템은 카메라·매핑·렌더 엔티티를 요구하지 않는다.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(LandmarkRenderSystem))]
    public partial struct LandmarkFilterSystem : ISystem
    {
        private const int HandLandmarks = MpudHandResult.LandmarksPerHand;
        private const int FaceLandmarks = MpudFaceResult.LandmarksPerFace;
        private const int PoseLandmarks = MpudPoseResult.LandmarksPerPose;
        private const int MaxHands = MpudHandResult.MaxHands;
        private const int MaxFaces = MpudFaceResult.MaxFaces;
        private const int MaxPoses = MpudPoseResult.MaxPoses;
        private Entity _handOutput;
        private Entity _faceOutput;
        private EntityQuery _handSourceQuery;
        private EntityQuery _faceSourceQuery;
        private EntityQuery _poseSourceQuery;
        private Entity _poseOutput;

        public void OnCreate(ref SystemState state)
        {
            _handOutput = CreateOutput(state.EntityManager, LandmarkTracker.Hand, 1, MaxHands * HandLandmarks);
            _handSourceQuery = state.GetEntityQuery(ComponentType.ReadOnly<HandTrackingStatus>());
            _faceSourceQuery = state.GetEntityQuery(ComponentType.ReadOnly<FaceTrackingStatus>());
            _poseSourceQuery = state.GetEntityQuery(ComponentType.ReadOnly<PoseTrackingStatus>());
            _faceOutput = CreateOutput(state.EntityManager, LandmarkTracker.Face, 2, MaxFaces * FaceLandmarks);
            _poseOutput = CreateOutput(state.EntityManager, LandmarkTracker.Pose, 3, MaxPoses * PoseLandmarks);
        }

        public void OnDestroy(ref SystemState state)
        {
            DestroyOutput(state.EntityManager, _handOutput);
            DestroyOutput(state.EntityManager, _faceOutput);
            DestroyOutput(state.EntityManager, _poseOutput);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var settings = SystemAPI.HasSingleton<OneEuroFilterSettings>()
                ? SystemAPI.GetSingleton<OneEuroFilterSettings>()
                : OneEuroFilterSettings.Default;

            var handStatus = state.EntityManager.GetComponentData<FilteredLandmarkStatus>(_handOutput);
            var handLandmarks = state.EntityManager.GetBuffer<FilteredLandmarkElement>(_handOutput);
            var handWorld = state.EntityManager.GetBuffer<FilteredWorldLandmarkElement>(_handOutput);
            var handStates = state.EntityManager.GetBuffer<FilteredLandmarkStateElement>(_handOutput);
            DynamicBuffer<LandmarkElement> rawHand = default;
            DynamicBuffer<HandWorldLandmarkElement> rawHandWorld = default;
            DynamicBuffer<LandmarkContinuityElement> handContinuity = default;
            var hasHandSource = _handSourceQuery.CalculateEntityCount() == 1;
            var hasHandBuffers = false;
            if (hasHandSource)
            {
                var sourceEntity = _handSourceQuery.GetSingletonEntity();
                if (state.EntityManager.HasBuffer<LandmarkElement>(sourceEntity)
                    && state.EntityManager.HasBuffer<HandWorldLandmarkElement>(sourceEntity))
                {
                    rawHand = state.EntityManager.GetBuffer<LandmarkElement>(sourceEntity, true);
                    rawHandWorld = state.EntityManager.GetBuffer<HandWorldLandmarkElement>(sourceEntity, true);
                    hasHandBuffers = true;
                    if (state.EntityManager.HasBuffer<LandmarkContinuityElement>(sourceEntity))
                    {
                        handContinuity = state.EntityManager.GetBuffer<LandmarkContinuityElement>(sourceEntity, true);
                    }
                }
            }

            if (hasHandSource && hasHandBuffers)
            {
                var sourceStatus = state.EntityManager.GetComponentData<HandTrackingStatus>(_handSourceQuery.GetSingletonEntity());
                ProcessHand(
                    sourceStatus,
                    rawHand,
                    rawHandWorld,
                    handContinuity,
                    ref handStatus,
                    handLandmarks,
                    handWorld,
                    handStates,
                    in settings);
            }
            else
            {
                Invalidate(ref handStatus, handLandmarks, handWorld, handStates);
            }
            state.EntityManager.SetComponentData(_handOutput, handStatus);

            var faceStatus = state.EntityManager.GetComponentData<FilteredLandmarkStatus>(_faceOutput);
            var faceLandmarks = state.EntityManager.GetBuffer<FilteredLandmarkElement>(_faceOutput);
            var faceWorld = state.EntityManager.GetBuffer<FilteredWorldLandmarkElement>(_faceOutput);
            var faceStates = state.EntityManager.GetBuffer<FilteredLandmarkStateElement>(_faceOutput);
            if (SystemAPI.HasSingleton<FaceTrackingStatus>()
                && SystemAPI.TryGetSingletonBuffer<FaceLandmarkElement>(out DynamicBuffer<FaceLandmarkElement> rawFace, true))
            {
                var sourceStatus = SystemAPI.GetSingleton<FaceTrackingStatus>();
                ProcessFace(
                    sourceStatus,
                    rawFace,
                    ReadContinuity(state.EntityManager, _faceSourceQuery),
                    ref faceStatus,
                    faceLandmarks,
                    faceWorld,
                    faceStates,
                    in settings);
            }
            else
            {
                Invalidate(ref faceStatus, faceLandmarks, faceWorld, faceStates);
            }
            state.EntityManager.SetComponentData(_faceOutput, faceStatus);

            var poseStatus = state.EntityManager.GetComponentData<FilteredLandmarkStatus>(_poseOutput);
            var poseLandmarks = state.EntityManager.GetBuffer<FilteredLandmarkElement>(_poseOutput);
            var poseWorld = state.EntityManager.GetBuffer<FilteredWorldLandmarkElement>(_poseOutput);
            var poseStates = state.EntityManager.GetBuffer<FilteredLandmarkStateElement>(_poseOutput);
            if (SystemAPI.HasSingleton<PoseTrackingStatus>()
                && SystemAPI.TryGetSingletonBuffer<PoseLandmarkElement>(out DynamicBuffer<PoseLandmarkElement> rawPose, true)
                && SystemAPI.TryGetSingletonBuffer<PoseWorldLandmarkElement>(out DynamicBuffer<PoseWorldLandmarkElement> rawPoseWorld, true))
            {
                var sourceStatus = SystemAPI.GetSingleton<PoseTrackingStatus>();
                ProcessPose(
                    sourceStatus,
                    rawPose,
                    rawPoseWorld,
                    ReadContinuity(state.EntityManager, _poseSourceQuery),
                    ref poseStatus,
                    poseLandmarks,
                    poseWorld,
                    poseStates,
                    in settings);
            }
            else
            {
                Invalidate(ref poseStatus, poseLandmarks, poseWorld, poseStates);
            }
            state.EntityManager.SetComponentData(_poseOutput, poseStatus);
        }

        private static Entity CreateOutput(EntityManager entityManager, LandmarkTracker tracker, ulong streamId, int stateCapacity)
        {
            var entity = entityManager.CreateEntity();
            entityManager.AddComponentData(entity, new FilteredLandmarkStatus
            {
                Tracker = tracker,
                StreamId = streamId,
                IsValid = 0,
                Count = 0,
            });
            var landmarks = entityManager.AddBuffer<FilteredLandmarkElement>(entity);
            landmarks.ResizeUninitialized(stateCapacity);
            var world = entityManager.AddBuffer<FilteredWorldLandmarkElement>(entity);
            world.ResizeUninitialized(stateCapacity);
            var states = entityManager.AddBuffer<FilteredLandmarkStateElement>(entity);
            states.ResizeUninitialized(stateCapacity * 2);
            ResetStates(states);
            return entity;
        }

        private static void DestroyOutput(EntityManager entityManager, Entity entity)
        {
            if (entity != Entity.Null && entityManager.Exists(entity))
            {
                entityManager.DestroyEntity(entity);
            }
        }

        private static void ProcessHand(
            in HandTrackingStatus sourceStatus,
            DynamicBuffer<LandmarkElement> raw,
            DynamicBuffer<HandWorldLandmarkElement> rawWorld,
            DynamicBuffer<LandmarkContinuityElement> continuity,
            ref FilteredLandmarkStatus outputStatus,
            DynamicBuffer<FilteredLandmarkElement> output,
            DynamicBuffer<FilteredWorldLandmarkElement> outputWorld,
            DynamicBuffer<FilteredLandmarkStateElement> states,
            in OneEuroFilterSettings settings)
        {
            var targetCount = math.clamp(sourceStatus.HandCount, 0, MaxHands);
            var count = targetCount * HandLandmarks;
            if (!sourceStatus.IsValid || targetCount == 0 || raw.Length < count || rawWorld.Length < count)
            {
                Invalidate(ref outputStatus, output, outputWorld, states);
                return;
            }

            if (PrepareFrame(
                    ref outputStatus,
                    states,
                    continuity,
                    HandLandmarks,
                    count,
                    sourceStatus.TimestampUs,
                    sourceStatus.CaptureEpoch,
                    sourceStatus.FrameCount,
                    out var duplicate))
            {
                if (duplicate)
                {
                    return;
                }
            }

            output.ResizeUninitialized(count);
            outputWorld.ResizeUninitialized(count);
            for (var target = 0; target < targetCount; target++)
            {
                var continuityToken = ResolveContinuity(continuity, target);
                for (var index = 0; index < HandLandmarks; index++)
                {
                    var offset = target * HandLandmarks + index;
                    var image = raw[offset];
                    var world = rawWorld[offset];
                    if (image.HandIndex != target || world.HandIndex != target
                        || !IsFinite(image.X) || !IsFinite(image.Y) || !IsFinite(image.Z)
                        || !IsFinite(world.X) || !IsFinite(world.Y) || !IsFinite(world.Z))
                    {
                        Invalidate(ref outputStatus, output, outputWorld, states);
                        return;
                    }

                    var imageStateIndex = offset;
                    var worldStateIndex = MaxHands * HandLandmarks + offset;
                    var imageState = PrepareState(states, imageStateIndex, target, index, continuityToken);
                    var worldState = PrepareState(states, worldStateIndex, target, index, continuityToken);
                    var filteredImage = Filter(
                        image.X,
                        image.Y,
                        image.Z,
                        ref imageState.State,
                        in settings,
                        LandmarkFilterTracker.Hand,
                        sourceStatus.TimestampUs);
                    var filteredWorld = Filter(
                        world.X,
                        world.Y,
                        world.Z,
                        ref worldState.State,
                        in settings,
                        LandmarkFilterTracker.Hand,
                        sourceStatus.TimestampUs);
                    imageState.ContinuityToken = continuityToken;
                    worldState.ContinuityToken = continuityToken;
                    states[imageStateIndex] = imageState;
                    states[worldStateIndex] = worldState;
                    output[offset] = new FilteredLandmarkElement
                    {
                        X = filteredImage.x,
                        Y = filteredImage.y,
                        Z = filteredImage.z,
                        Visibility = image.Visibility,
                        Presence = image.Presence,
                        Target = target,
                        Index = index,
                    };
                    outputWorld[offset] = new FilteredWorldLandmarkElement
                    {
                        X = filteredWorld.x,
                        Y = filteredWorld.y,
                        Z = filteredWorld.z,
                        Visibility = world.Visibility,
                        Presence = 0f,
                        Target = target,
                        Index = index,
                    };
                }
            }

            outputStatus.IsValid = 1;
            outputStatus.Count = count;
            outputStatus.TimestampUs = sourceStatus.TimestampUs;
            outputStatus.CaptureTimestampUs = sourceStatus.CaptureTimestampUs;
            outputStatus.FrameCount = sourceStatus.FrameCount;
            outputStatus.CaptureId = sourceStatus.CaptureId;
            outputStatus.CaptureEpoch = sourceStatus.CaptureEpoch;
        }

        private static void ProcessFace(
            in FaceTrackingStatus sourceStatus,
            DynamicBuffer<FaceLandmarkElement> raw,
            DynamicBuffer<LandmarkContinuityElement> continuity,
            ref FilteredLandmarkStatus outputStatus,
            DynamicBuffer<FilteredLandmarkElement> output,
            DynamicBuffer<FilteredWorldLandmarkElement> outputWorld,
            DynamicBuffer<FilteredLandmarkStateElement> states,
            in OneEuroFilterSettings settings)
        {
            var targetCount = math.clamp(sourceStatus.FaceCount, 0, MaxFaces);
            var count = targetCount * FaceLandmarks;
            if (!sourceStatus.IsValid || targetCount == 0 || raw.Length < count)
            {
                Invalidate(ref outputStatus, output, outputWorld, states);
                return;
            }

            if (PrepareFrame(
                    ref outputStatus,
                    states,
                    continuity,
                    FaceLandmarks,
                    count,
                    sourceStatus.TimestampUs,
                    sourceStatus.CaptureEpoch,
                    sourceStatus.FrameCount,
                    out var duplicate)
                && duplicate)
            {
                return;
            }

            output.ResizeUninitialized(count);
            outputWorld.ResizeUninitialized(0);
            for (var target = 0; target < targetCount; target++)
            {
                var continuityToken = ResolveContinuity(continuity, target);
                for (var index = 0; index < FaceLandmarks; index++)
                {
                    var offset = target * FaceLandmarks + index;
                    var source = raw[offset];
                    if (source.FaceIndex != target
                        || !IsFinite(source.X) || !IsFinite(source.Y) || !IsFinite(source.Z))
                    {
                        Invalidate(ref outputStatus, output, outputWorld, states);
                        return;
                    }

                    var state = PrepareState(states, offset, target, index, continuityToken);
                    var filtered = Filter(
                        source.X,
                        source.Y,
                        source.Z,
                        ref state.State,
                        in settings,
                        LandmarkFilterTracker.Face,
                        sourceStatus.TimestampUs);
                    state.ContinuityToken = continuityToken;
                    states[offset] = state;
                    output[offset] = new FilteredLandmarkElement
                    {
                        X = filtered.x,
                        Y = filtered.y,
                        Z = filtered.z,
                        Visibility = 0f,
                        Presence = 0f,
                        Target = target,
                        Index = index,
                    };
                }
            }

            outputStatus.IsValid = 1;
            outputStatus.Count = count;
            outputStatus.TimestampUs = sourceStatus.TimestampUs;
            outputStatus.CaptureTimestampUs = sourceStatus.CaptureTimestampUs;
            outputStatus.FrameCount = sourceStatus.FrameCount;
            outputStatus.CaptureId = sourceStatus.CaptureId;
            outputStatus.CaptureEpoch = sourceStatus.CaptureEpoch;
        }

        private static void ProcessPose(
            in PoseTrackingStatus sourceStatus,
            DynamicBuffer<PoseLandmarkElement> raw,
            DynamicBuffer<PoseWorldLandmarkElement> rawWorld,
            DynamicBuffer<LandmarkContinuityElement> continuity,
            ref FilteredLandmarkStatus outputStatus,
            DynamicBuffer<FilteredLandmarkElement> output,
            DynamicBuffer<FilteredWorldLandmarkElement> outputWorld,
            DynamicBuffer<FilteredLandmarkStateElement> states,
            in OneEuroFilterSettings settings)
        {
            var targetCount = math.clamp(sourceStatus.PoseCount, 0, MaxPoses);
            var count = targetCount * PoseLandmarks;
            if (!sourceStatus.IsValid || targetCount == 0 || raw.Length < count || rawWorld.Length < count)
            {
                Invalidate(ref outputStatus, output, outputWorld, states);
                return;
            }

            if (PrepareFrame(
                    ref outputStatus,
                    states,
                    continuity,
                    PoseLandmarks,
                    count,
                    sourceStatus.TimestampUs,
                    sourceStatus.CaptureEpoch,
                    sourceStatus.FrameCount,
                    out var duplicate)
                && duplicate)
            {
                return;
            }

            output.ResizeUninitialized(count);
            outputWorld.ResizeUninitialized(count);
            for (var target = 0; target < targetCount; target++)
            {
                var continuityToken = ResolveContinuity(continuity, target);
                for (var index = 0; index < PoseLandmarks; index++)
                {
                    var offset = target * PoseLandmarks + index;
                    var image = raw[offset];
                    var world = rawWorld[offset];
                    if (image.PoseIndex != target || world.PoseIndex != target
                        || !IsFinite(image.X) || !IsFinite(image.Y) || !IsFinite(image.Z)
                        || !IsFinite(world.X) || !IsFinite(world.Y) || !IsFinite(world.Z))
                    {
                        Invalidate(ref outputStatus, output, outputWorld, states);
                        return;
                    }

                    var imageStateIndex = offset;
                    var worldStateIndex = MaxPoses * PoseLandmarks + offset;
                    var imageState = PrepareState(states, imageStateIndex, target, index, continuityToken);
                    var worldState = PrepareState(states, worldStateIndex, target, index, continuityToken);
                    var filteredImage = Filter(
                        image.X,
                        image.Y,
                        image.Z,
                        ref imageState.State,
                        in settings,
                        LandmarkFilterTracker.Pose,
                        sourceStatus.TimestampUs);
                    var filteredWorld = Filter(
                        world.X,
                        world.Y,
                        world.Z,
                        ref worldState.State,
                        in settings,
                        LandmarkFilterTracker.Pose,
                        sourceStatus.TimestampUs);
                    imageState.ContinuityToken = continuityToken;
                    worldState.ContinuityToken = continuityToken;
                    states[imageStateIndex] = imageState;
                    states[worldStateIndex] = worldState;
                    output[offset] = new FilteredLandmarkElement
                    {
                        X = filteredImage.x,
                        Y = filteredImage.y,
                        Z = filteredImage.z,
                        Visibility = 0f,
                        Presence = 0f,
                        Target = target,
                        Index = index,
                    };
                    outputWorld[offset] = new FilteredWorldLandmarkElement
                    {
                        X = filteredWorld.x,
                        Y = filteredWorld.y,
                        Z = filteredWorld.z,
                        Visibility = 0f,
                        Presence = 0f,
                        Target = target,
                        Index = index,
                    };
                }
            }

            outputStatus.IsValid = 1;
            outputStatus.Count = count;
            outputStatus.TimestampUs = sourceStatus.TimestampUs;
            outputStatus.CaptureTimestampUs = sourceStatus.CaptureTimestampUs;
            outputStatus.FrameCount = sourceStatus.FrameCount;
            outputStatus.CaptureId = sourceStatus.CaptureId;
            outputStatus.CaptureEpoch = sourceStatus.CaptureEpoch;
        }

        private static bool PrepareFrame(
            ref FilteredLandmarkStatus outputStatus,
            DynamicBuffer<FilteredLandmarkStateElement> states,
            DynamicBuffer<LandmarkContinuityElement> continuity,
            int stride,
            int count,
            long timestampUs,
            long captureEpoch,
            long frameCount,
            out bool duplicate)
        {
            duplicate = outputStatus.IsValid != 0
                && outputStatus.Count == count
                && outputStatus.TimestampUs == timestampUs
                && outputStatus.CaptureEpoch == captureEpoch;
            for (var target = 0; duplicate && target < count / stride; target++)
            {
                duplicate = states[target * stride].ContinuityToken == ResolveContinuity(continuity, target);
            }
            if (duplicate)
            {
                return true;
            }

            if (outputStatus.IsValid == 0
                || outputStatus.Count != count
                || outputStatus.CaptureEpoch != captureEpoch
                || timestampUs < outputStatus.TimestampUs)
            {
                ResetStates(states);
            }

            outputStatus.IsValid = 0;
            outputStatus.Count = 0;
            outputStatus.TimestampUs = timestampUs;
            outputStatus.FrameCount = frameCount;
            outputStatus.CaptureEpoch = captureEpoch;
            return true;
        }

        private static FilteredLandmarkStateElement PrepareState(
            DynamicBuffer<FilteredLandmarkStateElement> states,
            int index,
            int target,
            int landmarkIndex,
            ulong continuityToken)
        {
            var state = states[index];
            if (state.Target != target || state.Index != landmarkIndex
                || continuityToken == 0 || state.ContinuityToken != continuityToken)
            {
                state.State = default;
                state.Target = target;
                state.Index = landmarkIndex;
                state.ContinuityToken = continuityToken;
            }

            return state;
        }

        private static float3 Filter(
            float x,
            float y,
            float z,
            ref LandmarkFilterState state,
            in OneEuroFilterSettings settings,
            LandmarkFilterTracker tracker,
            long timestampUs)
        {
            var minCutoff = ResolveMinCutoff(in settings, tracker);
            var beta = ResolveBeta(in settings, tracker);
            return OneEuroFilter.Filter(
                new float3(x, y, z),
                ref state,
                settings.Enabled,
                new float3(minCutoff, minCutoff, math.max(settings.ZMinCutoff, 0.0001f)),
                new float3(beta, beta, math.max(settings.ZBeta, 0f)),
                math.max(settings.DerivativeCutoffHz, 0.0001f),
                timestampUs);
        }

        private static float ResolveMinCutoff(in OneEuroFilterSettings settings, LandmarkFilterTracker tracker)
        {
            return math.max(tracker switch
            {
                LandmarkFilterTracker.Face => settings.FaceMinCutoff,
                LandmarkFilterTracker.Pose => settings.PoseMinCutoff,
                _ => settings.HandMinCutoff,
            }, 0.0001f);
        }

        private static float ResolveBeta(in OneEuroFilterSettings settings, LandmarkFilterTracker tracker)
        {
            return math.max(tracker switch
            {
                LandmarkFilterTracker.Face => settings.FaceBeta,
                LandmarkFilterTracker.Pose => settings.PoseBeta,
                _ => settings.HandBeta,
            }, 0f);
        }

        private static DynamicBuffer<LandmarkContinuityElement> ReadContinuity(EntityManager manager, EntityQuery query)
        {
            var entity = query.GetSingletonEntity();
            return manager.HasBuffer<LandmarkContinuityElement>(entity)
                ? manager.GetBuffer<LandmarkContinuityElement>(entity, true)
                : default;
        }

        private static ulong ResolveContinuity(DynamicBuffer<LandmarkContinuityElement> continuity, int target)
        {
            return continuity.IsCreated && target < continuity.Length ? continuity[target].Token : 0UL;
        }

        private static void Invalidate(
            ref FilteredLandmarkStatus status,
            DynamicBuffer<FilteredLandmarkElement> output,
            DynamicBuffer<FilteredWorldLandmarkElement> outputWorld,
            DynamicBuffer<FilteredLandmarkStateElement> states)
        {
            status.IsValid = 0;
            status.Count = 0;
            output.ResizeUninitialized(0);
            outputWorld.ResizeUninitialized(0);
            ResetStates(states);
        }

        private static void ResetStates(DynamicBuffer<FilteredLandmarkStateElement> states)
        {
            for (var i = 0; i < states.Length; i++)
            {
                states[i] = default;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
