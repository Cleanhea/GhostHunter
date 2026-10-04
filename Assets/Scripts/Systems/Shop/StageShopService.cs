using System;
using System.Collections.Generic;
using System.Text;
using GhostHunter.Core.Scenes;
using GhostHunter.Core.Steam;
using GhostHunter.Gameplay.Player;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Systems.Shop
{
    /// <summary>
    /// <see cref="IStageShopService"/> 구현. Steam 방이면 <see cref="ISteamLobbyService"/>(방장이 로비 데이터에 적는다)에 맡기고,
    /// Steam 방이 아닌 로컬 세션(UTP)이면 호스트가 메모리에 상태를 두고 NGO 이름 메시지로 클라이언트에게 보낸다.
    /// 로컬 상태는 호스트가 세션을 새로 열 때 시작 자금으로 초기화되고, 인게임 로비 ⇄ 스테이지를 오가는 동안 유지된다
    /// (세션이 유지되므로 — ADR-0018). 게임을 끄면 사라진다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageShopService : MonoBehaviour, IStageShopService
    {
        private const string StateMessage = "gh_local_shop_state";
        private const float BroadcastCheckInterval = 0.5f;
        // 접속 직후 클라이언트가 메시지 핸들러를 등록하기 전에 보낸 상태를 놓쳐도 곧 다시 받게 한다.
        private const float ForcedResendInterval = 3f;

        private ISteamLobbyService _lobby;
        private ISceneFlow _sceneFlow;
        private NetworkManager _network;
        private bool _registered;
        private float _broadcastCheckRemaining;
        private float _forcedResendRemaining;
        private string _lastSentState = string.Empty;

        // 로컬 세션 상태 — 호스트가 원본, 클라이언트는 받은 사본.
        private int _localBalance = StageShopRules.StartingBalance;
        private int _localCandles;
        private readonly Dictionary<ulong, MemberGear> _localGear = new();
        private readonly List<ShopMember> _localMembers = new();
        private readonly List<ShopMember> _memberBuffer = new();

        public event Action Changed;

        public void Initialize(ISteamLobbyService lobby, ISceneFlow sceneFlow)
        {
            _lobby = lobby;
            _sceneFlow = sceneFlow;
            if (_lobby != null)
                _lobby.LobbyUpdated += RaiseChanged;
        }

        private void OnDestroy()
        {
            if (_lobby != null)
                _lobby.LobbyUpdated -= RaiseChanged;
            Unregister();
        }

        private bool InSteamRoom => _lobby != null && _lobby.IsInLobby;

        private bool InLocalSession => !InSteamRoom && _network != null && _network.IsListening;

        private bool IsLocalHost => InLocalSession && _network.IsServer;

        public bool IsAvailable => InSteamRoom || InLocalSession;

        public bool CanManage => InSteamRoom ? _lobby.IsLobbyOwner : IsLocalHost;

        public int Balance => InSteamRoom ? _lobby.ShopBalance : _localBalance;

        public int CandleCount => InSteamRoom ? _lobby.CandleCount : _localCandles;

        public ulong LocalMemberKey => InSteamRoom ? _lobby.LocalSteamId
            : _network != null ? _network.LocalClientId : 0UL;

        public IReadOnlyList<ShopMember> GetMembers()
        {
            if (!InSteamRoom)
                return _localMembers;

            _memberBuffer.Clear();
            foreach (LobbyMemberInfo member in _lobby.GetMembers())
                _memberBuffer.Add(new ShopMember(member.SteamId, member.DisplayName));
            return _memberBuffer;
        }

        public MemberGear GetMemberGear(ulong memberKey)
        {
            if (InSteamRoom)
                return _lobby.GetMemberGear(memberKey);
            return _localGear.TryGetValue(memberKey, out MemberGear gear) ? gear : MemberGear.Starting;
        }

        public bool TrySetBalanceForDebug(int balance)
        {
            if (balance < 0 || !CanManage)
                return false;
            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.IsListening && (!network.IsServer || network.ShutdownInProgress))
                return false;
            if (InSteamRoom)
                return _lobby.TrySetShopBalanceForDebug(balance);
            if (!IsLocalHost)
                return false;
            if (_localBalance == balance)
                return true;
            _localBalance = balance;
            LocalStateChanged();
            return true;
        }

        public bool TryPurchase(ShopItem item, ulong memberKey)
        {
            if (InSteamRoom)
                return _lobby.TryPurchase(item, memberKey);
            if (!IsLocalHost || !TryGetShopScene(out SceneId scene, out bool loading))
                return false;

            bool personal = StageShopRules.IsPersonal(item);
            if (personal && !_localGear.ContainsKey(memberKey) && !IsLocalMember(memberKey))
                return false;
            MemberGear gear = personal ? GetMemberGear(memberKey) : MemberGear.Starting;
            if (!StageShopRules.CanPurchase(true, scene, loading, _localBalance, item, gear))
                return false;

            _localBalance -= StageShopRules.PriceOf(item);
            switch (item)
            {
                case ShopItem.IronDriver:
                    _localGear[memberKey] = new MemberGear(DriverTier.Iron, StageShopRules.MaxDriverDurability,
                        gear.HasLighter);
                    break;
                case ShopItem.IronLighter:
                    _localGear[memberKey] = new MemberGear(gear.Driver, gear.DriverDurability, true);
                    break;
                case ShopItem.CandleSet:
                    _localCandles += StageShopRules.CandlesPerSet;
                    break;
            }
            LocalStateChanged();
            return true;
        }

        public bool TryRepairDriver(ulong memberKey)
        {
            if (InSteamRoom)
                return _lobby.TryRepairDriver(memberKey);
            if (!IsLocalHost || !TryGetShopScene(out SceneId scene, out bool loading))
                return false;

            MemberGear gear = GetMemberGear(memberKey);
            if (!StageShopRules.CanRepair(true, scene, loading, _localBalance, gear))
                return false;
            _localBalance -= StageShopRules.RepairCost(gear.DriverDurability);
            _localGear[memberKey] = gear.WithDurability(StageShopRules.MaxDriverDurability);
            LocalStateChanged();
            return true;
        }

        public bool TryGetMemberKey(ulong ngoClientId, out ulong memberKey)
        {
            memberKey = 0;
            if (InSteamRoom)
                return _lobby.TryGetAuthenticatedSteamId(ngoClientId, out memberKey) && memberKey != 0;
            if (!IsLocalHost)
                return false;
            memberKey = ngoClientId;
            return true;
        }

        public bool TrySaveDriverDurability(ulong memberKey, int durability)
        {
            if (InSteamRoom)
                return _lobby.TrySaveDriverDurability(memberKey, durability);
            if (!IsLocalHost)
                return false;
            MemberGear gear = GetMemberGear(memberKey);
            if (gear.DriverDurability == StageShopRules.Clamp(durability))
                return true;
            _localGear[memberKey] = gear.WithDurability(durability);
            LocalStateChanged();
            return true;
        }

        public bool TryConsumeCandle()
        {
            if (InSteamRoom)
                return _lobby.TryConsumeCandle();
            if (!IsLocalHost || _localCandles <= 0)
                return false;
            _localCandles--;
            LocalStateChanged();
            return true;
        }

        public void ServerGrantStageReward()
        {
            if (InSteamRoom)
            {
                if (_lobby.IsLobbyOwner && !_lobby.TryGrantStageReward())
                    Debug.LogWarning($"스테이지 보상 ${StageShopRules.StageReward} 를 Steam 로비에 적지 못했습니다.", this);
                return;
            }
            if (!IsLocalHost)
                return;
            _localBalance = (int)Math.Min((long)_localBalance + StageShopRules.StageReward, int.MaxValue);
            LocalStateChanged();
        }

        private bool TryGetShopScene(out SceneId scene, out bool loading)
        {
            scene = _sceneFlow != null ? _sceneFlow.Current : SceneId.Bootstrap;
            loading = _sceneFlow == null || _sceneFlow.IsLoading;
            return _sceneFlow != null;
        }

        private bool IsLocalMember(ulong clientId) =>
            _network != null && _network.IsServer && _network.ConnectedClients.ContainsKey(clientId);

        // ───────────────────────── 로컬 세션 수명·전송 ─────────────────────────

        private void Update()
        {
            if (_network == null)
                _network = NetworkManager.Singleton;
            if (_network == null)
                return;

            bool listening = _network.IsListening && _network.CustomMessagingManager != null;
            if (listening && !_registered)
                Register();
            else if (!listening && _registered)
                Unregister();

            if (!IsLocalHost)
                return;

            _broadcastCheckRemaining -= Time.unscaledDeltaTime;
            if (_broadcastCheckRemaining > 0f)
                return;
            _broadcastCheckRemaining = BroadcastCheckInterval;
            // 새 접속자·이름 변경도 참가자 목록을 바꾸므로 주기적으로 비교해 달라졌을 때만 보낸다.
            RefreshLocalMembers();
            _forcedResendRemaining -= BroadcastCheckInterval;
            bool force = _forcedResendRemaining <= 0f;
            if (force)
                _forcedResendRemaining = ForcedResendInterval;
            Broadcast(force);
        }

        private void Register()
        {
            _registered = true;
            _network.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, HandleStateMessage);
            if (_network.IsServer && !InSteamRoom)
            {
                // 호스트가 세션을 새로 열었다 — 시작 자금으로 초기화한다.
                _localBalance = StageShopRules.StartingBalance;
                _localCandles = 0;
                _localGear.Clear();
                _lastSentState = string.Empty;
                RefreshLocalMembers();
            }
            RaiseChanged();
        }

        private void Unregister()
        {
            if (!_registered)
                return;
            _registered = false;
            if (_network != null && _network.CustomMessagingManager != null)
                _network.CustomMessagingManager.UnregisterNamedMessageHandler(StateMessage);
            _localMembers.Clear();
            RaiseChanged();
        }

        private void LocalStateChanged()
        {
            RefreshLocalMembers();
            Broadcast(force: true);
            RaiseChanged();
        }

        private void RefreshLocalMembers()
        {
            if (!IsLocalHost)
                return;
            _localMembers.Clear();
            foreach (ulong clientId in _network.ConnectedClientsIds)
                _localMembers.Add(new ShopMember(clientId, NameOf(clientId)));
        }

        private string NameOf(ulong clientId)
        {
            if (_network.ConnectedClients.TryGetValue(clientId, out NetworkClient client) && client.PlayerObject != null)
            {
                PlayerNameTag tag = client.PlayerObject.GetComponent<PlayerNameTag>();
                if (tag != null && !string.IsNullOrEmpty(tag.DisplayName))
                    return tag.DisplayName;
            }
            return clientId == NetworkManager.ServerClientId ? "호스트" : $"플레이어 {clientId}";
        }

        /// <summary>"잔액|촛불|키,등급,내구도,라이터,이름|…" 한 줄로 보낸다. 이름의 구분자는 지운다.</summary>
        private string SerializeLocalState()
        {
            var builder = new StringBuilder();
            builder.Append(_localBalance).Append('|').Append(_localCandles);
            foreach (ShopMember member in _localMembers)
            {
                MemberGear gear = GetMemberGear(member.Key);
                string name = (member.DisplayName ?? string.Empty).Replace('|', ' ').Replace(',', ' ');
                builder.Append('|').Append(member.Key).Append(',').Append(gear.ToLobbyData()).Append(',').Append(name);
            }
            return builder.ToString();
        }

        private void Broadcast(bool force)
        {
            if (!IsLocalHost || _network.CustomMessagingManager == null)
                return;
            string state = SerializeLocalState();
            if (!force && state == _lastSentState)
                return;
            _lastSentState = state;

            using var writer = new FastBufferWriter(256, Allocator.Temp, 16 * 1024);
            writer.WriteValueSafe(state);
            _network.CustomMessagingManager.SendNamedMessageToAll(StateMessage, writer,
                NetworkDelivery.ReliableFragmentedSequenced);
        }

        private void HandleStateMessage(ulong sender, FastBufferReader reader)
        {
            if (_network == null || _network.IsServer || sender != NetworkManager.ServerClientId)
                return;
            reader.ReadValueSafe(out string state);
            ApplyLocalState(state);
            RaiseChanged();
        }

        /// <summary>클라이언트 — 호스트가 보낸 상태 한 줄을 사본에 옮긴다. 형식이 틀린 항목은 건너뛴다.</summary>
        internal void ApplyLocalState(string state)
        {
            string[] parts = (state ?? string.Empty).Split('|');
            if (parts.Length < 2 || !int.TryParse(parts[0], out int balance) || !int.TryParse(parts[1], out int candles))
                return;

            _localBalance = balance;
            _localCandles = candles;
            _localGear.Clear();
            _localMembers.Clear();
            for (int i = 2; i < parts.Length; i++)
            {
                string[] fields = parts[i].Split(',');
                if (fields.Length < 5 || !ulong.TryParse(fields[0], out ulong key)
                    || !MemberGear.TryFromLobbyData($"{fields[1]},{fields[2]},{fields[3]}", out MemberGear gear))
                    continue;
                _localGear[key] = gear;
                _localMembers.Add(new ShopMember(key, fields[4]));
            }
        }

        private void RaiseChanged() => Changed?.Invoke();
    }
}
