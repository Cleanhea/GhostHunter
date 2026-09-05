using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 굴착 은신이 깨지는 규칙(<see cref="BurrowExposureTracker"/>)을 검증한다
    /// (사용자 확정 2026-09-05, mole-skill-system.md §5.2.1).
    /// 순수 로직이라 <see cref="BedHideEvaluator"/> 처럼 EditMode 로 돈다.
    /// </summary>
    public sealed class GhostBurrowExposureTests
    {
        private BurrowExposureTracker _tracker;

        [SetUp]
        public void SetUp() => _tracker = new BurrowExposureTracker();

        [Test]
        public void 땅속이_아니면_노출되지_않는다()
        {
            _tracker.Tick(burrowed: false, chased: true);

            Assert.IsFalse(_tracker.Exposed);
        }

        [Test]
        public void 감지되지_않은_채_숨으면_안전하다()
        {
            _tracker.Tick(burrowed: true, chased: false);

            Assert.IsFalse(_tracker.Exposed, "쫓기지 않는 상태에서 들어갔으면 땅속은 완전 은신이다.");
        }

        [Test]
        public void 감지된_채_숨으면_땅속에서도_노출된다()
        {
            _tracker.Tick(burrowed: true, chased: true);

            Assert.IsTrue(_tracker.Exposed);
        }

        [Test]
        public void 매몰_후_귀신이_놓쳐도_노출이_유지된다()
        {
            _tracker.Tick(burrowed: true, chased: true);

            // 진입 시점에 한 번만 판정한다 — 들어간 뒤 추격이 끊겼다고 안전해지지 않는다.
            for (int i = 0; i < 5; i++)
                _tracker.Tick(burrowed: true, chased: false);

            Assert.IsTrue(_tracker.Exposed);
        }

        [Test]
        public void 매몰_전에_따돌리면_안전하게_숨는다()
        {
            // 시전(0.3초) 동안은 아직 매몰이 아니다. 그 사이 추격이 끊기면 판정 자체가 안전으로 난다.
            _tracker.Tick(burrowed: false, chased: true);
            _tracker.Tick(burrowed: true, chased: false);

            Assert.IsFalse(_tracker.Exposed);
        }

        [Test]
        public void 굴착이_끝나면_판정이_풀린다()
        {
            _tracker.Tick(burrowed: true, chased: true);
            Assert.IsTrue(_tracker.Exposed);

            _tracker.Tick(burrowed: false, chased: true);
            Assert.IsFalse(_tracker.Exposed, "땅속을 벗어나면 노출 판정은 의미가 없다.");
        }

        [Test]
        public void 다음_굴착은_그때의_추격_상태로_다시_판정한다()
        {
            _tracker.Tick(burrowed: true, chased: true);
            _tracker.Tick(burrowed: false, chased: false);

            _tracker.Tick(burrowed: true, chased: false);

            Assert.IsFalse(_tracker.Exposed, "직전 굴착의 노출이 다음 굴착으로 새면 안 된다.");
        }

        [Test]
        public void 리셋하면_노출이_사라지고_다시_판정된다()
        {
            _tracker.Tick(burrowed: true, chased: true);
            _tracker.Reset();

            Assert.IsFalse(_tracker.Exposed);

            // Reset 은 '매몰 중이었다'는 기억까지 지우므로, 같은 매몰이 이어져도 새로 판정한다.
            _tracker.Tick(burrowed: true, chased: false);
            Assert.IsFalse(_tracker.Exposed);
        }
    }
}
