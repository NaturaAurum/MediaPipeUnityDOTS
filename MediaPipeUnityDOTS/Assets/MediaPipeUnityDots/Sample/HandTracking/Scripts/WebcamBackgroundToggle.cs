using UnityEngine;
using UnityEngine.UIElements;

namespace MediaPipeUnityDots.Sample.HandTracking.Scripts
{
    /// <summary>
    /// 웹캠 배경 on/off 토글. 레이아웃은 WebcamToggle.uxml/uss, C#은 바인딩만 담당한다.
    /// renderer/panelRenderer는 씬에서 직렬화 참조로 배선한다.
    /// </summary>
    public sealed class WebcamBackgroundToggle : MonoBehaviour
    {
        private const string ToggleElementName = "webcam-toggle";

        [SerializeField]
        private WebcamBackgroundRenderer _renderer;
        [SerializeField]
        private PanelRenderer _panelRenderer;

        private Toggle _toggle;

        private void OnEnable()
        {
            if (_renderer == null || _panelRenderer == null)
            {
                MpudLog.Error("[MPUD] WebcamBackgroundToggle references are not wired in the scene.");
                enabled = false;
                return;
            }

            _panelRenderer.RegisterUIReloadCallback(OnUIReload);
        }

        private void OnDisable()
        {
            if (_panelRenderer != null)
            {
                _panelRenderer.UnregisterUIReloadCallback(OnUIReload);
            }

            UnbindEvents();
        }

        private void OnUIReload(PanelRenderer renderer, VisualElement root, int version)
        {
            UnbindEvents();

            if (root == null)
            {
                return;
            }

            // 투명 캔버스가 하위 패널의 마우스 클릭을 가로채지 않도록 Ignore 처리
            root.pickingMode = PickingMode.Ignore;

            _toggle = root.Q<Toggle>(ToggleElementName);
            if (_toggle == null)
            {
                MpudLog.Error("[MPUD] webcam-toggle element was not found in WebcamToggle.uxml.");
                return;
            }

            var currentVisible = _renderer != null && _renderer.IsVisible;
            _toggle.SetValueWithoutNotify(currentVisible);
            _toggle.RegisterValueChangedCallback(OnToggleChanged);
        }

        private void UnbindEvents()
        {
            if (_toggle != null)
            {
                _toggle.UnregisterValueChangedCallback(OnToggleChanged);
                _toggle = null;
            }
        }

        private void OnToggleChanged(ChangeEvent<bool> evt)
        {
            MpudLog.Log($"[MPUD] Webcam background visible: {evt.newValue}");
            if (_renderer != null)
            {
                _renderer.SetVisible(evt.newValue);
            }
        }
    }
}
