using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 라이터(퀵슬롯 아이템)의 연료·불빛·효과음 설정. 규칙은 docs/project/lighter-system.md,
    /// 구조는 docs/architecture/headlamp.md §라이터. 수치는 전부 [TEMP] — 플레이 화면 기준으로 조정한다.
    /// </summary>
    [CreateAssetMenu(
        fileName = "LighterSettings_Default",
        menuName = "GhostHunter/Gameplay/Lighter Settings")]
    public sealed class LighterSettings : ScriptableObject
    {
        [Header("연료 [TEMP]")]
        [Tooltip("연료 총량.")]
        [SerializeField, Min(1f)] private float _maxFuel = 100f;

        [Tooltip("켜져 있는 동안 초당 감소량. 스테이지 씬에서만 줄어든다.")]
        [SerializeField, Min(0f)] private float _drainPerSecond = 0.8f;

        [Tooltip("드릴카 안에 있는 동안 초당 충전량. 충전 중에는 켜져 있어도 줄지 않는다.")]
        [SerializeField, Min(0f)] private float _rechargePerSecond = 5f;

        [Header("불빛 [TEMP]")]
        [Tooltip("카메라 피벗 기준 불꽃 위치(m). 오른손 앞 — 원격 플레이어 몸에 가려 그림자가 지지 않게 앞으로 뺀다.")]
        [SerializeField] private Vector3 _localOffset = new(0.22f, -0.2f, 0.42f);

        [Tooltip("점광원 도달 거리(m). 헤드라이트(14m)보다 훨씬 짧게 — 발밑과 주변만 둥글게 비춘다.")]
        [SerializeField, Min(0.5f)] private float _range = 4.5f;

        [SerializeField, Min(0f)] private float _intensity = 1.6f;

        [SerializeField] private Color _color = new(1f, 0.62f, 0.28f, 1f);

        [Tooltip("불꽃 흔들림 폭(밝기 비율). 0 이면 흔들리지 않는다.")]
        [SerializeField, Range(0f, 0.8f)] private float _flickerAmount = 0.18f;

        [Tooltip("불꽃 흔들림 빠르기.")]
        [SerializeField, Min(0f)] private float _flickerSpeed = 9f;

        [SerializeField] private LightShadows _shadows = LightShadows.Soft;

        [SerializeField, Range(0f, 1f)] private float _shadowStrength = 0.85f;

        [Header("모양 — 비어 있으면 불빛만 낸다")]
        [SerializeField] private Material _flameMaterial;
        [SerializeField] private Material _bodyMaterial;

        [Header("효과음 — 비어 있으면 재생하지 않는다")]
        [Tooltip("라이터를 꺼내 불이 붙을 때.")]
        [SerializeField] private AudioClip _onClip;

        [Tooltip("라이터를 집어넣어 불이 꺼질 때.")]
        [SerializeField] private AudioClip _offClip;

        [Tooltip("연료가 0인데 라이터를 꺼냈을 때.")]
        [SerializeField] private AudioClip _emptyClip;

        [SerializeField, Range(0f, 1f)] private float _sfxVolume = 0.8f;

        public float MaxFuel => _maxFuel;
        public float DrainPerSecond => _drainPerSecond;
        public float RechargePerSecond => _rechargePerSecond;
        public Vector3 LocalOffset => _localOffset;
        public float Range => _range;
        public float Intensity => _intensity;
        public Color Color => _color;
        public float FlickerAmount => _flickerAmount;
        public float FlickerSpeed => _flickerSpeed;
        public LightShadows Shadows => _shadows;
        public float ShadowStrength => _shadowStrength;
        public Material FlameMaterial => _flameMaterial;
        public Material BodyMaterial => _bodyMaterial;
        public AudioClip OnClip => _onClip;
        public AudioClip OffClip => _offClip;
        public AudioClip EmptyClip => _emptyClip;
        public float SfxVolume => _sfxVolume;
    }
}
