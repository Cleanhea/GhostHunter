using System.Collections.Generic;
using System.Linq;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Lighting;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// Stage1 집의 마감(벽이 천장까지 닿는다·방마다 천장이 있다)과 천장등 배선을 지킨다.
    /// 씬 파일을 직접 고치는 작업(ADR-0020)에서 벽 높이·천장 한 칸이 빠지면 플레이 전에는 알 수 없다.
    /// </summary>
    public sealed class Stage1StructureTests
    {
        private const string ScenePath = "Assets/Scenes/Stage1.unity";
        private const string HouseName = "House_Prototype_PlanB";
        private const float Tolerance = 0.01f;

        // 층 바닥 높이. 층고 3m, 위층 슬래브 아랫면(= 천장)은 바닥 + 2.8m.
        private static readonly Dictionary<string, float> FloorBase = new()
        {
            { "Floor_01", 0f }, { "Floor_02", 3f }, { "Floor_03_Attic", 6f },
        };

        private const float CeilingHeight = 2.8f;
        private const float OuterWallHeight = 3f;

        private Scene _scene;
        private bool _openedHere;
        private GameObject _house;

        [OneTimeSetUp]
        public void OpenScene()
        {
            _scene = SceneManager.GetSceneByPath(ScenePath);
            if (!_scene.isLoaded)
            {
                _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                _openedHere = true;
            }

            _house = _scene.GetRootGameObjects().FirstOrDefault(x => x.name == HouseName);
            Physics.SyncTransforms();
        }

        [OneTimeTearDown]
        public void CloseScene()
        {
            if (_openedHere && _scene.isLoaded)
                EditorSceneManager.CloseScene(_scene, true);
        }

        [Test]
        public void 집_루트가_있다()
        {
            Assert.IsNotNull(_house, $"{ScenePath} 에 {HouseName} 이 없습니다.");
        }

        [Test]
        public void 벽은_천장까지_닿고_외벽은_슬래브_끝을_덮는다()
        {
            Assume.That(_house, Is.Not.Null);
            var shortWalls = new List<string>();
            int checkedCount = 0;

            foreach (BoxCollider box in _house.GetComponentsInChildren<BoxCollider>(true))
            {
                string name = box.gameObject.name;
                bool outer = name.StartsWith("Outer_");
                if (!outer && !name.StartsWith("Wall_"))
                    continue;
                if (!TryFloorBase(box.transform, out float floorBase))
                    continue;

                checkedCount++;
                float top = box.transform.TransformPoint(box.center + Vector3.up * box.size.y * 0.5f).y;
                float expected = floorBase + (outer ? OuterWallHeight : CeilingHeight);
                if (Mathf.Abs(top - expected) > Tolerance)
                    shortWalls.Add($"{Path(box.transform)} 윗면 {top:0.###} (기대 {expected:0.###})");
            }

            Assert.Greater(checkedCount, 100, "벽을 거의 찾지 못했습니다 — 이름 규칙이 바뀌었는지 확인하세요.");
            CollectionAssert.IsEmpty(shortWalls, "천장에 닿지 않는 벽:\n" + string.Join("\n", shortWalls));
        }

        [Test]
        public void 방마다_천장_슬래브가_있다()
        {
            Assume.That(_house, Is.Not.Null);
            var open = new List<string>();
            int rooms = 0;

            foreach (Transform room in RoomAnchors())
            {
                rooms++;
                TryFloorBase(room, out float floorBase);
                Vector3 origin = room.position + Vector3.up * 1.2f;
                bool hit = Physics.Raycast(origin, Vector3.up, out RaycastHit ceiling, 3f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (!hit || Mathf.Abs(ceiling.point.y - (floorBase + CeilingHeight)) > 0.05f)
                    open.Add($"{Path(room.parent)} ({(hit ? ceiling.collider.name + " @" + ceiling.point.y.ToString("0.##") : "하늘")})");
            }

            Assert.AreEqual(22, rooms, "RoomDimensions 개수가 바뀌었습니다(방 21 + 현관).");
            CollectionAssert.IsEmpty(open, "천장이 없는 방:\n" + string.Join("\n", open));
        }

        [Test]
        public void 방마다_천장등이_있다()
        {
            Assume.That(_house, Is.Not.Null);
            Light[] lights = _house.GetComponentsInChildren<Light>(true);
            var dark = new List<string>();

            foreach (Transform room in RoomAnchors())
            {
                var box = room.GetComponent<BoxCollider>();
                Vector3 half = Vector3.Scale(box.size, new Vector3(0.5f, 0f, 0.5f));
                bool lit = lights.Any(light =>
                {
                    Vector3 local = light.transform.position - room.position;
                    return light.type == LightType.Point
                        && Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z
                        && local.y > 0f && local.y < CeilingHeight;
                });
                if (!lit)
                    dark.Add(Path(room.parent));
            }

            CollectionAssert.IsEmpty(dark, "천장등이 없는 방:\n" + string.Join("\n", dark));
        }

        [Test]
        public void 방_출입구마다_씬_배치_문이_있다()
        {
            Assume.That(_house, Is.Not.Null);
            NetworkObject[] doors = _house.GetComponentsInChildren<NetworkObject>(true)
                .Where(x => x.name.EndsWith("_Door_1.0m"))
                .ToArray();
            var missing = new List<string>();
            int doorways = 0;

            foreach (Transform header in _house.GetComponentsInChildren<Transform>(true))
            {
                // 현관 대문(외벽)은 문을 달지 않는다 — 방 출입구만 본다.
                if (!header.name.StartsWith("DoorFrame_") || !header.name.EndsWith("_Header")
                    || header.name == "DoorFrame_Front_Header")
                    continue;

                doorways++;
                // 경첩은 개구부 중심에서 벽을 따라 0.5m, 같은 층 바닥.
                bool hasDoor = doors.Any(door =>
                {
                    Vector3 d = door.transform.position - header.position;
                    return Mathf.Abs(new Vector2(d.x, d.z).magnitude - 0.5f) < 0.05f
                        && Mathf.Abs(door.transform.position.y - header.parent.position.y) < 0.01f;
                });
                if (!hasDoor)
                    missing.Add(Path(header.parent));
            }

            Assert.AreEqual(23, doorways, "방 출입구 개수가 바뀌었습니다(방 21 + 현관 2).");
            Assert.AreEqual(doorways, doors.Length, "출입구 수와 문 수가 다릅니다.");
            CollectionAssert.IsEmpty(missing, "문이 없는 출입구:\n" + string.Join("\n", missing));
            foreach (NetworkObject door in doors)
                Assert.AreNotEqual(0u, door.PrefabIdHash, $"{door.name} 의 GlobalObjectIdHash 가 0 입니다.");
            Assert.AreEqual(doors.Length, doors.Select(x => x.PrefabIdHash).Distinct().Count(), "문 해시가 중복됩니다.");
        }

        [Test]
        public void 천장등은_전부_조명_컨트롤러와_귀신_연출에_배선되어_있다()
        {
            Assume.That(_house, Is.Not.Null);
            StageLightingController controller = _scene.GetRootGameObjects()
                .Select(x => x.GetComponentInChildren<StageLightingController>(true))
                .FirstOrDefault(x => x != null);
            if (controller == null)
                Assert.Ignore("batchmode 에서는 프로젝트 스크립트가 바인딩되지 않아 StageLightingController 를 " +
                              "찾을 수 없습니다. 에디터 Test Runner 로 확인하세요.");

            var serialized = new SerializedObject(controller);
            Assert.IsNotNull(serialized.FindProperty("_settings").objectReferenceValue, "_settings 가 비어 있습니다.");
            Assert.IsNotNull(serialized.FindProperty("_sun").objectReferenceValue, "_sun 이 비어 있습니다.");

            StageRoomLight[] inScene = _house.GetComponentsInChildren<StageRoomLight>(true);
            Assert.Greater(inScene.Length, 0);
            CollectionAssert.AreEquivalent(inScene, controller.Lights, "컨트롤러 _lights 와 씬의 천장등이 다릅니다.");
            foreach (StageRoomLight light in inScene)
                Assert.IsNotNull(light.GetComponent<GhostAmbientLight>(), $"{light.name} 에 GhostAmbientLight 가 없습니다.");

            var installer = _scene.GetRootGameObjects()
                .SelectMany(x => x.GetComponentsInChildren<MonoBehaviour>(true))
                .FirstOrDefault(x => x != null && x.GetType().Name == "GameInstaller");
            Assert.IsNotNull(installer, "GameInstaller 가 없습니다.");
            Assert.AreSame(controller, new SerializedObject(installer).FindProperty("_lighting").objectReferenceValue,
                "GameInstaller._lighting 이 조명 컨트롤러를 가리키지 않습니다.");
        }

        [Test]
        public void 집_구조물의_스케일은_모두_1이다()
        {
            Assume.That(_house, Is.Not.Null);
            var scaled = _house.GetComponentsInChildren<Transform>(true)
                .Where(t => !PrefabUtility.IsPartOfPrefabInstance(t) && t.localScale != Vector3.one)
                .Select(Path)
                .ToList();

            CollectionAssert.IsEmpty(scaled, "스케일이 1이 아닌 오브젝트:\n" + string.Join("\n", scaled));
        }

        private IEnumerable<Transform> RoomAnchors()
        {
            return _house.GetComponentsInChildren<Transform>(true).Where(t => t.name == "RoomDimensions");
        }

        private static bool TryFloorBase(Transform t, out float floorBase)
        {
            for (Transform p = t; p != null; p = p.parent)
                if (FloorBase.TryGetValue(p.name, out floorBase))
                    return true;

            floorBase = 0f;
            return false;
        }

        private static string Path(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent)
                path = p.name + "/" + path;
            return path;
        }
    }
}
