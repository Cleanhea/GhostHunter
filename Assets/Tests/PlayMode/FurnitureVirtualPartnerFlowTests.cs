using System.Collections;
using GhostHunter.Gameplay.Furniture;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// 개발자 테스트용 가상 두 번째 홀더(<see cref="FurnitureVirtualPartner"/>) — 혼자서 2인 들기를 재현한다.
    /// 서버 Local Host 위에서 실제 <see cref="FurnitureGrabTarget"/>·<see cref="FurnitureHoverMotor"/> 로 확인한다.
    /// </summary>
    public sealed class FurnitureVirtualPartnerFlowTests : NetworkFurnitureFixture
    {
        [UnityTest]
        public IEnumerator 가상_파트너를_붙이면_2인_운반이_되고_물리_스텝_뒤에도_유지된다()
        {
            FurnitureGrabTarget furniture = HostHolds();
            var partner = new FurnitureVirtualPartner();

            Assert.IsTrue(partner.TryAttach(furniture, HostClientId, AimOrigin, AimDirection), partner.Message);
            Assert.AreEqual(FurnitureState.Held, furniture.State);
            Assert.AreEqual(2, furniture.HolderCount);
            Assert.IsTrue(furniture.ContainsHolder(FurnitureVirtualPartner.ClientId));

            // 접속하지 않은 홀더는 서버가 물리 스텝마다 확인해 강제 해제하는데(testing.md §4.4), 가상 파트너는 빼 준다.
            for (int i = 0; i < 10; i++)
            {
                yield return new WaitForFixedUpdate();
                partner.Tick(AimOrigin, AimDirection);
            }

            Assert.IsTrue(partner.IsAttached, partner.Message);
            Assert.AreEqual(FurnitureState.Held, furniture.State);
            Assert.AreEqual(2, furniture.HolderCount);

            partner.Detach();
            Assert.IsFalse(FurnitureVirtualPartner.IsVirtualHolder(FurnitureVirtualPartner.ClientId));
        }

        [UnityTest]
        public IEnumerator 대조군_일반_가짜_홀더는_물리_스텝에서_강제_해제된다()
        {
            FurnitureGrabTarget furniture = HostHolds();
            Assert.IsTrue(furniture.ServerTryAddHolder(PartnerClientId, AimOrigin + Vector3.right, AimDirection));
            Assert.AreEqual(2, furniture.HolderCount);

            for (int i = 0; i < 5; i++)
                yield return new WaitForFixedUpdate();

            Assert.IsFalse(furniture.ContainsHolder(PartnerClientId),
                "플레이어 오브젝트가 없는 홀더는 사라지는 것이 기본 동작이다 — 가상 파트너만 예외");
        }

        [UnityTest]
        public IEnumerator 파트너_조준은_호스트_시점에서_앞뒤_좌우로_떨어진_자리를_따라간다()
        {
            FurnitureGrabTarget furniture = HostHolds();
            var partner = new FurnitureVirtualPartner { Side = 1f, Front = 0.5f };
            Assert.IsTrue(partner.TryAttach(furniture, HostClientId, AimOrigin, AimDirection), partner.Message);

            // 호스트가 오른쪽(+X)을 본다 → 파트너의 오른쪽은 −Z, 앞은 +X.
            Vector3 eye = new(10f, 1.65f, 20f);
            partner.Tick(eye, Vector3.right);

            Assert.IsTrue(furniture.TryGetHolderAim(1, out ulong id, out Vector3 origin, out Vector3 direction));
            Assert.AreEqual(FurnitureVirtualPartner.ClientId, id);
            Assert.AreEqual(10.5f, origin.x, 1e-4f);
            Assert.AreEqual(19f, origin.z, 1e-4f);
            Assert.AreEqual(1.65f, origin.y, 1e-4f);
            Assert.AreEqual(1f, Vector3.Dot(direction, Vector3.right), 1e-4f);
            partner.Detach();
            yield return null;
        }

        [UnityTest]
        public IEnumerator 파트너를_떼면_호스트가_계속_든_채_1인_상태로_돌아간다()
        {
            FurnitureGrabTarget furniture = HostHolds();
            var partner = new FurnitureVirtualPartner();
            Assert.IsTrue(partner.TryAttach(furniture, HostClientId, AimOrigin, AimDirection), partner.Message);

            partner.Detach();

            Assert.IsFalse(partner.IsAttached);
            Assert.AreEqual(FurnitureState.ThrowReady, furniture.State);
            Assert.AreEqual(1, furniture.HolderCount);
            Assert.IsTrue(furniture.ContainsHolder(HostClientId));
            yield return null;
        }

        [UnityTest]
        public IEnumerator 호스트가_놓으면_발사되고_파트너는_자동으로_정리된다()
        {
            FurnitureGrabTarget furniture = HostHolds();
            var partner = new FurnitureVirtualPartner();
            Assert.IsTrue(partner.TryAttach(furniture, HostClientId, AimOrigin, AimDirection), partner.Message);

            furniture.ServerRelease(HostClientId, AimDirection, false);
            partner.Tick(AimOrigin, AimDirection);

            Assert.IsFalse(partner.IsAttached, "발사되어 남은 홀더가 풀리면 파트너 상태도 정리된다");
            Assert.IsFalse(FurnitureVirtualPartner.IsVirtualHolder(FurnitureVirtualPartner.ClientId));
            Assert.AreEqual(0, furniture.HolderCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 들고_있지_않거나_이미_두_명이면_붙이지_못한다()
        {
            FurnitureGrabTarget idle = SpawnFurniture();
            yield return null;
            var partner = new FurnitureVirtualPartner();

            Assert.IsFalse(partner.TryAttach(idle, HostClientId, AimOrigin, AimDirection), "호스트가 안 든 가구");
            Assert.IsFalse(partner.TryAttach((FurnitureGrabTarget)null, HostClientId, AimOrigin, AimDirection));

            Assert.IsTrue(idle.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection));
            Assert.IsTrue(idle.ServerTryAddHolder(PartnerClientId, AimOrigin + Vector3.right, AimDirection));
            Assert.IsFalse(partner.TryAttach(idle, HostClientId, AimOrigin, AimDirection), "이미 두 명이 듦");
            Assert.IsFalse(partner.IsAttached);
            Assert.IsFalse(FurnitureVirtualPartner.IsVirtualHolder(FurnitureVirtualPartner.ClientId),
                "실패한 붙이기가 가상 홀더 표시를 남기면 안 된다");
        }

        /// <summary>호스트가 방금 가구를 잡은(ThrowReady) 상태.</summary>
        private FurnitureGrabTarget HostHolds()
        {
            FurnitureGrabTarget furniture = SpawnFurniture();
            Assert.IsTrue(furniture.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection));
            Assert.AreEqual(FurnitureState.ThrowReady, furniture.State);
            return furniture;
        }
    }
}
