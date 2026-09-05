using MediaPipeUnityDots.Runtime.Ecs;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace MediaPipeUnityDots.Sample.HandTracking.Scripts
{
    /// <summary>
    /// UI Toolkit을 통해 1 Euro Filter 파라미터를 실시간으로 조절하고 ECS 싱글턴에 반영하는 패널.
    /// 레이아웃은 FilterSettings.uxml/uss, C#은 바인딩과 ECS 푸시만 담당한다.
    /// </summary>
    public sealed class OneEuroFilterSettingsPanel : MonoBehaviour
    {
        [SerializeField]
        private PanelRenderer _panelRenderer;

        [SerializeField]
        private VisualTreeAsset _settingsUxml;

        private OneEuroFilterSettings _settings = OneEuroFilterSettings.Default;

        private World _cachedWorld;
        private Entity _settingsEntity;

        private Toggle _enabledToggle;
        private Slider _handMinCutoff;
        private Slider _handBeta;
        private Slider _faceMinCutoff;
        private Slider _faceBeta;
        private Slider _poseMinCutoff;
        private Slider _poseBeta;
        private Button _resetButton;

        private void OnEnable()
        {
            if (_panelRenderer == null)
            {
                Debug.LogError("[MPUD] OneEuroFilterSettingsPanel: _panelRenderer is not wired.");
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

            _enabledToggle = null;
            _handMinCutoff = null;
            _handBeta = null;
            _faceMinCutoff = null;
            _faceBeta = null;
            _poseMinCutoff = null;
            _poseBeta = null;
            _resetButton = null;
        }

        private void OnUIReload(PanelRenderer renderer, VisualElement root, int version)
        {
            var panel = root.Q<VisualElement>("filter-settings-panel");
            if (panel == null && _settingsUxml != null)
            {
                _settingsUxml.CloneTree(root);
                panel = root.Q<VisualElement>("filter-settings-panel");
            }

            if (panel == null)
            {
                return;
            }

            _enabledToggle = panel.Q<Toggle>("filter-enabled-toggle");
            _handMinCutoff = panel.Q<Slider>("hand-min-cutoff");
            _handBeta = panel.Q<Slider>("hand-beta");
            _faceMinCutoff = panel.Q<Slider>("face-min-cutoff");
            _faceBeta = panel.Q<Slider>("face-beta");
            _poseMinCutoff = panel.Q<Slider>("pose-min-cutoff");
            _poseBeta = panel.Q<Slider>("pose-beta");
            _resetButton = panel.Q<Button>("reset-defaults-button");

            SyncUiFromSettings();

            if (_enabledToggle != null)
            {
                _enabledToggle.RegisterValueChangedCallback(evt =>
                {
                    _settings.Enabled = evt.newValue ? 1 : 0;
                    PushSettingsToEcs();
                });
            }

            RegisterSliderCallback(_handMinCutoff, val => _settings.HandMinCutoff = val);
            RegisterSliderCallback(_handBeta, val => _settings.HandBeta = val);
            RegisterSliderCallback(_faceMinCutoff, val => _settings.FaceMinCutoff = val);
            RegisterSliderCallback(_faceBeta, val => _settings.FaceBeta = val);
            RegisterSliderCallback(_poseMinCutoff, val => _settings.PoseMinCutoff = val);
            RegisterSliderCallback(_poseBeta, val => _settings.PoseBeta = val);

            if (_resetButton != null)
            {
                _resetButton.clicked += OnResetClicked;
            }
        }

        private void RegisterSliderCallback(Slider slider, System.Action<float> applyValue)
        {
            if (slider == null)
            {
                return;
            }

            slider.RegisterValueChangedCallback(evt =>
            {
                applyValue(evt.newValue);
                PushSettingsToEcs();
            });
        }

        private void OnResetClicked()
        {
            _settings = OneEuroFilterSettings.Default;
            SyncUiFromSettings();
            PushSettingsToEcs();
        }

        private void SyncUiFromSettings()
        {
            _enabledToggle?.SetValueWithoutNotify(_settings.Enabled != 0);
            _handMinCutoff?.SetValueWithoutNotify(_settings.HandMinCutoff);
            _handBeta?.SetValueWithoutNotify(_settings.HandBeta);
            _faceMinCutoff?.SetValueWithoutNotify(_settings.FaceMinCutoff);
            _faceBeta?.SetValueWithoutNotify(_settings.FaceBeta);
            _poseMinCutoff?.SetValueWithoutNotify(_settings.PoseMinCutoff);
            _poseBeta?.SetValueWithoutNotify(_settings.PoseBeta);
        }

        private void PushSettingsToEcs()
        {
            if (TryGetSettingsEntity(out var entityManager, out var entity))
            {
                entityManager.SetComponentData(entity, _settings);
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

            if (_cachedWorld != world || _settingsEntity == Entity.Null)
            {
                _cachedWorld = world;
                _settingsEntity = Entity.Null;
                var query = world.EntityManager.CreateEntityQuery(typeof(OneEuroFilterSettings));
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
                    _settingsEntity = world.EntityManager.CreateEntity(typeof(OneEuroFilterSettings));
                    world.EntityManager.SetComponentData(_settingsEntity, _settings);
                }
            }

            if (!world.EntityManager.Exists(_settingsEntity))
            {
                _settingsEntity = Entity.Null;
                return false;
            }

            entityManager = world.EntityManager;
            entity = _settingsEntity;
            return true;
        }
    }
}
