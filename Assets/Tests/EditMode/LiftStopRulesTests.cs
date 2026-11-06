using GhostHunter.Gameplay.Lift;
using NUnit.Framework;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>리프트 정지 포인트 규칙(기획서 lift-system.md §2.3)을 검증한다.</summary>
    public sealed class LiftStopRulesTests
    {
        private const float Floor = 3f;

        [Test]
        public void 맨_아래에서_상승은_한_층_위_정지_포인트다()
        {
            Assert.IsTrue(LiftStopRules.TryFindTarget(0f, LiftDirection.Up, Floor, 2, out float target));
            Assert.AreEqual(3f, target, 0.0001f);
        }

        [Test]
        public void 맨_위에서_하강은_맨_아래_정지_포인트다()
        {
            Assert.IsTrue(LiftStopRules.TryFindTarget(3f, LiftDirection.Down, Floor, 2, out float target));
            Assert.AreEqual(0f, target, 0.0001f);
        }

        [Test]
        public void 맨_아래에서_하강과_맨_위에서_상승은_갈_곳이_없다()
        {
            Assert.IsFalse(LiftStopRules.TryFindTarget(0f, LiftDirection.Down, Floor, 2, out _));
            Assert.IsFalse(LiftStopRules.TryFindTarget(3f, LiftDirection.Up, Floor, 2, out _));
        }

        [Test]
        public void 여러_층에서는_인접한_정지_포인트로_한_칸씩_간다()
        {
            Assert.IsTrue(LiftStopRules.TryFindTarget(3f, LiftDirection.Up, Floor, 4, out float up));
            Assert.AreEqual(6f, up, 0.0001f);
            Assert.IsTrue(LiftStopRules.TryFindTarget(6f, LiftDirection.Down, Floor, 4, out float down));
            Assert.AreEqual(3f, down, 0.0001f);
        }

        [TestCase(1.2f, LiftDirection.Up, 3f)]
        [TestCase(1.2f, LiftDirection.Down, 0f)]
        [TestCase(2.99f, LiftDirection.Up, 3f)]
        [TestCase(0.01f, LiftDirection.Down, 0f)]
        public void 막혀_중간에_선_뒤에는_입력_방향의_가장_가까운_정지_포인트로_간다(
            float height, LiftDirection direction, float expected)
        {
            // 제자리에서 한 층을 다시 오르는 것이 아니다(기획서 §2.3).
            Assert.IsTrue(LiftStopRules.TryFindTarget(height, direction, Floor, 2, out float target));
            Assert.AreEqual(expected, target, 0.0001f);
        }

        [Test]
        public void 정지_포인트와_오차_안이면_그_정지_포인트에_서_있는_것이다()
        {
            Assert.IsTrue(LiftStopRules.TryFindTarget(2.998f, LiftDirection.Down, Floor, 2, out float down));
            Assert.AreEqual(0f, down, 0.0001f);
            Assert.IsFalse(LiftStopRules.TryFindTarget(2.998f, LiftDirection.Up, Floor, 2, out _));
        }

        [Test]
        public void 정지_포인트에_있으면_그_번호를_돌려준다()
        {
            Assert.IsTrue(LiftStopRules.IsAtStop(0f, Floor, 3, out int a));
            Assert.AreEqual(0, a);
            Assert.IsTrue(LiftStopRules.IsAtStop(6.002f, Floor, 3, out int c));
            Assert.AreEqual(2, c);
            Assert.IsFalse(LiftStopRules.IsAtStop(4.5f, Floor, 3, out int none));
            Assert.AreEqual(-1, none);
        }

        [Test]
        public void 층_높이가_다르면_정지_포인트도_다르다()
        {
            // Tutorial 발코니 층고 3.57m.
            Assert.IsTrue(LiftStopRules.TryFindTarget(0f, LiftDirection.Up, 3.57f, 2, out float target));
            Assert.AreEqual(3.57f, target, 0.0001f);
        }

        [Test]
        public void 잘못된_설정은_움직이지_않는다()
        {
            Assert.IsFalse(LiftStopRules.TryFindTarget(0f, LiftDirection.Up, 0f, 2, out _));
            Assert.IsFalse(LiftStopRules.TryFindTarget(0f, LiftDirection.Up, 3f, 1, out _));
        }

        [Test]
        public void 높이는_정지_포인트_범위로_잘린다()
        {
            Assert.AreEqual(0f, LiftStopRules.Clamp(-1f, Floor, 2), 0.0001f);
            Assert.AreEqual(3f, LiftStopRules.Clamp(9f, Floor, 2), 0.0001f);
            Assert.AreEqual(1.5f, LiftStopRules.Clamp(1.5f, Floor, 2), 0.0001f);
        }
    }
}
