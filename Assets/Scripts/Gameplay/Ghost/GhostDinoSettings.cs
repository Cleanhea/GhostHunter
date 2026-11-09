using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// '박치기 공룡' 귀신 설정(project/ghost-dino.md). 기본 귀신 설정(<see cref="GhostPrototypeSettings"/>)을 그대로 상속하고
    /// 박치기 사이클 값만 더한다 — 귀신 컨트롤러는 설정이 이 타입일 때만 어택 중 추격→준비→돌진→스턴을 돈다
    /// (2026-11-09 설계 고정, ADR-0024: 기본 귀신과 박치기를 완전히 분리하고 박치기는 기본 귀신 아래로 상속).
    /// </summary>
    [CreateAssetMenu(
        menuName = "GhostHunter/Gameplay/Ghost Dino Settings",
        fileName = "GhostDinoSettings")]
    public sealed class GhostDinoSettings : GhostPrototypeSettings
    {
        [Header("Dino ghost · '박치기 공룡' (project/ghost-dino.md) — 어택 중 추격→준비→돌진→스턴 반복")]
        [Tooltip("어택이 시작되거나 스턴이 끝난 뒤 이 시간(초) 동안 기존 어택 규칙대로 추격한다. 기획 8초.")]
        [SerializeField, Min(0f)] private float _dinoChaseSeconds = 8f;
        [Tooltip("제자리에 서서 돌진을 준비하는 시간(초). 기획 2초.")]
        [SerializeField, Min(0.1f)] private float _dinoPrepareSeconds = 2f;
        [Tooltip("준비의 마지막 몇 초 동안 방향을 고정한다(그 앞은 마지막으로 타깃팅한 플레이어를 따라 방향 조정). 기획 0.7초.")]
        [SerializeField, Min(0f)] private float _dinoDirectionLockSeconds = 0.7f;
        [Tooltip("돌진이 끝난 자리에서 행동 불가인 시간(초). 기획 5초.")]
        [SerializeField, Min(0f)] private float _dinoStunSeconds = 5f;
        [Tooltip("돌진 속도(m/s) — 쉬움. 기획 5. 프로토타입에서 변경 가능성이 있다.")]
        [SerializeField, Min(0.1f)] private float _dinoChargeSpeedEasy = 5f;
        [Tooltip("돌진 속도(m/s) — 보통. 기획 7.")]
        [SerializeField, Min(0.1f)] private float _dinoChargeSpeedNormal = 7f;
        [Tooltip("돌진 속도(m/s) — 어려움. 기획 9.")]
        [SerializeField, Min(0.1f)] private float _dinoChargeSpeedHard = 9f;
        [Tooltip("준비 중 귀신이 돌진 방향으로 몸을 돌리는 속도(도/초). 방향 조정·고정 연출이다.")]
        [SerializeField, Min(30f)] private float _dinoTurnSpeedDegrees = 540f;
        [Tooltip("돌진 중 귀신 중심과 플레이어 사이 수평 거리가 이 값(m) 이하면 충돌해 즉사한다. 귀신 컨트롤러 반경 + 플레이어 반경 정도.")]
        [SerializeField, Min(0.1f)] private float _dinoHitRadius = 0.7f;
        [Tooltip("돌진으로 가구와 부딪혔을 때 돌진 거리 1m당 내구도 손실. 손실 = ⌊거리 × 이 값⌋, 최소 1. 기획 5.")]
        [SerializeField, Min(0f)] private float _dinoDamagePerMeter = 5f;
        [Tooltip("돌진이 이 시간(초)을 넘기면 충돌이 없어도 끝낸다. 보이지 않는 장애물에 끼어 영원히 도는 것을 막는 안전장치.")]
        [SerializeField, Min(1f)] private float _dinoMaxChargeSeconds = 8f;

        public float DinoChaseSeconds => _dinoChaseSeconds;
        public float DinoPrepareSeconds => _dinoPrepareSeconds;
        public float DinoDirectionLockSeconds => _dinoDirectionLockSeconds;
        public float DinoStunSeconds => _dinoStunSeconds;

        /// <summary>돌진 속도 — 기본 귀신 설정의 난이도(<see cref="GhostPrototypeSettings.Difficulty"/>)로 고른다.</summary>
        public float DinoChargeSpeed => Difficulty switch
        {
            GhostDifficulty.Easy => _dinoChargeSpeedEasy,
            GhostDifficulty.Hard => _dinoChargeSpeedHard,
            _ => _dinoChargeSpeedNormal,
        };

        public float DinoTurnSpeedDegrees => _dinoTurnSpeedDegrees;
        public float DinoHitRadius => _dinoHitRadius;
        public float DinoDamagePerMeter => _dinoDamagePerMeter;
        public float DinoMaxChargeSeconds => _dinoMaxChargeSeconds;

        protected override void OnValidate()
        {
            base.OnValidate();
            _dinoDirectionLockSeconds = Mathf.Min(_dinoDirectionLockSeconds, _dinoPrepareSeconds);
        }
    }
}
