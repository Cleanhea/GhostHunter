using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 귀신 프로토타입의 상태 전이·어택 판정·탐지·추격 수치를 보관한다.
    /// 값의 최종 권위는 귀신 시스템 기획서(docs/project/ghost-system.md)이며 대부분 `[임시]` 라
    /// 플레이 테스트로 조정한다. 시야 수치는 `[TBD]`(G-3)이고, 소리는 걷기 반경 6m만
    /// 확정됐으며 달리기 반경·적용 상태는 아직 `[TBD]`(G-4)다.
    /// </summary>
    [CreateAssetMenu(
        menuName = "GhostHunter/Gameplay/Ghost Prototype Settings",
        fileName = "GhostPrototypeSettings")]
    public sealed class GhostPrototypeSettings : ScriptableObject
    {
        [Header("Team sanity bands (§5.2)")]
        [Tooltip("이 값 이하면 평상시 → 활동으로 즉시 전환한다.")]
        [SerializeField] private int _activeTeamSanity = 80;

        [Tooltip("이 값 이하일 때만 활동 상태에서 어택 판정을 실행한다.")]
        [SerializeField] private int _attackTeamSanity = 60;

        [Tooltip("고위험 구간. 프로토타입에서는 HUD 표시에만 쓰인다.")]
        [SerializeField] private int _highRiskTeamSanity = 30;

        [Header("State durations (seconds)")]
        [SerializeField] private float _warningDuration = 5f;
        [SerializeField] private float _attackMinDuration = 30f;
        [SerializeField] private float _attackMaxDuration = 90f;

        [Tooltip("어택 30초 경과 후 팀 평균이 이 값 이상으로 회복되면 조기 종료한다.")]
        [SerializeField] private int _attackEarlyEndTeamSanity = 70;

        [SerializeField] private float _calmingDuration = 30f;
        [SerializeField] private float _suppressionDuration = 10f;

        [Header("Attack roll (§7.2 · §7.3)")]
        [SerializeField] private float _attackRollInterval = 10f;
        [SerializeField, Range(0f, 1f)] private float _attackChance51To60 = 0.20f;
        [SerializeField, Range(0f, 1f)] private float _attackChance41To50 = 0.40f;
        [SerializeField, Range(0f, 1f)] private float _attackChance31To40 = 0.60f;
        [SerializeField, Range(0f, 1f)] private float _attackChance0To30 = 0.80f;

        [Header("Cleaning progress trigger (§6.2 · F1 stub)")]
        [Tooltip("청소 진행도가 이 값에 최초 도달하면 활동으로 전환하거나 방해 빈도를 올린다.")]
        [SerializeField] private int _cleaningActivateProgress = 40;

        [Tooltip("이미 활동 중에 40%에 도달했을 때 방해 빈도가 올라가는 시간.")]
        [SerializeField] private float _cleaningBoostDuration = 90f;

        [Header("Vision detection (§8.1 · [TBD] G-3)")]
        [SerializeField] private float _visionDistance = 15f;
        [SerializeField, Range(1f, 180f)] private float _visionAngle = 120f;

        [Tooltip("시야각·가림을 무시하는 근거리 감지. 기획서에는 없는 P1 보조값이라 0으로 꺼도 된다.")]
        [SerializeField] private float _nearDetectRadius = 3f;

        [SerializeField] private float _ghostEyeHeight = 1.6f;
        [SerializeField] private float _targetCenterHeight = 1f;

        [Header("Hearing detection (§8.3 · G-4 부분 확정: 걷기 6m)")]
        [Tooltip("서버가 위치 변화로 추정한 이동 속력이 이 값 이상이면 달리기로 친다. 걷기 5m/s와 달리기 7m/s의 중간값.")]
        [SerializeField] private float _runSpeedThreshold = 6f;

        [Tooltip("이 값 이상이면 걷기. 미만이면 웅크림으로 간주해 소리를 내지 않는다.")]
        [SerializeField] private float _walkSpeedThreshold = 1.2f;

        [SerializeField] private float _runHearingRadius = 12f;
        [SerializeField] private float _walkHearingRadius = 6f;

        [Header("Chase / search AI (§9)")]
        [SerializeField] private float _searchDuration = 7f;
        [SerializeField] private float _roamSpeed = 1.6f;
        [SerializeField] private float _chaseSpeed = 3.4f;
        [SerializeField] private float _repathInterval = 0.5f;
        [SerializeField] private float _reachDistance = 0.6f;
        [SerializeField] private float _catchRadius = 1.4f;
        [SerializeField] private float _catchCooldown = 4f;
        [SerializeField] private float _gravity = 12f;

        [Tooltip("추격·수색 중 이 거리 안의 닫힌 방문을 직접 연다 (§9.4).")]
        [SerializeField] private float _doorOpenRange = 2.2f;

        [Header("Paranormal phenomena (§6 · [TBD] G-13)")]
        [Tooltip("평상시 초자연현상 발생 주기(초). 활동보다 드물다 (§6.1).")]
        [SerializeField] private float _phenomenaIdleInterval = 24f;

        [Tooltip("활동 상태 초자연현상 발생 주기(초).")]
        [SerializeField] private float _phenomenaActiveInterval = 10f;

        [Tooltip("팀 평균 30 이하이거나 청소 40% 방해 구간일 때의 발생 주기(초) (§6.1·§6.2).")]
        [SerializeField] private float _phenomenaHighRiskInterval = 6f;

        [Tooltip("귀신 기준 이 반경 안의 문·서랍·가구·조명이 현상 대상이 된다.")]
        [SerializeField] private float _phenomenonRadius = 6f;

        [Tooltip("'물건 흔들기' 회전 세기 — 사인파 각속도 진폭(rad/s). 클수록 크게 뒤틀린다.")]
        [SerializeField] private float _shakeTorque = 12f;

        [Tooltip("'물건 흔들기' 좌우로 달그락거리는 속도(m/s). 방향을 번갈아 줘 알짜 이동은 0에 가깝다.")]
        [SerializeField] private float _shakeShoveSpeed = 1.4f;

        [Tooltip("'물건 흔들기'가 이어지는 시간(초). 이 동안 흔든 뒤 멈춘다.")]
        [SerializeField] private float _shakeDuration = 1f;

        [Tooltip("'작은 물건 떨어뜨리기' 대상 최대 치수(m). 렌더러 바운즈 최대 변이 이보다 작아야 소품으로 친다.")]
        [SerializeField] private float _smallPropMaxSize = 0.45f;

        [Tooltip("'작은 물건 떨어뜨리기'가 소품을 튕겨 올리는 속도(m/s). 질량과 무관하게 직접 준다.")]
        [SerializeField] private float _dropSpeed = 2.5f;

        [Tooltip("'귀신 일시 출현'이 화면에 보이는 시간(초). 이 동안 서서히 흐려진다.")]
        [SerializeField] private float _apparitionSeconds = 1.8f;

        [Tooltip("'조명 깜빡임/끄기'가 이어지는 시간(초).")]
        [SerializeField] private float _lightFlickerSeconds = 1.6f;

        [Tooltip("끄면 '벽·문 두드리는 소리'·'발소리'가 선택 Pool에서 빠진다 (오디오 에셋 대기, GDD §9).")]
        [SerializeField] private bool _soundPhenomenaEnabled;

        [Header("Ghost event witnessed (§6.3 · G-6 해결 — 사용자 확정 2026-08-31)")]
        [Tooltip("발생한 초자연현상을 '목격'으로 인정하는 최대 거리(m). 어떤 현상이든 이 범위 안에서 " +
            "각도·가림을 모두 통과한 플레이어만 정신력이 줄어든다(SanitySystemSettings.GhostEventDecrease).")]
        [SerializeField] private float _phenomenonWitnessDistance = 12f;

        [Tooltip("플레이어 정면 기준 목격 판정 각도(도). 서버는 카메라 피치를 모르므로(SanityWitnessProp과 " +
            "같은 이유) 수평(요) 방향만 본다.")]
        [SerializeField, Range(1f, 180f)] private float _phenomenonWitnessAngle = 70f;

        [Tooltip("목격 판정에 쓰는 플레이어 눈높이(m).")]
        [SerializeField] private float _phenomenonWitnessEyeHeight = 1.5f;

        [Header("Hiding spots (§9.5 · G-8 판정 시점·주기·재검사 미정 — 지금은 '최초 접근 시 1회'만 임시 구현)")]
        [Tooltip("이 반경 안에 들어오면 은신처를 '발견'한 것으로 치고 검사를 시도한다.")]
        [SerializeField] private float _hidingSpotCheckRadius = 2.5f;

        [Tooltip("[임시] 은신처를 발견했을 때 실제로 안을 들여다볼 확률(§9.5 원문 30%).")]
        [SerializeField, Range(0f, 1f)] private float _hidingSpotCheckChance = 0.3f;

        public int ActiveTeamSanity => _activeTeamSanity;
        public int AttackTeamSanity => _attackTeamSanity;
        public int HighRiskTeamSanity => _highRiskTeamSanity;
        public float WarningDuration => _warningDuration;
        public float AttackMinDuration => _attackMinDuration;
        public float AttackMaxDuration => _attackMaxDuration;
        public int AttackEarlyEndTeamSanity => _attackEarlyEndTeamSanity;
        public float CalmingDuration => _calmingDuration;
        public float SuppressionDuration => _suppressionDuration;
        public float AttackRollInterval => _attackRollInterval;
        public int CleaningActivateProgress => _cleaningActivateProgress;
        public float CleaningBoostDuration => _cleaningBoostDuration;
        public float VisionDistance => _visionDistance;
        public float VisionAngle => _visionAngle;
        public float NearDetectRadius => _nearDetectRadius;
        public float GhostEyeHeight => _ghostEyeHeight;
        public float TargetCenterHeight => _targetCenterHeight;
        public float RunSpeedThreshold => _runSpeedThreshold;
        public float WalkSpeedThreshold => _walkSpeedThreshold;
        public float RunHearingRadius => _runHearingRadius;
        public float WalkHearingRadius => _walkHearingRadius;
        public float SearchDuration => _searchDuration;
        public float RoamSpeed => _roamSpeed;
        public float ChaseSpeed => _chaseSpeed;
        public float RepathInterval => _repathInterval;
        public float ReachDistance => _reachDistance;
        public float CatchRadius => _catchRadius;
        public float CatchCooldown => _catchCooldown;
        public float Gravity => _gravity;
        public float DoorOpenRange => _doorOpenRange;
        public float PhenomenaIdleInterval => _phenomenaIdleInterval;
        public float PhenomenaActiveInterval => _phenomenaActiveInterval;
        public float PhenomenaHighRiskInterval => _phenomenaHighRiskInterval;
        public float PhenomenonRadius => _phenomenonRadius;
        public float ShakeTorque => _shakeTorque;
        public float ShakeShoveSpeed => _shakeShoveSpeed;
        public float ShakeDuration => _shakeDuration;
        public float SmallPropMaxSize => _smallPropMaxSize;
        public float DropSpeed => _dropSpeed;
        public float ApparitionSeconds => _apparitionSeconds;
        public float LightFlickerSeconds => _lightFlickerSeconds;
        public bool SoundPhenomenaEnabled => _soundPhenomenaEnabled;
        public float PhenomenonWitnessDistance => _phenomenonWitnessDistance;
        public float PhenomenonWitnessAngle => _phenomenonWitnessAngle;
        public float PhenomenonWitnessEyeHeight => _phenomenonWitnessEyeHeight;
        public float HidingSpotCheckRadius => _hidingSpotCheckRadius;
        public float HidingSpotCheckChance => _hidingSpotCheckChance;

        /// <summary>기획서 §7.3 의 팀 평균 정신력별 10초당 어택 확률.</summary>
        public float AttackChanceForTeamSanity(int teamSanity)
        {
            if (teamSanity >= 61)
                return 0f;
            if (teamSanity >= 51)
                return _attackChance51To60;
            if (teamSanity >= 41)
                return _attackChance41To50;
            if (teamSanity >= 31)
                return _attackChance31To40;

            return _attackChance0To30;
        }

        private void OnValidate()
        {
            _activeTeamSanity = Mathf.Clamp(_activeTeamSanity, 0, 100);
            _attackTeamSanity = Mathf.Clamp(_attackTeamSanity, 0, _activeTeamSanity);
            _highRiskTeamSanity = Mathf.Clamp(_highRiskTeamSanity, 0, _attackTeamSanity);
            _attackEarlyEndTeamSanity = Mathf.Clamp(_attackEarlyEndTeamSanity, 0, 100);

            _warningDuration = Mathf.Max(0.1f, _warningDuration);
            _attackMinDuration = Mathf.Max(0.1f, _attackMinDuration);
            _attackMaxDuration = Mathf.Max(_attackMinDuration, _attackMaxDuration);
            _calmingDuration = Mathf.Max(0.1f, _calmingDuration);
            _suppressionDuration = Mathf.Max(0.1f, _suppressionDuration);
            _attackRollInterval = Mathf.Max(0.1f, _attackRollInterval);

            _cleaningActivateProgress = Mathf.Clamp(_cleaningActivateProgress, 1, 100);
            _cleaningBoostDuration = Mathf.Max(0f, _cleaningBoostDuration);

            _visionDistance = Mathf.Max(0.1f, _visionDistance);
            _nearDetectRadius = Mathf.Max(0f, _nearDetectRadius);
            _ghostEyeHeight = Mathf.Max(0f, _ghostEyeHeight);
            _targetCenterHeight = Mathf.Max(0f, _targetCenterHeight);

            _walkSpeedThreshold = Mathf.Max(0f, _walkSpeedThreshold);
            _runSpeedThreshold = Mathf.Max(_walkSpeedThreshold, _runSpeedThreshold);
            _walkHearingRadius = Mathf.Max(0f, _walkHearingRadius);
            _runHearingRadius = Mathf.Max(0f, _runHearingRadius);

            _searchDuration = Mathf.Max(0f, _searchDuration);
            _roamSpeed = Mathf.Max(0f, _roamSpeed);
            _chaseSpeed = Mathf.Max(0f, _chaseSpeed);
            _repathInterval = Mathf.Max(0.05f, _repathInterval);
            _reachDistance = Mathf.Max(0.05f, _reachDistance);
            _catchRadius = Mathf.Max(0.1f, _catchRadius);
            _catchCooldown = Mathf.Max(0f, _catchCooldown);
            _gravity = Mathf.Max(0f, _gravity);
            _doorOpenRange = Mathf.Max(0f, _doorOpenRange);

            _phenomenaIdleInterval = Mathf.Max(1f, _phenomenaIdleInterval);
            _phenomenaActiveInterval = Mathf.Max(1f, _phenomenaActiveInterval);
            _phenomenaHighRiskInterval = Mathf.Max(1f, _phenomenaHighRiskInterval);
            _phenomenonRadius = Mathf.Max(0.5f, _phenomenonRadius);
            _shakeTorque = Mathf.Max(0f, _shakeTorque);
            _shakeShoveSpeed = Mathf.Max(0f, _shakeShoveSpeed);
            _shakeDuration = Mathf.Max(0f, _shakeDuration);
            _smallPropMaxSize = Mathf.Max(0.05f, _smallPropMaxSize);
            _dropSpeed = Mathf.Max(0f, _dropSpeed);
            _apparitionSeconds = Mathf.Max(0.1f, _apparitionSeconds);
            _lightFlickerSeconds = Mathf.Max(0.1f, _lightFlickerSeconds);

            _phenomenonWitnessDistance = Mathf.Max(0.1f, _phenomenonWitnessDistance);
            _phenomenonWitnessEyeHeight = Mathf.Max(0f, _phenomenonWitnessEyeHeight);

            _hidingSpotCheckRadius = Mathf.Max(0.1f, _hidingSpotCheckRadius);
        }
    }
}
