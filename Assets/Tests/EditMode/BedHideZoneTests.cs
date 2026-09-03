using System.Reflection;
using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 침대 밑 은신 공간(<see cref="BedHideZone"/>)의 상자 경계 판정을 검증한다. 정적 레지스트리와
    /// 부모 침대 Idle 게이트는 Play Mode 전용 생명주기라 EditMode 대상이 아니다
    /// (<see cref="HidingSpot"/> 테스트와 같은 관례) — 순수 기하(<see cref="BedHideZone.ContainsPoint"/>)만 본다.
    /// </summary>
    public sealed class BedHideZoneTests
    {
        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
                Object.DestroyImmediate(_host);

            _host = null;
        }

        private BedHideZone CreateZone(Vector3 position, Vector3 size)
        {
            _host = new GameObject("BedHideZone_Test");
            _host.transform.position = position;
            var zone = _host.AddComponent<BedHideZone>();

            FieldInfo field = typeof(BedHideZone).GetField(
                "_size",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "BedHideZone._size 를 찾지 못했습니다.");
            field.SetValue(zone, size);

            return zone;
        }

        [Test]
        public void 중심과_경계_안쪽의_점은_은신_공간_안이다()
        {
            BedHideZone zone = CreateZone(new Vector3(3f, 0.35f, -2f), new Vector3(0.9f, 0.9f, 1.8f));

            Assert.IsTrue(zone.ContainsPoint(new Vector3(3f, 0.35f, -2f)));
            Assert.IsTrue(zone.ContainsPoint(new Vector3(3.44f, 0.1f, -2.89f)));
        }

        [Test]
        public void 옆면_밖의_점은_은신_공간_밖이다()
        {
            BedHideZone zone = CreateZone(new Vector3(0f, 0.35f, 0f), new Vector3(0.9f, 0.9f, 1.8f));

            Assert.IsFalse(zone.ContainsPoint(new Vector3(0.5f, 0.35f, 0f)));
            Assert.IsFalse(zone.ContainsPoint(new Vector3(0f, 0.35f, 1f)));
        }

        [Test]
        public void 회전된_침대의_은신_공간은_로컬_축_기준으로_판정한다()
        {
            BedHideZone zone = CreateZone(new Vector3(5f, 0.35f, 5f), new Vector3(0.9f, 0.9f, 2f));
            _host.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            // 90도 회전으로 로컬 X(폭 0.9m)·Z(길이 2m)가 월드 Z·X로 맞바뀐다.
            Assert.IsTrue(zone.ContainsPoint(new Vector3(5.9f, 0.35f, 5f)));
            Assert.IsFalse(zone.ContainsPoint(new Vector3(5f, 0.35f, 5.9f)));
        }
    }
}
