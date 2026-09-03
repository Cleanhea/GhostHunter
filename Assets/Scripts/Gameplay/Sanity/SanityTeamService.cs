using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Sanity
{
    /// <summary>플레이어 정신력을 등록하고 생존자 팀 평균과 F1 검증 기능을 제공한다.</summary>
    [DisallowMultipleComponent]
    public sealed class SanityTeamService : MonoBehaviour, ISanityTeamService, ISanityDebug
    {
        private const int DebugRestoreAmount = 10;
        private const ulong NoDebugCorpse = ulong.MaxValue;
        private const ulong FirstDebugCorpse = 0xD000000000000001;

        private readonly List<SanityNetworkState> _states = new(4);
        private readonly StringBuilder _statusBuilder = new(256);

        private string _lastStatus = "정신력 시스템 대기";
        private ulong _nextDebugCorpse = FirstDebugCorpse;
        private ulong _lastDebugCorpse = NoDebugCorpse;

        public bool CanControl
        {
            get
            {
                NetworkManager network = NetworkManager.Singleton;
                return network != null && network.IsServer && TryGetLocalState(out _);
            }
        }

        public string StatusSummary
        {
            get
            {
                BuildStatusSummary();
                return _statusBuilder.ToString();
            }
        }

        public string LastStatus => _lastStatus;

        public int SanityMinimum =>
            TryGetLocalState(out SanityNetworkState state) ? state.MinimumSanity : 0;

        public int SanityMaximum =>
            TryGetLocalState(out SanityNetworkState state) ? state.MaximumSanity : 100;

        public void Register(SanityNetworkState state)
        {
            if (state == null || _states.Contains(state))
                return;

            _states.Add(state);
        }

        public void Unregister(SanityNetworkState state)
        {
            if (state != null)
                _states.Remove(state);
        }

        public int CopyPlayerStates(SanityNetworkState[] destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            Array.Clear(destination, 0, destination.Length);
            int copiedCount = 0;

            for (int i = _states.Count - 1; i >= 0; i--)
            {
                SanityNetworkState state = _states[i];
                if (state == null)
                {
                    _states.RemoveAt(i);
                    continue;
                }

                if (!state.IsSpawned)
                    continue;

                int insertionIndex = 0;
                while (insertionIndex < copiedCount
                    && destination[insertionIndex].OwnerClientId < state.OwnerClientId)
                {
                    insertionIndex++;
                }

                if (insertionIndex >= destination.Length)
                    continue;

                int moveEnd = Mathf.Min(copiedCount, destination.Length - 1);
                for (int moveIndex = moveEnd; moveIndex > insertionIndex; moveIndex--)
                    destination[moveIndex] = destination[moveIndex - 1];

                destination[insertionIndex] = state;
                if (copiedCount < destination.Length)
                    copiedCount++;
            }

            return copiedCount;
        }

        public bool TryGetTeamAverage(
            out float exactAverage,
            out int roundedAverage,
            out int livingPlayerCount)
        {
            int totalSanity = 0;
            livingPlayerCount = 0;

            for (int i = _states.Count - 1; i >= 0; i--)
            {
                SanityNetworkState state = _states[i];
                if (state == null)
                {
                    _states.RemoveAt(i);
                    continue;
                }

                if (!state.IsSpawned || !state.HasSanity)
                    continue;

                totalSanity += state.Sanity;
                livingPlayerCount++;
            }

            if (livingPlayerCount == 0)
            {
                exactAverage = 0f;
                roundedAverage = 0;
                return false;
            }

            exactAverage = (float)totalSanity / livingPlayerCount;
            roundedAverage = SanityMath.RoundTeamAverage(totalSanity, livingPlayerCount);
            return true;
        }

        public void ToggleLocalDarkness()
        {
            if (!TryGetControllableLocalState(out SanityNetworkState state))
                return;

            bool next = !state.IsDarknessExposed;
            if (!state.ServerSetDarknessExposed(next))
            {
                _lastStatus = "어둠 노출 변경 실패: 생존 플레이어가 필요합니다.";
                return;
            }

            _lastStatus = next ? "어둠 노출을 시작했습니다." : "어둠 노출을 일시 정지했습니다.";
        }

        public void ApplyLocalGhostEvent()
        {
            if (!TryGetControllableLocalState(out SanityNetworkState state))
                return;

            _lastStatus = state.ServerApplyGhostEventWitnessed()
                ? "귀신 이벤트 목격: 정신력 15 감소."
                : "귀신 이벤트 적용 없음.";
        }

        public void WitnessNewCorpse()
        {
            if (!TryGetControllableLocalState(out SanityNetworkState state))
                return;

            ulong corpseId = _nextDebugCorpse;
            _nextDebugCorpse++;
            _lastDebugCorpse = corpseId;

            _lastStatus = state.ServerApplyCorpseWitnessed(corpseId)
                ? "새 시체 목격: 정신력 20 감소."
                : "새 시체 목격 적용 없음.";
        }

        public void WitnessSameCorpse()
        {
            if (!TryGetControllableLocalState(out SanityNetworkState state))
                return;

            if (_lastDebugCorpse == NoDebugCorpse)
            {
                _lastStatus = "먼저 새 시체 목격을 실행하세요.";
                return;
            }

            _lastStatus = state.ServerApplyCorpseWitnessed(_lastDebugCorpse)
                ? "동일 시체가 다시 적용됐습니다. 중복 방지 오류입니다."
                : "동일 시체 재목격: 정신력 감소 없음.";
        }

        public void RestoreLocalSanity()
        {
            if (!TryGetControllableLocalState(out SanityNetworkState state))
                return;

            _lastStatus = state.ServerRestoreSanity(DebugRestoreAmount)
                ? $"디버그 아이템: 정신력 {DebugRestoreAmount} 회복."
                : "정신력 회복 적용 없음.";
        }

        public bool TryGetLocalSanity(out int sanity)
        {
            if (TryGetLocalState(out SanityNetworkState state) && state.HasSanity)
            {
                sanity = state.Sanity;
                return true;
            }

            sanity = 0;
            return false;
        }

        public void SetLocalSanity(int value)
        {
            if (!TryGetControllableLocalState(out SanityNetworkState state))
                return;

            _lastStatus = state.ServerSetSanity(value)
                ? $"정신력을 {state.Sanity}%로 설정했습니다."
                : "정신력 설정 적용 없음 (사망 상태).";
        }

        public bool TryGetTeamSanity(out int roundedAverage)
        {
            return TryGetTeamAverage(out _, out roundedAverage, out _);
        }

        public void SetTeamSanity(int value)
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer)
            {
                _lastStatus = "팀 정신력 제어 실패: 호스트(서버)가 필요합니다.";
                return;
            }

            int appliedCount = 0;
            for (int i = _states.Count - 1; i >= 0; i--)
            {
                SanityNetworkState state = _states[i];
                if (state == null)
                {
                    _states.RemoveAt(i);
                    continue;
                }

                if (state.IsSpawned && state.ServerSetSanity(value))
                    appliedCount++;
            }

            _lastStatus = appliedCount > 0
                ? $"팀 {appliedCount}명의 정신력을 {value}%로 설정했습니다."
                : "팀 정신력 설정 대상이 없습니다 (전원 사망·미접속).";
        }

        public void MarkLocalPlayerDead()
        {
            if (!TryGetControllableLocalState(out SanityNetworkState state))
                return;

            _lastStatus = state.ServerMarkDead()
                ? "플레이어 사망 처리: 팀 평균에서 제외."
                : "사망 처리 적용 없음.";
        }

        public void ReviveLocalPlayer()
        {
            if (!TryGetControllableLocalState(out SanityNetworkState state))
                return;

            _lastStatus = state.ServerRevive()
                ? "플레이어 부활: 팀 평균에 다시 포함."
                : "부활 적용 없음 (이미 생존 중).";
        }

        public void ReviveTeam()
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer)
            {
                _lastStatus = "팀 부활 실패: 호스트(서버)가 필요합니다.";
                return;
            }

            int revivedCount = 0;
            for (int i = _states.Count - 1; i >= 0; i--)
            {
                SanityNetworkState state = _states[i];
                if (state == null)
                {
                    _states.RemoveAt(i);
                    continue;
                }

                if (state.IsSpawned && state.ServerRevive())
                    revivedCount++;
            }

            _lastStatus = revivedCount > 0
                ? $"팀 {revivedCount}명을 부활시켰습니다."
                : "부활 대상이 없습니다 (전원 생존 중이거나 미접속).";
        }

        public void ResetLocalPlayerForStage()
        {
            if (!TryGetControllableLocalState(out SanityNetworkState state))
                return;

            _lastStatus = state.ServerResetForStage()
                ? "스테이지 정신력을 100으로 초기화했습니다."
                : "정신력 초기화 실패.";
        }

        private void BuildStatusSummary()
        {
            _statusBuilder.Clear();

            if (_states.Count == 0)
            {
                _statusBuilder.Append("정신력 플레이어 상태 없음");
                return;
            }

            for (int i = 0; i < _states.Count; i++)
            {
                SanityNetworkState state = _states[i];
                if (state == null || !state.IsSpawned)
                    continue;

                _statusBuilder.Append("Client ");
                _statusBuilder.Append(state.OwnerClientId);
                if (state.IsOwner)
                    _statusBuilder.Append(" (나)");
                _statusBuilder.Append(": ");

                if (state.HasSanity)
                {
                    _statusBuilder.Append(state.Sanity);
                    _statusBuilder.Append("% / ");
                    _statusBuilder.Append(DebuffLabel(state.CurrentDebuffs));
                    if (state.IsDarknessExposed)
                    {
                        _statusBuilder.Append(" / 어둠 ");
                        _statusBuilder.Append(state.DarknessExposureSeconds.ToString("0.0"));
                        _statusBuilder.Append('s');
                    }
                }
                else
                {
                    _statusBuilder.Append("-% / 사망");
                }

                _statusBuilder.AppendLine();
            }

            if (TryGetTeamAverage(out float exact, out int rounded, out int living))
            {
                _statusBuilder.Append("팀 평균 ");
                _statusBuilder.Append(exact.ToString("0.00"));
                _statusBuilder.Append(" → ");
                _statusBuilder.Append(rounded);
                _statusBuilder.Append("% / 생존 ");
                _statusBuilder.Append(living);
            }
            else
            {
                _statusBuilder.Append("팀 평균 -% / 생존 0");
            }
        }

        private bool TryGetControllableLocalState(out SanityNetworkState state)
        {
            state = null;
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer || !TryGetLocalState(out state))
            {
                _lastStatus = "정신력 제어 실패: Local Host의 플레이어가 필요합니다.";
                return false;
            }

            return true;
        }

        public bool TryGetLocalState(out SanityNetworkState state)
        {
            for (int i = 0; i < _states.Count; i++)
            {
                SanityNetworkState candidate = _states[i];
                if (candidate != null && candidate.IsSpawned && candidate.IsOwner)
                {
                    state = candidate;
                    return true;
                }
            }

            state = null;
            return false;
        }

        private static string DebuffLabel(SanityDebuffFlags flags)
        {
            return flags switch
            {
                SanityDebuffFlags.None => "정상",
                SanityDebuffFlags.CameraNoise => "노이즈",
                SanityDebuffFlags.CameraNoise | SanityDebuffFlags.Whisper => "노이즈+속삭임",
                SanityDebuffFlags.CameraNoise
                    | SanityDebuffFlags.Whisper
                    | SanityDebuffFlags.BreathingHeartbeat => "노이즈+속삭임+심장",
                _ => flags.ToString(),
            };
        }
    }
}
