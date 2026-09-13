using GhostHunter.Networking;
using NUnit.Framework;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// NGO 씬 검증 규칙(<see cref="ConnectionManager.ShouldLoadNetworkScene"/>)을 검증한다.
    /// 서버에서 이미 로드된 씬을 거절하면 게스트 동기화 목록에서 Game 씬이 빠져 게스트가 씬 오브젝트를
    /// 받지 못한다 — 2026-09-13 Local 3프로세스 실측으로 확인한 결함의 회귀 방지.
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
    }
}
