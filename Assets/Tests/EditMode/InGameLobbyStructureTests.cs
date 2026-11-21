using System.Collections.Generic;
using System.Linq;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 카지노로 바꾼 인게임 로비(stage-system.md §1.1)의 배치를 검사한다 — 스케일 1, 재질, 스폰 안전, 단말기 도달, 중앙 통로.
    /// </summary>
    public sealed class InGameLobbyStructureTests
    {
        private const float PlayerRadius = 0.3f;
        private const float PlayerHeight = 1.3f;
        private const float EyeHeight = 1.2f;

        private Scene _scene;
        private bool _openedHere;

        [OneTimeSetUp]
        public void OpenScene()
        {
            const string path = "Assets/Scenes/InGameLobby.unity";
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
        public void 카지노_방과_소품은_스케일_1과_URP_재질을_쓴다()
        {
            foreach (string root in new[] { "--- Environment ---", "--- Lighting ---" })
            {
                Transform category = Roots().Single(x => x.name == root).transform;
                foreach (Transform t in category.GetComponentsInChildren<Transform>(true))
                    Assert.AreEqual(Vector3.one, t.localScale, t.name);
            }
            Transform environment = Roots().Single(x => x.name == "--- Environment ---").transform;
            foreach (MeshRenderer renderer in environment.GetComponentsInChildren<MeshRenderer>(true))
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                Assert.IsNotNull(mesh, renderer.name);
                Assert.AreEqual(mesh.subMeshCount, renderer.sharedMaterials.Length, renderer.name);
                foreach (Material material in renderer.sharedMaterials)
                {
                    Assert.IsNotNull(material, renderer.name);
                    Assert.AreEqual("Universal Render Pipeline/Lit", material.shader.name, renderer.name);
                }
            }
            Bounds room = environment.Find("CasinoRoom").GetComponentsInChildren<Renderer>()
                .Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            Assert.That(room.size.x, Is.InRange(19.9f, 20.1f));
            Assert.That(room.size.z, Is.InRange(13.9f, 14.1f));
        }

        [Test]
        public void 네_시작점은_바닥_위에서_소품과_겹치지_않는다()
        {
            PlayerSpawnRegistry registry = Components<PlayerSpawnRegistry>().Single();
            SerializedProperty points = new SerializedObject(registry).FindProperty("_spawnPoints");
            Assert.AreEqual(4, points.arraySize);
            for (int i = 0; i < points.arraySize; i++)
            {
                var point = (Transform)points.GetArrayElementAtIndex(i).objectReferenceValue;
                Assert.IsTrue(Physics.Raycast(point.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, 0.3f,
                    ~0, QueryTriggerInteraction.Ignore), point.name + " 바닥");
                Assert.AreEqual(0f, hit.point.y, 0.01f, point.name);
                Vector3 bottom = point.position + Vector3.up * (PlayerRadius + 0.05f);
                Vector3 top = point.position + Vector3.up * (PlayerHeight - PlayerRadius);
                Assert.IsFalse(Physics.CheckCapsule(bottom, top, PlayerRadius + 0.05f, ~0, QueryTriggerInteraction.Ignore),
                    point.name + " 겹침");
            }
        }

        [Test]
        public void 입구에서_캐셔_단말기까지_중앙_통로가_열려_있고_E로_조준된다()
        {
            StageLobbyTerminal terminal = Components<StageLobbyTerminal>().Single();
            Assert.IsNotNull(terminal.GetComponent<Collider>());
            Vector3 start = new(0f, PlayerRadius + 0.05f, -4.6f);
            Vector3 end = new(0f, PlayerRadius + 0.05f, terminal.transform.position.z - 1.0f);
            Vector3 up = Vector3.up * (PlayerHeight - 2f * PlayerRadius - 0.05f);
            foreach (float x in new[] { -1.2f, 0f, 1.2f })
            {
                Vector3 offset = new(x, 0f, 0f);
                bool blocked = Physics.CapsuleCast(start + offset, start + offset + up, PlayerRadius, Vector3.forward,
                    out RaycastHit block, end.z - start.z, ~0, QueryTriggerInteraction.Ignore);
                Assert.IsFalse(blocked, blocked ? "통로 막힘 x=" + x + " " + block.collider.name : string.Empty);
            }
            Vector3 eye = new(0f, EyeHeight, end.z);
            Assert.IsTrue(Physics.Raycast(eye, Vector3.forward, out RaycastHit hit, 2.5f, ~0, QueryTriggerInteraction.Ignore));
            Assert.AreEqual(terminal, hit.collider.GetComponentInParent<StageLobbyTerminal>(), "카운터 앞에서 단말기를 조준한다");
        }

        private IEnumerable<GameObject> Roots() => _scene.GetRootGameObjects();

        private IEnumerable<T> Components<T>() where T : Component =>
            Roots().SelectMany(x => x.GetComponentsInChildren<T>(true));
    }
}
