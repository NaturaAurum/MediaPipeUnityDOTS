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
    /// WebCamTexture로부터 프레임을 캡처하고 HandTrackingService에 제출하는 샘플 프로바이더.
    /// </summary>
    public class WebcamFrameProvider : MonoBehaviour
    {
        private const int LandmarkCapacity = 21;

        [SerializeField]
        private int _requestedWidth = 640;
        [SerializeField]
        private int _requestedHeight = 480;
        [SerializeField]
        private int _requestedFps = 30;
        [SerializeField]
        private int _numHands = 2;
        [SerializeField]
        private int _logIntervalFrames = 60;

        /// <summary>
        /// 추적할 손 수. HandTrackingService와 포인트 스포너가 공유한다.
        /// </summary>
        public int NumHands => Mathf.Clamp(_numHands, 1, MpudHandResult.MaxHands);

        /// <summary>
        /// Update에서 읽은 최신 raw 픽셀. 얼굴 등 다른 트래커와 웹캠을 공유한다.
        /// </summary>
        public Color32[] LatestPixels => _pixelBuffer;

        public int LatestPixelWidth { get; private set; }

        public int LatestPixelHeight { get; private set; }

        public bool LatestFlipVertically { get; private set; }

        private WebCamTexture _webCamTexture;
        private HandTrackingService _service;
        private Color32[] _pixelBuffer;
        private MpudNormalizedLandmark[] _landmarkCopyBuffer;
        private World _ecsWorld;
        private Entity _singletonEntity;
        private bool _hasLoggedRuntimeMetadata;
        private bool _hasLoggedFrameSummary;
        private bool _lastLoggedFrameIsValid;
        private int _lastLoggedFrameHandedness;
        private int _lastLoggedFrameLandmarkCount;
        private bool _pendingResetSnapshotPush;
        private long _submitCount;
        private long _lastCopiedTimestamp;

        private void OnEnable()
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

        private void Update()
        {
            if (_webCamTexture == null || _service == null)
            {
                return;
            }

            if (_pendingResetSnapshotPush && TryGetEntityManager(out var resetEntityManager))
            {
                HandTrackingSingletonUtil.WriteResetEmptyState(resetEntityManager, _singletonEntity);
                _pendingResetSnapshotPush = false;
            }

            if (!_webCamTexture.didUpdateThisFrame)
            {
                return;
            }

            var width = _webCamTexture.width;
            var height = _webCamTexture.height;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            var pixelCount = checked(width * height);
            if (_pixelBuffer == null || _pixelBuffer.Length != pixelCount)
            {
                _pixelBuffer = new Color32[pixelCount];
            }

            _webCamTexture.GetPixels32(_pixelBuffer);

            var flipVertically = _webCamTexture.videoVerticallyMirrored;
            LatestPixelWidth = width;
            LatestPixelHeight = height;
            LatestFlipVertically = flipVertically;
            if (!_hasLoggedRuntimeMetadata)
            {
                Debug.Log(
                    $"[MPUD] Webcam ready: {width}x{height} | mirrored={_webCamTexture.videoVerticallyMirrored} | rotation={_webCamTexture.videoRotationAngle} | flipVerticalSubmit={flipVertically}");
                _hasLoggedRuntimeMetadata = true;
            }

            var previousFrameCount = _service.LatestFrameCount;
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
                    $"[MPUD] Frame #{_service.LatestFrameCount} | Valid={_service.LatestIsValid} | Hands={_service.LatestHandCount} | Handedness={_service.LatestHandedness} | Score={_service.LatestScore:F2} | Landmarks={_service.LatestLandmarkCount} | ts={_service.LatestTimestampUs}");
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
            if (_webCamTexture != null || _service != null)
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
            var devices = WebCamTexture.devices;
            if (devices == null || devices.Length == 0)
            {
                throw new InvalidOperationException("No webcam devices were found.");
            }

            _service = new HandTrackingService(modelPath, _numHands);
            _webCamTexture = new WebCamTexture(devices[0].name, _requestedWidth, _requestedHeight, _requestedFps);
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

        private void DisposeResources()
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

        /// <summary>
        /// 배경 렌더용 웹캠 텍스처. 초기화 전이거나 실패 시 null이다.
        /// </summary>
        public WebCamTexture VideoTexture => _webCamTexture;

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
                _service.LatestFrameCount);
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
            }

            return true;
        }

        private void WriteResetStateIfPossible()
        {
            if (!TryGetEntityManager(out var entityManager))
            {
                return;
            }

            HandTrackingSingletonUtil.WriteResetEmptyState(entityManager, _singletonEntity);
        }

        private bool ShouldLogSubmit()
        {
            if (!MpudLogService.Enabled || _logIntervalFrames <= 0)
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
