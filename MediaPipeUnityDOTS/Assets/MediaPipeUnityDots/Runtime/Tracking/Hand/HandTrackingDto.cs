using UnityEngine;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// ECS 싱글턴/버퍼에서 읽어 visualizer와 UI에 전달하는 plain DTO.
    /// native 메모리와 ECS 메모리를 외부에 노출하지 않으며, 배열은 생성 시 1회 할당 후 재사용한다.
    /// Points는 손 우선(hand-major) 평탄 배열이다.
    /// </summary>
    public sealed class HandTrackingDto
    {
        public const int MaxHands = 4;
        public const int LandmarkCapacity = 21;

        public readonly Vector3[] Points = new Vector3[MaxHands * LandmarkCapacity];
        public readonly int[] Handedness = new int[MaxHands];
        public readonly float[] Scores = new float[MaxHands];
        public readonly int[] PointCounts = new int[MaxHands];

        public int HandCount;
        public int PointCount;
        public bool IsValid;
        public long TimestampUs;
        public long FrameCount;

        public HandTrackingDto()
        {
            for (var h = 0; h < MaxHands; h++)
            {
                Handedness[h] = -1;
            }
        }
    }
}
