using System;
using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    public static class FaceTrackingSingletonUtil
    {
        public static Entity GetOrCreateSingleton(EntityManager entityManager)
        {
            var singletonQuery = entityManager.CreateEntityQuery(typeof(FaceTrackingStatus));
            try
            {
                var entityCount = singletonQuery.CalculateEntityCount();
                if (entityCount == 0)
                {
                    var entity = entityManager.CreateEntity();
                    entityManager.AddComponentData(entity, CreateEmptyStatus(0L, 0L));
                    entityManager.AddBuffer<FaceLandmarkElement>(entity);
                    return entity;
                }

                if (entityCount == 1)
                {
                    var entity = singletonQuery.GetSingletonEntity();
                    if (!entityManager.HasBuffer<FaceLandmarkElement>(entity))
                    {
                        entityManager.AddBuffer<FaceLandmarkElement>(entity);
                    }

                    return entity;
                }

                throw new InvalidOperationException("[MPUD ECS] Expected 0 or 1 FaceTrackingStatus singleton entity.");
            }
            finally
            {
                singletonQuery.Dispose();
            }
        }

        public static void WriteInvalidPolledState(EntityManager entityManager, Entity entity, long timestampUs, long frameCount)
        {
            entityManager.SetComponentData(entity, CreateEmptyStatus(timestampUs, frameCount));
            entityManager.GetBuffer<FaceLandmarkElement>(entity).Clear();
        }

        public static void WriteResetEmptyState(EntityManager entityManager, Entity entity)
        {
            entityManager.SetComponentData(entity, CreateEmptyStatus(0L, 0L));
            entityManager.GetBuffer<FaceLandmarkElement>(entity).Clear();
        }

        private static FaceTrackingStatus CreateEmptyStatus(long timestampUs, long frameCount)
        {
            return new FaceTrackingStatus
            {
                IsValid = false,
                FaceCount = 0,
                LandmarkCount = 0,
                TimestampUs = timestampUs,
                FrameCount = frameCount,
            };
        }
    }
}
