using MediaPipeUnityDots.Runtime.Tracking;
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

        public static void WriteInvalidPolledState(
            EntityManager entityManager,
            Entity entity,
            long timestampUs,
            long frameCount,
            long captureId = 0L,
            long captureTimestampUs = 0L,
            long captureEpoch = 0L,
            TrackingResultStatus resultStatus = TrackingResultStatus.NoDetection)
        {
            var status = CreateEmptyStatus(timestampUs, frameCount);
            status.CaptureId = captureId;
            status.CaptureTimestampUs = captureTimestampUs;
            status.CaptureEpoch = captureEpoch;
            status.ResultStatus = resultStatus;
            entityManager.SetComponentData(entity, status);
            entityManager.GetBuffer<FaceLandmarkElement>(entity).Clear();
            entityManager.GetBuffer<FaceBlendshapeElement>(entity).Clear();
        }

        public static void WriteResetEmptyState(EntityManager entityManager, Entity entity)
        {
            var status = CreateEmptyStatus(0L, 0L);
            status.ResultStatus = TrackingResultStatus.Reset;
            entityManager.SetComponentData(entity, status);
            entityManager.GetBuffer<FaceLandmarkElement>(entity).Clear();
            entityManager.GetBuffer<FaceBlendshapeElement>(entity).Clear();
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
                ResultStatus = TrackingResultStatus.Waiting,
            };
        }
    }
}
