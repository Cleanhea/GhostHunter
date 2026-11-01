using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>맨손 플레이어 밀치기(throw-system.md §6)의 방향·속도·넉백 감속·서버 거리 허용치를 검증한다.</summary>
    public sealed class PlayerShoveRulesTests
    {
        [Test]
        public void ResolveDirection_조준_방향을_수평으로_눕힌다()
        {
            Vector3 direction = PlayerShoveRules.ResolveDirection(new Vector3(0f, -0.6f, 0.8f), Vector3.right);

            Assert.AreEqual(0f, direction.y, 0.0001f);
            Assert.AreEqual(1f, direction.magnitude, 0.0001f);
            Assert.AreEqual(1f, direction.z, 0.0001f);
        }

        [Test]
        public void ResolveDirection_거의_수직으로_보면_대상_방향을_쓴다()
        {
            Vector3 direction = PlayerShoveRules.ResolveDirection(Vector3.down, new Vector3(2f, 1f, 0f));

            Assert.AreEqual(Vector3.right, direction);
        }

        [Test]
        public void ResolveDirection_둘_다_수평_성분이_없으면_영벡터다()
        {
            Assert.AreEqual(Vector3.zero, PlayerShoveRules.ResolveDirection(Vector3.up, Vector3.up));
        }

        [Test]
        public void KnockbackVelocity_힘_비율만큼_수평과_위_속도를_낸다()
        {
            Vector3 velocity = PlayerShoveRules.KnockbackVelocity(Vector3.forward, 0.5f, 10f, 2f);

            Assert.AreEqual(5f, velocity.z, 0.0001f);
            Assert.AreEqual(1f, velocity.y, 0.0001f);
            Assert.AreEqual(0f, velocity.x, 0.0001f);
        }

        [Test]
        public void KnockbackVelocity_비율은_0과_1로_자른다()
        {
            Assert.AreEqual(10f, PlayerShoveRules.KnockbackVelocity(Vector3.forward, 3f, 10f, 0f).z, 0.0001f);
            Assert.AreEqual(Vector3.zero, PlayerShoveRules.KnockbackVelocity(Vector3.forward, -1f, 10f, 2f));
        }

        [Test]
        public void DecayKnockback_감속만큼_줄고_0_아래로_뒤집히지_않는다()
        {
            Vector3 after = PlayerShoveRules.DecayKnockback(new Vector3(0f, 0f, 9f), 18f, 0.1f);
            Assert.AreEqual(7.2f, after.z, 0.0001f);

            Assert.AreEqual(Vector3.zero, PlayerShoveRules.DecayKnockback(new Vector3(0f, 0f, 1f), 18f, 1f));
        }

        [Test]
        public void ServerReach_지연_여유와_몸_반지름을_더한다()
        {
            Assert.AreEqual(2f * 1.2f + 0.8f, PlayerShoveRules.ServerReach(2f), 0.0001f);
        }

        [Test]
        public void 밀치기_힘은_살짝_누르면_최소_비율_끝까지_누르면_최대다()
        {
            FurnitureThrowSettings settings = ScriptableObject.CreateInstance<FurnitureThrowSettings>();
            try
            {
                Assert.AreEqual(0.3f, settings.ShoveForceRatio(0f), 0.0001f);
                Assert.AreEqual(1f, settings.ShoveForceRatio(1f), 0.0001f);
                Assert.Greater(settings.ShoveForceRatio(1f), settings.ShoveForceRatio(0.5f));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }
    }
}
