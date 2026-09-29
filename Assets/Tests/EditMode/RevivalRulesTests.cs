using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Revival;
using NUnit.Framework;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>부활 의식 판정(revival-system.md §6·§8) — 타이밍 성공/늦음, 지연 봐주기, 정상/폐급/저주 결과.</summary>
    public sealed class RevivalRulesTests
    {
        private const float ZoneStart = 0.5f;
        private const float ZoneWidth = 0.2f;

        [Test]
        public void 초록_구간_안에서_누르면_성공이다()
        {
            Assert.IsTrue(RevivalRules.IsTimingSuccess(0.5f, ZoneStart, ZoneWidth), "구간 시작");
            Assert.IsTrue(RevivalRules.IsTimingSuccess(0.6f, ZoneStart, ZoneWidth));
            Assert.IsTrue(RevivalRules.IsTimingSuccess(0.7f, ZoneStart, ZoneWidth), "구간 끝");
        }

        [Test]
        public void 구간을_지나서_누르거나_안_누르면_늦음_실패다()
        {
            Assert.IsFalse(RevivalRules.IsTimingSuccess(0.71f, ZoneStart, ZoneWidth), "늦음");
            Assert.IsFalse(RevivalRules.IsTimingSuccess(-1f, ZoneStart, ZoneWidth), "끝까지 안 누름");
        }

        [Test]
        public void 서버_시간보다_이른_누름_보고는_믿지_않는다()
        {
            Assert.IsTrue(RevivalRules.IsPlausiblePress(0.5f, 0.75f, 1.5f, 0.4f), "0.75초에 50%");
            Assert.IsTrue(RevivalRules.IsPlausiblePress(0.5f, 0.4f, 1.5f, 0.4f), "지연 0.35초는 봐준다");
            Assert.IsFalse(RevivalRules.IsPlausiblePress(0.9f, 0.2f, 1.5f, 0.4f), "0.2초 만에 90% 는 불가능");
            Assert.IsFalse(RevivalRules.IsPlausiblePress(1.5f, 10f, 1.5f, 0.4f), "바 밖");
            Assert.IsTrue(RevivalRules.IsPlausiblePress(-1f, 0f, 1.5f, 0.4f), "안 누름은 언제나 가능");
        }

        [Test]
        public void 실패가_없으면_정상_부활이다()
        {
            Assert.AreEqual(DefectKind.None, RevivalRules.Judge(0, 0f, 0));
        }

        [Test]
        public void 실패가_하나라도_있으면_98퍼센트_폐급_2퍼센트_저주다()
        {
            Assert.AreEqual(DefectKind.CursedRandomMove, RevivalRules.Judge(1, 0.019f, 0), "2% 미만");
            Assert.AreNotEqual(DefectKind.CursedRandomMove, RevivalRules.Judge(1, 0.02f, 0), "2% 이상은 일반 폐급");
            Assert.AreNotEqual(DefectKind.None, RevivalRules.Judge(5, 0.99f, 3));
        }

        [Test]
        public void 일반_폐급은_4종이_모두_나온다()
        {
            var seen = new System.Collections.Generic.HashSet<DefectKind>();
            for (int variant = 0; variant < 8; variant++)
                seen.Add(RevivalRules.Judge(1, 0.5f, variant));

            CollectionAssert.AreEquivalent(new[]
            {
                DefectKind.Follower, DefectKind.Screamer, DefectKind.InvertedKeys, DefectKind.VoiceModulated,
            }, seen);
            Assert.AreNotEqual(DefectKind.None, RevivalRules.Judge(1, 0.5f, -7), "음수 굴림도 범위 안");
        }

        [Test]
        public void 효과가_들어간_폐급은_키_반대와_저주다()
        {
            Assert.IsTrue(RevivalRules.HasEffect(DefectKind.InvertedKeys));
            Assert.IsTrue(RevivalRules.HasEffect(DefectKind.CursedRandomMove));
            Assert.IsFalse(RevivalRules.HasEffect(DefectKind.Follower));
            Assert.IsFalse(RevivalRules.HasEffect(DefectKind.Screamer));
            Assert.IsFalse(RevivalRules.HasEffect(DefectKind.VoiceModulated));
        }
    }
}
