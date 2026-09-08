using System;
using System.IO;
using MediaPipeUnityDots.Runtime.Ecs;
using MediaPipeUnityDots.Runtime.Interop;
using MediaPipeUnityDots.Runtime.Tracking;
using Unity.Entities;
using UnityEngine;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// 공유 웹캠 픽셀을 얼굴 트래커에 제출하고 결과를 ECS에 푸시하는 런타임 프로바이더.
    /// WebcamFrameProvider.Update 이후에 동작하므로 LateUpdate에서 소비한다.
    /// </summary>
    public sealed class FaceFrameProvider : MonoBehaviour, IPointSource
    {
        private const int LandmarkCapacity = 478;

        [SerializeField]
        private WebcamFrameProvider _webcamSource;
        [SerializeField]
        private int _logIntervalFrames = 60;
        [SerializeField]
        private int _numFaces = 1;
        [SerializeField]
        private float _minDetectionConfidence = 0.5f;
        [SerializeField]
        private float _minTrackingConfidence = 0.5f;

        /// <summary>
        /// 추적할 얼굴 수. FaceTrackingService와 포인트 스포너가 공유한다.
        /// </summary>
        public int NumFaces => Mathf.Clamp(_numFaces, 1, MpudFaceResult.MaxFaces);
        int IPointSource.MaxTargets => NumFaces;

        private FaceTrackingService _service;
        private MpudNormalizedLandmark[] _landmarkCopyBuffer;
        private float[] _blendshapeCopyBuffer;
        private World _ecsWorld;
        private Entity _singletonEntity;
        private long _submitCount;
        private long _lastCopiedTimestamp;
        private bool _hasLoggedOwnershipConflict;
        private bool _pendingResetSnapshotPush;

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (_webcamSource == null)
            {
                MpudLog.Error("[MPUD] FaceFrameProvider needs WebcamFrameProvider.");
                enabled = false;
                return;
            }

            try
            {
                InitializeResources();
            }
            catch (Exception exception)
            {
                MpudLog.Error($"[MPUD] Failed to initialize face provider: {exception}");
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

            if (_pendingResetSnapshotPush && TryGetEntityManager(out var resetEntityManager))
            {
                if (EnsureFaceOwnership(resetEntityManager))
                {
                    FaceTrackingSingletonUtil.WriteResetEmptyState(resetEntityManager, _singletonEntity);
                }

                _pendingResetSnapshotPush = false;
            }

            if (_service.TryTakeCompleted())
            {
                if (MpudLog.Enabled && _logIntervalFrames > 0 && _submitCount % _logIntervalFrames == 0)
                {
                    MpudLog.Log(
                        $"[MPUD] Face frame #{_service.LatestFrameCount} | Valid={_service.LatestIsValid} | Faces={_service.LatestFaceCount} | Landmarks={_service.LatestLandmarkCount} | ts={_service.LatestTimestampUs}");
                }

                if (TryGetEntityManager(out var entityManager)
                    && _service.LatestTimestampUs > _lastCopiedTimestamp
                    && EnsureFaceOwnership(entityManager))
                {
                    PushLatestSnapshotToEcs(entityManager);
                    _lastCopiedTimestamp = _service.LatestTimestampUs;
                }
            }

            // 완료 수신은 픽셀·해상도 가드보다 먼저 수행한다.
            var pixels = _webcamSource.LatestPixels;
            var width = _webcamSource.LatestPixelWidth;
            var height = _webcamSource.LatestPixelHeight;
            if (pixels == null || width <= 0 || height <= 0)
            {
                return;
            }

            var stamp = new CaptureStamp(
                _webcamSource.LatestCaptureId,
                _webcamSource.LatestCaptureTimestampUs,
                _webcamSource.CaptureEpoch);
            if (stamp.CaptureId == 0)
            {
                return;
            }

            if (_service.TrySubmit(pixels, width, height, _webcamSource.LatestFlipVertically, stamp))
            {
                _submitCount++;
            }
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
                "face_landmarker.task");
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException("face_landmarker.task was not found.", modelPath);
            }

            _service = new FaceTrackingService(
                modelPath,
                NumFaces,
                _minDetectionConfidence,
                _minTrackingConfidence);
            _landmarkCopyBuffer = new MpudNormalizedLandmark[LandmarkCapacity];
            _blendshapeCopyBuffer = new float[MpudFaceResult.BlendshapesPerFace];
            _ecsWorld = null;
            _singletonEntity = Entity.Null;
            _submitCount = 0;
            _lastCopiedTimestamp = 0;
            _hasLoggedOwnershipConflict = false;
            _pendingResetSnapshotPush = false;

            TryGetEntityManager(out _);

            MpudLog.Log("[MPUD] Face provider started.");
        }

        private void DisposeResources()
        {
            if (_service != null)
            {
                _service.Dispose();
                _service = null;
            }

            _landmarkCopyBuffer = null;
            _blendshapeCopyBuffer = null;
            _ecsWorld = null;
            _singletonEntity = Entity.Null;
            _submitCount = 0;
            _lastCopiedTimestamp = 0;
            _pendingResetSnapshotPush = false;
        }

        public void ResetTracker()
        {
            if (_service == null)
            {
                return;
            }

            _service.ResetTracker();
            _webcamSource.BumpCaptureEpoch();
            _pendingResetSnapshotPush = true;
            _lastCopiedTimestamp = 0;
        }


        private void PushLatestSnapshotToEcs(EntityManager entityManager)
        {
            if (_service.LatestIsValid)
            {
                WriteValidPolledState(entityManager);
                return;
            }

            FaceTrackingSingletonUtil.WriteInvalidPolledState(
                entityManager,
                _singletonEntity,
                _service.LatestTimestampUs,
                _service.LatestFrameCount);
        }

        private void WriteValidPolledState(EntityManager entityManager)
        {
            var faceCount = _service.LatestFaceCount;

            entityManager.SetComponentData(
                _singletonEntity,
                new FaceTrackingStatus
                {
                    IsValid = true,
                    FaceCount = faceCount,
                    LandmarkCount = _service.LatestLandmarkCount,
                    TimestampUs = _service.LatestTimestampUs,
                    FrameCount = _service.LatestFrameCount,
                });

            var landmarks = entityManager.GetBuffer<FaceLandmarkElement>(_singletonEntity);
            if (landmarks.Length != faceCount * LandmarkCapacity)
            {
                landmarks.ResizeUninitialized(faceCount * LandmarkCapacity);
            }

            for (var f = 0; f < faceCount; f++)
            {
                var copiedCount = _service.CopyLatestFaceLandmarksTo(f, _landmarkCopyBuffer);
                for (var i = 0; i < LandmarkCapacity; i++)
                {
                    var bufferIndex = f * LandmarkCapacity + i;
                    if (i < copiedCount)
                    {
                        var source = _landmarkCopyBuffer[i];
                        landmarks[bufferIndex] = new FaceLandmarkElement
                        {
                            X = source.x,
                            Y = source.y,
                            Z = source.z,
                            FaceIndex = f,
                        };
                    }
                    else
                    {
                        landmarks[bufferIndex] = new FaceLandmarkElement { FaceIndex = -1 };
                    }
                }
            }
            var blendshapeCapacity = MpudFaceResult.BlendshapesPerFace;
            var blendshapes = entityManager.GetBuffer<FaceBlendshapeElement>(_singletonEntity);
            if (blendshapes.Length != faceCount * blendshapeCapacity)
            {
                blendshapes.ResizeUninitialized(faceCount * blendshapeCapacity);
            }

            for (var f = 0; f < faceCount; f++)
            {
                var copiedCount = _service.CopyLatestFaceBlendshapesTo(f, _blendshapeCopyBuffer);
                for (var i = 0; i < blendshapeCapacity; i++)
                {
                    var bufferIndex = f * blendshapeCapacity + i;
                    if (i < copiedCount)
                    {
                        blendshapes[bufferIndex] = new FaceBlendshapeElement
                        {
                            Score = _blendshapeCopyBuffer[i],
                            FaceIndex = f,
                            BlendshapeIndex = i,
                        };
                    }
                    else
                    {
                        blendshapes[bufferIndex] = new FaceBlendshapeElement { FaceIndex = -1 };
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
                _singletonEntity = FaceTrackingSingletonUtil.GetOrCreateSingleton(defaultWorld.EntityManager);
                _hasLoggedOwnershipConflict = false;
            }

            entityManager = defaultWorld.EntityManager;
            return true;
        }

        private void WriteResetStateIfPossible()
        {
            if (_ecsWorld is { IsCreated: true } && _singletonEntity != Entity.Null
                && _ecsWorld.EntityManager.Exists(_singletonEntity))
            {
                var owner = OwnerRaw();
                if (!TrackingWriterOwnershipUtil.IsOwner(_ecsWorld.EntityManager, _singletonEntity, owner))
                {
                    return;
                }

                FaceTrackingSingletonUtil.WriteResetEmptyState(_ecsWorld.EntityManager, _singletonEntity);
                TrackingWriterOwnershipUtil.Release(_ecsWorld.EntityManager, _singletonEntity, owner);
            }
        }

        // 싱글턴 단일 작성자 보장. 다른 프로바이더 소유면 이번 프레임 기록을 건너뛴다.
        private bool EnsureFaceOwnership(EntityManager entityManager)
        {
            var owner = OwnerRaw();
            if (TrackingWriterOwnershipUtil.IsOwner(entityManager, _singletonEntity, owner))
            {
                return true;
            }

            if (TrackingWriterOwnershipUtil.TryAcquire(entityManager, _singletonEntity, owner))
            {
                _hasLoggedOwnershipConflict = false;
                return true;
            }

            if (!_hasLoggedOwnershipConflict)
            {
                _hasLoggedOwnershipConflict = true;
                MpudLog.Warning("[MPUD] Face 싱글턴이 다른 프로바이더 소유라 기록을 건너뛴다.");
            }

            return false;
        }

        private ulong OwnerRaw() => EntityId.ToULong(GetEntityId());
    }
}
