using GhostHunter.Gameplay.Furniture;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>차지 → 발사 힘 비율 곡선(throw-system.md §4) — 살짝 누르면 약하고 끝까지 누르면 최대다.</summary>
    public sealed class FurnitureChargeForceTests
    {
        [Test]
        public void 차지_0이면_최소_비율_1이면_최대다()
        {
            Assert.AreEqual(0.15f, FurnitureThrowSettings.ChargeForceRatio(0f, 0.15f, 2f), 0.0001f);
            Assert.AreEqual(1f, FurnitureThrowSettings.ChargeForceRatio(1f, 0.15f, 2f), 0.0001f);
        }

        [Test]
        public void 곡선이_클수록_살짝_누른_힘이_약하다()
        {
            float tapLinear = FurnitureThrowSettings.ChargeForceRatio(0.1f, 0.15f, 1f);
            float tapCurved = FurnitureThrowSettings.ChargeForceRatio(0.1f, 0.15f, 2f);

            Assert.AreEqual(0.235f, tapLinear, 0.0001f, "직선: 0.15 + 0.85 × 0.1");
            Assert.AreEqual(0.1585f, tapCurved, 0.0001f, "제곱: 0.15 + 0.85 × 0.01");
            Assert.Less(tapCurved, tapLinear);
        }

        [Test]
        public void 누를수록_힘이_줄지_않는다()
        {
            float previous = 0f;
            for (int i = 0; i <= 20; i++)
            {
                float ratio = FurnitureThrowSettings.ChargeForceRatio(i / 20f, 0.15f, 2.5f);
                Assert.GreaterOrEqual(ratio, previous);
                previous = ratio;
            }
        }

        [Test]
        public void 범위를_벗어난_입력은_자른다()
        {
            Assert.AreEqual(1f, FurnitureThrowSettings.ChargeForceRatio(3f, 0.15f, 2f), 0.0001f);
            Assert.AreEqual(0.15f, FurnitureThrowSettings.ChargeForceRatio(-1f, 0.15f, 2f), 0.0001f);
            Assert.AreEqual(
                FurnitureThrowSettings.ChargeForceRatio(0.5f, 0.15f, 1f),
                FurnitureThrowSettings.ChargeForceRatio(0.5f, 0.15f, 0.2f),
                0.0001f,
                "곡선 지수는 1 미만으로 내려가지 않는다");
        }

        [Test]
        public void 기본_설정은_살짝_누르면_최대의_20퍼센트_미만이다()
        {
            var settings = ScriptableObject.CreateInstance<FurnitureThrowSettings>();
            try
            {
                float tap = settings.ForceRatio(0.1f / settings.ChargeTime);
                Assert.Less(tap, 0.2f);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }
    }
}
