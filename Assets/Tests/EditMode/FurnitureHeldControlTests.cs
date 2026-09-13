using GhostHunter.Gameplay.Furniture;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>2인 잡기 고정 추종 속도와 휠 회전·기울이기 계산(throw-system.md §3·§3.1)을 검증한다.</summary>
    public sealed class FurnitureHeldControlTests
    {
        private const float Step = 0.02f;

        [Test]
        public void TrackingVelocity_한_스텝에_목표에_닿는_속도를_낸다()
        {
            Vector3 velocity = FurnitureHeldControl.TrackingVelocity(
                Vector3.zero, new Vector3(0f, 0f, 0.2f), Step, 15f);

            Assert.AreEqual(10f, velocity.z, 0.001f);
            Assert.AreEqual(0f, velocity.x, 0.001f);
        }

        [Test]
        public void TrackingVelocity_최대_속력을_넘지_않는다()
        {
            Vector3 velocity = FurnitureHeldControl.TrackingVelocity(
                Vector3.zero, new Vector3(10f, 0f, 0f), Step, 15f);

            Assert.AreEqual(15f, velocity.magnitude, 0.001f);
        }

        [Test]
        public void TrackingVelocity_목표에_있으면_멈춘다()
        {
            Vector3 velocity = FurnitureHeldControl.TrackingVelocity(Vector3.one, Vector3.one, Step, 15f);

            Assert.AreEqual(Vector3.zero, velocity);
        }

        [Test]
        public void TrackingAngularVelocity_한_스텝_적분하면_목표_자세가_된다()
        {
            Quaternion target = Quaternion.Euler(0f, 10f, 0f);
            Vector3 angular = FurnitureHeldControl.TrackingAngularVelocity(
                Quaternion.identity, target, Step, 720f);

            float degrees = angular.magnitude * Mathf.Rad2Deg * Step;
            Quaternion reached = Quaternion.AngleAxis(degrees, angular.normalized);

            Assert.Less(Quaternion.Angle(reached, target), 0.05f);
        }

        [Test]
        public void TrackingAngularVelocity_짧은_쪽으로_돈다()
        {
            Vector3 angular = FurnitureHeldControl.TrackingAngularVelocity(
                Quaternion.identity, Quaternion.Euler(0f, 190f, 0f), Step, 100000f);

            Assert.Less(angular.y, 0f, "190° 대신 -170° 쪽으로 돌아야 합니다.");
        }

        [Test]
        public void TrackingAngularVelocity_최대_각속력으로_자른다()
        {
            Vector3 angular = FurnitureHeldControl.TrackingAngularVelocity(
                Quaternion.identity, Quaternion.Euler(0f, 90f, 0f), Step, 720f);

            Assert.AreEqual(720f, angular.magnitude * Mathf.Rad2Deg, 0.01f);
        }

        [Test]
        public void TrackingAngularVelocity_같은_자세면_돌지_않는다()
        {
            Quaternion rotation = Quaternion.Euler(20f, 40f, 60f);
            Vector3 angular = FurnitureHeldControl.TrackingAngularVelocity(rotation, rotation, Step, 720f);

            Assert.AreEqual(Vector3.zero, angular);
        }

        [Test]
        public void ApplyWheel_회전_모드는_월드_수직축으로_한_칸_돈다()
        {
            Quaternion start = Quaternion.Euler(30f, 0f, 0f);
            Quaternion result = FurnitureHeldControl.ApplyWheel(
                start, 1, FurnitureRotateMode.Rotate, Vector3.right, 15f);

            Quaternion expected = Quaternion.AngleAxis(15f, Vector3.up) * start;
            Assert.Less(Quaternion.Angle(result, expected), 0.01f);
        }

        [Test]
        public void ApplyWheel_기울이기_모드는_조준_오른쪽_수평축으로_돈다()
        {
            Quaternion result = FurnitureHeldControl.ApplyWheel(
                Quaternion.identity, -1, FurnitureRotateMode.Tilt, Vector3.forward, 15f);

            Quaternion expected = Quaternion.AngleAxis(-15f, Vector3.right);
            Assert.Less(Quaternion.Angle(result, expected), 0.01f);
        }

        [Test]
        public void ApplyWheel_기울이기는_각도_제한_없이_한_바퀴를_돈다()
        {
            Quaternion rotation = Quaternion.identity;
            for (int i = 0; i < 24; i++)
                rotation = FurnitureHeldControl.ApplyWheel(rotation, 1, FurnitureRotateMode.Tilt, Vector3.forward, 15f);

            Assert.Less(Quaternion.Angle(rotation, Quaternion.identity), 0.1f);
        }

        [Test]
        public void TiltAxis_조준_피치와_무관하게_수평_오른쪽이다()
        {
            Vector3 axis = FurnitureHeldControl.TiltAxis(new Vector3(0f, -0.8f, 0.6f));

            Assert.Less(Vector3.Distance(axis, Vector3.right), 0.001f);
        }

        [Test]
        public void TiltAxis_동쪽을_조준하면_남쪽이_오른쪽이다()
        {
            Vector3 axis = FurnitureHeldControl.TiltAxis(Vector3.right);

            Assert.Less(Vector3.Distance(axis, Vector3.back), 0.001f);
        }

        [Test]
        public void TiltAxis_수직으로_조준하면_월드_X축을_쓴다()
        {
            Assert.AreEqual(Vector3.right, FurnitureHeldControl.TiltAxis(Vector3.down));
        }
    }
}
