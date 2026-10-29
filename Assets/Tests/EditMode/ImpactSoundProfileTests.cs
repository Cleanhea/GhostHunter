using System;
using GhostHunter.Core.Audio;
using GhostHunter.Data.Audio;
using GhostHunter.Gameplay.Audio;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>충돌 속력 → 충돌음 큐·음량 결정과 소리 알림 음량 압축.</summary>
    public sealed class ImpactSoundProfileTests
    {
        private ImpactSoundProfile _profile;
        private SoundCue _light;
        private SoundCue _heavy;

        [SetUp]
        public void SetUp()
        {
            _light = AudioTestFactory.CreateCue("impact.light");
            _heavy = AudioTestFactory.CreateCue("impact.heavy");
            _profile = ScriptableObject.CreateInstance<ImpactSoundProfile>();
            AudioTestFactory.Set(_profile, "_minimumSpeed", 1f);
            AudioTestFactory.Set(_profile, "_fullVolumeSpeed", 9f);
            AudioTestFactory.Set(_profile, "_minimumVolume", 0.2f);
            AudioTestFactory.Set(_profile, "_tiers", MakeTiers(_light, 1f, _heavy, 6f));
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_profile);
            UnityEngine.Object.DestroyImmediate(_light);
            UnityEngine.Object.DestroyImmediate(_heavy);
        }

        private static Array MakeTiers(SoundCue first, float firstSpeed, SoundCue second, float secondSpeed)
        {
            var tiers = new ImpactSoundProfile.Tier[2];
            tiers[0] = new ImpactSoundProfile.Tier { MinimumSpeed = firstSpeed, Cue = first };
            tiers[1] = new ImpactSoundProfile.Tier { MinimumSpeed = secondSpeed, Cue = second };
            return tiers;
        }

        [Test]
        public void 최소_속력보다_느리면_소리가_없다()
        {
            Assert.IsFalse(_profile.TryEvaluate(0.5f, out _, out _));
        }

        [Test]
        public void 속력에_맞는_가장_센_단계의_큐를_쓴다()
        {
            Assert.IsTrue(_profile.TryEvaluate(3f, out SoundCue slow, out _));
            Assert.AreSame(_light, slow);
            Assert.IsTrue(_profile.TryEvaluate(6f, out SoundCue fast, out _));
            Assert.AreSame(_heavy, fast);
        }

        [Test]
        public void 단계_순서가_뒤섞여도_가장_센_단계가_이긴다()
        {
            AudioTestFactory.Set(_profile, "_tiers", MakeTiers(_heavy, 6f, _light, 1f));

            Assert.IsTrue(_profile.TryEvaluate(8f, out SoundCue cue, out _));
            Assert.AreSame(_heavy, cue);
        }

        [Test]
        public void 음량은_최소_속력에서_최소값이고_최대_속력에서_1이다()
        {
            _profile.TryEvaluate(1f, out _, out float atMinimum);
            _profile.TryEvaluate(9f, out _, out float atFull);
            _profile.TryEvaluate(30f, out _, out float beyond);

            Assert.AreEqual(0.2f, atMinimum, 1e-5f);
            Assert.AreEqual(1f, atFull, 1e-5f);
            Assert.AreEqual(1f, beyond, 1e-5f);
        }

        [Test]
        public void 맞는_단계가_없으면_소리가_없다()
        {
            AudioTestFactory.Set(_profile, "_tiers", new ImpactSoundProfile.Tier[0]);

            Assert.IsFalse(_profile.TryEvaluate(5f, out _, out _));
        }

        [Test]
        public void NaN_속력은_소리가_없다()
        {
            Assert.IsFalse(_profile.TryEvaluate(float.NaN, out _, out _));
        }

        [Test]
        public void 음량_압축은_범위를_자르고_오차가_한_단계_이하다()
        {
            Assert.AreEqual(0, NetworkSoundCodec.QuantizeVolume(-1f));
            Assert.AreEqual(255, NetworkSoundCodec.QuantizeVolume(5f));
            for (float v = 0f; v <= 1f; v += 0.037f)
                Assert.AreEqual(v, NetworkSoundCodec.DequantizeVolume(NetworkSoundCodec.QuantizeVolume(v)), 1f / 255f);
        }
    }
}
