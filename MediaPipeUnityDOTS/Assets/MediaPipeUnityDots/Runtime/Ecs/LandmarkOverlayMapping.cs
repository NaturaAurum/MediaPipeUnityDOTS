using Unity.Entities;
using Unity.Mathematics;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 배경 Quad 픽셀과 랜드마크의 정합 매핑. WebcamBackgroundRenderer가 매 프레임 기록한다.
    /// (x, y)는 submit 이미지 기준 정규화 좌표다. z는 쓰지 않고 Quad 앞 평면에 올린다.
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

        // Quad와 겹쳐 z-fighting이 나지 않게 카메라 쪽으로 띄운다.
        private const float OverlayEpsilon = 0.05f;

        public static float3 Map(float x, float y, in LandmarkOverlayMapping mapping)
        {
            var u = (x - mapping.UvOffsetX) / mapping.UvScaleX;
            // 리더는 반전 없이 직접 인덱싱한다(row r = array[r]).
            // flip=false면 y가 배열 분율 그대로(y=j), flip=true면 뒤집힌 배열에서 읽으므로(y=1-j).
            // 배경 샘플링(vt → array fraction vt)을 역연산하면 아래 식이 된다.
            var textureV = (mapping.Flipped != 0 ? 1f - y : y) - mapping.UvOffsetY;
            var v = textureV / mapping.UvScaleY;
            return mapping.Origin
                + (u - 0.5f) * mapping.AxisX
                + (v - 0.5f) * mapping.AxisY
                - mapping.Forward * OverlayEpsilon;
        }

        /// <summary>
        /// 월드 랜드마크(미터)를 Unity 좌표로 옮긴다.
        /// 기준점 상대 오프셋만 쓰므로 월드 원점 규약(고관절 중심 등)에 무관하다.
        /// z부호가 뒤집혀 보이면 Forward 항만 뒤집으면 된다.
        /// </summary>
        public static float3 MapWorld(
            float3 world,
            float3 worldCenter,
            float3 anchorPos,
            float3 right,
            float3 up,
            float3 forward,
            float scale)
        {
            return anchorPos
                + right * ((world.x - worldCenter.x) * scale)
                + up * ((world.y - worldCenter.y) * scale)
                + forward * ((world.z - worldCenter.z) * scale - OverlayEpsilon);
        }
    }
}
