using System.Reflection;
using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 일반 은신처(§9.5)의 상자 경계 판정을 검증한다. 정적 레지스트리는 <c>OnEnable</c>/
    /// <c>OnDisable</c>(<see cref="DrillCarSafeZone"/>·<see cref="GhostAmbientLight"/> 과 동일한
    /// 패턴)로 채워지는데, 이 생명주기는 Play Mode 에서만 도는 것이라 EditMode 에서는 검증하지
    /// 않는다 — 순수 기하 판정(<see cref="HidingSpot.ContainsPoint"/>)만 대상으로 한다.
    /// </summary>
    public sealed class HidingSpotTests
    {
        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
                Object.DestroyImmediate(_host);

            _host = null;
        }

        private HidingSpot CreateSpot(Vector3 position, Vector3 size)
        {
            _host = new GameObject("HidingSpot_Test");
            _host.transform.position = position;
            var spot = _host.AddComponent<HidingSpot>();

            FieldInfo field = typeof(HidingSpot).GetField(
                "_size",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "HidingSpot._size 를 찾지 못했습니다.");
            field.SetValue(spot, size);

            return spot;
        }

        [Test]
        public void 중심과_경계_안쪽의_점은_은신처_안이다()
        {
            HidingSpot spot = CreateSpot(new Vector3(2f, 0f, -1f), new Vector3(2f, 2f, 2f));

            Assert.IsTrue(spot.ContainsPoint(new Vector3(2f, 0f, -1f)));
            Assert.IsTrue(spot.ContainsPoint(new Vector3(2.99f, 0.9f, -1.99f)));
        }

        [Test]
        public void 경계_밖의_점은_은신처_밖이다()
        {
            HidingSpot spot = CreateSpot(new Vector3(0f, 0f, 0f), new Vector3(2f, 2f, 2f));

            Assert.IsFalse(spot.ContainsPoint(new Vector3(1.01f, 0f, 0f)));
            Assert.IsFalse(spot.ContainsPoint(new Vector3(0f, 1.5f, 0f)));
        }

        [Test]
        public void 회전된_은신처는_로컬_축_기준으로_판정한다()
        {
            HidingSpot spot = CreateSpot(new Vector3(5f, 0f, 5f), new Vector3(1f, 2f, 3f));
            _host.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            // 90도 회전으로 로컬 X(폭 1m)·Z(깊이 3m) 가 월드 Z·X 로 맞바뀐다.
            Assert.IsTrue(spot.ContainsPoint(new Vector3(5.9f, 0f, 5f)));
            Assert.IsFalse(spot.ContainsPoint(new Vector3(5f, 0f, 5.9f)));
        }
    }
}
