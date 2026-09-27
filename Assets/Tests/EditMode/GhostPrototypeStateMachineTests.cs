using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    public sealed class GhostPrototypeStateMachineTests
    {
        private GhostPrototypeSettings _settings;
        private GhostStateMachine _machine;
        private double _roll;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<GhostPrototypeSettings>();
            _roll = 0d;
            _machine = new GhostStateMachine(_settings, () => _roll);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_settings);
        }

        private GhostPhase Tick(float seconds, int sanity, int cleaning = 40)
        {
            return _machine.Tick(seconds, sanity, true, cleaning);
        }

        [Test]
        public void StartsActiveAndRequiresFortyPercentCleaning()
        {
            Assert.AreEqual(GhostPhase.Active, _machine.Phase);
            Assert.AreEqual(GhostPhase.Active, Tick(20f, 0, 39));
            Assert.AreEqual(GhostPhase.Warning, Tick(0.01f, 0, 40));
        }

        [Test]
        public void CleaningThresholdRemainsValidAboveFiftyPercent()
        {
            Tick(0f, 100, 40);
            Assert.AreEqual(GhostPhase.Warning, Tick(0.01f, 0, 100));
        }

        [Test]
        public void MidSanityUsesProbabilityAfterRollInterval()
        {
            _roll = 0.99d;
            Assert.AreEqual(GhostPhase.Active, Tick(10f, 55));
            _roll = 0d;
            Assert.AreEqual(GhostPhase.Warning, Tick(10f, 55));
        }

        [Test]
        public void LowSanityForcesWarningWithoutRoll()
        {
            _roll = 1d;
            Assert.AreEqual(GhostPhase.Warning, Tick(0f, 30));
        }

        [Test]
        public void WarningLastsFiveSecondsAndAttackEndsAfterSixty()
        {
            Tick(10f, 55);
            Assert.AreEqual(GhostPhase.Warning, Tick(4.9f, 55));
            Assert.AreEqual(GhostPhase.Attack, Tick(0.1f, 55));
            Assert.IsFalse(_machine.IsHighRiskAttack);
            Assert.AreEqual(GhostPhase.Attack, Tick(59.9f, 55));
            Assert.AreEqual(GhostPhase.Calming, Tick(0.1f, 55));
        }

        [Test]
        public void 이전_귀신_어택_상태는_남은_시간과_고위험_여부를_보존한다()
        {
            Tick(0f, 30);
            Tick(5f, 30);
            Tick(27f, 30);
            GhostStateMachine.Snapshot snapshot = _machine.CaptureSnapshot();

            var restored = new GhostStateMachine(_settings, () => 1d);
            restored.RestoreSnapshot(snapshot);
            Assert.AreEqual(GhostPhase.Attack, restored.Phase);
            Assert.IsTrue(restored.IsHighRiskAttack);
            Assert.AreEqual(27f, restored.PhaseElapsed, 0.001f);
            Assert.AreEqual(GhostPhase.Attack, restored.Tick(62.9f, 30, true, 40));
            Assert.AreEqual(GhostPhase.Calming, restored.Tick(0.1f, 30, true, 40));
        }

        [Test]
        public void SanitySixtyOneEndsAttackOnlyDuringFirstTwentySeconds()
        {
            Tick(10f, 55);
            Tick(5f, 55);
            Assert.AreEqual(GhostPhase.Calming, Tick(19f, 61));

            _machine.ResetForStage();
            Tick(10f, 55);
            Tick(5f, 55);
            Tick(21f, 55);
            Assert.AreEqual(GhostPhase.Attack, Tick(1f, 61));
        }

        [Test]
        public void HighRiskAttackLastsNinetySeconds()
        {
            Tick(0f, 30);
            Tick(5f, 30);
            Assert.IsTrue(_machine.IsHighRiskAttack);
            Assert.AreEqual(GhostPhase.Attack, Tick(89.9f, 30));
            Assert.AreEqual(GhostPhase.Calming, Tick(0.1f, 30));
        }

        [Test]
        public void LowSanityWarningKeepsHighRiskWhenSanityRisesDuringWarning()
        {
            Tick(0f, 30);
            Tick(5f, 40);
            Assert.IsTrue(_machine.IsHighRiskAttack);
        }

        [Test]
        public void ForcedEndEntersThirtySecondCalmThenRechecksSanity()
        {
            Tick(0f, 30);
            Tick(5f, 30);
            Assert.IsTrue(_machine.ForceSuppression());
            Assert.AreEqual(GhostPhase.Calming, _machine.Phase);
            Assert.AreEqual(GhostPhase.Calming, Tick(29.9f, 30));
            Assert.AreEqual(GhostPhase.Warning, Tick(0.1f, 30));
        }

        [Test]
        public void CalmEndImmediatelyRechecksMidSanityProbability()
        {
            Tick(10f, 55);
            Tick(5f, 55);
            _machine.ForceSuppression();
            Assert.AreEqual(GhostPhase.Warning, Tick(30f, 55));
        }

        [Test]
        public void SpecialAttackAtHighSanityDoesNotImmediatelyEarlyEnd()
        {
            Assert.IsTrue(_machine.ForceSpecialAttack());
            Tick(5f, 100, 0);
            Assert.AreEqual(GhostPhase.Attack, Tick(1f, 100, 0));
        }

        [TestCase(61, 0f)]
        [TestCase(60, 0.2f)]
        [TestCase(50, 0.4f)]
        [TestCase(40, 0.6f)]
        [TestCase(30, 1f)]
        public void AttackChancesMatchDesignBands(int sanity, float chance)
        {
            Assert.AreEqual(chance, _settings.AttackChanceForTeamSanity(sanity), 0.0001f);
        }

        [Test]
        public void TargetSwitchRequiresOneMeterAdvantage()
        {
            Assert.IsFalse(GhostPrototypeController.ShouldSwitchTarget(5f, 4.1f, 1f));
            Assert.IsTrue(GhostPrototypeController.ShouldSwitchTarget(5f, 4f, 1f));
        }
    }
}
