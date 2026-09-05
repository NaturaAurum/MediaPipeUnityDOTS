using UnityEngine;

namespace MediaPipeUnityDots
{
    /// <summary>
    /// MPUD 로그 단일 진입점. Info는 Enabled일 때만 기록한다.
    /// 보간 문자열은 호출 전에 평가되므로 프레임당 호출부는
    /// if (MpudLog.Enabled) 가드로 감싸야 GC 0이 보장된다.
    /// 경고/에러는 항상 기록한다.
    /// </summary>
    public static class MpudLog
    {
        public static bool Enabled;

        public static void Log(string message)
        {
            if (Enabled)
            {
                Debug.Log(message);
            }
        }

        public static void Warning(string message)
        {
            Debug.LogWarning(message);
        }

        public static void Error(string message)
        {
            Debug.LogError(message);
        }
    }
}
