using Unity.Entities;
using UnityEngine;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    public partial struct HandTrackingReadValidationSystem : ISystem
    {
        const int LogIntervalFrames = 60;

        long _lastLoggedTimestampUs;
        long _lastLoggedFrameCount;
        bool _hasLoggedState;
        bool _hasReportedState;
        bool _lastReportedIsValid;
        int _lastReportedHandedness;
        int _lastReportedLandmarkCount;
        int _lastReportedBufferLength;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<HandTrackingStatus>();
        }

        public void OnUpdate(ref SystemState state)
        {
            int singletonCount = 0;
            Entity singletonEntity = Entity.Null;
            HandTrackingStatus status = default;

            foreach ((RefRO<HandTrackingStatus> currentStatus, Entity entity) in SystemAPI.Query<RefRO<HandTrackingStatus>>().WithEntityAccess())
            {
                singletonCount++;
                if (singletonCount > 1)
                {
                    Debug.LogError("[MPUD ECS] HandTrackingReadValidationSystem found multiple HandTrackingStatus entities.");
                    return;
                }

                singletonEntity = entity;
                status = currentStatus.ValueRO;
            }

            if (singletonCount != 1)
            {
                return;
            }

            if (!SystemAPI.HasBuffer<LandmarkElement>(singletonEntity))
            {
                Debug.LogError("[MPUD ECS] HandTrackingReadValidationSystem could not find LandmarkElement buffer.");
                return;
            }

            if (_hasLoggedState && status.TimestampUs == _lastLoggedTimestampUs && status.FrameCount == _lastLoggedFrameCount)
            {
                return;
            }

            DynamicBuffer<LandmarkElement> landmarks = SystemAPI.GetBuffer<LandmarkElement>(singletonEntity);

            _hasLoggedState = true;
            _lastLoggedTimestampUs = status.TimestampUs;
            _lastLoggedFrameCount = status.FrameCount;

            if (!ShouldLogState(status, landmarks.Length))
            {
                return;
            }

            _hasReportedState = true;
            _lastReportedIsValid = status.IsValid;
            _lastReportedHandedness = status.Handedness;
            _lastReportedLandmarkCount = status.LandmarkCount;
            _lastReportedBufferLength = landmarks.Length;

            Debug.Log(
                $"[MPUD ECS] Frame #{status.FrameCount} | Valid={status.IsValid} | Handedness={status.Handedness} | Score={status.Score:F2} | Landmarks={status.LandmarkCount} | BufferLength={landmarks.Length} | ts={status.TimestampUs}");
        }

        bool ShouldLogState(in HandTrackingStatus status, int bufferLength)
        {
            if (!_hasReportedState)
            {
                return true;
            }

            if (status.IsValid != _lastReportedIsValid
                || status.Handedness != _lastReportedHandedness
                || status.LandmarkCount != _lastReportedLandmarkCount
                || bufferLength != _lastReportedBufferLength)
            {
                return true;
            }

            return LogIntervalFrames > 0 && status.FrameCount % LogIntervalFrames == 0;
        }
    }
}
