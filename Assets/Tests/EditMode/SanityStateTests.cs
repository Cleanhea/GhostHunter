using GhostHunter.Gameplay.Sanity;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>개인 정신력 계산, 팀 평균 반올림, 디버프 임계값을 검증한다.</summary>
    public sealed class SanityStateTests
    {
        private SanitySystemSettings _settings;
        private SanityState _state;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<SanitySystemSettings>();
            _state = new SanityState(_settings);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_settings);
            _settings = null;
            _state = null;
        }

        [Test]
        public void 스테이지_시작_정신력은_100이다()
        {
            Assert.AreEqual(100, _state.Value);
            Assert.IsTrue(_state.IsAlive);
        }

        [Test]
        public void 귀신_이벤트_목격은_정신력을_5_감소시킨다()
        {
            Assert.IsTrue(_state.ApplyGhostEvent());

            Assert.AreEqual(95, _state.Value);
        }

        [Test]
        public void 귀신_본체_목격은_정신력을_5_감소시킨다()
        {
            Assert.IsTrue(_state.WitnessGhostBody());
            Assert.AreEqual(95, _state.Value);
        }

        [Test]
        public void 같은_시체를_다시_목격해도_한_번만_10_감소한다()
        {
            Assert.IsTrue(_state.WitnessCorpse(17));
            Assert.IsFalse(_state.WitnessCorpse(17));

            Assert.AreEqual(90, _state.Value);
        }

        [Test]
        public void 이전_정신력_상태는_사망과_시체_목격을_보존한다()
        {
            _state.WitnessCorpse(17);
            _state.MarkDead();
            SanityState.Snapshot snapshot = _state.CaptureSnapshot();

            var restored = new SanityState(_settings);
            restored.RestoreSnapshot(snapshot);
            Assert.AreEqual(90, restored.Value);
            Assert.IsFalse(restored.IsAlive);
            restored.Revive();
            Assert.IsFalse(restored.WitnessCorpse(17));
        }

        [Test]
        public void 정신력_회복은_100을_넘지_않는다()
        {
            _state.ApplyGhostEvent();

            Assert.IsTrue(_state.Restore(20));
            Assert.AreEqual(100, _state.Value);
            Assert.IsFalse(_state.Restore(10));
        }

        [Test]
        public void 디버그_설정은_정신력을_지정_값으로_맞추고_범위_밖은_Clamp한다()
        {
            Assert.IsTrue(_state.SetTo(42));
            Assert.AreEqual(42, _state.Value);

            Assert.IsTrue(_state.SetTo(999));
            Assert.AreEqual(_settings.MaximumSanity, _state.Value);

            Assert.IsTrue(_state.SetTo(-5));
            Assert.AreEqual(_settings.MinimumSanity, _state.Value);
        }

        [Test]
        public void 디버그_설정은_사망_상태에서_무시된다()
        {
            _state.SetTo(30);
            Assert.IsTrue(_state.MarkDead());

            Assert.IsFalse(_state.SetTo(90));
            Assert.AreEqual(30, _state.Value);
        }

        [Test]
        public void 사망하면_정신력_변경과_디버프가_비활성화된다()
        {
            for (int i = 0; i < 10; i++)
                _state.ApplyGhostEvent();

            Assert.IsTrue(_state.MarkDead());

            Assert.AreEqual(SanityDebuffFlags.None, _state.Debuffs);
            Assert.IsFalse(_state.ApplyGhostEvent());
            Assert.IsFalse(_state.Restore(10));
            Assert.IsFalse(_state.WitnessCorpse(1));
        }

        [Test]
        public void 디버프는_20_10_5_이하에서_누적된다()
        {
            // 감소 이벤트 크기에 기대지 않고 경계값을 정확히 맞춘다.
            ReduceSanityTo(_settings.CameraNoiseThreshold);
            Assert.AreEqual(SanityDebuffFlags.CameraNoise, _state.Debuffs);

            ReduceSanityTo(_settings.WhisperThreshold);
            Assert.AreEqual(
                SanityDebuffFlags.CameraNoise | SanityDebuffFlags.Whisper,
                _state.Debuffs);

            ReduceSanityTo(_settings.BreathingHeartbeatThreshold);
            Assert.AreEqual(
                SanityDebuffFlags.CameraNoise
                    | SanityDebuffFlags.Whisper
                    | SanityDebuffFlags.BreathingHeartbeat,
                _state.Debuffs);
        }

        private void ReduceSanityTo(int target)
        {
            Assert.IsTrue(_state.SetTo(target));
        }

        [Test]
        public void 스테이지_리셋은_시체_목격_기록도_초기화한다()
        {
            Assert.IsTrue(_state.WitnessCorpse(44));
            _state.ResetForStage();

            Assert.IsTrue(_state.WitnessCorpse(44));
            Assert.AreEqual(90, _state.Value);
        }

        [Test]
        public void 부활은_사망_상태에서만_적용된다()
        {
            Assert.IsFalse(_state.Revive(), "생존 중인데 부활이 적용됐습니다.");

            Assert.IsTrue(_state.MarkDead());
            Assert.IsTrue(_state.Revive());
            Assert.IsTrue(_state.IsAlive);

            Assert.IsFalse(_state.Revive(), "이미 되살아났는데 또 적용됐습니다.");
        }

        [Test]
        public void 부활은_정신력_값을_되돌리지_않는다()
        {
            ReduceSanityTo(40);
            int beforeDeath = _state.Value;

            Assert.IsTrue(_state.MarkDead());
            Assert.IsTrue(_state.Revive());

            Assert.AreEqual(beforeDeath, _state.Value,
                "부활은 죽기 직전 정신력을 유지한다. 100으로 되돌리는 것은 스테이지 리셋이다.");
        }

        [Test]
        public void 부활은_시체_목격_기록을_유지한다()
        {
            Assert.IsTrue(_state.WitnessCorpse(7));
            Assert.IsTrue(_state.MarkDead());
            Assert.IsTrue(_state.Revive());

            Assert.IsFalse(_state.WitnessCorpse(7),
                "이미 본 시체인데 부활 후 다시 감소했습니다.");
        }

        [Test]
        public void 사망_중에는_정신력이_변하지_않는다()
        {
            ReduceSanityTo(60);
            Assert.IsTrue(_state.MarkDead());
            int atDeath = _state.Value;

            Assert.IsFalse(_state.ApplyGhostEvent());
            Assert.IsFalse(_state.Restore(20));

            Assert.AreEqual(atDeath, _state.Value);
        }

        [TestCase(2, 3)]
        [TestCase(3, 5)]
        [TestCase(5, 10)]
        [TestCase(10, 15)]
        public void 귀중품_금액은_표대로_정신력_증가량이_된다(int dollars, int expected)
        {
            Assert.IsTrue(_settings.TryGetValuableRestore(dollars, out int restore));
            Assert.AreEqual(expected, restore);

            _state.SetTo(40);
            Assert.IsTrue(_state.Restore(restore));
            Assert.AreEqual(40 + expected, _state.Value);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(4)]
        [TestCase(-3)]
        public void 표에_없는_귀중품_금액은_정신력을_올리지_않는다(int dollars)
        {
            Assert.IsFalse(_settings.TryGetValuableRestore(dollars, out int restore));
            Assert.AreEqual(0, restore);
        }

        [TestCase(199, 2, 100)]
        [TestCase(201, 4, 50)]
        [TestCase(202, 4, 51)]
        public void 팀_평균은_소수점_첫째_자리에서_일반_반올림한다(
            int total,
            int livingPlayers,
            int expected)
        {
            Assert.AreEqual(
                expected,
                SanityMath.RoundTeamAverage(total, livingPlayers));
        }
    }
}
