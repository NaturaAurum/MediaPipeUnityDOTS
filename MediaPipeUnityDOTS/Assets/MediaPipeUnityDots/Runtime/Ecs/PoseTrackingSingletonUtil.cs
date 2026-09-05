using System;
using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    public static class PoseTrackingSingletonUtil
    {
        public static Entity GetOrCreateSingleton(EntityManager entityManager)
        {
            var singletonQuery = entityManager.CreateEntityQuery(typeof(PoseTrackingStatus));
            try
            {
                var entityCount = singletonQuery.CalculateEntityCount();
                if (entityCount == 0)
                {
                    var entity = entityManager.CreateEntity();
                    entityManager.AddComponentData(entity, CreateEmptyStatus(0L, 0L));
                    entityManager.AddBuffer<PoseLandmarkElement>(entity);
                    return entity;
                }

                if (entityCount == 1)
                {
                    var entity = singletonQuery.GetSingletonEntity();
                    if (!entityManager.HasBuffer<PoseLandmarkElement>(entity))
                    {
                        entityManager.AddBuffer<PoseLandmarkElement>(entity);
                    }

                    return entity;
                }

                throw new InvalidOperationException("[MPUD ECS] Expected 0 or 1 PoseTrackingStatus singleton entity.");
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

        private static PoseTrackingStatus CreateEmptyStatus(long timestampUs, long frameCount)
        {
            return new PoseTrackingStatus
            {
                IsValid = false,
                PoseCount = 0,
                LandmarkCount = 0,
                TimestampUs = timestampUs,
                FrameCount = frameCount,
            };
        }
    }
}
