using System;
using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.Sanity
{
    /// <summary>개인 정신력의 감소 누적, 회복, 시체 중복 방지를 계산한다.</summary>
    internal sealed class SanityState
    {
        private readonly SanitySystemSettings _settings;
        private readonly HashSet<ulong> _witnessedCorpses = new();

        public int Value { get; private set; }
        public bool IsAlive { get; private set; }
        public float DarknessExposureSeconds { get; private set; }
        public SanityDebuffFlags Debuffs =>
            SanityMath.ResolveDebuffs(Value, IsAlive, _settings);

        public SanityState(SanitySystemSettings settings)
        {
            _settings = settings != null
                ? settings
                : throw new ArgumentNullException(nameof(settings));

            ResetForStage();
        }

        public void ResetForStage()
        {
            Value = _settings.StartingSanity;
            IsAlive = true;
            DarknessExposureSeconds = 0f;
            _witnessedCorpses.Clear();
        }

        public bool TickDarkness(float deltaTime, bool isExposed)
        {
            if (!IsAlive || !isExposed || deltaTime <= 0f)
                return false;

            DarknessExposureSeconds += deltaTime;
            int intervals = Mathf.FloorToInt(
                DarknessExposureSeconds / _settings.DarknessInterval);
            if (intervals <= 0)
                return false;

            DarknessExposureSeconds -= intervals * _settings.DarknessInterval;
            return SetValue(Value - intervals * _settings.DarknessDecrease);
        }

        public bool ApplyGhostEvent()
        {
            return IsAlive && SetValue(Value - _settings.GhostEventDecrease);
        }

        public bool WitnessCorpse(ulong corpseNetworkObjectId)
        {
            if (!IsAlive || !_witnessedCorpses.Add(corpseNetworkObjectId))
                return false;

            SetValue(Value - _settings.CorpseWitnessDecrease);
            return true;
        }

        public bool Restore(int amount)
        {
            return IsAlive && amount > 0 && SetValue(Value + amount);
        }

        /// <summary>디버그 HUD 전용: 개인 정신력을 지정 값으로 즉시 맞춘다. 사망 상태에서는 무시한다.</summary>
        public bool SetTo(int value)
        {
            if (!IsAlive)
                return false;

            Value = Mathf.Clamp(value, _settings.MinimumSanity, _settings.MaximumSanity);
            return true;
        }

        public bool MarkDead()
        {
            if (!IsAlive)
                return false;

            IsAlive = false;
            return true;
        }

        /// <summary>
        /// 사망한 플레이어를 다시 생존으로 되돌린다. 정신력 값과 목격한 시체 기록은 그대로 둔다 —
        /// 죽기 직전 상태로 돌아오는 것이지 스테이지를 다시 시작하는 것이 아니다
        /// (그건 <see cref="ResetForStage"/>).
        /// </summary>
        public bool Revive()
        {
            if (IsAlive)
                return false;

            IsAlive = true;

            // 죽어 있는 동안 TickDarkness 가 멈춰 있었으므로 누적분이 남아 있다.
            // 그대로 두면 부활 직후 남은 조각이 즉시 1틱을 깎는다.
            DarknessExposureSeconds = 0f;
            return true;
        }

        private bool SetValue(int value)
        {
            int clamped = Mathf.Clamp(value, _settings.MinimumSanity, _settings.MaximumSanity);
            if (Value == clamped)
                return false;

            Value = clamped;
            return true;
        }
    }
}
