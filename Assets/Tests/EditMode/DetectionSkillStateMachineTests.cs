using GhostHunter.Gameplay.Player;
using NUnit.Framework;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>탐지 플로우차트의 판정 3개와 5초 표시·10초 쿨타임을 순수 상태 머신으로 검증한다.</summary>
    public sealed class DetectionSkillStateMachineTests
    {
        [Test]
        public void TryStart_사망_상태면_입력을_무시한다()
        {
            var state = new DetectionSkillStateMachine(0.5f, 5f, 10f);

            Assert.IsFalse(state.TryStart(inputPressed: true, playerAlive: false));
            Assert.AreEqual(MoleSkillPhase.Idle, state.Phase);
        }

        [Test]
        public void TryStart_사용중이면_재입력을_무시한다()
        {
            var state = new DetectionSkillStateMachine(0.5f, 5f, 10f);

            Assert.IsTrue(state.TryStart(inputPressed: true, playerAlive: true));
            Assert.IsFalse(state.TryStart(inputPressed: true, playerAlive: true));
            Assert.AreEqual(MoleSkillPhase.Casting, state.Phase);
        }

        [Test]
        public void TryStart_쿨타임중이면_입력을_무시한다()
        {
            var state = new DetectionSkillStateMachine(0.1f, 5f, 10f);

            Assert.IsTrue(state.TryStart(inputPressed: true, playerAlive: true));
            Assert.AreEqual(
                DetectionSkillStateMachine.Transition.ActiveStarted,
                state.Tick(0.1f, playerAlive: true, cancelOnDeath: true));
            Assert.AreEqual(
                DetectionSkillStateMachine.Transition.Finished,
                state.Tick(5f, playerAlive: true, cancelOnDeath: true));

            Assert.IsFalse(state.TryStart(inputPressed: true, playerAlive: true));
            Assert.AreEqual(MoleSkillPhase.Cooldown, state.Phase);
        }

        [Test]
        public void Tick_시전중에는_표시카운트가_시작되지_않고_종료후_10초_쿨타임을_부여한다()
        {
            var state = new DetectionSkillStateMachine(0.5f, 5f, 10f);
            Assert.IsTrue(state.TryStart(inputPressed: true, playerAlive: true));

            Assert.AreEqual(
                DetectionSkillStateMachine.Transition.None,
                state.Tick(0.25f, playerAlive: true, cancelOnDeath: true));
            Assert.AreEqual(MoleSkillPhase.Casting, state.Phase);
            Assert.AreEqual(0.25f, state.PhaseRemainingSeconds, 0.0001f);

            Assert.AreEqual(
                DetectionSkillStateMachine.Transition.ActiveStarted,
                state.Tick(0.25f, playerAlive: true, cancelOnDeath: true));
            Assert.AreEqual(MoleSkillPhase.Active, state.Phase);
            Assert.AreEqual(5f, state.PhaseRemainingSeconds, 0.0001f);

            Assert.AreEqual(
                DetectionSkillStateMachine.Transition.Finished,
                state.Tick(5f, playerAlive: true, cancelOnDeath: true));
            Assert.AreEqual(MoleSkillPhase.Cooldown, state.Phase);
            Assert.AreEqual(10f, state.CooldownRemainingSeconds, 0.0001f);
        }
        [Test]
        public void Tick_10초_쿨타임이_끝나면_다시_시전할_수_있다()
        {
            var state = new DetectionSkillStateMachine(0f, 5f, 10f);
            Assert.IsTrue(state.TryStart(inputPressed: true, playerAlive: true));
            Assert.AreEqual(
                DetectionSkillStateMachine.Transition.ActiveStarted,
                state.Tick(0f, playerAlive: true, cancelOnDeath: true));
            Assert.AreEqual(
                DetectionSkillStateMachine.Transition.Finished,
                state.Tick(5f, playerAlive: true, cancelOnDeath: true));

            state.Tick(9.9f, playerAlive: true, cancelOnDeath: true);
            Assert.IsFalse(state.TryStart(inputPressed: true, playerAlive: true));

            state.Tick(0.1f, playerAlive: true, cancelOnDeath: true);
            Assert.IsTrue(state.TryStart(inputPressed: true, playerAlive: true));
            Assert.AreEqual(MoleSkillPhase.Casting, state.Phase);
        }
    }
}
