using GhostHunter.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>배터리 팩 충전 속도 규칙(기획서 headlamp-system.md §8.2)과 헤드라이트 충전 수신을 검증한다.</summary>
    public sealed class BatteryPackChargingTests
    {
        private static BatteryPackCharging Create() => new(10f, 5f, 1f, 30f);

        [TestCase(0f, 10f)]
        [TestCase(0.99f, 10f)]
        [TestCase(1f, 15f)]
        [TestCase(2.5f, 20f)]
        [TestCase(3f, 25f)]
        [TestCase(4f, 30f)]
        [TestCase(30f, 30f)]
        public void 속도는_기본_10에서_1초마다_5씩_늘어_최대_30이다(float heldSeconds, float expected)
        {
            Assert.AreEqual(expected, Create().RateAt(heldSeconds), 0.0001f);
        }

        [Test]
        public void 연속_홀드하면_시간이_쌓이며_속도가_올라간다()
        {
            BatteryPackCharging charging = Create();
            Assert.AreEqual(10f, charging.CurrentRate);

            float total = 0f;
            // 0.25초는 이진수로 정확해서 단계 경계(1초 간격)가 틀어지지 않는다.
            for (int i = 0; i < 20; i++)
                total += charging.Tick(0.25f, true);

            // 5초 동안: 10 + 15 + 20 + 25 + 30 = 100
            Assert.AreEqual(100f, total, 0.001f);
            Assert.AreEqual(30f, charging.CurrentRate, 0.0001f);
        }

        [Test]
        public void 홀드를_해제하면_속도가_기본으로_돌아가고_다시_충전하면_10부터_시작한다()
        {
            BatteryPackCharging charging = Create();
            for (int i = 0; i < 30; i++)
                charging.Tick(0.1f, true);
            Assert.Greater(charging.CurrentRate, 10f);

            Assert.AreEqual(0f, charging.Tick(0.1f, false));
            Assert.AreEqual(0f, charging.HeldSeconds);
            Assert.AreEqual(10f, charging.CurrentRate);
            Assert.AreEqual(1f, charging.Tick(0.1f, true), 0.0001f, "다시 충전할 때는 기본 속도 10%p/초로 시작한다");
        }

        [Test]
        public void 충전하지_않는_동안은_충전량이_0이다()
        {
            BatteryPackCharging charging = Create();
            Assert.AreEqual(0f, charging.Tick(1f, false));
            Assert.AreEqual(0f, charging.Tick(0f, true));
        }

        [Test]
        public void 설정_에셋의_기본값은_기획_확정_수치다()
        {
            var settings = ScriptableObject.CreateInstance<BatteryPackSettings>();
            try
            {
                Assert.AreEqual(10f, settings.BaseRatePerSecond);
                Assert.AreEqual(5f, settings.RateStep);
                Assert.AreEqual(1f, settings.StepSeconds);
                Assert.AreEqual(30f, settings.MaxRatePerSecond);
                Assert.AreEqual(30f, settings.CreateCharging().RateAt(100f));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void 헤드라이트는_받은_만큼_충전되고_100을_넘지_않는다()
        {
            var battery = new HeadlampBattery(100f, 0.3f, 0f);
            battery.Restore(40f, false);

            Assert.AreEqual(25f, battery.AddCharge(25f), 0.0001f);
            Assert.AreEqual(65f, battery.Charge, 0.0001f);

            Assert.AreEqual(35f, battery.AddCharge(80f), 0.0001f, "가득 찬 만큼만 더해진다");
            Assert.AreEqual(100f, battery.Charge, 0.0001f);
            Assert.AreEqual(0f, battery.AddCharge(5f));
            Assert.AreEqual(0f, battery.AddCharge(-3f));
        }

        [Test]
        public void 헤드라이트는_충전_속도_0이면_드릴카_안에서도_저절로_차지_않는다()
        {
            // 자동 충전 제거(2026-10-29) — 헤드라이트는 충전 속도 0으로 만든다.
            var battery = new HeadlampBattery(100f, 0.3f, 0f);
            battery.Restore(50f, false);
            battery.Tick(10f, charging: true, drains: true);
            Assert.AreEqual(50f, battery.Charge, 0.0001f);
        }
    }
}
