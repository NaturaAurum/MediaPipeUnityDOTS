using MediaPipeUnityDots.Runtime.Ecs;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace MediaPipeUnityDots.Sample.HandTracking.Scripts
{
    /// <summary>
    /// 손별 21개 랜드마크 포인트 엔티티를 생성하는 sample layer 스포너.
    /// 손 수는 WebcamFrameProvider와 공유한다.
    /// 렌더에 필요한 managed 객체(Mesh/Material)는 여기서만 다루고,
    /// 이후 매 프레임 위치 갱신은 HandLandmarkRenderSystem이 담당한다.
    /// </summary>
    public sealed class HandLandmarkPointSpawner : MonoBehaviour
    {
        private const int LandmarksPerHand = 21;

        [SerializeField]
        private WebcamFrameProvider _provider;

        private Entity[] _points;
        private Material _material;

        private void OnEnable()
        {
            if (_provider == null)
            {
                Debug.LogError("[MPUD] HandLandmarkPointSpawner needs WebcamFrameProvider.");
                enabled = false;
                return;
            }

            var world = World.DefaultGameObjectInjectionWorld;
            if (world is not { IsCreated: true })
            {
                return;
            }

            var entityManager = world.EntityManager;

            var mesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            _material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                color = Color.green,
            };

            var renderMeshArray = new RenderMeshArray(new[] { _material }, new[] { mesh });
            var description = new RenderMeshDescription(ShadowCastingMode.Off, false);
            var materialMeshInfo = MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0);

            var pointCount = _provider.NumHands * LandmarksPerHand;
            _points = new Entity[pointCount];
            for (var h = 0; h < _provider.NumHands; h++)
            {
                for (var i = 0; i < LandmarksPerHand; i++)
                {
                    var entity = entityManager.CreateEntity();
                    entityManager.AddComponentData(entity, new HandLandmarkPoint { HandIndex = h, Index = i });
                    entityManager.AddComponentData(entity, new LandmarkFilterState());
                    entityManager.AddComponentData(
                        entity,
                        LocalTransform.FromPositionRotationScale(float3.zero, quaternion.identity, 0f));
                    RenderMeshUtility.AddComponents(entity, entityManager, description, renderMeshArray, materialMeshInfo);
                    _points[h * LandmarksPerHand + i] = entity;
                }
            }
        }

        private void OnDisable()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (_points != null && world != null && world.IsCreated)
            {
                var entityManager = world.EntityManager;
                foreach (var t in _points)
                {
                    if (entityManager.Exists(t))
                    {
                        entityManager.DestroyEntity(t);
                    }
                }
            }

            _points = null;

            if (_material == null)
            {
                return;
            }
            Destroy(_material);
            _material = null;
        }
    }
}
