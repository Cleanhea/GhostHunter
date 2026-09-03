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
        public void 어둠에_10초_노출되면_정신력이_1_감소한다()
        {
            _state.TickDarkness(10f, true);

            Assert.AreEqual(99, _state.Value);
            Assert.AreEqual(0f, _state.DarknessExposureSeconds, 0.001f);
        }

        [Test]
        public void 어둠_누적은_노출이_아닐_때_초기화되지_않고_정지한다()
        {
            _state.TickDarkness(5f, true);
            _state.TickDarkness(30f, false);

            Assert.AreEqual(5f, _state.DarknessExposureSeconds, 0.001f);
            Assert.AreEqual(100, _state.Value);

            _state.TickDarkness(5f, true);

            Assert.AreEqual(99, _state.Value);
        }

        [Test]
        public void 긴_어둠_노출은_10초_구간을_모두_적용하고_나머지를_보존한다()
        {
            _state.TickDarkness(25f, true);

            Assert.AreEqual(98, _state.Value);
            Assert.AreEqual(5f, _state.DarknessExposureSeconds, 0.001f);
        }

        [Test]
        public void 귀신_이벤트_목격은_정신력을_15_감소시킨다()
        {
            Assert.IsTrue(_state.ApplyGhostEvent());

            Assert.AreEqual(85, _state.Value);
        }

        [Test]
        public void 같은_시체를_다시_목격해도_한_번만_20_감소한다()
        {
            Assert.IsTrue(_state.WitnessCorpse(17));
            Assert.IsFalse(_state.WitnessCorpse(17));

            Assert.AreEqual(80, _state.Value);
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
            Assert.IsFalse(_state.TickDarkness(10f, true));
        }

        [Test]
        public void 디버프는_20_10_5_이하에서_누적된다()
        {
            // 어둠 노출(−1/10초)로 정확한 경계값을 만든다. 귀신 이벤트(−15)는 임계값 간격(10·5)보다
            // 커서 20↔10 밴드를 건너뛸 수 있어 경계 검증에 못 쓴다(§6.3 정신력 감소량과는 별개 검증).
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
            while (_state.Value > target)
                _state.TickDarkness(_settings.DarknessInterval, true);
        }

        [Test]
        public void 스테이지_리셋은_시체_목격_기록도_초기화한다()
        {
            Assert.IsTrue(_state.WitnessCorpse(44));
            _state.ResetForStage();

            Assert.IsTrue(_state.WitnessCorpse(44));
            Assert.AreEqual(80, _state.Value);
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
        public void 부활_직후에는_어둠_누적이_남아_있지_않다()
        {
            // 감소 직전까지 누적시킨 뒤 죽는다. 부활하자마자 그 조각이 1틱을 깎으면 안 된다.
            _state.TickDarkness(_settings.DarknessInterval * 0.9f, true);
            Assert.IsTrue(_state.MarkDead());
            Assert.IsTrue(_state.Revive());

            int afterRevive = _state.Value;
            Assert.IsFalse(
                _state.TickDarkness(_settings.DarknessInterval * 0.2f, true),
                "부활 직후 남은 어둠 누적으로 정신력이 깎였습니다.");
            Assert.AreEqual(afterRevive, _state.Value);
        }

        [Test]
        public void 사망_중에는_정신력이_변하지_않는다()
        {
            ReduceSanityTo(60);
            Assert.IsTrue(_state.MarkDead());
            int atDeath = _state.Value;

            Assert.IsFalse(_state.ApplyGhostEvent());
            Assert.IsFalse(_state.Restore(20));
            Assert.IsFalse(_state.TickDarkness(_settings.DarknessInterval * 3f, true));

            Assert.AreEqual(atDeath, _state.Value);
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
