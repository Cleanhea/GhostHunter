using System;
using System.Collections.Generic;
using GhostHunter.Core;
using GhostHunter.Core.Audio;
using GhostHunter.Core.Settings;
using GhostHunter.Data.Audio;
using UnityEngine;
using Random = UnityEngine.Random;

namespace GhostHunter.Systems.Audio
{
    /// <summary>
    /// 미리 만들어 둔 AudioSource 풀로 3D 효과음·환경음을 재생한다. 동시 재생 한도, 큐별 쿨다운·동시 수,
    /// 거리 컬링, 벽 가림(음량·저역 통과), 버스 음량을 맡는다. 재생은 항상 이 클라이언트에서만 일어난다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioService : MonoBehaviour, IAudioService
    {
        private const int MaximumOcclusionHits = 8;
        // 거리 컬링은 완전한 3D 큐에만 건다. 2D 성분이 섞인 큐는 멀리서도 들리기 때문이다.
        private const float CullSpatialBlend = 0.99f;

        private sealed class PooledVoice
        {
            public Transform Transform;
            public AudioSource Source;
            public AudioLowPassFilter LowPass;
            public SoundCue Cue;
            public Transform Follow;
            public bool HasFollow;
            public int Version;
            public int StartFrame;
            public bool Is3D;
            public bool Occludable;
            public float BaseVolume;
            public float OcclusionGain = 1f;
            public float TargetGain = 1f;
            public float Cutoff;
            public float TargetCutoff;
            public double NextOcclusionCheck;
            public float FadeGain = 1f;
            public float FadeRate;
        }

        private readonly Dictionary<SoundCue, int> _lastClip = new();
        private readonly Dictionary<SoundCue, double> _lastPlayed = new();
        private readonly float[] _busVolume = new float[Enum.GetValues(typeof(SoundBus)).Length];
        private readonly RaycastHit[] _hits = new RaycastHit[MaximumOcclusionHits];

        private GameAudioSettings _settings;
        private IUserSettings _userSettings;
        private PooledVoice[] _voices = Array.Empty<PooledVoice>();
        private SoundVoiceSlot[] _slots = Array.Empty<SoundVoiceSlot>();
        private AudioListener _listener;
        private double _nextListenerSearch;
        private int _versionCounter;

        /// <summary>
        /// 풀을 만든다. 한 번만 부른다. <paramref name="userSettings"/>가 있으면 설정 창의 효과음·환경음 음량을
        /// 버스 음량에 곱하고, 값이 바뀔 때마다 다시 적용한다.
        /// </summary>
        public void Initialize(GameAudioSettings settings, IUserSettings userSettings = null)
        {
            if (settings == null)
            {
                Debug.LogError($"{nameof(AudioService)}: GameAudioSettings 가 배선되지 않았다.", this);
                return;
            }

            if (_settings != null)
            {
                Debug.LogError($"{nameof(AudioService)}: 이미 초기화되었다.", this);
                return;
            }

            _settings = settings;
            if (settings.Library == null)
                Debug.LogWarning($"{nameof(AudioService)}: SoundLibrary 가 없다. 네트워크로 받은 소리는 재생할 수 없다.", this);

            for (int i = 0; i < _busVolume.Length; i++)
                _busVolume[i] = settings.GetInitialBusVolume((SoundBus)i);

            int count = settings.VoiceCount;
            _voices = new PooledVoice[count];
            _slots = new SoundVoiceSlot[count];
            for (int i = 0; i < count; i++)
                _voices[i] = CreateVoice(i);

            _userSettings = userSettings;
            if (_userSettings != null)
            {
                _userSettings.Changed += ApplyUserVolumes;
                ApplyUserVolumes();
            }
        }

        public bool TryGetListenerPosition(out Vector3 position)
        {
            if (_listener == null || !_listener.isActiveAndEnabled)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (_settings == null || now < _nextListenerSearch)
                {
                    position = default;
                    return false;
                }

                // 씬 전환·관전 전환 뒤에 드물게만 찾는다. 매 프레임 탐색하지 않는다.
                _nextListenerSearch = now + _settings.ListenerRefreshSeconds;
                _listener = FindActiveListener();
                if (_listener == null)
                {
                    position = default;
                    return false;
                }
            }

            position = _listener.transform.position;
            return true;
        }

        public SoundHandle Play(SoundCue cue, Vector3 position, float volumeScale = 1f) =>
            Begin(cue, true, position, null, volumeScale);

        public SoundHandle PlayAttached(SoundCue cue, Transform target, float volumeScale = 1f)
        {
            if (target == null)
                return SoundHandle.None;

            return Begin(cue, true, target.position, target, volumeScale);
        }

        public SoundHandle Play2D(SoundCue cue, float volumeScale = 1f) =>
            Begin(cue, false, default, null, volumeScale);

        public bool IsPlaying(SoundHandle handle) => TryGetSlot(handle, out _);

        public void Stop(SoundHandle handle, float fadeSeconds = 0f)
        {
            if (!TryGetSlot(handle, out int slot))
                return;

            if (fadeSeconds <= 0f)
            {
                Release(slot);
                return;
            }

            _voices[slot].FadeRate = 1f / fadeSeconds;
        }

        public void StopAll()
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].Busy)
                    Release(i);
        }

        public float GetBusVolume(SoundBus bus) => _busVolume[(int)bus];

        public void SetBusVolume(SoundBus bus, float volume) => _busVolume[(int)bus] = Mathf.Clamp01(volume);

        public bool TryResolve(uint cueId, out SoundCue cue)
        {
            cue = null;
            return _settings != null && _settings.Library != null && _settings.Library.TryGet(cueId, out cue);
        }

        private void OnDestroy()
        {
            if (_userSettings != null)
                _userSettings.Changed -= ApplyUserVolumes;
        }

        private void LateUpdate()
        {
            if (_settings == null)
                return;

            double now = Time.realtimeSinceStartupAsDouble;
            float deltaTime = Time.unscaledDeltaTime;
            bool hasListener = TryGetListenerPosition(out Vector3 listenerPosition);
            float smoothing = 1f - Mathf.Exp(-deltaTime / _settings.OcclusionSmoothTime);
            for (int i = 0; i < _slots.Length; i++)
            {
                if (!_slots[i].Busy)
                    continue;

                PooledVoice voice = _voices[i];
                // 시작한 프레임에는 isPlaying 이 아직 갱신되지 않았을 수 있다.
                if (voice.StartFrame != Time.frameCount && !voice.Source.isPlaying)
                {
                    Release(i);
                    continue;
                }

                if (voice.HasFollow)
                {
                    if (voice.Follow == null)
                    {
                        Release(i);
                        continue;
                    }

                    voice.Transform.position = voice.Follow.position;
                }

                if (voice.FadeRate > 0f)
                {
                    voice.FadeGain -= voice.FadeRate * deltaTime;
                    if (voice.FadeGain <= 0f)
                    {
                        Release(i);
                        continue;
                    }
                }

                _slots[i].Distance = voice.Is3D && hasListener
                    ? Vector3.Distance(voice.Transform.position, listenerPosition)
                    : 0f;

                if (voice.Occludable && hasListener && (voice.Cue.Loop || voice.HasFollow)
                    && now >= voice.NextOcclusionCheck)
                {
                    MeasureOcclusion(voice, listenerPosition);
                    voice.NextOcclusionCheck = now + 1.0 / _settings.OcclusionHz;
                }

                voice.OcclusionGain = Mathf.Lerp(voice.OcclusionGain, voice.TargetGain, smoothing);
                voice.Cutoff = Mathf.Lerp(voice.Cutoff, voice.TargetCutoff, smoothing);
                ApplyOutput(voice);
            }
        }

        // 설정 에셋의 버스 음량이 기본 배율이고, 설정 창 값이 그 위에 곱해진다.
        private void ApplyUserVolumes()
        {
            _busVolume[(int)SoundBus.Sfx] = _settings.SfxVolume * _userSettings.SfxVolume;
            _busVolume[(int)SoundBus.Ambient] = _settings.AmbientVolume * _userSettings.AmbientVolume;
        }

        private SoundHandle Begin(SoundCue cue, bool is3D, Vector3 position, Transform follow, float volumeScale)
        {
            if (_settings == null || cue == null || !(volumeScale > 0f) || !IsFinite(position))
                return SoundHandle.None;

            int previousClip = _lastClip.TryGetValue(cue, out int previous) ? previous : -1;
            int clipIndex = cue.PickClipIndex(previousClip, Random.value);
            if (clipIndex < 0)
                return SoundHandle.None;

            double now = Time.realtimeSinceStartupAsDouble;
            if (cue.Cooldown > 0f && _lastPlayed.TryGetValue(cue, out double last) && now - last < cue.Cooldown)
                return SoundHandle.None;

            if (cue.MaxSimultaneous > 0 && CountPlaying(cue) >= cue.MaxSimultaneous)
                return SoundHandle.None;

            bool hasListener = TryGetListenerPosition(out Vector3 listenerPosition);
            float distance = 0f;
            if (is3D && hasListener)
            {
                distance = Vector3.Distance(position, listenerPosition);
                if (cue.SpatialBlend >= CullSpatialBlend && distance > cue.MaxDistance)
                    return SoundHandle.None;
            }

            int slot = SoundVoiceAllocator.Choose(_slots, cue.Priority, distance);
            if (slot < 0)
                return SoundHandle.None;
            if (_slots[slot].Busy)
                Release(slot);

            PooledVoice voice = _voices[slot];
            Configure(voice, cue, cue.GetClip(clipIndex), is3D, position);
            voice.Follow = follow;
            voice.HasFollow = follow != null;
            voice.BaseVolume = cue.ResolveVolume(Random.value) * Mathf.Clamp01(volumeScale);
            voice.Occludable = is3D && hasListener && cue.Occludable && _settings.OcclusionEnabled;
            voice.OcclusionGain = 1f;
            voice.TargetGain = 1f;
            voice.Cutoff = _settings.OpenCutoff;
            voice.TargetCutoff = _settings.OpenCutoff;
            voice.FadeGain = 1f;
            voice.FadeRate = 0f;
            voice.NextOcclusionCheck = now + 1.0 / _settings.OcclusionHz;
            if (voice.Occludable)
            {
                // 시작 소리가 벽 너머에서 크게 들렸다가 줄어드는 일이 없게 첫 값은 바로 적용한다.
                MeasureOcclusion(voice, listenerPosition);
                voice.OcclusionGain = voice.TargetGain;
                voice.Cutoff = voice.TargetCutoff;
            }

            _versionCounter = _versionCounter == int.MaxValue ? 1 : _versionCounter + 1;
            voice.Version = _versionCounter;
            voice.StartFrame = Time.frameCount;
            voice.Cue = cue;
            _slots[slot] = new SoundVoiceSlot
            {
                Busy = true,
                Priority = cue.Priority,
                Distance = distance,
                StartTime = now,
            };
            _lastClip[cue] = clipIndex;
            _lastPlayed[cue] = now;

            ApplyOutput(voice);
            voice.Source.Play();
            return new SoundHandle(slot, voice.Version);
        }

        private void Configure(PooledVoice voice, SoundCue cue, AudioClip clip, bool is3D, Vector3 position)
        {
            AudioSource source = voice.Source;
            source.Stop();
            source.clip = clip;
            source.loop = cue.Loop;
            source.pitch = cue.ResolvePitch(Random.value);
            source.priority = cue.Priority;
            source.spatialBlend = is3D ? cue.SpatialBlend : 0f;
            source.minDistance = cue.MinDistance;
            source.maxDistance = cue.MaxDistance;
            source.spread = cue.Spread;
            source.dopplerLevel = cue.DopplerLevel;
            switch (cue.Rolloff)
            {
                case SoundRolloff.Linear:
                    source.rolloffMode = AudioRolloffMode.Linear;
                    break;
                case SoundRolloff.Custom:
                    source.rolloffMode = AudioRolloffMode.Custom;
                    source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, cue.CustomRolloff);
                    break;
                default:
                    source.rolloffMode = AudioRolloffMode.Logarithmic;
                    break;
            }

            source.outputAudioMixerGroup = cue.MixerGroup != null
                ? cue.MixerGroup
                : _settings.GetDefaultGroup(cue.Bus);
            voice.Transform.position = position;
        }

        private void ApplyOutput(PooledVoice voice)
        {
            voice.Source.volume = voice.BaseVolume * _busVolume[(int)voice.Cue.Bus]
                * voice.OcclusionGain * voice.FadeGain;
            bool filtered = voice.Occludable && voice.Cutoff < _settings.OpenCutoff - 1f;
            voice.LowPass.enabled = filtered;
            if (filtered)
                voice.LowPass.cutoffFrequency = voice.Cutoff;
        }

        private void MeasureOcclusion(PooledVoice voice, Vector3 listenerPosition)
        {
            Vector3 delta = voice.Transform.position - listenerPosition;
            float rayLength = delta.magnitude - _settings.OcclusionRayEndInset;
            int walls = 0;
            if (rayLength > 0.01f)
                walls = Physics.RaycastNonAlloc(listenerPosition, delta.normalized, _hits, rayLength,
                    GameLayers.VoiceOccluderMask, QueryTriggerInteraction.Ignore);

            voice.TargetGain = SoundOcclusion.Gain(walls, _settings.WallVolumeFactor,
                _settings.MaximumWalls, _settings.OcclusionVolumeFloor);
            voice.TargetCutoff = SoundOcclusion.Cutoff(walls, _settings.OpenCutoff, _settings.BlockedCutoff,
                _settings.WallCutoffFactor, _settings.MaximumWalls);
        }

        private PooledVoice CreateVoice(int index)
        {
            // 꺼 둔 채로 만든다 — AddComponent 는 켜진 오브젝트에서 Awake 를 바로 돌려 playOnAwake(기본 참)로
            // 클립 없는 재생을 시도하고 "Only custom filters can be played" 경고를 낸다.
            var go = new GameObject($"Voice {index:00}");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            AudioLowPassFilter lowPass = go.AddComponent<AudioLowPassFilter>();
            lowPass.enabled = false;
            go.SetActive(true);
            return new PooledVoice { Transform = go.transform, Source = source, LowPass = lowPass };
        }

        private bool TryGetSlot(SoundHandle handle, out int slot)
        {
            slot = handle.Slot;
            return handle.IsValid && slot >= 0 && slot < _slots.Length
                && _slots[slot].Busy && _voices[slot].Version == handle.Version;
        }

        private int CountPlaying(SoundCue cue)
        {
            int count = 0;
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].Busy && _voices[i].Cue == cue)
                    count++;
            return count;
        }

        private void Release(int slot)
        {
            PooledVoice voice = _voices[slot];
            voice.Source.Stop();
            voice.Source.clip = null;
            voice.LowPass.enabled = false;
            voice.Cue = null;
            voice.Follow = null;
            voice.HasFollow = false;
            _slots[slot].Busy = false;
        }

        private static AudioListener FindActiveListener()
        {
            AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            foreach (AudioListener listener in listeners)
                if (listener.isActiveAndEnabled)
                    return listener;
            return null;
        }

        private static bool IsFinite(Vector3 value) =>
            !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z)
              || float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.z));
    }
}
