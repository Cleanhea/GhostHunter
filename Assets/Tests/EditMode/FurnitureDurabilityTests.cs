using GhostHunter.Gameplay.FurnitureDriver;
using GhostHunter.Gameplay.Furniture;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>내구도 계산(기획서 §3.3·§5) — 아이템 감소 클램프, 가구 상속, 조립 평균(소수점 버림).</summary>
    public sealed class FurnitureDurabilityTests
    {
        [TestCase(4.4f, 0)]
        [TestCase(6f, 0)]
        [TestCase(6.99f, 0)]
        [TestCase(7.7f, 1)]
        [TestCase(12f, 6)]
        [TestCase(30f, 24)]
        [TestCase(50f, 44)]
        [TestCase(1000f, 50)]
        public void 충돌_속도별_초기_피해_공식을_따른다(float speed, int expected)
        {
            Assert.AreEqual(expected, FurnitureCollisionDamage.Calculate(speed, 6f, 1f, 1f, 50));
        }

        [Test]
        public void 미끄러지는_속도는_피해에_포함하지_않는다()
        {
            Assert.AreEqual(2f, FurnitureCollisionDamage.NormalSpeed(new Vector3(30f, -2f, 0f), Vector3.up));
            Assert.AreEqual(2f, FurnitureCollisionDamage.NormalSpeed(new Vector3(-30f, 2f, 0f), Vector3.down));
        }

        [Test]
        public void 무게_계수와_상한을_적용한_뒤_버림한다()
        {
            Assert.AreEqual(7, FurnitureCollisionDamage.Calculate(11f, 6f, 1f, 1.5f, 50));
            Assert.AreEqual(5, FurnitureCollisionDamage.Calculate(11f, 6f, 2f, 1.5f, 5));
        }

        [Test]
        public void 유효하지_않은_속도는_피해가_없다()
        {
            Assert.AreEqual(0, FurnitureCollisionDamage.Calculate(float.NaN, 6f, 1f, 1f, 50));
            Assert.AreEqual(0, FurnitureCollisionDamage.Calculate(float.PositiveInfinity, 6f, 1f, 1f, 50));
            Assert.AreEqual(50, FurnitureCollisionDamage.Calculate(float.MaxValue, 6f, 1f, 1f, 50));
        }

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
        public void 내구도_0인_부품도_평균에_그대로_들어간다()
        {
            // 사라짐이 꺼져 있으면(2026-09-16 기본값) 0인 부품이 남아 조립에 쓰인다.
            Assert.AreEqual(10, FurnitureDurability.AverageOnAssemble(new[] { 0, 0, 30 }));
            Assert.AreEqual(0, FurnitureDurability.AverageOnAssemble(new[] { 0, 0, 0 }));
        }

        [Test]
        public void 부품이_없으면_0이다()
        {
            Assert.AreEqual(0, FurnitureDurability.AverageOnAssemble(System.Array.Empty<int>()));
        }
    }
}
