using GhostHunter.Core.Audio;
using UnityEngine;
using UnityEngine.Audio;

namespace GhostHunter.Data.Audio
{
    /// <summary>오디오 서비스의 동시 재생 수·거리 컬링·버스 음량·믹서 그룹·벽 가림 튜닝.</summary>
    [CreateAssetMenu(menuName = "GhostHunter/Audio/Game Audio Settings", fileName = "GameAudioSettings")]
    public sealed class GameAudioSettings : ScriptableObject
    {
        [Header("라이브러리")]
        [SerializeField] private SoundLibrary _library;

        [Header("동시 재생")]
        [Tooltip("한꺼번에 낼 수 있는 소리 수. 시작할 때 이만큼 오디오 소스를 미리 만든다.")]
        [SerializeField, Range(4, 128)] private int _voiceCount = 32;
        [Tooltip("활성 AudioListener 를 다시 찾는 간격(초). 씬 전환·카메라 교체 뒤에 쓴다.")]
        [SerializeField] private float _listenerRefreshSeconds = 0.5f;

        [Header("버스")]
        [SerializeField, Range(0f, 1f)] private float _sfxVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float _ambientVolume = 1f;
        [Tooltip("큐에 그룹이 없을 때 효과음이 가는 믹서 그룹.")]
        [SerializeField] private AudioMixerGroup _sfxGroup;
        [Tooltip("큐에 그룹이 없을 때 환경음이 가는 믹서 그룹. 비우면 효과음 그룹을 쓴다.")]
        [SerializeField] private AudioMixerGroup _ambientGroup;

        [Header("벽 가림")]
        [SerializeField] private bool _occlusionEnabled = true;
        [Tooltip("벽 한 겹을 지날 때마다 음량에 곱하는 값.")]
        [SerializeField, Range(0f, 1f)] private float _wallVolumeFactor = 0.5f;
        [Tooltip("벽 한 겹을 지날 때마다 저역 통과 차단 주파수에 곱하는 값.")]
        [SerializeField, Range(0f, 1f)] private float _wallCutoffFactor = 0.4f;
        [SerializeField, Range(1, 8)] private int _maximumWalls = 3;
        [SerializeField, Range(0f, 1f)] private float _occlusionVolumeFloor = 0.1f;
        [SerializeField] private float _openCutoff = 22000f;
        [SerializeField] private float _blockedCutoff = 900f;
        [Tooltip("반복 소리의 가림을 다시 재는 횟수(초당).")]
        [SerializeField] private float _occlusionHz = 5f;
        [Tooltip("가림 변화를 따라가는 시간 상수(초). 클수록 천천히 변한다.")]
        [SerializeField] private float _occlusionSmoothTime = 0.15f;
        [Tooltip("소리 위치 쪽 레이를 이만큼 짧게 쏜다(m). 바닥에 놓인 물체의 소리가 바닥에 가려지지 않게 한다.")]
        [SerializeField] private float _occlusionRayEndInset = 0.15f;

        public SoundLibrary Library => _library;
        public int VoiceCount => Mathf.Clamp(_voiceCount, 4, 128);
        public float ListenerRefreshSeconds => Mathf.Max(0.05f, _listenerRefreshSeconds);
        public float SfxVolume => Mathf.Clamp01(_sfxVolume);
        public float AmbientVolume => Mathf.Clamp01(_ambientVolume);
        public AudioMixerGroup SfxGroup => _sfxGroup;
        public AudioMixerGroup AmbientGroup => _ambientGroup != null ? _ambientGroup : _sfxGroup;
        public bool OcclusionEnabled => _occlusionEnabled;
        public float WallVolumeFactor => Mathf.Clamp01(_wallVolumeFactor);
        public float WallCutoffFactor => Mathf.Clamp01(_wallCutoffFactor);
        public int MaximumWalls => Mathf.Clamp(_maximumWalls, 1, 8);
        public float OcclusionVolumeFloor => Mathf.Clamp01(_occlusionVolumeFloor);
        public float OpenCutoff => Mathf.Clamp(_openCutoff, 10f, 22000f);
        public float BlockedCutoff => Mathf.Clamp(_blockedCutoff, 10f, OpenCutoff);
        public float OcclusionHz => Mathf.Max(0.1f, _occlusionHz);
        public float OcclusionSmoothTime => Mathf.Max(0.001f, _occlusionSmoothTime);
        public float OcclusionRayEndInset => Mathf.Max(0f, _occlusionRayEndInset);

        public float GetInitialBusVolume(SoundBus bus) => bus == SoundBus.Ambient ? AmbientVolume : SfxVolume;

        public AudioMixerGroup GetDefaultGroup(SoundBus bus) => bus == SoundBus.Ambient ? AmbientGroup : SfxGroup;
    }
}
