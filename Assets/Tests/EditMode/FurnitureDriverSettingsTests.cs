using GhostHunter.Gameplay.FurnitureDriver;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>행동 시간 임계값(기획서 §3.2 — 내구도 &gt; 15면 3초, ≤ 15면 9초) 경계를 검증한다.</summary>
    public sealed class FurnitureDriverSettingsTests
    {
        private FurnitureDriverSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<FurnitureDriverSettings>();
            _settings.Configure(3f, 9f, 15, 100, 5, 3f, 0.2f, 1f);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_settings);

        [Test]
        public void 내구도가_임계값보다_크면_3초다()
        {
            Assert.AreEqual(3f, _settings.ActionSecondsFor(16));
        }

        [Test]
        public void 내구도가_임계값과_같으면_9초다()
        {
            Assert.AreEqual(9f, _settings.ActionSecondsFor(15));
        }

        [Test]
        public void 내구도가_임계값보다_낮으면_9초다()
        {
            Assert.AreEqual(9f, _settings.ActionSecondsFor(1));
        }
    }
}
