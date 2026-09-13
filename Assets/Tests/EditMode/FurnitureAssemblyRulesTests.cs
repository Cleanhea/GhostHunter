using System.Collections.Generic;
using GhostHunter.Gameplay.FurnitureDriver;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 조립 성립 판정(기획서 §4.3)을 순수 로직 단위로 검증한다 — 혼입·개수 초과·부분 집합·
    /// 같은 종류 출처 혼합·빈 영역. 실제 레시피(더블 침대·옷장 2종·식탁·선반 2종)를 그대로 쓴다.
    /// </summary>
    public sealed class FurnitureAssemblyRulesTests
    {
        private FurnitureDisassemblyRecipe _bed;
        private FurnitureDisassemblyRecipe _wardrobe;
        private FurnitureDisassemblyRecipe[] _recipes;

        [SetUp]
        public void SetUp()
        {
            _bed = ScriptableObject.CreateInstance<FurnitureDisassemblyRecipe>();
            _bed.Configure("DoubleBed_1.6x2.0", "더블 침대", new[]
            {
                new FurniturePartRequirement("Mattress", 1),
                new FurniturePartRequirement("BedHead", 1),
                new FurniturePartRequirement("BedLeg", 1),
            });

            _wardrobe = ScriptableObject.CreateInstance<FurnitureDisassemblyRecipe>();
            _wardrobe.Configure("Wardrobe_1.2x0.6", "옷장(작은)", new[]
            {
                new FurniturePartRequirement("DoorPanel_1.2", 1),
                new FurniturePartRequirement("Hanger_1.2", 1),
                new FurniturePartRequirement("Clothes_1.2", 1),
            });

            _recipes = new[] { _bed, _wardrobe };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_bed);
            Object.DestroyImmediate(_wardrobe);
        }

        private static Dictionary<string, int> Counts(params (string part, int count)[] entries)
        {
            var counts = new Dictionary<string, int>();
            foreach ((string part, int count) in entries)
                counts[part] = count;
            return counts;
        }

        [Test]
        public void 빈_영역은_Empty다()
        {
            FurnitureAssemblyEvaluation result = FurnitureAssemblyRules.Evaluate(
                Counts(), _recipes);
            Assert.AreEqual(FurnitureAssemblyState.Empty, result.State);
            Assert.IsNull(result.MatchedRecipe);
        }

        [Test]
        public void 부품이_일부만_있으면_Partial이다()
        {
            FurnitureAssemblyEvaluation result = FurnitureAssemblyRules.Evaluate(
                Counts(("Mattress", 1)), _recipes);
            Assert.AreEqual(FurnitureAssemblyState.Partial, result.State);
            Assert.AreEqual(_bed, result.MatchedRecipe);
        }

        [Test]
        public void 올바른_부품이_전부_있으면_Ready다()
        {
            FurnitureAssemblyEvaluation result = FurnitureAssemblyRules.Evaluate(
                Counts(("Mattress", 1), ("BedHead", 1), ("BedLeg", 1)), _recipes);
            Assert.AreEqual(FurnitureAssemblyState.Ready, result.State);
            Assert.AreEqual(_bed, result.MatchedRecipe);
        }

        [Test]
        public void 다른_가구_부품이_섞이면_전부_있어도_Invalid다()
        {
            FurnitureAssemblyEvaluation result = FurnitureAssemblyRules.Evaluate(
                Counts(("Mattress", 1), ("BedHead", 1), ("BedLeg", 1), ("DoorPanel_1.2", 1)), _recipes);
            Assert.AreEqual(FurnitureAssemblyState.Invalid, result.State);
        }

        [Test]
        public void 부품_개수가_초과되면_Invalid다()
        {
            FurnitureAssemblyEvaluation result = FurnitureAssemblyRules.Evaluate(
                Counts(("Mattress", 1), ("BedHead", 1), ("BedLeg", 2)), _recipes);
            Assert.AreEqual(FurnitureAssemblyState.Invalid, result.State);
        }

        [Test]
        public void 어떤_레시피에도_속하지_않는_부품은_Invalid다()
        {
            FurnitureAssemblyEvaluation result = FurnitureAssemblyRules.Evaluate(
                Counts(("Unknown_Part", 1)), _recipes);
            Assert.AreEqual(FurnitureAssemblyState.Invalid, result.State);
        }

        [Test]
        public void 같은_종류_두_개체에서_나온_부품을_섞어도_Ready다()
        {
            // 더블 침대A의 매트리스·헤드 + 더블 침대B의 다리 — 규칙 2(§4.3): 출처를 구분하지 않는다.
            // 입력 자체가 부품 ID별 개수뿐이라 "어느 개체에서 나왔는지"는 판정에 들어오지 않는다.
            FurnitureAssemblyEvaluation result = FurnitureAssemblyRules.Evaluate(
                Counts(("Mattress", 1), ("BedHead", 1), ("BedLeg", 1)), _recipes);
            Assert.AreEqual(FurnitureAssemblyState.Ready, result.State);
        }

        [Test]
        public void 옷장_부품과_침대_부품은_서로_다른_레시피로_각각_판정된다()
        {
            FurnitureAssemblyEvaluation result = FurnitureAssemblyRules.Evaluate(
                Counts(("DoorPanel_1.2", 1), ("Hanger_1.2", 1), ("Clothes_1.2", 1)), _recipes);
            Assert.AreEqual(FurnitureAssemblyState.Ready, result.State);
            Assert.AreEqual(_wardrobe, result.MatchedRecipe);
        }

        [Test]
        public void 개수가_0인_항목은_무시한다()
        {
            FurnitureAssemblyEvaluation result = FurnitureAssemblyRules.Evaluate(
                Counts(("Mattress", 0), ("BedHead", 0)), _recipes);
            Assert.AreEqual(FurnitureAssemblyState.Empty, result.State);
        }
    }
}
