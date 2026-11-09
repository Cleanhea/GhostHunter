using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GhostHunter.Core;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// 가구와 귀신 몸의 물리 충돌(2026-11-07, 사용자 요청 "가구가 몸을 통과한다")과 귀신 경로의 가구 반영.
    /// 네트워크 없이 <see cref="Physics.Simulate"/> 와 런타임 NavMesh 로 직접 확인한다 —
    /// 실제 Stage1 에서 귀신이 가구에 걸리는 정도는 덮지 않는다.
    /// </summary>
    public sealed class GhostFurnitureCollisionTests
    {
        private const float Step = 0.02f;

        private readonly List<Object> _created = new();
        private SimulationMode _previousMode;
        private bool _previousGhostFurniture;
        private bool _previousGhostPlayer;
        private NavMeshData _navData;
        private NavMeshDataInstance _navInstance;

        [SetUp]
        public void SetUp()
        {
            Assert.GreaterOrEqual(GameLayers.GhostPrototype, 0, "GhostPrototype 레이어");
            Assert.GreaterOrEqual(GameLayers.Furniture, 0, "Furniture 레이어");
            _previousMode = Physics.simulationMode;
            _previousGhostFurniture = Physics.GetIgnoreLayerCollision(GameLayers.GhostPrototype, GameLayers.Furniture);
            _previousGhostPlayer = Physics.GetIgnoreLayerCollision(GameLayers.GhostPrototype, GameLayers.Player);
            Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            if (_navInstance.valid)
                _navInstance.Remove();
            if (_navData != null)
                Object.DestroyImmediate(_navData);
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
            Physics.IgnoreLayerCollision(GameLayers.GhostPrototype, GameLayers.Furniture, _previousGhostFurniture);
            Physics.IgnoreLayerCollision(GameLayers.GhostPrototype, GameLayers.Player, _previousGhostPlayer);
            Physics.simulationMode = _previousMode;
        }

        [Test]
        public void 가구는_귀신_몸을_통과하지_못한다()
        {
            // 예전 상태(충돌 끔)에서 출발해 컨트롤러가 켜는지 본다.
            Physics.IgnoreLayerCollision(GameLayers.GhostPrototype, GameLayers.Furniture, true);
            CreateGhostController(collideWithFurniture: true);
            CreateGhostBody(new Vector3(0f, 1f, 0f));
            Rigidbody furniture = CreateFlyingFurniture(new Vector3(-3f, 1f, 0f), speed: 6f);

            Simulate(1.5f);

            Assert.Less(furniture.position.x, 0.2f,
                $"가구가 귀신(x=0)을 통과했다. x={furniture.position.x:F2}");
        }

        [Test]
        public void 충돌_설정을_끄면_가구가_귀신을_통과한다()
        {
            Physics.IgnoreLayerCollision(GameLayers.GhostPrototype, GameLayers.Furniture, false);
            CreateGhostController(collideWithFurniture: false);
            CreateGhostBody(new Vector3(0f, 1f, 0f));
            Rigidbody furniture = CreateFlyingFurniture(new Vector3(-3f, 1f, 0f), speed: 6f);

            Simulate(1.5f);

            Assert.Greater(furniture.position.x, 3f, "대조군: 설정을 끄면 예전처럼 통과한다");
        }

        [Test]
        public void 귀신은_플레이어_레이어와는_계속_충돌하지_않는다()
        {
            Physics.IgnoreLayerCollision(GameLayers.GhostPrototype, GameLayers.Player, false);

            CreateGhostController(collideWithFurniture: true);

            Assert.IsTrue(Physics.GetIgnoreLayerCollision(GameLayers.GhostPrototype, GameLayers.Player));
        }

        [UnityTest]
        public IEnumerator 가구가_좁은_통로를_막으면_귀신_경로가_막히고_치우면_다시_열린다()
        {
            GameObject house = Track(new GameObject("GhostFurnitureCollisionTests_House"));
            house.transform.position = new Vector3(500f, 0f, 500f);
            Box(house.transform, "Ground", new Vector3(0f, -0.1f, 0f), new Vector3(12f, 0.2f, 2f));
            _navData = GhostNavigation.BuildHouseNavMesh(house.transform, ~0, 0.25f, 1.8f, 0.35f);
            Assert.IsNotNull(_navData);
            _navInstance = NavMesh.AddNavMeshData(_navData);
            Vector3 from = house.transform.position + new Vector3(-4f, 0f, 0f);
            Vector3 to = house.transform.position + new Vector3(4f, 0f, 0f);
            Assert.AreEqual(NavMeshPathStatus.PathComplete, PathStatus(from, to), "가구 없이는 통로가 열려 있어야 한다");

            // 통로 폭(2m)을 꽉 채우는 가구.
            GameObject sofa = Track(new GameObject("Sofa"));
            sofa.transform.position = house.transform.position + new Vector3(0f, 0.5f, 0f);
            BoxCollider body = sofa.AddComponent<BoxCollider>();
            body.size = new Vector3(0.8f, 1f, 2.2f);
            FurnitureNavObstacle obstacle = FurnitureNavObstacle.Attach(sofa);
            obstacle.Refresh(true, new Collider[] { body });
            FurnitureNavObstacle.RefreshAll();

            Assert.IsTrue(obstacle.IsCarving);
            yield return WaitUntilPath(from, to, status => status != NavMeshPathStatus.PathComplete);
            Assert.AreNotEqual(NavMeshPathStatus.PathComplete, PathStatus(from, to),
                "가구가 통로를 막았는데 귀신 경로가 가구를 뚫고 간다");

            obstacle.Refresh(false, new Collider[] { body });
            Assert.IsFalse(obstacle.IsCarving);
            yield return WaitUntilPath(from, to, status => status == NavMeshPathStatus.PathComplete);
            Assert.AreEqual(NavMeshPathStatus.PathComplete, PathStatus(from, to), "가구를 치우면 다시 열려야 한다");
        }

        private static IEnumerator WaitUntilPath(
            Vector3 from, Vector3 to, System.Func<NavMeshPathStatus, bool> done)
        {
            for (int frame = 0; frame < 240; frame++)
            {
                if (done(PathStatus(from, to)))
                    yield break;
                yield return null;
            }
        }

        private static NavMeshPathStatus PathStatus(Vector3 from, Vector3 to)
        {
            if (!NavMesh.SamplePosition(from, out NavMeshHit start, 1f, NavMesh.AllAreas)
                || !NavMesh.SamplePosition(to, out NavMeshHit end, 1f, NavMesh.AllAreas))
            {
                return NavMeshPathStatus.PathInvalid;
            }

            var path = new NavMeshPath();
            NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path);
            return path.status;
        }

        /// <summary>설정 에셋을 끼운 귀신 컨트롤러 — 활성화 전에 설정을 넣어 Awake 가 그 값을 읽게 한다.</summary>
        private GhostPrototypeController CreateGhostController(bool collideWithFurniture)
        {
            var settings = ScriptableObject.CreateInstance<GhostPrototypeSettings>();
            _created.Add(settings);
            typeof(GhostPrototypeSettings)
                .GetField("_collideWithFurniture", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(settings, collideWithFurniture);

            // RequireComponent 가 붙이는 캐릭터 컨트롤러가 시험 물체에 걸리지 않게 멀리 둔다.
            var go = Track(new GameObject("GhostController"));
            go.transform.position = new Vector3(2000f, 2000f, 2000f);
            go.SetActive(false);
            var controller = go.AddComponent<GhostPrototypeController>();
            typeof(GhostPrototypeController)
                .GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(controller, settings);
            go.SetActive(true);
            return controller;
        }

        /// <summary>귀신 몸을 흉내 낸 캐릭터 컨트롤러(귀신 레이어).</summary>
        private void CreateGhostBody(Vector3 position)
        {
            GameObject go = Track(new GameObject("GhostBody"));
            go.layer = GameLayers.GhostPrototype;
            go.transform.position = position;
            CharacterController controller = go.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.3f;
        }

        private Rigidbody CreateFlyingFurniture(Vector3 position, float speed)
        {
            GameObject go = Track(new GameObject("Furniture"));
            go.layer = GameLayers.Furniture;
            go.transform.position = position;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = Vector3.one * 0.5f;
            Rigidbody body = go.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.linearDamping = 0f;
            body.linearVelocity = new Vector3(speed, 0f, 0f);
            return body;
        }

        private static void Simulate(float seconds)
        {
            int steps = Mathf.CeilToInt(seconds / Step);
            for (int i = 0; i < steps; i++)
                Physics.Simulate(Step);
        }

        private void Box(Transform parent, string name, Vector3 localPosition, Vector3 size)
        {
            GameObject go = Track(new GameObject(name));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.AddComponent<BoxCollider>().size = size;
        }

        private GameObject Track(GameObject go)
        {
            _created.Add(go);
            return go;
        }
    }
}
