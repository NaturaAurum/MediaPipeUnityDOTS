using MediaPipeUnityDots.Sample.HandTracking.Scripts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace MediaPipeUnityDots.Tests.EditMode
{
    /// <summary>
    /// DepthSettingsPanel의 바인딩·동기화·해제 계약 검증.
    /// 배치 EditMode(6000.6)에서는 UI 이벤트가 디스패치되지 않아 값 변경→푸시 경로는 검사하지 않는다.
    /// 같은 이유로 기존 OneEuroFilterSettingsPanelTests 5개도 clean develop에서 실패한다.
    /// </summary>
    public sealed class DepthSettingsPanelTests
    {
        private GameObject _go;
        private DepthSettingsPanel _panel;
        private VisualElement _root;
        private VisualElement _panelElement;
        private Toggle _toggle;
        private Slider _weightSlider;
        private Slider _gainSlider;
        private Slider _maxOffsetSlider;
        private Button _resetButton;
        private Label _statusLabel;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TestDepthSettingsPanel");
            _panel = _go.AddComponent<DepthSettingsPanel>();

            _root = new VisualElement();
            _panelElement = new VisualElement { name = "depth-settings-panel" };

            _toggle = new Toggle { name = "depth-enabled-toggle" };
            _weightSlider = new Slider { name = "depth-weight-slider", lowValue = 0f, highValue = 1f, value = 0f };
            _gainSlider = new Slider { name = "depth-gain-slider", lowValue = 0f, highValue = 5f, value = 1f };
            _maxOffsetSlider = new Slider { name = "depth-max-offset-slider", lowValue = 0f, highValue = 0.5f, value = 0.1f };
            _resetButton = new Button { name = "depth-reset-defaults-button" };
            _statusLabel = new Label { name = "depth-status-label" };

            _panelElement.Add(_toggle);
            _panelElement.Add(_weightSlider);
            _panelElement.Add(_gainSlider);
            _panelElement.Add(_maxOffsetSlider);
            _panelElement.Add(_resetButton);
            _panelElement.Add(_statusLabel);
            _root.Add(_panelElement);
        }

        [TearDown]
        public void TearDown()
        {
            _panel.UnbindEvents();
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void BindToRoot_DiscoversControlsAndSyncsDefaults()
        {
            _toggle.value = true;
            _weightSlider.value = 0.9f;

            _panel.BindToRoot(_root);

            Assert.IsFalse(_toggle.value, "defaults must overwrite staged UI values without notify");
            Assert.AreEqual(0f, _weightSlider.value, 1e-5f);
            Assert.AreEqual(1f, _gainSlider.value, 1e-5f);
            Assert.AreEqual(0.1f, _maxOffsetSlider.value, 1e-5f);
            Assert.AreEqual("상태: 비활성", _statusLabel.text);
        }

        [Test]
        public void UnbindThenRebind_RediscoversWithoutThrow()
        {
            _panel.BindToRoot(_root);
            _panel.UnbindEvents();
            _panel.UnbindEvents();

            _root.Add(_panelElement);
            _panel.BindToRoot(_root);

            Assert.AreEqual("상태: 비활성", _statusLabel.text);
        }

        [Test]
        public void BindToRoot_NullRoot_DoesNotThrow()
        {
            _panel.BindToRoot(null);
            _panel.UnbindEvents();
        }
    }
}
