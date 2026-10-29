using GhostHunter.Systems.Audio;
using NUnit.Framework;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>동시 재생 한도에서 새 소리가 어느 칸을 쓰는지, 그리고 벽 가림 계산.</summary>
    public sealed class SoundVoiceAllocatorTests
    {
        private static SoundVoiceSlot Busy(int priority, float distance, double startTime = 0) =>
            new() { Busy = true, Priority = priority, Distance = distance, StartTime = startTime };

        [Test]
        public void 빈_칸이_있으면_첫_빈_칸이다()
        {
            var slots = new[] { Busy(128, 5f), new SoundVoiceSlot(), new SoundVoiceSlot() };

            Assert.AreEqual(1, SoundVoiceAllocator.Choose(slots, 255, 99f));
        }

        [Test]
        public void 가득_차면_가장_덜_중요한_소리를_밀어낸다()
        {
            var slots = new[] { Busy(50, 1f), Busy(200, 1f), Busy(100, 1f) };

            Assert.AreEqual(1, SoundVoiceAllocator.Choose(slots, 10, 1f));
        }

        [Test]
        public void 우선순위가_같으면_더_먼_소리가_밀린다()
        {
            var slots = new[] { Busy(128, 3f), Busy(128, 20f), Busy(128, 8f) };

            Assert.AreEqual(1, SoundVoiceAllocator.Choose(slots, 128, 2f));
        }

        [Test]
        public void 우선순위와_거리가_같으면_오래된_소리가_밀린다()
        {
            var slots = new[] { Busy(128, 5f, 10), Busy(128, 5f, 2), Busy(128, 5f, 7) };

            Assert.AreEqual(1, SoundVoiceAllocator.Choose(slots, 128, 4f));
        }

        [Test]
        public void 새_소리가_더_중요하지_않으면_버린다()
        {
            var slots = new[] { Busy(50, 1f), Busy(60, 1f) };

            Assert.AreEqual(-1, SoundVoiceAllocator.Choose(slots, 100, 0f));
            Assert.AreEqual(-1, SoundVoiceAllocator.Choose(slots, 60, 5f));
        }

        [Test]
        public void 벽이_없으면_가림이_없다()
        {
            Assert.AreEqual(1f, SoundOcclusion.Gain(0, 0.5f, 3, 0.1f));
            Assert.AreEqual(22000f, SoundOcclusion.Cutoff(0, 22000f, 900f, 0.4f, 3));
        }

        [Test]
        public void 벽_한_겹마다_음량과_차단_주파수가_줄고_바닥값에서_멈춘다()
        {
            Assert.AreEqual(0.5f, SoundOcclusion.Gain(1, 0.5f, 3, 0.1f), 1e-5f);
            Assert.AreEqual(0.25f, SoundOcclusion.Gain(2, 0.5f, 3, 0.1f), 1e-5f);
            Assert.AreEqual(0.125f, SoundOcclusion.Gain(3, 0.5f, 3, 0.1f), 1e-5f);
            Assert.AreEqual(0.2f, SoundOcclusion.Gain(3, 0.5f, 3, 0.2f), 1e-5f);
            Assert.AreEqual(8800f, SoundOcclusion.Cutoff(1, 22000f, 900f, 0.4f, 3), 1e-2f);
            Assert.AreEqual(2000f, SoundOcclusion.Cutoff(3, 22000f, 2000f, 0.4f, 3));
        }

        [Test]
        public void 최대_벽_수를_넘는_벽은_세지_않는다()
        {
            Assert.AreEqual(SoundOcclusion.Gain(3, 0.9f, 3, 0f), SoundOcclusion.Gain(8, 0.9f, 3, 0f), 1e-5f);
        }
    }
}
