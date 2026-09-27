#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using GhostHunter.Gameplay.Player;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// <see cref="PlayerCharacterAnimator"/> 가 루트 변위만 보고 대기·걷기 파라미터를 쓰는지 본다.
    /// 실제 컨트롤러 에셋은 설치 도구가 만들므로, 여기서는 같은 파라미터 이름의 메모리 컨트롤러로 대신한다.
    /// </summary>
    public sealed class PlayerCharacterAnimatorTests
    {
        private GameObject _root;
        private PlayerCharacterAnimationSettings _settings;
        private AnimatorController _controller;
        private Animator _animator;
        private PlayerCharacterAnimator _driver;

        [SetUp]
        public void SetUp()
        {
            // 프레임 간격이 흔들리면 "이번 프레임 이동 / 다음 프레임 deltaTime" 비율이 흔들린다. 60fps 로 고정한다.
            Time.captureFramerate = 60;
            _settings = ScriptableObject.CreateInstance<PlayerCharacterAnimationSettings>();
            _controller = new AnimatorController();
            _controller.AddLayer("Base Layer");
            _controller.AddParameter(PlayerCharacterAnimator.IsMovingParameter, AnimatorControllerParameterType.Bool);
            _controller.AddParameter(PlayerCharacterAnimator.WalkSpeedParameter, AnimatorControllerParameterType.Float);
            _controller.layers[0].stateMachine.AddState("Idle");

            _root = new GameObject("CharacterAnimatorTestPlayer");
            _root.SetActive(false);
            var model = new GameObject("Character");
            model.transform.SetParent(_root.transform, false);
            _animator = model.AddComponent<Animator>();
            _animator.runtimeAnimatorController = _controller;
            _driver = _root.AddComponent<PlayerCharacterAnimator>();
            Set(_driver, "_animator", _animator);
            Set(_driver, "_settings", _settings);
            _root.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_controller);
            Object.DestroyImmediate(_settings);
        }

        [UnityTest]
        public IEnumerator 움직이면_걷기_멈추면_대기()
        {
            yield return null;
            Assert.IsFalse(_animator.GetBool(PlayerCharacterAnimator.IsMovingParameter));

            for (int i = 0; i < 30; i++)
            {
                _root.transform.position += Vector3.forward * (5f * Time.deltaTime);
                yield return null;
            }

            Assert.IsTrue(_animator.GetBool(PlayerCharacterAnimator.IsMovingParameter));
            Assert.Greater(_driver.PlanarSpeed, 3f);
            Assert.AreEqual(_settings.MaxWalkPlaybackSpeed,
                _animator.GetFloat(PlayerCharacterAnimator.WalkSpeedParameter), 0.0001f, "5m/s 는 배속 상한");

            for (int i = 0; i < 60; i++)
                yield return null;

            Assert.IsFalse(_animator.GetBool(PlayerCharacterAnimator.IsMovingParameter));
        }

        [UnityTest]
        public IEnumerator 순간이동은_걷기로_보지_않는다()
        {
            yield return null;
            _root.transform.position += new Vector3(40f, 0f, 0f);
            yield return null;
            yield return null;

            Assert.IsFalse(_animator.GetBool(PlayerCharacterAnimator.IsMovingParameter));
            Assert.Less(_driver.PlanarSpeed, _settings.MoveThreshold);
        }

        private static void Set(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
        }
    }
}
#endif
