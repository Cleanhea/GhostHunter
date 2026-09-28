using System;
using System.Collections.Generic;
using GhostHunter.Gameplay.Ghost;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GhostHunter.Gameplay.Lighting
{
    /// <summary>
    /// 스테이지 집의 조명(방 천장등·해·환경광)을 <see cref="StageLightingSettings"/> 에 맞춘다.
    /// 설정이 바뀌거나(<see cref="StageLightingSettings.Version"/>) HUD 스위치를 누를 때만 다시 적용한다.
    ///
    /// <para>천장등 밝기는 귀신 연출의 기준값(<see cref="GhostAmbientLight"/>)으로도 넘긴다 — 깜빡임·어택
    /// 조명이 끝나면 HUD 에서 정한 밝기로 돌아온다. 환경광은 이 씬이 활성 씬일 때만 고치고
    /// 비활성화될 때 씬 저장값으로 되돌린다.</para>
    ///
    /// <para>네트워크 동기화 없음 — 조명은 피어마다 로컬 연출이다(귀신 현상과 같은 원칙).</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageLightingController : MonoBehaviour, IStageLightingDebug
    {
        [SerializeField] private StageLightingSettings _settings;
        [Tooltip("씬의 Directional Light. 비워 두면 해는 건드리지 않는다.")]
        [SerializeField] private Light _sun;
        [SerializeField] private StageRoomLight[] _lights = Array.Empty<StageRoomLight>();
        [SerializeField] private string[] _floorNames = { "1층", "2층", "다락" };

        private int _appliedVersion = int.MinValue;
        private bool _dirty = true;

        private bool _ambientCaptured;
        private Color _baseSky;
        private Color _baseEquator;
        private Color _baseGround;
        private float _baseAmbientIntensity;

        public IReadOnlyList<StageRoomLight> Lights => _lights;
        public int FloorCount => _floorNames.Length;

        public string FloorName(int floor) =>
            floor >= 0 && floor < _floorNames.Length ? _floorNames[floor] : $"{floor + 1}층";

        public string StatusSummary
        {
            get
            {
                int on = 0;
                foreach (StageRoomLight light in _lights)
                    if (light != null && light.Light.enabled)
                        on++;

                return _settings == null
                    ? "StageLightingSettings 미배선"
                    : $"천장등 {on}/{_lights.Length} 켜짐 · 그림자 {_settings.RoomShadows} · " +
                      $"해 {(_settings.SunOn ? "on" : "off")} · 환경광 ×{_settings.AmbientScale:0.##}";
            }
        }

        public bool IsFloorOn(int floor)
        {
            foreach (StageRoomLight light in _lights)
                if (light != null && light.Floor == floor && light.SwitchOn)
                    return true;

            return false;
        }

        public void SetFloorOn(int floor, bool on)
        {
            foreach (StageRoomLight light in _lights)
                if (light != null && light.Floor == floor)
                    light.SetSwitch(on);

            _dirty = true;
        }

        public void SetLightOn(int index, bool on)
        {
            if (index < 0 || index >= _lights.Length || _lights[index] == null)
                return;

            _lights[index].SetSwitch(on);
            _dirty = true;
        }

        public void SetAllOn(bool on)
        {
            foreach (StageRoomLight light in _lights)
                if (light != null)
                    light.SetSwitch(on);

            _dirty = true;
        }

        private void OnEnable()
        {
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
            _dirty = true;
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
            RestoreAmbient();
        }

        private void Update()
        {
            if (_settings == null)
                return;

            if (_dirty || _settings.Version != _appliedVersion)
                ApplyAll();
        }

        private void HandleActiveSceneChanged(Scene previous, Scene next)
        {
            if (next == gameObject.scene)
                _dirty = true;
        }

        private void ApplyAll()
        {
            _dirty = false;
            _appliedVersion = _settings.Version;

            foreach (StageRoomLight light in _lights)
            {
                if (light == null)
                    continue;

                light.Apply(_settings, _settings.RoomLightsOn, out float intensity, out Color color);
                if (light.TryGetComponent(out GhostAmbientLight ghostLight))
                    ghostLight.SetBase(intensity, color);
            }

            if (_sun != null)
            {
                _sun.enabled = _settings.SunOn;
                _sun.intensity = _settings.SunIntensity;
                _sun.shadows = _settings.SunShadows;
            }

            ApplyAmbient();
        }

        private void ApplyAmbient()
        {
            // 추가 로드된 씬이라도 조명 설정은 활성 씬의 것만 쓰인다. 남의 씬 값을 덮어쓰지 않는다.
            if (SceneManager.GetActiveScene() != gameObject.scene)
                return;

            if (!_ambientCaptured)
            {
                _baseSky = RenderSettings.ambientSkyColor;
                _baseEquator = RenderSettings.ambientEquatorColor;
                _baseGround = RenderSettings.ambientGroundColor;
                _baseAmbientIntensity = RenderSettings.ambientIntensity;
                _ambientCaptured = true;
            }

            // Trilight/Flat 은 색을, Skybox 는 intensity 를 쓴다 — 둘 다 배율을 곱해 두면 모드와 무관하다.
            float scale = _settings.AmbientScale;
            RenderSettings.ambientSkyColor = _baseSky * scale;
            RenderSettings.ambientEquatorColor = _baseEquator * scale;
            RenderSettings.ambientGroundColor = _baseGround * scale;
            RenderSettings.ambientIntensity = _baseAmbientIntensity * scale;
        }

        private void RestoreAmbient()
        {
            if (!_ambientCaptured || SceneManager.GetActiveScene() != gameObject.scene)
                return;

            RenderSettings.ambientSkyColor = _baseSky;
            RenderSettings.ambientEquatorColor = _baseEquator;
            RenderSettings.ambientGroundColor = _baseGround;
            RenderSettings.ambientIntensity = _baseAmbientIntensity;
            _ambientCaptured = false;
        }
    }
}
