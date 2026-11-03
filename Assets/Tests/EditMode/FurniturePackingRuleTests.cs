using GhostHunter.Gameplay.Map;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 가구 포장 판정 계산(FurniturePackingRule, 2026-11-03 기획 "2-2. 가구 포장 처리") — 구역 안 비율과 "끊김 없이 이어진 시간".
    /// 서버가 실제 가구에 적용하는 흐름은 PlayMode FurnitureDisassemblyFlowTests 에서 본다.
    /// </summary>
    public sealed class FurniturePackingRuleTests
    {
        private static readonly Vector3 ZoneSize = new(4f, 4f, 4f);

        [Test]
        public void 구역_안에_완전히_들어간_가구는_비율_1이다()
        {
            var bounds = new Bounds(Vector3.zero, new Vector3(2f, 2f, 2f));

            Assert.AreEqual(1f, FurniturePackingRule.InsideFraction(bounds, Matrix4x4.identity, ZoneSize), 0.001f);
        }

        [Test]
        public void 구역_밖의_가구는_비율_0이다()
        {
            var bounds = new Bounds(new Vector3(10f, 0f, 0f), new Vector3(2f, 2f, 2f));

            Assert.AreEqual(0f, FurniturePackingRule.InsideFraction(bounds, Matrix4x4.identity, ZoneSize), 0.001f);
        }

        [Test]
        public void 가구가_절반만_걸치면_비율은_절반이다()
        {
            // 가구 x 1~3, 구역 x −2~2 → 가구의 앞 절반만 안.
            var bounds = new Bounds(new Vector3(2f, 0f, 0f), new Vector3(2f, 2f, 2f));

            Assert.AreEqual(0.5f, FurniturePackingRule.InsideFraction(bounds, Matrix4x4.identity, ZoneSize), 0.01f);
        }

        [Test]
        public void 구역이_회전해_있어도_구역_기준으로_센다()
        {
            // 구역: 로컬 (4, 4, 2), 월드 (5, 0, 0) 에서 y 로 90° 돌았다 → 월드로는 x 폭 2, z 폭 4.
            // 가구 월드 x 3~7(폭 4) 중 구역 x 4~6 에 든 절반만 안, z 는 모두 안.
            Matrix4x4 worldToLocal = Matrix4x4.TRS(new Vector3(5f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), Vector3.one).inverse;
            var bounds = new Bounds(new Vector3(5f, 0f, 0f), new Vector3(4f, 2f, 2f));

            float fraction = FurniturePackingRule.InsideFraction(bounds, worldToLocal, new Vector3(4f, 4f, 2f));

            Assert.AreEqual(0.5f, fraction, 0.01f);
        }

        [Test]
        public void 기획_85퍼센트_기준을_넘으면_통과하고_못_미치면_실패한다()
        {
            // 구역 x ≤ 2, 가구 폭 2. 가구 x 0.3~2.3 이면 안쪽이 1.7(85%) — 표본 칸으로는 8칸 중 7칸(0.875).
            // 가구 x 0.8~2.8 이면 안쪽 1.2(60%) — 8칸 중 5칸(0.625).
            var mostlyInside = new Bounds(new Vector3(1.3f, 0f, 0f), new Vector3(2f, 2f, 2f));
            var halfOutside = new Bounds(new Vector3(1.8f, 0f, 0f), new Vector3(2f, 2f, 2f));
            const float threshold = 0.85f; // 기본값 자체는 아래 설정 테스트가 고정한다.

            Assert.GreaterOrEqual(FurniturePackingRule.InsideFraction(mostlyInside, Matrix4x4.identity, ZoneSize), threshold);
            Assert.Less(FurniturePackingRule.InsideFraction(halfOutside, Matrix4x4.identity, ZoneSize), threshold);
        }

        [Test]
        public void 조건이_이어지는_동안만_시간이_쌓이고_한_번_어긋나면_처음부터다()
        {
            float elapsed = 0f;
            elapsed = FurniturePackingRule.Advance(elapsed, true, 0.4f);
            elapsed = FurniturePackingRule.Advance(elapsed, true, 0.4f);
            Assert.AreEqual(0.8f, elapsed, 0.0001f);
            Assert.IsFalse(FurniturePackingRule.IsComplete(elapsed, 1f), "1초 미만은 포장되지 않는다");

            elapsed = FurniturePackingRule.Advance(elapsed, false, 0.02f);
            Assert.AreEqual(0f, elapsed, "붙잡거나 구역을 벗어나면 처음부터 센다");

            elapsed = FurniturePackingRule.Advance(elapsed, true, 1f);
            Assert.IsTrue(FurniturePackingRule.IsComplete(elapsed, 1f), "정확히 1초 이어지면 포장된다");
        }

        [Test]
        public void 설정_기본값은_기획_수치다()
        {
            var settings = ScriptableObject.CreateInstance<FurnitureSpawnSettings>();
            try
            {
                Assert.AreEqual(0.85f, settings.PackInsideRatio, 0.0001f, "가구의 85% 이상");
                Assert.AreEqual(1f, settings.PackHoldSeconds, 0.0001f, "1초 이상 유지");
                Assert.Greater(settings.PackMaxSpeed, 0f);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }
    }
}
