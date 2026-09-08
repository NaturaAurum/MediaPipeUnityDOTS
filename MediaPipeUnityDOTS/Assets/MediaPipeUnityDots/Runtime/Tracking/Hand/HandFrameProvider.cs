using System;
using System.IO;
using MediaPipeUnityDots.Runtime.Ecs;
using MediaPipeUnityDots.Runtime.Interop;
using Unity.Entities;
using UnityEngine;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// 공유 웹캠 픽셀을 손 트래커에 제출하고 결과를 ECS에 푸시하는 런타임 프로바이더.
    /// WebcamFrameProvider.Update 이후에 동작하므로 LateUpdate에서 소비한다.
    /// </summary>
    public sealed class HandFrameProvider : MonoBehaviour, IPointSource
    {
        private const int LandmarkCapacity = 21;

        [SerializeField]
        private WebcamFrameProvider _webcamSource;
        [SerializeField]
        private int _numHands = 2;
        [SerializeField]
        private int _logIntervalFrames = 60;

        /// <summary>
        /// 추적할 손 수. HandTrackingService와 포인트 스포너가 공유한다.
        /// </summary>
        public int NumHands => Mathf.Clamp(_numHands, 1, MpudHandResult.MaxHands);
        int IPointSource.MaxTargets => NumHands;

        private HandTrackingService _service;
        private MpudNormalizedLandmark[] _landmarkCopyBuffer;
        private World _ecsWorld;
        private Entity _singletonEntity;
        private bool _hasLoggedFrameSummary;
        private bool _lastLoggedFrameIsValid;
        private int _lastLoggedFrameHandedness;
        private int _lastLoggedFrameLandmarkCount;
        private bool _pendingResetSnapshotPush;
        private bool _hasLoggedOwnershipConflict;
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
                MpudLog.Error("[MPUD] HandFrameProvider needs WebcamFrameProvider.");
                enabled = false;
                return;
            }

            try
            {
                InitializeResources();
            }
            catch (Exception exception)
            {
                MpudLog.Error($"[MPUD] Failed to initialize hand provider: {exception}");
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
                if (EnsureHandOwnership(resetEntityManager))
                {
                    HandTrackingSingletonUtil.WriteResetEmptyState(resetEntityManager, _singletonEntity);
                }

                _pendingResetSnapshotPush = false;
            }


            if (_service.TryTakeCompleted())
            {
                if (ShouldLogFrameSummary())
                {
                    _hasLoggedFrameSummary = true;
                    _lastLoggedFrameIsValid = _service.LatestIsValid;
                    _lastLoggedFrameHandedness = _service.LatestHandedness;
                    _lastLoggedFrameLandmarkCount = _service.LatestLandmarkCount;

                    MpudLog.Log(
                        $"[MPUD] Frame #{_service.LatestFrameCount} | Valid={_service.LatestIsValid} | Hands={_service.LatestHandCount} | Handedness={_service.LatestHandedness} | Score={_service.LatestScore:F2} | Landmarks={_service.LatestLandmarkCount} | ts={_service.LatestTimestampUs}");
                }

                if (TryGetEntityManager(out var entityManager)
                    && _service.LatestTimestampUs > _lastCopiedTimestamp
                    && EnsureHandOwnership(entityManager))
                {
                    PushLatestSnapshotToEcs(entityManager);
                    _lastCopiedTimestamp = _service.LatestTimestampUs;
                }
            }
            var pixels = _webcamSource.LatestPixels;
            var width = _webcamSource.LatestPixelWidth;
            var height = _webcamSource.LatestPixelHeight;
            if (pixels == null || width <= 0 || height <= 0)
            {
                return;
            }


            // 수신한 완료를 먼저 비워야 단일 슬롯 결과가 덮어쓰이지 않는다.
            var stamp = new CaptureStamp(_webcamSource.LatestCaptureId, _webcamSource.LatestCaptureTimestampUs, _webcamSource.CaptureEpoch);
            if (_service.TrySubmit(pixels, width, height, _webcamSource.LatestFlipVertically, stamp))
            {
                _submitCount++;
                if (ShouldLogSubmit())
                {
                    MpudLog.Log($"[MPUD] Submit #{_submitCount}, ts={_service.LatestTimestampUs}");
                }
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
                "hand_landmarker.task");
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException("hand_landmarker.task was not found.", modelPath);
            }

            _service = new HandTrackingService(modelPath, _numHands);
            _landmarkCopyBuffer = new MpudNormalizedLandmark[LandmarkCapacity];
            _ecsWorld = null;
            _singletonEntity = Entity.Null;
            _hasLoggedFrameSummary = false;
            _lastLoggedFrameIsValid = false;
            _lastLoggedFrameHandedness = -1;
            _lastLoggedFrameLandmarkCount = 0;
            _pendingResetSnapshotPush = false;
            _submitCount = 0;
            _lastCopiedTimestamp = 0;

            TryGetEntityManager(out _);

            MpudLog.Log("[MPUD] Hand provider started.");
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
            _hasLoggedFrameSummary = false;
            _lastLoggedFrameIsValid = false;
            _lastLoggedFrameHandedness = -1;
            _lastLoggedFrameLandmarkCount = 0;
            _pendingResetSnapshotPush = false;
            _submitCount = 0;
            _lastCopiedTimestamp = 0;
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
            // reset-empty 상태는 ts=0을 유지해야 다음 poll 결과가 dedupe를 통과한다.
            _lastCopiedTimestamp = 0;
        }

        private void PushLatestSnapshotToEcs(EntityManager entityManager)
        {
            if (_service.LatestIsValid)
            {
                WriteValidPolledState(entityManager);
                return;
            }

            HandTrackingSingletonUtil.WriteInvalidPolledState(
                entityManager,
                _singletonEntity,
                _service.LatestTimestampUs,
                _service.LatestFrameCount,
                _service.LatestCaptureId,
                _service.LatestCaptureTimestampUs,
                _service.LatestCaptureEpoch);
        }

        private void WriteValidPolledState(EntityManager entityManager)
        {
            var handCount = _service.LatestHandCount;

            var status = new HandTrackingStatus
            {
                IsValid = true,
                HandCount = handCount,
                Handedness = _service.LatestHandedness,
                Score = _service.LatestScore,
                LandmarkCount = _service.LatestLandmarkCount,
                TimestampUs = _service.LatestTimestampUs,
                FrameCount = _service.LatestFrameCount,
                CaptureId = _service.LatestCaptureId,
                CaptureTimestampUs = _service.LatestCaptureTimestampUs,
                CaptureEpoch = _service.LatestCaptureEpoch,
            };
            status.HandednessList.Clear();
            status.ScoreList.Clear();
            for (var h = 0; h < handCount; h++)
            {
                status.HandednessList.Add(_service.GetLatestHandedness(h));
                status.ScoreList.Add(_service.GetLatestScore(h));
            }

            entityManager.SetComponentData(_singletonEntity, status);
            var landmarks = entityManager.GetBuffer<LandmarkElement>(_singletonEntity);
            landmarks.ResizeUninitialized(handCount * LandmarkCapacity);

            for (var h = 0; h < handCount; h++)
            {
                var copiedCount = _service.CopyLatestHandLandmarksTo(h, _landmarkCopyBuffer);
                for (var i = 0; i < LandmarkCapacity; i++)
                {
                    var bufferIndex = h * LandmarkCapacity + i;
                    if (i < copiedCount)
                    {
                        var source = _landmarkCopyBuffer[i];
                        landmarks[bufferIndex] = new LandmarkElement
                        {
                            X = source.x,
                            Y = source.y,
                            Z = source.z,
                            Visibility = source.visibility,
                            Presence = source.presence,
                            HandIndex = h,
                        };
                    }
                    else
                    {
                        landmarks[bufferIndex] = new LandmarkElement { HandIndex = -1 };
                    }
                }
            }

            var world = entityManager.GetBuffer<HandWorldLandmarkElement>(_singletonEntity);
            if (world.Length != handCount * LandmarkCapacity)
            {
                world.ResizeUninitialized(handCount * LandmarkCapacity);
            }

            for (var h = 0; h < handCount; h++)
            {
                var worldCount = _service.CopyLatestHandWorldLandmarksTo(h, _landmarkCopyBuffer);
                for (var i = 0; i < LandmarkCapacity; i++)
                {
                    var bufferIndex = h * LandmarkCapacity + i;
                    if (i < worldCount)
                    {
                        var source = _landmarkCopyBuffer[i];
                        world[bufferIndex] = new HandWorldLandmarkElement
                        {
                            X = source.x,
                            Y = source.y,
                            Z = source.z,
                            Visibility = source.visibility,
                            HandIndex = h,
                        };
                    }
                    else
                    {
                        world[bufferIndex] = new HandWorldLandmarkElement { HandIndex = -1 };
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

            entityManager = _ecsWorld.EntityManager;
            if (_singletonEntity == Entity.Null || !entityManager.Exists(_singletonEntity))
            {
                _singletonEntity = HandTrackingSingletonUtil.GetOrCreateSingleton(entityManager);
                _hasLoggedOwnershipConflict = false;
            }

            return true;
        }

        private void WriteResetStateIfPossible()
        {
            if (!TryGetEntityManager(out var entityManager))
            {
                return;
            }

            var owner = OwnerRaw();
            if (!TrackingWriterOwnershipUtil.IsOwner(entityManager, _singletonEntity, owner))
            {
                return;
            }

            HandTrackingSingletonUtil.WriteResetEmptyState(entityManager, _singletonEntity);
            TrackingWriterOwnershipUtil.Release(entityManager, _singletonEntity, owner);
        }

        // 싱글턴 단일 작성자 보장. 다른 프로바이더 소유면 이번 프레임 기록을 건너뛴다.
        private bool EnsureHandOwnership(EntityManager entityManager)
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
                MpudLog.Warning("[MPUD] Hand 싱글턴이 다른 프로바이더 소유라 기록을 건너뛴다.");
            }

            return false;
        }

        private ulong OwnerRaw() => EntityId.ToULong(GetEntityId());

        private bool ShouldLogSubmit()
        {
            if (!MpudLog.Enabled || _logIntervalFrames <= 0)
            {
                return false;
            }

            return _submitCount % _logIntervalFrames == 0;
        }

        private bool ShouldLogFrameSummary()
        {
            if (!_hasLoggedFrameSummary)
            {
                return true;
            }

            if (_service.LatestIsValid != _lastLoggedFrameIsValid
                || _service.LatestHandedness != _lastLoggedFrameHandedness
                || _service.LatestLandmarkCount != _lastLoggedFrameLandmarkCount)
            {
                return true;
            }

            return ShouldLogSubmit();
        }
    }
}
