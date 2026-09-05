namespace MediaPipeUnityDots.Runtime.Logging
{
    /// <summary>
    /// 주기성 MPUD 로그의 중앙 On/Off. Enabled=false가 기본이다.
    /// 문자열 보간은 호출 전에 평가되므로 호출부에서 반드시
    /// if (MpudLogService.Enabled) 가드로 감싸야 GC 0이 보장된다.
    /// 경고/에러는 항상 기록한다.
    /// </summary>
    public static class MpudLogService
    {
        public static bool Enabled;
    }
}
