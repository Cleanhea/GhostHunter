using GhostHunter.Core.Scenes;
using GhostHunter.Core.Steam;
using GhostHunter.Networking;
using GhostHunter.Systems.SceneFlow;
using NUnit.Framework;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 인게임 로비(ADR-0018)·스테이지 씬(ADR-0019)의 규칙.
    /// </summary>
    public sealed class InGameLobbyTests
    {
        private static readonly ShelfStock Stocked = new(1, 2, 3);

        [Test]
        public void 상점은_인게임_로비에서_방장만_살_수_있다()
        {
            Assert.IsTrue(StageShopRules.CanPurchase(true, SceneId.InGameLobby, false, 100, ShopItem.CandleSet));
            Assert.IsFalse(StageShopRules.CanPurchase(false, SceneId.InGameLobby, false, 100, ShopItem.CandleSet),
                "게스트는 관람만 한다");
            Assert.IsFalse(StageShopRules.CanPurchase(true, SceneId.Lobby, false, 100, ShopItem.CandleSet),
                "일반 로비에서는 사지 않는다");
            Assert.IsFalse(StageShopRules.CanPurchase(true, SceneId.Stage1, false, 100, ShopItem.CandleSet),
                "스테이지 중에는 사지 않는다");
            Assert.IsFalse(StageShopRules.CanPurchase(true, SceneId.InGameLobby, true, 100, ShopItem.CandleSet),
                "전환 중에는 사지 않는다");
        }

        [Test]
        public void 가격표와_시작_자금_보상()
        {
            Assert.AreEqual(25, StageShopRules.StartingBalance);
            Assert.AreEqual(50, StageShopRules.StageReward);
            Assert.AreEqual(35, StageShopRules.PriceOf(ShopItem.IronDriver));
            Assert.AreEqual(10, StageShopRules.PriceOf(ShopItem.IronLighter));
            Assert.AreEqual(25, StageShopRules.PriceOf(ShopItem.CandleSet));
            Assert.IsFalse(StageShopRules.IsValidItem(ShopItem.None));
        }

        [Test]
        public void 시작_자금으로는_철제_드라이버를_못_사고_한_판_뒤에는_산다()
        {
            Assert.IsFalse(StageShopRules.CanPurchase(true, SceneId.InGameLobby, false,
                StageShopRules.StartingBalance, ShopItem.IronDriver));
            Assert.IsTrue(StageShopRules.CanPurchase(true, SceneId.InGameLobby, false,
                StageShopRules.StartingBalance + StageShopRules.StageReward, ShopItem.IronDriver));
        }

        [Test]
        public void 수리비는_손실_2당_1달러이고_홀수는_올림한다()
        {
            Assert.AreEqual(0, StageShopRules.RepairCost(100));
            Assert.AreEqual(5, StageShopRules.RepairCost(90), "10% → $5");
            Assert.AreEqual(1, StageShopRules.RepairCost(99), "1 손실 → $1 올림");
            Assert.AreEqual(2, StageShopRules.RepairCost(97), "3 손실 → $2 올림");
            Assert.AreEqual(50, StageShopRules.RepairCost(0));
        }

        [Test]
        public void 수리는_철제_드라이버만_잔액이_있을_때_한다()
        {
            var worn = new MemberGear(DriverTier.Iron, 90, false);
            Assert.IsTrue(StageShopRules.CanRepair(true, SceneId.InGameLobby, false, 5, worn));
            Assert.IsFalse(StageShopRules.CanRepair(true, SceneId.InGameLobby, false, 4, worn), "잔액 부족");
            Assert.IsFalse(StageShopRules.CanRepair(true, SceneId.InGameLobby, false, 99,
                new MemberGear(DriverTier.Iron, 100, false)), "닳지 않았다");
            Assert.IsFalse(StageShopRules.CanRepair(true, SceneId.InGameLobby, false, 99,
                new MemberGear(DriverTier.Wood, 50, false)), "나무 드라이버는 수리하지 않는다");
            Assert.IsFalse(StageShopRules.CanRepair(false, SceneId.InGameLobby, false, 99, worn), "방장만");
        }

        [TestCase(ShopItem.IronDriver, 65, 2, 2, 3)]
        [TestCase(ShopItem.IronLighter, 90, 1, 3, 3)]
        [TestCase(ShopItem.CandleSet, 75, 1, 2, 4)]
        public void 구매하면_차감하고_선반에_하나_쌓인다(ShopItem item, int expectedBalance,
            int expectedDrivers, int expectedLighters, int expectedCandleSets)
        {
            int balance = 100;
            ShelfStock shelf = Stocked;

            Assert.IsTrue(StageShopRules.TryPurchase(true, SceneId.InGameLobby, false, item, ref balance, ref shelf));
            Assert.AreEqual(expectedBalance, balance);
            Assert.AreEqual(new ShelfStock(expectedDrivers, expectedLighters, expectedCandleSets), shelf);
        }

        [Test]
        public void 같은_품목을_여러_개_사서_선반에_쌓는다()
        {
            int balance = 100;
            ShelfStock shelf = default;
            for (int i = 0; i < 2; i++)
                Assert.IsTrue(StageShopRules.TryPurchase(true, SceneId.InGameLobby, false, ShopItem.IronDriver,
                    ref balance, ref shelf), "이미 선반에 있어도 또 산다");
            Assert.AreEqual(30, balance);
            Assert.AreEqual(2, shelf.CountOf(ShopItem.IronDriver));
        }

        [TestCase(false, SceneId.InGameLobby, false, 100, ShopItem.IronDriver)]
        [TestCase(true, SceneId.Stage1, false, 100, ShopItem.IronDriver)]
        [TestCase(true, SceneId.InGameLobby, true, 100, ShopItem.IronDriver)]
        [TestCase(true, SceneId.InGameLobby, false, 34, ShopItem.IronDriver)]
        [TestCase(true, SceneId.InGameLobby, false, 100, ShopItem.None)]
        [TestCase(true, SceneId.InGameLobby, false, 100, (ShopItem)999)]
        public void 구매가_거절되면_잔액과_선반을_보존한다(bool owner, SceneId scene,
            bool loading, int startingBalance, ShopItem item)
        {
            int balance = startingBalance;
            ShelfStock shelf = Stocked;

            Assert.IsFalse(StageShopRules.TryPurchase(owner, scene, loading, item, ref balance, ref shelf));
            Assert.AreEqual(startingBalance, balance);
            Assert.AreEqual(Stocked, shelf);
        }

        [TestCase(ShopItem.IronDriver, 0, 2, 3, DriverTier.Iron, 100, false, 4)]
        [TestCase(ShopItem.IronLighter, 1, 1, 3, DriverTier.Wood, 73, true, 4)]
        [TestCase(ShopItem.CandleSet, 1, 2, 2, DriverTier.Wood, 73, false, 9)]
        public void 선반에서_꺼내면_재고가_줄고_꺼낸_사람이_갖는다(ShopItem item,
            int expectedDrivers, int expectedLighters, int expectedCandleSets,
            DriverTier expectedDriver, int expectedDurability, bool expectedLighter, int expectedCandles)
        {
            ShelfStock shelf = Stocked;
            var gear = new MemberGear(DriverTier.Wood, 73, false, 4);

            Assert.IsTrue(StageShopRules.TryTakeFromShelf(item, ref shelf, ref gear));
            Assert.AreEqual(new ShelfStock(expectedDrivers, expectedLighters, expectedCandleSets), shelf);
            Assert.AreEqual(new MemberGear(expectedDriver, expectedDurability, expectedLighter, expectedCandles), gear);
        }

        [Test]
        public void 이미_가진_품목도_꺼내면_새것으로_바뀐다()
        {
            ShelfStock shelf = Stocked;
            var gear = new MemberGear(DriverTier.Iron, 40, true, 2);

            Assert.IsTrue(StageShopRules.TryTakeFromShelf(ShopItem.IronDriver, ref shelf, ref gear));
            Assert.AreEqual(new MemberGear(DriverTier.Iron, 100, true, 2), gear, "닳은 철제 드라이버가 새것으로");
            Assert.IsTrue(StageShopRules.TryTakeFromShelf(ShopItem.IronLighter, ref shelf, ref gear));
            Assert.AreEqual(new MemberGear(DriverTier.Iron, 100, true, 2), gear);
            Assert.AreEqual(new ShelfStock(0, 1, 3), shelf, "가진 품목이어도 선반에서는 빠진다");
        }

        [TestCase(ShopItem.IronDriver)]
        [TestCase(ShopItem.None)]
        [TestCase((ShopItem)999)]
        public void 선반에_없으면_꺼내지_못하고_상태를_보존한다(ShopItem item)
        {
            var empty = new ShelfStock(0, 2, 3);
            ShelfStock shelf = empty;
            var initial = new MemberGear(DriverTier.Wood, 73, false, 1);
            MemberGear gear = initial;

            Assert.IsFalse(StageShopRules.TryTakeFromShelf(item, ref shelf, ref gear));
            Assert.AreEqual(empty, shelf);
            Assert.AreEqual(initial, gear);
        }

        [Test]
        public void 촛불은_든_사람_것에서_하나씩_쓴다()
        {
            var gear = new MemberGear(DriverTier.Iron, 60, true, 1);
            Assert.IsTrue(StageShopRules.TryConsumeCandle(ref gear));
            Assert.AreEqual(new MemberGear(DriverTier.Iron, 60, true, 0), gear, "촛불만 하나 준다");
            Assert.IsFalse(StageShopRules.TryConsumeCandle(ref gear), "촛불이 없으면 못 꽂는다");
            Assert.AreEqual(0, gear.Candles);
        }

        [Test]
        public void 선반_재고는_데이터로_왕복하고_음수는_읽지_않는다()
        {
            ShelfStock stock = new(4, 0, 7);
            Assert.IsTrue(ShelfStock.TryParse(("25," + stock.ToData()).Split(','), 1, out ShelfStock read));
            Assert.AreEqual(stock, read);
            Assert.IsFalse(ShelfStock.TryParse("1,-1,2".Split(','), 0, out _));
            Assert.IsFalse(ShelfStock.TryParse("1,2".Split(','), 0, out _));
            Assert.AreEqual(0, new ShelfStock(1, 0, 0).With(ShopItem.IronDriver, -5).IronDrivers, "0 아래로 내려가지 않는다");
        }

        [TestCase(90, 95)]
        [TestCase(99, 99)]
        public void 수리는_비용만_차감하고_라이터_보유를_유지한다(int durability, int expectedBalance)
        {
            int balance = 100;
            var gear = new MemberGear(DriverTier.Iron, durability, true, 3);

            Assert.IsTrue(StageShopRules.TryRepair(true, SceneId.InGameLobby, false, ref balance, ref gear));
            Assert.AreEqual(expectedBalance, balance);
            Assert.AreEqual(new MemberGear(DriverTier.Iron, 100, true, 3), gear);
        }

        [TestCase(false, SceneId.InGameLobby, false, 100, DriverTier.Iron, 90)]
        [TestCase(true, SceneId.Stage1, false, 100, DriverTier.Iron, 90)]
        [TestCase(true, SceneId.InGameLobby, true, 100, DriverTier.Iron, 90)]
        [TestCase(true, SceneId.InGameLobby, false, 4, DriverTier.Iron, 90)]
        [TestCase(true, SceneId.InGameLobby, false, 100, DriverTier.Wood, 90)]
        [TestCase(true, SceneId.InGameLobby, false, 100, DriverTier.Iron, 100)]
        public void 수리가_거절되면_잔액과_장비를_보존한다(bool owner, SceneId scene,
            bool loading, int startingBalance, DriverTier driver, int durability)
        {
            int balance = startingBalance;
            var initial = new MemberGear(driver, durability, true);
            MemberGear gear = initial;

            Assert.IsFalse(StageShopRules.TryRepair(owner, scene, loading, ref balance, ref gear));
            Assert.AreEqual(startingBalance, balance);
            Assert.AreEqual(initial, gear);
        }

        [Test]
        public void 플레이어_장비는_로비_데이터로_왕복한다()
        {
            var gear = new MemberGear(DriverTier.Iron, 73, true, 6);
            Assert.IsTrue(MemberGear.TryFromLobbyData(gear.ToLobbyData(), out MemberGear read));
            Assert.AreEqual(DriverTier.Iron, read.Driver);
            Assert.AreEqual(73, read.DriverDurability);
            Assert.IsTrue(read.HasLighter);
            Assert.AreEqual(6, read.Candles);

            Assert.IsTrue(MemberGear.TryFromLobbyData("1,50,1", out MemberGear legacy), "촛불이 없던 옛 형식");
            Assert.AreEqual(0, legacy.Candles);

            Assert.IsFalse(MemberGear.TryFromLobbyData(null, out MemberGear missing));
            Assert.AreEqual(DriverTier.Wood, missing.Driver, "기록이 없으면 시작 지급 — 나무 드라이버");
            Assert.AreEqual(100, missing.DriverDurability);
            Assert.IsFalse(missing.HasLighter, "라이터는 꺼내야 생긴다");
            Assert.AreEqual(0, missing.Candles);
            Assert.IsFalse(MemberGear.TryFromLobbyData("1,50", out _));
            Assert.IsFalse(MemberGear.TryFromLobbyData("1,50,1,-2", out _));
            Assert.AreEqual(100, new MemberGear(DriverTier.Iron, 250, false).DriverDurability, "최대 내구도로 자른다");
        }

        [Test]
        public void 인게임_로비와_스테이지_사이는_이전_씬을_먼저_내린다()
        {
            // 두 씬 모두 플레이어 서비스를 등록하므로 겹치면 두 번째 등록이 충돌한다.
            Assert.IsTrue(SceneFlowController.UnloadsBeforeLoad(SceneId.InGameLobby, SceneId.Stage1));
            Assert.IsTrue(SceneFlowController.UnloadsBeforeLoad(SceneId.Stage1, SceneId.InGameLobby));
            Assert.IsTrue(SceneFlowController.UnloadsBeforeLoad(SceneId.InGameLobby, SceneId.Stage2));
            Assert.IsTrue(SceneFlowController.UnloadsBeforeLoad(SceneId.Stage2, SceneId.InGameLobby));
            Assert.IsFalse(SceneFlowController.UnloadsBeforeLoad(SceneId.Stage2, SceneId.Result));
            Assert.IsTrue(SceneFlowController.UnloadsBeforeLoad(SceneId.ProtoTypeGame, SceneId.InGameLobby));
            Assert.IsFalse(SceneFlowController.UnloadsBeforeLoad(SceneId.Stage1, SceneId.Result),
                "정산 화면은 스테이지 플레이어를 옮겨 받는다 — 겹쳐 올린다");
            Assert.IsFalse(SceneFlowController.UnloadsBeforeLoad(SceneId.Result, SceneId.InGameLobby));
            Assert.IsFalse(SceneFlowController.UnloadsBeforeLoad(SceneId.Lobby, SceneId.InGameLobby));
        }

        [Test]
        public void 플레이_스테이지는_정신력과_정산_흐름에_참여한다()
        {
            Assert.IsTrue(SceneId.Stage1.IsStage());
            Assert.IsTrue(SceneId.Stage2.IsStage());
            Assert.IsTrue(SceneId.Tutorial.IsStage());
            Assert.IsTrue(SceneId.DinoTest.IsStage());
            Assert.IsTrue(SceneId.ProtoTypeGame.IsStage(), "프로토타입 씬도 정신력·정산·음성 그룹이 켜지는 스테이지다");
            foreach (SceneId id in new[] { SceneId.Bootstrap, SceneId.Title, SceneId.Lobby, SceneId.Result, SceneId.InGameLobby })
                Assert.IsFalse(id.IsStage(), $"{id} 는 스테이지가 아니다");
        }

        [Test]
        public void Local_호스트는_이미_인게임_로비면_바로_연다()
        {
            Assert.IsFalse(ConnectionManager.ShouldLoadGameSceneBeforeLocalHost(true, SceneId.InGameLobby));
        }
    }
}
