using System;
using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>팀 정신력과 청소 진행도에 따른 서버 권위 귀신 상태 계산.</summary>
    internal sealed class GhostStateMachine
    {
        internal readonly struct Snapshot
        {
            internal readonly GhostPhase Phase;
            internal readonly float PhaseElapsed;
            internal readonly float SecondsUntilNextRoll;
            internal readonly bool CleaningThresholdReached;
            internal readonly bool HighRiskAttack;
            internal readonly bool AttackStartedBelowRecoveryThreshold;
            internal readonly int LastTeamSanity;
            internal readonly bool CleaningAttackPending;
            internal readonly bool CleaningAttackFired;

            internal Snapshot(GhostPhase phase, float phaseElapsed, float secondsUntilNextRoll,
                bool cleaningThresholdReached, bool highRiskAttack,
                bool attackStartedBelowRecoveryThreshold, int lastTeamSanity,
                bool cleaningAttackPending = false, bool cleaningAttackFired = true)
            {
                Phase = phase;
                PhaseElapsed = phaseElapsed;
                SecondsUntilNextRoll = secondsUntilNextRoll;
                CleaningThresholdReached = cleaningThresholdReached;
                HighRiskAttack = highRiskAttack;
                AttackStartedBelowRecoveryThreshold = attackStartedBelowRecoveryThreshold;
                LastTeamSanity = lastTeamSanity;
                CleaningAttackPending = cleaningAttackPending;
                CleaningAttackFired = cleaningAttackFired;
            }
        }

        private static readonly System.Random SharedRandom = new();

        private readonly GhostPrototypeSettings _settings;
        private readonly Func<double> _roll;
        private bool _cleaningThresholdReached;
        private bool _highRiskAttack;
        private bool _attackStartedBelowRecoveryThreshold;
        private int _lastTeamSanity = 100;
        // 청소 40~50% 일회성 강제 어택(원문 0.3): 40%를 처음 넘으면 대기(pending)로 두었다가 활동 상태에서 경고를 시작한다.
        private bool _cleaningAttackPending;
        private bool _cleaningAttackFired;

        internal GhostStateMachine(GhostPrototypeSettings settings, Func<double> roll = null)
        {
            _settings = settings != null
                ? settings
                : throw new ArgumentNullException(nameof(settings));
            _roll = roll ?? SharedRandom.NextDouble;
            ResetForStage();
        }

        internal GhostPhase Phase { get; private set; }
        internal float PhaseElapsed { get; private set; }
        internal float SecondsUntilNextRoll { get; private set; }
        internal bool CleaningBoostActive => false; // 이전 프로토타입 API 호환. 청소는 이제 영구 발동 조건이다.
        internal bool CleaningThresholdReached => _cleaningThresholdReached;
        internal bool IsHighRiskAttack => _highRiskAttack && Phase == GhostPhase.Attack;
        internal bool IsAttackSequenceLocked =>
            Phase is GhostPhase.Warning or GhostPhase.Attack or GhostPhase.Calming;

        /// <summary>
        /// 켜져 있는 동안은 어택 **시간이 다 돼도** 어택을 끝내지 않는다(박치기 공룡: 준비·돌진 중이면 돌진까지 마친 뒤 종료).
        /// 조기 종료(정신력 회복)·강제 진정 아이템은 막지 않는다. 풀리면 다음 틱에 시간 종료가 적용된다.
        /// </summary>
        internal bool HoldAttackEnd { get; set; }

        internal void ResetForStage()
        {
            Phase = GhostPhase.Active;
            PhaseElapsed = 0f;
            SecondsUntilNextRoll = _settings.AttackRollInterval;
            _cleaningThresholdReached = false;
            _highRiskAttack = false;
            _attackStartedBelowRecoveryThreshold = false;
            _lastTeamSanity = 100;
            _cleaningAttackPending = false;
            _cleaningAttackFired = false;
        }

        /// <summary>청소 진행도 40%를 처음 넘어 강제 어택을 기다리는 중인가(활동 상태가 아니라서 아직 못 시작함).</summary>
        internal bool CleaningAttackPending => _cleaningAttackPending;

        /// <summary>청소 40% 일회성 강제 어택이 이미 쓰였는가.</summary>
        internal bool CleaningAttackFired => _cleaningAttackFired;

        internal Snapshot CaptureSnapshot() => new(Phase, PhaseElapsed, SecondsUntilNextRoll,
            _cleaningThresholdReached, _highRiskAttack,
            _attackStartedBelowRecoveryThreshold, _lastTeamSanity,
            _cleaningAttackPending, _cleaningAttackFired);

        internal void RestoreSnapshot(Snapshot snapshot)
        {
            if (!Enum.IsDefined(typeof(GhostPhase), snapshot.Phase))
                throw new ArgumentOutOfRangeException(nameof(snapshot));
            Phase = snapshot.Phase;
            PhaseElapsed = Mathf.Max(0f, snapshot.PhaseElapsed);
            SecondsUntilNextRoll = Mathf.Max(0f, snapshot.SecondsUntilNextRoll);
            _cleaningThresholdReached = snapshot.CleaningThresholdReached;
            _highRiskAttack = snapshot.HighRiskAttack;
            _attackStartedBelowRecoveryThreshold = snapshot.AttackStartedBelowRecoveryThreshold;
            _lastTeamSanity = snapshot.LastTeamSanity;
            _cleaningAttackPending = snapshot.CleaningAttackPending;
            _cleaningAttackFired = snapshot.CleaningAttackFired;
        }

        /// <summary>귀신별 특수 조건에서 즉시 경고를 시작한다.</summary>
        internal bool ForceSpecialAttack()
        {
            if (Phase != GhostPhase.Active)
                return false;

            _highRiskAttack = _lastTeamSanity <= _settings.HighRiskTeamSanity;
            EnterPhase(GhostPhase.Warning);
            return true;
        }

        /// <summary>어택 종료 아이템. 현재 어택을 즉시 끝내고 30초 자연 진정으로 들어간다.</summary>
        internal bool ForceSuppression()
        {
            if (Phase != GhostPhase.Attack)
                return false;

            EnterPhase(GhostPhase.Calming);
            return true;
        }

        internal GhostPhase Tick(float deltaTime, int teamSanity, bool hasLivingPlayers, int cleaningProgress)
        {
            deltaTime = Mathf.Max(0f, deltaTime);
            PhaseElapsed += deltaTime;
            _lastTeamSanity = teamSanity;
            if (cleaningProgress >= _settings.CleaningActivateProgress && !_cleaningThresholdReached)
            {
                _cleaningThresholdReached = true;
                // 40%를 처음 넘는 순간 한 번만. 어택·자연 진정 중이면 활동으로 돌아올 때까지 기다린다(G-22 임시 해석).
                if (_settings.CleaningForcesAttackOnce && !_cleaningAttackFired)
                    _cleaningAttackPending = true;
            }

            switch (Phase)
            {
                case GhostPhase.Active:
                    TickActive(deltaTime, teamSanity, hasLivingPlayers);
                    break;

                case GhostPhase.Warning:
                    if (PhaseElapsed >= _settings.WarningDuration)
                    {
                        _highRiskAttack |= teamSanity <= _settings.HighRiskTeamSanity;
                        _attackStartedBelowRecoveryThreshold =
                            teamSanity < _settings.AttackEarlyEndTeamSanity;
                        EnterPhase(GhostPhase.Attack);
                    }
                    break;

                case GhostPhase.Attack:
                    TickAttack(teamSanity);
                    break;

                case GhostPhase.Calming:
                    if (PhaseElapsed >= _settings.CalmingDuration)
                        RecheckAfterCalm(teamSanity, hasLivingPlayers);
                    break;
            }

            return Phase;
        }

        private void TickActive(float deltaTime, int teamSanity, bool hasLivingPlayers)
        {
            if (!hasLivingPlayers || !_cleaningThresholdReached)
                return;

            // 청소 진행도 40%를 처음 넘으면 정신력과 상관없이 무조건 한 번 어택한다(원문 0.3).
            if (_cleaningAttackPending)
            {
                _highRiskAttack = teamSanity <= _settings.HighRiskTeamSanity;
                EnterPhase(GhostPhase.Warning);
                return;
            }

            // 0~30은 확률 판정 없이 즉시 경고를 시작한다.
            if (teamSanity <= _settings.HighRiskTeamSanity)
            {
                _highRiskAttack = true;
                EnterPhase(GhostPhase.Warning);
                return;
            }

            SecondsUntilNextRoll -= deltaTime;
            if (SecondsUntilNextRoll > 0f)
                return;

            SecondsUntilNextRoll = _settings.AttackRollInterval;
            if (teamSanity <= _settings.AttackTeamSanity
                && _roll() < _settings.AttackChanceForTeamSanity(teamSanity))
            {
                _highRiskAttack = false;
                EnterPhase(GhostPhase.Warning);
            }
        }

        private void TickAttack(int teamSanity)
        {
            if (_attackStartedBelowRecoveryThreshold
                && PhaseElapsed <= _settings.AttackEarlyEndWindow
                && teamSanity >= _settings.AttackEarlyEndTeamSanity)
            {
                EnterPhase(GhostPhase.Calming);
                return;
            }

            float duration = _highRiskAttack
                ? _settings.HighRiskAttackDuration
                : _settings.AttackMaxDuration;
            // 팀 정신력이 0~30이면 시간이 지나도 계속 어택한다(원문 0.3). 고위험 90초도 이 구간에서는 끝나지 않는다.
            bool critical = _settings.AttackContinuesWhileCritical && teamSanity <= _settings.HighRiskTeamSanity;
            if (PhaseElapsed >= duration && !HoldAttackEnd && !critical)
                EnterPhase(GhostPhase.Calming);
        }

        private void RecheckAfterCalm(int teamSanity, bool hasLivingPlayers)
        {
            EnterPhase(GhostPhase.Active);
            if (!hasLivingPlayers || !_cleaningThresholdReached)
                return;

            if (teamSanity <= _settings.HighRiskTeamSanity)
            {
                _highRiskAttack = true;
                EnterPhase(GhostPhase.Warning);
            }
            else if (teamSanity <= _settings.AttackTeamSanity
                && _roll() < _settings.AttackChanceForTeamSanity(teamSanity))
            {
                _highRiskAttack = false;
                EnterPhase(GhostPhase.Warning);
            }
        }

        private void EnterPhase(GhostPhase phase)
        {
            // 어택이 시작되면(어떤 경로든) 청소 40% 강제 어택의 몫은 끝난 것으로 본다.
            if (phase == GhostPhase.Warning && _cleaningThresholdReached)
            {
                _cleaningAttackPending = false;
                _cleaningAttackFired = true;
            }

            Phase = phase;
            PhaseElapsed = 0f;
            SecondsUntilNextRoll = phase == GhostPhase.Active
                ? _settings.AttackRollInterval
                : 0f;
        }
    }
}
