using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 트래커 공통 포인트 스포너. 트래커별 스포너 3종을 대체한다.
    /// 렌더에 필요한 managed 객체(Mesh/Material)는 여기서만 다루고,
    /// 이후 매 프레임 위치 갱신은 LandmarkRenderSystem이 담당한다.
    /// </summary>
    public sealed class LandmarkPointSpawner : MonoBehaviour
    {
        [Serializable]
        public struct TrackerSpawnConfig
        {
            public LandmarkTracker Tracker;
            public MonoBehaviour Source;
            public Color Color;
        }

        private const int HandLandmarks = 21;
        private const int FaceLandmarks = 478;
        private const int PoseLandmarks = 33;

        [SerializeField]
        private TrackerSpawnConfig[] _trackers;

        private Entity[] _points;
        private Material[] _materials;

        private void OnEnable()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world is not { IsCreated: true })
            {
                return;
            }

            if (_trackers == null || _trackers.Length == 0)
            {
                MpudLog.Error("[MPUD] LandmarkPointSpawner has no tracker configs.");
                enabled = false;
                return;
            }

            var entityManager = world.EntityManager;
            var mesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            if (mesh == null)
            {
                MpudLog.Error("[MPUD] Built-in Sphere mesh was not found.");
                enabled = false;
                return;
            }

            var totalPoints = 0;
            foreach (var config in _trackers)
            {
                totalPoints += GetMaxTargets(config) * GetLandmarkCount(config.Tracker);
            }

            _points = new Entity[totalPoints];
            _materials = new Material[_trackers.Length];
            var pointOffset = 0;
            for (var c = 0; c < _trackers.Length; c++)
            {
                var config = _trackers[c];
                var maxTargets = GetMaxTargets(config);
                if (maxTargets <= 0)
                {
                    continue;
                }

                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                {
                    color = ResolveColor(config),
                };
                _materials[c] = material;
                var renderMeshArray = new RenderMeshArray(new[] { material }, new[] { mesh });
                var description = new RenderMeshDescription(ShadowCastingMode.Off, false);
                var materialMeshInfo = MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0);
                var landmarkCount = GetLandmarkCount(config.Tracker);
                for (var t = 0; t < maxTargets; t++)
                {
                    for (var i = 0; i < landmarkCount; i++)
                    {
                        var entity = entityManager.CreateEntity();
                        entityManager.AddComponentData(entity, new LandmarkPoint
                        {
                            Tracker = config.Tracker,
                            Target = t,
                            Index = i,
                        });
                        entityManager.AddComponentData(entity, new LandmarkFilterState());
                        entityManager.AddComponentData(
                            entity,
                            LocalTransform.FromPositionRotationScale(float3.zero, quaternion.identity, 0f));
                        RenderMeshUtility.AddComponents(entity, entityManager, description, renderMeshArray, materialMeshInfo);
                        _points[pointOffset++] = entity;
                    }
                }
            }
        }

        private void OnDisable()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (_points != null && world != null && world.IsCreated)
            {
                var entityManager = world.EntityManager;
                foreach (var point in _points)
                {
                    if (point != Entity.Null && entityManager.Exists(point))
                    {
                        entityManager.DestroyEntity(point);
                    }
                }
            }

            _points = null;
            if (_materials != null)
            {
                foreach (var material in _materials)
                {
                    if (material != null)
                    {
                        Destroy(material);
                    }
                }

                _materials = null;
            }
        }

        private static int GetMaxTargets(TrackerSpawnConfig config)
        {
            if (config.Source == null)
            {
                MpudLog.Error($"[MPUD] LandmarkPointSpawner needs a source for {config.Tracker}.");
                return 0;
            }

            if (config.Source is not IPointSource source)
            {
                MpudLog.Error($"[MPUD] LandmarkPointSpawner source is not an IPointSource ({config.Tracker}).");
                return 0;
            }

            return source.MaxTargets;
        }

        private static int GetLandmarkCount(LandmarkTracker tracker)
        {
            return tracker switch
            {
                LandmarkTracker.Hand => HandLandmarks,
                LandmarkTracker.Face => FaceLandmarks,
                LandmarkTracker.Pose => PoseLandmarks,
                _ => 0,
            };
        }

        private static Color ResolveColor(TrackerSpawnConfig config)
        {
            if (config.Color.a > 0f)
            {
                return config.Color;
            }

            return config.Tracker switch
            {
                LandmarkTracker.Hand => Color.green,
                LandmarkTracker.Face => Color.cyan,
                LandmarkTracker.Pose => Color.yellow,
                _ => Color.white,
            };
        }
    }
}
