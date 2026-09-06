using MediaPipeUnityDots.Runtime.Ecs;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace MediaPipeUnityDots.Sample.HandTracking.Scripts
{
    /// <summary>
    /// UI Toolkit을 통해 깊이 보정 설정을 조절하고 ECS 싱글턴에 반영하는 패널.
    /// 레이아웃은 DepthSettings.uxml/uss, C#은 바인딩과 ECS 푸시만 담당한다.
    /// </summary>
    public sealed class DepthSettingsPanel : MonoBehaviour
    {
        [SerializeField]
        private PanelRenderer _panelRenderer;

        [SerializeField]
        private VisualTreeAsset _settingsUxml;

        private DepthSettings _settings = DepthSettings.Default;

        private World _cachedWorld;
        private Entity _settingsEntity;

        private VisualElement _panelElement;
        private Toggle _enabledToggle;
        private Slider _weightSlider;
        private Slider _gainSlider;
        private Slider _maxOffsetSlider;
        private Button _resetButton;
        private Label _statusLabel;

        public int PushCount { get; private set; }

        private void OnEnable()
        {
            if (_panelRenderer == null)
            {
                MpudLog.Error("[MPUD] DepthSettingsPanel: _panelRenderer is not wired.");
                enabled = false;
                return;
            }

            _panelRenderer.RegisterUIReloadCallback(OnUIReload);
            PushSettingsToEcs();
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
            BindToRoot(root);
        }

        public void BindToRoot(VisualElement root)
        {
            UnbindEvents();

            if (root == null)
            {
                return;
            }

            root.pickingMode = PickingMode.Ignore;

            _panelElement = root.Q<VisualElement>("depth-settings-panel");
            if (_panelElement == null && _settingsUxml != null)
            {
                _settingsUxml.CloneTree(root);
                _panelElement = root.Q<VisualElement>("depth-settings-panel");
            }

            if (_panelElement == null)
            {
                return;
            }

            _enabledToggle = _panelElement.Q<Toggle>("depth-enabled-toggle");
            _weightSlider = _panelElement.Q<Slider>("depth-weight-slider");
            _gainSlider = _panelElement.Q<Slider>("depth-gain-slider");
            _maxOffsetSlider = _panelElement.Q<Slider>("depth-max-offset-slider");
            _resetButton = _panelElement.Q<Button>("depth-reset-defaults-button");
            _statusLabel = _panelElement.Q<Label>("depth-status-label");

            SyncUiFromSettings();

            if (_enabledToggle != null)
            {
                _enabledToggle.RegisterValueChangedCallback(OnEnabledToggleChanged);
            }

            if (_weightSlider != null)
            {
                _weightSlider.RegisterValueChangedCallback(OnWeightChanged);
            }

            if (_gainSlider != null)
            {
                _gainSlider.RegisterValueChangedCallback(OnGainChanged);
            }

            if (_maxOffsetSlider != null)
            {
                _maxOffsetSlider.RegisterValueChangedCallback(OnMaxOffsetChanged);
            }

            if (_resetButton != null)
            {
                _resetButton.clicked += OnResetClicked;
            }
        }

        public void UnbindEvents()
        {
            if (_enabledToggle != null)
            {
                _enabledToggle.UnregisterValueChangedCallback(OnEnabledToggleChanged);
                _enabledToggle = null;
            }

            if (_weightSlider != null)
            {
                _weightSlider.UnregisterValueChangedCallback(OnWeightChanged);
                _weightSlider = null;
            }

            if (_gainSlider != null)
            {
                _gainSlider.UnregisterValueChangedCallback(OnGainChanged);
                _gainSlider = null;
            }

            if (_maxOffsetSlider != null)
            {
                _maxOffsetSlider.UnregisterValueChangedCallback(OnMaxOffsetChanged);
                _maxOffsetSlider = null;
            }

            if (_resetButton != null)
            {
                _resetButton.clicked -= OnResetClicked;
                _resetButton = null;
            }

            _statusLabel = null;

            if (_panelElement != null)
            {
                _panelElement.RemoveFromHierarchy();
                _panelElement = null;
            }
        }

        private void OnEnabledToggleChanged(ChangeEvent<bool> evt)
        {
            _settings.Enabled = evt.newValue ? 1 : 0;
            PushSettingsToEcs();
        }

        private void OnWeightChanged(ChangeEvent<float> evt)
        {
            if (!float.IsFinite(evt.newValue))
            {
                return;
            }

            _settings.Weight = Mathf.Clamp01(evt.newValue);
            PushSettingsToEcs();
        }

        private void OnGainChanged(ChangeEvent<float> evt)
        {
            if (!float.IsFinite(evt.newValue))
            {
                return;
            }

            _settings.DepthGain = Mathf.Max(0f, evt.newValue);
            PushSettingsToEcs();
        }

        private void OnMaxOffsetChanged(ChangeEvent<float> evt)
        {
            if (!float.IsFinite(evt.newValue))
            {
                return;
            }

            _settings.MaxOffset = Mathf.Max(0f, evt.newValue);
            PushSettingsToEcs();
        }

        private void OnResetClicked()
        {
            _settings = DepthSettings.Default;
            SyncUiFromSettings();
            PushSettingsToEcs();
        }

        private void SyncUiFromSettings()
        {
            _enabledToggle?.SetValueWithoutNotify(_settings.Enabled != 0);
            _weightSlider?.SetValueWithoutNotify(_settings.Weight);
            _gainSlider?.SetValueWithoutNotify(_settings.DepthGain);
            _maxOffsetSlider?.SetValueWithoutNotify(_settings.MaxOffset);
            RefreshStatusLabel();
        }

        private void RefreshStatusLabel()
        {
            if (_statusLabel == null)
            {
                return;
            }

            if (_settings.Enabled == 0)
            {
                _statusLabel.text = "상태: 비활성";
                return;
            }

            if (TryGetSampleSnapshot(out var valid, out var captureId))
            {
                _statusLabel.text = valid ? $"상태: 유효 capture #{captureId}" : "상태: 샘플 없음";
            }
            else
            {
                _statusLabel.text = "상태: 샘플 없음";
            }
        }

        private void PushSettingsToEcs()
        {
            PushCount++;
            RefreshStatusLabel();
            if (TryGetSettingsEntity(out var entityManager, out var entity))
            {
                entityManager.SetComponentData(entity, _settings);
            }
        }

        private bool TryGetSampleSnapshot(out bool valid, out long captureId)
        {
            valid = false;
            captureId = 0;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world is not { IsCreated: true })
            {
                return false;
            }

            var query = world.EntityManager.CreateEntityQuery(typeof(DepthSampleStatus));
            try
            {
                if (query.CalculateEntityCount() != 1)
                {
                    return false;
                }

                var status = world.EntityManager.GetComponentData<DepthSampleStatus>(query.GetSingletonEntity());
                valid = status.IsValid;
                captureId = status.CaptureId;
                return true;
            }
            finally
            {
                query.Dispose();
            }
        }

        private bool TryGetSettingsEntity(out EntityManager entityManager, out Entity entity)
        {
            entityManager = default;
            entity = Entity.Null;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world is not { IsCreated: true })
            {
                return false;
            }

            if (_cachedWorld != world || _settingsEntity == Entity.Null
                || !world.EntityManager.Exists(_settingsEntity))
            {
                _cachedWorld = world;
                _settingsEntity = Entity.Null;
                var query = world.EntityManager.CreateEntityQuery(typeof(DepthSettings));
                try
                {
                    if (query.CalculateEntityCount() == 1)
                    {
                        _settingsEntity = query.GetSingletonEntity();
                    }
                }
                finally
                {
                    query.Dispose();
                }

                if (_settingsEntity == Entity.Null)
                {
                    _settingsEntity = world.EntityManager.CreateEntity(typeof(DepthSettings));
                    world.EntityManager.SetComponentData(_settingsEntity, _settings);
                }
            }

            entityManager = world.EntityManager;
            entity = _settingsEntity;
            return true;
        }
    }
}
