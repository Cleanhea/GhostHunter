using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>어택 중 천장등 깜빡임 무늬(<see cref="GhostAmbientLight.FlickerLevel"/>, 사용자 요청 2026-11-14).</summary>
    public sealed class GhostAttackLightTests
    {
        [Test]
        public void 깜빡임은_꺼짐이거나_하한과_1_사이이고_켜지는_비율은_설정을_따른다()
        {
            const float steps = 9f;
            int on = 0;
            const int samples = 4000;
            for (int i = 0; i < samples; i++)
            {
                float level = GhostAmbientLight.FlickerLevel(i / steps + 0.01f, 1234, steps, 0.55f, 0.5f);
                Assert.IsTrue(level == 0f || (level >= 0.5f && level <= 1f), $"단계 {i}: {level}");
                if (level > 0f)
                    on++;
            }

            Assert.AreEqual(0.55f, on / (float)samples, 0.04f, "켜지는 비율 ≈ 켜질 확률");
        }

        [Test]
        public void 같은_단계_안에서는_같은_값이고_조명마다_무늬가_다르다()
        {
            float a = GhostAmbientLight.FlickerLevel(1.001f, 7, 9f, 0.55f, 0.5f);
            float b = GhostAmbientLight.FlickerLevel(1.05f, 7, 9f, 0.55f, 0.5f);
            Assert.AreEqual(a, b, "1/9초 단계 안에서는 그대로");

            int differences = 0;
            for (int i = 0; i < 200; i++)
            {
                float t = i / 9f + 0.01f;
                if ((GhostAmbientLight.FlickerLevel(t, 7, 9f, 0.55f, 0.5f) > 0f)
                    != (GhostAmbientLight.FlickerLevel(t, 99991, 9f, 0.55f, 0.5f) > 0f))
                    differences++;
            }

            Assert.Greater(differences, 40, "씨앗(조명 위치)이 다르면 켜지고 꺼지는 순간이 다르다");
        }

        [Test]
        public void 켜질_확률이_1이면_항상_켜진다()
        {
            for (int i = 0; i < 100; i++)
                Assert.Greater(GhostAmbientLight.FlickerLevel(i * 0.37f, 5, 9f, 1f, 0.5f), 0f);
        }

        [Test]
        public void 귀신_기본_설정은_어택_천장등_깜빡임을_켠다()
        {
            var settings = ScriptableObject.CreateInstance<GhostPrototypeSettings>();
            try
            {
                Assert.IsTrue(settings.AttackRoomLightFlicker);
                Assert.Greater(settings.AttackFlickerStepsPerSecond, 0f);
                Assert.That(settings.AttackFlickerOnChance, Is.InRange(0.05f, 1f));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }
    }
}
