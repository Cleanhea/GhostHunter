using GhostHunter.Core.Scenes;

namespace GhostHunter.Core.Steam
{
    /// <summary>상점 품목. 값은 로비 데이터·UI 순서에 쓰이므로 바꾸지 않는다.</summary>
    public enum ShopItem
    {
        None = 0,
        /// <summary>가구용 멀티 드라이버(철). 꺼낸 사람의 드라이버를 내구도 최대인 철제로 바꾼다.</summary>
        IronDriver = 1,
        /// <summary>철제 라이터. 꺼낸 사람이 라이터를 쓸 수 있게 된다.</summary>
        IronLighter = 2,
        /// <summary>촛대 5개 세트. 꺼낸 사람의 촛불이 5개 는다. 부활 의식에서 하나씩 소모한다.</summary>
        CandleSet = 3,
    }

    /// <summary>
    /// 상점·경제 규칙(2026-09-29 사용자 확정 — stage-system.md §2.2). 상점은 <b>인게임 로비</b>에서만 열리고
    /// <b>방장만</b> 산다 — 나머지는 잔액·보유를 본다.
    ///
    /// <list type="bullet">
    /// <item>시작 자금 $25. 한 판이 끝날 때마다(전멸 포함) $50.</item>
    /// <item>시작 지급: 플레이어마다 나무 드라이버·대걸레. 라이터는 없다.</item>
    /// <item>철제 드라이버 $35, 철제 라이터 $10, 촛대(5개 세트) $25.</item>
    /// <item>산 아이템은 팀 공동 <b>드릴카 선반</b>(<see cref="ShelfStock"/>)에 쌓이고, 스테이지에서 선반에 E 를 누른 사람이
    /// 하나씩 꺼내 갖는다(2026-11-09). 이미 가진 품목도 꺼낼 수 있다 — 드라이버는 새것으로 바뀐다.</item>
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

        /// <summary>상점이 열려 있는가 — 인게임 로비에 있고 씬 전환 중이 아니다.</summary>
        public static bool IsOpen(SceneId currentScene, bool isLoading) =>
            currentScene == SceneId.InGameLobby && !isLoading;

        public static bool CanPurchase(bool isLobbyOwner, SceneId currentScene, bool isLoading,
            int balance, ShopItem item) =>
            isLobbyOwner && IsOpen(currentScene, isLoading) && IsValidItem(item) && balance >= PriceOf(item);

        /// <summary>구매가 가능하면 잔액을 빼고 선반에 하나 올린다. 거절하면 입력값을 보존한다.</summary>
        public static bool TryPurchase(bool isLobbyOwner, SceneId currentScene, bool isLoading,
            ShopItem item, ref int balance, ref ShelfStock shelf)
        {
            if (!CanPurchase(isLobbyOwner, currentScene, isLoading, balance, item))
                return false;
            balance -= PriceOf(item);
            shelf = shelf.With(item, 1);
            return true;
        }

        public static bool CanTakeFromShelf(ShelfStock shelf, ShopItem item) =>
            IsValidItem(item) && shelf.CountOf(item) > 0;

        /// <summary>
        /// 선반에서 하나를 꺼내 그 사람 장비에 넣는다. 드라이버는 내구도 최대인 철제로 바뀌고(이미 철제여도 새것),
        /// 라이터는 생기고, 촛대 세트는 촛불 <see cref="CandlesPerSet"/>개가 된다. 거절하면 입력값을 보존한다.
        /// </summary>
        public static bool TryTakeFromShelf(ShopItem item, ref ShelfStock shelf, ref MemberGear gear)
        {
            if (!CanTakeFromShelf(shelf, item))
                return false;

            switch (item)
            {
                case ShopItem.IronDriver:
                    gear = new MemberGear(DriverTier.Iron, MaxDriverDurability, gear.HasLighter, gear.Candles);
                    break;
                case ShopItem.IronLighter:
                    gear = new MemberGear(gear.Driver, gear.DriverDurability, true, gear.Candles);
                    break;
                case ShopItem.CandleSet:
                    gear = gear.WithCandles((int)System.Math.Min((long)gear.Candles + CandlesPerSet, int.MaxValue));
                    break;
                default:
                    return false;
            }
            shelf = shelf.With(item, -1);
            return true;
        }

        /// <summary>부활 의식 촛대에 꽂을 촛불 하나를 그 사람 장비에서 뺀다. 없으면 거절한다.</summary>
        public static bool TryConsumeCandle(ref MemberGear gear)
        {
            if (gear.Candles <= 0)
                return false;
            gear = gear.WithCandles(gear.Candles - 1);
            return true;
        }

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

        /// <summary>수리가 가능하면 비용을 빼고 내구도를 채운다. 거절하면 잔액·장비를 보존한다.</summary>
        public static bool TryRepair(bool isLobbyOwner, SceneId currentScene, bool isLoading,
            ref int balance, ref MemberGear gear)
        {
            if (!CanRepair(isLobbyOwner, currentScene, isLoading, balance, gear))
                return false;
            balance -= RepairCost(gear.DriverDurability);
            gear = gear.WithDurability(MaxDriverDurability);
            return true;
        }

        public static int Clamp(int durability) =>
            durability < 0 ? 0 : durability > MaxDriverDurability ? MaxDriverDurability : durability;
    }
}
