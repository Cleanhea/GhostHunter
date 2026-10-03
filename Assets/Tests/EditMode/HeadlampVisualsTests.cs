using GhostHunter.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    public sealed class HeadlampVisualsTests
    {
        private const float Hotspot = 0.32f;
        private const float Spill = 0.45f;
        private const float Ring = 0.12f;

        [Test]
        public void 쿠키는_가운데가_가장_밝고_바깥_각도에서_0이다()
        {
            Assert.AreEqual(1f, HeadlampVisuals.CookieProfile(0f, Hotspot, Spill, Ring), 1e-4f);
            Assert.AreEqual(0f, HeadlampVisuals.CookieProfile(1f, Hotspot, Spill, Ring), 1e-4f);
            Assert.AreEqual(0f, HeadlampVisuals.CookieProfile(1.4f, Hotspot, Spill, Ring), 1e-4f,
                "정사각형 쿠키의 모서리가 비치면 안 됩니다.");
        }

        [Test]
        public void 쿠키는_핫스팟_밖에도_주변광이_남는다()
        {
            float justOutside = HeadlampVisuals.CookieProfile(Hotspot + 0.1f, Hotspot, Spill, Ring);
            Assert.Greater(justOutside, Spill * 0.5f, "핫스팟 바로 밖이 너무 어둡습니다 — 동그란 원만 남습니다.");
            Assert.Less(justOutside, 1f);

            float nearEdge = HeadlampVisuals.CookieProfile(0.85f, Hotspot, Spill, Ring);
            Assert.Less(nearEdge, justOutside, "주변광은 가장자리로 갈수록 어두워져야 합니다.");
        }

        [Test]
        public void 쿠키_텍스처는_클램프이고_업로드_후_CPU_사본을_버린다()
        {
            Texture2D cookie = HeadlampVisuals.CreateCookie(Hotspot, Spill, Ring);
            try
            {
                Assert.AreEqual(TextureWrapMode.Clamp, cookie.wrapMode);
                Assert.AreEqual(HeadlampVisuals.CookieSize, cookie.width);
                Assert.IsFalse(cookie.isReadable, "업로드 후 CPU 사본은 버려야 합니다.");
            }
            finally
            {
                Object.DestroyImmediate(cookie);
            }
        }

        [Test]
        public void 빛줄기_메시는_원점에서_Z로_길이_1이고_경계는_최대_길이를_덮는다()
        {
            Mesh mesh = HeadlampVisuals.CreateBeamMesh(36f, 6f);
            try
            {
                float maxZ = 0f;
                foreach (Vector3 vertex in mesh.vertices)
                    maxZ = Mathf.Max(maxZ, vertex.z);

                Assert.AreEqual(1f, maxZ, 1e-4f);
                Assert.AreEqual(6f, mesh.bounds.max.z, 1e-4f);
                Assert.AreEqual(0f, mesh.bounds.min.z, 1e-4f);
                Assert.AreEqual(mesh.vertexCount, mesh.normals.Length);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
