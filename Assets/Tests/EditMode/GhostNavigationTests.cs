using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 귀신 전용 NavMesh 굽기(MAP-11)와 층 인식 도착 판정을 검증한다. 기하는 Stage1 B안 수치를 흉내 낸다
    /// — 문틀 안쪽 1.0m, 층고 3.0m·진행 5.0m 경사 콜라이더.
    /// </summary>
    public sealed class GhostNavigationTests
    {
        private const float GhostRadius = 0.25f;
        private const float GhostHeight = 1.8f;
        private const float GhostClimb = 0.35f;

        private GameObject _root;
        private NavMeshData _data;
        private NavMeshDataInstance _instance;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("GhostNavigationTests_House");
            _root.transform.position = new Vector3(500f, 0f, 500f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_instance.valid)
                _instance.Remove();
            if (_data != null)
                Object.DestroyImmediate(_data);
            if (_root != null)
                Object.DestroyImmediate(_root);
        }

        [Test]
        public void 폭_1m_문틀을_지나는_완전한_경로가_있다()
        {
            BuildRoomsWithDoorway();
            Bake();

            Assert.AreEqual(NavMeshPathStatus.PathComplete, PathStatus(Local(-4f, 0f, 0f), Local(4f, 0f, 0f)));
        }

        [Test]
        public void 문틀의_트리거는_NavMesh_장애물이_아니다()
        {
            BuildRoomsWithDoorway();
            Box("HidingTrigger", Local(0f, 1f, 0f), new Vector3(0.6f, 2f, 1f), Quaternion.identity)
                .GetComponent<BoxCollider>().isTrigger = true;
            Bake();

            Assert.AreEqual(NavMeshPathStatus.PathComplete, PathStatus(Local(-4f, 0f, 0f), Local(4f, 0f, 0f)));
        }

        [Test]
        public void 경사_계단으로_위층까지_완전한_경로가_있다()
        {
            Box("Ground", Local(0f, -0.1f, 0f), new Vector3(16f, 0.2f, 8f), Quaternion.identity);
            Box("UpperFloor", Local(4f, 2.9f, 0f), new Vector3(8f, 0.2f, 8f), Quaternion.identity);

            float angle = Mathf.Atan2(3f, 5f) * Mathf.Rad2Deg;
            float length = Mathf.Sqrt(3f * 3f + 5f * 5f);
            Vector3 up = Quaternion.Euler(0f, 0f, angle) * Vector3.up;
            Box("Ramp", Local(-2.5f, 1.5f, 0f) - up * 0.11f, new Vector3(length, 0.22f, 2.2f),
                Quaternion.Euler(0f, 0f, angle));
            Bake();

            Assert.AreEqual(NavMeshPathStatus.PathComplete, PathStatus(Local(-6f, 0f, 0f), Local(5f, 3f, 0f)));
        }

        [Test]
        public void 도착은_같은_층일_때만이다()
        {
            Vector3 ghost = new(1f, 0f, 1f);

            Assert.IsTrue(GhostPrototypeController.HasArrived(ghost, new Vector3(1.3f, 0.1f, 1f), 0.6f));
            Assert.IsFalse(GhostPrototypeController.HasArrived(ghost, new Vector3(1f, 3f, 1f), 0.6f),
                "바로 위층 지점은 평면 거리가 0이어도 도착이 아니다");
            Assert.IsFalse(GhostPrototypeController.HasArrived(ghost, new Vector3(2f, 0f, 1f), 0.6f));
        }

        [Test]
        public void 배회_포기_시간은_긴_경로를_끝까지_걸을_만큼_길다()
        {
            // 30m 경로(층 이동)를 1.6m/s 로 걸으면 19초 가까이 걸린다.
            Assert.Greater(GhostPrototypeController.RoamGiveUpSeconds(30f, 1.6f), 30f / 1.6f);
            Assert.Greater(GhostPrototypeController.RoamGiveUpSeconds(0f, 1.6f), 0f);
            Assert.Greater(GhostPrototypeController.RoamGiveUpSeconds(10f, 0f), 0f);
        }

        /// <summary>바닥 12×8m 가운데 x=0 에 두께 0.2m 벽, z=±0.5 사이 1.0m 출입구.</summary>
        private void BuildRoomsWithDoorway()
        {
            Box("Ground", Local(0f, -0.1f, 0f), new Vector3(12f, 0.2f, 8f), Quaternion.identity);
            Box("Wall_South", Local(0f, 1.4f, -2.25f), new Vector3(0.2f, 2.8f, 3.5f), Quaternion.identity);
            Box("Wall_North", Local(0f, 1.4f, 2.25f), new Vector3(0.2f, 2.8f, 3.5f), Quaternion.identity);
        }

        private void Bake()
        {
            _data = GhostPrototypeController.BuildHouseNavMesh(
                _root.transform, ~0, GhostRadius, GhostHeight, GhostClimb);
            Assert.IsNotNull(_data, "NavMesh 를 굽지 못했습니다.");
            _instance = NavMesh.AddNavMeshData(_data);
        }

        private static NavMeshPathStatus PathStatus(Vector3 from, Vector3 to)
        {
            Assert.IsTrue(NavMesh.SamplePosition(from, out NavMeshHit start, 1f, NavMesh.AllAreas),
                $"{from} 근처에 NavMesh 가 없습니다.");
            Assert.IsTrue(NavMesh.SamplePosition(to, out NavMeshHit end, 1f, NavMesh.AllAreas),
                $"{to} 근처에 NavMesh 가 없습니다.");

            var path = new NavMeshPath();
            NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path);
            return path.status;
        }

        private Vector3 Local(float x, float y, float z) => _root.transform.position + new Vector3(x, y, z);

        private GameObject Box(string name, Vector3 center, Vector3 size, Quaternion rotation)
        {
            var box = new GameObject(name);
            box.transform.SetParent(_root.transform, false);
            box.transform.SetPositionAndRotation(center, rotation);
            box.AddComponent<BoxCollider>().size = size;
            return box;
        }
    }
}
