using System.Reflection;
using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 귀신 공통 기획서 원문 0.3·0.4 규칙의 순수 계산 — 청소 40% 일회성 강제 어택, 0~30 계속 어택, 난이도별 돌진 속도, 말소리 소음 반경.
    /// 컨트롤러가 하는 규칙(7m 스폰·쉬움 사망 종료)은 PlayMode <c>GhostAttackRulesFlowTests</c>.
    /// </summary>
    public sealed class GhostAttackRulesTests
    {
        private GhostPrototypeSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<GhostPrototypeSettings>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_settings);
        }

        private static void Set(object target, string field, object value)
            => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private GhostStateMachine NewMachine() => new(_settings, () => 1d);

        // ---- 청소 40% 일회성 강제 어택 ----

        [Test]
        public void 청소_40퍼센트를_처음_넘으면_정신력이_높아도_무조건_한_번_어택한다()
        {
            GhostStateMachine machine = NewMachine();

            Assert.AreEqual(GhostPhase.Active, machine.Tick(30f, 100, true, 39), "40% 전에는 정신력이 100이라 활동");
            Assert.AreEqual(GhostPhase.Warning, machine.Tick(0.1f, 100, true, 40), "40%를 넘는 순간 정신력과 무관하게 경고");
            Assert.IsTrue(machine.CleaningAttackFired);
            Assert.IsFalse(machine.CleaningAttackPending);
            Assert.AreEqual(GhostPhase.Attack, machine.Tick(5.1f, 100, true, 40));
        }

        [Test]
        public void 강제_어택은_일회성이라_다시_40에서_50일_때_발동하지_않는다()
        {
            GhostStateMachine machine = NewMachine();
            machine.Tick(0.1f, 100, true, 40);
            machine.Tick(5.1f, 100, true, 40);   // 어택
            machine.Tick(60.1f, 100, true, 40);  // 자연 진정
            Assert.AreEqual(GhostPhase.Calming, machine.Phase);
            Assert.AreEqual(GhostPhase.Active, machine.Tick(40.1f, 100, true, 45));

            Assert.AreEqual(GhostPhase.Active, machine.Tick(60f, 100, true, 45), "다시 청소 진행도가 40~50%여도 발동하지 않는다");
            Assert.AreEqual(GhostPhase.Active, machine.Tick(60f, 100, true, 100));
        }

        [Test]
        public void 한_번에_50퍼센트를_넘겨도_처음_넘은_틱에_한_번_발동한다()
        {
            GhostStateMachine machine = NewMachine();

            Assert.AreEqual(GhostPhase.Warning, machine.Tick(0.1f, 100, true, 90));
        }

        [Test]
        public void 어택_중에_40퍼센트를_넘으면_활동으로_돌아온_뒤_한_번_발동한다()
        {
            GhostStateMachine machine = NewMachine();
            Assert.IsTrue(machine.ForceSpecialAttack());   // 디버그 어택 — 청소 진행도와 무관
            Assert.AreEqual(GhostPhase.Warning, machine.Phase);

            Assert.AreEqual(GhostPhase.Warning, machine.Tick(1f, 100, true, 40));
            Assert.IsTrue(machine.CleaningAttackPending, "경고 중이라 못 시작하고 기다린다");
            Assert.IsFalse(machine.CleaningAttackFired);

            machine.Tick(4.1f, 100, true, 40);    // 어택
            machine.Tick(60.1f, 100, true, 40);   // 자연 진정
            Assert.AreEqual(GhostPhase.Active, machine.Tick(40.1f, 100, true, 40));
            Assert.AreEqual(GhostPhase.Warning, machine.Tick(0.1f, 100, true, 40), "활동으로 돌아온 다음 틱에 강제 어택");
            Assert.IsTrue(machine.CleaningAttackFired);
        }

        [Test]
        public void 스냅샷은_강제_어택_상태를_보존하고_예전_스냅샷은_다시_발동하지_않는다()
        {
            GhostStateMachine machine = NewMachine();
            machine.Tick(0.1f, 100, true, 40);
            machine.Tick(5.1f, 100, true, 40);
            GhostStateMachine.Snapshot snapshot = machine.CaptureSnapshot();
            Assert.IsTrue(snapshot.CleaningAttackFired);

            GhostStateMachine restored = NewMachine();
            restored.RestoreSnapshot(snapshot);
            Assert.IsTrue(restored.CleaningAttackFired);

            // 필드가 없던 예전 스냅샷(기본 생성자 인자) — 이미 40%를 넘긴 판이면 다시 발동하지 않는다.
            var legacy = new GhostStateMachine.Snapshot(GhostPhase.Active, 0f, 10f, true, false, false, 100);
            GhostStateMachine old = NewMachine();
            old.RestoreSnapshot(legacy);
            Assert.AreEqual(GhostPhase.Active, old.Tick(1f, 100, true, 60));
        }

        [Test]
        public void 설정을_끄면_40퍼센트는_예전처럼_정신력_어택의_전제일_뿐이다()
        {
            Set(_settings, "_cleaningForcesAttackOnce", false);
            GhostStateMachine machine = NewMachine();

            Assert.AreEqual(GhostPhase.Active, machine.Tick(0.1f, 100, true, 40), "정신력 100이면 어택하지 않는다");
            Assert.IsFalse(machine.CleaningAttackPending);
        }

        // ---- 0~30 계속 어택 ----

        private GhostStateMachine NewCriticalAttack()
        {
            GhostStateMachine machine = NewMachine();
            machine.Tick(0f, 20, true, 40);       // 0~30 → 강제 경고(고위험)
            machine.Tick(5.1f, 20, true, 40);     // 어택
            Assert.AreEqual(GhostPhase.Attack, machine.Phase);
            Assert.IsTrue(machine.IsHighRiskAttack);
            return machine;
        }

        [Test]
        public void 팀_정신력이_0에서_30이면_어택_시간이_지나도_계속_어택한다()
        {
            GhostStateMachine machine = NewCriticalAttack();

            Assert.AreEqual(GhostPhase.Attack, machine.Tick(61f, 20, true, 40), "일반 60초가 지났어도 계속");
            Assert.AreEqual(GhostPhase.Attack, machine.Tick(60f, 20, true, 40), "고위험 90초가 지났어도 계속");
            Assert.AreEqual(GhostPhase.Attack, machine.Tick(600f, 30, true, 40), "30은 아직 0~30");
        }

        [Test]
        public void 정신력이_31_이상으로_오르면_다음_틱에_어택이_끝난다()
        {
            GhostStateMachine machine = NewCriticalAttack();
            machine.Tick(200f, 20, true, 40);

            Assert.AreEqual(GhostPhase.Calming, machine.Tick(0.1f, 31, true, 40));
        }

        [Test]
        public void 설정을_끄면_고위험_어택은_90초에_끝난다()
        {
            Set(_settings, "_attackContinuesWhileCritical", false);
            GhostStateMachine machine = NewCriticalAttack();

            Assert.AreEqual(GhostPhase.Attack, machine.Tick(89f, 20, true, 40));
            Assert.AreEqual(GhostPhase.Calming, machine.Tick(1.1f, 20, true, 40));
        }

        [Test]
        public void 정신력_31에서_60의_일반_어택은_60초에_끝난다()
        {
            GhostStateMachine machine = NewMachine();
            machine.Tick(10f, 55, true, 40);
            machine.Tick(5f, 55, true, 40);
            Assert.AreEqual(GhostPhase.Attack, machine.Phase);

            Assert.AreEqual(GhostPhase.Calming, machine.Tick(61f, 55, true, 40));
        }

        // ---- 난이도별 돌진 속도 ----

        [TestCase(GhostDifficulty.Easy, 5f)]
        [TestCase(GhostDifficulty.Normal, 7f)]
        [TestCase(GhostDifficulty.Hard, 9f)]
        public void 돌진_속도는_난이도별이다(GhostDifficulty difficulty, float expected)
        {
            Set(_settings, "_difficulty", difficulty);

            Assert.AreEqual(expected, _settings.DinoChargeSpeed);
        }

        [Test]
        public void 기본_난이도는_보통이고_Tutorial_에셋은_쉬움이다()
        {
            Assert.AreEqual(GhostDifficulty.Normal, _settings.Difficulty);

            var tutorial = UnityEditor.AssetDatabase.LoadAssetAtPath<GhostPrototypeSettings>(
                "Assets/Settings/Gameplay/GhostPrototypeSettings_Tutorial.asset");
            var stage = UnityEditor.AssetDatabase.LoadAssetAtPath<GhostPrototypeSettings>(
                "Assets/Settings/Gameplay/GhostPrototypeSettings_Default.asset");
            Assert.AreEqual(GhostDifficulty.Easy, tutorial.Difficulty);
            Assert.AreEqual(GhostDifficulty.Normal, stage.Difficulty);
            Assert.Greater(stage.HighRiskVisionDistance, stage.VisionDistance, "고위험 탐지 강화 — 거리");
            Assert.Greater(stage.HighRiskVisionAngle, stage.VisionAngle, "고위험 탐지 강화 — 각도");
        }

        // ---- 말소리 소음 ----

        [TestCase(-60f, 0f)]
        [TestCase(-40f, 0f)]
        [TestCase(-27.5f, 0.5f)]
        [TestCase(-15f, 1f)]
        [TestCase(-3f, 1f)]
        public void 말소리_크기는_조용함_기준과_시끄러움_기준_사이로_보간한다(float decibels, float expected)
        {
            Assert.AreEqual(expected, GhostVoiceNoise.Loudness(decibels, -40f, -15f), 1e-4f);
        }

        [Test]
        public void 말소리_반경은_보통_8m에서_높음_12m까지_커진다()
        {
            Assert.AreEqual(8f, GhostVoiceNoise.Radius(-50f, -40f, -15f, 8f, 12f), 1e-4f);
            Assert.AreEqual(10f, GhostVoiceNoise.Radius(-27.5f, -40f, -15f, 8f, 12f), 1e-4f);
            Assert.AreEqual(12f, GhostVoiceNoise.Radius(-10f, -40f, -15f, 8f, 12f), 1e-4f);
        }

        [Test]
        public void 말소리_소음_수준은_절반을_넘으면_높음이다()
        {
            Assert.AreEqual(GhostNoiseLevel.Medium, GhostVoiceNoise.Level(0.49f));
            Assert.AreEqual(GhostNoiseLevel.High, GhostVoiceNoise.Level(0.5f));
        }

        [Test]
        public void 기준이_같거나_뒤집혀도_말소리_계산이_깨지지_않는다()
        {
            Assert.AreEqual(1f, GhostVoiceNoise.Loudness(-20f, -20f, -20f));
            Assert.AreEqual(0f, GhostVoiceNoise.Loudness(-30f, -20f, -20f));
            Assert.AreEqual(12f, GhostVoiceNoise.Radius(0f, -10f, -40f, 12f, 8f), "최대가 최소보다 작으면 최소로 맞춘다");
        }
    }
}
