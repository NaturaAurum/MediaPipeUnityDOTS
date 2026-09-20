using UnityEngine;

namespace MediaPipeUnityDots.Sample.LandmarkApi
{
    /// <summary>실제 Player에서 네이티브 추론과 공개 API를 확인한다.</summary>
    public sealed class LandmarkApiPlayerSmoke : MonoBehaviour
    {
        private LandmarkApiSmoke _smoke;

        private void Start()
        {
            _smoke = LandmarkApiSmoke.CreateDefault(Debug.Log, (success, error) =>
            {
                if (!success)
                    Debug.LogError("[MPUD CONSUMER SMOKE] PLAYER_FAIL " + error);
                else
                    Debug.Log("[MPUD CONSUMER SMOKE] PLAYER_PASS");
                Application.Quit(success ? 0 : 1);
            });
            _smoke.Start();
        }

        private void Update() => _smoke?.Tick();
        private void OnDestroy() => _smoke?.Dispose();
    }
}
