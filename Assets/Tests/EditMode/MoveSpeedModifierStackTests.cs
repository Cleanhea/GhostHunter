using GhostHunter.Gameplay.Player;
using NUnit.Framework;

namespace GhostHunter.Tests.EditMode
{
    public sealed class MoveSpeedModifierStackTests
    {
        [Test]
        public void 아무_배율도_걸지_않으면_1이다()
        {
            var stack = new MoveSpeedModifierStack();

            Assert.AreEqual(1f, stack.Multiplier);
        }

        [Test]
        public void 굴착_배율을_걸면_그_값이_된다()
        {
            var stack = new MoveSpeedModifierStack();

            stack.Set(SpeedModifierSource.Burrow, 2f);

            Assert.AreEqual(2f, stack.Multiplier, 1e-4f);
        }

        [Test]
        public void 같은_출처를_다시_걸면_덮어쓴다()
        {
            var stack = new MoveSpeedModifierStack();

            stack.Set(SpeedModifierSource.Burrow, 2f);
            stack.Set(SpeedModifierSource.Burrow, 1.5f);

            Assert.AreEqual(1.5f, stack.Multiplier, 1e-4f);
        }

        [Test]
        public void 걷어_내면_1로_돌아온다()
        {
            var stack = new MoveSpeedModifierStack();
            stack.Set(SpeedModifierSource.Burrow, 2f);

            stack.Clear(SpeedModifierSource.Burrow);

            Assert.AreEqual(1f, stack.Multiplier);
        }
    }
}
