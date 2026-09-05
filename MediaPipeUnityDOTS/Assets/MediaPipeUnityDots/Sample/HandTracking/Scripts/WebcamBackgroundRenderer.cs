using UnityEngine;

namespace MediaPipeUnityDots.Sample.HandTracking.Scripts
{
    /// <summary>
    /// 웹캠 영상을 랜드마크 뒤쪽 배경으로 그리는 Quad.
    /// 카메라 앞 일정 거리에 프러스텀 크기로 유지하고, 화면비에 맞춰 늘림 없이 덮어쓴다(cover-crop).
    /// Quad/카메라/provider는 씬에서 직렬화 참조로 배선한다.
    /// </summary>
    public sealed class WebcamBackgroundRenderer : MonoBehaviour
    {
        // 랜드마크 평면(z≈0)보다 뒤, far(1000)보다 훨씬 앞.
        private const float BackgroundDistance = 15f;
        private const string UnlitShaderName = "Universal Render Pipeline/Unlit";
        private const string QuadMeshName = "Quad.fbx";

        [SerializeField]
        private WebcamFrameProvider _provider;
        [SerializeField]
        private Camera _camera;
        [SerializeField]
        private MeshFilter _quadFilter;
        [SerializeField]
        private MeshRenderer _quadRenderer;

        private Material _material;
        private bool _visible = true;

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_quadRenderer != null)
            {
                _quadRenderer.enabled = visible;
            }
        }

        private void OnEnable()
        {
            if (_provider == null || _camera == null || _quadFilter == null || _quadRenderer == null)
            {
                Debug.LogError("[MPUD] Webcam background references are not wired in the scene.");
                enabled = false;
                return;
            }

            var quadMesh = Resources.GetBuiltinResource<Mesh>(QuadMeshName);
            if (quadMesh == null)
            {
                Debug.LogError("[MPUD] Built-in Quad mesh was not found.");
                enabled = false;
                return;
            }

            _quadFilter.sharedMesh = quadMesh;
            _material = new Material(Shader.Find(UnlitShaderName));
            _quadRenderer.material = _material;
            _quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _quadRenderer.receiveShadows = false;
            _quadRenderer.enabled = _visible;
        }

        private void LateUpdate()
        {
            if (_provider == null || _camera == null || _quadRenderer == null || _material == null)
            {
                return;
            }

            var video = _provider.VideoTexture;
            if (video == null || video.width <= 0 || video.height <= 0)
            {
                _quadRenderer.enabled = false;
                return;
            }

            _quadRenderer.enabled = _visible;
            FitQuadToFrustum();
            UpdateCoverUv(video);
        }

        private void OnDisable()
        {
            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }
        }

        private void FitQuadToFrustum()
        {
            var cameraTransform = _camera.transform;
            _quadRenderer.transform.SetPositionAndRotation(
                cameraTransform.position + cameraTransform.forward * BackgroundDistance,
                cameraTransform.rotation);
            var height = 2f * BackgroundDistance * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            _quadRenderer.transform.localScale = new Vector3(height * _camera.aspect, height, 1f);
        }

        private void UpdateCoverUv(WebCamTexture video)
        {
            if (_material.mainTexture != video)
            {
                _material.mainTexture = video;
            }

            var videoAspect = (float)video.width / video.height;
            var screenAspect = _camera.aspect;
            var scale = Vector2.one;
            var offset = Vector2.zero;
            if (videoAspect > screenAspect)
            {
                scale.x = screenAspect / videoAspect;
                offset.x = (1f - scale.x) * 0.5f;
            }
            else
            {
                scale.y = videoAspect / screenAspect;
                offset.y = (1f - scale.y) * 0.5f;
            }

            // ponytail: videoRotationAngle 보정 없음, landscape 웹캠 가정. portrait 필요 시 반영.
            if (video.videoVerticallyMirrored)
            {
                offset.y += scale.y;
                scale.y = -scale.y;
            }

            _material.mainTextureScale = scale;
            _material.mainTextureOffset = offset;
        }
    }
}
