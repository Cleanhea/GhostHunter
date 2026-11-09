using System.Collections.Generic;
using System.Linq;
using GhostHunter.Gameplay.Cleaning;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Map;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Revival;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>Stage2의 맵 단위·충돌·스폰·필수 서비스·씬 네트워크 식별자를 검사한다.</summary>
    public sealed class Stage2StructureTests
    {
        private Scene _scene;
        private bool _openedHere;

        [OneTimeSetUp]
        public void OpenScene()
        {
            const string path = "Assets/Scenes/Stage2.unity";
            _scene = SceneManager.GetSceneByPath(path);
            if (!_scene.isLoaded)
            {
                _scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                _openedHere = true;
            }
            Physics.SyncTransforms();
        }

        [OneTimeTearDown]
        public void CloseScene()
        {
            if (_openedHere && _scene.isLoaded)
                EditorSceneManager.CloseScene(_scene, true);
        }

        [Test]
        public void 원본_맵은_미터_단위와_스케일_1과_충돌체를_유지한다()
        {
            GameObject map = Components<Transform>().Single(x => x.name == "Stage2Map").gameObject;
            Bounds bounds = map.GetComponentsInChildren<Renderer>()[0].bounds;
            foreach (Renderer renderer in map.GetComponentsInChildren<Renderer>())
                bounds.Encapsulate(renderer.bounds);
            Assert.That(bounds.size.x, Is.InRange(16f, 17f));
            Assert.That(bounds.size.z, Is.InRange(28f, 30f));
            foreach (Transform transform in map.GetComponentsInChildren<Transform>(true))
                Assert.AreEqual(Vector3.one, transform.localScale, transform.name);
            foreach (MeshFilter mesh in map.GetComponentsInChildren<MeshFilter>())
            {
                if (!AssetDatabase.GetAssetPath(mesh.sharedMesh).EndsWith(".obj"))
                    continue;
                Assert.IsTrue(mesh.TryGetComponent(out MeshCollider collider), mesh.name);
                Assert.AreEqual(mesh.sharedMesh, collider.sharedMesh, mesh.name);
            }
            foreach (Renderer renderer in map.GetComponentsInChildren<Renderer>())
            foreach (Material material in renderer.sharedMaterials)
                Assert.AreEqual("Universal Render Pipeline/Lit", material.shader.name, renderer.name);
        }

        [Test]
        public void 네_시작점은_드릴카_안의_바닥에서_벽과_겹치지_않는다()
        {
            PlayerSpawnRegistry registry = Components<PlayerSpawnRegistry>().Single();
            SerializedProperty points = new SerializedObject(registry).FindProperty("_spawnPoints");
            Assert.AreEqual(4, points.arraySize);
            DrillCarSafeZone safe = Components<DrillCarSafeZone>().Single();
            for (int i = 0; i < points.arraySize; i++)
            {
                var point = (Transform)points.GetArrayElementAtIndex(i).objectReferenceValue;
                Assert.IsTrue(new Bounds(Vector3.zero, safe.Size).Contains(safe.transform.InverseTransformPoint(point.position)), point.name);
                Assert.IsTrue(Physics.Raycast(point.position + Vector3.up * 0.1f, Vector3.down,
                    out RaycastHit hit, 0.3f, ~0, QueryTriggerInteraction.Ignore), point.name + " 바닥");
                Assert.That(hit.point.y, Is.InRange(point.position.y - 0.2f, point.position.y));
                Assert.IsFalse(Physics.CheckCapsule(point.position + Vector3.up * 0.35f,
                    point.position + Vector3.up * 1.1f, 0.3f, ~0, QueryTriggerInteraction.Ignore), point.name + " 벽");
            }
        }

        [Test]
        public void 귀신과_청소와_부활과_배터리에_실제_씬_참조가_있다()
        {
            GhostPrototypeSpawner ghost = Components<GhostPrototypeSpawner>().Single();
            var navigation = (Transform)new SerializedObject(ghost).FindProperty("_navigationRoot").objectReferenceValue;
            Assert.AreEqual("Stage2Navigation", navigation.name);
            CleaningController cleaning = Components<CleaningController>().Single();
            SerializedProperty points = new SerializedObject(cleaning).FindProperty("_points");
            Assert.GreaterOrEqual(points.arraySize, cleaning.Stains.Length);
            for (int i = 0; i < points.arraySize; i++)
                Assert.IsNotNull(points.GetArrayElementAtIndex(i).objectReferenceValue);
            RevivalRitual ritual = Components<RevivalRitual>().Single();
            Assert.Greater(ritual.EligibleRoomCount, 0);
            Assert.IsNotNull(ritual.GetComponentInParent<NetworkObject>());
            Assert.AreEqual(1, Components<BatteryPack>().Count());
        }

        [Test]
        public void 원본_이동_가구는_프리팹과_정확한_배치_지점과_유효한_계획을_가진다()
        {
            FurnitureSpawnController controller = Components<FurnitureSpawnController>().Single();
            Assert.AreEqual(18, controller.Items.Length);
            Assert.AreEqual(18, controller.Settings.MinimumTargetTypes);
            Assert.AreEqual(18, controller.Settings.MaximumTargetTypes);
            Assert.IsTrue(controller.Settings.Pools.All(rule => rule.IsWorkTarget));
            foreach (RandomFurnitureItem item in controller.Items)
            {
                Assert.IsTrue(PrefabUtility.IsPartOfPrefabInstance(item), item.name);
                Assert.IsNotNull(item.GetComponent<GhostHunter.Gameplay.Furniture.FurnitureGrabTarget>());
                Assert.IsNotNull(item.GetComponent<GhostHunter.Gameplay.Furniture.FurnitureNetworkPhysics>().Definition);
                foreach (Transform transform in item.GetComponentsInChildren<Transform>(true))
                    Assert.AreEqual(Vector3.one, transform.localScale, transform.name);
            }
            for (int seed = 0; seed < 20; seed++)
                Assert.IsTrue(controller.TryBuildPlan(seed, out _, out _, out string error), error);
        }

        [Test]
        public void 씬_네트워크_해시는_새_씬의_고유한_값이고_스크립트가_누락되지_않는다()
        {
            var hashes = new HashSet<long>();
            foreach (NetworkObject network in Components<NetworkObject>())
            {
                var serialized = new SerializedObject(network);
                long hash = serialized.FindProperty("GlobalObjectIdHash").longValue;
                Assert.AreNotEqual(0, hash, network.name);
                Assert.IsTrue(hashes.Add(hash), network.name + " 중복 해시");
                Assert.IsTrue(serialized.FindProperty("m_InScenePlaced").boolValue, network.name);
            }
            Assert.Greater(hashes.Count, 12);
            foreach (Transform transform in Components<Transform>())
                Assert.AreEqual(0, GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), transform.name);
        }

        private IEnumerable<T> Components<T>() where T : Component =>
            _scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    }
}
