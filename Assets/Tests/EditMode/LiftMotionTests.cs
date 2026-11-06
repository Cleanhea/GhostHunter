using GhostHunter.Gameplay.Lift;
using NUnit.Framework;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>승강 발판 한 프레임 이동 계산(가속·감속·정확한 도착)을 검증한다.</summary>
    public sealed class LiftMotionTests
    {
        private const float MaxSpeed = 1.5f;
        private const float Acceleration = 2f;
        private const float Dt = 1f / 60f;

        private static float Run(float start, float target, out float seconds, out float peakSpeed)
        {
            float height = start;
            float velocity = 0f;
            seconds = 0f;
            peakSpeed = 0f;
            for (int i = 0; i < 6000; i++)
            {
                float move = LiftMotion.Step(height, target, ref velocity, MaxSpeed, Acceleration, Dt, out bool arrived);
                height += move;
                seconds += Dt;
                peakSpeed = System.Math.Max(peakSpeed, System.Math.Abs(velocity));
                if (arrived)
                    return height;
            }

            return height;
        }

        [Test]
        public void 올라갈_때_목표에_정확히_도착한다()
        {
            float end = Run(0f, 3f, out float seconds, out _);
            Assert.AreEqual(3f, end, 0.0001f);
            Assert.Less(seconds, 10f, "도착하지 못하고 멈춰 서면 안 된다");
        }

        [Test]
        public void 내려갈_때_목표에_정확히_도착한다()
        {
            float end = Run(3.57f, 0f, out _, out _);
            Assert.AreEqual(0f, end, 0.0001f);
        }

        [Test]
        public void 속력은_최대_속력을_넘지_않는다()
        {
            Run(0f, 3f, out _, out float peak);
            Assert.LessOrEqual(peak, MaxSpeed + 0.0001f);
            Assert.Greater(peak, MaxSpeed * 0.9f, "긴 거리에서는 최대 속력에 닿아야 한다");
        }

        [Test]
        public void 짧은_거리는_최대_속력에_닿기_전에_감속해_멈춘다()
        {
            Run(0f, 0.3f, out _, out float peak);
            Assert.Less(peak, MaxSpeed);
        }

        [Test]
        public void 한_프레임에_목표를_지나치지_않는다()
        {
            float velocity = 5f;
            float move = LiftMotion.Step(2.99f, 3f, ref velocity, MaxSpeed, Acceleration, 1f, out bool arrived);
            Assert.IsTrue(arrived);
            Assert.AreEqual(0.01f, move, 0.0001f);
            Assert.AreEqual(0f, velocity);
        }

        [Test]
        public void 이미_도착해_있으면_움직이지_않고_도착으로_알린다()
        {
            float velocity = 1f;
            float move = LiftMotion.Step(3f, 3f, ref velocity, MaxSpeed, Acceleration, Dt, out bool arrived);
            Assert.IsTrue(arrived);
            Assert.AreEqual(0f, move, 0.0001f);
        }

        [Test]
        public void 시간이_0이면_움직이지_않는다()
        {
            float velocity = 0f;
            float move = LiftMotion.Step(0f, 3f, ref velocity, MaxSpeed, Acceleration, 0f, out bool arrived);
            Assert.IsFalse(arrived);
            Assert.AreEqual(0f, move);
        }
    }
}
