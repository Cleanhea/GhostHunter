using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 침대 밑 은신(<see cref="BedHideEvaluator"/>) 성립 규칙을 검증한다(사용자 확정 2026-09-03,
    /// ghost-system.md §9.5 / §13 G-8). 순수 로직이라 <c>GhostStateMachine</c> 처럼 EditMode 로 돈다.
    /// </summary>
    public sealed class GhostBedHideEvaluatorTests
    {
        private const float ConcealSeconds = 1f;

        private BedHideEvaluator _evaluator;

        [SetUp]
        public void SetUp() => _evaluator = new BedHideEvaluator();

        [Test]
        public void 엎드려_침대_밑에_없으면_성립하지_않는다()
        {
            bool granted = _evaluator.Tick(2f, eligible: false, chasedByGhost: false, visibleToGhost: false, ConcealSeconds);

            Assert.IsFalse(granted);
            Assert.IsFalse(_evaluator.Granted);
        }

        [Test]
        public void 조건_충족이_은폐_시간을_넘기면_성립한다()
        {
            Assert.IsFalse(_evaluator.Tick(0.6f, true, false, false, ConcealSeconds), "아직 은폐 시간 미달");
            Assert.IsTrue(_evaluator.Tick(0.6f, true, false, false, ConcealSeconds), "누적 1.2s ≥ 1s → 성립");
        }

        [Test]
        public void 귀신에게_쫓기는_동안에는_침대_밑이어도_성립하지_않는다()
        {
            // 오래 숨어 있어도 추격 대상인 한 타이머가 계속 0으로 리셋된다.
            for (int i = 0; i < 10; i++)
                Assert.IsFalse(_evaluator.Tick(0.5f, eligible: true, chasedByGhost: true, visibleToGhost: false, ConcealSeconds));

            Assert.AreEqual(0f, _evaluator.ConcealTimer);
        }

        [Test]
        public void 귀신_시야에_직접_걸리면_타이머가_리셋된다()
        {
            _evaluator.Tick(0.9f, true, false, visibleToGhost: false, ConcealSeconds);
            _evaluator.Tick(0.1f, true, false, visibleToGhost: true, ConcealSeconds);

            Assert.IsFalse(_evaluator.Granted);
            Assert.AreEqual(0f, _evaluator.ConcealTimer);
        }

        [Test]
        public void 추격이_끝난_뒤_계속_숨어_있으면_그제서야_성립한다()
        {
            // 들어가는 걸 봤다 → 추격 중엔 성립 안 됨.
            _evaluator.Tick(3f, eligible: true, chasedByGhost: true, visibleToGhost: false, ConcealSeconds);
            Assert.IsFalse(_evaluator.Granted);

            // 귀신이 놓쳐 배회로 복귀 → 은폐 시간을 채우면 성립.
            _evaluator.Tick(0.7f, true, false, false, ConcealSeconds);
            Assert.IsFalse(_evaluator.Granted);
            _evaluator.Tick(0.7f, true, false, false, ConcealSeconds);
            Assert.IsTrue(_evaluator.Granted);
        }

        [Test]
        public void 성립_후_상자에서_나가면_바로_풀린다()
        {
            _evaluator.Tick(2f, true, false, false, ConcealSeconds);
            Assert.IsTrue(_evaluator.Granted);

            _evaluator.Tick(0.1f, eligible: false, chasedByGhost: false, visibleToGhost: false, ConcealSeconds);
            Assert.IsFalse(_evaluator.Granted);
        }

        [Test]
        public void Reset_은_누적과_성립을_모두_지운다()
        {
            _evaluator.Tick(2f, true, false, false, ConcealSeconds);
            _evaluator.Reset();

            Assert.IsFalse(_evaluator.Granted);
            Assert.AreEqual(0f, _evaluator.ConcealTimer);
        }
    }
}
