using GhostHunter.Core.Scenes;

namespace GhostHunter.Core.Steam
{
    /// <summary>상점 품목. 값은 로비 데이터·UI 순서에 쓰이므로 바꾸지 않는다.</summary>
    public enum ShopItem
    {
        None = 0,
        /// <summary>가구용 멀티 드라이버(철). 플레이어 한 명의 나무 드라이버를 바꿔 준다.</summary>
        IronDriver = 1,
        /// <summary>철제 라이터. 플레이어 한 명이 라이터를 쓸 수 있게 된다.</summary>
        IronLighter = 2,
        /// <summary>촛대 5개 세트. 공동 자산 — 사면 팀 촛불 재고가 5개 는다. 부활 의식에서 하나씩 소모한다.</summary>
        CandleSet = 3,
    }

    /// <summary>
    /// 상점·경제 규칙(2026-09-29 사용자 확정 — stage-system.md §2.2). 상점은 <b>인게임 로비</b>에서만 열리고
    /// <b>방장만</b> 산다 — 나머지는 잔액·보유를 본다.
    ///
    /// <list type="bullet">
    /// <item>시작 자금 $25. 한 판이 끝날 때마다(전멸 포함) $50.</item>
    /// <item>시작 지급: 플레이어마다 나무 드라이버·대걸레. 라이터는 없다.</item>
    /// <item>철제 드라이버 $35, 철제 라이터 $10 — 플레이어 한 명에게 사 준다. 촛대(5개 세트) $25 — 공동 촛불 재고 +5.</item>
    /// <item>철제 드라이버 수리: 손실 내구도 2당 $1, 홀수는 $1 올림, 최대 내구도까지 한 번에(10% = $5).</item>
    /// </list>
    /// </summary>
    public static class StageShopRules
    {
        public const int StartingBalance = 25;
        public const int StageReward = 50;
        public const int MaxDriverDurability = 100;

        /// <summary>촛대 세트 하나에 든 촛불 수. 부활 1회에 5개가 든다(revival-system.md).</summary>
        public const int CandlesPerSet = 5;

        public static readonly ShopItem[] Items = { ShopItem.IronDriver, ShopItem.IronLighter, ShopItem.CandleSet };

        public static int PriceOf(ShopItem item) => item switch
        {
            ShopItem.IronDriver => 35,
            ShopItem.IronLighter => 10,
            ShopItem.CandleSet => 25,
            _ => 0,
        };

        public static string NameOf(ShopItem item) => item switch
        {
            ShopItem.IronDriver => "가구용 멀티 드라이버(철)",
            ShopItem.IronLighter => "철제 라이터",
            ShopItem.CandleSet => "촛대(5개 세트)",
            _ => "-",
        };

        public static bool IsValidItem(ShopItem item) => PriceOf(item) > 0;

        /// <summary>플레이어 한 명에게 사 주는 품목인가(아니면 공동 자산).</summary>
        public static bool IsPersonal(ShopItem item) => item is ShopItem.IronDriver or ShopItem.IronLighter;

        /// <summary>상점이 열려 있는가 — 인게임 로비에 있고 씬 전환 중이 아니다.</summary>
        public static bool IsOpen(SceneId currentScene, bool isLoading) =>
            currentScene == SceneId.InGameLobby && !isLoading;

        /// <summary>그 플레이어가 이미 가진 개인 품목은 다시 사지 않는다.</summary>
        public static bool AlreadyOwned(ShopItem item, MemberGear gear) => item switch
        {
            ShopItem.IronDriver => gear.Driver == DriverTier.Iron,
            ShopItem.IronLighter => gear.HasLighter,
            _ => false,
        };

        public static bool CanPurchase(bool isLobbyOwner, SceneId currentScene, bool isLoading,
            int balance, ShopItem item, MemberGear gear) =>
            isLobbyOwner && IsOpen(currentScene, isLoading) && IsValidItem(item)
            && balance >= PriceOf(item) && !(IsPersonal(item) && AlreadyOwned(item, gear));

        /// <summary>손실 내구도 2당 $1, 홀수면 $1 올림. 손실이 없으면 0.</summary>
        public static int RepairCost(int durability)
        {
            int lost = MaxDriverDurability - Clamp(durability);
            return lost <= 0 ? 0 : (lost + 1) / 2;
        }

        /// <summary>수리는 철제 드라이버만 된다(기획 목록에서 수리가 철제 드라이버 아래에 있다).</summary>
        public static bool IsRepairable(MemberGear gear) => gear.Driver == DriverTier.Iron;

        public static bool CanRepair(bool isLobbyOwner, SceneId currentScene, bool isLoading,
            int balance, MemberGear gear)
        {
            int cost = RepairCost(gear.DriverDurability);
            return isLobbyOwner && IsOpen(currentScene, isLoading) && IsRepairable(gear)
                && cost > 0 && balance >= cost;
        }

        public static int Clamp(int durability) =>
            durability < 0 ? 0 : durability > MaxDriverDurability ? MaxDriverDurability : durability;
    }
}
