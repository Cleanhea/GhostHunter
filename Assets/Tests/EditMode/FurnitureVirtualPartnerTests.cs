using GhostHunter.Gameplay.Furniture;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>가상 두 번째 홀더(혼자 2인 들기 테스트)의 위치 계산.</summary>
    public sealed class FurnitureVirtualPartnerTests
    {
        [Test]
        public void 옆으로_서면_호스트_시선의_오른쪽이다()
        {
            FurnitureVirtualPartner.ComputePartnerAim(
                new Vector3(0f, 1.65f, 0f), Vector3.forward, front: 0f, side: 0.9f,
                out Vector3 origin, out Vector3 direction);

            Assert.AreEqual(0.9f, origin.x, 1e-4f);
            Assert.AreEqual(0f, origin.z, 1e-4f);
            Assert.AreEqual(1.65f, origin.y, 1e-4f);
            Assert.AreEqual(1f, Vector3.Dot(direction, Vector3.forward), 1e-4f);
        }

        [Test]
        public void 앞뒤로_서면_수평_진행_방향으로_떨어진다()
        {
            // 위를 올려다봐도 파트너 높이는 호스트 눈과 같고 수평으로만 떨어진다.
            Vector3 lookUp = new Vector3(0f, 1f, 1f).normalized;
            FurnitureVirtualPartner.ComputePartnerAim(
                new Vector3(5f, 1.65f, 5f), lookUp, front: 1.8f, side: 0f,
                out Vector3 origin, out Vector3 direction);

            Assert.AreEqual(5f, origin.x, 1e-4f);
            Assert.AreEqual(6.8f, origin.z, 1e-4f);
            Assert.AreEqual(1.65f, origin.y, 1e-4f);
            Assert.AreEqual(1f, Vector3.Dot(direction, lookUp), 1e-4f, "조준 방향은 호스트와 같다");
        }

        [Test]
        public void 거의_수직으로_봐도_위치가_유한하고_조준은_유지된다()
        {
            FurnitureVirtualPartner.ComputePartnerAim(
                Vector3.zero, Vector3.up, front: 1f, side: 1f, out Vector3 origin, out Vector3 direction);

            Assert.IsTrue(float.IsFinite(origin.x) && float.IsFinite(origin.y) && float.IsFinite(origin.z));
            Assert.AreEqual(1f, direction.magnitude, 1e-4f);

            FurnitureVirtualPartner.ComputePartnerAim(
                Vector3.zero, Vector3.zero, front: 0f, side: 0f, out _, out Vector3 fallback);
            Assert.AreEqual(1f, fallback.magnitude, 1e-4f, "시선이 0 벡터여도 조준은 정규화된 값이다");
        }

        [Test]
        public void 오프셋은_최대_거리로_잘린다()
        {
            FurnitureVirtualPartner.ComputePartnerAim(
                Vector3.zero, Vector3.forward, front: 100f, side: -100f, out Vector3 origin, out _);

            Assert.AreEqual(FurnitureVirtualPartner.MaxOffset, origin.z, 1e-4f);
            Assert.AreEqual(-FurnitureVirtualPartner.MaxOffset, origin.x, 1e-4f);
        }
    }
}
