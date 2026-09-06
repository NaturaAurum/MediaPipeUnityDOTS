using System;
using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    public static class HandTrackingSingletonUtil
    {
        public static Entity GetOrCreateSingleton(EntityManager entityManager)
        {
            var singletonQuery = entityManager.CreateEntityQuery(typeof(HandTrackingStatus));
            try
            {
                var entityCount = singletonQuery.CalculateEntityCount();
                if (entityCount == 0)
                {
                    var entity = entityManager.CreateEntity();
                    entityManager.AddComponentData(entity, CreateEmptyStatus(0L, 0L));
                    entityManager.AddBuffer<LandmarkElement>(entity);
                    entityManager.AddBuffer<WorldLandmarkElement>(entity);
                    return entity;
                }

                if (entityCount == 1)
                {
                    var entity = singletonQuery.GetSingletonEntity();
                    if (!entityManager.HasBuffer<LandmarkElement>(entity))
                    {
                        entityManager.AddBuffer<LandmarkElement>(entity);
                    }

                    if (!entityManager.HasBuffer<WorldLandmarkElement>(entity))
                    {
                        entityManager.AddBuffer<WorldLandmarkElement>(entity);
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
        }

        public static void WriteResetEmptyState(EntityManager entityManager, Entity entity)
        {
            entityManager.SetComponentData(entity, CreateEmptyStatus(0L, 0L));
        }

        private static HandTrackingStatus CreateEmptyStatus(long timestampUs, long frameCount)
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
