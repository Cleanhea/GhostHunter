using GhostHunter.Gameplay.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 스태미나 순수 규칙 검증. 기본 에셋 값은 사용자가 계속 조정하므로 모든 수치를 테스트가 직접 정한다.
    /// 총량 100 · 소모 20/s · 회복 10/s(지연 1초) · 탈진 해제 25% · 40% 아래부터 느려져 0 에서 0.6배 ·
    /// 30% 이하에서 헐떡이기 시작해 60% 에서 멈춘다.
    /// </summary>
    public sealed class StaminaModelTests
    {
        private StaminaSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<StaminaSettings>();
            Set("_maxStamina", 100f);
            Set("_sprintDrainPerSecond", 20f);
            Set("_regenPerSecond", 10f);
            Set("_regenDelaySeconds", 1f);
            Set("_exhaustedRecoverRatio", 0.25f);
            Set("_slowStartRatio", 0.4f);
            Set("_minSpeedMultiplier", 0.6f);
            Set("_breathStartRatio", 0.3f);
            Set("_breathStopRatio", 0.6f);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_settings);

        private void Set(string field, float value)
        {
            var serialized = new SerializedObject(_settings);
            serialized.FindProperty(field).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void 가득_찬_상태로_시작한다()
        {
            var model = new StaminaModel(_settings);

            Assert.AreEqual(1f, model.Ratio);
            Assert.IsTrue(model.CanSprint);
            Assert.IsFalse(model.Exhausted);
            Assert.IsFalse(model.Panting);
            Assert.AreEqual(1f, model.SpeedMultiplier);
        }

        [Test]
        public void 달리면_초당_소모량만큼_줄어든다()
        {
            var model = new StaminaModel(_settings);

            model.Tick(1f, sprinting: true);

            Assert.AreEqual(80f, model.Current, 1e-3f);
        }

        [Test]
        public void 달리기를_멈추고_지연이_지나야_회복한다()
        {
            var model = new StaminaModel(_settings);
            model.Tick(1f, sprinting: true);

            model.Tick(0.5f, sprinting: false);
            model.Tick(0.5f, sprinting: false);
            Assert.AreEqual(80f, model.Current, 1e-3f, "회복 지연(1초) 동안은 차오르지 않는다.");

            model.Tick(1f, sprinting: false);
            Assert.AreEqual(90f, model.Current, 1e-3f);
        }

        [Test]
        public void 달리는_동안에는_회복하지_않는다()
        {
            var model = new StaminaModel(_settings);

            model.Tick(0.5f, sprinting: true);

            Assert.AreEqual(90f, model.Current, 1e-3f);
        }

        [Test]
        public void 가득_차면_더_회복하지_않는다()
        {
            var model = new StaminaModel(_settings);

            model.Tick(5f, sprinting: false);

            Assert.AreEqual(100f, model.Current, 1e-3f);
        }

        [Test]
        public void 바닥나면_탈진해_달릴_수_없다()
        {
            var model = new StaminaModel(_settings);

            model.Tick(5f, sprinting: true);

            Assert.AreEqual(0f, model.Current);
            Assert.IsTrue(model.Exhausted);
            Assert.IsFalse(model.CanSprint);
        }

        [Test]
        public void 탈진_중에_달리기_입력이_들어와도_소모하지_않는다()
        {
            var model = new StaminaModel(_settings);
            model.Tick(5f, sprinting: true);

            model.Tick(1f, sprinting: true);

            Assert.AreEqual(0f, model.Current);
        }

        [Test]
        public void 탈진은_회복_비율에_닿아야_풀린다()
        {
            var model = new StaminaModel(_settings);
            model.Tick(5f, sprinting: true);
            model.Tick(1f, sprinting: false); // 지연
            model.Tick(1f, sprinting: false); // 10

            model.Tick(1f, sprinting: false); // 20 — 25% 아래
            Assert.IsTrue(model.Exhausted);
            Assert.IsFalse(model.CanSprint);

            model.Tick(1f, sprinting: false); // 30 — 25% 이상
            Assert.IsFalse(model.Exhausted);
            Assert.IsTrue(model.CanSprint);
        }

        [Test]
        public void 회복_비율이_1이면_가득_찰_때까지_다시_달릴_수_없다()
        {
            Set("_exhaustedRecoverRatio", 1f);
            var model = new StaminaModel(_settings);
            model.Tick(5f, sprinting: true);
            model.Tick(1f, sprinting: false); // 지연

            for (int second = 1; second <= 9; second++)
            {
                model.Tick(1f, sprinting: false); // 10, 20, … 90
                Assert.IsTrue(model.Exhausted, $"{second * 10}% 에서는 아직 탈진이다.");
                Assert.IsFalse(model.CanSprint);
            }

            model.Tick(1f, sprinting: false); // 100
            Assert.AreEqual(100f, model.Current, 1e-3f);
            Assert.IsFalse(model.Exhausted);
            Assert.IsTrue(model.CanSprint);
        }

        [Test]
        public void 느려지기_시작하는_비율_위에서는_배율이_1이다()
        {
            var model = new StaminaModel(_settings);

            model.Tick(3f, sprinting: true); // 40 = 정확히 시작 비율

            Assert.AreEqual(1f, model.SpeedMultiplier, 1e-4f);
        }

        [Test]
        public void 지치면_바닥에_가까울수록_점점_느려진다()
        {
            var model = new StaminaModel(_settings);

            model.Tick(4f, sprinting: true); // 20 — 시작 비율의 절반
            float half = model.SpeedMultiplier;
            model.Tick(1f, sprinting: true); // 0
            float empty = model.SpeedMultiplier;

            Assert.AreEqual(0.8f, half, 1e-4f);
            Assert.AreEqual(0.6f, empty, 1e-4f);
        }

        [Test]
        public void 느려지는_비율이_0이면_느려지지_않는다()
        {
            Set("_slowStartRatio", 0f);
            var model = new StaminaModel(_settings);

            model.Tick(5f, sprinting: true);

            Assert.AreEqual(1f, model.SpeedMultiplier);
        }

        [Test]
        public void 시작_비율_이하로_내려가면_헐떡이고_정지_비율까지_올라야_멈춘다()
        {
            var model = new StaminaModel(_settings);

            model.Tick(3.6f, sprinting: true); // 28 — 30% 이하
            Assert.IsTrue(model.Panting);

            model.Tick(1f, sprinting: false); // 지연
            model.Tick(2f, sprinting: false); // 48
            Assert.IsTrue(model.Panting, "시작 비율을 벗어나도 정지 비율 전에는 계속 헐떡인다.");

            model.Tick(2f, sprinting: false); // 68
            Assert.IsFalse(model.Panting);
        }

        [Test]
        public void 탈진하면_비율_설정과_상관없이_헐떡인다()
        {
            Set("_breathStartRatio", 0f);
            Set("_breathStopRatio", 0f);
            var model = new StaminaModel(_settings);

            model.Tick(5f, sprinting: true);

            Assert.IsTrue(model.Exhausted);
            Assert.IsTrue(model.Panting);
        }

        [Test]
        public void 다시_채우면_모든_상태가_초기화된다()
        {
            var model = new StaminaModel(_settings);
            model.Tick(5f, sprinting: true);

            model.Refill();

            Assert.AreEqual(100f, model.Current);
            Assert.IsFalse(model.Exhausted);
            Assert.IsFalse(model.Panting);
        }

        [Test]
        public void 총량을_낮추면_현재값이_새_총량으로_잘린다()
        {
            var model = new StaminaModel(_settings);
            Set("_maxStamina", 50f);

            model.Tick(0f, sprinting: false);

            Assert.AreEqual(50f, model.Current, 1e-3f);
            Assert.AreEqual(1f, model.Ratio);
        }
    }
}
