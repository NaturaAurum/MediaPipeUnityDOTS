using System;
using MediaPipeUnityDots.Runtime.Input;
using UnityEngine;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// WebCamTexture로부터 프레임을 캡처하고 최신 픽셀·CaptureStamp를 제공한다. 추론은 하지 않는다.
    /// </summary>
    public class WebcamFrameProvider : MonoBehaviour
    {
        [SerializeField]
        private int _requestedWidth = 640;
        [SerializeField]
        private int _requestedHeight = 480;
        [SerializeField]
        private int _requestedFps = 30;

        /// <summary>
        /// Update에서 읽은 최신 raw 픽셀. 얼굴 등 다른 트래커와 웹캠을 공유한다.
        /// </summary>
        public Color32[] LatestPixels => _pixelBuffer;

        public int LatestPixelWidth { get; private set; }

        public int LatestPixelHeight { get; private set; }

        public bool LatestFlipVertically { get; private set; }

        public long LatestCaptureId => _latestCaptureId;

        public long LatestCaptureTimestampUs => _latestCaptureTimestampUs;

        public long CaptureEpoch => _captureEpoch;

        public long CaptureClockUs => _captureClock.PeekTimestampUs();

        private WebCamTexture _webCamTexture;
        private Color32[] _pixelBuffer;
        private readonly MonotonicTimestampGenerator _captureClock = new();
        private long _captureEpoch;
        private long _latestCaptureId;
        private long _latestCaptureTimestampUs;
        private bool _hasLoggedRuntimeMetadata;

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
                MpudLog.Error($"[MPUD] Failed to initialize webcam provider: {exception}");
                DisposeResources();
                enabled = false;
            }
        }

        private void Update()
        {
            if (_webCamTexture == null)
            {
                return;
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
            _latestCaptureId++;
            _latestCaptureTimestampUs = _captureClock.NextTimestampUs();
            if (!_hasLoggedRuntimeMetadata)
            {
                MpudLog.Log(
                    $"[MPUD] Webcam ready: {width}x{height} | mirrored={_webCamTexture.videoVerticallyMirrored} | rotation={_webCamTexture.videoRotationAngle} | flipVerticalSubmit={flipVertically}");
                _hasLoggedRuntimeMetadata = true;
            }
        }

        private void OnDisable()
        {
            DisposeResources();
        }

        private void OnDestroy() => DisposeResources();

        private void InitializeResources()
        {
            if (_webCamTexture != null)
            {
                return;
            }

            var devices = WebCamTexture.devices;
            if (devices == null || devices.Length == 0)
            {
                throw new InvalidOperationException("No webcam devices were found.");
            }

            _webCamTexture = new WebCamTexture(devices[0].name, _requestedWidth, _requestedHeight, _requestedFps);
            _webCamTexture.Play();

            _pixelBuffer = null;
            _captureEpoch++;
            _latestCaptureId = 0;
            _latestCaptureTimestampUs = 0;
            _hasLoggedRuntimeMetadata = false;

            MpudLog.Log($"[MPUD] Webcam provider started with device '{devices[0].name}'.");
        }

        private void DisposeResources()
        {
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
        }

        /// <summary>
        /// 배경 렌더용 웹캠 텍스처. 초기화 전이거나 실패 시 null이다.
        /// </summary>
        public WebCamTexture VideoTexture => _webCamTexture;

        /// <summary>
        /// tracker Reset 시 Depth 무효화를 위해 캡처 세대를 증가시킨다.
        /// </summary>
        public void BumpCaptureEpoch()
        {
            _captureEpoch++;
        }
    }
}
