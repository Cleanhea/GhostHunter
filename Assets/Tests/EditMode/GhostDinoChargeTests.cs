using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>'박치기 공룡' 귀신 사이클(project/ghost-dino.md §2)과 어택 종료 유예.</summary>
    public sealed class GhostDinoChargeTests
    {
        private const float Step = 0.1f;

        private static GhostDinoCharge Create() => new()
        {
            ChaseSeconds = 8f,
            PrepareSeconds = 2f,
            DirectionLockSeconds = 0.7f,
            StunSeconds = 5f,
        };

        private static void Run(GhostDinoCharge dino, float seconds, bool hasTarget = true)
        {
            int ticks = Mathf.RoundToInt(seconds / Step);
            for (int i = 0; i < ticks; i++)
                dino.Tick(Step, hasTarget);
        }

        /// <summary>그 단계에 들어올 때까지 돌린다 — 실수 누적 오차에 경계 틱이 흔들리지 않게, 들어온 직후(경과 0)에서 시작한다.</summary>
        private static void RunUntil(GhostDinoCharge dino, GhostDinoCharge.Stage stage, float maxSeconds = 20f)
        {
            int ticks = Mathf.RoundToInt(maxSeconds / Step);
            for (int i = 0; i < ticks && dino.Current != stage; i++)
                dino.Tick(Step, true);
            Assert.AreEqual(stage, dino.Current, $"{maxSeconds}초 안에 {stage} 단계에 들어가지 못했다");
            Assert.AreEqual(0f, dino.Elapsed, "단계에 들어오면 경과 시간은 0이다");
        }

        [Test]
        public void 어택이_시작되면_8초쯤_추격한_뒤_돌진을_준비한다()
        {
            GhostDinoCharge dino = Create();

            Run(dino, 7.7f);
            Assert.AreEqual(GhostDinoCharge.Stage.Chase, dino.Current);
            Assert.IsFalse(dino.IsCommitted);

            Run(dino, 0.4f);
            Assert.AreEqual(GhostDinoCharge.Stage.Prepare, dino.Current, "8초가 지났다");
            Assert.IsTrue(dino.IsCommitted);
        }

        [Test]
        public void 타깃이_없으면_추격이_8초를_넘겨도_준비로_넘어가지_않고_생기면_바로_넘어간다()
        {
            GhostDinoCharge dino = Create();

            Run(dino, 12f, hasTarget: false);
            Assert.AreEqual(GhostDinoCharge.Stage.Chase, dino.Current);

            dino.Tick(Step, hasTarget: true);
            Assert.AreEqual(GhostDinoCharge.Stage.Prepare, dino.Current);
        }

        [Test]
        public void Tutorial_귀신은_뒤돌아보지마_대신_박치기를_쓴다()
        {
            var tutorial = UnityEditor.AssetDatabase.LoadAssetAtPath<GhostPrototypeSettings>(
                "Assets/Settings/Gameplay/GhostPrototypeSettings_Tutorial.asset");
            var stage = UnityEditor.AssetDatabase.LoadAssetAtPath<GhostPrototypeSettings>(
                "Assets/Settings/Gameplay/GhostPrototypeSettings_Default.asset");
            Assert.IsInstanceOf<GhostDinoSettings>(tutorial, "2026-11-09 사용자 결정 — Tutorial 귀신을 박치기 귀신으로");
            Assert.IsFalse(tutorial.GazeFreezeEnabled, "두 기믹을 같이 켜지 않는다");
            Assert.IsNotInstanceOf<GhostDinoSettings>(stage, "기본 귀신 설정(Default)은 박치기가 아니다 — ADR-0024");
        }

        [Test]
        public void 준비는_2초이고_앞_1_3초는_방향을_조정하고_뒤_0_7초는_고정한다()
        {
            GhostDinoCharge dino = Create();
            RunUntil(dino, GhostDinoCharge.Stage.Prepare);

            Run(dino, 1.2f);
            Assert.IsFalse(dino.IsDirectionLocked, "1.2초: 아직 방향을 따라 조정한다");

            Run(dino, 0.2f);
            Assert.IsTrue(dino.IsDirectionLocked, "1.4초: 고정 구간");

            Run(dino, 0.4f);
            Assert.AreEqual(GhostDinoCharge.Stage.Prepare, dino.Current, "1.8초: 아직 준비 중");
            Assert.IsTrue(dino.IsDirectionLocked);

            Run(dino, 0.3f);
            Assert.AreEqual(GhostDinoCharge.Stage.Charge, dino.Current, "2초가 지나면 돌진한다");
            Assert.IsFalse(dino.IsDirectionLocked, "고정 구간은 준비 단계에서만 의미가 있다");
        }

        [Test]
        public void 돌진은_충돌로_끝내기_전에는_계속되고_끝나면_5초_스턴_뒤_다시_8초_추격한다()
        {
            GhostDinoCharge dino = Create();
            RunUntil(dino, GhostDinoCharge.Stage.Prepare);
            RunUntil(dino, GhostDinoCharge.Stage.Charge);

            Run(dino, 30f);
            Assert.AreEqual(GhostDinoCharge.Stage.Charge, dino.Current, "첫 충돌 전까지 제한 없음");
            Assert.IsTrue(dino.IsCommitted);

            dino.EndCharge();
            Assert.AreEqual(GhostDinoCharge.Stage.Stun, dino.Current);
            Assert.IsFalse(dino.IsCommitted, "스턴 중에는 어택 종료를 미루지 않는다");

            Run(dino, 4.7f);
            Assert.AreEqual(GhostDinoCharge.Stage.Stun, dino.Current);
            Run(dino, 0.4f);
            Assert.AreEqual(GhostDinoCharge.Stage.Chase, dino.Current, "5초가 지나 스턴이 풀렸다");

            Run(dino, 7.7f);
            Assert.AreEqual(GhostDinoCharge.Stage.Chase, dino.Current, "스턴 뒤에도 8초 추격을 다시 한다");
            Run(dino, 0.4f);
            Assert.AreEqual(GhostDinoCharge.Stage.Prepare, dino.Current);
        }

        [Test]
        public void 돌진_중이_아닐_때의_충돌_종료는_무시한다()
        {
            GhostDinoCharge dino = Create();

            dino.EndCharge();
            Assert.AreEqual(GhostDinoCharge.Stage.Chase, dino.Current);

            RunUntil(dino, GhostDinoCharge.Stage.Prepare);
            dino.EndCharge();
            Assert.AreEqual(GhostDinoCharge.Stage.Prepare, dino.Current);
        }

        [Test]
        public void 리셋하면_추격_처음으로_돌아간다()
        {
            GhostDinoCharge dino = Create();
            RunUntil(dino, GhostDinoCharge.Stage.Prepare);
            RunUntil(dino, GhostDinoCharge.Stage.Charge);

            dino.Reset();

            Assert.AreEqual(GhostDinoCharge.Stage.Chase, dino.Current);
            Assert.AreEqual(0f, dino.Elapsed);
            Run(dino, 7.7f);
            Assert.AreEqual(GhostDinoCharge.Stage.Chase, dino.Current);
        }

        [TestCase(0f, 1)]
        [TestCase(0.1f, 1)]
        [TestCase(0.19f, 1)]
        [TestCase(0.4f, 2)]
        [TestCase(3.7f, 18)]
        [TestCase(12f, 60)]
        public void 가구_내구도_손실은_거리_곱_5를_버림하고_최소_1이다(float distance, int expected)
        {
            Assert.AreEqual(expected, GhostDinoCharge.FurnitureDamage(distance, 5f));
        }

        // ---- 어택 종료 유예 ----

        private static GhostStateMachine CreateMachineInAttack(GhostPrototypeSettings settings)
        {
            var machine = new GhostStateMachine(settings, () => 0d);
            machine.Tick(10f, 55, true, 40);   // 경고
            machine.Tick(5f, 55, true, 40);    // 어택
            Assert.AreEqual(GhostPhase.Attack, machine.Phase);
            return machine;
        }

        [Test]
        public void 돌진_중이면_어택_시간이_끝나도_어택을_끝내지_않고_풀리면_끝난다()
        {
            var settings = ScriptableObject.CreateInstance<GhostPrototypeSettings>();
            try
            {
                GhostStateMachine machine = CreateMachineInAttack(settings);
                machine.HoldAttackEnd = true;

                Assert.AreEqual(GhostPhase.Attack, machine.Tick(61f, 55, true, 40), "60초가 지났어도 돌진 중");
                Assert.AreEqual(GhostPhase.Attack, machine.Tick(5f, 55, true, 40));

                machine.HoldAttackEnd = false;
                Assert.AreEqual(GhostPhase.Calming, machine.Tick(0.01f, 55, true, 40), "돌진이 끝난 다음 틱에 종료");
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void 조기_종료와_강제_진정은_돌진_유예를_무시한다()
        {
            var settings = ScriptableObject.CreateInstance<GhostPrototypeSettings>();
            try
            {
                GhostStateMachine early = CreateMachineInAttack(settings);
                early.HoldAttackEnd = true;
                Assert.AreEqual(GhostPhase.Calming, early.Tick(1f, 70, true, 40), "정신력 회복 조기 종료는 유예하지 않는다");

                GhostStateMachine suppressed = CreateMachineInAttack(settings);
                suppressed.HoldAttackEnd = true;
                Assert.IsTrue(suppressed.ForceSuppression());
                Assert.AreEqual(GhostPhase.Calming, suppressed.Phase);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }
    }
}
