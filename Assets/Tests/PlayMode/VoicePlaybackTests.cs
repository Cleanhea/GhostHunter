using System.Reflection;
using GhostHunter.Core.Voice;
using GhostHunter.Data;
using GhostHunter.Gameplay.Voice;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// 수신 재생 경로 — 받은 24kHz 음성이 출력 레이트로 바뀌어 오디오 필터 콜백마다 끊김 없이 나오는지 본다.
    /// 콜백은 리플렉션으로 직접 부른다. 한 프레임 안에서 끝내므로 LateUpdate·실제 오디오 스레드와 겹치지 않는다.
    /// 예전 스트리밍 AudioClip 방식은 400ms씩 몰아 읽어 절반이 무음이 됐다(docs/architecture/voice-chat.md).
    /// </summary>
    public sealed class VoicePlaybackTests
    {
        private GameObject _root;
        private VoiceChatSettings _settings;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            if (_settings != null) Object.Destroy(_settings);
        }

        [Test]
        public void Playback_IsContinuousOnceJitterBufferFills()
        {
            VoiceReceiver receiver = CreateReceiver();
            receiver.Enqueue(Constant(0.5f, 4800), 4800); // 200ms — 선버퍼 100ms 를 넘긴다

            for (int callback = 0; callback < 4; callback++)
            {
                float[] data = Carrier(1024, 2);
                Filter(receiver, data, 2);
                Assert.That(data, Is.All.EqualTo(0.5f).Within(1e-5f), $"{callback}번째 콜백에 빈 구간이 있다");
            }
        }

        [Test]
        public void Playback_WaitsForPrebufferAndRefillsAfterUnderrun()
        {
            VoiceReceiver receiver = CreateReceiver();
            receiver.Enqueue(Constant(0.5f, 240), 240); // 10ms — 선버퍼 미달
            float[] data = Carrier(256, 2);
            Filter(receiver, data, 2);
            Assert.That(data, Is.All.EqualTo(0f), "선버퍼가 차기 전에는 무음이어야 한다");

            receiver.Enqueue(Constant(0.5f, 4800), 4800);
            data = Carrier(256, 2);
            Filter(receiver, data, 2);
            Assert.That(data, Is.All.EqualTo(0.5f).Within(1e-5f));

            // 바닥날 때까지 읽는다. 한 번 바닥나면 선버퍼가 다시 찰 때까지 무음이다.
            for (int i = 0; i < 64; i++) Filter(receiver, Carrier(1024, 2), 2);
            receiver.Enqueue(Constant(0.5f, 240), 240);
            data = Carrier(256, 2);
            Filter(receiver, data, 2);
            Assert.That(data, Is.All.EqualTo(0f), "바닥난 뒤 조금만 들어왔을 때 찔끔찔끔 재생하면 안 된다");
        }

        [Test]
        public void Playback_KeepsSpatialGainOfCarrier()
        {
            VoiceReceiver receiver = CreateReceiver();
            receiver.Enqueue(Constant(0.5f, 4800), 4800);
            // 캐리어에는 AudioSource 가 적용한 좌우 패닝·음량이 들어 있다. 음성은 그 위에 곱해져야 한다.
            float[] data = new float[512];
            for (int i = 0; i < data.Length; i += 2) { data[i] = 0.2f; data[i + 1] = 0.8f; }
            Filter(receiver, data, 2);
            Assert.That(data[0], Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(data[1], Is.EqualTo(0.4f).Within(1e-5f));
        }

        private VoiceReceiver CreateReceiver()
        {
            _settings = ScriptableObject.CreateInstance<VoiceChatSettings>();
            _root = new GameObject("VoicePlaybackTest");
            var source = _root.AddComponent<AudioSource>();
            source.playOnAwake = false;
            var receiver = _root.AddComponent<VoiceReceiver>();
            var filter = _root.AddComponent<AudioLowPassFilter>();
            Set(receiver, "_source", source);
            Set(receiver, "_filter", filter);
            receiver.Initialize(_settings, new Speaker(), new VoiceChatService(new SilentCapture(), _settings), 24000);
            return receiver;
        }

        private static float[] Constant(float value, int count)
        {
            var samples = new float[count];
            for (int i = 0; i < count; i++) samples[i] = value;
            return samples;
        }

        private static float[] Carrier(int frames, int channels) => Constant(1f, frames * channels);

        private static void Filter(VoiceReceiver receiver, float[] data, int channels)
            => typeof(VoiceReceiver).GetMethod("OnAudioFilterRead", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(receiver, new object[] { data, channels });

        private static void Set(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private sealed class Speaker : IVoiceParticipant
        {
            public ulong ClientId => 1;
            public bool IsAlive => true;
            public bool IsSpeaking => false;
            public Vector3 MouthPosition => Vector3.zero;
            public Transform Ear => null;
            public string DisplayName => "Test";
            public float Volume { get => 1f; set { } }
        }

        private sealed class SilentCapture : IVoiceCaptureService
        {
            public bool IsAvailable => false;
            public bool IsRecording => false;
            public string Status => "Test";
            public byte Codec => 1;
            public int SampleRate => 24000;
            public void SetRecording(bool recording) { }
            public int ReadFrame(byte[] destination) => 0;
            public IVoiceDecoder CreateDecoder() => null;
            public float InputLevelDb => -120f;
            public bool IsGateOpen => false;
            public bool IsMonitoring { get; set; }
        }
    }
}
