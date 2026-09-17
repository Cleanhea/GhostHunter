using System.Collections.Generic;

namespace GhostHunter.Core.Voice
{
    /// <summary>게임 세션의 음성 설정·참가자·개발용 캡처 교체 계약.</summary>
    public interface IVoiceChatService
    {
        VoiceMode Mode { get; set; }
        bool IsMuted { get; set; }
        float MasterVolume { get; set; }
        float OpenThreshold { get; set; }
        bool IsTransmitting { get; set; }
        IVoiceCaptureService Capture { get; }
        IVoiceCaptureService TestCapture { get; set; }
        IVoiceCaptureService TestDecoder { get; set; }
        /// <summary>개발용 자가 모니터. 켜면 내 목소리가 서버를 거쳐 <b>켠 자리</b>에서 되돌아온다 —
        /// 혼자서 거리·벽·층 감쇠를 확인하는 수단이다. 릴리스 빌드에서는 무시된다.</summary>
        bool SelfMonitor { get; set; }
        /// <summary>개발 HUD가 보여줄 한 줄 진단(입력 레벨·게이트·자가 모니터 수치). 소유자만 쓴다.</summary>
        string Diagnostics { get; set; }
        IVoiceParticipant LocalParticipant { get; }
        IReadOnlyList<IVoiceParticipant> Participants { get; }
        void Register(IVoiceParticipant participant, bool local);
        void Unregister(IVoiceParticipant participant);
    }
}

