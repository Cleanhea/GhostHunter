using UnityEngine;
using UnityEngine.Audio;

namespace GhostHunter.Core.Audio
{
    /// <summary>
    /// 소리 하나의 재생 정의 — 클립 묶음, 음량·음높이 흔들림, 3D 감쇠, 동시 재생 제한.
    /// 코드는 이 에셋을 참조해 <see cref="IAudioService"/>로 재생한다. 오디오 파일이 바뀌어도 코드는 그대로다.
    /// </summary>
    [CreateAssetMenu(menuName = "GhostHunter/Audio/Sound Cue", fileName = "SFX_")]
    public sealed class SoundCue : ScriptableObject
    {
        // 네트워크로는 클립이 아니라 이 키의 해시만 보낸다. 키를 바꾸면 이전 빌드와 맞지 않는다.
        [Header("식별")]
        [Tooltip("네트워크 식별용 고유 키. 비우면 에셋 이름을 쓴다. 라이브러리 안에서 겹치면 안 된다.")]
        [SerializeField] private string _key;

        [Header("클립")]
        [Tooltip("재생할 때마다 하나를 고른다. 같은 클립이 연속으로 나오지 않는다.")]
        [SerializeField] private AudioClip[] _clips;
        [SerializeField] private SoundBus _bus = SoundBus.Sfx;

        [Header("음량·음높이")]
        [SerializeField, Range(0f, 1f)] private float _volume = 1f;
        [Tooltip("재생마다 음량을 ± 이 값만큼 흔든다.")]
        [SerializeField, Range(0f, 0.5f)] private float _volumeJitter;
        [SerializeField, Range(0.1f, 3f)] private float _pitch = 1f;
        [Tooltip("재생마다 음높이를 ± 이 값만큼 흔든다.")]
        [SerializeField, Range(0f, 0.5f)] private float _pitchJitter;

        [Header("3D")]
        [Tooltip("0 = 2D, 1 = 완전한 3D.")]
        [SerializeField, Range(0f, 1f)] private float _spatialBlend = 1f;
        [SerializeField] private float _minDistance = 1.5f;
        [SerializeField] private float _maxDistance = 25f;
        [SerializeField] private SoundRolloff _rolloff = SoundRolloff.Logarithmic;
        [SerializeField] private AnimationCurve _customRolloff = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        [Tooltip("스피커 퍼짐 각도. 0 이면 방향이 또렷하고 360 이면 사방에서 들린다.")]
        [SerializeField, Range(0f, 360f)] private float _spread;
        [SerializeField, Range(0f, 5f)] private float _dopplerLevel;
        [Tooltip("벽 뒤에서는 작아지고 먹먹해진다. 설정은 GameAudioSettings 의 가림 항목.")]
        [SerializeField] private bool _occludable = true;

        [Header("재생 제어")]
        [SerializeField] private bool _loop;
        [Tooltip("0 이 가장 중요하다. 동시 재생 한도를 넘으면 값이 큰 소리가 먼저 밀려난다.")]
        [SerializeField, Range(0, 255)] private int _priority = 128;
        [Tooltip("같은 큐를 다시 재생하기까지 최소 간격(초). 0 이면 제한 없음.")]
        [SerializeField] private float _cooldown;
        [Tooltip("이 큐가 동시에 낼 수 있는 소리 수. 0 이면 제한 없음.")]
        [SerializeField] private int _maxSimultaneous;
        [Tooltip("비우면 GameAudioSettings 의 버스 기본 그룹을 쓴다.")]
        [SerializeField] private AudioMixerGroup _mixerGroup;

        public string Key => string.IsNullOrWhiteSpace(_key) ? name : _key.Trim();
        public uint Id => SoundCueId.FromKey(Key);
        public SoundBus Bus => _bus;
        public int ClipCount => _clips != null ? _clips.Length : 0;
        public bool HasClips => FirstClipIndex() >= 0;
        public float Volume => Mathf.Clamp01(_volume);
        public float VolumeJitter => Mathf.Clamp(_volumeJitter, 0f, 0.5f);
        public float Pitch => Mathf.Clamp(_pitch, 0.1f, 3f);
        public float PitchJitter => Mathf.Clamp(_pitchJitter, 0f, 0.5f);
        public float SpatialBlend => Mathf.Clamp01(_spatialBlend);
        public float MinDistance => Mathf.Max(0.01f, _minDistance);
        public float MaxDistance => Mathf.Max(MinDistance + 0.01f, _maxDistance);
        public SoundRolloff Rolloff => _rolloff;
        public AnimationCurve CustomRolloff => _customRolloff;
        public float Spread => Mathf.Clamp(_spread, 0f, 360f);
        public float DopplerLevel => Mathf.Clamp(_dopplerLevel, 0f, 5f);
        public bool Occludable => _occludable;
        public bool Loop => _loop;
        public int Priority => Mathf.Clamp(_priority, 0, 255);
        public float Cooldown => Mathf.Max(0f, _cooldown);
        public int MaxSimultaneous => Mathf.Max(0, _maxSimultaneous);
        public AudioMixerGroup MixerGroup => _mixerGroup;

        public AudioClip GetClip(int index) =>
            _clips != null && index >= 0 && index < _clips.Length ? _clips[index] : null;

        /// <summary>
        /// 다음에 재생할 클립 번호. 비어 있는 칸은 건너뛰고, 둘 이상이면 직전과 같은 클립을 피한다.
        /// 고를 클립이 없으면 -1. <paramref name="random01"/>은 0 이상 1 미만의 난수다.
        /// </summary>
        public int PickClipIndex(int previousIndex, float random01)
        {
            if (_clips == null)
                return -1;

            int available = 0;
            for (int i = 0; i < _clips.Length; i++)
                if (_clips[i] != null && i != previousIndex)
                    available++;

            // 직전 클립밖에 없으면(또는 하나도 없으면) 반복을 피할 수 없다.
            if (available == 0)
                return FirstClipIndex();

            int pick = Mathf.Clamp((int)(Mathf.Clamp01(random01) * available), 0, available - 1);
            for (int i = 0; i < _clips.Length; i++)
            {
                if (_clips[i] == null || i == previousIndex)
                    continue;
                if (pick-- == 0)
                    return i;
            }

            return -1;
        }

        /// <summary><paramref name="random01"/>이 0 이면 -흔들림, 1 이면 +흔들림.</summary>
        public float ResolveVolume(float random01) =>
            Mathf.Clamp01(Volume + (Mathf.Clamp01(random01) * 2f - 1f) * VolumeJitter);

        public float ResolvePitch(float random01) =>
            Mathf.Clamp(Pitch + (Mathf.Clamp01(random01) * 2f - 1f) * PitchJitter, 0.1f, 3f);

        private int FirstClipIndex()
        {
            if (_clips == null)
                return -1;
            for (int i = 0; i < _clips.Length; i++)
                if (_clips[i] != null)
                    return i;
            return -1;
        }

        private void OnValidate()
        {
            if (_maxDistance < _minDistance)
                _maxDistance = _minDistance + 0.01f;
            if (_customRolloff == null)
                _customRolloff = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        }
    }
}
