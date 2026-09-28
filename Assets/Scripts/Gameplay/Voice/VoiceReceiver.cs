using System;
using GhostHunter.Core;
using GhostHunter.Core.Voice;
using GhostHunter.Data;
using UnityEngine;

namespace GhostHunter.Gameplay.Voice
{
    /// <summary>
    /// 원격 화자의 스트리밍 재생·거리 감쇠·벽 로우패스를 구동한다.
    ///
    /// <para><b>재생 방식</b> — AudioSource 는 값이 1인 짧은 캐리어 클립을 반복 재생하고,
    /// <see cref="OnAudioFilterRead"/> 가 오디오 스레드에서 캐리어에 음성 샘플을 곱한다. 필터는 3D 패닝·음량이
    /// 적용된 뒤의 신호를 받으므로 공간감이 그대로 유지되고, 이 컴포넌트 아래의 AudioLowPassFilter 가 음성에
    /// 걸린다(컴포넌트 순서가 중요하다). 스트리밍 AudioClip(PCMReaderCallback)은 시작 때 약 800ms, 이후 400ms씩
    /// 몰아 읽어 200ms 링버퍼의 절반을 무음으로 채웠다 — 2026-09-27 측정, docs/architecture/voice-chat.md.</para>
    /// </summary>
    public sealed class VoiceReceiver : MonoBehaviour
    {
        private const int CarrierLength = 1024;

        [SerializeField] private AudioSource _source;
        [SerializeField] private AudioLowPassFilter _filter;
        private VoiceChatSettings _settings;
        private IVoiceParticipant _speaker;
        private IVoiceChatService _chat;
        private volatile VoicePcmBuffer _buffer;
        private VoiceResampler _resampler;
        private readonly VoiceActivityGate _speech = new();
        private float[] _resampled = Array.Empty<float>();
        private readonly float[] _mono = new float[4096];
        private AudioClip _carrier;
        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private double _lastPacket = double.NegativeInfinity;
        private float _nextOcclusion;
        private float _occlusion = 1f;
        private float _gain;
        private int _sourceRate;
        private int _prebuffer;
        private volatile bool _primed;

        /// <summary>
        /// 재생 중이고 들리는 거리이며, 받은 소리가 발화 표시 임계값을 넘었을 때. 표시 전용이다 —
        /// 송신·재생은 이 값과 무관하게 모든 소리를 그대로 다룬다.
        /// </summary>
        public bool IsSpeaking => _source != null && _source.isPlaying && _gain >= 0.001f && _speech.IsOpen;
        // 혼자 검증할 때 귀 대신 눈으로 확인하는 값들 → DebugTools/VoiceDebugHud.
        internal float Gain => _gain;
        internal float Occlusion => _occlusion;
        internal float Cutoff => _filter != null ? _filter.cutoffFrequency : 0f;
        internal float Distance { get; private set; }

        /// <summary>런타임에 만든 로비 화자 오브젝트의 오디오 컴포넌트를 연결한다.</summary>
        public void Configure(AudioSource source, AudioLowPassFilter filter)
        {
            _source = source;
            _filter = filter;
        }

        public void Initialize(VoiceChatSettings settings, IVoiceParticipant speaker, IVoiceChatService chat, int sampleRate)
        {
            _settings = settings;
            _speaker = speaker;
            _chat = chat;
            _sourceRate = sampleRate;
            int outputRate = AudioSettings.outputSampleRate;
            _resampler = new VoiceResampler(sampleRate, outputRate);
            _resampled = new float[_resampler.MaximumOutput(sampleRate)];
            _prebuffer = (int)(outputRate * settings.JitterSeconds);
            if (_carrier != null) Destroy(_carrier);
            _carrier = CreateCarrier(outputRate);
            _source.clip = _carrier;
            _source.playOnAwake = false;
            _source.loop = true;
            _source.spatialBlend = 1f;
            _source.dopplerLevel = 0f;
            _source.spread = 30f;
            _source.priority = 32;
            _source.rolloffMode = AudioRolloffMode.Custom;
            _source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0f, 1f, 1f));
            _source.maxDistance = settings.MaximumDistance;
            _source.outputAudioMixerGroup = settings.OutputGroup;
            _source.volume = 0f;
            _nextOcclusion = Time.unscaledTime + (speaker.ClientId % 4) / (4f * settings.OcclusionHz);
            WarnIfFilterAboveReceiver();
            // 오디오 스레드가 보는 버퍼는 나머지 준비가 끝난 뒤에 건다.
            _buffer = new VoicePcmBuffer(outputRate, (int)(outputRate * settings.MaximumJitterSeconds));
        }

        public void Enqueue(float[] samples, int count)
        {
            VoicePcmBuffer buffer = _buffer;
            if (buffer == null || count <= 0) return;
            UpdateSpeech(samples, count);
            if (_resampled.Length < _resampler.MaximumOutput(count)) _resampled = new float[_resampler.MaximumOutput(count)];
            buffer.Write(_resampled, _resampler.Process(samples, count, _resampled));
            _lastPacket = Time.unscaledTimeAsDouble;
        }

        public void Flush()
        {
            if (_source != null) { _source.Stop(); _source.volume = 0f; }
            _buffer?.Clear();
            _resampler?.Reset();
            _speech.Reset();
            _primed = false;
            _gain = 0f;
            _lastPacket = double.NegativeInfinity;
        }

        private void LateUpdate()
        {
            VoicePcmBuffer buffer = _buffer;
            if (buffer == null) return;
            IVoiceParticipant listener = _chat.LocalParticipant;
            if (!_chat.IsActive || listener == null
                || (!_chat.IsResultChannel && listener.IsAlive != _speaker.IsAlive)
                || _speaker.Volume <= 0f || _chat.MasterVolume <= 0f)
            { Flush(); return; }
            if (Time.unscaledTimeAsDouble - _lastPacket > _settings.SilenceTimeout) { Flush(); return; }
            bool global = _chat.IsResultChannel || !_speaker.IsAlive;
            if (!global && listener.Ear == null) { Flush(); return; }
            Vector3 delta = global ? Vector3.zero : _speaker.MouthPosition - listener.Ear.position;
            float distance = new Vector2(delta.x, delta.z).magnitude;
            Distance = distance;
            float horizontal = VoiceAttenuation.Horizontal(distance, _settings.MinimumDistance, _settings.FadeDistance,
                _settings.MaximumDistance, _settings.RolloffExponent);
            float vertical = VoiceAttenuation.Vertical(delta.y, _settings.VerticalNear, _settings.VerticalCut);
            // 공간 경계와 사망 채널은 평활 꼬리로 새지 않도록 정확히 차단한다.
            if (!global && (horizontal == 0f || vertical == 0f)) { Flush(); return; }
            if (!global && Time.unscaledTime >= _nextOcclusion)
            {
                _nextOcclusion = Time.unscaledTime + 1f / _settings.OcclusionHz;
                Vector3 origin = listener.Ear.position;
                Vector3 side = Vector3.Cross(Vector3.up, delta).normalized * _settings.RayOffset;
                _occlusion = (2f * Trace(origin, _speaker.MouthPosition) +
                    Trace(origin + side, _speaker.MouthPosition + side) + Trace(origin - side, _speaker.MouthPosition - side)) / 4f;
            }
            float occlusion = global ? 1f : _occlusion;
            float target = (global ? 1f : horizontal * vertical) * occlusion * _chat.MasterVolume * _speaker.Volume;
            _gain = VoiceAttenuation.Smooth(_gain, target, Time.unscaledDeltaTime, _settings.GainSmoothTime);
            _source.volume = _gain;
            _source.spatialBlend = global ? 0f : 1f;
            _filter.cutoffFrequency = VoiceAttenuation.Smooth(_filter.cutoffFrequency,
                Mathf.Lerp(_settings.OpenCutoff, _settings.BlockedCutoff, 1f - occlusion), Time.unscaledDeltaTime, _settings.CutoffSmoothTime);
            transform.position = _speaker.MouthPosition;
            if (_gain < 0.001f) { _source.Stop(); buffer.Clear(); _primed = false; }
            else if (!_source.isPlaying && buffer.Count >= _prebuffer) _source.Play();
        }

        private void OnDisable() => Flush();

        private void OnDestroy()
        {
            Flush();
            if (_carrier != null) Destroy(_carrier);
        }

        /// <summary>
        /// 오디오 스레드. <paramref name="data"/> 에는 3D 패닝·음량이 적용된 캐리어(1.0)가 들어 있다.
        /// 선버퍼(<see cref="VoiceChatSettings.JitterSeconds"/>)가 찰 때까지는 무음이고, 바닥나면 다시 채운다.
        /// Unity API·lock·할당을 쓰지 않는다.
        /// </summary>
        private void OnAudioFilterRead(float[] data, int channels)
        {
            VoicePcmBuffer buffer = _buffer;
            if (buffer == null || channels <= 0) { Array.Clear(data, 0, data.Length); return; }
            if (!_primed)
            {
                if (buffer.Count < _prebuffer) { Array.Clear(data, 0, data.Length); return; }
                _primed = true;
            }

            int frames = data.Length / channels;
            for (int done = 0; done < frames;)
            {
                int chunk = Math.Min(_mono.Length, frames - done);
                if (buffer.Read(_mono, chunk) < chunk) _primed = false;
                for (int frame = 0; frame < chunk; frame++)
                {
                    float sample = _mono[frame];
                    int index = (done + frame) * channels;
                    for (int channel = 0; channel < channels; channel++) data[index + channel] *= sample;
                }
                done += chunk;
            }
        }

        /// <summary>받은 소리로 "말하는 중" 표시만 갱신한다. 20ms 창 단위.</summary>
        private void UpdateSpeech(float[] samples, int count)
        {
            int window = Math.Max(1, _sourceRate / 50);
            for (int offset = 0; offset < count; offset += window)
            {
                int length = Math.Min(window, count - offset);
                _speech.Step(VoiceActivityGate.Decibels(samples, offset, length), length / (float)_sourceRate,
                    _settings.OpenThreshold, _settings.CloseThreshold, _settings.HangoverSeconds);
            }
        }

        private float Trace(Vector3 origin, Vector3 target)
        {
            Vector3 delta = target - origin;
            int count = Physics.RaycastNonAlloc(origin, delta.normalized, _hits, delta.magnitude,
                GameLayers.VoiceOccluderMask, QueryTriggerInteraction.Ignore);
            return VoiceAttenuation.Occlusion(count, _settings.WallFactor, _settings.MaximumWalls, _settings.OcclusionFloor);
        }

        private static AudioClip CreateCarrier(int sampleRate)
        {
            var ones = new float[CarrierLength];
            for (int i = 0; i < ones.Length; i++) ones[i] = 1f;
            AudioClip carrier = AudioClip.Create("Voice carrier", CarrierLength, 1, sampleRate, false);
            carrier.SetData(ones, 0);
            return carrier;
        }

        /// <summary>필터는 인스펙터 순서대로 걸린다. 로우패스가 위에 있으면 캐리어만 거르고 음성은 그대로 나간다.</summary>
        private void WarnIfFilterAboveReceiver()
        {
            if (_filter == null || _filter.gameObject != gameObject) return;
            Component[] components = GetComponents<Component>();
            if (Array.IndexOf(components, _filter) < Array.IndexOf(components, this))
                Debug.LogError($"[VoiceReceiver] '{name}' 에서 AudioLowPassFilter 가 VoiceReceiver 보다 위에 있어 벽 로우패스가 음성에 걸리지 않는다. " +
                               "프리팹이면 컴포넌트 순서를, 런타임 생성이면 AddComponent 순서를 VoiceReceiver → AudioLowPassFilter 로 바꿔라.", this);
        }
    }
}
