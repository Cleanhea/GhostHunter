using System;
using GhostHunter.Core;
using GhostHunter.Gameplay.Player;
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

        public int Sanity => _sanity.Value;
        public bool HasSanity => _isAlive.Value;
        public bool IsCrouching => _playerMotor != null && _playerMotor.IsCrouching;

        /// <summary>
        /// 굴착 스킬로 땅속에 숨어 있는지(두더지 스킬 시스템 기획서 §5.2). 귀신의 탐지 판정이
        /// 이 값을 보고 완전히 건너뛴다 — <see cref="IsCrouching"/> 과 같은 Owner-authoritative
        /// 패턴이라 서버에서도 신뢰할 수 있다.
        /// </summary>
        public bool IsBurrowed => _burrowController != null && _burrowController.IsBurrowed;
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

        private void Awake()
        {
            _playerMotor = GetComponent<PlayerMotor>();
            _burrowController = GetComponent<MoleBurrowController>();

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
        }

        public override void OnNetworkDespawn()
        {
            if (_teamService != null)
                _teamService.Unregister(this);

            _sanity.OnValueChanged -= HandleSanityChanged;
            _isAlive.OnValueChanged -= HandleAliveChanged;
            _teamService = null;
            _serverState = null;
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || _serverState == null)
                return;

            if (_serverState.TickDarkness(Time.deltaTime, _isDarknessExposed.Value))
                _sanity.Value = _serverState.Value;
        }

        /// <summary>스테이지 시작 시 개인 정신력과 목격 기록을 초기화한다.</summary>
        public bool ServerResetForStage()
        {
            if (!CanMutateOnServer())
                return false;

            _serverState.ResetForStage();
            _isDarknessExposed.Value = false;
            PublishServerState();
            return true;
        }

        /// <summary>헤드라이트·드릴 카 판정이 계산한 최종 어둠 노출 여부를 서버에 적용한다.</summary>
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

        /// <summary>사망한 플레이어를 팀 평균과 정신력 디버프 대상에서 제외한다.</summary>
        public bool ServerMarkDead()
        {
            if (!CanMutateOnServer() || !_serverState.MarkDead())
                return false;

            _isDarknessExposed.Value = false;
            PublishServerState();
            return true;
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
