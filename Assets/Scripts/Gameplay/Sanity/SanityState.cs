using System;
using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.Sanity
{
    /// <summary>개인 정신력의 감소, 회복, 시체 중복 방지를 계산한다.</summary>
    internal sealed class SanityState
    {
        internal readonly struct Snapshot
        {
            internal readonly int Value;
            internal readonly bool IsAlive;
            internal readonly ulong[] WitnessedCorpses;

            internal Snapshot(int value, bool isAlive, ulong[] witnessedCorpses)
            {
                Value = value;
                IsAlive = isAlive;
                WitnessedCorpses = witnessedCorpses;
            }
        }

        private readonly SanitySystemSettings _settings;
        private readonly HashSet<ulong> _witnessedCorpses = new();

        public int Value { get; private set; }
        public bool IsAlive { get; private set; }
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
            _witnessedCorpses.Clear();
        }

        internal Snapshot CaptureSnapshot()
        {
            ulong[] corpses = new ulong[_witnessedCorpses.Count];
            _witnessedCorpses.CopyTo(corpses);
            Array.Sort(corpses);
            return new Snapshot(Value, IsAlive, corpses);
        }

        internal void RestoreSnapshot(Snapshot snapshot)
        {
            Value = Mathf.Clamp(snapshot.Value, _settings.MinimumSanity, _settings.MaximumSanity);
            IsAlive = snapshot.IsAlive;
            _witnessedCorpses.Clear();
            if (snapshot.WitnessedCorpses != null)
                foreach (ulong corpse in snapshot.WitnessedCorpses)
                    _witnessedCorpses.Add(corpse);
        }

        public bool ApplyGhostEvent()
        {
            return IsAlive && SetValue(Value - _settings.GhostEventDecrease);
        }

        public bool WitnessGhostBody()
        {
            return IsAlive && SetValue(Value - _settings.GhostBodyWitnessDecrease);
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
