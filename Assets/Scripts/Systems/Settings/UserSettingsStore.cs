using System;
using GhostHunter.Core.Settings;
using GhostHunter.Core.Voice;
using UnityEngine;

namespace GhostHunter.Systems.Settings
{
    /// <summary>
    /// <see cref="IUserSettings"/> 구현. 값을 자르고, 저장소에 쓰고, 엔진에 바로 적용한다.
    /// 전체 음량은 <see cref="AudioListener.volume"/>, 수직 동기화·프레임 상한은 QualitySettings·Application 이다.
    /// </summary>
    public sealed class UserSettingsStore : IUserSettings
    {
        public const string MasterVolumeKey = "Audio.MasterVolume";
        // 음성 세 키는 VoiceChatService 가 직접 저장하던 시절 이름 그대로다 — 기존 사용자 값이 이어진다.
        public const string VoiceVolumeKey = "Voice.Volume";
        public const string VoiceModeKey = "Voice.Mode";
        public const string MicMutedKey = "Voice.Muted";
        public const string MouseSensitivityKey = "Input.MouseSensitivity";
        public const string InvertMouseYKey = "Input.InvertMouseY";
        public const string VSyncKey = "Display.VSync";
        public const string FrameRateLimitKey = "Display.FrameRateLimit";

        private readonly IUserSettingsStorage _storage;
        private readonly bool _applyToEngine;

        private float _masterVolume;
        private float _voiceVolume;
        private VoiceMode _voiceMode;
        private bool _micMuted;
        private float _mouseSensitivity;
        private bool _invertMouseY;
        private bool _vSync;
        private int _frameRateLimit;

        /// <param name="applyToEngine">false 면 엔진 전역 값을 건드리지 않는다(테스트용).</param>
        public UserSettingsStore(IUserSettingsStorage storage, bool applyToEngine = true)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _applyToEngine = applyToEngine;

            _masterVolume = Mathf.Clamp01(_storage.GetFloat(MasterVolumeKey, 1f));
            _voiceVolume = Mathf.Clamp01(_storage.GetFloat(VoiceVolumeKey, 1f));
            _voiceMode = ToVoiceMode(_storage.GetInt(VoiceModeKey, (int)VoiceMode.OpenMic));
            _micMuted = _storage.GetInt(MicMutedKey, 0) != 0;
            _mouseSensitivity = ClampSensitivity(
                _storage.GetFloat(MouseSensitivityKey, UserSettingsLimits.DefaultMouseSensitivity));
            _invertMouseY = _storage.GetInt(InvertMouseYKey, 0) != 0;
            // 기본값은 설정 창이 생기기 전 동작 그대로다 — 프로젝트 품질 설정의 vSyncCount 0, 프레임 무제한.
            _vSync = _storage.GetInt(VSyncKey, 0) != 0;
            _frameRateLimit = Mathf.Max(0, _storage.GetInt(FrameRateLimitKey, 0));

            ApplyAudio();
            ApplyDisplay();
        }

        public event Action Changed;

        public float MasterVolume
        {
            get => _masterVolume;
            set
            {
                float next = Mathf.Clamp01(value);
                if (Mathf.Approximately(next, _masterVolume))
                    return;

                _masterVolume = next;
                _storage.SetFloat(MasterVolumeKey, next);
                ApplyAudio();
                Changed?.Invoke();
            }
        }

        public float VoiceVolume
        {
            get => _voiceVolume;
            set
            {
                float next = Mathf.Clamp01(value);
                if (Mathf.Approximately(next, _voiceVolume))
                    return;

                _voiceVolume = next;
                _storage.SetFloat(VoiceVolumeKey, next);
                Changed?.Invoke();
            }
        }

        public VoiceMode VoiceMode
        {
            get => _voiceMode;
            set
            {
                VoiceMode next = ToVoiceMode((int)value);
                if (next == _voiceMode)
                    return;

                _voiceMode = next;
                _storage.SetInt(VoiceModeKey, (int)next);
                Changed?.Invoke();
            }
        }

        public bool MicMuted
        {
            get => _micMuted;
            set
            {
                if (value == _micMuted)
                    return;

                _micMuted = value;
                _storage.SetInt(MicMutedKey, value ? 1 : 0);
                Changed?.Invoke();
            }
        }

        public float MouseSensitivity
        {
            get => _mouseSensitivity;
            set
            {
                float next = ClampSensitivity(value);
                if (Mathf.Approximately(next, _mouseSensitivity))
                    return;

                _mouseSensitivity = next;
                _storage.SetFloat(MouseSensitivityKey, next);
                Changed?.Invoke();
            }
        }

        public bool InvertMouseY
        {
            get => _invertMouseY;
            set
            {
                if (value == _invertMouseY)
                    return;

                _invertMouseY = value;
                _storage.SetInt(InvertMouseYKey, value ? 1 : 0);
                Changed?.Invoke();
            }
        }

        public bool VSync
        {
            get => _vSync;
            set
            {
                if (value == _vSync)
                    return;

                _vSync = value;
                _storage.SetInt(VSyncKey, value ? 1 : 0);
                ApplyDisplay();
                Changed?.Invoke();
            }
        }

        public int FrameRateLimit
        {
            get => _frameRateLimit;
            set
            {
                int next = Mathf.Max(0, value);
                if (next == _frameRateLimit)
                    return;

                _frameRateLimit = next;
                _storage.SetInt(FrameRateLimitKey, next);
                ApplyDisplay();
                Changed?.Invoke();
            }
        }

        public void ResetToDefaults(UserSettingsGroup group)
        {
            switch (group)
            {
                case UserSettingsGroup.Audio:
                    MasterVolume = 1f;
                    VoiceVolume = 1f;
                    break;
                case UserSettingsGroup.Microphone:
                    VoiceMode = VoiceMode.OpenMic;
                    MicMuted = false;
                    break;
                case UserSettingsGroup.Controls:
                    MouseSensitivity = UserSettingsLimits.DefaultMouseSensitivity;
                    InvertMouseY = false;
                    break;
                case UserSettingsGroup.Display:
                    VSync = false;
                    FrameRateLimit = 0;
                    break;
            }
        }

        public void Save() => _storage.Save();

        private void ApplyAudio()
        {
            if (_applyToEngine)
                AudioListener.volume = _masterVolume;
        }

        private void ApplyDisplay()
        {
            if (!_applyToEngine)
                return;

            QualitySettings.vSyncCount = _vSync ? 1 : 0;
            Application.targetFrameRate = _frameRateLimit > 0 ? _frameRateLimit : -1;
        }

        private static VoiceMode ToVoiceMode(int value)
            => value == (int)VoiceMode.PushToTalk ? VoiceMode.PushToTalk : VoiceMode.OpenMic;

        private static float ClampSensitivity(float value)
            => Mathf.Clamp(value, UserSettingsLimits.MinMouseSensitivity, UserSettingsLimits.MaxMouseSensitivity);
    }
}
