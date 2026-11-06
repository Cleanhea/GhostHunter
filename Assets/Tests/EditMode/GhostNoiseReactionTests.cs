using System.Reflection;
using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>원문 0.4 §7 — 소리 기억의 우선순위·만료, 층별 활동량 가산, 층 번호 기준, 토글.</summary>
    public sealed class GhostNoiseReactionTests
    {
        private const float Memory = 8f;

        [Test]
        public void 소리가_없으면_꺼낼_수_없다()
        {
            var noise = new GhostNoiseMemory();
            Assert.IsFalse(noise.TryTake(0f, Memory, out _));
        }

        [Test]
        public void 꺼낸_소리는_소비되어_다시_나오지_않는다()
        {
            var noise = new GhostNoiseMemory();
            noise.Report(Vector3.right, GhostNoiseLevel.High, 1f, Memory);
            Assert.IsTrue(noise.TryTake(2f, Memory, out Vector3 position));
            Assert.AreEqual(Vector3.right, position);
            Assert.IsFalse(noise.TryTake(2f, Memory, out _));
        }

        [Test]
        public void 높은_수준의_소리는_낮은_수준을_덮고_반대는_무시한다()
        {
            var noise = new GhostNoiseMemory();
            noise.Report(Vector3.left, GhostNoiseLevel.Medium, 1f, Memory);
            noise.Report(Vector3.forward, GhostNoiseLevel.VeryHigh, 2f, Memory);
            noise.Report(Vector3.back, GhostNoiseLevel.High, 3f, Memory);
            Assert.IsTrue(noise.TryTake(4f, Memory, out Vector3 position));
            Assert.AreEqual(Vector3.forward, position, "가구 충돌·낙하(매우 높음)가 우선 조사된다");
        }

        [Test]
        public void 같은_수준이면_최신_소리가_이긴다()
        {
            var noise = new GhostNoiseMemory();
            noise.Report(Vector3.left, GhostNoiseLevel.High, 1f, Memory);
            noise.Report(Vector3.right, GhostNoiseLevel.High, 2f, Memory);
            Assert.IsTrue(noise.TryTake(3f, Memory, out Vector3 position));
            Assert.AreEqual(Vector3.right, position);
        }

        [Test]
        public void 기억_시간이_지난_소리는_버려지고_낮은_수준이_대신한다()
        {
            var noise = new GhostNoiseMemory();
            noise.Report(Vector3.forward, GhostNoiseLevel.VeryHigh, 0f, Memory);
            noise.Report(Vector3.left, GhostNoiseLevel.Medium, 20f, Memory);
            Assert.IsTrue(noise.TryTake(21f, Memory, out Vector3 position));
            Assert.AreEqual(Vector3.left, position, "오래된 큰 소리는 새 작은 소리를 막지 않는다");

            noise.Report(Vector3.forward, GhostNoiseLevel.High, 0f, Memory);
            Assert.IsFalse(noise.TryTake(Memory + 1f, Memory, out _), "만료된 소리는 조사하지 않는다");
        }

        [Test]
        public void 수준_None은_기록하지_않는다()
        {
            var noise = new GhostNoiseMemory();
            noise.Report(Vector3.one, GhostNoiseLevel.None, 0f, Memory);
            Assert.IsFalse(noise.HasNoise);
        }

        [Test]
        public void 활동량_가산은_가장_활동한_층이_최대이고_나머지는_비례한다()
        {
            var map = new GhostActivityMap();
            map.Add(0, 20f);
            map.Add(1, 10f);
            Assert.AreEqual(30f, map.Bonus(0, 30f, 2f), 1e-4f);
            Assert.AreEqual(15f, map.Bonus(1, 30f, 2f), 1e-4f);
            Assert.AreEqual(0f, map.Bonus(2, 30f, 2f), 1e-4f, "활동이 없는 층은 가산이 없다");
        }

        [Test]
        public void 활동이_거의_없으면_어느_층에도_가산하지_않는다()
        {
            var map = new GhostActivityMap();
            Assert.AreEqual(0f, map.Bonus(0, 30f, 2f), "기록이 없으면 편향 없음");
            map.Add(0, 1f);
            Assert.AreEqual(0f, map.Bonus(0, 30f, 2f), "최고 활동량이 최소 기준 미만");
        }

        [Test]
        public void 활동량은_반감기마다_절반이_되고_바닥_아래는_지워진다()
        {
            var map = new GhostActivityMap();
            map.Add(1, 8f);
            map.Decay(20f, 20f);
            Assert.AreEqual(4f, map.Get(1), 1e-3f);
            map.Decay(400f, 20f);
            Assert.AreEqual(0, map.FloorCount);
        }

        [Test]
        public void 음수_또는_0_활동은_쌓이지_않는다()
        {
            var map = new GhostActivityMap();
            map.Add(0, 0f);
            map.Add(0, -3f);
            Assert.AreEqual(0, map.FloorCount);
        }

        [Test]
        public void 층_번호는_방문_기록_칸과_같은_기준이다()
        {
            Assert.AreEqual(0, GhostRoamMemory.FloorOf(0f));
            Assert.AreEqual(0, GhostRoamMemory.FloorOf(1.4f));
            Assert.AreEqual(1, GhostRoamMemory.FloorOf(1.6f));
            Assert.AreEqual(1, GhostRoamMemory.FloorOf(3f));
            Assert.AreEqual(2, GhostRoamMemory.FloorOf(6f));

            var memory = new GhostRoamMemory(3f, 90f);
            Assert.AreEqual(GhostRoamMemory.FloorOf(4.2f), memory.CellOf(new Vector3(1f, 4.2f, 1f)).y);
        }

        [Test]
        public void 소리_조사는_전체_OFF_또는_개별_OFF에서_꺼진다()
        {
            var settings = ScriptableObject.CreateInstance<GhostPrototypeSettings>();
            try
            {
                Set(settings, "_aiExperimentsEnabled", true);
                Assert.IsTrue(settings.NoiseReactionEnabled, "기본값은 ON");
                int on = settings.AiFeatureMask;
                Set(settings, "_noiseReactionEnabled", false);
                Assert.IsFalse(settings.NoiseReactionEnabled);
                Assert.AreNotEqual(on, settings.AiFeatureMask, "토글이 바뀌면 마스크가 달라져 상태를 초기화한다");
                Set(settings, "_noiseReactionEnabled", true);
                Set(settings, "_aiExperimentsEnabled", false);
                Assert.IsFalse(settings.NoiseReactionEnabled, "전체 OFF가 우선");
                Assert.AreEqual(0, settings.AiFeatureMask);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void 소음_기본_수치는_원문_주기와_수준_순서를_지킨다()
        {
            var settings = ScriptableObject.CreateInstance<GhostPrototypeSettings>();
            try
            {
                Assert.AreEqual(0.5f, settings.NoiseScanInterval, "원문 0.4: 0.5초마다 탐지");
                Assert.Less(settings.SmallPropNoiseRadius, settings.LargeFurnitureNoiseRadius, "보통 < 높음");
                Assert.LessOrEqual(settings.LargeFurnitureNoiseRadius, settings.ImpactHearingRadius, "높음 ≤ 매우 높음");
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void 가구_소음_훑기는_가구_목록이_없어도_안전하다()
        {
            var scanner = new GhostFurnitureNoiseScanner();
            Assert.DoesNotThrow(() => scanner.Scan(null, 0.5f, _ => false, (_, _) => Assert.Fail()));
            Assert.DoesNotThrow(() => scanner.Scan(
                new GhostHunter.Gameplay.Furniture.FurnitureGrabTarget[1], 0.5f, _ => false, (_, _) => Assert.Fail()));
        }

        private static void Set(GhostPrototypeSettings settings, string field, object value)
        {
            typeof(GhostPrototypeSettings)
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(settings, value);
        }
    }
}
