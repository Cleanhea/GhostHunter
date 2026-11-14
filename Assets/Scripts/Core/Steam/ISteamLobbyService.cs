using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GhostHunter.Core.Scenes;
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
        bool IsGameLoading { get; }
        /// <summary>공동 잔액($). 로비에 없으면 시작 자금.</summary>
        int ShopBalance { get; }

        /// <summary>드릴카 선반에 쌓인 팀 공동 아이템. 로비에 없으면 비어 있다.</summary>
        ShelfStock Shelf { get; }

        /// <summary>스테이지 서버(방장)가 선반에서 하나를 꺼내 그 플레이어 장비에 넣는다.</summary>
        bool TryTakeShelfItem(ulong steamId, ShopItem item);

        /// <summary>스테이지 서버(방장)가 부활 의식에 촛불 하나를 놓을 때 그 플레이어가 든 촛불에서 뺀다.</summary>
        bool TryConsumeCandle(ulong steamId);

        /// <summary>방장이 한 판 보상(<see cref="StageShopRules.StageReward"/>)을 공동 잔액에 더한다.</summary>
        bool TryGrantStageReward();

        /// <summary>방장이 공동 잔액에 양수 금액을 더한다 — 귀중품을 훔친 순간(theft-system.md). 선반 재고와 장비는 보존한다.</summary>
        bool TryAddShopBalance(int amount);

        /// <summary>개발 튜닝 — 방장이 공동 잔액을 바꾼다. 선반 재고와 장비는 보존한다.</summary>
        bool TrySetShopBalanceForDebug(int balance);

        /// <summary>플레이어 한 명의 장비. 기록이 없으면 <see cref="MemberGear.Starting"/>.</summary>
        MemberGear GetMemberGear(ulong steamId);

        /// <summary>방장이 산다. 산 아이템은 드릴카 선반에 쌓인다. 규칙은 <see cref="StageShopRules.CanPurchase"/>.</summary>
        bool TryPurchase(ShopItem item);

        /// <summary>방장이 그 플레이어의 철제 드라이버를 최대 내구도까지 수리한다.</summary>
        bool TryRepairDriver(ulong steamId);

        /// <summary>스테이지 서버(방장)가 드라이버 사용으로 바뀐 내구도를 적는다. 판 사이에 유지된다.</summary>
        bool TrySaveDriverDurability(ulong steamId, int durability);
        int PublishedSettlementCount { get; }
        bool TryPublishStageSettlement(StageSettlementRecord record);
        bool TryGetPublishedSettlement(int index, out StageSettlementRecord record);

        /// <summary>참가자에게 공유하는 사람이 읽는 방 코드. 로비에 없으면 빈 문자열.</summary>
        string CurrentRoomCode { get; }

        /// <summary>접속 대상 호스트 SteamId. 로비에 없으면 0.</summary>
        ulong CurrentHostSteamId { get; }
        string CurrentStageId { get; }
        int CurrentHostGeneration { get; }
        bool IsMigratedHostReady { get; }
        bool IsMigratedStageResumed { get; }
        void MarkMigratedHostReady();
        void MarkMigratedStageResumed();
        bool TryGetAuthenticatedSteamId(ulong ngoClientId, out ulong steamId);

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
        void MarkGameLoading();
        void MarkGameStarted();
        void MarkGameEnded();
        void LeaveLobby();

        void SetLocalReady(bool ready);

        /// <summary>최소 2명이 참가했고 호스트를 제외한 전원이 준비 완료인가.</summary>
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
