using System;
using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 깊이 샘플 싱글턴 생성·초기화. Depth 전용 단일 작성자가 소유한다.
    /// Hand/Pose 싱글턴 소유권을 획득하거나 원본을 초기화하지 않는다.
    /// </summary>
    public static class DepthSamplingSingletonUtil
    {
        public static Entity GetOrCreateSingleton(EntityManager entityManager)
        {
            var singletonQuery = entityManager.CreateEntityQuery(typeof(DepthSampleStatus));
            try
            {
                var entityCount = singletonQuery.CalculateEntityCount();
                if (entityCount == 0)
                {
                    var entity = entityManager.CreateEntity();
                    entityManager.AddComponentData(entity, CreateEmptyStatus());
                    entityManager.AddBuffer<HandDepthSampleElement>(entity);
                    entityManager.AddBuffer<PoseDepthSampleElement>(entity);
                    TrackingWriterOwnershipUtil.EnsureExists(entityManager, entity);
                    return entity;
                }

                if (entityCount == 1)
                {
                    var entity = singletonQuery.GetSingletonEntity();
                    if (!entityManager.HasBuffer<HandDepthSampleElement>(entity))
                    {
                        entityManager.AddBuffer<HandDepthSampleElement>(entity);
                    }

                    if (!entityManager.HasBuffer<PoseDepthSampleElement>(entity))
                    {
                        entityManager.AddBuffer<PoseDepthSampleElement>(entity);
                    }

                    TrackingWriterOwnershipUtil.EnsureExists(entityManager, entity);
                    return entity;
                }

                throw new InvalidOperationException("[MPUD ECS] Expected 0 or 1 DepthSampleStatus singleton entity.");
            }
            finally
            {
                singletonQuery.Dispose();
            }
        }

        public static void WriteResetEmptyState(EntityManager entityManager, Entity entity)
        {
            entityManager.SetComponentData(entity, CreateEmptyStatus());
        }

        private static DepthSampleStatus CreateEmptyStatus()
        {
            return new DepthSampleStatus
            {
                IsValid = false,
                HandCount = 0,
                PoseCount = 0,
                HandValidMask = 0,
                PoseValid = 0,
            };
        }
    }
}
