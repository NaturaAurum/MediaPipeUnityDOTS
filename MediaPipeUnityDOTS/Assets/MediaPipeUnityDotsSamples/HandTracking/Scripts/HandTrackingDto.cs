using UnityEngine;

namespace MediaPipeUnityDotsSamples.HandTracking
{
    /// <summary>
    /// ECS 싱글턴/버퍼에서 읽어 visualizer와 UI에 전달하는 plain DTO.
    /// native 메모리와 ECS 메모리를 외부에 노출하지 않으며, 배열은 생성 시 1회 할당 후 재사용한다.
    /// </summary>
    public sealed class HandTrackingDto
    {
        public const int LandmarkCapacity = 21;

        public readonly Vector3[] Points = new Vector3[LandmarkCapacity];

        public int PointCount;
        public bool IsValid;
        public int Handedness = -1;
        public float Score;
        public long TimestampUs;
        public long FrameCount;
    }
}
