using MediaPipeUnityDots.Runtime.Ecs;
using MediaPipeUnityDots.Runtime.Logging;
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

        private VisualElement _panelElement;
        private Toggle _enabledToggle;
        private Toggle _verboseLoggingToggle;
        private Slider _handMinCutoff;
        private Slider _handBeta;
        private Slider _faceMinCutoff;
        private Slider _faceBeta;
        private Slider _poseMinCutoff;
        private Slider _poseBeta;
        private Button _resetButton;

        public int PushCount { get; private set; }

        private void OnEnable()
        {
            if (_panelRenderer == null)
            {
                MpudLog.Error("[MPUD] OneEuroFilterSettingsPanel: _panelRenderer is not wired.");
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

        // version은 무시하고 매번 리바인딩한다. OnDisable에서 참조를 비우므로 가드 시 stale 위험이 있다.
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

            // 투명 캔버스가 하위 패널의 마우스 클릭을 가로채지 않도록 Ignore 처리
            root.pickingMode = PickingMode.Ignore;

            _panelElement = root.Q<VisualElement>("filter-settings-panel");
            if (_panelElement == null && _settingsUxml != null)
            {
                _settingsUxml.CloneTree(root);
                _panelElement = root.Q<VisualElement>("filter-settings-panel");
            }

            if (_panelElement == null)
            {
                return;
            }

            _enabledToggle = _panelElement.Q<Toggle>("filter-enabled-toggle");
            _verboseLoggingToggle = _panelElement.Q<Toggle>("verbose-logging-toggle");
            _handMinCutoff = _panelElement.Q<Slider>("hand-min-cutoff");
            _handBeta = _panelElement.Q<Slider>("hand-beta");
            _faceMinCutoff = _panelElement.Q<Slider>("face-min-cutoff");
            _faceBeta = _panelElement.Q<Slider>("face-beta");
            _poseMinCutoff = _panelElement.Q<Slider>("pose-min-cutoff");
            _poseBeta = _panelElement.Q<Slider>("pose-beta");
            _resetButton = _panelElement.Q<Button>("reset-defaults-button");

            SyncUiFromSettings();

            if (_enabledToggle != null)
            {
                _enabledToggle.RegisterValueChangedCallback(OnEnabledToggleChanged);
            }

            if (_verboseLoggingToggle != null)
            {
                _verboseLoggingToggle.RegisterValueChangedCallback(OnVerboseLoggingChanged);
            }

            if (_handMinCutoff != null)
            {
                _handMinCutoff.RegisterValueChangedCallback(OnHandMinCutoffChanged);
            }

            if (_handBeta != null)
            {
                _handBeta.RegisterValueChangedCallback(OnHandBetaChanged);
            }

            if (_faceMinCutoff != null)
            {
                _faceMinCutoff.RegisterValueChangedCallback(OnFaceMinCutoffChanged);
            }

            if (_faceBeta != null)
            {
                _faceBeta.RegisterValueChangedCallback(OnFaceBetaChanged);
            }

            if (_poseMinCutoff != null)
            {
                _poseMinCutoff.RegisterValueChangedCallback(OnPoseMinCutoffChanged);
            }

            if (_poseBeta != null)
            {
                _poseBeta.RegisterValueChangedCallback(OnPoseBetaChanged);
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

            if (_verboseLoggingToggle != null)
            {
                _verboseLoggingToggle.UnregisterValueChangedCallback(OnVerboseLoggingChanged);
                _verboseLoggingToggle = null;
            }

            if (_handMinCutoff != null)
            {
                _handMinCutoff.UnregisterValueChangedCallback(OnHandMinCutoffChanged);
                _handMinCutoff = null;
            }

            if (_handBeta != null)
            {
                _handBeta.UnregisterValueChangedCallback(OnHandBetaChanged);
                _handBeta = null;
            }

            if (_faceMinCutoff != null)
            {
                _faceMinCutoff.UnregisterValueChangedCallback(OnFaceMinCutoffChanged);
                _faceMinCutoff = null;
            }

            if (_faceBeta != null)
            {
                _faceBeta.UnregisterValueChangedCallback(OnFaceBetaChanged);
                _faceBeta = null;
            }

            if (_poseMinCutoff != null)
            {
                _poseMinCutoff.UnregisterValueChangedCallback(OnPoseMinCutoffChanged);
                _poseMinCutoff = null;
            }

            if (_poseBeta != null)
            {
                _poseBeta.UnregisterValueChangedCallback(OnPoseBetaChanged);
                _poseBeta = null;
            }

            if (_resetButton != null)
            {
                _resetButton.clicked -= OnResetClicked;
                _resetButton = null;
            }

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
            LogEcsReadback("toggle");
        }

        private void OnVerboseLoggingChanged(ChangeEvent<bool> evt)
        {
            MpudLog.Enabled = evt.newValue;
            MpudLog.Log($"[MPUD] Verbose logging: {(evt.newValue ? "on" : "off")}");
        }

        private void OnHandMinCutoffChanged(ChangeEvent<float> evt)
        {
            _settings.HandMinCutoff = evt.newValue;
            PushSettingsToEcs();
        }

        private void OnHandBetaChanged(ChangeEvent<float> evt)
        {
            _settings.HandBeta = evt.newValue;
            PushSettingsToEcs();
        }

        private void OnFaceMinCutoffChanged(ChangeEvent<float> evt)
        {
            _settings.FaceMinCutoff = evt.newValue;
            PushSettingsToEcs();
        }

        private void OnFaceBetaChanged(ChangeEvent<float> evt)
        {
            _settings.FaceBeta = evt.newValue;
            PushSettingsToEcs();
        }

        private void OnPoseMinCutoffChanged(ChangeEvent<float> evt)
        {
            _settings.PoseMinCutoff = evt.newValue;
            PushSettingsToEcs();
        }

        private void OnPoseBetaChanged(ChangeEvent<float> evt)
        {
            _settings.PoseBeta = evt.newValue;
            PushSettingsToEcs();
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
            _verboseLoggingToggle?.SetValueWithoutNotify(MpudLog.Enabled);
            _handMinCutoff?.SetValueWithoutNotify(_settings.HandMinCutoff);
            _handBeta?.SetValueWithoutNotify(_settings.HandBeta);
            _faceMinCutoff?.SetValueWithoutNotify(_settings.FaceMinCutoff);
            _faceBeta?.SetValueWithoutNotify(_settings.FaceBeta);
            _poseMinCutoff?.SetValueWithoutNotify(_settings.PoseMinCutoff);
            _poseBeta?.SetValueWithoutNotify(_settings.PoseBeta);
        }

        private void PushSettingsToEcs()
        {
            PushCount++;
            if (TryGetSettingsEntity(out var entityManager, out var entity))
            {
                entityManager.SetComponentData(entity, _settings);
            }
        }

        private void LogEcsReadback(string reason)
        {
            if (TryGetSettingsEntity(out var entityManager, out var entity))
            {
                var echoed = entityManager.GetComponentData<OneEuroFilterSettings>(entity);
                MpudLog.Log($"[MPUD] OneEuroFilter {reason}: pushed Enabled={_settings.Enabled}, " +
                    $"ecs Enabled={echoed.Enabled}, HandMinCutoff={echoed.HandMinCutoff}");
            }
            else
            {
                MpudLog.Warning("[MPUD] OneEuroFilter push skipped: no ECS world.");
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

            entityManager = world.EntityManager;
            entity = _settingsEntity;
            return true;
        }
    }
}
