using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Map;
using GhostHunter.Gameplay.Revival;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>의식 방 제외·가구 정리·의식 배치를 실제 서버 생성 흐름으로 검증한다.</summary>
    public sealed class RevivalRoomFlowTests : NetworkFurnitureFixture
    {
        private readonly List<GameObject> _objects = new();
        private FurnitureSpawnSettings _spawnSettings;
        private RevivalRitualSettings _ritualSettings;
        private uint _nextHash = 0x5F510001;
        private UnityEngine.Random.State _randomState;

        [SetUp]
        public void SaveRandomState() => _randomState = UnityEngine.Random.state;

        [UnityTest]
        public IEnumerator 의식_방은_작업_가구를_보존하며_비울_수_있는_방에서_선택한다()
        {
            CreateFloor();
            GameObject house = CreateObject("House");
            CreateRoom(house.transform, "BlockedRoom", new Vector3(0f, 0f, 3f));
            CreateRoom(house.transform, "ClearRoom", new Vector3(20f, 0f, 3f));
            RandomFurnitureItem item = SpawnRandomFurniture();
            FurnitureSpawnController controller = SpawnController(item);

            FurnitureGrabTarget fixedFurniture = SpawnFurniture();
            _objects.Add(fixedFurniture.gameObject);
            Vector3 fixedPosition = new(20f, 1f, 3f);
            fixedFurniture.GetComponent<NetworkTransform>().Teleport(fixedPosition, Quaternion.identity, Vector3.one);
            fixedFurniture.GetComponent<Rigidbody>().position = fixedPosition;

            GameObject ritualObject = CreateObject("Ritual", false);
            NetworkObject networkObject = AddNetworkObject(ritualObject);
            var ritual = ritualObject.AddComponent<RevivalRitual>();
            _ritualSettings = ScriptableObject.CreateInstance<RevivalRitualSettings>();
            SetPrivateField(ritual, "_settings", _ritualSettings);
            SetPrivateField(ritual, "_houseRoot", house.transform);
            SetPrivateField(ritual, "_furniture", controller);
            // 기존 구현이 배치 후보가 유일하게 있는 방을 고르도록 난수를 고정한다.
            for (int seed = 0; seed < 100; seed++)
            {
                UnityEngine.Random.InitState(seed);
                if (UnityEngine.Random.Range(0, 2) != 0)
                    continue;
                UnityEngine.Random.InitState(seed);
                break;
            }
            ritualObject.SetActive(true);
            networkObject.Spawn();

            float deadline = Time.realtimeSinceStartup + 5f;
            while ((!controller.IsReady || !ritual.IsPlaced) && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsTrue(controller.IsReady, "작업 대상 가구 배치가 완료되어야 합니다.");
            Assert.IsTrue(ritual.IsPlaced, "빈 방에 의식을 배치해야 합니다.");
            Assert.AreEqual("ClearRoom", ritual.RoomName);
            Assert.IsTrue(item.IsAssignedWorkTarget, "방을 비워도 작업 대상 수량은 보존해야 합니다.");
            Assert.IsFalse(ritual.ContainsInRoom(item.transform.position));
            Assert.IsTrue(fixedFurniture.GetComponent<FurnitureNetworkPhysics>().IsStowed);
            Assert.IsFalse(fixedFurniture.GetComponent<Collider>().enabled);
            Assert.AreEqual(5, ritual.GetComponentsInChildren<RevivalCandleSlot>().Length);
            Transform circle = ritual.transform.Find("RevivalCircleVisual/MagicCircle");
            Assert.IsNotNull(circle);
            Assert.Greater(circle.position.y, 0.04f, "마법진이 방 바닥 마감 메시 밑에 묻히면 보이지 않는다.");
            foreach (RevivalCandleSlot slot in ritual.GetComponentsInChildren<RevivalCandleSlot>())
                Assert.GreaterOrEqual(slot.transform.position.y, 0.04f, "촛대 받침도 바닥 마감 위에 서야 한다.");
        }

        [UnityTest]
        public IEnumerator 제외한_방을_되살리는_부분_배치를_하지_않는다()
        {
            CreateFloor();
            RandomFurnitureItem item = SpawnRandomFurniture();
            FurnitureSpawnController controller = SpawnController(item);
            controller.ServerExcludeArea(position => Mathf.Abs(position.x) < 3f);
            LogAssert.Expect(LogType.Error, new Regex(@"\[FurnitureSpawnController\] 배치 실패"));
            for (int frame = 0; frame < 6; frame++)
                yield return null;
            Assert.IsFalse(controller.IsReady);
            Assert.IsFalse(item.IsPresent, "제외 조건을 무시하며 가구를 배치하면 안 됩니다.");
        }

        [TearDown]
        public void DestroyRoomObjects()
        {
            foreach (GameObject item in _objects)
                if (item != null)
                    Object.DestroyImmediate(item);
            _objects.Clear();
            if (_spawnSettings != null)
                Object.DestroyImmediate(_spawnSettings);
            if (_ritualSettings != null)
                Object.DestroyImmediate(_ritualSettings);
            UnityEngine.Random.state = _randomState;
        }

        private FurnitureSpawnController SpawnController(RandomFurnitureItem item)
        {
            GameObject pointObject = CreateObject("OnlyPlacementPoint");
            pointObject.transform.position = new Vector3(0f, 0f, 3f);
            var point = pointObject.AddComponent<FurnitureSpawnPoint>();
            point.Configure(0, FurnitureSpawnType.Floor, Vector3.one * 2.4f);
            _spawnSettings = ScriptableObject.CreateInstance<FurnitureSpawnSettings>();
            SetPrivateField(_spawnSettings, "_pools", new[]
            {
                new FurnitureSpawnSettings.PoolRule("Box", FurnitureSpawnType.Floor, 1, 1),
            });
            SetPrivateField(_spawnSettings, "_minimumTargetTypes", 1);
            SetPrivateField(_spawnSettings, "_maximumTargetTypes", 1);
            SetPrivateField(_spawnSettings, "_useFixedSeed", true);
            GameObject controllerObject = CreateObject("FurnitureController", false);
            NetworkObject networkObject = AddNetworkObject(controllerObject);
            var controller = controllerObject.AddComponent<FurnitureSpawnController>();
            controller.Configure(_spawnSettings, new[] { item }, new[] { point }, null);
            controllerObject.SetActive(true);
            networkObject.Spawn();
            return controller;
        }

        private RandomFurnitureItem SpawnRandomFurniture()
        {
            GameObject itemObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _objects.Add(itemObject);
            itemObject.SetActive(false);
            itemObject.transform.position = new Vector3(-100f, 0f, 0f);
            var body = itemObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            NetworkObject networkObject = AddNetworkObject(itemObject);
            itemObject.AddComponent<NetworkTransform>();
            var physics = itemObject.AddComponent<FurnitureNetworkPhysics>();
            SetPrivateField(physics, "_definition", Definition);
            var item = itemObject.AddComponent<RandomFurnitureItem>();
            item.Configure("Box", new Bounds(Vector3.zero, Vector3.one), null);
            itemObject.SetActive(true);
            networkObject.Spawn();
            return item;
        }

        private void CreateFloor()
        {
            GameObject floor = CreateObject("Floor");
            floor.transform.position = new Vector3(10f, -0.5f, 3f);
            floor.AddComponent<BoxCollider>().size = new Vector3(40f, 1f, 12f);
        }

        private void CreateRoom(Transform house, string name, Vector3 position)
        {
            GameObject room = CreateObject(name);
            room.transform.SetParent(house);
            room.transform.position = position;
            GameObject boundsObject = CreateObject("RoomDimensions");
            boundsObject.transform.SetParent(room.transform, false);
            var bounds = boundsObject.AddComponent<BoxCollider>();
            bounds.size = new Vector3(6f, 1f, 6f);
            bounds.enabled = false;
            GameObject door = CreateObject("Door", false);
            door.transform.SetParent(room.transform, false);
            door.AddComponent<DoorInteractable>();

            // 실제 방처럼 충돌체 바닥보다 4cm 솟은 바닥 마감 메시(윗면 0.040m, 충돌체 없음).
            GameObject finish = GameObject.CreatePrimitive(PrimitiveType.Cube);
            finish.name = "RoomFloor";
            _objects.Add(finish);
            Object.DestroyImmediate(finish.GetComponent<Collider>());
            finish.transform.SetParent(room.transform, false);
            finish.transform.localPosition = new Vector3(0f, 0.0225f, 0f);
            finish.transform.localScale = new Vector3(6f, 0.035f, 6f);
        }

        private GameObject CreateObject(string name, bool active = true)
        {
            var item = new GameObject(name);
            item.SetActive(active);
            _objects.Add(item);
            return item;
        }

        private NetworkObject AddNetworkObject(GameObject target)
        {
            var networkObject = target.AddComponent<NetworkObject>();
            SetPrivateField(networkObject, "GlobalObjectIdHash", _nextHash++);
            return networkObject;
        }
    }
}
