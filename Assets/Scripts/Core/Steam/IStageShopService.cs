using System;
using System.Collections.Generic;

namespace GhostHunter.Core.Steam
{
    /// <summary>상점 화면에 나오는 참가자 한 명. <see cref="Key"/> 는 Steam 방이면 SteamId, 로컬 세션이면 NGO clientId.</summary>
    public readonly struct ShopMember
    {
        public ShopMember(ulong key, string displayName)
        {
            Key = key;
            DisplayName = displayName;
        }

        public ulong Key { get; }
        public string DisplayName { get; }
    }

    /// <summary>
    /// 공동 잔액·드릴카 선반 재고·플레이어별 장비(stage-system.md §2.2). 세션 종류와 상관없이 같은 규칙으로 쓴다 —
    /// Steam 방이면 방장이 로비 데이터에 적고, 로컬 세션(UTP)이면 호스트가 메모리에 두고 클라이언트에게 보낸다
    /// (2026-09-30 사용자 요청 "로컬에서도 상점을 쓰게").
    /// </summary>
    public interface IStageShopService
    {
        /// <summary>세션 중이라 상점 값이 있는가(Steam 방이거나 로컬 호스트·클라이언트).</summary>
        bool IsAvailable { get; }

        /// <summary>이 피어가 살 수 있는가 — Steam 방장 또는 로컬 호스트.</summary>
        bool CanManage { get; }

        int Balance { get; }

        /// <summary>드릴카 선반에 쌓인 팀 공동 아이템. 상점에서 사면 늘고 스테이지에서 꺼내면 준다(판 사이 유지).</summary>
        ShelfStock Shelf { get; }
        ulong LocalMemberKey { get; }

        IReadOnlyList<ShopMember> GetMembers();
        MemberGear GetMemberGear(ulong memberKey);

        /// <summary>방장·로컬 호스트가 산다. 산 아이템은 드릴카 선반에 쌓인다.</summary>
        bool TryPurchase(ShopItem item);
        bool TryRepairDriver(ulong memberKey);

        /// <summary>서버 전용 — 접속자(NGO clientId)의 장비 키.</summary>
        bool TryGetMemberKey(ulong ngoClientId, out ulong memberKey);

        /// <summary>서버 전용 — 드라이버 사용으로 바뀐 내구도를 적는다(판 사이 유지).</summary>
        bool TrySaveDriverDurability(ulong memberKey, int durability);

        /// <summary>서버 전용 — 선반에서 하나를 꺼내 그 참가자 장비에 넣는다(<see cref="StageShopRules.TryTakeFromShelf"/>).</summary>
        bool TryTakeShelfItem(ulong memberKey, ShopItem item);

        /// <summary>서버 전용 — 부활 의식에 촛불 하나를 놓을 때 그 참가자가 든 촛불에서 뺀다.</summary>
        bool TryConsumeCandle(ulong memberKey);

        /// <summary>서버 전용 — 한 판이 끝났다(전멸 포함). <see cref="StageShopRules.StageReward"/> 를 더한다.</summary>
        void ServerGrantStageReward();

        /// <summary>
        /// 서버 전용 — 훔친 귀중품 금액을 공동 잔액에 <b>즉시</b> 더한다(theft-system.md TH-5, 사용자 확정 2026-11-12).
        /// 훔친 사람이 나중에 죽거나 실종돼도 되돌리지 않는다. 0 이하·세션 밖이면 거짓.
        /// </summary>
        bool ServerAddStolenMoney(int amount);

        /// <summary>개발 튜닝 — 호스트가 현재 공동 잔액을 0 이상의 정수로 바꾼다. 세션 상태에만 반영한다.</summary>
        bool TrySetBalanceForDebug(int balance);

        /// <summary>잔액·재고·장비·참가자가 바뀌었다.</summary>
        event Action Changed;
    }
}
