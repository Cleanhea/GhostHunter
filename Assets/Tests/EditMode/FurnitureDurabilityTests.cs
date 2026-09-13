using GhostHunter.Gameplay.FurnitureDriver;
using NUnit.Framework;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>내구도 계산(기획서 §3.3·§5) — 아이템 감소 클램프, 가구 상속, 조립 평균(소수점 버림).</summary>
    public sealed class FurnitureDurabilityTests
    {
        [Test]
        public void 사용_성공시_지정한_만큼_감소한다()
        {
            Assert.AreEqual(95, FurnitureDurability.ApplyItemUse(100, 5));
        }

        [Test]
        public void 내구도는_0_미만으로_내려가지_않는다()
        {
            Assert.AreEqual(0, FurnitureDurability.ApplyItemUse(3, 5));
        }

        [Test]
        public void 이미_0이면_계속_0이다()
        {
            Assert.AreEqual(0, FurnitureDurability.ApplyItemUse(0, 5));
        }

        [Test]
        public void 분해시_부품은_큰_가구와_동일한_내구도를_물려받는다()
        {
            Assert.AreEqual(60, FurnitureDurability.InheritOnDisassemble(60));
        }

        [Test]
        public void 조립시_부품_내구도의_평균을_소수점_버림으로_계산한다()
        {
            // 기획서 §5 예시: 60·60·30 → 50
            Assert.AreEqual(50, FurnitureDurability.AverageOnAssemble(new[] { 60, 60, 30 }));
        }

        [Test]
        public void 평균이_나누어떨어지지_않으면_버림한다()
        {
            // (60 + 61) / 2 = 60.5 → 60
            Assert.AreEqual(60, FurnitureDurability.AverageOnAssemble(new[] { 60, 61 }));
        }

        [Test]
        public void 부품이_없으면_0이다()
        {
            Assert.AreEqual(0, FurnitureDurability.AverageOnAssemble(System.Array.Empty<int>()));
        }
    }
}
