using System.Collections.Generic;
using System.Reflection;
using GhostHunter.Core.Player;
using GhostHunter.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 스폰 지점 선택. 클라이언트 ID 로 나눠 고르기만 하면 재접속·난입에서 이미 서 있는
    /// 사람 위에 겹쳐 나온다 → [ADR-0012 §4]
    /// </summary>
    public sealed class PlayerSpawnRegistryTests
    {
        private const float Spacing = 5f;

        private readonly List<GameObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
            Physics.SyncTransforms();
        }

        private T Track<T>(T target) where T : Object
        {
            if (target is GameObject gameObject)
                _created.Add(gameObject);

            return target;
        }

        /// <summary>x 축을 따라 <paramref name="count"/> 개의 스폰 지점을 만든 레지스트리.</summary>
        private IPlayerSpawnRegistry CreateRegistry(int count, out Transform[] points)
        {
            var host = Track(new GameObject("PlayerSpawnPoints"));
            var registry = host.AddComponent<PlayerSpawnRegistry>();

            points = new Transform[count];
            for (int i = 0; i < count; i++)
            {
                var point = new GameObject($"PlayerSpawn_{i}");
                point.transform.SetParent(host.transform);
                point.transform.position = new Vector3(i * Spacing, 0f, 0f);
                points[i] = point.transform;
            }

            FieldInfo field = typeof(PlayerSpawnRegistry).GetField(
                "_spawnPoints",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "PlayerSpawnRegistry._spawnPoints 를 찾지 못했습니다.");
            field.SetValue(registry, points);

            Physics.SyncTransforms();
            return registry;
        }

        /// <summary>그 자리를 사람이 차지한 것처럼 막는다.</summary>
        private GameObject Occupy(Vector3 footPosition)
        {
            var blocker = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            blocker.name = "Occupant";
            blocker.transform.position = footPosition + Vector3.up;
            blocker.transform.localScale = new Vector3(1f, 2f, 1f);
            Physics.SyncTransforms();
            return blocker;
        }

        [Test]
        public void TryGetSpawn_지점이_없으면_실패한다()
        {
            IPlayerSpawnRegistry registry = CreateRegistry(0, out _);

            Assert.IsFalse(registry.TryGetSpawn(0, null, out _, out _));
        }

        [Test]
        public void TryGetSpawn_비어_있으면_선호_지점을_고른다()
        {
            IPlayerSpawnRegistry registry = CreateRegistry(4, out Transform[] points);

            Assert.IsTrue(registry.TryGetSpawn(2, null, out Vector3 position, out _));
            Assert.AreEqual(points[2].position, position);
        }

        /// <summary>클라이언트 ID 가 지점 수를 넘어가면 앞에서부터 다시 고른다.</summary>
        [Test]
        public void TryGetSpawn_선호_지점은_클라이언트_ID_의_나머지로_정해진다()
        {
            IPlayerSpawnRegistry registry = CreateRegistry(4, out Transform[] points);

            Assert.IsTrue(registry.TryGetSpawn(6, null, out Vector3 position, out _));
            Assert.AreEqual(points[2].position, position);
        }

        [Test]
        public void TryGetSpawn_선호_지점이_막혀_있으면_다른_지점을_고른다()
        {
            IPlayerSpawnRegistry registry = CreateRegistry(4, out Transform[] points);
            Occupy(points[1].position);

            Assert.IsTrue(registry.TryGetSpawn(1, null, out Vector3 position, out _));
            Assert.AreNotEqual(points[1].position, position);
        }

        /// <summary>
        /// 플레이어 오브젝트는 자리를 정하기 <b>전에</b> 이미 씬에 있다. 자기 콜라이더를 세면
        /// 모든 자리가 막힌 것처럼 보인다.
        /// </summary>
        [Test]
        public void TryGetSpawn_자기_콜라이더는_막힌_것으로_세지_않는다()
        {
            IPlayerSpawnRegistry registry = CreateRegistry(4, out Transform[] points);
            GameObject self = Occupy(points[1].position);

            Assert.IsTrue(registry.TryGetSpawn(1, self.transform, out Vector3 position, out _));
            Assert.AreEqual(points[1].position, position);
        }

        [Test]
        public void TryGetSpawn_전_지점이_막히면_선호_지점_주변으로_밀어낸다()
        {
            IPlayerSpawnRegistry registry = CreateRegistry(4, out Transform[] points);
            foreach (Transform point in points)
                Occupy(point.position);

            Assert.IsTrue(registry.TryGetSpawn(0, null, out Vector3 position, out _));

            Assert.AreNotEqual(points[0].position, position);
            Assert.Less(
                Vector3.Distance(points[0].position, position),
                2f,
                "선호 지점에서 너무 멀리 밀려났습니다.");
        }
    }
}
