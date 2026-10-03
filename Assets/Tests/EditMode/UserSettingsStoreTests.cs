using System.Collections.Generic;
using GhostHunter.Core.Settings;
using GhostHunter.Core.Voice;
using GhostHunter.Data;
using GhostHunter.Gameplay.Voice;
using GhostHunter.Systems.Settings;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>개인 설정 저장소와 음성 서비스 연결(docs/architecture/settings-menu.md).</summary>
    public sealed class UserSettingsStoreTests
    {
        private VoiceChatSettings _voiceSettings;

        [SetUp]
        public void SetUp() => _voiceSettings = ScriptableObject.CreateInstance<VoiceChatSettings>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_voiceSettings);

        private static UserSettingsStore Create(MemoryStorage storage) => new(storage, applyToEngine: false);

        [Test]
        public void 저장된_값이_없으면_기본값이다()
        {
            UserSettingsStore store = Create(new MemoryStorage());

            Assert.AreEqual(1f, store.MasterVolume);
            Assert.AreEqual(1f, store.VoiceVolume);
            Assert.AreEqual(VoiceMode.OpenMic, store.VoiceMode);
            Assert.IsFalse(store.MicMuted);
            Assert.AreEqual(UserSettingsLimits.DefaultMouseSensitivity, store.MouseSensitivity);
            Assert.IsFalse(store.InvertMouseY);
            Assert.IsFalse(store.VSync);
            Assert.AreEqual(0, store.FrameRateLimit);
            Assert.AreEqual(string.Empty, store.MicDevice);
            Assert.AreEqual(0f, store.MicGainDb);
            Assert.IsTrue(store.NoiseGateEnabled, "노이즈 게이트는 기본으로 켜져 있다");
            Assert.AreEqual(UserSettingsLimits.DefaultNoiseGateThresholdDb, store.NoiseGateThresholdDb);
        }

        [Test]
        public void 마이크_장치_게인_게이트는_저장되고_잘린다()
        {
            var storage = new MemoryStorage();
            UserSettingsStore first = Create(storage);
            first.MicDevice = "USB Mic";
            first.MicGainDb = 40f;
            first.NoiseGateEnabled = false;
            first.NoiseGateThresholdDb = -100f;

            UserSettingsStore second = Create(storage);

            Assert.AreEqual("USB Mic", second.MicDevice);
            Assert.AreEqual(UserSettingsLimits.MaxMicGainDb, second.MicGainDb);
            Assert.IsFalse(second.NoiseGateEnabled);
            Assert.AreEqual(UserSettingsLimits.MinNoiseGateThresholdDb, second.NoiseGateThresholdDb);

            second.MicDevice = null;
            Assert.AreEqual(string.Empty, second.MicDevice, "null 은 기본 장치(빈 문자열)로 본다");
        }

        [Test]
        public void 범위를_벗어난_값은_잘린다()
        {
            UserSettingsStore store = Create(new MemoryStorage());

            store.MasterVolume = 1.5f;
            store.VoiceVolume = -1f;
            store.MouseSensitivity = 99f;
            store.FrameRateLimit = -30;

            Assert.AreEqual(1f, store.MasterVolume);
            Assert.AreEqual(0f, store.VoiceVolume);
            Assert.AreEqual(UserSettingsLimits.MaxMouseSensitivity, store.MouseSensitivity);
            Assert.AreEqual(0, store.FrameRateLimit);

            store.MouseSensitivity = 0f;
            Assert.AreEqual(UserSettingsLimits.MinMouseSensitivity, store.MouseSensitivity);
        }

        [Test]
        public void 바꾼_값은_다시_만들어도_남아_있다()
        {
            var storage = new MemoryStorage();
            UserSettingsStore first = Create(storage);
            first.MasterVolume = 0.4f;
            first.VoiceVolume = 0.7f;
            first.VoiceMode = VoiceMode.PushToTalk;
            first.MicMuted = true;
            first.MouseSensitivity = 1.8f;
            first.InvertMouseY = true;
            first.VSync = true;
            first.FrameRateLimit = 144;

            UserSettingsStore second = Create(storage);

            Assert.AreEqual(0.4f, second.MasterVolume, 1e-5f);
            Assert.AreEqual(0.7f, second.VoiceVolume, 1e-5f);
            Assert.AreEqual(VoiceMode.PushToTalk, second.VoiceMode);
            Assert.IsTrue(second.MicMuted);
            Assert.AreEqual(1.8f, second.MouseSensitivity, 1e-5f);
            Assert.IsTrue(second.InvertMouseY);
            Assert.IsTrue(second.VSync);
            Assert.AreEqual(144, second.FrameRateLimit);
        }

        [Test]
        public void 예전_음성_키에_저장된_값을_이어서_읽는다()
        {
            var storage = new MemoryStorage();
            storage.SetInt("Voice.Mode", (int)VoiceMode.PushToTalk);
            storage.SetInt("Voice.Muted", 1);
            storage.SetFloat("Voice.Volume", 0.25f);

            UserSettingsStore store = Create(storage);

            Assert.AreEqual(VoiceMode.PushToTalk, store.VoiceMode);
            Assert.IsTrue(store.MicMuted);
            Assert.AreEqual(0.25f, store.VoiceVolume, 1e-5f);
        }

        [Test]
        public void 알수없는_모드_값은_오픈_마이크로_읽는다()
        {
            var storage = new MemoryStorage();
            storage.SetInt("Voice.Mode", 7);

            Assert.AreEqual(VoiceMode.OpenMic, Create(storage).VoiceMode);
        }

        [Test]
        public void 값이_실제로_바뀔_때만_알린다()
        {
            UserSettingsStore store = Create(new MemoryStorage());
            int changed = 0;
            store.Changed += () => changed++;

            store.MasterVolume = 1f;
            store.MicMuted = false;
            Assert.AreEqual(0, changed);

            store.MasterVolume = 0.5f;
            store.MicMuted = true;
            Assert.AreEqual(2, changed);
        }

        [Test]
        public void 기본값_되돌리기는_고른_탭만_되돌린다()
        {
            UserSettingsStore store = Create(new MemoryStorage());
            store.MicMuted = true;
            store.VoiceMode = VoiceMode.PushToTalk;
            store.MasterVolume = 0.3f;
            store.MouseSensitivity = 2f;

            store.MicGainDb = 6f;
            store.NoiseGateEnabled = false;

            store.ResetToDefaults(UserSettingsGroup.Microphone);

            Assert.AreEqual(0f, store.MicGainDb);
            Assert.IsTrue(store.NoiseGateEnabled);
            Assert.IsFalse(store.MicMuted);
            Assert.AreEqual(VoiceMode.OpenMic, store.VoiceMode);
            Assert.AreEqual(0.3f, store.MasterVolume, 1e-5f);
            Assert.AreEqual(2f, store.MouseSensitivity, 1e-5f);
        }

        [Test]
        public void 음성_서비스는_개인_설정의_값을_읽고_쓴다()
        {
            UserSettingsStore store = Create(new MemoryStorage());
            var chat = new VoiceChatService(new IdleCapture(), _voiceSettings, userSettings: store);

            // M 키 경로(음성 서비스)가 설정 창이 보는 값을 바꾼다.
            chat.IsMuted = true;
            chat.Mode = VoiceMode.PushToTalk;
            chat.MasterVolume = 0.6f;
            Assert.IsTrue(store.MicMuted);
            Assert.AreEqual(VoiceMode.PushToTalk, store.VoiceMode);
            Assert.AreEqual(0.6f, store.VoiceVolume, 1e-5f);

            // 설정 창이 바꾼 값을 진행 중인 음성 서비스가 바로 본다.
            store.MicMuted = false;
            store.VoiceMode = VoiceMode.OpenMic;
            store.VoiceVolume = 0.2f;
            Assert.IsFalse(chat.IsMuted);
            Assert.AreEqual(VoiceMode.OpenMic, chat.Mode);
            Assert.AreEqual(0.2f, chat.MasterVolume, 1e-5f);
        }

        [Test]
        public void 개인_설정이_없으면_음성_서비스_안에만_둔다()
        {
            var chat = new VoiceChatService(new IdleCapture(), _voiceSettings);

            chat.IsMuted = true;
            chat.MasterVolume = 3f;

            Assert.IsTrue(chat.IsMuted);
            Assert.AreEqual(1f, chat.MasterVolume);
            Assert.AreEqual(_voiceSettings.Mode, chat.Mode);
        }

        private sealed class MemoryStorage : IUserSettingsStorage
        {
            private readonly Dictionary<string, float> _floats = new();
            private readonly Dictionary<string, int> _ints = new();
            private readonly Dictionary<string, string> _strings = new();

            public float GetFloat(string key, float defaultValue) => _floats.TryGetValue(key, out float value) ? value : defaultValue;
            public void SetFloat(string key, float value) => _floats[key] = value;
            public int GetInt(string key, int defaultValue) => _ints.TryGetValue(key, out int value) ? value : defaultValue;
            public void SetInt(string key, int value) => _ints[key] = value;
            public string GetString(string key, string defaultValue) => _strings.TryGetValue(key, out string value) ? value : defaultValue;
            public void SetString(string key, string value) => _strings[key] = value;
            public void Save() { }
        }

        private sealed class IdleCapture : IVoiceCaptureService
        {
            public bool IsAvailable => false;
            public bool IsRecording => false;
            public string Status => "Test";
            public byte Codec => 1;
            public int SampleRate => 24000;
            public float InputLevelDb => -120f;
            public bool IsGateOpen => false;
            public bool IsMonitoring { get; set; }
            public void SetRecording(bool recording) { }
            public int ReadFrame(byte[] destination) => 0;
            public IVoiceDecoder CreateDecoder() => null;
        }
    }
}
