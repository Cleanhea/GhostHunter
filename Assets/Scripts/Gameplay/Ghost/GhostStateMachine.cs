using System;
using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 귀신 시스템 기획서 §4·§5·§7 의 공통 상태 전이를 계산한다. 팀 평균 정신력과 청소 진행도를
    /// 입력으로 받아 현재 <see cref="GhostPhase"/> 를 정하고, 활동 상태에서 10초 주기 어택 판정을 굴린다.
    /// NGO·씬에 의존하지 않는 순수 로직이라 EditMode 로 검증한다.
    /// </summary>
    internal sealed class GhostStateMachine
    {
        private static readonly System.Random SharedRandom = new();

        private readonly GhostPrototypeSettings _settings;
        private readonly Func<double> _roll;

        private bool _forceActive;
        private bool _cleaningTriggerConsumed;
        private float _cleaningBoostRemaining;

        internal GhostStateMachine(GhostPrototypeSettings settings, Func<double> roll = null)
        {
            _settings = settings != null
                ? settings
                : throw new ArgumentNullException(nameof(settings));
            _roll = roll ?? SharedRandom.NextDouble;

            ResetForStage();
        }

        /// <summary>현재 상태.</summary>
        internal GhostPhase Phase { get; private set; }

        /// <summary>현재 상태에 머문 시간(초).</summary>
        internal float PhaseElapsed { get; private set; }

        /// <summary>활동 상태에서 다음 어택 판정까지 남은 시간(초). 그 외 상태에서는 0.</summary>
        internal float SecondsUntilNextRoll { get; private set; }

        /// <summary>청소 40% 도달 직후 방해 빈도가 올라가 있는 구간인지(§6.2).</summary>
        internal bool CleaningBoostActive => _cleaningBoostRemaining > 0f;

        /// <summary>어택·경고 진행 중이라 신규 어택 판정을 하지 않는 상태인지(§11.3).</summary>
        internal bool IsAttackSequenceLocked =>
            Phase is GhostPhase.Warning or GhostPhase.Attack
                or GhostPhase.Calming or GhostPhase.Suppressed;

        internal void ResetForStage()
        {
            Phase = GhostPhase.Idle;
            PhaseElapsed = 0f;
            SecondsUntilNextRoll = 0f;
            _forceActive = false;
            _cleaningTriggerConsumed = false;
            _cleaningBoostRemaining = 0f;
        }

        /// <summary>F1 디버그 토글. 정신력·청소와 무관하게 활동 조건을 하나 더 세운다.</summary>
        internal void SetForceActive(bool forceActive) => _forceActive = forceActive;

        /// <summary>
        /// 특수 어택 Trigger(§7.4). 평상시·활동에서 부르면 10초 주기를 기다리지 않고 즉시 경고로 간다.
        /// 이미 경고·어택·진정 중이면 무시한다 — 원문이 끊긴 부분(G-2)이라 중첩·연장하지 않는다.
        /// </summary>
        internal bool ForceSpecialAttack()
        {
            if (Phase is not (GhostPhase.Idle or GhostPhase.Active))
                return false;

            EnterPhase(GhostPhase.Warning);
            return true;
        }

        /// <summary>강제 진정(§4.8). 어느 상태에서든 걸리고 진행 중인 어택을 즉시 끊는다.</summary>
        internal bool ForceSuppression()
        {
            if (Phase == GhostPhase.Suppressed)
                return false;

            EnterPhase(GhostPhase.Suppressed);
            return true;
        }

        /// <summary>한 프레임 진행한다. 반환값은 갱신된 현재 상태.</summary>
        internal GhostPhase Tick(
            float deltaTime,
            int teamSanity,
            bool hasLivingPlayers,
            int cleaningProgress)
        {
            if (deltaTime < 0f)
                deltaTime = 0f;

            PhaseElapsed += deltaTime;
            if (_cleaningBoostRemaining > 0f)
                _cleaningBoostRemaining = Mathf.Max(0f, _cleaningBoostRemaining - deltaTime);

            HandleCleaningTrigger(cleaningProgress);

            switch (Phase)
            {
                case GhostPhase.Idle:
                    if (HasActiveCondition(teamSanity))
                        EnterPhase(GhostPhase.Active);
                    break;

                case GhostPhase.Active:
                    TickActive(deltaTime, teamSanity, hasLivingPlayers);
                    break;

                case GhostPhase.Warning:
                    if (PhaseElapsed >= _settings.WarningDuration)
                        EnterPhase(GhostPhase.Attack);
                    break;

                case GhostPhase.Attack:
                    TickAttack(teamSanity);
                    break;

                case GhostPhase.Calming:
                    if (PhaseElapsed >= _settings.CalmingDuration)
                        RecheckAfterCalm(teamSanity);
                    break;

                case GhostPhase.Suppressed:
                    if (PhaseElapsed >= _settings.SuppressionDuration)
                        RecheckAfterCalm(teamSanity);
                    break;
            }

            return Phase;
        }

        private void TickActive(float deltaTime, int teamSanity, bool hasLivingPlayers)
        {
            if (!HasActiveCondition(teamSanity))
            {
                EnterPhase(GhostPhase.Idle);
                return;
            }

            SecondsUntilNextRoll -= deltaTime;
            if (SecondsUntilNextRoll > 0f)
                return;

            SecondsUntilNextRoll += _settings.AttackRollInterval;

            // §7.1 전제: 팀 평균 60 이하 + 생존자 존재. 조건을 못 채우면 판정 자체를 하지 않는다.
            if (!hasLivingPlayers || teamSanity > _settings.AttackTeamSanity)
                return;

            float chance = _settings.AttackChanceForTeamSanity(teamSanity);
            if (chance > 0f && _roll() < chance)
                EnterPhase(GhostPhase.Warning);
        }

        private void TickAttack(int teamSanity)
        {
            if (PhaseElapsed >= _settings.AttackMaxDuration)
            {
                EnterPhase(GhostPhase.Calming);
                return;
            }

            // 최소 30초 전에는 강제 진정 아이템 말고는 절대 끝나지 않는다(§7.5).
            if (PhaseElapsed >= _settings.AttackMinDuration
                && teamSanity >= _settings.AttackEarlyEndTeamSanity)
            {
                EnterPhase(GhostPhase.Calming);
            }
        }

        private void RecheckAfterCalm(int teamSanity)
        {
            EnterPhase(HasActiveCondition(teamSanity) ? GhostPhase.Active : GhostPhase.Idle);
        }

        private void HandleCleaningTrigger(int cleaningProgress)
        {
            if (_cleaningTriggerConsumed || cleaningProgress < _settings.CleaningActivateProgress)
                return;

            // 40% 는 일회성 트리거로 취급한다(G-1). 유지되는 활동 조건으로 박아 두지 않고,
            // 도달 순간 활동으로 밀어 넣은 뒤 방해 증가 구간(§6.2, [임시] 90초)만 켠다.
            // 그 구간이 지나면 다른 활동 조건이 없는 한 평상시로 돌아올 수 있다.
            _cleaningTriggerConsumed = true;
            _cleaningBoostRemaining = _settings.CleaningBoostDuration;

            if (Phase == GhostPhase.Idle)
                EnterPhase(GhostPhase.Active);
        }

        private bool HasActiveCondition(int teamSanity)
        {
            return _forceActive
                || teamSanity <= _settings.ActiveTeamSanity
                || CleaningBoostActive;
        }

        private void EnterPhase(GhostPhase phase)
        {
            Phase = phase;
            PhaseElapsed = 0f;
            SecondsUntilNextRoll = phase == GhostPhase.Active ? _settings.AttackRollInterval : 0f;
        }
    }
}
