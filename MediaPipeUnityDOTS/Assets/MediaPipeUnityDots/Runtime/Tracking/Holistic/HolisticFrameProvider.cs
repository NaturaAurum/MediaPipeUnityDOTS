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
    /// 단일 holistic 추론으로 얼굴/포즈/양손을 기존 ECS 싱글턴에 푸시하는 런타임 프로바이더.
    /// 같은 스트림에 작성자가 둘 있으면 먼저 획득한 쪽만 쓰고, 다른 쪽은 경고를 남기고 건너뛴다.
    /// 개별 트래커 프로바이더와 동시 실행하면 같은 싱글턴에 쓰므로 비교 시에는 한쪽을 끈다.
    /// WebcamFrameProvider.Update 이후에 동작하므로 LateUpdate에서 소비한다.
    /// </summary>
    public sealed class HolisticFrameProvider : MonoBehaviour
    {
        [SerializeField]
        private WebcamFrameProvider _webcamSource;
        [SerializeField]
        private float _minDetectionConfidence = 0.5f;
        [SerializeField]
        private float _minPresenceConfidence = 0.5f;
        [SerializeField]
        private int _logIntervalFrames = 60;

        // 전담 프로바이더와 같은 싱글턴에 쓰면 깜빡거린다. 소유자가 따로 있으면 꺼라.
        [SerializeField]
        private bool _publishFace = true;
        [SerializeField]
        private bool _publishPose = true;
        [SerializeField]
        private bool _publishHands = true;

        private HolisticTrackingService _service;
        private MpudNormalizedLandmark[] _faceCopyBuffer;
        private MpudNormalizedLandmark[] _poseCopyBuffer;
        private MpudNormalizedLandmark[] _handCopyBuffer;
        private MpudNormalizedLandmark[] _leftHandCopy;
        private World _ecsWorld;
        private Entity _faceSingleton;
        private Entity _poseSingleton;
        private Entity _handSingleton;
        private bool _hasLoggedFaceOwnershipConflict;
        private bool _hasLoggedPoseOwnershipConflict;
        private bool _hasLoggedHandOwnershipConflict;
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
                MpudLog.Error("[MPUD] HolisticFrameProvider needs WebcamFrameProvider.");
                enabled = false;
                return;
            }

            try
            {
                InitializeResources();
            }
            catch (Exception exception)
            {
                MpudLog.Error($"[MPUD] Failed to initialize holistic provider: {exception}");
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
            _service.SubmitAndPoll(pixels, width, height, _webcamSource.LatestFlipVertically, new CaptureStamp(_webcamSource.LatestCaptureId, _webcamSource.LatestCaptureTimestampUs, _webcamSource.CaptureEpoch));

            if (_service.LatestFrameCount == previousFrameCount)
            {
                return;
            }

            _submitCount++;
            if (MpudLog.Enabled && _logIntervalFrames > 0 && _submitCount % _logIntervalFrames == 0)
            {
                MpudLog.Log(
                    $"[MPUD] Holistic frame #{_service.LatestFrameCount} | Valid={_service.LatestIsValid} | Face={_service.LatestFaceLandmarkCount} Pose={_service.LatestPoseLandmarkCount} L={_service.LatestLeftHandLandmarkCount} R={_service.LatestRightHandLandmarkCount} | ts={_service.LatestTimestampUs}");
            }

            if (!TryGetEntityManager(out var entityManager))
            {
                return;
            }

            if (_service.LatestTimestampUs <= _lastCopiedTimestamp)
            {
                return;
            }

            PushAllToEcs(entityManager);
            _lastCopiedTimestamp = _service.LatestTimestampUs;
        }

        private void OnDisable()
        {
            WriteResetStatesIfPossible();
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
                "holistic_landmarker.task");
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException("holistic_landmarker.task was not found.", modelPath);
            }

            _service = new HolisticTrackingService(modelPath, _minDetectionConfidence, _minPresenceConfidence);
            _faceCopyBuffer = new MpudNormalizedLandmark[MpudHolisticResult.FaceLandmarks];
            _poseCopyBuffer = new MpudNormalizedLandmark[MpudHolisticResult.PoseLandmarks];
            _handCopyBuffer = new MpudNormalizedLandmark[MpudHolisticResult.HandLandmarks];
            _leftHandCopy = new MpudNormalizedLandmark[MpudHolisticResult.HandLandmarks];
            _ecsWorld = null;
            _faceSingleton = Entity.Null;
            _poseSingleton = Entity.Null;
            _handSingleton = Entity.Null;
            _submitCount = 0;
            _lastCopiedTimestamp = 0;

            TryGetEntityManager(out _);

            MpudLog.Log("[MPUD] Holistic provider started.");
        }

        private void DisposeResources()
        {
            if (_service != null)
            {
                _service.Dispose();
                _service = null;
            }

            _faceCopyBuffer = null;
            _poseCopyBuffer = null;
            _handCopyBuffer = null;
            _leftHandCopy = null;
            _ecsWorld = null;
            _faceSingleton = Entity.Null;
            _poseSingleton = Entity.Null;
            _handSingleton = Entity.Null;
        }

        private void PushAllToEcs(EntityManager entityManager)
        {
            if (_publishFace && EnsureStreamOwnership(entityManager, _faceSingleton, ref _hasLoggedFaceOwnershipConflict, "Face"))
            {
                PushFaceToEcs(entityManager);
            }

            if (_publishPose && EnsureStreamOwnership(entityManager, _poseSingleton, ref _hasLoggedPoseOwnershipConflict, "Pose"))
            {
                PushPoseToEcs(entityManager);
            }

            if (_publishHands && EnsureStreamOwnership(entityManager, _handSingleton, ref _hasLoggedHandOwnershipConflict, "Hand"))
            {
                PushHandsToEcs(entityManager);
            }
        }

        private void PushFaceToEcs(EntityManager entityManager)
        {
            var count = _service.CopyLatestFaceTo(_faceCopyBuffer);
            if (count > 0)
            {
                entityManager.SetComponentData(
                    _faceSingleton,
                    new FaceTrackingStatus
                    {
                        IsValid = true,
                        FaceCount = 1,
                        LandmarkCount = count,
                        TimestampUs = _service.LatestTimestampUs,
                        FrameCount = _service.LatestFrameCount,
                    });

                var landmarks = entityManager.GetBuffer<FaceLandmarkElement>(_faceSingleton);
                if (landmarks.Length != count)
                {
                    landmarks.ResizeUninitialized(count);
                }
                for (var i = 0; i < count; i++)
                {
                    var source = _faceCopyBuffer[i];
                    landmarks[i] = new FaceLandmarkElement
                    {
                        X = source.x,
                        Y = source.y,
                        Z = source.z,
                        FaceIndex = 0,
                    };
                }

                return;
            }

            FaceTrackingSingletonUtil.WriteInvalidPolledState(
                entityManager, _faceSingleton, _service.LatestTimestampUs, _service.LatestFrameCount);
        }

        private void PushPoseToEcs(EntityManager entityManager)
        {
            var count = _service.CopyLatestPoseTo(_poseCopyBuffer);
            if (count > 0)
            {
                entityManager.SetComponentData(
                    _poseSingleton,
                    new PoseTrackingStatus
                    {
                        IsValid = true,
                        PoseCount = 1,
                        LandmarkCount = count,
                        TimestampUs = _service.LatestTimestampUs,
                        FrameCount = _service.LatestFrameCount,
                        CaptureId = _service.LatestCaptureId,
                        CaptureTimestampUs = _service.LatestCaptureTimestampUs,
                        CaptureEpoch = _service.LatestCaptureEpoch,
                    });

                var landmarks = entityManager.GetBuffer<PoseLandmarkElement>(_poseSingleton);
                if (landmarks.Length != count)
                {
                    landmarks.ResizeUninitialized(count);
                }
                for (var i = 0; i < count; i++)
                {
                    var source = _poseCopyBuffer[i];
                    landmarks[i] = new PoseLandmarkElement
                    {
                        X = source.x,
                        Y = source.y,
                        Z = source.z,
                        PoseIndex = 0,
                    };
                }

                var world = entityManager.GetBuffer<PoseWorldLandmarkElement>(_poseSingleton);
                if (world.Length != count)
                {
                    world.ResizeUninitialized(count);
                }

                var worldCount = _service.CopyLatestPoseWorldTo(_poseCopyBuffer);
                for (var i = 0; i < count; i++)
                {
                    if (i < worldCount)
                    {
                        var worldSource = _poseCopyBuffer[i];
                        world[i] = new PoseWorldLandmarkElement
                        {
                            X = worldSource.x,
                            Y = worldSource.y,
                            Z = worldSource.z,
                            Visibility = worldSource.visibility,
                            PoseIndex = 0,
                        };
                    }
                    else
                    {
                        world[i] = new PoseWorldLandmarkElement { PoseIndex = -1 };
                    }
                }


                return;
            }

            PoseTrackingSingletonUtil.WriteInvalidPolledState(
                entityManager, _poseSingleton, _service.LatestTimestampUs, _service.LatestFrameCount, _service.LatestCaptureId, _service.LatestCaptureTimestampUs, _service.LatestCaptureEpoch);
        }

        private void PushHandsToEcs(EntityManager entityManager)
        {
            var leftCount = _service.CopyLatestLeftHandTo(_handCopyBuffer);
            Array.Copy(_handCopyBuffer, _leftHandCopy, Math.Min(Math.Max(leftCount, 0), _leftHandCopy.Length));

            var rightCount = _service.CopyLatestRightHandTo(_handCopyBuffer);

            var handCount = (leftCount > 0 ? 1 : 0) + (rightCount > 0 ? 1 : 0);
            if (handCount == 0)
            {
                HandTrackingSingletonUtil.WriteInvalidPolledState(
                    entityManager, _handSingleton, _service.LatestTimestampUs, _service.LatestFrameCount, _service.LatestCaptureId, _service.LatestCaptureTimestampUs, _service.LatestCaptureEpoch);
                return;
            }

            var status = new HandTrackingStatus
            {
                IsValid = true,
                HandCount = handCount,
                Handedness = 0,
                Score = 1f,
                LandmarkCount = leftCount > 0 ? leftCount : rightCount,
                TimestampUs = _service.LatestTimestampUs,
                FrameCount = _service.LatestFrameCount,
                CaptureId = _service.LatestCaptureId,
                CaptureTimestampUs = _service.LatestCaptureTimestampUs,
                CaptureEpoch = _service.LatestCaptureEpoch,
            };
            status.HandednessList.Clear();
            status.ScoreList.Clear();

            var landmarks = entityManager.GetBuffer<LandmarkElement>(_handSingleton);
            if (landmarks.Length != handCount * 21)
            {
                landmarks.ResizeUninitialized(handCount * 21);
            }

            var slot = 0;
            if (leftCount > 0)
            {
                WriteHandSlot(landmarks, slot, 0, _leftHandCopy, leftCount);
                status.HandednessList.Add(0);
                status.ScoreList.Add(1f);
                slot++;
            }

            if (rightCount > 0)
            {
                WriteHandSlot(landmarks, slot, slot, _handCopyBuffer, rightCount);
                status.HandednessList.Add(1);
                status.ScoreList.Add(1f);
                slot++;
            }

            var world = entityManager.GetBuffer<HandWorldLandmarkElement>(_handSingleton);
            if (world.Length != handCount * 21)
            {
                world.ResizeUninitialized(handCount * 21);
            }

            var worldSlot = 0;
            if (leftCount > 0)
            {
                var leftWorldCount = _service.CopyLatestLeftHandWorldTo(_handCopyBuffer);
                for (var i = 0; i < 21; i++)
                {
                    var bufferIndex = worldSlot * 21 + i;
                    if (i < leftWorldCount)
                    {
                        var worldSource = _handCopyBuffer[i];
                        world[bufferIndex] = new HandWorldLandmarkElement
                        {
                            X = worldSource.x,
                            Y = worldSource.y,
                            Z = worldSource.z,
                            Visibility = worldSource.visibility,
                            HandIndex = 0,
                        };
                    }
                    else
                    {
                        world[bufferIndex] = new HandWorldLandmarkElement { HandIndex = -1 };
                    }
                }

                worldSlot++;
            }

            if (rightCount > 0)
            {
                var rightWorldCount = _service.CopyLatestRightHandWorldTo(_handCopyBuffer);
                for (var i = 0; i < 21; i++)
                {
                    var bufferIndex = worldSlot * 21 + i;
                    if (i < rightWorldCount)
                    {
                        var worldSource = _handCopyBuffer[i];
                        world[bufferIndex] = new HandWorldLandmarkElement
                        {
                            X = worldSource.x,
                            Y = worldSource.y,
                            Z = worldSource.z,
                            Visibility = worldSource.visibility,
                            HandIndex = worldSlot,
                        };
                    }
                    else
                    {
                        world[bufferIndex] = new HandWorldLandmarkElement { HandIndex = -1 };
                    }
                }

                worldSlot++;
            }



            entityManager.SetComponentData(_handSingleton, status);
        }

        private static void WriteHandSlot(
            DynamicBuffer<LandmarkElement> landmarks, int slot, int handIndex,
            MpudNormalizedLandmark[] source, int count)
        {
            for (var i = 0; i < 21; i++)
            {
                var bufferIndex = slot * 21 + i;
                if (i < count)
                {
                    var landmark = source[i];
                    landmarks[bufferIndex] = new LandmarkElement
                    {
                        X = landmark.x,
                        Y = landmark.y,
                        Z = landmark.z,
                        Visibility = landmark.visibility,
                        Presence = landmark.presence,
                        HandIndex = handIndex,
                    };
                }
                else
                {
                    landmarks[bufferIndex] = new LandmarkElement { HandIndex = -1 };
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
                _faceSingleton = Entity.Null;
                _poseSingleton = Entity.Null;
                _handSingleton = Entity.Null;
                return false;
            }

            if (_ecsWorld == null || _ecsWorld != defaultWorld || !_ecsWorld.IsCreated)
            {
                _ecsWorld = defaultWorld;
                _faceSingleton = Entity.Null;
                _poseSingleton = Entity.Null;
                _handSingleton = Entity.Null;
            }

            var manager = defaultWorld.EntityManager;
            if (_publishFace)
            {
                if (_faceSingleton == Entity.Null || !manager.Exists(_faceSingleton))
                {
                    _faceSingleton = FaceTrackingSingletonUtil.GetOrCreateSingleton(manager);
                    _hasLoggedFaceOwnershipConflict = false;
                }
            }
            else
            {
                ResetStreamIfOwned(manager, ref _faceSingleton, FaceTrackingSingletonUtil.WriteResetEmptyState);
            }

            if (_publishPose)
            {
                if (_poseSingleton == Entity.Null || !manager.Exists(_poseSingleton))
                {
                    _poseSingleton = PoseTrackingSingletonUtil.GetOrCreateSingleton(manager);
                    _hasLoggedPoseOwnershipConflict = false;
                }
            }
            else
            {
                ResetStreamIfOwned(manager, ref _poseSingleton, PoseTrackingSingletonUtil.WriteResetEmptyState);
            }

            if (_publishHands)
            {
                if (_handSingleton == Entity.Null || !manager.Exists(_handSingleton))
                {
                    _handSingleton = HandTrackingSingletonUtil.GetOrCreateSingleton(manager);
                    _hasLoggedHandOwnershipConflict = false;
                }
            }
            else
            {
                ResetStreamIfOwned(manager, ref _handSingleton, HandTrackingSingletonUtil.WriteResetEmptyState);
            }

            entityManager = manager;
            return true;
        }

        private void WriteResetStatesIfPossible()
        {
            if (_ecsWorld is not { IsCreated: true })
            {
                return;
            }

            var entityManager = _ecsWorld.EntityManager;
            ResetStreamIfOwned(entityManager, ref _faceSingleton, FaceTrackingSingletonUtil.WriteResetEmptyState);
            ResetStreamIfOwned(entityManager, ref _poseSingleton, PoseTrackingSingletonUtil.WriteResetEmptyState);
            ResetStreamIfOwned(entityManager, ref _handSingleton, HandTrackingSingletonUtil.WriteResetEmptyState);
        }

        // 발행이 꺼진 스트림은 소유 중이면 초기화하고 소유권을 놓는다. 소유자가 아니면 건드리지 않는다.
        private void ResetStreamIfOwned(EntityManager entityManager, ref Entity singleton, Action<EntityManager, Entity> writeReset)
        {
            if (singleton == Entity.Null || !entityManager.Exists(singleton))
            {
                singleton = Entity.Null;
                return;
            }

            var owner = OwnerRaw();
            if (TrackingWriterOwnershipUtil.IsOwner(entityManager, singleton, owner))
            {
                writeReset(entityManager, singleton);
                TrackingWriterOwnershipUtil.Release(entityManager, singleton, owner);
            }

            singleton = Entity.Null;
        }

        // 스트림별 단일 작성자 보장. 다른 프로바이더 소유면 이번 프레임 기록을 건너뛴다.
        private bool EnsureStreamOwnership(EntityManager entityManager, Entity singleton, ref bool loggedConflict, string streamName)
        {
            var owner = OwnerRaw();
            if (TrackingWriterOwnershipUtil.IsOwner(entityManager, singleton, owner))
            {
                return true;
            }

            if (TrackingWriterOwnershipUtil.TryAcquire(entityManager, singleton, owner))
            {
                loggedConflict = false;
                return true;
            }

            if (!loggedConflict)
            {
                loggedConflict = true;
                MpudLog.Warning($"[MPUD] {streamName} 싱글턴이 다른 프로바이더 소유라 기록을 건너뛴다.");
            }

            return false;
        }

        private ulong OwnerRaw() => EntityId.ToULong(GetEntityId());
    }
}
