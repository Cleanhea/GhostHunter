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
        [Test]
        public void 상점은_인게임_로비에서_방장만_살_수_있다()
        {
            Assert.IsTrue(StageShopRules.CanPurchase(true, SceneId.InGameLobby, false, 600, 3));
            Assert.IsFalse(StageShopRules.CanPurchase(false, SceneId.InGameLobby, false, 600, 1), "게스트는 관람만 한다");
            Assert.IsFalse(StageShopRules.CanPurchase(true, SceneId.Lobby, false, 600, 1), "일반 로비에서는 사지 않는다");
            Assert.IsFalse(StageShopRules.CanPurchase(true, SceneId.Stage1, false, 600, 1), "스테이지 중에는 사지 않는다");
            Assert.IsFalse(StageShopRules.CanPurchase(true, SceneId.InGameLobby, true, 600, 1), "전환 중에는 사지 않는다");
        }

        [Test]
        public void 잔액이_모자라거나_없는_아이템은_살_수_없다()
        {
            Assert.IsFalse(StageShopRules.CanPurchase(true, SceneId.InGameLobby, false, 299, 3));
            Assert.IsTrue(StageShopRules.CanPurchase(true, SceneId.InGameLobby, false, 300, 3));
            Assert.IsFalse(StageShopRules.CanPurchase(true, SceneId.InGameLobby, false, 600, 0));
            Assert.IsFalse(StageShopRules.CanPurchase(true, SceneId.InGameLobby, false, 600, 4));
            Assert.AreEqual(200, StageShopRules.PriceOf(2));
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
