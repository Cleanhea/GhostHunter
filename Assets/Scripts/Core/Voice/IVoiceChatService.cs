using System.Collections.Generic;

namespace GhostHunter.Core.Voice
{
    /// <summary>게임 세션의 음성 설정·참가자·개발용 캡처 교체 계약.</summary>
    public interface IVoiceChatService
    {
        VoiceMode Mode { get; set; }
        bool IsMuted { get; set; }
        float MasterVolume { get; set; }
        /// <summary>지금 마이크 소리를 보내는 중인가. 오픈 마이크는 켜져 있는 동안 항상, PTT 는 누르는 동안.</summary>
        bool IsTransmitting { get; set; }
        /// <summary>Game, Result 또는 Lobby 씬에서 음성을 주고받는다.</summary>
        bool IsActive { get; }
        /// <summary>Result·Lobby에서는 생존 여부와 거리 제한 없이 모두 같은 음성 채널을 쓴다.</summary>
        bool IsResultChannel { get; }
        IVoiceCaptureService Capture { get; }
        IVoiceCaptureService TestCapture { get; set; }
        IVoiceCaptureService TestDecoder { get; set; }
        /// <summary>개발용 자가 모니터. 켜면 내 목소리가 서버를 거쳐 <b>켠 자리</b>에서 되돌아온다 —
        /// 혼자서 거리·벽·층 감쇠를 확인하는 수단이다. 릴리스 빌드에서는 무시된다.</summary>
        bool SelfMonitor { get; set; }
        /// <summary>개발 HUD가 보여줄 한 줄 진단(입력 레벨·발화 감지·자가 모니터 수치). 소유자만 쓴다.</summary>
        string Diagnostics { get; set; }
        IVoiceParticipant LocalParticipant { get; }
        IReadOnlyList<IVoiceParticipant> Participants { get; }
        void Register(IVoiceParticipant participant, bool local);
        void Unregister(IVoiceParticipant participant);
    }
}

