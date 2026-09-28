using System;
using GhostHunter.Core;
using GhostHunter.Core.Scenes;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Recovery;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Sanity
{
    /// <summary>플레이어 개인 정신력을 서버에서 변경하고 모든 클라이언트에 복제한다.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class SanityNetworkState : NetworkBehaviour
    {
        [SerializeField] private SanitySystemSettings _settings;

        private readonly NetworkVariable<int> _sanity = new(
            100,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _isAlive = new(
            true,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _isDarknessExposed = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private SanityState _serverState;
        private ISanityTeamService _teamService;
        private PlayerMotor _playerMotor;
        private MoleBurrowController _burrowController;
        private PlayerHeadlamp _headlamp;
        private PlayerLighter _lighter;

        public int Sanity => _sanity.Value;
        public int MinimumSanity => _settings != null ? _settings.MinimumSanity : 0;
        public int MaximumSanity => _settings != null ? _settings.MaximumSanity : 100;
        public bool HasSanity => _isAlive.Value;
        public bool IsCrouching => _playerMotor != null && _playerMotor.IsCrouching;

        /// <summary>
        /// 엎드려 있는지(player-controller.md). 귀신은 이 값을 <see cref="IsCrouching"/> 과 같이
        /// 소리 탐지 제외에 쓰고, 침대 밑 은신(<c>BedHideEvaluator</c>) 후보 판정에도 읽는다.
        /// </summary>
        public bool IsProne => _playerMotor != null && _playerMotor.IsProne;

        /// <summary>
        /// 굴착 스킬로 땅속에 <b>숨어</b> 있는지(두더지 스킬 시스템 기획서 §5.2). 귀신의 탐지 판정이
        /// 이 값을 보고 완전히 건너뛴다 — <see cref="IsCrouching"/> 과 같은 Owner-authoritative
        /// 패턴이라 서버에서도 신뢰할 수 있다.
        ///
        /// <para>땅굴 안에서 헤드라이트나 라이터를 켜 두면 숨은 것이 아니다(§5.2.1 ②, 라이터는 lighter-system.md) — 이 값이 false 가 되어 귀신은
        /// 땅 위에 서 있는 플레이어처럼 탐지·포획한다. 몸 숨기기·이름표처럼 "땅속에 있는가" 자체가 필요한 곳은
        /// <see cref="MoleBurrowController.IsBurrowed"/> 를 직접 읽는다.</para>
        /// </summary>
        public bool IsBurrowed => _burrowController != null && _burrowController.IsBurrowed
            && (_headlamp == null || !_headlamp.IsOn)
            && (_lighter == null || !_lighter.IsLit);
        public bool IsDarknessExposed => _isDarknessExposed.Value;
        public float DarknessExposureSeconds =>
            _serverState != null ? _serverState.DarknessExposureSeconds : 0f;
        public SanityDebuffFlags CurrentDebuffs => SanityMath.ResolveDebuffs(
            _sanity.Value,
            _isAlive.Value,
            _settings);

        public event Action<int, int> SanityChanged;
        public event Action<SanityDebuffFlags, SanityDebuffFlags> DebuffsChanged;
        public event Action BreathingHeartbeatTriggered;

        /// <summary>
        /// 생존 여부가 바뀔 때마다(사망·부활) 알린다. 관전 시스템(spectator-system.md)이 이
        /// 이벤트로 자유시점 진입·복귀를 판단한다 — 값 자체는 <see cref="HasSanity"/>로 이미
        /// 읽을 수 있었지만, 변화 시점을 알 공개 경로가 없었다.
        /// </summary>
        public event Action<bool> AliveStateChanged;

        private ILocalPlayerContext _localPlayer;

        private void Awake()
        {
            _playerMotor = GetComponent<PlayerMotor>();
            _burrowController = GetComponent<MoleBurrowController>();
            _headlamp = GetComponent<PlayerHeadlamp>();
            _lighter = GetComponent<PlayerLighter>();

            if (_settings != null)
                return;

            Debug.LogError($"{nameof(SanityNetworkState)}: 정신력 설정 에셋이 필요합니다.", this);
            enabled = false;
        }

        public override void OnNetworkSpawn()
        {
            _sanity.OnValueChanged += HandleSanityChanged;
            _isAlive.OnValueChanged += HandleAliveChanged;

            _teamService = Services.Get<ISanityTeamService>();

            if (IsServer)
            {
                _serverState = new SanityState(_settings);
                _isDarknessExposed.Value = false;
                PublishServerState();
            }

            _teamService.Register(this);

            if (IsOwner)
            {
                _localPlayer = Services.Get<ILocalPlayerContext>();
                _localPlayer.Register(this);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (_teamService != null)
                _teamService.Unregister(this);

            _localPlayer?.Unregister(this);
            _localPlayer = null;

            _sanity.OnValueChanged -= HandleSanityChanged;
            _isAlive.OnValueChanged -= HandleAliveChanged;
            _teamService = null;
            _serverState = null;
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || _serverState == null
                || StageRecoveryGate.Restoring)
                return;

            if (_serverState.TickDarkness(Time.deltaTime, _isDarknessExposed.Value))
                _sanity.Value = _serverState.Value;
        }

        /// <summary>스테이지 시작 시 개인 정신력과 목격 기록을 초기화한다.</summary>
        public bool ServerResetForStage()
        {
            if (!CanMutateOnServer() || (_teamService != null && _teamService.IsTeamWiped))
                return false;

            _serverState.ResetForStage();
            _isDarknessExposed.Value = false;
            PublishServerState();
            return true;
        }

        /// <summary>
        /// 헤드라이트·드릴 카 판정이 계산한 최종 어둠 노출 여부를 서버에 적용한다. 게임플레이 호출부는
        /// <see cref="PlayerHeadlamp"/> 하나다 — 판정이 바뀔 때마다 덮어쓰므로 F1 HUD 의 어둠 토글은 다음 판정까지만 간다.
        /// </summary>
        public bool ServerSetDarknessExposed(bool isExposed)
        {
            if (!CanMutateOnServer() || !_serverState.IsAlive)
                return false;

            _isDarknessExposed.Value = isExposed;
            return true;
        }

        /// <summary>이 플레이어가 귀신 이벤트를 목격했을 때 정신력 10을 감소시킨다.</summary>
        public bool ServerApplyGhostEventWitnessed()
        {
            return ApplyServerMutation(_serverState != null && _serverState.ApplyGhostEvent());
        }

        /// <summary>활동 중 귀신 본체를 마주친 플레이어의 정신력을 5 감소시킨다.</summary>
        public bool ServerApplyGhostBodyWitnessed()
        {
            return ApplyServerMutation(_serverState != null && _serverState.WitnessGhostBody());
        }

        /// <summary>시체를 처음 목격했을 때만 정신력 20을 감소시킨다.</summary>
        public bool ServerApplyCorpseWitnessed(ulong corpseNetworkObjectId)
        {
            return ApplyServerMutation(
                _serverState != null && _serverState.WitnessCorpse(corpseNetworkObjectId));
        }

        /// <summary>정신력 증가 아이템이 확정한 양만큼 개인 정신력을 회복한다.</summary>
        public bool ServerRestoreSanity(int amount)
        {
            return ApplyServerMutation(_serverState != null && _serverState.Restore(amount));
        }

        /// <summary>디버그 HUD 슬라이더 전용: 개인 정신력을 지정 값으로 즉시 맞추고 복제한다.</summary>
        public bool ServerSetSanity(int value)
        {
            if (!CanMutateOnServer() || !_serverState.SetTo(value))
                return false;

            _sanity.Value = _serverState.Value;
            return true;
        }

        /// <summary>사망한 플레이어를 팀 평균과 정신력 디버프 대상에서 제외한다.</summary>
        public bool ServerMarkDead()
        {
            if (!CanMutateOnServer() || !_serverState.MarkDead())
                return false;

            _isDarknessExposed.Value = false;
            PublishServerState();
            _teamService?.ServerEvaluateTeamWipe();
            return true;
        }

        /// <summary>
        /// 사망한 플레이어를 다시 생존으로 되돌려 팀 평균과 디버프 대상에 넣는다.
        /// 정신력 값은 죽을 때 그대로다 — 100으로 되돌리려면 <see cref="ServerResetForStage"/>.
        /// 어둠 노출은 꺼진 채로 시작하며, 다음 판정이 다시 켜 준다.
        /// </summary>
        public bool ServerRevive()
        {
            if (!CanMutateOnServer()
                || (_teamService != null && _teamService.IsTeamWiped)
                || !_serverState.Revive())
                return false;

            _isDarknessExposed.Value = false;
            PublishServerState();
            return true;
        }

        /// <summary>서버 전용 누적 값까지 복구해 전멸 판정 전에 현재 플레이어에 적용한다.</summary>
        public void ServerRestoreStageState(StageRecoverySnapshot.PlayerState snapshot)
        {
            if (!CanMutateOnServer())
                return;
            _serverState.RestoreSnapshot(new SanityState.Snapshot(snapshot.Sanity,
                snapshot.Alive, snapshot.DarknessSeconds, snapshot.WitnessedCorpses));
            _isDarknessExposed.Value = snapshot.DarknessExposed && snapshot.Alive;
            PublishServerState();
        }

        public void CaptureStageState(ref StageRecoverySnapshot.PlayerState snapshot)
        {
            if (IsServer && _serverState != null)
            {
                SanityState.Snapshot state = _serverState.CaptureSnapshot();
                snapshot.Sanity = state.Value;
                snapshot.Alive = state.IsAlive;
                snapshot.DarknessSeconds = state.DarknessExposureSeconds;
                snapshot.WitnessedCorpses = state.WitnessedCorpses;
            }
            else
            {
                snapshot.Sanity = _sanity.Value;
                snapshot.Alive = _isAlive.Value;
            }
            snapshot.DarknessExposed = _isDarknessExposed.Value;
        }

        /// <summary>Result 씬을 열기 전에 모든 피어에 전멸 집계를 보낸다.</summary>
        public void ServerBroadcastStageFailure(int deadCount)
        {
            if (IsSpawned && IsServer)
                RecordStageFailureRpc(deadCount);
        }

        /// <summary>정상 종료 결과를 Result 씬 전환 전에 모든 피어에 전달한다.</summary>
        public void ServerBroadcastStageSettlement(StageSettlementRecord record)
        {
            if (IsSpawned && IsServer)
                RecordStageSettlementRpc(record.DeliveredFurniture, record.TargetFurniture,
                    record.CleaningPercent, record.Survivors, record.Missing, record.Dead,
                    record.TeamWiped);
        }

        [Rpc(SendTo.Everyone)]
        private void RecordStageSettlementRpc(int deliveredFurniture, int targetFurniture,
            int cleaningPercent, int survivors, int missing, int dead, bool teamWiped)
        {
            if (Services.TryGet(out ISceneFlow sceneFlow))
                sceneFlow.RecordStageSettlement(new StageSettlementRecord(deliveredFurniture,
                    targetFurniture, cleaningPercent, survivors, missing, dead, teamWiped));
        }

        [Rpc(SendTo.Everyone)]
        private void RecordStageFailureRpc(int deadCount)
        {
            if (Services.TryGet(out ISceneFlow sceneFlow))
                sceneFlow.RecordStageFailure(deadCount);
        }

        private bool ApplyServerMutation(bool changed)
        {
            if (!CanMutateOnServer() || !changed)
                return false;

            _sanity.Value = _serverState.Value;
            return true;
        }

        private bool CanMutateOnServer()
        {
            return IsSpawned && IsServer && _serverState != null;
        }

        private void PublishServerState()
        {
            _sanity.Value = _serverState.Value;
            _isAlive.Value = _serverState.IsAlive;
        }

        private void HandleSanityChanged(int previous, int current)
        {
            SanityChanged?.Invoke(previous, current);
            PublishDebuffChange(previous, _isAlive.Value, current, _isAlive.Value);
        }

        private void HandleAliveChanged(bool previous, bool current)
        {
            PublishDebuffChange(_sanity.Value, previous, _sanity.Value, current);

            if (previous != current)
                AliveStateChanged?.Invoke(current);
        }

        private void PublishDebuffChange(
            int previousSanity,
            bool wasAlive,
            int currentSanity,
            bool isAlive)
        {
            SanityDebuffFlags previous = SanityMath.ResolveDebuffs(
                previousSanity,
                wasAlive,
                _settings);
            SanityDebuffFlags current = SanityMath.ResolveDebuffs(
                currentSanity,
                isAlive,
                _settings);
            if (previous == current)
                return;

            DebuffsChanged?.Invoke(previous, current);

            bool heartbeatStarted =
                (previous & SanityDebuffFlags.BreathingHeartbeat) == 0
                && (current & SanityDebuffFlags.BreathingHeartbeat) != 0;
            if (IsOwner && heartbeatStarted)
                BreathingHeartbeatTriggered?.Invoke();
        }
    }
}
