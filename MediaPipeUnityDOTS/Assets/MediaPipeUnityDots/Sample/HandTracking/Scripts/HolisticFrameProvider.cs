using System;
using System.IO;
using MediaPipeUnityDots.Runtime.Ecs;
using MediaPipeUnityDots.Runtime.Logging;
using MediaPipeUnityDots.Runtime.Interop;
using MediaPipeUnityDots.Runtime.Tracking;
using Unity.Entities;
using UnityEngine;

namespace MediaPipeUnityDots.Sample.HandTracking.Scripts
{
    /// <summary>
    /// 단일 holistic 추론으로 얼굴/포즈/양손을 기존 ECS 싱글턴에 푸시하는 샘플 프로바이더.
    /// 새 ECS/스포너를 만들지 않고 Face/Pose/Hand 싱글턴+버퍼를 재사용한다.
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

        private HolisticTrackingService _service;
        private MpudNormalizedLandmark[] _faceCopyBuffer;
        private MpudNormalizedLandmark[] _poseCopyBuffer;
        private MpudNormalizedLandmark[] _handCopyBuffer;
        private MpudNormalizedLandmark[] _leftHandCopy;
        private World _ecsWorld;
        private Entity _faceSingleton;
        private Entity _poseSingleton;
        private Entity _handSingleton;
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
            _service.SubmitAndPoll(pixels, width, height, _webcamSource.LatestFlipVertically);

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
            PushFaceToEcs(entityManager);
            PushPoseToEcs(entityManager);
            PushHandsToEcs(entityManager);
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

                return;
            }

            PoseTrackingSingletonUtil.WriteInvalidPolledState(
                entityManager, _poseSingleton, _service.LatestTimestampUs, _service.LatestFrameCount);
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
                    entityManager, _handSingleton, _service.LatestTimestampUs, _service.LatestFrameCount);
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
            if (_faceSingleton == Entity.Null || !manager.Exists(_faceSingleton))
            {
                _faceSingleton = FaceTrackingSingletonUtil.GetOrCreateSingleton(manager);
            }

            if (_poseSingleton == Entity.Null || !manager.Exists(_poseSingleton))
            {
                _poseSingleton = PoseTrackingSingletonUtil.GetOrCreateSingleton(manager);
            }

            if (_handSingleton == Entity.Null || !manager.Exists(_handSingleton))
            {
                _handSingleton = HandTrackingSingletonUtil.GetOrCreateSingleton(manager);
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
            if (_faceSingleton != Entity.Null && entityManager.Exists(_faceSingleton))
            {
                FaceTrackingSingletonUtil.WriteResetEmptyState(entityManager, _faceSingleton);
            }

            if (_poseSingleton != Entity.Null && entityManager.Exists(_poseSingleton))
            {
                PoseTrackingSingletonUtil.WriteResetEmptyState(entityManager, _poseSingleton);
            }

            if (_handSingleton != Entity.Null && entityManager.Exists(_handSingleton))
            {
                HandTrackingSingletonUtil.WriteResetEmptyState(entityManager, _handSingleton);
            }
        }
    }
}
