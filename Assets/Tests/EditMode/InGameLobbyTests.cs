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
        private static readonly MemberGear Starting = MemberGear.Starting;

        [Test]
        public void 상점은_인게임_로비에서_방장만_살_수_있다()
        {
            Assert.IsTrue(StageShopRules.CanPurchase(true, SceneId.InGameLobby, false, 100, ShopItem.CandleSet, Starting));
            Assert.IsFalse(StageShopRules.CanPurchase(false, SceneId.InGameLobby, false, 100, ShopItem.CandleSet, Starting),
                "게스트는 관람만 한다");
            Assert.IsFalse(StageShopRules.CanPurchase(true, SceneId.Lobby, false, 100, ShopItem.CandleSet, Starting),
                "일반 로비에서는 사지 않는다");
            Assert.IsFalse(StageShopRules.CanPurchase(true, SceneId.Stage1, false, 100, ShopItem.CandleSet, Starting),
                "스테이지 중에는 사지 않는다");
            Assert.IsFalse(StageShopRules.CanPurchase(true, SceneId.InGameLobby, true, 100, ShopItem.CandleSet, Starting),
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
            Assert.IsTrue(StageShopRules.IsPersonal(ShopItem.IronDriver));
            Assert.IsTrue(StageShopRules.IsPersonal(ShopItem.IronLighter));
            Assert.IsFalse(StageShopRules.IsPersonal(ShopItem.CandleSet), "촛대는 공동 자산");
        }

        [Test]
        public void 시작_자금으로는_철제_드라이버를_못_사고_한_판_뒤에는_산다()
        {
            Assert.IsFalse(StageShopRules.CanPurchase(true, SceneId.InGameLobby, false,
                StageShopRules.StartingBalance, ShopItem.IronDriver, Starting));
            Assert.IsTrue(StageShopRules.CanPurchase(true, SceneId.InGameLobby, false,
                StageShopRules.StartingBalance + StageShopRules.StageReward, ShopItem.IronDriver, Starting));
        }

        [Test]
        public void 이미_가진_개인_품목은_다시_사지_않는다()
        {
            var iron = new MemberGear(DriverTier.Iron, 100, true);
            Assert.IsFalse(StageShopRules.CanPurchase(true, SceneId.InGameLobby, false, 999, ShopItem.IronDriver, iron));
            Assert.IsFalse(StageShopRules.CanPurchase(true, SceneId.InGameLobby, false, 999, ShopItem.IronLighter, iron));
            Assert.IsTrue(StageShopRules.CanPurchase(true, SceneId.InGameLobby, false, 999, ShopItem.CandleSet, iron),
                "공동 품목은 여러 개 산다");
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

        [Test]
        public void 플레이어_장비는_로비_데이터로_왕복한다()
        {
            var gear = new MemberGear(DriverTier.Iron, 73, true);
            Assert.IsTrue(MemberGear.TryFromLobbyData(gear.ToLobbyData(), out MemberGear read));
            Assert.AreEqual(DriverTier.Iron, read.Driver);
            Assert.AreEqual(73, read.DriverDurability);
            Assert.IsTrue(read.HasLighter);

            Assert.IsFalse(MemberGear.TryFromLobbyData(null, out MemberGear missing));
            Assert.AreEqual(DriverTier.Wood, missing.Driver, "기록이 없으면 시작 지급 — 나무 드라이버");
            Assert.AreEqual(100, missing.DriverDurability);
            Assert.IsFalse(missing.HasLighter, "라이터는 사야 생긴다");
            Assert.IsFalse(MemberGear.TryFromLobbyData("1,50", out _));
            Assert.AreEqual(100, new MemberGear(DriverTier.Iron, 250, false).DriverDurability, "최대 내구도로 자른다");
        }

        [Test]
        public void 인게임_로비와_스테이지_사이는_이전_씬을_먼저_내린다()
        {
            // 두 씬 모두 플레이어 서비스를 등록하므로 겹치면 두 번째 등록이 충돌한다.
            Assert.IsTrue(SceneFlowController.UnloadsBeforeLoad(SceneId.InGameLobby, SceneId.Stage1));
            Assert.IsTrue(SceneFlowController.UnloadsBeforeLoad(SceneId.Stage1, SceneId.InGameLobby));
            Assert.IsTrue(SceneFlowController.UnloadsBeforeLoad(SceneId.ProtoTypeGame, SceneId.InGameLobby));
            Assert.IsFalse(SceneFlowController.UnloadsBeforeLoad(SceneId.Stage1, SceneId.Result),
                "정산 화면은 스테이지 플레이어를 옮겨 받는다 — 겹쳐 올린다");
            Assert.IsFalse(SceneFlowController.UnloadsBeforeLoad(SceneId.Result, SceneId.InGameLobby));
            Assert.IsFalse(SceneFlowController.UnloadsBeforeLoad(SceneId.Lobby, SceneId.InGameLobby));
        }

        [Test]
        public void 스테이지는_Stage1과_ProtoTypeGame이다()
        {
            Assert.IsTrue(SceneId.Stage1.IsStage());
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
