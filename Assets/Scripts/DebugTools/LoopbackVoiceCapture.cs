using System;
using GhostHunter.Core.Voice;

namespace GhostHunter.DebugTools
{
    /// <summary>마이크와 Steam 없이 네트워크·감쇠를 확인하는 50ms 사인파 캡처.</summary>
    public sealed class LoopbackVoiceCapture : IVoiceCaptureService
    {
        private int _phase;
        public bool IsAvailable => true;
        public bool IsRecording { get; private set; }
        public string Status => "개발용 사인파 (실제 마이크 아님)";
        public byte Codec => VoiceCodecs.TestTone;
        public int SampleRate => 24000;
        // 진폭 0.1 사인파의 RMS(-23dBFS). 설정 창 입력 막대가 개발용 송신 중에도 움직이게 한다.
        public float InputLevelDb => IsRecording ? -23f : -120f;
        public bool IsGateOpen => IsRecording;
        public bool IsMonitoring { get; set; }
        public void SetRecording(bool recording) { IsRecording = recording; }
        // 사인파 블록은 위상만 담아 상태가 없다. 디코더는 이 캡처의 Decode 를 그대로 부른다.
        public IVoiceDecoder CreateDecoder() => new ToneDecoder(this);
        public int ReadFrame(byte[] destination)
        {
            if (!IsRecording) return 0;
            destination[0] = (byte)_phase;
            destination[1] = (byte)(_phase >> 8);
            _phase = (_phase + 1200) % SampleRate;
            return 2;
        }
        private int Decode(byte[] compressed, int count, float[] samples)
        {
            if (count != 2 || samples.Length < 1200) return 0;
            int phase = compressed[0] | compressed[1] << 8;
            for (int i = 0; i < 1200; i++)
                samples[i] = 0.1f * (float)Math.Sin(2 * Math.PI * 440 * (phase + i) / SampleRate);
            return 1200;
        }
        private sealed class ToneDecoder : IVoiceDecoder
        {
            private readonly LoopbackVoiceCapture _owner;
            public ToneDecoder(LoopbackVoiceCapture owner) => _owner = owner;
            public int Decode(byte[] compressed, int count, float[] samples) => _owner.Decode(compressed, count, samples);
        }
    }
}
