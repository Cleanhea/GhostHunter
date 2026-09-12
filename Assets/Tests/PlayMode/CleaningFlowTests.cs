using System.Collections;
using System.Reflection;
using GhostHunter.Gameplay.Cleaning;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>서버 얼룩 제거·초기화·가림 판정을 실제 NGO Host 세션에서 검증한다.</summary>
    public sealed class CleaningFlowTests : NetworkFurnitureFixture
    {
        private GameObject _stainObject;
        private GameObject _controllerObject;
        private GameObject _wall;
        private CleaningSettings _cleaningSettings;

        [UnityTest]
        public IEnumerator 청소는_중복되지_않고_초기화_이전의_요청을_거부한다()
        {
            CleaningStain stain = SpawnStain();
            Assert.IsFalse(stain.IsDirty);
            stain.ServerReset(new Vector3(10f, 0f, 3f), 60f, 1, true);
            Assert.IsTrue(stain.IsDirty);
            Assert.IsTrue(stain.HitCollider.enabled);
            Assert.IsTrue(stain.ServerClean(1));
            Assert.IsFalse(stain.ServerClean(1));
            Assert.IsFalse(stain.HitCollider.enabled);
            Assert.IsFalse(stain.IsDirty);
            stain.ServerReset(new Vector3(12f, 0f, 3f), 90f, 2, true);
            Assert.IsTrue(stain.IsDirty);
            Assert.IsFalse(stain.ServerClean(1));
            Assert.AreEqual(new Vector3(12f, 0f, 3f), stain.transform.position);
            Assert.IsTrue(stain.ServerClean(2));
            yield return null;
        }

        [UnityTest]
        public IEnumerator 벽_너머와_거리_밖의_얼룩은_조준되지_않는다()
        {
            CleaningStain stain = SpawnStain();
            stain.ServerReset(new Vector3(10f, 0f, 3f), 0f, 1, true);
            _controllerObject = new GameObject("CleaningTestController");
            _controllerObject.SetActive(false);
            _controllerObject.AddComponent<NetworkObject>();
            var controller = _controllerObject.AddComponent<CleaningController>();
            Set(controller, "_settings", _cleaningSettings);
            Set(controller, "_stains", new[] { stain });
            _controllerObject.SetActive(true);
            Vector3 origin = new(10f, 0f, 0f);
            Physics.SyncTransforms();
            Assert.IsTrue(controller.TryRaycast(origin, Vector3.forward, 4f, out CleaningStain target));
            Assert.AreSame(stain, target);
            Assert.IsFalse(controller.TryRaycast(origin, Vector3.forward, 1f, out _));
            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _wall.transform.position = origin + Vector3.forward;
            Physics.SyncTransforms();
            Assert.IsFalse(controller.TryRaycast(origin, Vector3.forward, 4f, out _));
            // 요청자 충돌체만 제외할 수 있어야 하며, 일반 벽은 계속 가려야 한다.
            Assert.IsTrue(controller.TryRaycast(origin, Vector3.forward, 4f, out _, _wall.transform));
            Assert.IsTrue(controller.IsObstructed(origin, origin + Vector3.forward * 2f, null));
            Assert.IsFalse(controller.IsObstructed(origin, origin + Vector3.forward * 2f, _wall.transform));
            _wall.GetComponent<Collider>().isTrigger = true;
            Assert.IsTrue(controller.TryRaycast(origin, Vector3.forward, 4f, out _));
            yield return null;
        }

        [UnityTest]
        public IEnumerator 스폰하지_않은_얼룩은_서버_API로_변경할_수_없다()
        {
            CleaningStain stain = SpawnStain();
            stain.NetworkObject.Despawn(false);
            stain.ServerReset(Vector3.one, 0f, 1, true);
            Assert.IsFalse(stain.IsDirty);
            Assert.IsFalse(stain.ServerClean(1));
            yield return null;
        }

        [TearDown]
        public void DestroyCleaningObjects()
        {
            if (_stainObject != null)
                Object.DestroyImmediate(_stainObject);
            if (_controllerObject != null)
                Object.DestroyImmediate(_controllerObject);
            if (_wall != null)
                Object.DestroyImmediate(_wall);
            if (_cleaningSettings != null)
                Object.DestroyImmediate(_cleaningSettings);
        }

        private CleaningStain SpawnStain()
        {
            _cleaningSettings = ScriptableObject.CreateInstance<CleaningSettings>();
            _stainObject = new GameObject("CleaningTestStain");
            _stainObject.SetActive(false);
            var networkObject = _stainObject.AddComponent<NetworkObject>();
            typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(networkObject, 0x5F200001u);
            var collider = _stainObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            var renderer = _stainObject.AddComponent<MeshRenderer>();
            var stain = _stainObject.AddComponent<CleaningStain>();
            Set(stain, "_settings", _cleaningSettings);
            Set(stain, "_hitCollider", collider);
            Set(stain, "_visual", renderer);
            _stainObject.SetActive(true);
            networkObject.Spawn();
            return stain;
        }

        private static void Set(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
