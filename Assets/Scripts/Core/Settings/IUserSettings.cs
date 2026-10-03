using System;
using GhostHunter.Core.Voice;

namespace GhostHunter.Core.Settings
{
    /// <summary>
    /// 플레이어 개인 설정 — 음량·마이크·감도·화면. 앱 수명 동안 하나이고 BootstrapInstaller 가 등록한다.
    /// 값은 바꾸는 즉시 적용되고, <see cref="Save"/> 를 불러야 디스크에 남는다.
    /// 화면 모드·해상도는 Unity 가 스스로 저장하므로 여기 두지 않는다 → docs/architecture/settings-menu.md
    /// </summary>
    public interface IUserSettings
    {
        /// <summary>게임 전체 음량 0~1. 목소리를 포함한 모든 소리에 곱해진다.</summary>
        float MasterVolume { get; set; }

        /// <summary>다른 플레이어 목소리 음량 0~1.</summary>
        float VoiceVolume { get; set; }

        VoiceMode VoiceMode { get; set; }

        /// <summary>내 마이크를 끈다. 게임 중 M 키와 같은 값이다.</summary>
        bool MicMuted { get; set; }

        /// <summary>입력 장치 이름. 빈 문자열은 운영체제 기본 장치다.</summary>
        string MicDevice { get; set; }

        /// <summary>마이크 입력 게인(dB). 0 은 그대로.</summary>
        float MicGainDb { get; set; }

        /// <summary>오픈 마이크일 때 기준보다 작은 소리(키보드·잡음)를 보내지 않는다.</summary>
        bool NoiseGateEnabled { get; set; }

        /// <summary>노이즈 게이트가 열리는 크기(dBFS, 게인 적용 후).</summary>
        float NoiseGateThresholdDb { get; set; }

        /// <summary>마우스 시점 감도 배율. 설정 에셋의 기본 감도에 곱한다.</summary>
        float MouseSensitivity { get; set; }

        bool InvertMouseY { get; set; }

        bool VSync { get; set; }

        /// <summary>초당 프레임 상한. 0 은 무제한. 수직 동기화가 켜져 있으면 무시된다.</summary>
        int FrameRateLimit { get; set; }

        /// <summary>어떤 값이든 바뀌면 알린다.</summary>
        event Action Changed;

        void ResetToDefaults(UserSettingsGroup group);

        void Save();
    }
}
