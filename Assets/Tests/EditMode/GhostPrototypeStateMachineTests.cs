using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 귀신 공통 상태 전이(기획서 §4·§5·§7)를 검증한다. 팀 평균 정신력·청소 진행도를 입력으로
    /// 주고 평상시/활동/경고/어택/자연 진정/강제 진정 전이와 10초 어택 판정을 확인한다.
    /// </summary>
    public sealed class GhostPrototypeStateMachineTests
    {
        private GhostPrototypeSettings _settings;
        private double _roll;
        private GhostStateMachine _machine;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<GhostPrototypeSettings>();
            _roll = 1d;
            _machine = new GhostStateMachine(_settings, () => _roll);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_settings);
            _settings = null;
            _machine = null;
        }

        private GhostPhase Advance(
            float seconds,
            int teamSanity,
            bool hasLivingPlayers = true,
            int cleaningProgress = 0)
        {
            return _machine.Tick(seconds, teamSanity, hasLivingPlayers, cleaningProgress);
        }

        [Test]
        public void 시작하면_평상시다()
        {
            Assert.AreEqual(GhostPhase.Idle, _machine.Phase);
        }

        [Test]
        public void 팀_평균_80_이하면_활동으로_전환한다()
        {
            Assert.AreEqual(GhostPhase.Active, Advance(0.1f, 80));
        }

        [Test]
        public void 팀_평균_81이면_평상시_유지()
        {
            Assert.AreEqual(GhostPhase.Idle, Advance(0.1f, 81));
        }

        [Test]
        public void 활동에서_정신력이_회복되면_평상시로_돌아온다()
        {
            Advance(0.1f, 70);
            Assert.AreEqual(GhostPhase.Idle, Advance(0.1f, 90));
        }

        [Test]
        public void 활동_강제면_정신력_100이어도_활동이다()
        {
            _machine.SetForceActive(true);
            Assert.AreEqual(GhostPhase.Active, Advance(0.1f, 100));
        }

        [Test]
        public void 청소_40퍼센트_최초_도달시_평상시에서_활동으로_전환한다()
        {
            Assert.AreEqual(GhostPhase.Active, Advance(0.1f, 100, cleaningProgress: 40));
        }

        [Test]
        public void 청소_40퍼센트는_일회성이라_방해_구간이_지나면_평상시로_돌아온다()
        {
            Assert.AreEqual(GhostPhase.Active, Advance(0.1f, 100, cleaningProgress: 40));

            // 방해 증가 구간이 끝나고 다른 활동 조건이 없으면 평상시로 복귀한다(G-1 일회성 해석).
            Assert.AreEqual(
                GhostPhase.Idle,
                Advance(_settings.CleaningBoostDuration + 1f, 100, cleaningProgress: 100));
        }

        [Test]
        public void 청소_40퍼센트를_활동_중에_도달하면_방해_증가_구간이_켜진다()
        {
            Advance(0.1f, 50);
            Advance(0.1f, 50, cleaningProgress: 40);
            Assert.IsTrue(_machine.CleaningBoostActive);

            Advance(_settings.CleaningBoostDuration + 1f, 50);
            Assert.IsFalse(_machine.CleaningBoostActive);
        }

        [Test]
        public void 팀_평균_60_초과_활동에서는_어택_판정을_하지_않는다()
        {
            _roll = 0d;
            Advance(0.1f, 70);
            Assert.AreEqual(GhostPhase.Active, Advance(10.01f, 70));
        }

        [Test]
        public void 팀_평균_60_이하_활동에서_어택_판정에_성공하면_경고로_간다()
        {
            _roll = 0d;
            Advance(0.1f, 55);
            Assert.AreEqual(GhostPhase.Warning, Advance(10.01f, 55));
        }

        [Test]
        public void 어택_판정에_실패하면_경고로_가지_않는다()
        {
            _roll = 0.99d;
            Advance(0.1f, 55);
            Assert.AreEqual(GhostPhase.Active, Advance(10.01f, 55));
        }

        [Test]
        public void 생존자가_없으면_어택_판정을_하지_않는다()
        {
            _roll = 0d;
            _machine.SetForceActive(true);
            Advance(0.1f, 100);
            Assert.AreEqual(GhostPhase.Active, Advance(10.01f, 55, hasLivingPlayers: false));
        }

        [Test]
        public void 경고는_5초_뒤_어택으로_전환한다()
        {
            _roll = 0d;
            Advance(0.1f, 55);
            Advance(10.01f, 55);
            Assert.AreEqual(GhostPhase.Attack, Advance(5.01f, 55));
        }

        [Test]
        public void 어택은_최소_30초_전에는_정신력이_회복돼도_끝나지_않는다()
        {
            EnterAttack();
            Assert.AreEqual(GhostPhase.Attack, Advance(20f, 100));
        }

        [Test]
        public void 어택은_30초_후_팀_평균_70_이상이면_조기_종료한다()
        {
            EnterAttack();
            Assert.AreEqual(GhostPhase.Calming, Advance(31f, 100));
        }

        [Test]
        public void 어택은_정신력이_안_올라도_90초에_강제_종료된다()
        {
            EnterAttack();
            Assert.AreEqual(GhostPhase.Calming, Advance(91f, 20));
        }

        [Test]
        public void 자연_진정은_30초_뒤_조건을_재검사해_평상시로_간다()
        {
            EnterAttack();
            Advance(91f, 20);
            Assert.AreEqual(GhostPhase.Idle, Advance(31f, 100));
        }

        [Test]
        public void 자연_진정_후_정신력이_낮으면_활동으로_돌아간다()
        {
            EnterAttack();
            Advance(91f, 20);
            Assert.AreEqual(GhostPhase.Active, Advance(31f, 50));
        }

        [Test]
        public void 강제_진정은_어택을_즉시_끊는다()
        {
            EnterAttack();
            Assert.IsTrue(_machine.ForceSuppression());
            Assert.AreEqual(GhostPhase.Suppressed, _machine.Phase);
            Assert.AreEqual(GhostPhase.Suppressed, Advance(1f, 20));
        }

        [Test]
        public void 강제_진정은_10초_뒤_조건을_재검사한다()
        {
            EnterAttack();
            _machine.ForceSuppression();
            Assert.AreEqual(GhostPhase.Idle, Advance(11f, 100));
        }

        [Test]
        public void 특수_어택_Trigger는_활동에서_즉시_경고로_보낸다()
        {
            Advance(0.1f, 55);
            Assert.IsTrue(_machine.ForceSpecialAttack());
            Assert.AreEqual(GhostPhase.Warning, _machine.Phase);
        }

        [Test]
        public void 특수_어택_Trigger는_이미_어택_중이면_무시한다()
        {
            EnterAttack();
            Assert.IsFalse(_machine.ForceSpecialAttack());
            Assert.AreEqual(GhostPhase.Attack, _machine.Phase);
        }

        [TestCase(100, 0f)]
        [TestCase(61, 0f)]
        [TestCase(60, 0.20f)]
        [TestCase(50, 0.40f)]
        [TestCase(40, 0.60f)]
        [TestCase(30, 0.80f)]
        [TestCase(0, 0.80f)]
        public void 어택_확률표는_기획서_7_3과_같다(int teamSanity, float expected)
        {
            Assert.AreEqual(expected, _settings.AttackChanceForTeamSanity(teamSanity), 0.0001f);
        }

        [Test]
        public void 스테이지_리셋은_평상시로_되돌린다()
        {
            EnterAttack();
            _machine.ResetForStage();
            Assert.AreEqual(GhostPhase.Idle, _machine.Phase);
        }

        /// <summary>정신력 55 · 즉시 성공 판정으로 평상시 → 활동 → 경고 → 어택까지 밀어 넣는다.</summary>
        private void EnterAttack()
        {
            _roll = 0d;
            Advance(0.1f, 55);
            Advance(10.01f, 55);
            Advance(5.01f, 55);
            Assert.AreEqual(GhostPhase.Attack, _machine.Phase, "테스트 준비: 어택 진입 실패");
            _roll = 1d;
        }
    }
}
