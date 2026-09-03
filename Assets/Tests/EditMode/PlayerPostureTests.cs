using System.Reflection;
using GhostHunter.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 자세별 캡슐 높이·카메라 높이·이동 속도 규칙(<see cref="PlayerPosture"/>)을 검증한다.
    /// 우선순위: 엎드리기 &gt; 웅크리기 &gt; 달리기 &gt; 걷기.
    /// </summary>
    public sealed class PlayerPostureTests
    {
        private PlayerMoveSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<PlayerMoveSettings>();
            // 값이 확실히 구분되도록 리플렉션으로 지정한다(기본값은 서로 가까울 수 있다).
            Set("_moveSpeed", 5f);
            Set("_crouchMoveSpeed", 3.5f);
            Set("_proneMoveSpeed", 1.4f);
            Set("_sprintMultiplier", 1.4f);
            Set("_standingHeight", 1.8f);
            Set("_crouchHeight", 1.2f);
            Set("_proneHeight", 0.5f);
            Set("_standingCameraHeight", 1.65f);
            Set("_crouchCameraHeight", 1.05f);
            Set("_proneCameraHeight", 0.35f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_settings);
            _settings = null;
        }

        private void Set(string field, float value)
        {
            FieldInfo info = typeof(PlayerMoveSettings).GetField(
                field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, $"{field} 를 찾지 못했습니다.");
            info.SetValue(_settings, value);
        }

        [Test]
        public void 엎드리기가_웅크리기를_덮는다()
        {
            Assert.AreEqual(PlayerStance.Prone, PlayerPosture.Resolve(prone: true, crouching: true));
            Assert.AreEqual(PlayerStance.Crouching, PlayerPosture.Resolve(prone: false, crouching: true));
            Assert.AreEqual(PlayerStance.Standing, PlayerPosture.Resolve(prone: false, crouching: false));
        }

        [Test]
        public void 자세가_낮아질수록_캡슐과_카메라가_낮아진다()
        {
            Assert.Greater(
                PlayerPosture.CapsuleHeight(PlayerStance.Standing, _settings),
                PlayerPosture.CapsuleHeight(PlayerStance.Crouching, _settings));
            Assert.Greater(
                PlayerPosture.CapsuleHeight(PlayerStance.Crouching, _settings),
                PlayerPosture.CapsuleHeight(PlayerStance.Prone, _settings));

            Assert.Greater(
                PlayerPosture.CameraHeight(PlayerStance.Crouching, _settings),
                PlayerPosture.CameraHeight(PlayerStance.Prone, _settings));
        }

        [Test]
        public void 엎드리면_달리기_입력을_무시하고_가장_느리다()
        {
            float prone = PlayerPosture.MoveSpeed(PlayerStance.Prone, sprintHeld: true, _settings);
            float crouch = PlayerPosture.MoveSpeed(PlayerStance.Crouching, sprintHeld: true, _settings);
            float sprint = PlayerPosture.MoveSpeed(PlayerStance.Standing, sprintHeld: true, _settings);

            Assert.AreEqual(_settings.ProneMoveSpeed, prone);
            Assert.Less(prone, crouch);
            Assert.Less(crouch, sprint);
        }

        [Test]
        public void 서서_달리면_걷기의_배수만큼_빨라진다()
        {
            float walk = PlayerPosture.MoveSpeed(PlayerStance.Standing, sprintHeld: false, _settings);
            float sprint = PlayerPosture.MoveSpeed(PlayerStance.Standing, sprintHeld: true, _settings);

            Assert.AreEqual(_settings.MoveSpeed, walk);
            Assert.AreEqual(_settings.MoveSpeed * _settings.SprintMultiplier, sprint, 1e-4f);
        }
    }
}
