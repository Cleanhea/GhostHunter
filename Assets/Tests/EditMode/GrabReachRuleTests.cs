using GhostHunter.Gameplay.Interaction;
using NUnit.Framework;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 서버 잡기 사거리 규칙 — 클라이언트 타게팅과 같은 기준(시선 원점 → 가구 표면)으로 잰다.
    /// 흐름 검증은 PlayMode <c>GrabReachFlowTests</c>.
    /// </summary>
    public sealed class GrabReachRuleTests
    {
        private const float MaxTargetDistance = 2f;

        [Test]
        public void 표면이_타게팅_사거리_안이면_통과한다()
        {
            Assert.IsTrue(GrabController.IsWithinGrabReach(0.2f, 1.9f, MaxTargetDistance));
        }

        [Test]
        public void 지연_보정_여유는_사거리의_1_2배까지다()
        {
            Assert.IsTrue(GrabController.IsWithinGrabReach(0.2f, 2.4f, MaxTargetDistance),
                "클라이언트는 2m 에서 맞혔는데 서버에 도착하는 사이 움직인 만큼은 봐 준다.");
            Assert.IsFalse(GrabController.IsWithinGrabReach(0.2f, 2.41f, MaxTargetDistance));
        }

        [Test]
        public void 표면이_멀면_거부한다()
        {
            Assert.IsFalse(GrabController.IsWithinGrabReach(0.2f, 5f, MaxTargetDistance));
        }

        [Test]
        public void 조준_원점이_플레이어에서_멀면_표면이_가까워도_거부한다()
        {
            Assert.IsTrue(GrabController.IsWithinGrabReach(GrabController.AimOriginMaxOffset, 1f, MaxTargetDistance));
            Assert.IsFalse(GrabController.IsWithinGrabReach(GrabController.AimOriginMaxOffset + 0.01f, 1f, MaxTargetDistance),
                "클라이언트가 보낸 원점을 속여 사거리를 늘릴 수 없다.");
        }
    }
}
