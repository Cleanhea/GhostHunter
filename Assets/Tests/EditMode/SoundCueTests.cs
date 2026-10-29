using GhostHunter.Core.Audio;
using GhostHunter.Data.Audio;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>큐 식별자·클립 선택·흔들림과 라이브러리 검사.</summary>
    public sealed class SoundCueTests
    {
        private SoundCue _cue;

        [TearDown]
        public void TearDown()
        {
            if (_cue != null)
                Object.DestroyImmediate(_cue);
        }

        [Test]
        public void 식별자는_같은_키에서_항상_같고_0이_아니다()
        {
            uint first = SoundCueId.FromKey("furniture.impact.wood");

            Assert.AreEqual(first, SoundCueId.FromKey("furniture.impact.wood"));
            Assert.AreNotEqual(SoundCueId.None, first);
            Assert.AreNotEqual(first, SoundCueId.FromKey("furniture.impact.metal"));
        }

        [Test]
        public void 빈_키는_식별자_없음이다()
        {
            Assert.AreEqual(SoundCueId.None, SoundCueId.FromKey(null));
            Assert.AreEqual(SoundCueId.None, SoundCueId.FromKey(""));
        }

        [Test]
        public void 키를_비우면_에셋_이름이_키다()
        {
            _cue = AudioTestFactory.CreateCue("SFX_Test");
            AudioTestFactory.Set(_cue, "_key", " ");

            Assert.AreEqual("SFX_Test", _cue.Key);
            Assert.AreEqual(SoundCueId.FromKey("SFX_Test"), _cue.Id);
        }

        [Test]
        public void 클립이_둘_이상이면_직전_클립을_다시_고르지_않는다()
        {
            _cue = AudioTestFactory.CreateCue("pick", 3);

            for (int previous = 0; previous < 3; previous++)
                for (float random = 0f; random < 1f; random += 0.1f)
                    Assert.AreNotEqual(previous, _cue.PickClipIndex(previous, random));
        }

        [Test]
        public void 클립이_하나면_직전과_같아도_그_클립이다()
        {
            _cue = AudioTestFactory.CreateCue("single", 1);

            Assert.AreEqual(0, _cue.PickClipIndex(0, 0.5f));
        }

        [Test]
        public void 클립이_없으면_고를_수_없다()
        {
            _cue = AudioTestFactory.CreateCue("empty", 0);

            Assert.AreEqual(-1, _cue.PickClipIndex(-1, 0.5f));
            Assert.IsFalse(_cue.HasClips);
        }

        [Test]
        public void 비어_있는_칸은_건너뛴다()
        {
            _cue = AudioTestFactory.CreateCue("gap", 3);
            var clips = new AudioClip[] { null, AudioTestFactory.CreateClip(), null };
            AudioTestFactory.Set(_cue, "_clips", clips);

            Assert.AreEqual(1, _cue.PickClipIndex(-1, 0.9f));
        }

        [Test]
        public void 흔들림은_기준값_위아래_범위를_벗어나지_않는다()
        {
            _cue = AudioTestFactory.CreateCue("jitter");
            AudioTestFactory.Set(_cue, "_volume", 0.6f);
            AudioTestFactory.Set(_cue, "_volumeJitter", 0.2f);
            AudioTestFactory.Set(_cue, "_pitch", 1f);
            AudioTestFactory.Set(_cue, "_pitchJitter", 0.1f);

            Assert.AreEqual(0.4f, _cue.ResolveVolume(0f), 1e-5f);
            Assert.AreEqual(0.6f, _cue.ResolveVolume(0.5f), 1e-5f);
            Assert.AreEqual(0.8f, _cue.ResolveVolume(1f), 1e-5f);
            Assert.AreEqual(0.9f, _cue.ResolvePitch(0f), 1e-5f);
            Assert.AreEqual(1.1f, _cue.ResolvePitch(1f), 1e-5f);
        }

        [Test]
        public void 라이브러리는_식별자로_큐를_찾는다()
        {
            _cue = AudioTestFactory.CreateCue("lib.a");
            SoundLibrary library = AudioTestFactory.CreateLibrary(_cue);

            Assert.IsTrue(library.TryGet(_cue.Id, out SoundCue found));
            Assert.AreSame(_cue, found);
            Assert.IsFalse(library.TryGet(SoundCueId.FromKey("lib.missing"), out _));
            Object.DestroyImmediate(library);
        }

        [Test]
        public void 라이브러리는_빈_칸_중복_키_클립_없음을_알린다()
        {
            _cue = AudioTestFactory.CreateCue("dup");
            SoundCue duplicate = AudioTestFactory.CreateCue("dup");
            SoundCue silent = AudioTestFactory.CreateCue("silent", 0);
            SoundLibrary library = AudioTestFactory.CreateLibrary(_cue, duplicate, silent, null);

            // 중복 1 + 클립 없음 1 + 빈 칸 1.
            Assert.AreEqual(3, library.FindProblems().Count);
            Object.DestroyImmediate(library);
            Object.DestroyImmediate(duplicate);
            Object.DestroyImmediate(silent);
        }
    }
}
