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
        IVoiceParticipant LocalParticipant { get; }
        IReadOnlyList<IVoiceParticipant> Participants { get; }
        void Register(IVoiceParticipant participant, bool local);
        void Unregister(IVoiceParticipant participant);
    }
}

