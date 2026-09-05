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
                Debug.LogError("[MPUD] WebcamBackgroundToggle references are not wired in the scene.");
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

            _toggle = null;
        }
        // panel과 동일하게 version 무시 리바인딩.
        private void OnUIReload(PanelRenderer renderer, VisualElement root, int version)
        {
            _toggle = root.Q<Toggle>(ToggleElementName);
            if (_toggle == null)
            {
                Debug.LogError("[MPUD] webcam-toggle element was not found in WebcamToggle.uxml.");
                return;
            }

            _toggle.SetValueWithoutNotify(true);
            _toggle.RegisterValueChangedCallback(OnToggleChanged);
        }

        private void OnToggleChanged(ChangeEvent<bool> evt)
        {
            if (_renderer != null)
            {
                _renderer.SetVisible(evt.newValue);
            }
        }
    }
}
