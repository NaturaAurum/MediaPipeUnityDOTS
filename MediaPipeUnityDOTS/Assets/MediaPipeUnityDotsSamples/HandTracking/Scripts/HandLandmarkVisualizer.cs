using UnityEngine;

namespace MediaPipeUnityDotsSamples.HandTracking
{
    /// <summary>
    /// adapter DTO의 21개 정규화 landmark를 Game View에 구체로 렌더한다.
    /// normalized (x: 0~1, y: 0~1, z: 상대 깊이)를 월드 박스에 매핑한다.
    /// 구체는 실행 중에 생성/파괴하므로 씬 에셋 변경이 필요 없다.
    /// </summary>
    public sealed class HandLandmarkVisualizer : MonoBehaviour
    {
        private const float WorldWidth = 2f;
        private const float WorldHeight = 1.5f;
        private const float DepthScale = 1f;
        private const float PointScale = 0.05f;

        private HandTrackingAdapter _adapter;
        private readonly HandTrackingDto _dto = new HandTrackingDto();
        private GameObject[] _points;
        private Material _pointMaterial;

        private void OnEnable()
        {
            _adapter = FindAnyObjectByType<HandTrackingAdapter>();

            _pointMaterial = new Material(Shader.Find("Sprites/Default"))
            {
                color = Color.green,
            };

            _points = new GameObject[HandTrackingDto.LandmarkCapacity];
            for (var i = 0; i < _points.Length; i++)
            {
                var point = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                point.name = $"Landmark_{i:00}";
                point.transform.SetParent(transform, false);
                point.transform.localScale = Vector3.one * PointScale;
                point.GetComponent<Renderer>().sharedMaterial = _pointMaterial;
                point.SetActive(false);
                _points[i] = point;
            }
        }

        private void LateUpdate()
        {
            if (_adapter == null || !_adapter.TryRead(_dto) || !_dto.IsValid || _dto.PointCount == 0)
            {
                SetAllActive(false);
                return;
            }

            for (var i = 0; i < _points.Length; i++)
            {
                if (i < _dto.PointCount)
                {
                    var normalized = _dto.Points[i];
                    _points[i].transform.localPosition = new Vector3(
                        (normalized.x - 0.5f) * WorldWidth,
                        (0.5f - normalized.y) * WorldHeight,
                        -normalized.z * DepthScale);
                    _points[i].SetActive(true);
                }
                else
                {
                    _points[i].SetActive(false);
                }
            }
        }

        private void OnDisable()
        {
            if (_points != null)
            {
                foreach (var t in _points)
                {
                    if (t != null)
                    {
                        Destroy(t);
                    }
                }

                _points = null;
            }

            if (_pointMaterial == null)
            {
                return;
            }
            Destroy(_pointMaterial);
            _pointMaterial = null;
        }

        private void SetAllActive(bool active)
        {
            if (_points == null)
            {
                return;
            }

            foreach (var t in _points)
            {
                if (t != null && t.activeSelf != active)
                {
                    t.SetActive(active);
                }
            }
        }
    }
}
