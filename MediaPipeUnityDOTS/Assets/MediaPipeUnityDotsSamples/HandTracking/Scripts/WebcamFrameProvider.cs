using System;
using System.IO;
using MediaPipeUnityDots.Runtime.Ecs;
using MediaPipeUnityDots.Runtime.Interop;
using MediaPipeUnityDots.Runtime.Tracking;
using Unity.Entities;
using UnityEngine;

namespace MediaPipeUnityDotsSamples.HandTracking
{
    /// <summary>
    /// WebCamTexture로부터 프레임을 캡처하고 HandTrackingService에 제출하는 샘플 프로바이더.
    /// </summary>
    public class WebcamFrameProvider : MonoBehaviour
    {
        const int LandmarkCapacity = 21;

        [SerializeField] int requestedWidth = 640;
        [SerializeField] int requestedHeight = 480;
        [SerializeField] int requestedFps = 30;
        [SerializeField] int logIntervalFrames = 60;

        WebCamTexture _webCamTexture;
        HandTrackingService _service;
        Color32[] _pixelBuffer;
        MpudNormalizedLandmark[] _landmarkCopyBuffer;
        World _ecsWorld;
        Entity _singletonEntity;
        bool _hasLoggedRuntimeMetadata;
        bool _hasLoggedFrameSummary;
        bool _lastLoggedFrameIsValid;
        int _lastLoggedFrameHandedness;
        int _lastLoggedFrameLandmarkCount;
        bool _pendingResetSnapshotPush;
        long _submitCount;
        long _lastCopiedTimestamp;

        void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            try
            {
                InitializeResources();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[MPUD] Failed to initialize webcam provider: {exception}");
                DisposeResources();
                enabled = false;
            }
        }

        void Update()
        {
            if (_webCamTexture == null || _service == null)
            {
                return;
            }

            if (_pendingResetSnapshotPush && TryGetEntityManager(out EntityManager resetEntityManager))
            {
                HandTrackingSingletonUtil.WriteResetEmptyState(resetEntityManager, _singletonEntity);
                _pendingResetSnapshotPush = false;
            }

            if (!_webCamTexture.didUpdateThisFrame)
            {
                return;
            }

            int width = _webCamTexture.width;
            int height = _webCamTexture.height;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            int pixelCount = checked(width * height);
            if (_pixelBuffer == null || _pixelBuffer.Length != pixelCount)
            {
                _pixelBuffer = new Color32[pixelCount];
            }

            _webCamTexture.GetPixels32(_pixelBuffer);

            bool flipVertically = _webCamTexture.videoVerticallyMirrored;
            if (!_hasLoggedRuntimeMetadata)
            {
                Debug.Log(
                    $"[MPUD] Webcam ready: {width}x{height} | mirrored={_webCamTexture.videoVerticallyMirrored} | rotation={_webCamTexture.videoRotationAngle} | flipVerticalSubmit={flipVertically}");
                _hasLoggedRuntimeMetadata = true;
            }

            long previousFrameCount = _service.LatestFrameCount;
            _service.SubmitAndPoll(_pixelBuffer, width, height, flipVertically);

            if (_service.LatestFrameCount == previousFrameCount)
            {
                return;
            }

            _submitCount++;
            if (ShouldLogSubmit())
            {
                Debug.Log($"[MPUD] Submit #{_submitCount}, ts={_service.LatestTimestampUs}");
            }

            if (ShouldLogFrameSummary())
            {
                _hasLoggedFrameSummary = true;
                _lastLoggedFrameIsValid = _service.LatestIsValid;
                _lastLoggedFrameHandedness = _service.LatestHandedness;
                _lastLoggedFrameLandmarkCount = _service.LatestLandmarkCount;

                Debug.Log(
                    $"[MPUD] Frame #{_service.LatestFrameCount} | Valid={_service.LatestIsValid} | Handedness={_service.LatestHandedness} | Score={_service.LatestScore:F2} | Landmarks={_service.LatestLandmarkCount} | ts={_service.LatestTimestampUs}");
            }

            if (!TryGetEntityManager(out EntityManager entityManager))
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

        void OnDisable()
        {
            WriteResetStateIfPossible();
            DisposeResources();
        }

        void OnDestroy()
        {
            DisposeResources();
        }

        void InitializeResources()
        {
            if (_webCamTexture != null || _service != null)
            {
                return;
            }

            string modelPath = Path.Combine(
                Application.streamingAssetsPath,
                "MediaPipe",
                "Models",
                "hand_landmarker.task");
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException("hand_landmarker.task was not found.", modelPath);
            }

            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices == null || devices.Length == 0)
            {
                throw new InvalidOperationException("No webcam devices were found.");
            }

            _service = new HandTrackingService(modelPath);
            _webCamTexture = new WebCamTexture(devices[0].name, requestedWidth, requestedHeight, requestedFps);
            _webCamTexture.Play();

            _pixelBuffer = null;
            _landmarkCopyBuffer = new MpudNormalizedLandmark[LandmarkCapacity];
            _ecsWorld = null;
            _singletonEntity = Entity.Null;
            _hasLoggedRuntimeMetadata = false;
            _hasLoggedFrameSummary = false;
            _lastLoggedFrameIsValid = false;
            _lastLoggedFrameHandedness = -1;
            _lastLoggedFrameLandmarkCount = 0;
            _pendingResetSnapshotPush = false;
            _submitCount = 0;
            _lastCopiedTimestamp = 0;

            TryGetEntityManager(out _);

            Debug.Log($"[MPUD] Webcam provider started with device '{devices[0].name}'.");
        }

        void DisposeResources()
        {
            if (_service != null)
            {
                _service.Dispose();
                _service = null;
            }

            if (_webCamTexture != null)
            {
                if (_webCamTexture.isPlaying)
                {
                    _webCamTexture.Stop();
                }

                Destroy(_webCamTexture);
                _webCamTexture = null;
            }

            _pixelBuffer = null;
            _landmarkCopyBuffer = null;
            _ecsWorld = null;
            _singletonEntity = Entity.Null;
            _hasLoggedRuntimeMetadata = false;
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
            _pendingResetSnapshotPush = true;
            // reset-empty 상태는 ts=0을 유지해야 다음 poll 결과가 dedupe를 통과한다.
            _lastCopiedTimestamp = 0;
        }

        void PushLatestSnapshotToEcs(EntityManager entityManager)
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
                _service.LatestFrameCount);
        }

        void WriteValidPolledState(EntityManager entityManager)
        {
            int copiedCount = _service.CopyLatestLandmarksTo(_landmarkCopyBuffer);

            entityManager.SetComponentData(
                _singletonEntity,
                new HandTrackingStatus
                {
                    IsValid = true,
                    Handedness = _service.LatestHandedness,
                    Score = _service.LatestScore,
                    LandmarkCount = copiedCount,
                    TimestampUs = _service.LatestTimestampUs,
                    FrameCount = _service.LatestFrameCount,
                });

            DynamicBuffer<LandmarkElement> landmarks = entityManager.GetBuffer<LandmarkElement>(_singletonEntity);
            landmarks.ResizeUninitialized(copiedCount);

            for (int i = 0; i < copiedCount; i++)
            {
                MpudNormalizedLandmark source = _landmarkCopyBuffer[i];
                landmarks[i] = new LandmarkElement
                {
                    X = source.x,
                    Y = source.y,
                    Z = source.z,
                    Visibility = source.visibility,
                    Presence = source.presence,
                };
            }
        }

        bool TryGetEntityManager(out EntityManager entityManager)
        {
            entityManager = default;

            World defaultWorld = World.DefaultGameObjectInjectionWorld;
            if (defaultWorld == null || !defaultWorld.IsCreated)
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
            }

            return true;
        }

        void WriteResetStateIfPossible()
        {
            if (!TryGetEntityManager(out EntityManager entityManager))
            {
                return;
            }

            HandTrackingSingletonUtil.WriteResetEmptyState(entityManager, _singletonEntity);
        }

        bool ShouldLogSubmit()
        {
            if (logIntervalFrames <= 0)
            {
                return true;
            }

            return _submitCount % logIntervalFrames == 0;
        }

        bool ShouldLogFrameSummary()
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
