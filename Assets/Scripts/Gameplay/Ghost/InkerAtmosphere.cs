using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 인커 현상 중 화면 분위기(사용자 요청 2026-11-15 "이상현상 발동됐을 때 적절한 포스트프로세싱"). 각 피어가 로컬로 만든다 —
    /// 복제된 현상 단계와 내 카메라~현상 위치 거리로 세기를 정하고, 쾅·목 꺾임·갑툭튀 순간에는 충격을 얹는다.
    /// <list type="bullet">
    /// <item>분위기 Volume: 검붉은 비네트(심장 박동처럼 오르내림)·채도 빠짐·대비·노출 낮춤·필름 그레인·색수차.</item>
    /// <item>충격 Volume(더 높은 우선순위): 렌즈 왜곡·강한 색수차·붉은 번쩍임 — 빠르게 사라진다.</item>
    /// </list>
    /// 프로필은 실행 중에만 만든다(에셋 저장 없음). 저장된 Volume 가중치가 남는 문제가 없도록 처음엔 0 이다.
    /// 플레이어 카메라는 URP 포스트프로세싱이 켜져 있고 Volume 레이어 마스크가 Default 다(Player.prefab) — 이 오브젝트도 Default 레이어.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InkerAtmosphere : MonoBehaviour
    {
        private const float AmbientPriority = 60f;
        private const float ShockPriority = 61f;
        private const float FadeInSeconds = 0.6f;
        private const float FadeOutSeconds = 1.8f;

        private Volume _ambient;
        private Volume _shock;
        private VolumeProfile _ambientProfile;
        private VolumeProfile _shockProfile;
        private Vignette _vignette;
        private ChromaticAberration _ambientAberration;
        private LensDistortion _distortion;

        private float _target;
        private float _shockLevel;
        private float _shockDecay = 2.5f;
        private float _strength = 1f;

        /// <summary>지금 분위기 세기(0~1, 테스트·디버그).</summary>
        public float AmbientWeight => _ambient != null ? _ambient.weight : 0f;

        /// <summary>지금 충격 세기(0~1).</summary>
        public float ShockWeight => _shock != null ? _shock.weight : 0f;

        public static InkerAtmosphere Create()
        {
            var root = new GameObject("InkerAtmosphere");
            root.layer = 0;
            InkerAtmosphere atmosphere = root.AddComponent<InkerAtmosphere>();
            atmosphere.Build();
            return atmosphere;
        }

        /// <summary>이번 프레임의 목표 세기(0~1)와 전체 배율. 매 프레임 부른다.</summary>
        public void SetTarget(float target, float strength)
        {
            _target = Mathf.Clamp01(target);
            _strength = Mathf.Clamp(strength, 0f, 1.5f);
        }

        /// <summary>순간 충격(0~1). 더 센 충격이 오면 덮어쓴다. <paramref name="seconds"/> 동안 대부분 사라진다.</summary>
        public void Shock(float amount, float seconds = 0.6f)
        {
            amount = Mathf.Clamp01(amount * _strength);
            if (amount <= _shockLevel)
                return;
            _shockLevel = amount;
            _shockDecay = 3f / Mathf.Max(0.1f, seconds);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            float target = Mathf.Clamp01(_target * _strength);
            float duration = target > _ambient.weight ? FadeInSeconds : FadeOutSeconds;
            _ambient.weight = Mathf.MoveTowards(_ambient.weight, target, dt / duration);

            // 심장 박동처럼 비네트가 조였다 풀린다 — 세기가 높을수록 빠르다.
            float rate = Mathf.Lerp(1.1f, 1.9f, _ambient.weight);
            float beat = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Time.time * rate * Mathf.PI * 2f)), 6f);
            _vignette.intensity.value = 0.42f + 0.1f * beat;
            _ambientAberration.intensity.value = 0.22f + 0.18f * beat;

            _shockLevel = Mathf.Max(0f, _shockLevel - _shockLevel * _shockDecay * dt - 0.02f * dt);
            _shock.weight = _shockLevel;
            // 충격 중 화면이 꿀렁인다.
            _distortion.intensity.value = -0.38f + Mathf.Sin(Time.time * 37f) * 0.08f;
        }

        private void Build()
        {
            _ambientProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _ambientProfile.name = "InkerAmbient (runtime)";
            _vignette = _ambientProfile.Add<Vignette>();
            _vignette.color.Override(new Color(0.2f, 0f, 0.01f));
            _vignette.intensity.Override(0.45f);
            _vignette.smoothness.Override(0.55f);
            // 세상은 잿빛으로 빠지고(약간 차가운 병색), 붉은 기는 가장자리 비네트에만 남긴다.
            var ambientColor = _ambientProfile.Add<ColorAdjustments>();
            ambientColor.saturation.Override(-60f);
            ambientColor.contrast.Override(20f);
            ambientColor.postExposure.Override(-0.4f);
            ambientColor.colorFilter.Override(new Color(0.9f, 0.93f, 0.95f));
            var grain = _ambientProfile.Add<FilmGrain>();
            grain.type.Override(FilmGrainLookup.Medium1);
            grain.intensity.Override(0.35f);
            grain.response.Override(0.75f);
            _ambientAberration = _ambientProfile.Add<ChromaticAberration>();
            _ambientAberration.intensity.Override(0.25f);

            _shockProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _shockProfile.name = "InkerShock (runtime)";
            _distortion = _shockProfile.Add<LensDistortion>();
            _distortion.intensity.Override(-0.38f);
            _distortion.scale.Override(1f);
            var shockAberration = _shockProfile.Add<ChromaticAberration>();
            shockAberration.intensity.Override(1f);
            var shockColor = _shockProfile.Add<ColorAdjustments>();
            shockColor.colorFilter.Override(new Color(1f, 0.38f, 0.36f));
            shockColor.saturation.Override(-20f);
            shockColor.postExposure.Override(-0.6f);
            shockColor.contrast.Override(30f);
            var shockVignette = _shockProfile.Add<Vignette>();
            shockVignette.color.Override(new Color(0.35f, 0f, 0f));
            shockVignette.intensity.Override(0.62f);
            shockVignette.smoothness.Override(0.4f);

            _ambient = CreateVolume("Ambient", _ambientProfile, AmbientPriority);
            _shock = CreateVolume("Shock", _shockProfile, ShockPriority);
        }

        private Volume CreateVolume(string name, VolumeProfile profile, float priority)
        {
            var child = new GameObject(name);
            child.layer = 0;
            child.transform.SetParent(transform, false);
            Volume volume = child.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = priority;
            volume.weight = 0f;
            volume.sharedProfile = profile;
            return volume;
        }

        private void OnDestroy()
        {
            Release(_ambientProfile);
            Release(_shockProfile);
        }

        private static void Release(Object asset)
        {
            if (asset == null)
                return;
            if (Application.isPlaying)
                Destroy(asset);
            else
                DestroyImmediate(asset);
        }
    }
}
