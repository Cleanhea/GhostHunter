using GhostHunter.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 플레이어 캐릭터(두더지) 대기·걷기 전환 규칙을 본다.
    /// </summary>
    public sealed class PlayerCharacterAnimationTests
    {
        private PlayerCharacterAnimationSettings _settings;

        [SetUp]
        public void SetUp() => _settings = ScriptableObject.CreateInstance<PlayerCharacterAnimationSettings>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_settings);

        [Test]
        public void 속도는_수평_변위만_잰다()
        {
            float speed = PlayerCharacterAnimationRules.MeasurePlanarSpeed(
                Vector3.zero, new Vector3(3f, 10f, 4f), 1f, 30f, 0f);

            Assert.AreEqual(5f, speed, 0.0001f, "낙하·점프의 수직 변위가 걷기로 잡히면 안 된다");
        }

        [Test]
        public void 순간이동과_멈춘_시간은_직전_속도를_유지한다()
        {
            Assert.AreEqual(1.5f, PlayerCharacterAnimationRules.MeasurePlanarSpeed(
                Vector3.zero, new Vector3(50f, 0f, 0f), 0.016f, 30f, 1.5f), "스폰·텔레포트 한 프레임이 전력 질주로 보이면 안 된다");
            Assert.AreEqual(1.5f, PlayerCharacterAnimationRules.MeasurePlanarSpeed(
                Vector3.zero, Vector3.one, 0f, 30f, 1.5f));
        }

        [Test]
        public void 평활은_프레임률과_무관하다()
        {
            float once = PlayerCharacterAnimationRules.SmoothSpeed(0f, 5f, 0.1f, 0.02f);
            float half = PlayerCharacterAnimationRules.SmoothSpeed(0f, 5f, 0.1f, 0.01f);
            float twice = PlayerCharacterAnimationRules.SmoothSpeed(half, 5f, 0.1f, 0.01f);

            Assert.AreEqual(once, twice, 0.0001f);
            Assert.AreEqual(5f, PlayerCharacterAnimationRules.SmoothSpeed(0f, 5f, 0f, 0.01f), "평활 시간 0은 즉시 반영");
        }

        [Test]
        public void 이동_임계값을_넘어야_걷는다()
        {
            Assert.IsFalse(PlayerCharacterAnimationRules.IsMoving(_settings.MoveThreshold, _settings));
            Assert.IsTrue(PlayerCharacterAnimationRules.IsMoving(_settings.MoveThreshold + 0.01f, _settings));
        }

        [Test]
        public void 걷기_배속은_이동_속도에_비례하되_범위로_자른다()
        {
            Assert.AreEqual(1f, PlayerCharacterAnimationRules.WalkPlaybackSpeed(_settings.WalkClipSpeed, _settings), 0.0001f);
            Assert.AreEqual(_settings.MaxWalkPlaybackSpeed,
                PlayerCharacterAnimationRules.WalkPlaybackSpeed(7f, _settings), 0.0001f, "달리기 7m/s 는 상한");
            Assert.AreEqual(_settings.MinWalkPlaybackSpeed,
                PlayerCharacterAnimationRules.WalkPlaybackSpeed(0.25f, _settings), 0.0001f);
        }
    }
}
