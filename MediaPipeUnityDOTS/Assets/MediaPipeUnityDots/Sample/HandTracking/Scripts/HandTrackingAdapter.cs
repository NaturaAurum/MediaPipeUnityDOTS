using MediaPipeUnityDots.Runtime.Ecs;
using Unity.Entities;
using UnityEngine;

namespace MediaPipeUnityDots.Sample.HandTracking.Scripts
{
    /// <summary>
    /// ECS에서 hand tracking 상태를 읽어 caller-owned DTO에 복사하는 유일한 reader.
    /// visualizer와 UI presenter는 이 adapter가 채운 DTO만 사용하고 EntityManager에 직접 접근하지 않는다.
    /// World/Entity는 캐시하며, 무효화되면 다시 해석한다. steady-state에서 할당하지 않는다.
    /// </summary>
    public sealed class HandTrackingAdapter : MonoBehaviour
    {
        private World _cachedWorld;
        private Entity _cachedEntity;

        /// <summary>
        /// 현재 ECS 상태를 destination에 복사한다. World가 없으면 false.
        /// 싱글턴이 없으면 empty 상태 DTO(false, -1, 0, 0점)를 채우고 true를 반환한다.
        /// </summary>
        public bool TryRead(HandTrackingDto destination)
        {
            if (destination == null)
            {
                return false;
            }

            var world = World.DefaultGameObjectInjectionWorld;
            if (world is not { IsCreated: true })
            {
                return false;
            }

            if (_cachedWorld != world || _cachedEntity == Entity.Null)
            {
                _cachedWorld = world;
                _cachedEntity = Entity.Null;
            }

            var entityManager = world.EntityManager;
            if (_cachedEntity == Entity.Null || !entityManager.Exists(_cachedEntity))
            {
                _cachedEntity = HandTrackingSingletonUtil.GetOrCreateSingleton(entityManager);
            }

            if (!entityManager.HasComponent<HandTrackingStatus>(_cachedEntity)
                || !entityManager.HasBuffer<LandmarkElement>(_cachedEntity))
            {
                WriteEmpty(destination, 0L, 0L);
                return true;
            }

            var status = entityManager.GetComponentData<HandTrackingStatus>(_cachedEntity);
            var buffer = entityManager.GetBuffer<LandmarkElement>(_cachedEntity);

            destination.IsValid = status.IsValid;
            destination.Handedness = status.Handedness;
            destination.Score = status.Score;
            destination.TimestampUs = status.TimestampUs;
            destination.FrameCount = status.FrameCount;

            var count = buffer.Length;
            if (count > HandTrackingDto.LandmarkCapacity)
            {
                count = HandTrackingDto.LandmarkCapacity;
            }

            for (var i = 0; i < count; i++)
            {
                var element = buffer[i];
                destination.Points[i] = new Vector3(element.X, element.Y, element.Z);
            }

            destination.PointCount = count;
            return true;
        }

        private void OnDisable()
        {
            _cachedWorld = null;
            _cachedEntity = Entity.Null;
        }

        private static void WriteEmpty(HandTrackingDto destination, long timestampUs, long frameCount)
        {
            destination.IsValid = false;
            destination.Handedness = -1;
            destination.Score = 0f;
            destination.TimestampUs = timestampUs;
            destination.FrameCount = frameCount;
            destination.PointCount = 0;
        }
    }
}
