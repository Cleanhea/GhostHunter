using System.Collections;
using GhostHunter.Core;
using GhostHunter.Gameplay.Furniture;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>Local Host에서 충돌 피해·파손·복구·배치 보호를 검증한다.</summary>
    public sealed class FurnitureCollisionFlowTests : NetworkFurnitureFixture
    {
        [UnityTest]
        public IEnumerator 스폰_직후에는_충돌_보호가_적용된다()
        {
            var target = SpawnFurniture();
            var physics = target.GetComponent<FurnitureNetworkPhysics>();
            physics.ServerApplyCollisionSpeed(100f);
            Assert.AreEqual(100, physics.Durability);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 일미터_낙하와_바닥에_놓인_동안에는_감소하지_않는다()
        {
            var physics = ReadyFurniture();
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                floor.transform.position = new Vector3(0f, -0.5f, 3f);
                floor.transform.localScale = new Vector3(10f, 1f, 10f);
                physics.Rigidbody.position = new Vector3(0f, 1.5f, 3f);
                Physics.SyncTransforms();
                for (int i = 0; i < 100; i++)
                    yield return new WaitForFixedUpdate();
                Assert.Less(physics.Rigidbody.position.y, 0.6f, "바닥까지 실제로 낙하해야 합니다.");
                Assert.AreEqual(100, physics.Durability);
            }
            finally
            {
                Object.DestroyImmediate(floor);
            }
        }

        [UnityTest]
        public IEnumerator 판정_창에는_최대_피해만_적용하고_다음_창에서는_새로_감소한다()
        {
            var physics = ReadyFurniture();
            physics.ServerApplyCollisionSpeed(12f);
            physics.ServerApplyCollisionSpeed(30f);
            physics.ServerApplyCollisionSpeed(20f);
            physics.ServerApplyCollisionSpeed(30f);
            Assert.AreEqual(76, physics.Durability);
            SetPrivateField(physics, "_windowEndsAt", 0d);
            physics.ServerApplyCollisionSpeed(30f);
            Assert.AreEqual(52, physics.Durability);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 없애지_않는_설정에서는_내구도가_0이어도_가구가_그대로_남는다()
        {
            // 2026-09-16 사용자 요청 — 기본값은 "파괴하지 않음"이다(FurnitureDefinition 기본값).
            var physics = ReadyFurniture();
            var target = physics.GetComponent<FurnitureGrabTarget>();
            Assert.IsTrue(target.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection));
            physics.ServerSetDurability(3);
            physics.ServerApplyCollisionSpeed(20f);
            Assert.AreEqual(0, physics.Durability);
            Assert.IsFalse(physics.IsBroken);
            Assert.IsTrue(physics.IsAvailable);
            Assert.AreEqual(1, target.HolderCount, "0이 되어도 들고 있던 홀더를 떼지 않습니다.");
            Assert.IsFalse(physics.GetComponent<Renderer>().forceRenderingOff);
            Assert.IsTrue(physics.GetComponent<Collider>().enabled);
            Assert.IsFalse(physics.Rigidbody.isKinematic);
            physics.ServerApplyCollisionSpeed(100f);
            Assert.AreEqual(0, physics.Durability, "0 아래로는 내려가지 않습니다.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator 운반_중_파손되면_발사하지_않고_두_홀더를_즉시_해제한다()
        {
            // 사라짐은 기본값이 꺼짐(FD-10 재확정 2026-09-16)이라 이 경로를 검증하려면 설정에서 켠다.
            SetPrivateField(Definition, "_destroyAtZeroDurability", true);
            var physics = ReadyFurniture();
            var target = physics.GetComponent<FurnitureGrabTarget>();
            Assert.IsTrue(target.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection));
            Assert.IsTrue(target.ServerTryAddHolder(PartnerClientId, AimOrigin, AimDirection));
            physics.ServerSetDurability(3);
            physics.ServerApplyCollisionSpeed(20f);
            Assert.IsTrue(physics.IsBroken);
            Assert.AreEqual(0, target.HolderCount);
            Assert.AreEqual(FurnitureState.Idle, target.State);
            Assert.IsFalse(target.CanGrab(HostClientId));
            Assert.IsTrue(physics.GetComponent<Renderer>().forceRenderingOff);
            Assert.IsFalse(physics.GetComponent<Collider>().enabled);
            Assert.IsTrue(physics.Rigidbody.isKinematic);
            Assert.IsFalse(physics.Rigidbody.detectCollisions);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 파손_복구는_가구를_되살리고_보호_시간을_갱신한다()
        {
            SetPrivateField(Definition, "_destroyAtZeroDurability", true);
            var physics = ReadyFurniture();
            Vector3 original = physics.Rigidbody.position;
            physics.ServerSetDurability(3);
            physics.ServerApplyCollisionSpeed(20f);
            Assert.IsFalse(physics.ServerSetDurability(100), "일반 상속 경로로는 파손 가구를 복구할 수 없습니다.");
            physics.Rigidbody.position = original + Vector3.right * 10f;
            physics.ServerResetDurability(true);
            Assert.AreEqual(original, physics.Rigidbody.position);
            Assert.AreEqual(100, physics.Durability);
            Assert.IsTrue(physics.IsAvailable);
            Assert.IsFalse(physics.GetComponent<Renderer>().forceRenderingOff);
            Assert.IsTrue(physics.GetComponent<Collider>().enabled);
            physics.ServerApplyCollisionSpeed(100f);
            Assert.AreEqual(100, physics.Durability);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 실제_가구끼리_충돌하면_양쪽에_같은_피해를_준다()
        {
            var first = ReadyFurniture();
            var second = ReadyFurniture();
            first.Rigidbody.position = new Vector3(-0.65f, 3f, 0f);
            second.Rigidbody.position = new Vector3(0.65f, 3f, 0f);
            first.Rigidbody.useGravity = false;
            second.Rigidbody.useGravity = false;
            first.Rigidbody.linearVelocity = Vector3.right * 20f;
            Physics.SyncTransforms();
            for (int i = 0; i < 20 && first.Durability == 100; i++)
                yield return new WaitForFixedUpdate();
            Assert.Less(first.Durability, 100);
            Assert.AreEqual(first.Durability, second.Durability);
        }

        [UnityTest]
        public IEnumerator 실제_플레이어와_귀신_레이어_충돌은_피해가_없다()
        {
            foreach (int layer in new[] { GameLayers.Player, GameLayers.GhostPrototype })
            {
                var physics = ReadyFurniture();
                GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                try
                {
                    wall.layer = layer;
                    wall.transform.position = new Vector3(1.1f, 3f, 0f);
                    physics.Rigidbody.position = new Vector3(0f, 3f, 0f);
                    physics.Rigidbody.useGravity = false;
                    physics.Rigidbody.linearVelocity = Vector3.right * 20f;
                    Physics.SyncTransforms();
                    for (int i = 0; i < 5; i++)
                        yield return new WaitForFixedUpdate();
                    Assert.AreEqual(100, physics.Durability);
                    Assert.Less(physics.Rigidbody.position.x, 1f, "실제로 차단된 충돌인지 확인합니다.");
                }
                finally
                {
                    Object.DestroyImmediate(wall);
                    Object.DestroyImmediate(physics.gameObject);
                }
            }
        }

        [UnityTest]
        public IEnumerator 보호_중인_가구에_충돌하면_보호되지_않은_쪽만_감소한다()
        {
            var first = ReadyFurniture();
            var second = ReadyFurniture();
            second.ServerProtectPlacement();
            first.Rigidbody.position = new Vector3(-0.65f, 3f, 0f);
            second.Rigidbody.position = new Vector3(0.65f, 3f, 0f);
            first.Rigidbody.useGravity = false;
            second.Rigidbody.useGravity = false;
            first.Rigidbody.linearVelocity = Vector3.right * 20f;
            Physics.SyncTransforms();
            for (int i = 0; i < 20 && first.Durability == 100; i++)
                yield return new WaitForFixedUpdate();
            Assert.Less(first.Durability, 100);
            Assert.AreEqual(100, second.Durability);
        }

        private FurnitureNetworkPhysics ReadyFurniture()
        {
            var physics = SpawnFurniture().GetComponent<FurnitureNetworkPhysics>();
            SetPrivateField(physics, "_protectedUntil", 0d);
            return physics;
        }
    }
}
