using GhostHunter.Gameplay.Furniture;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 발사각 보정은 던지기 손맛을 좌우하는 순수 계산이라 EditMode 로 고정해 둔다.
    /// 조준 방향이 어디를 향하든 결과는 항상 최소~최대 각도 사이의 단위 벡터여야 한다.
    /// </summary>
    public sealed class FurnitureLaunchDirectionTests
    {
        private const float MinAngle = 20f;
        private const float MaxAngle = 70f;
        private const float Tolerance = 0.01f;

        private static float ElevationDegrees(Vector3 direction)
        {
            Vector3 horizontal = Vector3.ProjectOnPlane(direction, Vector3.up);
            return Mathf.Atan2(direction.y, horizontal.magnitude) * Mathf.Rad2Deg;
        }

        [Test]
        public void ResolveLaunchDirection_지평선_아래를_겨냥_최소각으로_올라간다()
        {
            Vector3 result = FurnitureLauncher.ResolveLaunchDirection(
                new Vector3(0f, -1f, 1f),
                MinAngle,
                MaxAngle);

            Assert.AreEqual(MinAngle, ElevationDegrees(result), Tolerance);
        }

        [Test]
        public void ResolveLaunchDirection_거의_수직을_겨냥_최대각으로_내려간다()
        {
            Vector3 result = FurnitureLauncher.ResolveLaunchDirection(
                new Vector3(0f, 10f, 1f),
                MinAngle,
                MaxAngle);

            Assert.AreEqual(MaxAngle, ElevationDegrees(result), Tolerance);
        }

        [Test]
        public void ResolveLaunchDirection_범위_안의_조준각은_그대로_유지된다()
        {
            const float aimed = 45f;
            Vector3 aim = Quaternion.Euler(-aimed, 0f, 0f) * Vector3.forward;

            Vector3 result = FurnitureLauncher.ResolveLaunchDirection(aim, MinAngle, MaxAngle);

            Assert.AreEqual(aimed, ElevationDegrees(result), Tolerance);
        }

        [Test]
        public void ResolveLaunchDirection_결과는_항상_단위_벡터다()
        {
            Vector3 result = FurnitureLauncher.ResolveLaunchDirection(
                new Vector3(3f, -7f, -2f),
                MinAngle,
                MaxAngle);

            Assert.AreEqual(1f, result.magnitude, Tolerance);
        }

        /// <summary>
        /// 바로 위(수평 성분 0)를 겨냥하면 방위가 정해지지 않는다. 0 벡터를 정규화해
        /// NaN 을 내보내는 대신 전방(+Z)으로 떨어져야 한다.
        /// </summary>
        [Test]
        public void ResolveLaunchDirection_수평_성분이_없으면_전방을_향한다()
        {
            Vector3 result = FurnitureLauncher.ResolveLaunchDirection(
                Vector3.up,
                MinAngle,
                MaxAngle);

            Assert.Greater(result.z, 0f);
            Assert.AreEqual(0f, result.x, Tolerance);
        }

        /// <summary>
        /// 최소 > 최대로 뒤집힌 값이 들어와도 (설정 실수) 각도가 튀거나 NaN 이 나오면 안 된다.
        /// </summary>
        [Test]
        public void ResolveLaunchDirection_최소가_최대보다_커도_최소각을_따른다()
        {
            Vector3 result = FurnitureLauncher.ResolveLaunchDirection(
                new Vector3(0f, 0f, 1f),
                60f,
                30f);

            Assert.AreEqual(60f, ElevationDegrees(result), Tolerance);
        }
    }
}
