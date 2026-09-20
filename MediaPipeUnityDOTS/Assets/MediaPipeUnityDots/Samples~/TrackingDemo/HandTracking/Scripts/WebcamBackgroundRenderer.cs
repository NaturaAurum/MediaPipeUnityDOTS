using MediaPipeUnityDots.Runtime.Ecs;
using MediaPipeUnityDots.Runtime.Tracking;
using Unity.Entities;
using Unity.Mathematics;
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
        // 랜드마크는 이 Quad 평면(+앞 0.05)에 정합 배치한다. UV 크롭식과 함께 매핑을 발행한다.
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

        private World _cachedWorld;
        private Entity _mappingEntity;

        private Material _material;
        private bool _visible = true;
        public bool IsVisible => _visible;

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
                MpudLog.Error("[MPUD] Webcam background references are not wired in the scene.");
                enabled = false;
                return;
            }

            var quadMesh = Resources.GetBuiltinResource<Mesh>(QuadMeshName);
            if (quadMesh == null)
            {
                MpudLog.Error("[MPUD] Built-in Quad mesh was not found.");
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
                PublishOverlayMappingInvalid();
                return;
            }

            _quadRenderer.enabled = _visible;
            FitQuadToFrustum();
            UpdateCoverUv(video, out var uvScale, out var uvOffset);
            PublishOverlayMapping(video, uvScale, uvOffset);
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
            var height = _camera.orthographic
                ? 2f * _camera.orthographicSize
                : 2f * BackgroundDistance * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            _quadRenderer.transform.localScale = new Vector3(height * _camera.aspect, height, 1f);
        }
        private void UpdateCoverUv(WebCamTexture video, out Vector2 uvScale, out Vector2 uvOffset)
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
            uvScale = scale;
            uvOffset = offset;
        }

        private void PublishOverlayMappingInvalid()
        {
            if (!TryGetMappingEntity(out var entityManager, out var entity))
            {
                return;
            }

            var mapping = entityManager.GetComponentData<LandmarkOverlayMapping>(entity);
            mapping.IsValid = 0;
            entityManager.SetComponentData(entity, mapping);
        }

        private void PublishOverlayMapping(WebCamTexture video, Vector2 uvScale, Vector2 uvOffset)
        {
            if (!TryGetMappingEntity(out var entityManager, out var entity))
            {
                return;
            }

            var quadTransform = _quadRenderer.transform;
            entityManager.SetComponentData(entity, new LandmarkOverlayMapping
            {
                IsValid = 1,
                Flipped = video.videoVerticallyMirrored ? 1 : 0,
                UvScaleX = uvScale.x,
                UvOffsetX = uvOffset.x,
                UvScaleY = uvScale.y,
                UvOffsetY = uvOffset.y,
                Origin = quadTransform.position,
                AxisX = (float3)quadTransform.right * quadTransform.localScale.x,
                AxisY = (float3)quadTransform.up * quadTransform.localScale.y,
                Forward = quadTransform.forward,
                CameraPosition = _camera.transform.position,
                NearClipPlane = _camera.nearClipPlane,
                IsPerspective = _camera.orthographic ? 0 : 1,
            });
        }

        private bool TryGetMappingEntity(out EntityManager entityManager, out Entity entity)
        {
            entityManager = default;
            entity = Entity.Null;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world is not { IsCreated: true })
            {
                return false;
            }

            if (_cachedWorld != world || _mappingEntity == Entity.Null)
            {
                _cachedWorld = world;
                _mappingEntity = Entity.Null;
                var query = world.EntityManager.CreateEntityQuery(typeof(LandmarkOverlayMapping));
                try
                {
                    if (query.CalculateEntityCount() == 1)
                    {
                        _mappingEntity = query.GetSingletonEntity();
                    }
                }
                finally
                {
                    query.Dispose();
                }

                if (_mappingEntity == Entity.Null)
                {
                    _mappingEntity = world.EntityManager.CreateEntity(typeof(LandmarkOverlayMapping));
                }
            }

            if (!world.EntityManager.Exists(_mappingEntity))
            {
                _mappingEntity = Entity.Null;
                return false;
            }

            entityManager = world.EntityManager;
            entity = _mappingEntity;
            return true;
        }
    }
}
