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

        public bool MarkDead()
        {
            if (!IsAlive)
                return false;

            IsAlive = false;
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
