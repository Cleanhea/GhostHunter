using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Recovery;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>Local Host 에서 배터리 팩의 공유 잔량·서버 청구 검증을 확인한다(기획서 headlamp-system.md §8).</summary>
    public sealed class BatteryPackFlowTests : NetworkFurnitureFixture
    {
        private readonly List<Object> _created = new();
        private uint _nextHash = 0x5F600001;

        [UnityTest]
        public IEnumerator 스폰하면_잔량이_총량이다()
        {
            BatteryPack pack = SpawnPack();
            yield return null;
            Assert.AreEqual(200f, pack.Remaining);
            Assert.IsFalse(pack.IsEmpty);
        }

        [UnityTest]
        public IEnumerator 서버는_청구한_양을_잔량에서_빼고_빼준_양을_돌려준다()
        {
            BatteryPack pack = SpawnPack();
            yield return null;
            float granted = pack.ServerDraw(HostClientId, 5f);
            Assert.AreEqual(5f, granted, 0.0001f);
            Assert.AreEqual(195f, pack.Remaining, 0.0001f);
        }

        [UnityTest]
        public IEnumerator 시간이_안_지났는데_몰아서_청구하면_최대_충전_속도만큼만_받는다()
        {
            BatteryPack pack = SpawnPack();
            yield return null;
            pack.ServerDraw(HostClientId, 1f);
            // 곧바로 큰 양을 청구 — 지난 시간이 거의 0이라 허용량이 거의 없다.
            float granted = pack.ServerDraw(HostClientId, 150f);
            Assert.Less(granted, 5f, "충전 속도 상한(30%p/초)을 넘는 청구는 받아 주지 않는다");
            Assert.Greater(pack.Remaining, 190f);
        }

        [UnityTest]
        public IEnumerator 잔량보다_많이_청구해도_잔량까지만_주고_0이_되면_아무도_충전할_수_없다()
        {
            BatteryPack pack = SpawnPack();
            yield return null;
            var snapshot = new StageRecoverySnapshot { HasBatteryPack = true, BatteryPackRemaining = 3f };
            pack.ServerRestoreStageState(snapshot);
            Assert.AreEqual(3f, pack.Remaining);

            Assert.AreEqual(3f, pack.ServerDraw(HostClientId, 10f), 0.0001f);
            Assert.AreEqual(0f, pack.Remaining);
            Assert.IsTrue(pack.IsEmpty);
            Assert.AreEqual(0f, pack.ServerDraw(HostClientId, 10f), "잔량 0이면 거절한다");
        }

        [UnityTest]
        public IEnumerator 멀리_떨어진_플레이어의_청구는_거절한다()
        {
            BatteryPack pack = SpawnPack();
            pack.transform.position = new Vector3(0f, 0f, 50f);
            yield return null;
            Assert.AreEqual(0f, pack.ServerDraw(HostClientId, 5f));
            Assert.AreEqual(200f, pack.Remaining);
        }

        [UnityTest]
        public IEnumerator 팩_정보가_없던_옛_스냅샷은_잔량을_그대로_두고_새_스냅샷은_잔량을_싣는다()
        {
            BatteryPack pack = SpawnPack();
            yield return null;
            pack.ServerRestoreStageState(new StageRecoverySnapshot());
            Assert.AreEqual(200f, pack.Remaining, "HasBatteryPack 이 거짓인 옛 스냅샷은 가득 찬 채로 둔다");

            pack.ServerDraw(HostClientId, 10f);
            var snapshot = new StageRecoverySnapshot();
            pack.CaptureStageState(snapshot);
            Assert.IsTrue(snapshot.HasBatteryPack);
            Assert.AreEqual(190f, snapshot.BatteryPackRemaining, 0.0001f);
        }

        [TearDown]
        public void DestroyPackObjects()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        private BatteryPack SpawnPack()
        {
            var settings = ScriptableObject.CreateInstance<BatteryPackSettings>();
            _created.Add(settings);

            var instance = new GameObject("TestBatteryPack");
            _created.Add(instance);
            instance.SetActive(false);
            instance.transform.position = new Vector3(0f, 0f, 1f);
            NetworkObject networkObject = instance.AddComponent<NetworkObject>();
            typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(networkObject, _nextHash++);
            BatteryPack pack = instance.AddComponent<BatteryPack>();
            typeof(BatteryPack).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(pack, settings);
            instance.SetActive(true);
            networkObject.Spawn();
            return pack;
        }
    }
}
