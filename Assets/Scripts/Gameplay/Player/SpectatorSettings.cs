using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 사망 후 관전(자유시점) 튜닝 값. 관전 기획서 SP-3(사용자 확정 2026-09-12: 고정 속도,
    /// 가속·빠른 이동 키 없음)의 구체적 수치는 사용자가 확정하지 않았다 — 아래 기본값은
    /// 구현자 임시값이며 밸런스 확정이 아니다 → docs/project/spectator-system.md §5.
    /// </summary>
    [CreateAssetMenu(
        fileName = "SpectatorSettings_Default",
        menuName = "GhostHunter/Gameplay/Spectator Settings")]
    public sealed class SpectatorSettings : ScriptableObject
    {
        [Header("자유비행 (임시값 — SP-3)")]
        [Tooltip("자유비행 이동 속도(m/s). 고정 속도 — 가속·빠른 이동 없음(SP-3 확정). 수치는 임시값.")]
        [SerializeField, Min(0.1f)] private float _flySpeed = 6f;

        [Tooltip("자유비행 시점 감도. 생존자 시점(PlayerMoveSettings.MouseSensitivity)과 별개다.")]
        [SerializeField, Min(0.01f)] private float _lookSensitivity = 0.1f;

        [Tooltip("자유비행 상하 시점 한계(도).")]
        [SerializeField, Range(1f, 89f)] private float _pitchLimit = 89f;

        [Header("죽음 임시 연출·시체")]
        [SerializeField, HideInInspector] private int _deathSettingsVersion;
        [SerializeField, Min(0f)] private float _deathSequenceSeconds = 1.4f;
        [SerializeField, Min(0f)] private float _blackoutSeconds = 0.3f;
        [SerializeField, Min(0f)] private float _corpseLiftHeight = 1.3f;
        [SerializeField, Min(0.1f)] private float _corpseMass = 1f;
        [SerializeField, Min(0f)] private float _deathShakeDegrees = 2f;
        [SerializeField, Min(0.1f)] private float _corpseWitnessDistance = 12f;
        [SerializeField, Range(1f, 180f)] private float _corpseWitnessAngle = 70f;
        [SerializeField, Min(0.05f)] private float _corpseWitnessInterval = 0.2f;
        [Tooltip("시체 캡슐 콜라이더 길이(m). 서 있는 캡슐 높이와 같게 둔다.")]
        [SerializeField, Min(0.1f)] private float _corpseHeight = 1.3f;
        [Tooltip("시체 캡슐 콜라이더 반지름(m).")]
        [SerializeField, Min(0.05f)] private float _corpseRadius = 0.3f;
        [Tooltip("시체 조준·잡기 윤곽선 셸 재질 — 가구와 같은 Furniture_Outline. 비우면 윤곽선이 없다.")]
        [SerializeField] private Material _corpseOutlineMaterial;

        public float FlySpeed => _flySpeed;
        public float LookSensitivity => _lookSensitivity;
        public float PitchLimit => _pitchLimit;
        public float DeathSequenceSeconds => _deathSequenceSeconds;
        public float BlackoutSeconds => _blackoutSeconds;
        public float CorpseLiftHeight => _corpseLiftHeight;
        public float CorpseMass => _corpseMass;
        public float DeathShakeDegrees => _deathShakeDegrees;
        public float CorpseWitnessDistance => _corpseWitnessDistance;
        public float CorpseWitnessAngle => _corpseWitnessAngle;
        public float CorpseWitnessInterval => _corpseWitnessInterval;
        public float CorpseHeight => _corpseHeight;
        public float CorpseRadius => _corpseRadius;
        public Material CorpseOutlineMaterial => _corpseOutlineMaterial;

        private void OnEnable()
        {
            // 기존 SpectatorSettings 에셋에는 새 필드가 없다. 편집기 저장 전에도
            // 기존 에셋과 런타임 생성 에셋이 같은 임시값으로 동작하도록 보정한다.
            if (_deathSettingsVersion != 0)
                return;

            _deathSequenceSeconds = 1.4f;
            _blackoutSeconds = 0.3f;
            _corpseLiftHeight = 1.3f;
            _corpseMass = 1f;
            _deathShakeDegrees = 2f;
            _corpseWitnessDistance = 12f;
            _corpseWitnessAngle = 70f;
            _corpseWitnessInterval = 0.2f;
            _corpseHeight = 1.3f;
            _corpseRadius = 0.3f;
            _deathSettingsVersion = 1;
        }

        private void OnValidate()
        {
            _flySpeed = Mathf.Max(0.1f, _flySpeed);
            _lookSensitivity = Mathf.Max(0.01f, _lookSensitivity);
            _pitchLimit = Mathf.Clamp(_pitchLimit, 1f, 89f);
            _deathSequenceSeconds = Mathf.Max(0f, _deathSequenceSeconds);
            _blackoutSeconds = Mathf.Clamp(_blackoutSeconds, 0f, _deathSequenceSeconds);
            _corpseLiftHeight = Mathf.Max(0f, _corpseLiftHeight);
            _corpseMass = Mathf.Max(0.1f, _corpseMass);
            _deathShakeDegrees = Mathf.Max(0f, _deathShakeDegrees);
            _corpseWitnessDistance = Mathf.Max(0.1f, _corpseWitnessDistance);
            _corpseWitnessAngle = Mathf.Clamp(_corpseWitnessAngle, 1f, 180f);
            _corpseWitnessInterval = Mathf.Max(0.05f, _corpseWitnessInterval);
            _corpseHeight = Mathf.Max(0.1f, _corpseHeight);
            _corpseRadius = Mathf.Clamp(_corpseRadius, 0.05f, _corpseHeight * 0.5f);
        }
    }
}
