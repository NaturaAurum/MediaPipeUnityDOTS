using MediaPipeUnityDots.Sample.HandTracking.Scripts;
using MediaPipeUnityDots.Runtime.Logging;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace MediaPipeUnityDots.Tests.EditMode
{
    /// <summary>
    /// OneEuroFilterSettingsPanel의 UI 이벤트 라이프사이클(바인드/언바인드/재바인드)을 검증한다.
    /// 패널 비활성화 시 콜백이 해제되어 추가 푸시가 없어야 하며,
    /// 재활성화 후에도 이벤트당 정확히 1회만 푸시되어 구독 누적이 없어야 한다.
    /// </summary>
    public sealed class OneEuroFilterSettingsPanelTests
    {
        private GameObject _go;
        private OneEuroFilterSettingsPanel _panel;
        private VisualElement _root;
        private VisualElement _panelElement;
        private Toggle _toggle;
        private Slider _handMinCutoff;
        private Slider _handBeta;
        private Button _resetButton;
        private Toggle _verboseLoggingToggle;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TestSettingsPanel");
            _panel = _go.AddComponent<OneEuroFilterSettingsPanel>();

            _root = new VisualElement();
            _panelElement = new VisualElement { name = "filter-settings-panel" };

            _toggle = new Toggle { name = "filter-enabled-toggle" };
            _handMinCutoff = new Slider { name = "hand-min-cutoff", lowValue = 0.1f, highValue = 5f, value = 1f };
            _handBeta = new Slider { name = "hand-beta", lowValue = 0.001f, highValue = 0.05f, value = 0.007f };
            _resetButton = new Button { name = "reset-defaults-button" };
            _verboseLoggingToggle = new Toggle { name = "verbose-logging-toggle" };

            _panelElement.Add(_toggle);
            _panelElement.Add(_handMinCutoff);
            _panelElement.Add(_handBeta);
            _panelElement.Add(_resetButton);
            _panelElement.Add(_verboseLoggingToggle);
            _root.Add(_panelElement);
        }

        [TearDown]
        public void TearDown()
        {
            _panel.UnbindEvents();
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void ValueChange_TriggersPush_WhenBound()
        {
            _panel.BindToRoot(_root);
            var initialPushes = _panel.PushCount;

            _handMinCutoff.value = 2.5f;
            Assert.AreEqual(initialPushes + 1, _panel.PushCount);

            _toggle.value = false;
            Assert.AreEqual(initialPushes + 2, _panel.PushCount);
        }

        [Test]
        public void UnbindEvents_DetachesCallbacks_NoPushesAfterDisable()
        {
            _panel.BindToRoot(_root);
            var initialPushes = _panel.PushCount;

            _panel.UnbindEvents();

            _handMinCutoff.value = 3.0f;
            _toggle.value = false;

            Assert.AreEqual(initialPushes, _panel.PushCount, "Unbound controls must not trigger ECS pushes.");
        }

        [Test]
        public void EnableDisableEnable_DoesNotAccumulateSubscriptions()
        {
            // First bind (Enable)
            _panel.BindToRoot(_root);
            var baseline = _panel.PushCount;

            _handMinCutoff.value = 1.5f;
            Assert.AreEqual(baseline + 1, _panel.PushCount);

            // Unbind (Disable)
            _panel.UnbindEvents();
            _root.Add(_panelElement); // Re-add panel element to root since Unbind removed it

            // Second bind (Re-enable)
            _panel.BindToRoot(_root);
            var afterRebind = _panel.PushCount;

            // Single change must trigger exactly 1 push, not 2
            _handMinCutoff.value = 2.0f;
            Assert.AreEqual(afterRebind + 1, _panel.PushCount, "Re-bound control must trigger exactly 1 push per change.");
        }

        [Test]
        public void ResetButton_PushesDefaults()
        {
            _panel.BindToRoot(_root);
            _handMinCutoff.value = 4.0f;
            var pushesBeforeReset = _panel.PushCount;

            // Simulate button click
            using var evt = ClickEvent.GetPooled();
            evt.target = _resetButton;
            _resetButton.SendEvent(evt);

            Assert.AreEqual(pushesBeforeReset + 1, _panel.PushCount);
            Assert.AreEqual(1.0f, _handMinCutoff.value, 1e-5f);
        }

        [Test]
        public void VerboseLoggingToggle_FlipsLogServiceFlag()
        {
            _panel.BindToRoot(_root);
            _verboseLoggingToggle.value = true;
            Assert.IsTrue(MpudLogService.Enabled);
            _verboseLoggingToggle.value = false;
            Assert.IsFalse(MpudLogService.Enabled);
        }
    }
}
