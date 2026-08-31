using System.Collections.Generic;
using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 초자연현상 선정 루프(기획서 §6.1·§6.4)를 검증한다. 상태·팀 평균 정신력·청소 방해 구간을
    /// 입력으로 주고 발생 주기, 평상시·활동에서만 발생하는지, 직전 현상 제외, 소리 현상 Pool
    /// 게이트를 확인한다. <see cref="GhostPhenomenaDirector"/> 는 순수 로직이라 EditMode 로 돈다.
    /// </summary>
    public sealed class GhostPhenomenaDirectorTests
    {
        private GhostPrototypeSettings _settings;
        private double _roll;
        private GhostPhenomenaDirector _director;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<GhostPrototypeSettings>();
            _roll = 0d;
            _director = new GhostPhenomenaDirector(_settings, () => _roll);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_settings);
            _settings = null;
            _director = null;
        }

        private void SetSoundEnabled(bool enabled)
        {
            var serialized = new SerializedObject(_settings);
            serialized.FindProperty("_soundPhenomenaEnabled").boolValue = enabled;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Pool 은 생성자에서 굳으므로 설정을 바꾼 뒤 새로 만든다.
            _director = new GhostPhenomenaDirector(_settings, () => _roll);
        }

        /// <summary>초기 평상시 주기를 태워 첫 발생을 일으키고, 이후 주기를 정상화한다.</summary>
        private void PrimeFirstFire(GhostPhase phase, int teamSanity, bool boost = false)
        {
            GhostPhenomenonKind fired = _director.Tick(1000f, phase, teamSanity, boost);
            Assert.AreNotEqual(GhostPhenomenonKind.None, fired, "테스트 준비: 첫 현상 발생 실패");
        }

        [Test]
        public void 평상시에는_긴_주기_뒤에_발생한다()
        {
            Assert.AreEqual(
                GhostPhenomenonKind.None,
                _director.Tick(_settings.PhenomenaIdleInterval - 1f, GhostPhase.Idle, 100, false));

            Assert.AreNotEqual(
                GhostPhenomenonKind.None,
                _director.Tick(2f, GhostPhase.Idle, 100, false));
        }

        [Test]
        public void 활동_주기는_평상시보다_짧다()
        {
            PrimeFirstFire(GhostPhase.Active, 100);
            Assert.AreEqual(_settings.PhenomenaActiveInterval, _director.SecondsUntilNext, 0.001f);
            Assert.Less(_settings.PhenomenaActiveInterval, _settings.PhenomenaIdleInterval);
        }

        [Test]
        public void 팀_평균_30_이하면_주기가_더_짧다()
        {
            PrimeFirstFire(GhostPhase.Active, 20);
            Assert.AreEqual(_settings.PhenomenaHighRiskInterval, _director.SecondsUntilNext, 0.001f);
        }

        [Test]
        public void 청소_방해_구간이면_주기가_짧다()
        {
            PrimeFirstFire(GhostPhase.Active, 100, boost: true);
            Assert.AreEqual(_settings.PhenomenaHighRiskInterval, _director.SecondsUntilNext, 0.001f);
        }

        [TestCase(GhostPhase.Warning)]
        [TestCase(GhostPhase.Attack)]
        [TestCase(GhostPhase.Calming)]
        [TestCase(GhostPhase.Suppressed)]
        public void 경고_어택_진정_상태에서는_발생하지_않는다(GhostPhase phase)
        {
            Assert.AreEqual(
                GhostPhenomenonKind.None,
                _director.Tick(1000f, phase, 10, true));
        }

        [Test]
        public void 직전_현상은_연속으로_나오지_않는다()
        {
            double[] rolls = { 0.0, 0.19, 0.4, 0.6, 0.85, 0.99, 0.5, 0.33, 0.72, 0.11 };
            PrimeFirstFire(GhostPhase.Active, 100);

            GhostPhenomenonKind previous = _director.Last;
            for (int i = 0; i < 40; i++)
            {
                _roll = rolls[i % rolls.Length];
                GhostPhenomenonKind fired = _director.Tick(
                    _settings.PhenomenaActiveInterval + 0.01f,
                    GhostPhase.Active,
                    100,
                    false);

                Assert.AreNotEqual(GhostPhenomenonKind.None, fired, $"{i}번째 현상이 발생하지 않았습니다");
                Assert.AreNotEqual(previous, fired, $"{i}번째 현상이 직전과 같습니다");
                previous = fired;
            }
        }

        [Test]
        public void 소리_현상은_기본값에서_선택되지_않는다()
        {
            PrimeFirstFire(GhostPhase.Active, 100);

            var seen = new HashSet<GhostPhenomenonKind>();
            for (int i = 0; i < 80; i++)
            {
                _roll = i / 80d;
                seen.Add(_director.Tick(
                    _settings.PhenomenaActiveInterval + 0.01f,
                    GhostPhase.Active,
                    100,
                    false));
            }

            Assert.IsFalse(seen.Contains(GhostPhenomenonKind.WallKnock));
            Assert.IsFalse(seen.Contains(GhostPhenomenonKind.Footsteps));
            Assert.IsTrue(seen.Contains(GhostPhenomenonKind.LightFlicker));
        }

        [Test]
        public void 소리_현상을_켜면_선택_Pool에_들어온다()
        {
            SetSoundEnabled(true);
            PrimeFirstFire(GhostPhase.Active, 100);

            var seen = new HashSet<GhostPhenomenonKind>();
            for (int i = 0; i < 200; i++)
            {
                _roll = (i * 7 % 100) / 100d;
                seen.Add(_director.Tick(
                    _settings.PhenomenaActiveInterval + 0.01f,
                    GhostPhase.Active,
                    100,
                    false));
            }

            Assert.IsTrue(
                seen.Contains(GhostPhenomenonKind.WallKnock)
                || seen.Contains(GhostPhenomenonKind.Footsteps),
                "소리 현상을 켰는데 Pool 에 들어오지 않았습니다");
        }

        [Test]
        public void 강제_실행은_주기를_무시하고_현상을_뽑는다()
        {
            GhostPhenomenonKind fired = _director.ForceNext(GhostPhase.Idle, 100, false);
            Assert.AreNotEqual(GhostPhenomenonKind.None, fired);
            Assert.AreEqual(fired, _director.Last);
        }
    }
}
