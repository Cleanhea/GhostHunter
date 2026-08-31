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
