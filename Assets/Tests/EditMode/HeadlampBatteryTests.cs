using GhostHunter.Gameplay.Player;
using NUnit.Framework;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>헤드라이트 전원·배터리 규칙(docs/project/headlamp-system.md §2).</summary>
    public sealed class HeadlampBatteryTests
    {
        private const float Max = 100f;
        private const float Drain = 0.6f;
        private const float Recharge = 5f;

        private static HeadlampBattery Create() => new(Max, Drain, Recharge);

        [Test]
        public void 처음에는_꺼져_있고_배터리가_가득_차_있다()
        {
            HeadlampBattery battery = Create();

            Assert.IsFalse(battery.IsOn);
            Assert.AreEqual(Max, battery.Charge);
        }

        [Test]
        public void F키는_켜고_끄기를_번갈아_한다()
        {
            HeadlampBattery battery = Create();

            Assert.AreEqual(HeadlampToggleResult.TurnedOn, battery.Toggle(true));
            Assert.IsTrue(battery.IsOn);
            Assert.AreEqual(HeadlampToggleResult.TurnedOff, battery.Toggle(true));
            Assert.IsFalse(battery.IsOn);
        }

        [Test]
        public void 켜진_동안_초당_0점6씩_준다()
        {
            HeadlampBattery battery = Create();
            battery.Toggle(true);

            battery.Tick(10f, charging: false, drains: true);

            Assert.AreEqual(Max - 6f, battery.Charge, 0.0001f);
        }

        [Test]
        public void 꺼져_있거나_스테이지가_아니면_줄지_않는다()
        {
            HeadlampBattery battery = Create();
            battery.Tick(10f, charging: false, drains: true);
            Assert.AreEqual(Max, battery.Charge);

            battery.Toggle(true);
            battery.Tick(10f, charging: false, drains: false);
            Assert.AreEqual(Max, battery.Charge);
        }

        [Test]
        public void 바닥나면_자동으로_꺼지고_다시_켜지지_않는다()
        {
            HeadlampBattery battery = Create();
            battery.Toggle(true);

            bool autoOff = battery.Tick(Max / Drain + 1f, charging: false, drains: true);

            Assert.IsTrue(autoOff);
            Assert.IsFalse(battery.IsOn);
            Assert.AreEqual(0f, battery.Charge);
            Assert.AreEqual(HeadlampToggleResult.Empty, battery.Toggle(true));
            Assert.IsFalse(battery.IsOn);
        }

        [Test]
        public void 드릴카_안에서는_초당_5씩_충전되고_켜져_있어도_줄지_않는다()
        {
            HeadlampBattery battery = Create();
            battery.Toggle(true);
            battery.Tick(50f, charging: false, drains: true);
            float before = battery.Charge;

            battery.Tick(2f, charging: true, drains: true);

            Assert.AreEqual(before + 10f, battery.Charge, 0.0001f);
            Assert.IsTrue(battery.IsOn);
        }

        [Test]
        public void 충전은_최대치를_넘지_않는다()
        {
            HeadlampBattery battery = Create();

            battery.Tick(100f, charging: true, drains: true);

            Assert.AreEqual(Max, battery.Charge);
        }

        [Test]
        public void 바닥난_뒤_충전되면_다시_켤_수_있다()
        {
            HeadlampBattery battery = Create();
            battery.Toggle(true);
            battery.Tick(Max / Drain + 1f, charging: false, drains: true);

            battery.Tick(0.1f, charging: true, drains: true);

            Assert.AreEqual(HeadlampToggleResult.TurnedOn, battery.Toggle(true));
        }

        [Test]
        public void 켤_수_없는_상태에서는_켜지지_않지만_끄기는_된다()
        {
            HeadlampBattery battery = Create();
            Assert.AreEqual(HeadlampToggleResult.Blocked, battery.Toggle(false));
            Assert.IsFalse(battery.IsOn);

            battery.Toggle(true);
            Assert.AreEqual(HeadlampToggleResult.TurnedOff, battery.Toggle(false));
        }

        [Test]
        public void 복원은_범위를_지키고_빈_배터리는_켜진_채로_두지_않는다()
        {
            HeadlampBattery battery = Create();

            battery.Restore(150f, on: true);
            Assert.AreEqual(Max, battery.Charge);
            Assert.IsTrue(battery.IsOn);

            battery.Restore(0f, on: true);
            Assert.AreEqual(0f, battery.Charge);
            Assert.IsFalse(battery.IsOn);
        }

        [TestCase(0f, true)]      // 저전력 진입 즉시 첫 번째 깜빡임
        [TestCase(0.05f, true)]
        [TestCase(0.1f, false)]   // 다시 켜짐
        [TestCase(0.2f, true)]    // 두 번째
        [TestCase(0.38f, true)]   // 세 번째
        [TestCase(0.46f, false)]
        [TestCase(0.6f, false)]   // 묶음 끝(3 × 0.18초) 뒤에는 켜져 있다
        [TestCase(4.9f, false)]
        [TestCase(5.02f, true)]   // 5초 뒤 다음 묶음
        public void 저전력_깜빡임은_5초마다_3번이다(float secondsSinceLow, bool expectedDark)
        {
            bool dark = HeadlampBattery.IsBlinkDark(secondsSinceLow, 5f, 3, 0.08f, 0.1f);

            Assert.AreEqual(expectedDark, dark);
        }
    }
}
