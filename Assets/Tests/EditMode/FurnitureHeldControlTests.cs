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

        [Test]
        public void TryFollowRotation_두_손이_진입_때와_같은_방향이면_돌지_않는다()
        {
            Assert.IsTrue(FurnitureHeldControl.TryFollowRotation(
                Vector3.right, new Vector3(2.5f, 0f, 0f), 0.35f, 25f, out Quaternion follow));

            Assert.Less(Quaternion.Angle(follow, Quaternion.identity), 0.01f);
        }

        [Test]
        public void TryFollowRotation_두_사람이_돌아_서면_가구도_같은_각도로_돈다()
        {
            Assert.IsTrue(FurnitureHeldControl.TryFollowRotation(
                Vector3.right, new Vector3(0f, 0f, 1.5f), 0.35f, 25f, out Quaternion follow));

            Assert.Less(Vector3.Distance(follow * Vector3.right, Vector3.forward), 0.001f);
        }

        [Test]
        public void TryFollowRotation_높이_차이는_최대_기울기까지만_따른다()
        {
            Assert.IsTrue(FurnitureHeldControl.TryFollowRotation(
                Vector3.right, new Vector3(1f, 1f, 0f), 0.35f, 25f, out Quaternion follow));

            Vector3 axis = follow * Vector3.right;
            float elevation = Mathf.Atan2(axis.y, new Vector2(axis.x, axis.z).magnitude) * Mathf.Rad2Deg;
            Assert.AreEqual(25f, elevation, 0.01f, "손 1 쪽이 높으면 그쪽 끝이 최대 기울기만큼 올라가야 합니다.");
            Assert.Greater(axis.x, 0f, "수평 방향은 그대로여야 합니다.");
        }

        [Test]
        public void TryFollowRotation_기울기_0이면_수평을_유지한다()
        {
            Assert.IsTrue(FurnitureHeldControl.TryFollowRotation(
                Vector3.right, new Vector3(0f, 1f, 1f), 0.35f, 0f, out Quaternion follow));

            Assert.Less(Vector3.Distance(follow * Vector3.right, Vector3.forward), 0.001f);
        }

        [Test]
        public void TryFollowRotation_손잡이가_붙어_있거나_두_손이_겹치면_방향을_정하지_않는다()
        {
            Assert.IsFalse(FurnitureHeldControl.TryFollowRotation(
                new Vector3(0.2f, 0f, 0f), Vector3.forward, 0.35f, 25f, out _), "손잡이 간격이 짧다");
            Assert.IsFalse(FurnitureHeldControl.TryFollowRotation(
                Vector3.right, new Vector3(0.05f, 1f, 0f), 0.35f, 25f, out _), "두 손이 수평으로 겹친다");
        }

        [Test]
        public void SolveCarryPosition_손잡이_중점이_손_중점에_온다()
        {
            Quaternion rotation = Quaternion.Euler(0f, 90f, 0f);
            Vector3 gripMidpointLocal = new(0.5f, 0.2f, 0f);
            Vector3 handMidpoint = new(3f, 1.2f, -2f);

            Vector3 position = FurnitureHeldControl.SolveCarryPosition(handMidpoint, rotation, gripMidpointLocal);

            Assert.Less(Vector3.Distance(position + rotation * gripMidpointLocal, handMidpoint), 0.0001f);
        }

        [Test]
        public void LimitChange_최대_변화량만큼만_바꾼다()
        {
            Vector3 result = FurnitureHeldControl.LimitChange(Vector3.zero, new Vector3(0f, 0f, 10f), 1.2f);

            Assert.AreEqual(1.2f, result.z, 0.0001f);
            Assert.AreEqual(Vector3.one, FurnitureHeldControl.LimitChange(Vector3.zero, Vector3.one, 5f));
        }

        [Test]
        public void SmoothingFactor_시간_상수만큼_지나면_63퍼센트를_따라간다()
        {
            Assert.AreEqual(1f - Mathf.Exp(-1f), FurnitureHeldControl.SmoothingFactor(0.05f, 0.05f), 0.0001f);
            Assert.AreEqual(1f, FurnitureHeldControl.SmoothingFactor(0.02f, 0f), "0이면 즉시 따라간다");
        }
    }
}
