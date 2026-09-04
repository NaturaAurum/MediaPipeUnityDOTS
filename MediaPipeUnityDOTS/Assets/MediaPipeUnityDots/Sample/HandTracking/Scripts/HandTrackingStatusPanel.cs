using UnityEngine;
using UnityEngine.UIElements;

namespace MediaPipeUnityDots.Sample.HandTracking.Scripts
{
    /// <summary>
    /// tracker 상태/프레임/타임스탬프/handedness/confidence를 보여주는 UI Toolkit 패널.
    /// 레이아웃은 StatusPanel.uxml/uss, C#은 바인딩만 담당한다.
    /// adapter DTO만 읽고 EntityManager에 직접 접근하지 않는다.
    /// adapter/provider/panelRenderer는 씬에서 직렬화 참조로 배선한다.
    /// </summary>
    public sealed class HandTrackingStatusPanel : MonoBehaviour
    {
        [SerializeField]
        private HandTrackingAdapter _adapter;
        [SerializeField]
        private WebcamFrameProvider _provider;
        [SerializeField]
        private PanelRenderer _panelRenderer;

        private readonly HandTrackingDto _dto = new HandTrackingDto();

        private Label _stateLabel;
        private Label _frameLabel;
        private Label _timestampLabel;
        private Label _handednessLabel;
        private Label _confidenceLabel;

        private void OnEnable()
        {
            if (_adapter == null || _provider == null || _panelRenderer == null)
            {
                Debug.LogError("[MPUD] HandTrackingStatusPanel references are not wired in the scene.");
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

            _stateLabel = null;
            _frameLabel = null;
            _timestampLabel = null;
            _handednessLabel = null;
            _confidenceLabel = null;
        }

        // version은 무시하고 매번 리바인딩한다. OnDisable에서 참조를 비우므로 가드 시 stale 위험이 있다.
        private void OnUIReload(PanelRenderer renderer, VisualElement root, int version)
        {
            _stateLabel = root.Q<Label>("state-label");
            _frameLabel = root.Q<Label>("frame-label");
            _timestampLabel = root.Q<Label>("timestamp-label");
            _handednessLabel = root.Q<Label>("hand-label");
            _confidenceLabel = root.Q<Label>("confidence-label");

            var resetButton = root.Q<Button>("reset-button");
            if (resetButton != null)
            {
                resetButton.clicked += OnResetClicked;
            }

            if (_stateLabel == null || _frameLabel == null || _timestampLabel == null
                || _handednessLabel == null || _confidenceLabel == null || resetButton == null)
            {
                Debug.LogError("[MPUD] StatusPanel.uxml is missing expected elements.");
            }
        }

        private void LateUpdate()
        {
            if (_stateLabel == null || _adapter == null || !_adapter.TryRead(_dto))
            {
                return;
            }

            _stateLabel.text = $"state: {(_dto.IsValid ? "tracking" : "idle")}";
            _frameLabel.text = $"frame: {_dto.FrameCount}";
            _timestampLabel.text = $"timestamp: {_dto.TimestampUs} us";
            _handednessLabel.text = $"hand: {HandednessText(_dto.Handedness)}";
            _confidenceLabel.text = $"confidence: {_dto.Score:F2} ({_dto.PointCount} pts)";
        }

        private void OnResetClicked()
        {
            if (_provider != null)
            {
                _provider.ResetTracker();
            }
        }

        private static string HandednessText(int handedness) => handedness switch
        {
            0 => "Left",
            1 => "Right",
            _ => "-",
        };
    }
}
