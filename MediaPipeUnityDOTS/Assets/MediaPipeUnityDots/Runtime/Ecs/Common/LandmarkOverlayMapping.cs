using Unity.Entities;
using Unity.Mathematics;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 배경 Quad 픽셀과 랜드마크의 정합 매핑. WebcamBackgroundRenderer가 매 프레임 기록한다.
    /// XY는 submit 이미지 기준 정규화 좌표, 깊이는 표시용 Unity 단위다.
    /// </summary>
    public struct LandmarkOverlayMapping : IComponentData
    {
        public int IsValid;
        public int Flipped;
        public float UvScaleX;
        public float UvOffsetX;
        public float UvScaleY;
        public float UvOffsetY;
        public float3 Origin;
        public float3 AxisX;
        public float3 AxisY;
        public float3 Forward;
        public float3 CameraPosition;
        public float NearClipPlane;
        public int IsPerspective;

        // Quad와 겹쳐 z-fighting이 나지 않게 카메라 쪽으로 띄운다.
        private const float OverlayEpsilon = 0.05f;
        private const float MinimumWorldSpan = 1e-6f;

        public static float3 Map(float x, float y, in LandmarkOverlayMapping mapping)
            => MapWithDepth(x, y, 0f, in mapping);

        /// <summary>
        /// 깊이를 바꿔도 배경 영상의 같은 픽셀에 투영한다. 음수 깊이는 카메라 쪽이다.
        /// </summary>
        public static float3 MapWithDepth(float x, float y, float depth, in LandmarkOverlayMapping mapping)
        {
            var u = (x - mapping.UvOffsetX) / mapping.UvScaleX;
            // 리더는 반전 없이 직접 인덱싱한다(row r = array[r]).
            // flip=false면 y가 배열 분율 그대로(y=j), flip=true면 뒤집힌 배열에서 읽으므로(y=1-j).
            // 배경 샘플링(vt → array fraction vt)을 역연산하면 아래 식이 된다.
            var textureV = (mapping.Flipped != 0 ? 1f - y : y) - mapping.UvOffsetY;
            var v = textureV / mapping.UvScaleY;
            var plane = mapping.Origin
                + (u - 0.5f) * mapping.AxisX
                + (v - 0.5f) * mapping.AxisY;
            var planeDepth = math.dot(plane - mapping.CameraPosition, mapping.Forward);
            var offset = depth - OverlayEpsilon;
            if (mapping.NearClipPlane > 0f)
            {
                offset = math.max(offset, mapping.NearClipPlane + OverlayEpsilon - planeDepth);
            }

            if (mapping.IsPerspective != 0 && planeDepth > 0f)
            {
                return mapping.CameraPosition + (plane - mapping.CameraPosition) * (1f + offset / planeDepth);
            }

            return plane + mapping.Forward * offset;
        }

        /// <summary>
        /// 정규화 영상 크기와 월드 XY 크기로 표시용 깊이 배율을 구한다.
        /// 퇴화한 월드 XY에서는 깊이를 펼치지 않는다. 실거리 복원이 아닌 시각화 근사다.
        /// </summary>
        public static float GetDepthScale(float2 imageSpan, float2 worldSpan, in LandmarkOverlayMapping mapping)
        {
            var worldLength = math.length(worldSpan);
            if (worldLength <= MinimumWorldSpan)
            {
                return 0f;
            }

            var imageSize = new float2(
                imageSpan.x * math.length(mapping.AxisX) / mapping.UvScaleX,
                imageSpan.y * math.length(mapping.AxisY) / mapping.UvScaleY);
            return math.length(imageSize) / worldLength;
        }
    }

    // 대상별 유효 쌍만 집계한다. 관리 객체나 임시 NativeArray를 만들지 않는다.
    internal struct LandmarkDepthBounds
    {
        private float2 _imageMin;
        private float2 _imageMax;
        private float2 _worldMin;
        private float2 _worldMax;
        private float _farthestZ;
        private int _count;

        public void Add(float2 image, float3 world)
        {
            if (_count++ == 0)
            {
                _imageMin = _imageMax = image;
                _worldMin = _worldMax = world.xy;
                _farthestZ = world.z;
                return;
            }

            _imageMin = math.min(_imageMin, image);
            _imageMax = math.max(_imageMax, image);
            _worldMin = math.min(_worldMin, world.xy);
            _worldMax = math.max(_worldMax, world.xy);
            _farthestZ = math.max(_farthestZ, world.z);
        }

        public float2 Resolve(in LandmarkOverlayMapping mapping)
            => _count == 0
                ? new float2(0f, float.NaN)
                : new float2(
                    LandmarkOverlayMapping.GetDepthScale(_imageMax - _imageMin, _worldMax - _worldMin, in mapping),
                    _farthestZ);
    }
}
