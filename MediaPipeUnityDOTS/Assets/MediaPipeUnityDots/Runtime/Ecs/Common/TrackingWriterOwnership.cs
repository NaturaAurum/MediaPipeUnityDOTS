using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 스트림 싱글턴의 단일 작성자 표시. UnityEngine 타입 없이 ulong 핸들만 저장해 unmanaged을 유지한다.
    /// </summary>
    public struct TrackingWriterOwnership : IComponentData
    {
        public ulong Owner;
    }

    /// <summary>
    /// 소유권 획득/확인/해제. 처음 쓰는 작성자가 소유하고, 다른 작성자의 획득은 실패한다.
    /// </summary>
    public static class TrackingWriterOwnershipUtil
    {
        public const ulong Unowned = 0;

        public static void EnsureExists(EntityManager entityManager, Entity entity)
        {
            if (!entityManager.HasComponent<TrackingWriterOwnership>(entity))
            {
                entityManager.AddComponentData(entity, new TrackingWriterOwnership { Owner = Unowned });
            }
        }

        public static bool TryAcquire(EntityManager entityManager, Entity entity, ulong owner)
        {
            if (owner == Unowned)
            {
                return false;
            }

            EnsureExists(entityManager, entity);
            var current = entityManager.GetComponentData<TrackingWriterOwnership>(entity);
            if (current.Owner != Unowned && current.Owner != owner)
            {
                return false;
            }

            entityManager.SetComponentData(entity, new TrackingWriterOwnership { Owner = owner });
            return true;
        }

        public static bool IsOwner(EntityManager entityManager, Entity entity, ulong owner)
        {
            return owner != Unowned
                && entityManager.HasComponent<TrackingWriterOwnership>(entity)
                && entityManager.GetComponentData<TrackingWriterOwnership>(entity).Owner == owner;
        }

        public static void Release(EntityManager entityManager, Entity entity, ulong owner)
        {
            if (IsOwner(entityManager, entity, owner))
            {
                entityManager.SetComponentData(entity, new TrackingWriterOwnership { Owner = Unowned });
            }
        }
    }
}
