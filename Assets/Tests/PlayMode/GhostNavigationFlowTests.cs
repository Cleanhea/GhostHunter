using System.Collections;
using System.Text.RegularExpressions;
using GhostHunter.Core;
using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>실제 NavMesh와 CharacterController로 경로 유지·이동·해제를 검증한다.</summary>
    public sealed class GhostNavigationFlowTests
    {
        private static readonly Vector3 Center = new(500f, 0f, 500f);
        private static readonly Vector3 Extents = new(10f, 3f, 10f);

        private GameObject _house;
        private GameObject _actor;
        private GhostPrototypeSettings _settings;
        private GhostNavigation _navigation;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _house = new GameObject("GhostNavigationFlow_House");
            _house.transform.position = Center;
            var floor = new GameObject("Floor");
            floor.transform.SetParent(_house.transform, false);
            floor.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            floor.AddComponent<BoxCollider>().size = new Vector3(16f, 0.2f, 16f);
            _actor = new GameObject("GhostNavigationFlow_Actor");
            _actor.layer = GameLayers.GhostPrototype;
            _actor.transform.position = Center;
            var controller = _actor.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.center = Vector3.up * 0.9f;
            controller.radius = 0.25f;
            controller.stepOffset = 0.35f;
            _settings = ScriptableObject.CreateInstance<GhostPrototypeSettings>();
            _navigation = new GhostNavigation(controller, _settings, (corners, count) => false, _actor);
            Physics.SyncTransforms();
            _navigation.EnsureNavMesh(_house.transform, Center, Extents, restrictHeight: false);
            Assert.IsTrue(NavMesh.SamplePosition(Center, out _, 1f, NavMesh.AllAreas));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _navigation.Dispose();
            Object.Destroy(_actor);
            Object.Destroy(_house);
            Object.Destroy(_settings);
            yield return null;
        }

        [Test]
        public void 후보_경로_측정은_현재_추격_경로의_코너를_덮지_않는다()
        {
            Vector3 destination = Center + Vector3.forward * 4f;
            Assert.IsTrue(_navigation.MoveToward(destination, 2f, 0.1f, Center, Extents, allowPartial: false));
            Vector3 waypoint = _navigation.GetWaypointOr(Center);
            Assert.Greater((waypoint - Center).sqrMagnitude, 1f);
            Assert.IsTrue(_navigation.TryMeasurePath(Center + Vector3.left * 4f, out float length));
            Assert.Greater(length, 0f);
            Assert.AreEqual(waypoint, _navigation.GetWaypointOr(Center));
            Assert.IsTrue(_navigation.MoveToward(destination, 2f, 0.1f, Center, Extents, allowPartial: false));
            Assert.Greater(_actor.transform.position.z, Center.z);
            Assert.AreEqual(Center.x, _actor.transform.position.x, 0.01f);
        }

        [Test]
        public void 경로가_없는_목적지로_수평_직선_이동하지_않는다()
        {
            Vector3 before = _actor.transform.position;
            LogAssert.Expect(LogType.Warning, new Regex("NavMesh"));
            Assert.IsFalse(_navigation.MoveToward(Center + Vector3.right * 30f, 3f, 0.1f,
                Center, Vector3.one * 100f, allowPartial: false));
            Assert.Less(Vector3.ProjectOnPlane(_actor.transform.position - before, Vector3.up).magnitude, 0.01f);
        }

        [Test]
        public void 이동_목적지는_집_평면_경계_안으로_제한한다()
        {
            Vector3 extents = new(2f, 3f, 2f);
            for (int i = 0; i < 30; i++)
                _navigation.MoveToward(Center + Vector3.right * 5f, 3f, 0.1f,
                    Center, extents, allowPartial: false);
            Assert.Greater(_actor.transform.position.x, Center.x + 1f);
            Assert.LessOrEqual(_actor.transform.position.x, Center.x + extents.x + 0.01f);
            Assert.AreEqual(Center.z, _actor.transform.position.z, 0.01f);
        }

        [UnityTest]
        public IEnumerator NavMesh_해제는_경로와_배회_캐시를_비우며_여러번_호출해도_안전하다()
        {
            Assert.IsTrue(_navigation.TryRandomNavPoint(out _));
            _navigation.Dispose();
            _navigation.Dispose();
            Assert.IsFalse(NavMesh.SamplePosition(Center, out _, 1f, NavMesh.AllAreas));
            Assert.IsFalse(_navigation.TryRandomNavPoint(out _));
            Assert.AreEqual(Center, _navigation.GetWaypointOr(Center));
            yield return null;
        }
    }
}
