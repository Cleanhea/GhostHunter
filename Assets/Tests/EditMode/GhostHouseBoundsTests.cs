using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>귀신의 집 내부 활동 경계(X/Z)를 검증한다.</summary>
    public sealed class GhostHouseBoundsTests
    {
        private static readonly Vector3 Center = new(2f, 4f, -3f);
        private static readonly Vector3 Extents = new(5f, 2f, 4f);

        [Test]
        public void 중심과_경계선의_점은_집_내부다()
        {
            Assert.IsTrue(GhostPrototypeController.IsInsideHouseBounds(Center, Center, Extents));
            Assert.IsTrue(GhostPrototypeController.IsInsideHouseBounds(
                new Vector3(7f, 100f, -7f), Center, Extents));
        }

        [Test]
        public void X축_또는_Z축을_넘은_점은_집_외부다()
        {
            Assert.IsFalse(GhostPrototypeController.IsInsideHouseBounds(
                new Vector3(7.01f, 4f, -3f), Center, Extents));
            Assert.IsFalse(GhostPrototypeController.IsInsideHouseBounds(
                new Vector3(2f, 4f, 1.01f), Center, Extents));
        }

        [Test]
        public void 경계_보정은_XZ만_제한하고_높이는_보존한다()
        {
            Vector3 clamped = GhostPrototypeController.ClampToHouseBounds(
                new Vector3(-10f, 12f, 9f), Center, Extents);

            Assert.AreEqual(new Vector3(-3f, 12f, 1f), clamped);
        }

        [Test]
        public void 집_내부_좌표는_경계_보정_후에도_변하지_않는다()
        {
            Vector3 inside = new(3f, -2f, -1f);

            Assert.AreEqual(
                inside,
                GhostPrototypeController.ClampToHouseBounds(inside, Center, Extents));
        }
    }
}
