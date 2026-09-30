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
        [Tooltip("이 값 이하일 때만 활동 상태에서 어택 판정을 실행한다.")]
        [SerializeField] private int _attackTeamSanity = 60;

        [Tooltip("이 값 이하이면 확률 판정 없이 고위험 어택을 시작한다.")]
        [SerializeField] private int _highRiskTeamSanity = 30;

        [Header("State durations (seconds)")]
        [SerializeField] private float _warningDuration = 5f;
        [SerializeField] private float _attackMaxDuration = 60f;
        [SerializeField] private float _highRiskAttackDuration = 90f;
        [SerializeField] private float _attackEarlyEndWindow = 20f;

        [Tooltip("어택 시작 후 20초 이내 팀 평균이 이 값 이상이면 조기 종료한다.")]
        [SerializeField] private int _attackEarlyEndTeamSanity = 61;

        [SerializeField] private float _calmingDuration = 30f;

        [Header("Attack roll (§7.2 · §7.3)")]
        [SerializeField] private float _attackRollInterval = 10f;
        [SerializeField, Range(0f, 1f)] private float _attackChance51To60 = 0.20f;
        [SerializeField, Range(0f, 1f)] private float _attackChance41To50 = 0.40f;
        [SerializeField, Range(0f, 1f)] private float _attackChance31To40 = 0.60f;
        [SerializeField, Range(0f, 1f)] private float _attackChance0To30 = 1f;

        [Header("Cleaning progress attack condition (§7.1)")]
        [Tooltip("실제 청소 진행도가 이 값에 한 번 도달하면 스테이지 동안 어택 조건이 유지된다.")]
        [SerializeField] private int _cleaningActivateProgress = 40;

        [Header("Vision detection (§8.1 · [TBD] G-3)")]
        [SerializeField] private float _visionDistance = 15f;
        [SerializeField, Range(1f, 180f)] private float _visionAngle = 120f;
        [Tooltip("고위험 어택의 시야 거리. 확정 수치 대기 중이므로 현재는 일반 시야와 같다(G-18).")]
        [SerializeField] private float _highRiskVisionDistance = 15f;
        [Tooltip("고위험 어택의 시야 각도. 확정 수치 대기 중이므로 현재는 일반 시야와 같다(G-18).")]
        [SerializeField, Range(1f, 180f)] private float _highRiskVisionAngle = 120f;

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
        [SerializeField] private float _searchDuration = 10f;
        [SerializeField] private float _targetSelectionInterval = 0.2f;
        [SerializeField] private float _targetSwitchDistance = 1f;
        [SerializeField] private float _roamSpeed = 1.6f;
        [SerializeField] private float _chaseSpeed = 3.4f;
        [SerializeField] private float _repathInterval = 0.5f;
        [SerializeField] private float _reachDistance = 0.6f;
        [SerializeField] private float _catchRadius = 1.4f;
        [SerializeField] private float _catchCooldown = 4f;
        [SerializeField] private float _gravity = 12f;

        [Tooltip("추격·수색 중 이 거리 안의 닫힌 방문을 직접 연다 (§9.4).")]
        [SerializeField] private float _doorOpenRange = 2.2f;

        [Header("AI experiments · 전체 / 개별 토글 ([TEMP])")]
        [Tooltip("전체 OFF면 기존 AI. F2 귀신 설정에서 플레이 중 비교. 서버만 판정한다.")]
        [SerializeField] private bool _aiExperimentsEnabled;
        [Tooltip("시야·청각 단서로만 추격. 놓치면 마지막 관측 위치에서 수색한다.")]
        [SerializeField] private bool _evidenceTrackingEnabled = true;
        [Tooltip("수색 후보를 도주 방향·방문 기록·이동 비용으로 평가하고 가까운 문도 확인한다.")]
        [SerializeField] private bool _utilitySearchEnabled = true;
        [Tooltip("관측한 이동 방향으로 짧게 앞질러 간다. 벽을 통과하는 예측은 사용하지 않는다.")]
        [SerializeField] private bool _predictiveChaseEnabled = true;
        [Tooltip("어택 중 가구 충돌 지점을 조사한다. 소음만으로 플레이어 좌표를 알지는 못한다.")]
        [SerializeField] private bool _impactInvestigationEnabled = true;
        [Tooltip("최근 위협이 강하면 초자연현상 간격을 늘려 연출 사이에 여유를 준다.")]
        [SerializeField] private bool _tensionPacingEnabled = true;

        [Header("AI experiment tuning ([TEMP])")]
        [SerializeField, Min(0.1f)] private float _evidenceMemorySeconds = 8f;
        [SerializeField, Min(0f)] private float _predictionSeconds = 0.6f;
        [SerializeField, Min(0f)] private float _predictionMaxDistance = 2.5f;
        [SerializeField, Min(0.1f)] private float _observedSpeedLimit = 8f;
        [SerializeField, Range(1, 16)] private int _searchCandidateCount = 8;
        [SerializeField, Min(0f)] private float _searchDirectionWeight = 2f;
        [SerializeField, Min(0f)] private float _searchNoveltyWeight = 3f;
        [SerializeField, Min(0f)] private float _searchTravelWeight = 0.4f;
        [SerializeField, Min(0f)] private float _searchScanSeconds = 0.7f;
        [SerializeField, Min(0.1f)] private float _impactMinimumSpeed = 2f;
        [SerializeField, Min(0f)] private float _impactHearingRadius = 14f;
        [SerializeField, Range(0f, 1f)] private float _occludedImpactMultiplier = 0.5f;
        [SerializeField, Min(0.1f)] private float _impactInvestigateCooldown = 1f;
        [SerializeField, Min(0.1f)] private float _tensionReleaseSeconds = 15f;
        [SerializeField, Min(1f)] private float _tensionIntervalMultiplier = 2f;

        [Header("Navigation (MAP-11 · [TEMP])")]
        [Tooltip("귀신 전용 NavMesh 를 굽는 에이전트 반경(m). 프로젝트 기본 Humanoid(0.5)로는 B안 1.0m 문틀이 " +
            "양쪽에서 0.5씩 깎여 막힌다. CharacterController 반경(0.35)보다 작게 둬 벽에 붙어 미끄러지게 한다.")]
        [SerializeField] private float _navAgentRadius = 0.25f;

        [Tooltip("배회 목적지를 고를 때 현재 위치에서 최소 이만큼(m) 떨어진 곳을 우선한다. 제자리 맴돌기 방지.")]
        [SerializeField] private float _roamMinDistance = 6f;

        [Tooltip("수색(§9.3) 중 마지막 위치 주변을 어슬렁거리는 반경(m).")]
        [SerializeField] private float _searchWanderRadius = 4f;

        [Header("Roam patrol ([TEMP] — 기획 §9.1 '집 내부 배회'의 구현 방식)")]
        [Tooltip("방문 기록 격자 한 칸(m). 층은 3m 단위로 따로 센다. 오래 안 간 칸이 다음 목적지로 뽑힌다.")]
        [SerializeField, Min(1f)] private float _roamCellSize = 3f;

        [Tooltip("이 시간(초) 이상 안 간 칸은 '처음 가는 곳'과 같게 친다. 짧으면 최근에 돈 곳으로 금방 되돌아간다.")]
        [SerializeField, Min(1f)] private float _roamMemorySeconds = 90f;

        [Tooltip("목적지 한 번 고를 때 비교하는 후보 수. 많을수록 덜 가 본 곳을 잘 고르지만 경로 계산이 는다.")]
        [SerializeField, Range(1, 12)] private int _roamCandidateCount = 6;

        [Tooltip("활동(비어택) 배회 중 목적지에 닿으면 멈춰 두리번거리는 시간(초) 하한·상한. 어택 중에는 멈추지 않는다.")]
        [SerializeField, Min(0f)] private float _roamLookAroundMin = 1.5f;

        [SerializeField, Min(0f)] private float _roamLookAroundMax = 3.5f;

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
        [SerializeField] private float _phenomenonWitnessEyeHeight = 1.2f;

        [Header("Bed hiding (§9.5 · 엎드려 침대 밑 — 사용자 확정 2026-09-03)")]
        [Tooltip("엎드려 침대 밑에 들어간 뒤, 귀신에게 안 쫓기고 시야에도 안 걸린 상태가 이 시간(초) " +
            "이상 이어지면 '완전히 숨은' 것으로 쳐서 탐지·잡힘·수색 훔쳐보기에서 전부 빠진다. " +
            "들어가는 걸 귀신이 봤으면(추격/수색 대상이면) 타이머가 돌지 않는다.")]
        [SerializeField, Min(0f)] private float _bedHideConcealSeconds = 1f;

        [Header("Body state light (§10.3 · §10.4 · [TEMP] 어두운 집 기준)")]
        [Tooltip("경고·어택 중 귀신 몸에 켜지는 점광원 반경(m). 크면 불 꺼진 집을 귀신이 들고 다니는 등불처럼 밝힌다.")]
        [SerializeField, Min(0.5f)] private float _stateLightRange = 3f;

        [Tooltip("경고 중 심장 박동처럼 오가는 밝기 하한.")]
        [SerializeField, Min(0f)] private float _warningLightMin = 0.2f;

        [Tooltip("경고 중 심장 박동처럼 오가는 밝기 상한.")]
        [SerializeField, Min(0f)] private float _warningLightMax = 0.8f;

        [Tooltip("어택 중 빨간 몸 조명 밝기.")]
        [SerializeField, Min(0f)] private float _attackLightIntensity = 1.2f;

        [Header("Body clarity by viewer sanity ([TEMP] 사용자 요청 2026-09-30)")]
        [Tooltip("보는 사람 정신력이 최대일 때 귀신 본체의 불투명도(알파). 정신력이 낮을수록 아래 값 쪽으로 선명해진다.")]
        [SerializeField, Range(0f, 1f)] private float _bodyAlphaAtFullSanity = 0.3f;

        [Tooltip("보는 사람 정신력이 최소일 때(그리고 정신력을 잃은 관전자에게) 귀신 본체의 불투명도(알파).")]
        [SerializeField, Range(0f, 1f)] private float _bodyAlphaAtZeroSanity = 0.9f;

        public int AttackTeamSanity => _attackTeamSanity;
        public int HighRiskTeamSanity => _highRiskTeamSanity;
        public float WarningDuration => _warningDuration;
        public float AttackMaxDuration => _attackMaxDuration;
        public float HighRiskAttackDuration => _highRiskAttackDuration;
        public float AttackEarlyEndWindow => _attackEarlyEndWindow;
        public int AttackEarlyEndTeamSanity => _attackEarlyEndTeamSanity;
        public float CalmingDuration => _calmingDuration;
        public float AttackRollInterval => _attackRollInterval;
        public int CleaningActivateProgress => _cleaningActivateProgress;
        public float VisionDistance => _visionDistance;
        public float VisionAngle => _visionAngle;
        public float HighRiskVisionDistance => _highRiskVisionDistance;
        public float HighRiskVisionAngle => _highRiskVisionAngle;
        public float NearDetectRadius => _nearDetectRadius;
        public float GhostEyeHeight => _ghostEyeHeight;
        public float TargetCenterHeight => _targetCenterHeight;
        public float RunSpeedThreshold => _runSpeedThreshold;
        public float WalkSpeedThreshold => _walkSpeedThreshold;
        public float RunHearingRadius => _runHearingRadius;
        public float WalkHearingRadius => _walkHearingRadius;
        public float SearchDuration => _searchDuration;
        public float TargetSelectionInterval => _targetSelectionInterval;
        public float TargetSwitchDistance => _targetSwitchDistance;
        public float RoamSpeed => _roamSpeed;
        public float ChaseSpeed => _chaseSpeed;
        public float RepathInterval => _repathInterval;
        public float ReachDistance => _reachDistance;
        public float CatchRadius => _catchRadius;
        public float CatchCooldown => _catchCooldown;
        public float Gravity => _gravity;
        public float DoorOpenRange => _doorOpenRange;
        public bool AiExperimentsEnabled => _aiExperimentsEnabled;
        public bool EvidenceTrackingEnabled => _aiExperimentsEnabled && _evidenceTrackingEnabled;
        public bool UtilitySearchEnabled => _aiExperimentsEnabled && _utilitySearchEnabled;
        public bool PredictiveChaseEnabled => _aiExperimentsEnabled && _predictiveChaseEnabled;
        public bool ImpactInvestigationEnabled => _aiExperimentsEnabled && _impactInvestigationEnabled;
        public bool TensionPacingEnabled => _aiExperimentsEnabled && _tensionPacingEnabled;
        public float EvidenceMemorySeconds => _evidenceMemorySeconds;
        public float PredictionSeconds => _predictionSeconds;
        public float PredictionMaxDistance => _predictionMaxDistance;
        public float ObservedSpeedLimit => _observedSpeedLimit;
        public int SearchCandidateCount => _searchCandidateCount;
        public float SearchDirectionWeight => _searchDirectionWeight;
        public float SearchNoveltyWeight => _searchNoveltyWeight;
        public float SearchTravelWeight => _searchTravelWeight;
        public float SearchScanSeconds => _searchScanSeconds;
        public float ImpactMinimumSpeed => _impactMinimumSpeed;
        public float ImpactHearingRadius => _impactHearingRadius;
        public float OccludedImpactMultiplier => _occludedImpactMultiplier;
        public float ImpactInvestigateCooldown => _impactInvestigateCooldown;
        public float TensionReleaseSeconds => _tensionReleaseSeconds;
        public float TensionIntervalMultiplier => _tensionIntervalMultiplier;
        public int AiFeatureMask => !_aiExperimentsEnabled ? 0
            : 1 | (_evidenceTrackingEnabled ? 2 : 0) | (_utilitySearchEnabled ? 4 : 0)
                | (_predictiveChaseEnabled ? 8 : 0) | (_impactInvestigationEnabled ? 16 : 0)
                | (_tensionPacingEnabled ? 32 : 0);
        public float NavAgentRadius => _navAgentRadius;
        public float RoamMinDistance => _roamMinDistance;
        public float SearchWanderRadius => _searchWanderRadius;
        public float RoamCellSize => _roamCellSize;
        public float RoamMemorySeconds => _roamMemorySeconds;
        public int RoamCandidateCount => _roamCandidateCount;
        public float RoamLookAroundMin => _roamLookAroundMin;
        public float RoamLookAroundMax => Mathf.Max(_roamLookAroundMin, _roamLookAroundMax);
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
        public float BedHideConcealSeconds => _bedHideConcealSeconds;
        public float StateLightRange => _stateLightRange;
        public float WarningLightMin => _warningLightMin;
        public float WarningLightMax => _warningLightMax;
        public float AttackLightIntensity => _attackLightIntensity;
        public float BodyAlphaAtFullSanity => _bodyAlphaAtFullSanity;
        public float BodyAlphaAtZeroSanity => _bodyAlphaAtZeroSanity;

        /// <summary>보는 사람 정신력에 따른 본체 불투명도. 정신력이 낮을수록 선명하다(선형).</summary>
        public float BodyAlphaForSanity(int sanity, int minimumSanity, int maximumSanity)
        {
            float t = maximumSanity > minimumSanity
                ? Mathf.InverseLerp(maximumSanity, minimumSanity, sanity)
                : 1f;
            return Mathf.Lerp(_bodyAlphaAtFullSanity, _bodyAlphaAtZeroSanity, t);
        }

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
            _attackTeamSanity = Mathf.Clamp(_attackTeamSanity, 0, 100);
            _highRiskTeamSanity = Mathf.Clamp(_highRiskTeamSanity, 0, _attackTeamSanity);
            _attackEarlyEndTeamSanity = Mathf.Clamp(_attackEarlyEndTeamSanity, 0, 100);

            _warningDuration = Mathf.Max(0.1f, _warningDuration);
            _attackMaxDuration = Mathf.Max(0.1f, _attackMaxDuration);
            _highRiskAttackDuration = Mathf.Max(_attackMaxDuration, _highRiskAttackDuration);
            _attackEarlyEndWindow = Mathf.Clamp(_attackEarlyEndWindow, 0f, _attackMaxDuration);
            _calmingDuration = Mathf.Max(0.1f, _calmingDuration);
            _attackRollInterval = Mathf.Max(0.1f, _attackRollInterval);

            _cleaningActivateProgress = Mathf.Clamp(_cleaningActivateProgress, 1, 100);

            _visionDistance = Mathf.Max(0.1f, _visionDistance);
            _highRiskVisionDistance = Mathf.Max(_visionDistance, _highRiskVisionDistance);
            _highRiskVisionAngle = Mathf.Max(_visionAngle, _highRiskVisionAngle);
            _nearDetectRadius = Mathf.Max(0f, _nearDetectRadius);
            _ghostEyeHeight = Mathf.Max(0f, _ghostEyeHeight);
            _targetCenterHeight = Mathf.Max(0f, _targetCenterHeight);

            _walkSpeedThreshold = Mathf.Max(0f, _walkSpeedThreshold);
            _runSpeedThreshold = Mathf.Max(_walkSpeedThreshold, _runSpeedThreshold);
            _walkHearingRadius = Mathf.Max(0f, _walkHearingRadius);
            _runHearingRadius = Mathf.Max(0f, _runHearingRadius);

            _searchDuration = Mathf.Max(0f, _searchDuration);
            _targetSelectionInterval = Mathf.Max(0.05f, _targetSelectionInterval);
            _targetSwitchDistance = Mathf.Max(0f, _targetSwitchDistance);
            _roamSpeed = Mathf.Max(0f, _roamSpeed);
            _chaseSpeed = Mathf.Max(0f, _chaseSpeed);
            _repathInterval = Mathf.Max(0.05f, _repathInterval);
            _reachDistance = Mathf.Max(0.05f, _reachDistance);
            _catchRadius = Mathf.Max(0.1f, _catchRadius);
            _catchCooldown = Mathf.Max(0f, _catchCooldown);
            _gravity = Mathf.Max(0f, _gravity);
            _doorOpenRange = Mathf.Max(0f, _doorOpenRange);
            _evidenceMemorySeconds = Mathf.Max(0.1f, _evidenceMemorySeconds);
            _predictionSeconds = Mathf.Max(0f, _predictionSeconds);
            _predictionMaxDistance = Mathf.Max(0f, _predictionMaxDistance);
            _observedSpeedLimit = Mathf.Max(0.1f, _observedSpeedLimit);
            _searchCandidateCount = Mathf.Clamp(_searchCandidateCount, 1, 16);
            _searchDirectionWeight = Mathf.Max(0f, _searchDirectionWeight);
            _searchNoveltyWeight = Mathf.Max(0f, _searchNoveltyWeight);
            _searchTravelWeight = Mathf.Max(0f, _searchTravelWeight);
            _searchScanSeconds = Mathf.Max(0f, _searchScanSeconds);
            _impactMinimumSpeed = Mathf.Max(0.1f, _impactMinimumSpeed);
            _impactHearingRadius = Mathf.Max(0f, _impactHearingRadius);
            _occludedImpactMultiplier = Mathf.Clamp01(_occludedImpactMultiplier);
            _impactInvestigateCooldown = Mathf.Max(0.1f, _impactInvestigateCooldown);
            _tensionReleaseSeconds = Mathf.Max(0.1f, _tensionReleaseSeconds);
            _tensionIntervalMultiplier = Mathf.Max(1f, _tensionIntervalMultiplier);
            _navAgentRadius = Mathf.Clamp(_navAgentRadius, 0.1f, 0.5f);
            _roamMinDistance = Mathf.Max(0f, _roamMinDistance);
            _searchWanderRadius = Mathf.Max(0.5f, _searchWanderRadius);

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

            _stateLightRange = Mathf.Max(0.5f, _stateLightRange);
            _warningLightMin = Mathf.Max(0f, _warningLightMin);
            _warningLightMax = Mathf.Max(_warningLightMin, _warningLightMax);
            _attackLightIntensity = Mathf.Max(0f, _attackLightIntensity);
        }
    }
}
