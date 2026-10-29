using System.Collections.Generic;
using GhostHunter.Core.Audio;
using GhostHunter.Data.Audio;
using GhostHunter.Systems.Audio;
using GhostHunter.Systems.Settings;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>오디오 서비스의 재생 거절 조건·슬롯 재사용·버스 음량.</summary>
    public sealed class AudioServiceTests
    {
        private GameObject _host;
        private GameObject _listenerObject;
        private AudioService _service;
        private GameAudioSettings _settings;
        private SoundLibrary _library;
        private SoundCue _cue;
        private AudioListener[] _foreignListeners;

        [SetUp]
        public void SetUp()
        {
            // 테스트 씬에 이미 있는 리스너(카메라 등)가 거리 판정에 끼지 않게 잠시 끈다.
            _foreignListeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            foreach (AudioListener listener in _foreignListeners)
                listener.enabled = false;

            _cue = AudioTestFactory.CreateCue("service.a");
            AudioTestFactory.Set(_cue, "_maxDistance", 10f);
            _library = AudioTestFactory.CreateLibrary(_cue);
            _settings = AudioTestFactory.CreateSettings(_library, 4);
            _host = new GameObject("AudioServiceTest");
            _service = _host.AddComponent<AudioService>();
            _service.Initialize(_settings);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_host);
            if (_listenerObject != null)
                Object.DestroyImmediate(_listenerObject);
            Object.DestroyImmediate(_settings);
            Object.DestroyImmediate(_library);
            Object.DestroyImmediate(_cue);
            foreach (AudioListener listener in _foreignListeners)
                if (listener != null)
                    listener.enabled = true;
        }

        private void AddListenerAtOrigin()
        {
            _listenerObject = new GameObject("TestListener");
            _listenerObject.AddComponent<AudioListener>();
        }

        [Test]
        public void 재생하면_유효한_표가_나오고_멈추면_재생_중이_아니다()
        {
            SoundHandle handle = _service.Play(_cue, Vector3.zero);

            Assert.IsTrue(handle.IsValid);
            Assert.IsTrue(_service.IsPlaying(handle));
            _service.Stop(handle);
            Assert.IsFalse(_service.IsPlaying(handle));
        }

        [Test]
        public void 초기화_전에는_아무것도_재생하지_않는다()
        {
            var bare = new GameObject("Bare").AddComponent<AudioService>();

            Assert.IsFalse(bare.Play(_cue, Vector3.zero).IsValid);
            Assert.IsFalse(bare.TryResolve(_cue.Id, out _));
            Object.DestroyImmediate(bare.gameObject);
        }

        [Test]
        public void 큐_없음_NaN_위치_음량_0은_거절한다()
        {
            Assert.IsFalse(_service.Play(null, Vector3.zero).IsValid);
            Assert.IsFalse(_service.Play(_cue, new Vector3(float.NaN, 0f, 0f)).IsValid);
            Assert.IsFalse(_service.Play(_cue, Vector3.zero, 0f).IsValid);
        }

        [Test]
        public void 클립이_없는_큐는_거절한다()
        {
            SoundCue silent = AudioTestFactory.CreateCue("service.silent", 0);

            Assert.IsFalse(_service.Play(silent, Vector3.zero).IsValid);
            Object.DestroyImmediate(silent);
        }

        [Test]
        public void 쿨다운_안의_같은_큐는_거절한다()
        {
            AudioTestFactory.Set(_cue, "_cooldown", 60f);

            Assert.IsTrue(_service.Play(_cue, Vector3.zero).IsValid);
            Assert.IsFalse(_service.Play(_cue, Vector3.zero).IsValid);
        }

        [Test]
        public void 큐의_동시_재생_수를_넘으면_거절하고_멈추면_다시_재생한다()
        {
            AudioTestFactory.Set(_cue, "_maxSimultaneous", 1);

            SoundHandle first = _service.Play(_cue, Vector3.zero);
            Assert.IsTrue(first.IsValid);
            Assert.IsFalse(_service.Play(_cue, Vector3.zero).IsValid);
            _service.Stop(first);
            Assert.IsTrue(_service.Play(_cue, Vector3.zero).IsValid);
        }

        [Test]
        public void 들릴_거리_밖의_3D_소리는_거절하고_2D_소리는_거리를_보지_않는다()
        {
            AddListenerAtOrigin();

            Assert.IsFalse(_service.Play(_cue, new Vector3(50f, 0f, 0f)).IsValid);
            Assert.IsTrue(_service.Play(_cue, new Vector3(5f, 0f, 0f)).IsValid);
            Assert.IsTrue(_service.Play2D(_cue).IsValid);
        }

        [Test]
        public void 가득_차면_더_중요한_새_소리가_밀어내고_밀린_표는_무효가_된다()
        {
            SoundCue weak = AudioTestFactory.CreateCue("service.weak");
            AudioTestFactory.Set(weak, "_priority", 200);
            SoundCue strong = AudioTestFactory.CreateCue("service.strong");
            AudioTestFactory.Set(strong, "_priority", 10);
            var handles = new SoundHandle[4];
            for (int i = 0; i < handles.Length; i++)
                handles[i] = _service.Play(weak, Vector3.zero);

            SoundHandle newcomer = _service.Play(strong, Vector3.zero);

            Assert.IsTrue(newcomer.IsValid);
            int evicted = 0;
            foreach (SoundHandle handle in handles)
                if (!_service.IsPlaying(handle))
                    evicted++;
            Assert.AreEqual(1, evicted);
            Object.DestroyImmediate(weak);
            Object.DestroyImmediate(strong);
        }

        [Test]
        public void 덜_중요한_새_소리는_가득_찬_풀에서_버려진다()
        {
            SoundCue strong = AudioTestFactory.CreateCue("service.strong");
            AudioTestFactory.Set(strong, "_priority", 10);
            SoundCue weak = AudioTestFactory.CreateCue("service.weak");
            AudioTestFactory.Set(weak, "_priority", 200);
            for (int i = 0; i < 4; i++)
                Assert.IsTrue(_service.Play(strong, Vector3.zero).IsValid);

            Assert.IsFalse(_service.Play(weak, Vector3.zero).IsValid);
            Object.DestroyImmediate(strong);
            Object.DestroyImmediate(weak);
        }

        [Test]
        public void 슬롯이_재사용되어도_이전_표로_새_소리를_멈출_수_없다()
        {
            SoundHandle old = _service.Play(_cue, Vector3.zero);
            _service.Stop(old);
            SoundHandle fresh = _service.Play(_cue, Vector3.zero);

            _service.Stop(old);

            Assert.AreEqual(old.Slot, fresh.Slot);
            Assert.IsTrue(_service.IsPlaying(fresh));
        }

        [Test]
        public void StopAll은_반복_소리까지_모두_멈춘다()
        {
            SoundCue loop = AudioTestFactory.CreateCue("service.loop", 1, true);
            SoundHandle one = _service.Play(_cue, Vector3.zero);
            SoundHandle looping = _service.Play(loop, Vector3.zero);

            _service.StopAll();

            Assert.IsFalse(_service.IsPlaying(one));
            Assert.IsFalse(_service.IsPlaying(looping));
            Object.DestroyImmediate(loop);
        }

        [Test]
        public void 버스_음량은_설정값으로_시작하고_0에서_1로_잘린다()
        {
            Assert.AreEqual(1f, _service.GetBusVolume(SoundBus.Sfx));

            _service.SetBusVolume(SoundBus.Ambient, 3f);
            Assert.AreEqual(1f, _service.GetBusVolume(SoundBus.Ambient));
            _service.SetBusVolume(SoundBus.Ambient, -1f);
            Assert.AreEqual(0f, _service.GetBusVolume(SoundBus.Ambient));
        }

        [Test]
        public void 설정_창_음량은_버스_음량에_곱해지고_바뀌면_다시_적용된다()
        {
            AudioTestFactory.Set(_settings, "_sfxVolume", 0.5f);
            AudioTestFactory.Set(_settings, "_ambientVolume", 1f);
            var store = new UserSettingsStore(new MemoryStorage(), applyToEngine: false);
            store.SfxVolume = 0.5f;
            var host = new GameObject("AudioServiceWithUserSettings");
            AudioService service = host.AddComponent<AudioService>();
            service.Initialize(_settings, store);

            Assert.AreEqual(0.25f, service.GetBusVolume(SoundBus.Sfx), 1e-5f);
            Assert.AreEqual(1f, service.GetBusVolume(SoundBus.Ambient), 1e-5f);

            store.SfxVolume = 1f;
            store.AmbientVolume = 0.3f;
            Assert.AreEqual(0.5f, service.GetBusVolume(SoundBus.Sfx), 1e-5f);
            Assert.AreEqual(0.3f, service.GetBusVolume(SoundBus.Ambient), 1e-5f);
            Object.DestroyImmediate(host);
        }

        [Test]
        public void 식별자로_라이브러리의_큐를_찾는다()
        {
            Assert.IsTrue(_service.TryResolve(_cue.Id, out SoundCue found));
            Assert.AreSame(_cue, found);
            Assert.IsFalse(_service.TryResolve(SoundCueId.FromKey("service.unknown"), out _));
        }

        [Test]
        public void 활성_리스너가_있으면_위치를_돌려준다()
        {
            AddListenerAtOrigin();
            _listenerObject.transform.position = new Vector3(1f, 2f, 3f);

            Assert.IsTrue(_service.TryGetListenerPosition(out Vector3 position));
            Assert.AreEqual(new Vector3(1f, 2f, 3f), position);
        }

        [Test]
        public void 리스너가_없으면_위치를_돌려주지_않는다()
        {
            Assert.IsFalse(_service.TryGetListenerPosition(out _));
        }

        private sealed class MemoryStorage : IUserSettingsStorage
        {
            private readonly Dictionary<string, float> _floats = new();

            public float GetFloat(string key, float defaultValue) => _floats.TryGetValue(key, out float value) ? value : defaultValue;
            public void SetFloat(string key, float value) => _floats[key] = value;
            public int GetInt(string key, int defaultValue) => defaultValue;
            public void SetInt(string key, int value) { }
            public string GetString(string key, string defaultValue) => defaultValue;
            public void SetString(string key, string value) { }
            public void Save() { }
        }
    }
}
