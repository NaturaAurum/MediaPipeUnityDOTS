using UnityEngine;
using UnityEngine.UIElements;

namespace MediaPipeUnityDots.Sample.HandTracking.Scripts
{
    /// <summary>
    /// tracker 상태/프레임/타임스탬프/handedness/confidence를 보여주는 UI Toolkit 패널.
    /// UXML/USS 에셋 없이 코드로 VisualElement를 구성하므로 씬 에셋 변경이 필요 없다.
    /// adapter DTO만 읽고 EntityManager에 직접 접근하지 않는다.
    /// </summary>
    /// UIDocument는 씬에 미리 배치하지 않고 OnEnable에서 추가한다.
    /// UnityEngine 내장 스크립트 GUID를 씬 YAML에 하드코딩하지 않기 위함이다.
    public sealed class HandTrackingStatusPanel : MonoBehaviour
    {
        private HandTrackingAdapter _adapter;
        private WebcamFrameProvider _provider;
        private readonly HandTrackingDto _dto = new HandTrackingDto();

        private Label _stateLabel;
        private Label _frameLabel;
        private Label _timestampLabel;
        private Label _handednessLabel;
        private Label _confidenceLabel;

        private void OnEnable()
        {
            _adapter = FindAnyObjectByType<HandTrackingAdapter>();
            _provider = FindAnyObjectByType<WebcamFrameProvider>();

            var document = GetComponent<UIDocument>();
            if (document == null)
            {
                document = gameObject.AddComponent<UIDocument>();
            }
            if (document.panelSettings == null)
            {
                document.panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            }

            var root = document.rootVisualElement;
            root.Clear();

            var panel = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    top = 10,
                    left = 10,
                    paddingTop = 8,
                    paddingBottom = 8,
                    paddingLeft = 10,
                    paddingRight = 10,
                    backgroundColor = new Color(0f, 0f, 0f, 0.6f),
                },
            };

            _stateLabel = AddLabel(panel, "state: -");
            _frameLabel = AddLabel(panel, "frame: -");
            _timestampLabel = AddLabel(panel, "timestamp: -");
            _handednessLabel = AddLabel(panel, "hand: -");
            _confidenceLabel = AddLabel(panel, "confidence: -");

            var resetButton = new Button(OnResetClicked)
            {
                text = "Reset Tracker",
            };
            panel.Add(resetButton);

            root.Add(panel);
        }

        private void LateUpdate()
        {
            if (_adapter == null || !_adapter.TryRead(_dto))
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

        private static Label AddLabel(VisualElement parent, string text)
        {
            var label = new Label(text)
            {
                style = { color = Color.white, fontSize = 13 },
            };
            parent.Add(label);
            return label;
        }

        private static string HandednessText(int handedness) => handedness switch
        {
            0 => "Left",
            1 => "Right",
            _ => "-",
        };
    }
}
