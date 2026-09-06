using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 표시 파이프라인이 공유하는 트래커 구분. Burst 호환 unmanaged enum이다.
    /// </summary>
    public enum LandmarkTracker : int
    {
        Hand = 0,
        Face = 1,
        Pose = 2,
    }

    /// <summary>
    /// 트래커 공통 포인트 태그. 트래커별 포인트 태그(Hand/Face/PoseLandmarkPoint)를 대체한다.
    /// </summary>
    public struct LandmarkPoint : IComponentData
    {
        public LandmarkTracker Tracker;
        public int Target;
        public int Index;
    }

    /// <summary>
    /// 포인트 수를 제공하는 추적 소스. 스포너(MonoBehaviour) 전용이며 Burst/Jobs에서 쓰지 않는다.
    /// </summary>
    public interface IPointSource
    {
        int MaxTargets { get; }
    }
}
