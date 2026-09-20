using MediaPipeUnityDots.Runtime.Ecs;
using MediaPipeUnityDots.Sample.HandTracking.Scripts;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace MediaPipeUnityDots.Tests.EditMode
{
    /// <summary>
    /// 실제 EditorWindow에 연결된 UI Toolkit 컨트롤이 필터 설정 싱글턴과 로그 스위치를 바꾸는지 검증한다.
    /// </summary>
    public sealed class OneEuroFilterSettingsPanelTests
    {
        private GameObject _go;
        private OneEuroFilterSettingsPanel _panel;
        private AttachedPanelTestWindow _window;
        private World _world;
        private World _previousWorld;
        private Entity _settingsEntity;
        private bool _previousLoggingEnabled;
        private VisualElement _root;
        private VisualElement _panelElement;
        private Toggle _toggle;
        private Slider _handMinCutoff;
        private Button _resetButton;
        private Toggle _verboseLoggingToggle;
        private Toggle _renderModeToggle;

        [SetUp]
        public void SetUp()
        {
            _previousWorld = World.DefaultGameObjectInjectionWorld;
            _world = new World("OneEuroFilterSettingsPanelTests");
            World.DefaultGameObjectInjectionWorld = _world;
            _settingsEntity = _world.EntityManager.CreateEntity(typeof(OneEuroFilterSettings));
            _world.EntityManager.SetComponentData(_settingsEntity, OneEuroFilterSettings.Default);

            _previousLoggingEnabled = MpudLog.Enabled;
            MpudLog.Enabled = false;

            _go = new GameObject("TestSettingsPanel");
            _go.SetActive(false);
            _panel = _go.AddComponent<OneEuroFilterSettingsPanel>();

            _window = AttachedPanelTestWindow.Open();
            _root = _window.rootVisualElement;
            _panelElement = new VisualElement { name = "filter-settings-panel" };

            _toggle = new Toggle { name = "filter-enabled-toggle" };
            _handMinCutoff = new Slider
            {
                name = "hand-min-cutoff",
                lowValue = 0.1f,
                highValue = 5f,
                value = 1f,
            };
            _resetButton = new Button { name = "reset-defaults-button" };
            _verboseLoggingToggle = new Toggle { name = "verbose-logging-toggle" };
            _renderModeToggle = new Toggle { name = "render-mode-toggle" };

            _panelElement.Add(_toggle);
            _panelElement.Add(_handMinCutoff);
            _panelElement.Add(_resetButton);
            _panelElement.Add(_verboseLoggingToggle);
            _panelElement.Add(_renderModeToggle);
            _root.Add(_panelElement);
        }

        [TearDown]
        public void TearDown()
        {
            if (_panel != null)
            {
                _panel.UnbindEvents();
            }

            if (_window != null)
            {
                _window.Close();
            }

            if (_go != null)
            {
                Object.DestroyImmediate(_go);
            }

            MpudLog.Enabled = _previousLoggingEnabled;
            if (World.DefaultGameObjectInjectionWorld == _world)
            {
                World.DefaultGameObjectInjectionWorld = _previousWorld;
            }

            if (_world != null && _world.IsCreated)
            {
                _world.Dispose();
            }
        }

        [Test]
        public void AttachedControlChanges_UpdateFilterSettingsInEcs()
        {
            _panel.BindToRoot(_root);

            _handMinCutoff.value = 2.5f;
            Assert.AreEqual(2.5f, ReadSettings().HandMinCutoff, 1e-5f);

            _toggle.value = false;
            Assert.AreEqual(0, ReadSettings().Enabled);
        }

        [Test]
        public void UnbindAndRebind_ControlsStopAndResumeUpdatingEcs()
        {
            _panel.BindToRoot(_root);
            _handMinCutoff.value = 1.5f;
            Assert.AreEqual(1.5f, ReadSettings().HandMinCutoff, 1e-5f);

            _panel.UnbindEvents();
            _handMinCutoff.value = 2.0f;
            Assert.AreEqual(1.5f, ReadSettings().HandMinCutoff, 1e-5f);

            _root.Add(_panelElement);
            _panel.BindToRoot(_root);
            _handMinCutoff.value = 2.0f;
            Assert.AreEqual(2.0f, ReadSettings().HandMinCutoff, 1e-5f);
        }

        [Test]
        public void ResetButton_RestoresFilterDefaultsInEcsAndControls()
        {
            _panel.BindToRoot(_root);
            _handMinCutoff.value = 4.0f;
            _renderModeToggle.value = true;
            Assert.AreEqual(4.0f, ReadSettings().HandMinCutoff, 1e-5f);
            Assert.AreEqual(1, ReadSettings().RenderMode);

            _resetButton.Focus();
            using var evt = NavigationSubmitEvent.GetPooled();
            evt.target = _resetButton;
            _resetButton.SendEvent(evt);

            var defaults = OneEuroFilterSettings.Default;
            var settings = ReadSettings();
            Assert.AreEqual(defaults.Enabled, settings.Enabled);
            Assert.AreEqual(defaults.RenderMode, settings.RenderMode);
            Assert.AreEqual(defaults.HandMinCutoff, settings.HandMinCutoff, 1e-5f);
            Assert.AreEqual(defaults.HandMinCutoff, _handMinCutoff.value, 1e-5f);
            Assert.IsFalse(_renderModeToggle.value);
        }

        [Test]
        public void RenderModeToggle_UpdatesFilterSettingsInEcs()
        {
            _panel.BindToRoot(_root);

            _renderModeToggle.value = true;

            Assert.AreEqual(1, ReadSettings().RenderMode);
        }

        [Test]
        public void VerboseLoggingToggle_ChangesLoggingSwitch()
        {
            _panel.BindToRoot(_root);

            _verboseLoggingToggle.value = true;
            Assert.IsTrue(MpudLog.Enabled);

            _verboseLoggingToggle.value = false;
            Assert.IsFalse(MpudLog.Enabled);
        }

        private OneEuroFilterSettings ReadSettings()
        {
            return _world.EntityManager.GetComponentData<OneEuroFilterSettings>(_settingsEntity);
        }
    }
}
