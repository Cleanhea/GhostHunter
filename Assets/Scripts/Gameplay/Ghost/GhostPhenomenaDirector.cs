using System;
using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 귀신 시스템 기획서 §6.1·§6.4 의 초자연현상 선정 루프를 계산한다. 현재 상태와 팀 평균
    /// 정신력을 입력으로 받아 발생 주기를 정하고, 발생할 때는 활성 Pool에서 **직전 현상을 뺀**
    /// 균등 확률로 하나를 고른다(§6.4 6단계). NGO·씬에 의존하지 않는 순수 로직이라
    /// <see cref="GhostStateMachine"/> 과 같은 방식으로 EditMode 로 검증한다.
    /// 난수는 <see cref="Func{Double}"/> 로 주입해 테스트에서 결정적으로 만든다.
    /// </summary>
    internal sealed class GhostPhenomenaDirector
    {
        private static readonly System.Random SharedRandom = new();

        private readonly GhostPrototypeSettings _settings;
        private readonly Func<double> _roll;
        private readonly List<GhostPhenomenonKind> _pool = new(8);

        private float _cooldownRemaining;
        private GhostPhenomenonKind _last;

        internal GhostPhenomenaDirector(GhostPrototypeSettings settings, Func<double> roll = null)
        {
            _settings = settings != null
                ? settings
                : throw new ArgumentNullException(nameof(settings));
            _roll = roll ?? SharedRandom.NextDouble;

            RebuildPool();
            ResetForStage();
        }

        /// <summary>가장 최근에 발생한 현상. 다음 선택에서 제외된다(§6.4 6단계).</summary>
        internal GhostPhenomenonKind Last => _last;

        /// <summary>다음 현상 발생까지 남은 시간(초). 발생 불가 상태에서는 현재 구간의 주기.</summary>
        internal float SecondsUntilNext => _cooldownRemaining;

        internal void ResetForStage()
        {
            _last = GhostPhenomenonKind.None;
            _cooldownRemaining = _settings.PhenomenaIdleInterval;
        }

        /// <summary>
        /// 한 틱 진행한다. 이번 틱에 현상이 발생하면 그 종류를, 아니면
        /// <see cref="GhostPhenomenonKind.None"/> 을 반환한다.
        /// </summary>
        internal GhostPhenomenonKind Tick(
            float deltaTime,
            GhostPhase phase,
            int teamSanity,
            bool cleaningBoostActive)
        {
            if (deltaTime < 0f)
                deltaTime = 0f;

            // §4.1 표: 초자연현상은 평상시·활동에서만 발생한다. 경고·어택·진정 중에는 멈추고,
            // 타이머를 현재 구간 주기로 되돌려 상태 복귀 직후 곧바로 터지지 않게 한다.
            if (phase is not (GhostPhase.Idle or GhostPhase.Active))
            {
                _cooldownRemaining = IntervalFor(phase, teamSanity, cleaningBoostActive);
                return GhostPhenomenonKind.None;
            }

            _cooldownRemaining -= deltaTime;
            if (_cooldownRemaining > 0f)
                return GhostPhenomenonKind.None;

            _cooldownRemaining = IntervalFor(phase, teamSanity, cleaningBoostActive);
            return Fire();
        }

        /// <summary>F1 디버그 — 주기를 무시하고 지금 현상 하나를 강제로 뽑는다.</summary>
        internal GhostPhenomenonKind ForceNext(
            GhostPhase phase,
            int teamSanity,
            bool cleaningBoostActive)
        {
            _cooldownRemaining = IntervalFor(phase, teamSanity, cleaningBoostActive);
            return Fire();
        }

        private GhostPhenomenonKind Fire()
        {
            int count = _pool.Count;
            if (count == 0)
                return GhostPhenomenonKind.None;

            if (count == 1)
            {
                _last = _pool[0];
                return _last;
            }

            // §6.4 6단계: 직전 현상은 이번 선택 Pool에서 제외한다.
            bool excludeLast = _last != GhostPhenomenonKind.None && _pool.Contains(_last);
            int choices = excludeLast ? count - 1 : count;

            int index = (int)(_roll() * choices);
            if (index >= choices)
                index = choices - 1;
            if (index < 0)
                index = 0;

            for (int i = 0; i < count; i++)
            {
                if (excludeLast && _pool[i] == _last)
                    continue;

                if (index == 0)
                {
                    _last = _pool[i];
                    return _last;
                }

                index--;
            }

            _last = _pool[count - 1];
            return _last;
        }

        private float IntervalFor(GhostPhase phase, int teamSanity, bool cleaningBoostActive)
        {
            if (phase == GhostPhase.Idle)
                return _settings.PhenomenaIdleInterval;

            // §6.1: 팀 평균 30 이하이거나 청소 40% 방해 구간(§6.2)이면 활동보다 더 잦다.
            if (teamSanity <= _settings.HighRiskTeamSanity || cleaningBoostActive)
                return _settings.PhenomenaHighRiskInterval;

            return _settings.PhenomenaActiveInterval;
        }

        private void RebuildPool()
        {
            _pool.Clear();
            _pool.Add(GhostPhenomenonKind.ObjectShake);
            _pool.Add(GhostPhenomenonKind.SmallObjectDrop);
            _pool.Add(GhostPhenomenonKind.DoorMove);
            _pool.Add(GhostPhenomenonKind.DrawerOpen);
            _pool.Add(GhostPhenomenonKind.LightFlicker);
            _pool.Add(GhostPhenomenonKind.Apparition);

            if (_settings.SoundPhenomenaEnabled)
            {
                _pool.Add(GhostPhenomenonKind.WallKnock);
                _pool.Add(GhostPhenomenonKind.Footsteps);
            }
        }
    }
}
