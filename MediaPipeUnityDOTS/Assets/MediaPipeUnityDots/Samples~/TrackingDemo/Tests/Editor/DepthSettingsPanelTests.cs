using MediaPipeUnityDots.Runtime.Ecs;
using MediaPipeUnityDots.Sample.HandTracking.Scripts;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace MediaPipeUnityDots.Tests.EditMode
{
    /// <summary>
    /// 실제 EditorWindow에 연결된 UI Toolkit 컨트롤이 깊이 보정 설정 싱글턴을 바꾸는지 검증한다.
    /// </summary>
    public sealed class DepthSettingsPanelTests
    {
        private GameObject _go;
        private DepthSettingsPanel _panel;
        private AttachedPanelTestWindow _window;
        private World _world;
        private World _previousWorld;
        private Entity _settingsEntity;
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
            _previousWorld = World.DefaultGameObjectInjectionWorld;
            _world = new World("DepthSettingsPanelTests");
            World.DefaultGameObjectInjectionWorld = _world;
            _settingsEntity = _world.EntityManager.CreateEntity(typeof(DepthSettings));
            _world.EntityManager.SetComponentData(_settingsEntity, DepthSettings.Default);

            _go = new GameObject("TestDepthSettingsPanel");
            _go.SetActive(false);
            _panel = _go.AddComponent<DepthSettingsPanel>();

            _window = AttachedPanelTestWindow.Open();
            _root = _window.rootVisualElement;
            _panelElement = new VisualElement { name = "depth-settings-panel" };

            _toggle = new Toggle { name = "depth-enabled-toggle" };
            _weightSlider = new Slider
            {
                name = "depth-weight-slider",
                lowValue = 0f,
                highValue = 1f,
                value = 0f,
            };
            _gainSlider = new Slider
            {
                name = "depth-gain-slider",
                lowValue = 0f,
                highValue = 5f,
                value = 1f,
            };
            _maxOffsetSlider = new Slider
            {
                name = "depth-max-offset-slider",
                lowValue = 0f,
                highValue = 0.5f,
                value = 0.1f,
            };
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
        public void AttachedControls_UpdateDepthSettingsInEcs()
        {
            _panel.BindToRoot(_root);

            _toggle.value = true;
            _weightSlider.value = 0.75f;
            _gainSlider.value = 2.5f;
            _maxOffsetSlider.value = 0.25f;

            var settings = ReadSettings();
            Assert.AreEqual(1, settings.Enabled);
            Assert.AreEqual(0.75f, settings.Weight, 1e-5f);
            Assert.AreEqual(2.5f, settings.DepthGain, 1e-5f);
            Assert.AreEqual(0.25f, settings.MaxOffset, 1e-5f);
        }

        [Test]
        public void UnbindAndRebind_ControlsStopAndResumeUpdatingEcs()
        {
            _panel.BindToRoot(_root);
            _weightSlider.value = 0.4f;
            Assert.AreEqual(0.4f, ReadSettings().Weight, 1e-5f);

            _panel.UnbindEvents();
            _weightSlider.value = 0.8f;
            Assert.AreEqual(0.4f, ReadSettings().Weight, 1e-5f);

            _root.Add(_panelElement);
            _panel.BindToRoot(_root);
            _weightSlider.value = 0.8f;
            Assert.AreEqual(0.8f, ReadSettings().Weight, 1e-5f);
        }

        [Test]
        public void ResetButton_RestoresDepthDefaultsInEcsAndControls()
        {
            _panel.BindToRoot(_root);
            _toggle.value = true;
            _weightSlider.value = 0.75f;
            _gainSlider.value = 2.5f;
            _maxOffsetSlider.value = 0.25f;

            _resetButton.Focus();
            using var evt = NavigationSubmitEvent.GetPooled();
            evt.target = _resetButton;
            _resetButton.SendEvent(evt);

            var defaults = DepthSettings.Default;
            var settings = ReadSettings();
            Assert.AreEqual(defaults.Enabled, settings.Enabled);
            Assert.AreEqual(defaults.Weight, settings.Weight, 1e-5f);
            Assert.AreEqual(defaults.DepthGain, settings.DepthGain, 1e-5f);
            Assert.AreEqual(defaults.MaxOffset, settings.MaxOffset, 1e-5f);
            Assert.IsFalse(_toggle.value);
            Assert.AreEqual(defaults.Weight, _weightSlider.value, 1e-5f);
            Assert.AreEqual(defaults.DepthGain, _gainSlider.value, 1e-5f);
            Assert.AreEqual(defaults.MaxOffset, _maxOffsetSlider.value, 1e-5f);
        }

        private DepthSettings ReadSettings()
        {
            return _world.EntityManager.GetComponentData<DepthSettings>(_settingsEntity);
        }
    }
}
