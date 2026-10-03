using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 안전모 헤드라이트(기획서 "손전등" 0.1 → 헤드라이트로 변경, 2026-09-28)의 배터리·조명·효과음 설정.
    /// 규칙은 docs/project/headlamp-system.md, 구조는 docs/architecture/headlamp.md.
    /// </summary>
    [CreateAssetMenu(
        fileName = "HeadlampSettings_Default",
        menuName = "GhostHunter/Gameplay/Headlamp Settings")]
    public sealed class HeadlampSettings : ScriptableObject
    {
        [Header("배터리 (기획서 §2-2)")]
        [Tooltip("배터리 총량.")]
        [SerializeField, Min(1f)] private float _maxBattery = 100f;

        [Tooltip("켜져 있는 동안 초당 감소량. 스테이지 씬에서만 줄어든다.")]
        [SerializeField, Min(0f)] private float _drainPerSecond = 0.6f;

        [Tooltip("드릴카 안에 있는 동안 초당 충전량. 충전 중에는 켜져 있어도 줄지 않는다.")]
        [SerializeField, Min(0f)] private float _rechargePerSecond = 5f;

        [Tooltip("이 값 이하이면 저전력 깜빡임을 시작한다.")]
        [SerializeField, Min(0f)] private float _lowBatteryThreshold = 15f;

        [Header("저전력 깜빡임 (기획서 §2-2)")]
        [Tooltip("깜빡임 묶음이 시작되는 간격(초).")]
        [SerializeField, Min(0.5f)] private float _lowBatteryBlinkInterval = 5f;

        [Tooltip("한 묶음에서 깜빡이는 횟수.")]
        [SerializeField, Min(1)] private int _lowBatteryBlinkCount = 3;

        [Tooltip("깜빡임 한 번에서 꺼져 있는 시간(초). [임시]")]
        [SerializeField, Min(0.01f)] private float _blinkOffSeconds = 0.08f;

        [Tooltip("깜빡임 사이에 다시 켜져 있는 시간(초). [임시]")]
        [SerializeField, Min(0.01f)] private float _blinkOnSeconds = 0.1f;

        [Header("조명 (기획서 §2-3, 레퍼런스 크라임씬 클리너) — 수치는 플레이 화면 기준으로 조정")]
        [Tooltip("카메라 피벗 기준 조명 위치(m). 안전모 앞쪽 — 원격 플레이어 머리 메시에 가려 그림자가 지지 않게 앞으로 뺀다.")]
        [SerializeField] private Vector3 _localOffset = new(0f, 0.12f, 0.18f);

        [Tooltip("조명 도달 거리(m).")]
        [SerializeField, Min(0.5f)] private float _range = 14f;

        [Tooltip("원뿔 바깥 각도(도). 바닥 기준 전방 약 1.5m 반경을 주로 비추는 값에서 출발한다.")]
        [SerializeField, Range(1f, 179f)] private float _spotAngle = 62f;

        [Tooltip("원뿔 안쪽 각도(도). 이 안은 가장 밝고 바깥으로 갈수록 어두워진다.")]
        [SerializeField, Range(0f, 179f)] private float _innerSpotAngle = 28f;

        [SerializeField, Min(0f)] private float _intensity = 6f;

        [SerializeField] private Color _color = new(1f, 0.95f, 0.86f, 1f);

        [Tooltip("벽·가구에 가려 그림자가 생기게 한다. URP 파이프라인 에셋의 Additional Light Shadows 가 켜져 있어야 보인다.")]
        [SerializeField] private LightShadows _shadows = LightShadows.Soft;

        [SerializeField, Range(0f, 1f)] private float _shadowStrength = 0.9f;

        [Header("쿠키 — 표면에 맺히는 무늬. 가운데 핫스팟 + 테두리 링 + 흐린 주변광")]
        [Tooltip("직접 그린 쿠키 텍스처(가운데 = 원뿔 축, 가장자리는 검정, Wrap Clamp). 비어 있으면 아래 값으로 만든다.")]
        [SerializeField] private Texture2D _cookieOverride;

        [Tooltip("핫스팟 반지름 — 쿠키 반지름(바깥 각도) 대비 비율.")]
        [SerializeField, Range(0.05f, 0.9f)] private float _cookieHotspotRadius = 0.32f;

        [Tooltip("핫스팟 밖 주변광 밝기(핫스팟 = 1). 낮을수록 동그란 원만 남는다.")]
        [SerializeField, Range(0f, 1f)] private float _cookieSpill = 0.45f;

        [Tooltip("핫스팟 테두리에 생기는 밝은 링 세기. 반사경 손전등 느낌.")]
        [SerializeField, Range(0f, 0.5f)] private float _cookieRingStrength = 0.12f;

        [Header("빛줄기 — 공기 중에 은은하게 보이는 원뿔(가짜 볼류메트릭)")]
        [Tooltip("GhostHunter/HeadlampBeam 재질. 윤곽·감쇠·벽 경계 흐림은 재질에서 조정한다. 비어 있으면 빛줄기를 만들지 않는다.")]
        [SerializeField] private Material _beamMaterial;

        [Tooltip("빛줄기 원뿔 각도(도). 조명 바깥 각도보다 좁게 — 핫스팟 언저리만 보이게 한다.")]
        [SerializeField, Range(1f, 120f)] private float _beamAngle = 36f;

        [Tooltip("빛줄기 최대 길이(m). 앞이 가로막히면 그 거리까지만 그린다.")]
        [SerializeField, Min(0.5f)] private float _beamLength = 6f;

        [Tooltip("빛줄기 세기(조명 색에 곱한다). 다른 플레이어 기준 — 자기 시점은 재질의 Near Lamp Scale 만큼 더 약하다.")]
        [SerializeField, Range(0f, 1f)] private float _beamIntensity = 0.12f;

        [Tooltip("빛줄기를 자르는 레이어. 벽·가구·플레이어처럼 빛을 막는 것.")]
        [SerializeField] private LayerMask _beamOcclusionMask = ~0;

        [Header("효과음 (기획서 §4) — 비어 있으면 재생하지 않는다")]
        [Tooltip("SFX_flashlight_on")]
        [SerializeField] private AudioClip _onClip;

        [Tooltip("SFX_flashlight_off")]
        [SerializeField] private AudioClip _offClip;

        [Tooltip("SFX_flashlight_broken — 배터리가 0인데 켜려고 할 때.")]
        [SerializeField] private AudioClip _brokenClip;

        [SerializeField, Range(0f, 1f)] private float _sfxVolume = 0.8f;

        public float MaxBattery => _maxBattery;
        public float DrainPerSecond => _drainPerSecond;
        public float RechargePerSecond => _rechargePerSecond;
        public float LowBatteryThreshold => _lowBatteryThreshold;
        public float LowBatteryBlinkInterval => _lowBatteryBlinkInterval;
        public int LowBatteryBlinkCount => _lowBatteryBlinkCount;
        public float BlinkOffSeconds => _blinkOffSeconds;
        public float BlinkOnSeconds => _blinkOnSeconds;
        public Vector3 LocalOffset => _localOffset;
        public float Range => _range;
        public float SpotAngle => _spotAngle;
        public float InnerSpotAngle => _innerSpotAngle;
        public float Intensity => _intensity;
        public Color Color => _color;
        public LightShadows Shadows => _shadows;
        public float ShadowStrength => _shadowStrength;
        public Texture2D CookieOverride => _cookieOverride;
        public float CookieHotspotRadius => _cookieHotspotRadius;
        public float CookieSpill => _cookieSpill;
        public float CookieRingStrength => _cookieRingStrength;
        public Material BeamMaterial => _beamMaterial;
        public float BeamAngle => _beamAngle;
        public float BeamLength => _beamLength;
        public float BeamIntensity => _beamIntensity;
        public LayerMask BeamOcclusionMask => _beamOcclusionMask;
        public AudioClip OnClip => _onClip;
        public AudioClip OffClip => _offClip;
        public AudioClip BrokenClip => _brokenClip;
        public float SfxVolume => _sfxVolume;

        private void OnValidate()
        {
            _lowBatteryThreshold = Mathf.Clamp(_lowBatteryThreshold, 0f, _maxBattery);
            _innerSpotAngle = Mathf.Min(_innerSpotAngle, _spotAngle);

            // 한 묶음이 다음 묶음 시작 전에 끝나야 한다.
            float burst = _lowBatteryBlinkCount * (_blinkOffSeconds + _blinkOnSeconds);
            if (burst > _lowBatteryBlinkInterval)
                _lowBatteryBlinkInterval = burst;
        }
    }
}
