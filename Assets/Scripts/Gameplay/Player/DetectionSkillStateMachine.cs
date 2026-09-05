using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 탐지 스킬의 시전·표시·재사용 대기 전이를 순수하게 계산한다.
    /// 입력·렌더링·네트워크 객체를 몰라 EditMode에서 플로우를 직접 검증할 수 있다.
    /// </summary>
    public sealed class DetectionSkillStateMachine
    {
        /// <summary>한 번의 Tick에서 발생한 상태 전이.</summary>
        public enum Transition
        {
            None,
            ActiveStarted,
            Finished,
            Cancelled,
        }

        /// <summary>
        /// 실수 누적 오차 허용치(초). 쿨타임 10초를 9.9 + 0.1 로 나눠 태우면 float 오차로
        /// 3.8e-7 초가 남아 <b>한 프레임 더 잠긴다</b>. 시간 비교를 0 대신 이 값과 한다.
        /// </summary>
        private const float TimeEpsilon = 1e-4f;

        private float _castDurationSeconds;
        private float _displayDurationSeconds;
        private float _cooldownDurationSeconds;

        private float _phaseRemainingSeconds;
        private float _cooldownRemainingSeconds;

        public DetectionSkillStateMachine(
            float castDurationSeconds,
            float displayDurationSeconds,
            float cooldownDurationSeconds)
        {
            _castDurationSeconds = Mathf.Max(0f, castDurationSeconds);
            _displayDurationSeconds = Mathf.Max(0.1f, displayDurationSeconds);
            _cooldownDurationSeconds = Mathf.Max(0f, cooldownDurationSeconds);
            Phase = MoleSkillPhase.Idle;
        }

        /// <summary>튜닝 HUD가 같은 설정 에셋을 수정했을 때 다음 프레임부터 반영한다.</summary>
        public void Configure(
            float castDurationSeconds,
            float displayDurationSeconds,
            float cooldownDurationSeconds)
        {
            _castDurationSeconds = Mathf.Max(0f, castDurationSeconds);
            _displayDurationSeconds = Mathf.Max(0.1f, displayDurationSeconds);
            _cooldownDurationSeconds = Mathf.Max(0f, cooldownDurationSeconds);

            if (Phase == MoleSkillPhase.Casting)
                _phaseRemainingSeconds = Mathf.Min(_phaseRemainingSeconds, _castDurationSeconds);
            else if (Phase == MoleSkillPhase.Active)
                _phaseRemainingSeconds = Mathf.Min(_phaseRemainingSeconds, _displayDurationSeconds);
            else if (Phase == MoleSkillPhase.Cooldown)
                _cooldownRemainingSeconds = Mathf.Min(
                    _cooldownRemainingSeconds,
                    _cooldownDurationSeconds);
        }

        public MoleSkillPhase Phase { get; private set; }

        public float PhaseRemainingSeconds =>
            Phase == MoleSkillPhase.Cooldown
                ? _cooldownRemainingSeconds
                : Phase == MoleSkillPhase.Idle ? 0f : _phaseRemainingSeconds;

        public float CooldownRemainingSeconds => _cooldownRemainingSeconds;
        public float ActiveDurationSeconds => _displayDurationSeconds;
        public float CooldownDurationSeconds => _cooldownDurationSeconds;

        /// <summary>
        /// Q 입력이 들어온 경우, 생존 → 사용 중 아님 → 쿨타임 0 순서로 판정한다.
        /// 실패한 입력은 어떤 대기열에도 쌓지 않는다.
        /// </summary>
        public bool TryStart(bool inputPressed, bool playerAlive)
        {
            // 기획서 §4.2의 판정 순서: 생존 → 사용 중 아님 → 쿨타임 0.
            if (!playerAlive)
                return false;

            if (!inputPressed)
                return false;

            if (Phase != MoleSkillPhase.Idle)
                return false;

            if (_cooldownRemainingSeconds > 0f)
                return false;

            Phase = MoleSkillPhase.Casting;
            _phaseRemainingSeconds = _castDurationSeconds;
            return true;
        }

        /// <summary>경과 시간을 적용하고 전이 결과를 돌려준다.</summary>
        public Transition Tick(float deltaTime, bool playerAlive, bool cancelOnDeath)
        {
            deltaTime = Mathf.Max(0f, deltaTime);

            if (Phase == MoleSkillPhase.Cooldown)
            {
                _cooldownRemainingSeconds = Mathf.Max(
                    0f,
                    _cooldownRemainingSeconds - deltaTime);
                if (_cooldownRemainingSeconds <= TimeEpsilon)
                {
                    _cooldownRemainingSeconds = 0f;
                    Phase = MoleSkillPhase.Idle;
                }

                return Transition.None;
            }

            if ((Phase == MoleSkillPhase.Casting || Phase == MoleSkillPhase.Active)
                && !playerAlive
                && cancelOnDeath)
            {
                Cancel();
                return Transition.Cancelled;
            }

            if (Phase == MoleSkillPhase.Idle)
                return Transition.None;

            _phaseRemainingSeconds -= deltaTime;
            if (_phaseRemainingSeconds > TimeEpsilon)
                return Transition.None;

            if (Phase == MoleSkillPhase.Casting)
            {
                Phase = MoleSkillPhase.Active;
                _phaseRemainingSeconds = _displayDurationSeconds;
                return Transition.ActiveStarted;
            }

            Phase = MoleSkillPhase.Cooldown;
            _phaseRemainingSeconds = 0f;
            _cooldownRemainingSeconds = _cooldownDurationSeconds;
            return Transition.Finished;
        }

        /// <summary>진행 중 표시를 취소하고 대기 상태로 되돌린다. 쿨타임은 부여하지 않는다.</summary>
        public void Cancel()
        {
            Phase = MoleSkillPhase.Idle;
            _phaseRemainingSeconds = 0f;
            _cooldownRemainingSeconds = 0f;
        }

        /// <summary>개발 HUD가 재사용 대기를 즉시 지울 때 쓴다.</summary>
        public void ResetCooldown()
        {
            _cooldownRemainingSeconds = 0f;
            if (Phase == MoleSkillPhase.Cooldown)
                Phase = MoleSkillPhase.Idle;
        }
    }
}
