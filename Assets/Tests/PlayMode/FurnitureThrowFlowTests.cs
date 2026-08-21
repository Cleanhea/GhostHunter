using System.Collections;
using GhostHunter.Gameplay.Furniture;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// 잡기 → 차징 → 발사 상태 기계를 서버 권위 경로로 검증한다.
    /// <c>Assets/Scripts/DebugTools/PrototypeRuntimeSmoke.cs</c> 가 플레이 중에 하던 점검을
    /// 테스트 러너로 옮긴 것이다 → roadmap MIG-7.
    ///
    /// <b>2인 경로에서 yield 하지 않는 이유:</b> 두 번째 홀더(<see cref="PartnerClientId"/>)는
    /// 실제로 접속한 클라이언트가 아니라 플레이어 오브젝트가 없다.
    /// <see cref="FurnitureHoverMotor"/>는 FixedUpdate 마다 홀더의 플레이어 위치를 확인해
    /// 찾지 못하면 강제 해제하므로, 물리 스텝을 사이에 두면 홀더가 사라진다.
    /// 상태 전이는 <see cref="FurnitureGrabTarget"/> 안에서 동기적으로 끝나므로
    /// 같은 프레임에 이어서 검사한다.
    /// </summary>
    public sealed class FurnitureThrowFlowTests : NetworkFurnitureFixture
    {
        private static Rigidbody BodyOf(FurnitureGrabTarget target)
        {
            return target.GetComponent<Rigidbody>();
        }

        [UnityTest]
        public IEnumerator ServerTryAddHolder_한_명이_잡으면_ThrowReady_가_된다()
        {
            FurnitureGrabTarget furniture = SpawnFurniture();
            yield return null;

            Assert.IsTrue(furniture.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection));

            Assert.AreEqual(FurnitureState.ThrowReady, furniture.State);
            Assert.AreEqual(1, furniture.HolderCount);
        }

        /// <summary>
        /// 1인 홀드는 "던질 준비"일 뿐 부양이 아니다. 중력이 꺼지면 가구가 공중에 뜬다.
        /// </summary>
        [UnityTest]
        public IEnumerator ServerTryAddHolder_한_명이_잡아도_중력은_유지된다()
        {
            FurnitureGrabTarget furniture = SpawnFurniture();
            yield return null;

            furniture.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection);

            Assert.IsTrue(BodyOf(furniture).useGravity);
        }

        [UnityTest]
        public IEnumerator ServerTryAddHolder_두_명이_잡으면_Held_가_되고_중력이_꺼진다()
        {
            FurnitureGrabTarget furniture = SpawnFurniture();
            yield return null;

            furniture.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection);
            furniture.ServerTryAddHolder(PartnerClientId, AimOrigin + Vector3.right, AimDirection);

            Assert.AreEqual(FurnitureState.Held, furniture.State);
            Assert.AreEqual(2, furniture.HolderCount);
            Assert.IsFalse(BodyOf(furniture).useGravity);
        }

        [UnityTest]
        public IEnumerator ServerTryAddHolder_같은_클라이언트가_두_슬롯을_차지하지_못한다()
        {
            FurnitureGrabTarget furniture = SpawnFurniture();
            yield return null;

            Assert.IsTrue(furniture.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection));
            Assert.IsFalse(furniture.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection));
            Assert.AreEqual(1, furniture.HolderCount);
        }

        [UnityTest]
        public IEnumerator ServerTryAddHolder_홀더는_두_명을_넘지_못한다()
        {
            FurnitureGrabTarget furniture = SpawnFurniture();
            yield return null;

            furniture.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection);
            furniture.ServerTryAddHolder(PartnerClientId, AimOrigin, AimDirection);

            Assert.IsFalse(furniture.ServerTryAddHolder(1234, AimOrigin, AimDirection));
            Assert.AreEqual(FurnitureGrabTarget.MaxHolders, furniture.HolderCount);
        }

        /// <summary>조준 방향이 0 벡터거나 NaN 이면 홀드 자체를 거부해야 한다.</summary>
        [UnityTest]
        public IEnumerator ServerTryAddHolder_방향이_없으면_거부한다()
        {
            FurnitureGrabTarget furniture = SpawnFurniture();
            yield return null;

            Assert.IsFalse(furniture.ServerTryAddHolder(HostClientId, AimOrigin, Vector3.zero));
            Assert.AreEqual(FurnitureState.Idle, furniture.State);
        }

        [UnityTest]
        public IEnumerator ServerRelease_한_명이_놓으면_발사되어_속도를_얻는다()
        {
            FurnitureGrabTarget furniture = SpawnFurniture();
            yield return null;

            furniture.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection);
            furniture.ServerRelease(HostClientId, AimDirection, false);

            yield return new WaitForFixedUpdate();

            Assert.AreEqual(FurnitureState.Launched, furniture.State);
            Assert.Greater(BodyOf(furniture).linearVelocity.magnitude, 5f);
        }

        /// <summary>
        /// 두 명이 들고 있다가 한 명이 손을 떼면, 남은 한 명의 "던질 준비"로 돌아가야 한다.
        /// (LaunchOnFirstRelease 가 꺼져 있을 때의 기본 정책)
        /// </summary>
        [UnityTest]
        public IEnumerator ServerRelease_두_명_중_첫_해제는_ThrowReady_로_돌아간다()
        {
            FurnitureGrabTarget furniture = SpawnFurniture();
            yield return null;

            Assume.That(Settings.LaunchOnFirstRelease, Is.False);

            furniture.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection);
            furniture.ServerTryAddHolder(PartnerClientId, AimOrigin + Vector3.right, AimDirection);
            furniture.ServerRelease(HostClientId, AimDirection, false);

            Assert.AreEqual(FurnitureState.ThrowReady, furniture.State);
            Assert.AreEqual(1, furniture.HolderCount);
            Assert.IsTrue(BodyOf(furniture).useGravity);
        }

        [UnityTest]
        public IEnumerator ServerRelease_마지막_홀더가_놓으면_발사된다()
        {
            FurnitureGrabTarget furniture = SpawnFurniture();
            yield return null;

            furniture.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection);
            furniture.ServerTryAddHolder(PartnerClientId, AimOrigin + Vector3.right, AimDirection);
            furniture.ServerRelease(HostClientId, AimDirection, false);
            furniture.ServerRelease(PartnerClientId, AimDirection, false);

            Assert.AreEqual(FurnitureState.Launched, furniture.State);

            yield return new WaitForFixedUpdate();

            Assert.Greater(BodyOf(furniture).linearVelocity.sqrMagnitude, 0.01f);
        }

        /// <summary>
        /// 접속이 끊긴 홀더를 정리하는 경로. 사고로 끊긴 사람이 가구를 던져 주면 안 된다.
        /// </summary>
        [UnityTest]
        public IEnumerator ServerForceRelease_강제_해제는_발사하지_않는다()
        {
            FurnitureGrabTarget furniture = SpawnFurniture();
            yield return null;

            furniture.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection);
            furniture.ServerForceRelease(HostClientId);

            Assert.AreEqual(FurnitureState.Idle, furniture.State);
            Assert.AreEqual(0, furniture.HolderCount);
        }

        [UnityTest]
        public IEnumerator CanGrab_발사된_가구는_다시_잡을_수_없다()
        {
            FurnitureGrabTarget furniture = SpawnFurniture();
            yield return null;

            furniture.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection);
            furniture.ServerRelease(HostClientId, AimDirection, false);

            Assert.AreEqual(FurnitureState.Launched, furniture.State);
            Assert.IsFalse(furniture.CanGrab(HostClientId));
            Assert.IsFalse(furniture.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection));
        }

        /// <summary>
        /// 차징은 첫 홀더가 붙은 순간부터 흐른다. 값 자체보다 "0 에서 시작해 늘어난다"를 본다.
        /// </summary>
        [UnityTest]
        public IEnumerator Charge_홀드하는_동안_0에서_증가한다()
        {
            FurnitureGrabTarget furniture = SpawnFurniture();
            yield return null;

            furniture.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection);
            Assert.AreEqual(0f, furniture.Charge, 0.0001f);

            yield return new WaitForSeconds(Settings.ChargeTime * 0.5f);

            Assert.Greater(furniture.Charge, 0f);
            Assert.LessOrEqual(furniture.Charge, 1f);
        }
    }
}
