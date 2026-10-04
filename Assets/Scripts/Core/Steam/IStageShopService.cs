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
    /// 공동 잔액·촛불 재고·플레이어별 장비(stage-system.md §2.2). 세션 종류와 상관없이 같은 규칙으로 쓴다 —
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
        int CandleCount { get; }
        ulong LocalMemberKey { get; }

        IReadOnlyList<ShopMember> GetMembers();
        MemberGear GetMemberGear(ulong memberKey);

        bool TryPurchase(ShopItem item, ulong memberKey);
        bool TryRepairDriver(ulong memberKey);

        /// <summary>서버 전용 — 접속자(NGO clientId)의 장비 키.</summary>
        bool TryGetMemberKey(ulong ngoClientId, out ulong memberKey);

        /// <summary>서버 전용 — 드라이버 사용으로 바뀐 내구도를 적는다(판 사이 유지).</summary>
        bool TrySaveDriverDurability(ulong memberKey, int durability);

        /// <summary>서버 전용 — 부활 의식에 촛불 하나를 놓을 때 재고에서 뺀다.</summary>
        bool TryConsumeCandle();

        /// <summary>서버 전용 — 한 판이 끝났다(전멸 포함). <see cref="StageShopRules.StageReward"/> 를 더한다.</summary>
        void ServerGrantStageReward();

        /// <summary>개발 튜닝 — 호스트가 현재 공동 잔액을 0 이상의 정수로 바꾼다. 세션 상태에만 반영한다.</summary>
        bool TrySetBalanceForDebug(int balance);

        /// <summary>잔액·재고·장비·참가자가 바뀌었다.</summary>
        event Action Changed;
    }
}
