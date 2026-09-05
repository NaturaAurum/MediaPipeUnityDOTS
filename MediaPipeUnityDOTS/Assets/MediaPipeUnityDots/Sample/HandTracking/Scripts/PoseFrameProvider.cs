using System;
using System.IO;
using MediaPipeUnityDots.Runtime.Ecs;
using MediaPipeUnityDots.Runtime.Interop;
using MediaPipeUnityDots.Runtime.Tracking;
using Unity.Entities;
using UnityEngine;

namespace MediaPipeUnityDots.Sample.HandTracking.Scripts
{
    /// <summary>
    /// 공유 웹캠 픽셀을 포즈 트래커에 제출하고 결과를 ECS에 푸시하는 샘플 프로바이더.
    /// WebcamFrameProvider.Update 이후에 동작하므로 LateUpdate에서 소비한다.
    /// </summary>
    public sealed class PoseFrameProvider : MonoBehaviour
    {
        private const int LandmarkCapacity = 33;

        [SerializeField]
        private WebcamFrameProvider _webcamSource;
        [SerializeField]
        private int _numPoses = 1;
        [SerializeField]
        private float _minDetectionConfidence = 0.5f;
        [SerializeField]
        private float _minTrackingConfidence = 0.5f;
        [SerializeField]
        private int _logIntervalFrames = 60;

        /// <summary>
        /// 추적할 포즈 수. PoseTrackingService와 포인트 스포너가 공유한다.
        /// </summary>
        public int NumPoses => Mathf.Clamp(_numPoses, 1, MpudPoseResult.MaxPoses);

        private PoseTrackingService _service;
        private MpudNormalizedLandmark[] _landmarkCopyBuffer;
        private World _ecsWorld;
        private Entity _singletonEntity;
        private long _submitCount;
        private long _lastCopiedTimestamp;

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (_webcamSource == null)
            {
                MpudLog.Error("[MPUD] PoseFrameProvider needs WebcamFrameProvider.");
                enabled = false;
                return;
            }

            try
            {
                InitializeResources();
            }
            catch (Exception exception)
            {
                MpudLog.Error($"[MPUD] Failed to initialize pose provider: {exception}");
                DisposeResources();
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (_service == null)
            {
                return;
            }

            var pixels = _webcamSource.LatestPixels;
            var width = _webcamSource.LatestPixelWidth;
            var height = _webcamSource.LatestPixelHeight;
            if (pixels == null || width <= 0 || height <= 0)
            {
                return;
            }

            var previousFrameCount = _service.LatestFrameCount;
            _service.SubmitAndPoll(pixels, width, height, _webcamSource.LatestFlipVertically);

            if (_service.LatestFrameCount == previousFrameCount)
            {
                return;
            }

            _submitCount++;
            if (MpudLog.Enabled && _logIntervalFrames > 0 && _submitCount % _logIntervalFrames == 0)
            {
                MpudLog.Log(
                    $"[MPUD] Pose frame #{_service.LatestFrameCount} | Valid={_service.LatestIsValid} | Poses={_service.LatestPoseCount} | Landmarks={_service.LatestLandmarkCount} | ts={_service.LatestTimestampUs}");
            }

            if (!TryGetEntityManager(out var entityManager))
            {
                return;
            }

            if (_service.LatestTimestampUs <= _lastCopiedTimestamp)
            {
                return;
            }

            PushLatestSnapshotToEcs(entityManager);
            _lastCopiedTimestamp = _service.LatestTimestampUs;
        }

        private void OnDisable()
        {
            WriteResetStateIfPossible();
            DisposeResources();
        }

        private void OnDestroy() => DisposeResources();

        private void InitializeResources()
        {
            if (_service != null)
            {
                return;
            }

            var modelPath = Path.Combine(
                Application.streamingAssetsPath,
                "MediaPipe",
                "Models",
                "pose_landmarker_full.task");
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException("pose_landmarker_full.task was not found.", modelPath);
            }

            _service = new PoseTrackingService(modelPath, NumPoses, _minDetectionConfidence, _minTrackingConfidence);
            _landmarkCopyBuffer = new MpudNormalizedLandmark[LandmarkCapacity];
            _ecsWorld = null;
            _singletonEntity = Entity.Null;
            _submitCount = 0;
            _lastCopiedTimestamp = 0;

            TryGetEntityManager(out _);

            MpudLog.Log("[MPUD] Pose provider started.");
        }

        private void DisposeResources()
        {
            if (_service != null)
            {
                _service.Dispose();
                _service = null;
            }

            _landmarkCopyBuffer = null;
            _ecsWorld = null;
            _singletonEntity = Entity.Null;
        }

        private void PushLatestSnapshotToEcs(EntityManager entityManager)
        {
            if (_service.LatestIsValid)
            {
                WriteValidPolledState(entityManager);
                return;
            }

            PoseTrackingSingletonUtil.WriteInvalidPolledState(
                entityManager,
                _singletonEntity,
                _service.LatestTimestampUs,
                _service.LatestFrameCount);
        }

        private void WriteValidPolledState(EntityManager entityManager)
        {
            var poseCount = _service.LatestPoseCount;

            entityManager.SetComponentData(
                _singletonEntity,
                new PoseTrackingStatus
                {
                    IsValid = true,
                    PoseCount = poseCount,
                    LandmarkCount = _service.LatestLandmarkCount,
                    TimestampUs = _service.LatestTimestampUs,
                    FrameCount = _service.LatestFrameCount,
                });

            var landmarks = entityManager.GetBuffer<PoseLandmarkElement>(_singletonEntity);
            if (landmarks.Length != poseCount * LandmarkCapacity)
            {
                landmarks.ResizeUninitialized(poseCount * LandmarkCapacity);
            }

            for (var p = 0; p < poseCount; p++)
            {
                var copiedCount = _service.CopyLatestPoseLandmarksTo(p, _landmarkCopyBuffer);
                for (var i = 0; i < LandmarkCapacity; i++)
                {
                    var bufferIndex = p * LandmarkCapacity + i;
                    if (i < copiedCount)
                    {
                        var source = _landmarkCopyBuffer[i];
                        landmarks[bufferIndex] = new PoseLandmarkElement
                        {
                            X = source.x,
                            Y = source.y,
                            Z = source.z,
                            PoseIndex = p,
                        };
                    }
                    else
                    {
                        landmarks[bufferIndex] = new PoseLandmarkElement { PoseIndex = -1 };
                    }
                }
            }

            var world = entityManager.GetBuffer<PoseWorldLandmarkElement>(_singletonEntity);
            if (world.Length != poseCount * LandmarkCapacity)
            {
                world.ResizeUninitialized(poseCount * LandmarkCapacity);
            }

            for (var p = 0; p < poseCount; p++)
            {
                var worldCount = _service.CopyLatestPoseWorldLandmarksTo(p, _landmarkCopyBuffer);
                for (var i = 0; i < LandmarkCapacity; i++)
                {
                    var bufferIndex = p * LandmarkCapacity + i;
                    if (i < worldCount)
                    {
                        var source = _landmarkCopyBuffer[i];
                        world[bufferIndex] = new PoseWorldLandmarkElement
                        {
                            X = source.x,
                            Y = source.y,
                            Z = source.z,
                            Visibility = source.visibility,
                            PoseIndex = p,
                        };
                    }
                    else
                    {
                        world[bufferIndex] = new PoseWorldLandmarkElement { PoseIndex = -1 };
                    }
                }
            }
        }

        private bool TryGetEntityManager(out EntityManager entityManager)
        {
            entityManager = default;

            var defaultWorld = World.DefaultGameObjectInjectionWorld;
            if (defaultWorld is not { IsCreated: true })
            {
                _ecsWorld = null;
                _singletonEntity = Entity.Null;
                return false;
            }

            if (_ecsWorld == null || _ecsWorld != defaultWorld || !_ecsWorld.IsCreated)
            {
                _ecsWorld = defaultWorld;
                _singletonEntity = Entity.Null;
            }

            if (_singletonEntity == Entity.Null || !defaultWorld.EntityManager.Exists(_singletonEntity))
            {
                _singletonEntity = PoseTrackingSingletonUtil.GetOrCreateSingleton(defaultWorld.EntityManager);
            }

            entityManager = defaultWorld.EntityManager;
            return true;
        }

        private void WriteResetStateIfPossible()
        {
            if (_ecsWorld is { IsCreated: true } && _singletonEntity != Entity.Null
                && _ecsWorld.EntityManager.Exists(_singletonEntity))
            {
                PoseTrackingSingletonUtil.WriteResetEmptyState(_ecsWorld.EntityManager, _singletonEntity);
            }
        }
    }
}
