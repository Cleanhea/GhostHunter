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

        // ───────────── 2층·지붕·리프트·문 (2026-11-16) ─────────────

        private const float UpperFloorY = 2.8f;

        [Test]
        public void 이층_가구와_소품은_이층_바닥이나_받침_위에_놓이고_떠_있지_않다()
        {
            GameObject map = Components<Transform>().Single(x => x.name == "Stage2Map").gameObject;
            var floating = new List<string>();
            int checkedCount = 0;
            foreach (Renderer renderer in map.GetComponentsInChildren<Renderer>())
            {
                Bounds b = renderer.bounds;
                // 2층 바닥에 직접 서는 물체(밑면 2.8m 안팎). 커튼 봉·벽걸이처럼 높이 걸린 것은 대상이 아니다.
                if (b.min.y < UpperFloorY - 0.05f || b.min.y > UpperFloorY + 0.12f || b.size.y < 0.05f)
                    continue;
                checkedCount++;
                Vector3 origin = new(b.center.x, b.min.y + 0.03f, b.center.z);
                bool supported = Physics.RaycastAll(origin, Vector3.down, 0.2f, ~0, QueryTriggerInteraction.Ignore)
                    .Any(hit => !hit.collider.transform.IsChildOf(renderer.transform) && hit.point.y >= UpperFloorY - 0.03f);
                if (!supported)
                    floating.Add($"{renderer.name} {b.min}");
            }
            Assert.Greater(checkedCount, 20, "2층 가구를 찾지 못했다");
            Assert.IsEmpty(floating, "받침 없이 떠 있는 2층 물체:\n" + string.Join("\n", floating));

            // 숨겨진 SH3D 레벨 2의 협탁이 빠져 0.6m 떠 있던 탁상 조명은 협탁 위에 놓인다.
            Renderer lamp = map.GetComponentsInChildren<Renderer>().Single(r => r.name == "263");
            Assert.IsTrue(Physics.Raycast(lamp.bounds.center, Vector3.down, out RaycastHit stand, 0.5f, ~0, QueryTriggerInteraction.Ignore));
            Assert.AreEqual("UpperFloor_Nightstand", stand.collider.name);
            Assert.AreEqual(lamp.bounds.min.y, stand.point.y, 0.03f);
        }

        [Test]
        public void 이층은_바닥_외벽_천장으로_닫히고_계단_구멍과_리프트_입구만_열려_있다()
        {
            Assert.IsTrue(Physics.Raycast(new Vector3(-4.4f, 3.2f, 4.2f), Vector3.up, out RaycastHit ceiling, 4f, ~0, QueryTriggerInteraction.Ignore));
            Assert.AreEqual(5.3f, ceiling.point.y, 0.01f, "2층 천장");
            Assert.IsTrue(Physics.Raycast(new Vector3(-4.4f, 3.2f, 4.2f), Vector3.down, out RaycastHit floor, 1f, ~0, QueryTriggerInteraction.Ignore));
            Assert.AreEqual(UpperFloorY, floor.point.y, 0.01f, "2층 바닥");

            // 2층 네 벽: 리프트 입구(z 0~2)와 창(유리 충돌체) 외에는 벽에 막힌다.
            Vector3 center = new(-4.4f, 3.6f, 4.2f);
            for (float z = 0.4f; z < 8.3f; z += 0.5f)
            {
                bool liftBay = z > 0f && z < 2f;
                Vector3 target = new(0.5f, 3.6f, z);
                bool blocked = Physics.Linecast(new Vector3(-1f, 3.6f, z), target, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
                    && hit.point.x < 0.3f;
                Assert.AreEqual(!liftBay, blocked, $"동쪽 벽 z={z}");
            }
            foreach (Vector3 dir in new[] { Vector3.left, Vector3.forward, Vector3.back })
                Assert.IsTrue(Physics.Raycast(center, dir, 12f, ~0, QueryTriggerInteraction.Ignore), "2층 벽 " + dir);

            // 1층 복도는 더 이상 하늘로 뚫려 있지 않다(2층 바닥·1층 지붕 밑면이 천장).
            foreach (Vector3 hall in new[] { new Vector3(-4.5f, 1.5f, 2f), new Vector3(-1.5f, 1.5f, 12f), new Vector3(-7.5f, 1.5f, 10.5f), new Vector3(-12f, 1.5f, 9.8f) })
            {
                Assert.IsTrue(Physics.Raycast(hall, Vector3.up, out RaycastHit top, 3f, ~0, QueryTriggerInteraction.Ignore), "복도 천장 " + hall);
                Assert.Less(top.point.y, 2.52f, "복도 천장 높이 " + hall);
            }

            // 계단 위는 비어 있어 2층으로 올라간다.
            Assert.IsFalse(Physics.Raycast(new Vector3(-7.35f, 2.95f, 4.0f), Vector3.up, 2f, ~0, QueryTriggerInteraction.Ignore), "계단 위 머리 공간");
        }

        [Test]
        public void 일층_물체는_지붕을_뚫지_않는다()
        {
            GameObject map = Components<Transform>().Single(x => x.name == "Stage2Map").gameObject;
            var upperRoom = new Bounds(new Vector3(-4.41f, 4f, 4.21f), new Vector3(9.36f, 6f, 8.96f));
            var pierce = map.GetComponentsInChildren<Renderer>()
                .Where(r => r.bounds.max.y > UpperFloorY + 0.001f && r.bounds.min.y < 2.5f)
                .Where(r => !upperRoom.Contains(new Vector3(r.bounds.center.x, 4f, r.bounds.center.z)))
                .Select(r => $"{r.name} max.y={r.bounds.max.y:F3}").ToList();
            Assert.IsEmpty(pierce, "1층 지붕(2.8m) 위로 나온 물체:\n" + string.Join("\n", pierce));
        }

        [Test]
        public void 리프트는_이층_리프트_입구_앞에서_이층_바닥_높이까지_오르내린다()
        {
            GhostHunter.Gameplay.Lift.LiftController lift = Components<GhostHunter.Gameplay.Lift.LiftController>().Single();
            Transform deck = lift.GetComponentsInChildren<BoxCollider>(true).Single(c => c.name == "Deck").transform;
            BoxCollider deckBox = deck.GetComponent<BoxCollider>();
            Bounds deckBounds = deckBox.bounds;
            Assert.That(deckBounds.min.x, Is.InRange(0.3f, 0.45f), "발판 건물 쪽 가장자리가 외벽(0.27m) 바로 앞");
            Assert.That(deckBounds.center.z, Is.InRange(0.6f, 1.4f), "발판이 리프트 입구(z 0~2) 앞");
            Assert.AreEqual(0.24f, deckBounds.max.y, 0.01f, "맨 아래 발판 윗면");
            float topDeck = deckBounds.max.y + lift.Settings.FloorHeight * (lift.Settings.StopCount - 1);
            Assert.AreEqual(UpperFloorY, topDeck, 0.01f, "맨 위 발판 윗면 = 2층 바닥");

            // 발판이 지나가는 길과 입구 안쪽이 비어 있다(리프트 자신 제외).
            var path = new Bounds(new Vector3(deckBounds.center.x, (0.3f + topDeck) * 0.5f + 0.1f, deckBounds.center.z),
                new Vector3(deckBounds.size.x - 0.1f, topDeck - 0.3f, deckBounds.size.z - 0.1f));
            Collider[] hits = Physics.OverlapBox(path.center, path.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)
                .Where(c => !c.transform.IsChildOf(lift.transform)).ToArray();
            Assert.IsEmpty(hits, "리프트 이동 경로를 막는 것: " + string.Join(", ", hits.Select(h => h.name)));
            Assert.IsFalse(Physics.CheckBox(new Vector3(-0.2f, 3.95f, 1.15f), new Vector3(0.4f, 1.05f, 0.75f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore),
                "2층 리프트 입구 안쪽");
        }

        [Test]
        public void 원본_여닫이문은_모두_상호작용_문이고_열린_문틀은_막히지_않는다()
        {
            string[] expected = { "Stage2EntranceDoor", "Stage2Door_Storage", "Stage2Door_Bedroom", "Stage2Door_Bath1", "Stage2Door_Bath2",
                "Stage2Door_MasterBedroom", "RitualDoor_0", "RitualDoor_1", "RitualDoor_2" };
            DoorInteractable[] doors = Components<DoorInteractable>().ToArray();
            CollectionAssert.AreEquivalent(expected, doors.Select(d => d.name));
            Transform ritualRoom = Components<Transform>().Single(t => t.name == "Stage2RitualRoom");
            Assert.AreEqual(3, ritualRoom.GetComponentsInChildren<DoorInteractable>(true).Length, "의식 방 문 3개(양개문 + 식당 남쪽 문)");

            foreach (DoorInteractable door in doors)
            {
                BoxCollider leaf = door.GetComponentInChildren<BoxCollider>(true);
                Assert.IsNotNull(leaf, door.name);
                var serialized = new SerializedObject(door);
                float closedYaw = serialized.FindProperty("_closedYaw").floatValue;
                float openYaw = serialized.FindProperty("_openYaw").floatValue;
                Assert.IsTrue(serialized.FindProperty("_startsOpen").boolValue, door.name);
                Assert.AreEqual(openYaw, Mathf.DeltaAngle(0f, door.transform.localEulerAngles.y), 0.5f, door.name + " 씬 각도 = 열린 상태");
                foreach (Transform t in door.GetComponentsInChildren<Transform>(true))
                    Assert.AreEqual(Vector3.one, t.localScale, t.name);

                // 닫힌 문짝 가운데(문틀 한가운데)를 사람이 지나갈 수 있다 — 정적 문짝이 남아 있으면 막힌다.
                Vector3 local = leaf.center;
                Vector3 closedMid = door.transform.position + door.ClosedWorldRotation * new Vector3(local.x, 0f, 0f);
                Collider[] blocking = Physics.OverlapCapsule(closedMid + Vector3.up * 0.4f, closedMid + Vector3.up * 1.6f, 0.22f, ~0,
                        QueryTriggerInteraction.Ignore)
                    .Where(c => c.GetComponentInParent<DoorInteractable>() == null && c.attachedRigidbody == null).ToArray();
                Assert.IsEmpty(blocking, door.name + " 문틀 통로: " + string.Join(", ", blocking.Select(c => c.name)));

                // 열린 문짝(경첩 쪽 0.15m 제외)은 벽·가구와 겹치지 않는다.
                Vector3 half = leaf.size * 0.5f;
                Vector3 shrink = new(half.x - 0.1f, half.y - 0.05f, 0.01f);
                Vector3 c0 = leaf.transform.TransformPoint(leaf.center + new Vector3(0.05f, 0f, 0f));
                Collider[] overlap = Physics.OverlapBox(c0, shrink, leaf.transform.rotation, ~0, QueryTriggerInteraction.Ignore)
                    .Where(c => c.GetComponentInParent<DoorInteractable>() != door).ToArray();
                Assert.IsEmpty(overlap, door.name + " 열린 문짝과 겹침: " + string.Join(", ", overlap.Select(c => c.name)));
                Assert.AreNotEqual(closedYaw, openYaw, door.name);
            }

            // 문으로 바꾼 원본 정적 문짝은 꺼져 있다.
            GameObject map = Components<Transform>().Single(x => x.name == "Stage2Map").gameObject;
            // 방문 문짝은 바닥(0m)부터 약 1.96m다. 붙박이장 문(0.15~2.48m)·샤워 부스 문은 대상이 아니다.
            var staticLeaves = map.GetComponentsInChildren<MeshCollider>()
                .Where(c => c.name.Contains("opening_on_hinge_1_door_") && c.bounds.min.y < 0.05f
                    && c.bounds.size.y > 1.8f && c.bounds.max.y < 2.1f)
                .Select(c => c.name).ToList();
            Assert.IsEmpty(staticLeaves, "남은 정적 문짝: " + string.Join(", ", staticLeaves));
        }

        [Test]
        public void 이층_방에_조명이_있다()
        {
            var lights = Components<GhostHunter.Gameplay.Lighting.StageRoomLight>().Where(l => l.Floor == 1).ToArray();
            Assert.AreEqual(1, lights.Length);
            Assert.That(lights[0].transform.position.y, Is.InRange(UpperFloorY + 1.8f, 5.3f));
        }

        private IEnumerable<T> Components<T>() where T : Component =>
            _scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    }
}
