using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>베이스 귀신 '뒤돌아보지마'(project/ghost-base.md §3.2) 상태 기계.</summary>
    public sealed class GhostGazeFreezeTests
    {
        private const float Step = 0.1f;

        private static GhostGazeFreeze Create() => new(5f, 5f, 5f, 1.5f);

        private static void Run(GhostGazeFreeze gaze, float seconds, bool gazed)
        {
            int ticks = (int)(seconds / Step + 0.5f);
            for (int i = 0; i < ticks; i++)
                gaze.Tick(Step, gazed);
        }

        [Test]
        public void Starts_free_at_normal_speed()
        {
            GhostGazeFreeze gaze = Create();

            Assert.IsFalse(gaze.IsFrozen);
            Assert.IsFalse(gaze.IsImmune);
            Assert.AreEqual(1f, gaze.SpeedMultiplier);
        }

        [Test]
        public void Freezes_immediately_when_gazed()
        {
            GhostGazeFreeze gaze = Create();

            gaze.Tick(Step, true);

            Assert.IsTrue(gaze.IsFrozen);
        }

        [Test]
        public void Resumes_immediately_when_nobody_gazes_and_grants_immunity_and_boost()
        {
            GhostGazeFreeze gaze = Create();
            gaze.Tick(Step, true);

            gaze.Tick(Step, false);

            Assert.IsFalse(gaze.IsFrozen);
            Assert.IsTrue(gaze.IsImmune);
            Assert.AreEqual(1.5f, gaze.SpeedMultiplier);
        }

        [Test]
        public void Force_resumes_after_max_freeze_even_if_still_gazed()
        {
            GhostGazeFreeze gaze = Create();

            Run(gaze, 4.8f, true);
            Assert.IsTrue(gaze.IsFrozen);

            Run(gaze, 0.4f, true);
            Assert.IsFalse(gaze.IsFrozen);
            Assert.IsTrue(gaze.IsImmune);
        }

        [Test]
        public void Immune_ghost_ignores_gaze_then_can_freeze_again()
        {
            GhostGazeFreeze gaze = Create();
            gaze.Tick(Step, true);
            gaze.Tick(Step, false);

            Run(gaze, 4.5f, true);
            Assert.IsFalse(gaze.IsFrozen, "면역 중에는 바라봐도 정지하지 않는다");

            Run(gaze, 0.6f, true);
            Assert.IsTrue(gaze.IsFrozen, "면역이 끝나면 다시 정지한다");
            Assert.AreEqual(1f, gaze.SpeedMultiplier, "다시 정지시키면 가속이 끝난다");
        }

        [Test]
        public void Boost_ends_after_its_duration_when_nobody_gazes()
        {
            GhostGazeFreeze gaze = Create();
            gaze.Tick(Step, true);
            gaze.Tick(Step, false);

            Run(gaze, 5.2f, false);

            Assert.IsFalse(gaze.IsImmune);
            Assert.AreEqual(1f, gaze.SpeedMultiplier);
        }

        [Test]
        public void Force_release_unfreezes_and_starts_immunity()
        {
            GhostGazeFreeze gaze = Create();
            gaze.Tick(Step, true);

            gaze.ForceRelease();

            Assert.IsFalse(gaze.IsFrozen);
            Assert.IsTrue(gaze.IsImmune);
        }

        [Test]
        public void Force_release_does_nothing_when_not_frozen()
        {
            GhostGazeFreeze gaze = Create();

            gaze.ForceRelease();

            Assert.IsFalse(gaze.IsImmune);
            Assert.AreEqual(1f, gaze.SpeedMultiplier);
        }

        [Test]
        public void Reset_returns_to_free()
        {
            GhostGazeFreeze gaze = Create();
            gaze.Tick(Step, true);

            gaze.Reset();

            Assert.IsFalse(gaze.IsFrozen);
            Assert.IsFalse(gaze.IsImmune);
            Assert.AreEqual(1f, gaze.SpeedMultiplier);
        }

        [Test]
        public void Boost_can_outlast_immunity_and_ends_on_refreeze()
        {
            var gaze = new GhostGazeFreeze(5f, 2f, 5f, 1.5f);
            gaze.Tick(Step, true);
            gaze.Tick(Step, false);

            Run(gaze, 2.5f, false);
            Assert.IsFalse(gaze.IsImmune);
            Assert.AreEqual(1.5f, gaze.SpeedMultiplier, "가속이 면역보다 길면 면역 뒤에도 남는다");

            gaze.Tick(Step, true);
            Assert.IsTrue(gaze.IsFrozen);
            Assert.AreEqual(1f, gaze.SpeedMultiplier);
        }
    }
}
