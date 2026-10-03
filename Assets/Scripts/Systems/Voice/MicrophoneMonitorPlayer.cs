using System;
using GhostHunter.Gameplay.Voice;
using UnityEngine;

namespace GhostHunter.Systems.Voice
{
    /// <summary>
    /// 마이크 테스트의 "내 목소리 듣기". 처리된(게인·게이트 적용) 48kHz PCM 을 2D 로 재생한다.
    /// 원격 음성과 같은 방식이다 — AudioSource 가 1.0 캐리어를 반복 재생하고 <see cref="OnAudioFilterRead"/> 가
    /// 오디오 스레드에서 샘플로 덮어쓴다 → docs/architecture/voice-chat.md "재생".
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MicrophoneMonitorPlayer : MonoBehaviour
    {
        private const int CarrierLength = 1024;
        // 녹음 지연 위에 더 쌓이지 않게 짧게 둔다. 넘치면 오래된 샘플부터 건너뛴다.
        private const float MaximumQueuedSeconds = 0.25f;

        private AudioSource _source;
        private AudioClip _carrier;
        private VoiceResampler _resampler;
        private float[] _resampled = Array.Empty<float>();
        private volatile VoicePcmBuffer _buffer;
        private readonly float[] _mono = new float[4096];

        public static MicrophoneMonitorPlayer Create(Transform parent)
        {
            var go = new GameObject("MicrophoneMonitor");
            go.transform.SetParent(parent, false);
            // 필터 콜백은 AudioSource 뒤에 붙어야 그 소스의 신호를 받는다.
            var source = go.AddComponent<AudioSource>();
            var player = go.AddComponent<MicrophoneMonitorPlayer>();
            player.Initialize(source);
            return player;
        }

        private void OnDestroy()
        {
            _buffer = null;
            if (_carrier != null)
                Destroy(_carrier);
        }

        /// <summary>48kHz mono 샘플을 재생 큐에 넣는다. 메인 스레드에서만 부른다.</summary>
        public void Push(float[] samples, int count)
        {
            VoicePcmBuffer buffer = _buffer;
            if (buffer == null || count <= 0)
                return;

            if (!_source.isPlaying)
                _source.Play();

            int needed = _resampler.MaximumOutput(count);
            if (_resampled.Length < needed)
                _resampled = new float[needed];
            buffer.Write(_resampled, _resampler.Process(samples, count, _resampled));
        }

        public void Stop()
        {
            if (_source != null)
                _source.Stop();
            _buffer?.Clear();
            _resampler?.Reset();
        }

        private void Initialize(AudioSource source)
        {
            _source = source;
            int outputRate = AudioSettings.outputSampleRate;
            _resampler = new VoiceResampler(OpusVoiceBlock.SampleRate, outputRate);
            _carrier = AudioClip.Create("MicrophoneMonitorCarrier", CarrierLength, 1, outputRate, false);
            var ones = new float[CarrierLength];
            for (int i = 0; i < ones.Length; i++)
                ones[i] = 1f;
            _carrier.SetData(ones, 0);

            _source.clip = _carrier;
            _source.loop = true;
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.priority = 16;
            _buffer = new VoicePcmBuffer(outputRate, (int)(outputRate * MaximumQueuedSeconds));
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            VoicePcmBuffer buffer = _buffer;
            if (buffer == null || channels <= 0)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            // 오디오 스레드에서 할당하지 않는다 — 고정 크기 조각으로 나눠 읽는다.
            int frames = data.Length / channels;
            for (int done = 0; done < frames;)
            {
                int chunk = Math.Min(_mono.Length, frames - done);
                buffer.Read(_mono, chunk);
                for (int frame = 0; frame < chunk; frame++)
                {
                    float sample = _mono[frame];
                    int index = (done + frame) * channels;
                    for (int channel = 0; channel < channels; channel++)
                        data[index + channel] = sample;
                }

                done += chunk;
            }
        }
    }
}
