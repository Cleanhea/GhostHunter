using GhostHunter.Core.Scenes;
using GhostHunter.Networking;
using NUnit.Framework;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// NGO 씬 검증 규칙(<see cref="ConnectionManager.ShouldLoadNetworkScene"/>)을 검증한다.
    /// 서버에서 이미 로드된 씬을 거절하면 게스트 동기화 목록에서 Game 씬이 빠져 게스트가 씬 오브젝트를
    /// 받지 못한다 — 2026-09-13 Local 3프로세스 실측으로 확인한 결함의 회귀 방지.
    /// Local 호스트의 씬 선행 규칙(<see cref="ConnectionManager.ShouldLoadGameSceneBeforeLocalHost"/>)도 함께 둔다.
    /// </summary>
    public sealed class ConnectionSceneValidationTests
    {
        private const int BootstrapIndex = 0;
        private const int GameIndex = 3;

        [Test]
        public void 호스트는_이미_로드된_Game_씬도_게스트_동기화에_포함한다()
        {
            Assert.IsTrue(ConnectionManager.ShouldLoadNetworkScene(true, GameIndex, alreadyLoaded: true));
        }

        [Test]
        public void 호스트는_Bootstrap을_게스트_동기화에서_뺀다()
        {
            Assert.IsFalse(ConnectionManager.ShouldLoadNetworkScene(true, BootstrapIndex, alreadyLoaded: true));
        }

        [Test]
        public void 게스트는_이미_로드된_씬을_다시_올리지_않는다()
        {
            Assert.IsFalse(ConnectionManager.ShouldLoadNetworkScene(false, BootstrapIndex, alreadyLoaded: true));
        }

        [Test]
        public void 게스트는_아직_없는_Game_씬을_올린다()
        {
            Assert.IsTrue(ConnectionManager.ShouldLoadNetworkScene(false, GameIndex, alreadyLoaded: false));
        }

        // 2026-09-27: HUD 의 Local → Host 가 Title 위에서 StartHost 해 플레이어가 Game 씬 서비스 없이 스폰됐다.

        [Test]
        public void Local_호스트는_Title에서_Game_씬을_먼저_올린다()
        {
            Assert.IsTrue(ConnectionManager.ShouldLoadGameSceneBeforeLocalHost(true, SceneId.Title));
        }

        [Test]
        public void Local_호스트는_Lobby에서도_Game_씬을_먼저_올린다()
        {
            Assert.IsTrue(ConnectionManager.ShouldLoadGameSceneBeforeLocalHost(true, SceneId.Lobby));
        }

        [Test]
        public void Local_호스트는_이미_스테이지면_바로_연다()
        {
            Assert.IsFalse(ConnectionManager.ShouldLoadGameSceneBeforeLocalHost(true, SceneId.ProtoTypeGame));
            Assert.IsFalse(ConnectionManager.ShouldLoadGameSceneBeforeLocalHost(true, SceneId.Stage1));
            Assert.IsFalse(ConnectionManager.ShouldLoadGameSceneBeforeLocalHost(true, SceneId.Tutorial));
        }

        [Test]
        public void Local_호스트는_씬_흐름이_없으면_지금_씬에서_연다()
        {
            Assert.IsFalse(ConnectionManager.ShouldLoadGameSceneBeforeLocalHost(false, SceneId.Bootstrap));
        }

        [Test]
        public void 정산_기록은_방_데이터로_왕복한다()
        {
            var original = new StageSettlementRecord(3, 5, 75, 1, 1, 2, false);
            Assert.IsTrue(StageSettlementRecord.TryFromLobbyData(
                original.ToLobbyData(), out StageSettlementRecord restored));
            Assert.AreEqual(original.DeliveredFurniture, restored.DeliveredFurniture);
            Assert.AreEqual(original.TargetFurniture, restored.TargetFurniture);
            Assert.AreEqual(original.CleaningPercent, restored.CleaningPercent);
            Assert.AreEqual(original.Survivors, restored.Survivors);
            Assert.AreEqual(original.Missing, restored.Missing);
            Assert.AreEqual(original.Dead, restored.Dead);
            Assert.AreEqual(original.TeamWiped, restored.TeamWiped);
        }

        [Test]
        public void 손상된_방_정산_기록은_무시한다()
        {
            Assert.IsFalse(StageSettlementRecord.TryFromLobbyData(
                "9,3,101,1,0,0,2", out _));
            Assert.IsFalse(StageSettlementRecord.TryFromLobbyData("1,2,3", out _));
        }
    }
}
