using System;
using System.IO;
using MediaPipeUnityDots.Runtime.Tracking;
using UnityEngine;

namespace MediaPipeUnityDotsSamples.HandTracking
{
    /// <summary>
    /// WebCamTexture로부터 프레임을 캡처하고 HandTrackingService에 제출하는 샘플 프로바이더.
    /// </summary>
    public class WebcamFrameProvider : MonoBehaviour
    {
        [SerializeField] int requestedWidth = 640;
        [SerializeField] int requestedHeight = 480;
        [SerializeField] int requestedFps = 30;
        [SerializeField] int logIntervalFrames = 60;

        WebCamTexture _webCamTexture;
        HandTrackingService _service;
        Color32[] _pixelBuffer;
        bool _hasLoggedRuntimeMetadata;
        long _submitCount;

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

            Debug.Log(
                $"[MPUD] Frame #{_service.LatestFrameCount} | Valid={_service.LatestIsValid} | Handedness={_service.LatestHandedness} | Score={_service.LatestScore:F2} | Landmarks={_service.LatestLandmarkCount} | ts={_service.LatestTimestampUs}");
        }

        void OnDisable()
        {
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
            _hasLoggedRuntimeMetadata = false;
            _submitCount = 0;

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
            _hasLoggedRuntimeMetadata = false;
            _submitCount = 0;
        }

        bool ShouldLogSubmit()
        {
            if (logIntervalFrames <= 0)
            {
                return true;
            }

            return _submitCount % logIntervalFrames == 0;
        }
    }
}
