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
                    entityManager.AddBuffer<FaceBlendshapeElement>(entity);
                    TrackingWriterOwnershipUtil.EnsureExists(entityManager, entity);
                    return entity;
                }

                if (entityCount == 1)
                {
                    var entity = singletonQuery.GetSingletonEntity();
                    if (!entityManager.HasBuffer<FaceLandmarkElement>(entity))
                    {
                        entityManager.AddBuffer<FaceLandmarkElement>(entity);
                    }

                    if (!entityManager.HasBuffer<FaceBlendshapeElement>(entity))
                    {
                        entityManager.AddBuffer<FaceBlendshapeElement>(entity);
                    }
                    TrackingWriterOwnershipUtil.EnsureExists(entityManager, entity);

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
        }

        public static void WriteResetEmptyState(EntityManager entityManager, Entity entity)
        {
            entityManager.SetComponentData(entity, CreateEmptyStatus(0L, 0L));
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
