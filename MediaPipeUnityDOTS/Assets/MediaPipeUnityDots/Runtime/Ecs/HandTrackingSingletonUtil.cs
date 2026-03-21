using System;
using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    public static class HandTrackingSingletonUtil
    {
        public static Entity GetOrCreateSingleton(EntityManager entityManager)
        {
            EntityQuery singletonQuery = entityManager.CreateEntityQuery(typeof(HandTrackingStatus));
            try
            {
                int entityCount = singletonQuery.CalculateEntityCount();
                if (entityCount == 0)
                {
                    Entity entity = entityManager.CreateEntity();
                    entityManager.AddComponentData(entity, CreateEmptyStatus(0L, 0L));
                    entityManager.AddBuffer<LandmarkElement>(entity);
                    return entity;
                }

                if (entityCount == 1)
                {
                    Entity entity = singletonQuery.GetSingletonEntity();
                    if (!entityManager.HasBuffer<LandmarkElement>(entity))
                    {
                        entityManager.AddBuffer<LandmarkElement>(entity);
                    }

                    return entity;
                }

                throw new InvalidOperationException("[MPUD ECS] Expected 0 or 1 HandTrackingStatus singleton entity.");
            }
            finally
            {
                singletonQuery.Dispose();
            }
        }

        public static void WriteInvalidPolledState(EntityManager entityManager, Entity entity, long timestampUs, long frameCount)
        {
            entityManager.SetComponentData(entity, CreateEmptyStatus(timestampUs, frameCount));
            entityManager.GetBuffer<LandmarkElement>(entity).Clear();
        }

        public static void WriteResetEmptyState(EntityManager entityManager, Entity entity)
        {
            entityManager.SetComponentData(entity, CreateEmptyStatus(0L, 0L));
            entityManager.GetBuffer<LandmarkElement>(entity).Clear();
        }

        static HandTrackingStatus CreateEmptyStatus(long timestampUs, long frameCount)
        {
            return new HandTrackingStatus
            {
                IsValid = false,
                Handedness = -1,
                Score = 0f,
                LandmarkCount = 0,
                TimestampUs = timestampUs,
                FrameCount = frameCount,
            };
        }
    }
}
