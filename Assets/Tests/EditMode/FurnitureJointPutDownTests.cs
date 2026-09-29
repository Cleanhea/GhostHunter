using GhostHunter.Gameplay.Furniture;
using NUnit.Framework;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>2인 잡기 '같이 내려놓기' 판정 — 먼저 놓은 뒤 2.5초 안에 마지막 홀더가 놓으면 발사하지 않는다.</summary>
    public sealed class FurnitureJointPutDownTests
    {
        private const float FirstReleaseAt = 10f;
        private const float Window = 2.5f;
        private const float PutDownUntil = FirstReleaseAt + Window;

        [Test]
        public void 먼저_놓은_뒤_창_안에_마지막_홀더가_놓으면_내려놓기다()
        {
            Assert.IsTrue(FurnitureGrabTarget.IsJointPutDown(1, FirstReleaseAt, PutDownUntil), "같은 순간");
            Assert.IsTrue(FurnitureGrabTarget.IsJointPutDown(1, FirstReleaseAt + 2.4f, PutDownUntil));
            Assert.IsTrue(FurnitureGrabTarget.IsJointPutDown(1, PutDownUntil, PutDownUntil), "경계 포함");
        }

        [Test]
        public void 창이_지나서_놓으면_평소처럼_던진다()
        {
            Assert.IsFalse(FurnitureGrabTarget.IsJointPutDown(1, FirstReleaseAt + 2.6f, PutDownUntil));
        }

        [Test]
        public void 혼자_잡았다_놓은_것은_내려놓기가_아니다()
        {
            Assert.IsFalse(FurnitureGrabTarget.IsJointPutDown(1, FirstReleaseAt, float.NegativeInfinity),
                "2인 잡기에서 먼저 놓은 사람이 없으면 창이 열리지 않는다");
        }

        [Test]
        public void 둘이_들고_있을_때_첫_해제는_판정_대상이_아니다()
        {
            Assert.IsFalse(FurnitureGrabTarget.IsJointPutDown(2, FirstReleaseAt, PutDownUntil));
        }
    }
}
