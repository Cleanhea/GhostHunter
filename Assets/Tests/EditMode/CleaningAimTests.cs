using GhostHunter.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>청소 요청의 조준 원점과 비정상 벡터 경계를 검증한다.</summary>
    public sealed class CleaningAimTests
    {
        [Test]
        public void 플레이어_눈높이의_정상_조준을_허용한다()
        {
            Assert.IsTrue(PlayerCleaningController.IsValidAim(Vector3.zero,
                Vector3.up * 1.65f, Vector3.down));
        }

        [Test]
        public void 멀리_떨어진_원점과_정규화하지_않은_방향을_거부한다()
        {
            Assert.IsFalse(PlayerCleaningController.IsValidAim(Vector3.zero, Vector3.right * 3f, Vector3.down));
            Assert.IsFalse(PlayerCleaningController.IsValidAim(Vector3.zero, Vector3.up, Vector3.zero));
            Assert.IsFalse(PlayerCleaningController.IsValidAim(Vector3.zero, Vector3.up, Vector3.forward * 2f));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void 비정상_좌표를_거부한다(float invalid)
        {
            var value = new Vector3(invalid, 0f, 0f);
            Assert.IsFalse(PlayerCleaningController.IsValidAim(value, Vector3.up, Vector3.down));
            Assert.IsFalse(PlayerCleaningController.IsValidAim(Vector3.zero, value, Vector3.down));
            Assert.IsFalse(PlayerCleaningController.IsValidAim(Vector3.zero, Vector3.up, value));
        }
    }
}
