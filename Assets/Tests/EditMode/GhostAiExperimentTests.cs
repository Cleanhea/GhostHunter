using System.Reflection;
using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    public sealed class GhostAiExperimentTests
    {
        private GhostPrototypeSettings _settings;

        [SetUp]
        public void SetUp() => _settings = ScriptableObject.CreateInstance<GhostPrototypeSettings>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_settings);

        [Test]
        public void 마지막_관측은_외부_좌표_없이_시간에_따라_만료된다()
        {
            var memory = new GhostTrackingMemory();
            memory.Observe(1, Vector3.forward * 4f, 10f, 8f);
            Assert.AreEqual(0.5f, memory.Confidence(14f, 8f), 0.001f);
            Assert.AreEqual(0f, memory.Confidence(18f, 8f));
            Assert.AreEqual(Vector3.forward * 4f, memory.Position);
        }

        [Test]
        public void 예측은_평면_속력과_최대_거리를_제한한다()
        {
            var memory = new GhostTrackingMemory();
            memory.Observe(1, Vector3.zero, 0f, 8f);
            memory.Observe(1, new Vector3(100f, 3f, 0f), 1f, 8f);
            Assert.AreEqual(new Vector3(8f, 0f, 0f), memory.Velocity);
            Assert.AreEqual(new Vector3(102f, 3f, 0f), memory.Predict(1f, 8f, 10f, 2f));
        }

        [Test]
        public void 타깃을_바꾸면_이전_플레이어_속도를_물려받지_않는다()
        {
            var memory = new GhostTrackingMemory();
            memory.Observe(1, Vector3.zero, 0f, 8f);
            memory.Observe(1, Vector3.forward, 1f, 8f);
            memory.Observe(2, Vector3.right * 20f, 2f, 8f);
            Assert.AreEqual(Vector3.zero, memory.Velocity);
        }

        [Test]
        public void 단서가_만료되면_예측은_마지막_위치로_돌아간다()
        {
            var memory = new GhostTrackingMemory();
            memory.Observe(1, Vector3.zero, 0f, 8f);
            memory.Observe(1, Vector3.forward, 1f, 8f);
            Assert.AreEqual(memory.Position, memory.Predict(10f, 8f, 0.6f, 2.5f));
        }

        [Test]
        public void 수색은_도주_방향을_우선하지만_단서가_만료되면_차이가_없다()
        {
            float forward = GhostTrackingMemory.SearchScore(Vector3.forward, Vector3.zero,
                Vector3.forward, 1f, 1f, 1f, _settings);
            float back = GhostTrackingMemory.SearchScore(Vector3.back, Vector3.zero,
                Vector3.forward, 1f, 1f, 1f, _settings);
            Assert.Greater(forward, back);
            Assert.AreEqual(
                GhostTrackingMemory.SearchScore(Vector3.forward, Vector3.zero,
                    Vector3.forward, 0f, 1f, 1f, _settings),
                GhostTrackingMemory.SearchScore(Vector3.back, Vector3.zero,
                    Vector3.forward, 0f, 1f, 1f, _settings));
        }

        [Test]
        public void 수색은_같은_거리라면_방금_확인한_곳보다_미확인_곳을_우선한다()
        {
            float visited = GhostTrackingMemory.SearchScore(Vector3.right, Vector3.zero,
                Vector3.zero, 0f, 0f, 3f, _settings);
            float fresh = GhostTrackingMemory.SearchScore(Vector3.left, Vector3.zero,
                Vector3.zero, 0f, 1f, 3f, _settings);
            Assert.Greater(fresh, visited);
        }

        [Test]
        public void 전체_OFF는_개별_ON보다_우선하고_다시_ON하면_선택을_유지한다()
        {
            Set("_aiExperimentsEnabled", true);
            Set("_predictiveChaseEnabled", false);
            Set("_aiExperimentsEnabled", false);
            Assert.IsFalse(_settings.EvidenceTrackingEnabled);
            Assert.IsFalse(_settings.UtilitySearchEnabled);
            Assert.IsFalse(_settings.ImpactInvestigationEnabled);
            Assert.IsFalse(_settings.TensionPacingEnabled);
            Set("_aiExperimentsEnabled", true);
            Assert.IsTrue(_settings.EvidenceTrackingEnabled);
            Assert.IsFalse(_settings.PredictiveChaseEnabled);
        }

        [Test]
        public void 공포_간격_OFF는_플레이_중_즉시_기본_속도로_돌아간다()
        {
            Set("_aiExperimentsEnabled", true);
            var director = new GhostPhenomenaDirector(_settings, () => 0d);
            director.SetTension(1f);
            director.Tick(4f, GhostPhase.Active, 100, false);
            Assert.AreEqual(_settings.PhenomenaIdleInterval - 2f, director.SecondsUntilNext, 0.001f);
            Set("_tensionPacingEnabled", false);
            director.Tick(4f, GhostPhase.Active, 100, false);
            Assert.AreEqual(_settings.PhenomenaIdleInterval - 6f, director.SecondsUntilNext, 0.001f);
        }

        [Test]
        public void 공포_간격을_켜도_강제_현상은_즉시_발생한다()
        {
            Set("_aiExperimentsEnabled", true);
            var director = new GhostPhenomenaDirector(_settings, () => 0d);
            director.SetTension(1f);
            Assert.AreNotEqual(GhostPhenomenonKind.None,
                director.ForceNext(GhostPhase.Active, 100, false));
        }

        [Test]
        public void 수색_기억은_크기가_제한되고_새_수색에서_초기화된다()
        {
            var memory = new GhostSearchMemory(3f, 10f);
            for (int i = 0; i < 100; i++)
                memory.MarkVisited(Vector3.right * (i * 3f), i);
            Assert.AreEqual(32, memory.Count);
            Assert.AreEqual(0f, memory.Staleness(Vector3.right * 297f, 99f));
            Assert.AreEqual(10f, memory.Staleness(Vector3.zero, 99f));
            memory.Clear();
            Assert.AreEqual(10f, memory.Staleness(Vector3.right * 297f, 99f));
        }

        private void Set(string name, object value)
            => typeof(GhostPrototypeSettings).GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_settings, value);
    }
}
