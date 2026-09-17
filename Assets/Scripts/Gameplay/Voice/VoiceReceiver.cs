using GhostHunter.Core;
using GhostHunter.Core.Voice;
using GhostHunter.Data;
using UnityEngine;

namespace GhostHunter.Gameplay.Voice
{
    /// <summary>원격 화자의 스트리밍 재생·거리 감쇠·벽 로우패스를 구동한다.</summary>
    public sealed class VoiceReceiver : MonoBehaviour
    {
        [SerializeField] private AudioSource _source;
        [SerializeField] private AudioLowPassFilter _filter;
        private VoiceChatSettings _settings;
        private IVoiceParticipant _speaker;
        private IVoiceChatService _chat;
        private VoicePcmBuffer _buffer;
        private AudioClip _clip;
        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private double _lastPacket = double.NegativeInfinity;
        private float _nextOcclusion;
        private float _occlusion = 1f;
        private float _gain;
        private int _prebuffer;
        public bool IsSpeaking => _source != null && _source.isPlaying && _gain >= 0.001f;
        // 혼자 검증할 때 귀 대신 눈으로 확인하는 값들 → DebugTools/VoiceDebugHud.
        internal float Gain => _gain;
        internal float Occlusion => _occlusion;
        internal float Cutoff => _filter != null ? _filter.cutoffFrequency : 0f;
        internal float Distance { get; private set; }
        public void Initialize(VoiceChatSettings settings, IVoiceParticipant speaker, IVoiceChatService chat, int sampleRate)
        {
            _settings = settings;
            _speaker = speaker;
            _chat = chat;
            _buffer = new VoicePcmBuffer(sampleRate, (int)(sampleRate * settings.MaximumJitterSeconds));
            _prebuffer = (int)(sampleRate * settings.JitterSeconds);
            _clip = AudioClip.Create("Remote voice", sampleRate, 1, sampleRate, true, _buffer.Read);
            _source.clip = _clip;
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
        }
        public void Enqueue(float[] samples, int count)
        {
            if (_buffer == null || count <= 0) return;
            _buffer.Write(samples, count);
            _lastPacket = Time.unscaledTimeAsDouble;
        }
        public void Flush()
        {
            if (_source != null) { _source.Stop(); _source.volume = 0f; }
            _buffer?.Clear();
            _gain = 0f;
            _lastPacket = double.NegativeInfinity;
        }
        private void LateUpdate()
        {
            if (_buffer == null) return;
            IVoiceParticipant listener = _chat.LocalParticipant;
            if (listener == null || listener.IsAlive != _speaker.IsAlive || _speaker.Volume <= 0f || _chat.MasterVolume <= 0f)
            { Flush(); return; }
            if (Time.unscaledTimeAsDouble - _lastPacket > _settings.SilenceTimeout) { Flush(); return; }
            bool global = !_speaker.IsAlive;
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
            if (_gain < 0.001f) { _source.Stop(); _buffer.Clear(); }
            else if (!_source.isPlaying && _buffer.Count >= _prebuffer) _source.Play();
        }
        private float Trace(Vector3 origin, Vector3 target)
        {
            Vector3 delta = target - origin;
            int count = Physics.RaycastNonAlloc(origin, delta.normalized, _hits, delta.magnitude,
                GameLayers.VoiceOccluderMask, QueryTriggerInteraction.Ignore);
            return VoiceAttenuation.Occlusion(count, _settings.WallFactor, _settings.MaximumWalls, _settings.OcclusionFloor);
        }
        private void OnDisable() => Flush();
        private void OnDestroy()
        {
            Flush();
            if (_clip != null) Destroy(_clip);
        }
    }
}
