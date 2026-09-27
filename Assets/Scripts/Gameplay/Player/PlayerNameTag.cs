using System;
using GhostHunter.Core;
using GhostHunter.Core.Steam;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 플레이어 닉네임을 서버 권위로 복제한다. 소유자가 스폰 직후 자기 Steam 이름을 한 번 요청하고,
    /// 서버가 <see cref="PlayerNameRules"/> 로 정리해 확정한다. 머리 위 표시는 UI 의
    /// <c>PlayerNameTagView</c> 가 이 값을 읽어서 한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerNameTag : NetworkBehaviour
    {
        private readonly NetworkVariable<FixedString128Bytes> _displayName = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ulong> _steamId = new(0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private bool _hasAcceptedRequest;

        /// <summary>확정된 닉네임. 서버가 확정하기 전에는 빈 문자열이다.</summary>
        public string DisplayName { get; private set; } = string.Empty;
        /// <summary>세션을 재시작해 NGO clientId가 바뀌어도 유지되는 방 멤버 식별자.</summary>
        public ulong SteamId => _steamId.Value;

        /// <summary>닉네임이 바뀌었을 때. 스폰 시점의 초기값도 한 번 알린다.</summary>
        public event Action<string> DisplayNameChanged;

        public override void OnNetworkSpawn()
        {
            _displayName.OnValueChanged += HandleDisplayNameChanged;

            // 늦게 접속한 클라이언트는 OnValueChanged 없이 초기값만 받는다.
            Apply(_displayName.Value);

            if (IsOwner)
            {
                RequestDisplayNameRpc(new FixedString128Bytes(PlayerNameRules.Sanitize(ResolveLocalName())));
                if (Services.TryGet(out ISteamLobbyService lobby) && lobby.IsInLobby)
                    RequestSteamIdentityRpc(lobby.LocalSteamId);
            }
        }

        public override void OnNetworkDespawn()
        {
            _displayName.OnValueChanged -= HandleDisplayNameChanged;
            _hasAcceptedRequest = false;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestDisplayNameRpc(FixedString128Bytes requestedName)
        {
            // 스폰당 한 번만 받는다. 반복 요청으로 NetworkVariable 을 계속 흔들지 못하게 막는다.
            if (_hasAcceptedRequest)
                return;

            _hasAcceptedRequest = true;
            _displayName.Value = new FixedString128Bytes(
                PlayerNameRules.Resolve(requestedName.ToString(), OwnerClientId));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestSteamIdentityRpc(ulong requestedId)
        {
            if (_steamId.Value != 0 || requestedId == 0
                || !Services.TryGet(out ISteamLobbyService lobby) || !lobby.IsInLobby)
                return;
            if (!lobby.TryGetAuthenticatedSteamId(OwnerClientId, out ulong authenticatedId)
                || authenticatedId != requestedId)
                return;
            foreach (LobbyMemberInfo member in lobby.GetMembers())
                if (member.SteamId == requestedId)
                {
                    _steamId.Value = requestedId;
                    return;
                }
        }

        /// <summary>
        /// Steam 이 없으면(Local 트랜스포트 단독 검증) 빈 이름을 보내 서버가 대체 이름을 붙이게 한다.
        /// <see cref="ISteamLobbyService.LocalName"/> 은 미연결일 때 안내 문구를 돌려주므로 준비 여부를 먼저 본다.
        /// </summary>
        private static string ResolveLocalName()
        {
            return Services.TryGet(out ISteamLobbyService lobby) && lobby.IsSteamReady
                ? lobby.LocalName
                : string.Empty;
        }

        private void HandleDisplayNameChanged(FixedString128Bytes previous, FixedString128Bytes current) => Apply(current);

        private void Apply(FixedString128Bytes value)
        {
            DisplayName = value.ToString();
            DisplayNameChanged?.Invoke(DisplayName);
        }
    }
}
