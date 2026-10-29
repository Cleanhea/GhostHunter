using GhostHunter.Core;
using GhostHunter.Core.Audio;
using UnityEngine;

namespace GhostHunter.Gameplay.Audio
{
    /// <summary>
    /// 씬에 놓아 두는 환경음 발생원. 리스너가 들릴 거리 안에 들어오면 반복 큐를 이 오브젝트에 붙여 재생하고,
    /// 벗어나면 줄이며 멈춘다. 네트워크 상태가 없으므로 각 접속자가 자기 쪽에서 따로 재생한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AmbientSoundSource : MonoBehaviour
    {
        [SerializeField] private SoundCue _cue;
        [SerializeField, Range(0f, 1f)] private float _volumeScale = 1f;
        [Tooltip("들릴 거리 안에 들어왔는지 확인하는 간격(초).")]
        [SerializeField] private float _checkInterval = 0.25f;
        [Tooltip("재생을 멈추기 전 최대 거리에 더하는 여유(m). 경계에서 켜졌다 꺼지기를 반복하지 않게 한다.")]
        [SerializeField] private float _stopMargin = 2f;
        [SerializeField] private float _stopFadeSeconds = 0.5f;

        private IAudioService _audio;
        private SoundHandle _handle;
        private float _nextCheck;

        private void Awake()
        {
            if (_cue == null)
            {
                Debug.LogError($"{nameof(AmbientSoundSource)}: _cue 미할당", this);
                enabled = false;
                return;
            }

            if (!_cue.Loop)
            {
                Debug.LogError($"{nameof(AmbientSoundSource)}: 반복(Loop) 큐가 아니다 — {_cue.name}", this);
                enabled = false;
                return;
            }

            // 오디오 서비스가 없는 단독 씬 실행에서는 조용히 꺼 둔다.
            if (!Services.TryGet(out _audio))
                enabled = false;
        }

        private void OnEnable()
        {
            // 켜지는 프레임에 바로 판정하고, 여러 발생원이 같은 프레임에 몰리지 않게 시작 시점을 흩는다.
            _nextCheck = Time.unscaledTime + Random.value * _checkInterval;
        }

        private void OnDisable()
        {
            if (_audio != null)
                _audio.Stop(_handle, _stopFadeSeconds);
            _handle = SoundHandle.None;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextCheck)
                return;

            _nextCheck = Time.unscaledTime + _checkInterval;
            if (!_audio.TryGetListenerPosition(out Vector3 listener))
                return;

            float distance = Vector3.Distance(listener, transform.position);
            bool playing = _audio.IsPlaying(_handle);
            if (!playing && distance <= _cue.MaxDistance)
                _handle = _audio.PlayAttached(_cue, transform, _volumeScale);
            else if (playing && distance > _cue.MaxDistance + _stopMargin)
            {
                _audio.Stop(_handle, _stopFadeSeconds);
                _handle = SoundHandle.None;
            }
        }
    }
}
