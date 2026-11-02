using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    public sealed class SceneInputTransitionFlowTests
    {
        private static readonly Type Operations = Type.GetType(
            "GhostHunter.Systems.SceneFlow.SceneTransitionOperations, GhostHunter.Systems", throwOnError: true);
        private Scene _scene;
        private EventSystem _events;
        private AudioListener _activeListener;
        private AudioListener _inactiveListener;

        [SetUp]
        public void SetUp()
        {
            _scene = SceneManager.CreateScene(nameof(SceneInputTransitionFlowTests));
            var root = new GameObject("SceneInputTransitionRoot");
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, _scene);
            _events = root.AddComponent<EventSystem>();
            _activeListener = root.AddComponent<AudioListener>();
            var child = new GameObject("DisabledAudioListener");
            child.transform.SetParent(root.transform, false);
            _inactiveListener = child.AddComponent<AudioListener>();
            _inactiveListener.enabled = false;
            root.SetActive(true);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scene.IsValid() && _scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(_scene);
        }

        [Test]
        public void 전환_실패_복원은_원래_켜져_있던_입력과_음향만_되살린다()
        {
            var suspended = SuspendInput();
            Assert.AreEqual(2, suspended.Count);
            Assert.IsFalse(_events.enabled);
            Assert.IsFalse(_activeListener.enabled);
            Assert.IsFalse(_inactiveListener.enabled);
            RestoreInput(suspended);
            Assert.IsTrue(_events.enabled);
            Assert.IsTrue(_activeListener.enabled);
            Assert.IsFalse(_inactiveListener.enabled);
        }

        [Test]
        public void 실패_복원_전에_파괴된_입력_컴포넌트는_건너뛴다()
        {
            var suspended = SuspendInput();
            Object.DestroyImmediate(_events);
            Assert.DoesNotThrow(() => RestoreInput(suspended));
            Assert.IsTrue(_activeListener.enabled);
            Assert.IsFalse(_inactiveListener.enabled);
        }

        private List<Behaviour> SuspendInput() => (List<Behaviour>)Operations
            .GetMethod("SuspendInput", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { _scene });

        private static void RestoreInput(List<Behaviour> suspended) => Operations
            .GetMethod("RestoreInput", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { suspended });
    }
}
