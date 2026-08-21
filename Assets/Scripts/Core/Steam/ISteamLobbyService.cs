using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GhostHunter.Core.Steam
{
    /// <summary>
    /// UI와 게임 로직이 소비하는 로비 기능. 구현체만 Steamworks를 알고,
    /// 이 인터페이스는 <c>ulong</c>·<c>string</c>·DTO만 노출한다.
    /// </summary>
    public interface ISteamLobbyService
    {
        bool IsSteamReady { get; }
        string LocalName { get; }
        ulong LocalSteamId { get; }

        bool IsInLobby { get; }
        bool IsLobbyOwner { get; }
        bool IsGameStarted { get; }

        /// <summary>참가자에게 공유하는 사람이 읽는 방 코드. 로비에 없으면 빈 문자열.</summary>
        string CurrentRoomCode { get; }

        /// <summary>접속 대상 호스트 SteamId. 로비에 없으면 0.</summary>
        ulong CurrentHostSteamId { get; }

        /// <summary>사람이 읽는 진행 상황.</summary>
        event Action<string> StatusChanged;

        /// <summary>멤버 입퇴장·로비/멤버 데이터 변경 등 로비 UI를 다시 그려야 할 때.</summary>
        event Action LobbyUpdated;

        /// <summary>호스트로서 로비 준비 완료.</summary>
        event Action HostLobbyReady;

        /// <summary>참가자로서 접속할 호스트 SteamId 확보.</summary>
        event Action<ulong> JoinTargetResolved;

        event Action LobbyLeft;

        UniTask CreateLobbyAsync();
        UniTask JoinLobbyByCodeAsync(string rawCode);
        void OpenInviteOverlay();
        bool TrySetConnectionTarget(ulong hostSteamId);
        void MarkGameStarted();
        void LeaveLobby();

        void SetLocalReady(bool ready);

        /// <summary>호스트를 제외한 전원이 준비 완료인가. 게스트가 없으면 true.</summary>
        bool AllGuestsReady();

        /// <summary>현재 로비 멤버. 방장이 항상 첫 번째다. 로비에 없으면 빈 목록.</summary>
        IReadOnlyList<LobbyMemberInfo> GetMembers();

        /// <summary>
        /// 멤버의 아바타 텍스처. 실패하면 <c>null</c>을 돌려준다.
        /// 구현체가 SteamId별로 캐시하므로 호출부는 캐시를 따로 두지 않는다.
        /// </summary>
        UniTask<Texture2D> GetAvatarAsync(ulong steamId);
    }
}
