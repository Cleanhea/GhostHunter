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

            internal Snapshot(GhostPhase phase, float phaseElapsed, float secondsUntilNextRoll,
                bool cleaningThresholdReached, bool highRiskAttack,
                bool attackStartedBelowRecoveryThreshold, int lastTeamSanity)
            {
                Phase = phase;
                PhaseElapsed = phaseElapsed;
                SecondsUntilNextRoll = secondsUntilNextRoll;
                CleaningThresholdReached = cleaningThresholdReached;
                HighRiskAttack = highRiskAttack;
                AttackStartedBelowRecoveryThreshold = attackStartedBelowRecoveryThreshold;
                LastTeamSanity = lastTeamSanity;
            }
        }

        private static readonly System.Random SharedRandom = new();

        private readonly GhostPrototypeSettings _settings;
        private readonly Func<double> _roll;
        private bool _cleaningThresholdReached;
        private bool _highRiskAttack;
        private bool _attackStartedBelowRecoveryThreshold;
        private int _lastTeamSanity = 100;

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

        internal void ResetForStage()
        {
            Phase = GhostPhase.Active;
            PhaseElapsed = 0f;
            SecondsUntilNextRoll = _settings.AttackRollInterval;
            _cleaningThresholdReached = false;
            _highRiskAttack = false;
            _attackStartedBelowRecoveryThreshold = false;
            _lastTeamSanity = 100;
        }

        internal Snapshot CaptureSnapshot() => new(Phase, PhaseElapsed, SecondsUntilNextRoll,
            _cleaningThresholdReached, _highRiskAttack,
            _attackStartedBelowRecoveryThreshold, _lastTeamSanity);

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
            if (cleaningProgress >= _settings.CleaningActivateProgress)
                _cleaningThresholdReached = true;

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
            if (PhaseElapsed >= duration)
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
            Phase = phase;
            PhaseElapsed = 0f;
            SecondsUntilNextRoll = phase == GhostPhase.Active
                ? _settings.AttackRollInterval
                : 0f;
        }
    }
}
