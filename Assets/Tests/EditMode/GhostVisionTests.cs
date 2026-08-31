using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>귀신 원뿔형 시야(기획서 §8.1)의 각도·거리 판정과 시야 표시 메시 생성을 검증한다.</summary>
    public sealed class GhostVisionTests
    {
        private const float ConeAngle = 120f;
        private const float MaxDistance = 15f;

        [Test]
        public void 정면_거리안_대상은_시야_안이다()
        {
            var toTarget = new Vector3(0f, 0f, 5f);
            Assert.IsTrue(GhostVision.IsInsideCone(
                Vector3.forward, toTarget, ConeAngle, toTarget.magnitude, MaxDistance));
        }

        [Test]
        public void 시야_반각_안쪽_대상은_시야_안이다()
        {
            Vector3 toTarget = Quaternion.Euler(0f, 55f, 0f) * Vector3.forward * 5f;
            Assert.IsTrue(GhostVision.IsInsideCone(
                Vector3.forward, toTarget, ConeAngle, toTarget.magnitude, MaxDistance));
        }

        [Test]
        public void 시야_반각_밖_대상은_시야_밖이다()
        {
            Vector3 toTarget = Quaternion.Euler(0f, 61f, 0f) * Vector3.forward * 5f;
            Assert.IsFalse(GhostVision.IsInsideCone(
                Vector3.forward, toTarget, ConeAngle, toTarget.magnitude, MaxDistance));
        }

        [Test]
        public void 옆_90도_대상은_120도_시야_밖이다()
        {
            var toTarget = new Vector3(5f, 0f, 0f);
            Assert.IsFalse(GhostVision.IsInsideCone(
                Vector3.forward, toTarget, ConeAngle, toTarget.magnitude, MaxDistance));
        }

        [Test]
        public void 뒤쪽_대상은_시야_밖이다()
        {
            var toTarget = new Vector3(0f, 0f, -5f);
            Assert.IsFalse(GhostVision.IsInsideCone(
                Vector3.forward, toTarget, ConeAngle, toTarget.magnitude, MaxDistance));
        }

        [Test]
        public void 시야_거리를_넘은_대상은_정면이어도_시야_밖이다()
        {
            var toTarget = new Vector3(0f, 0f, 20f);
            Assert.IsFalse(GhostVision.IsInsideCone(
                Vector3.forward, toTarget, ConeAngle, toTarget.magnitude, MaxDistance));
        }

        [Test]
        public void 시야_표시_메시는_부채꼴_정점과_양면_삼각형을_만든다()
        {
            const int segments = 24;
            Mesh mesh = GhostVision.BuildConeMesh(MaxDistance, ConeAngle, segments);

            Assert.AreEqual(segments + 2, mesh.vertexCount);
            Assert.AreEqual(segments * 6, mesh.triangles.Length);
            Assert.Greater(mesh.bounds.size.sqrMagnitude, 0f);

            Object.DestroyImmediate(mesh);
        }
    }
}
