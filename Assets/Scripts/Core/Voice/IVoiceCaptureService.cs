namespace GhostHunter.Core.Voice
{
    /// <summary>
    /// 마이크 캡처와 압축 블록 교환. 플랫폼·코덱 타입을 노출하지 않는다. 버퍼는 호출자가 소유한다.
    /// 입력 장치·게인·노이즈 게이트는 개인 설정(IUserSettings)을 따른다 → docs/architecture/voice-chat.md
    /// </summary>
    public interface IVoiceCaptureService
    {
        bool IsAvailable { get; }
        bool IsRecording { get; }
        string Status { get; }
        byte Codec { get; }
        int SampleRate { get; }
        void SetRecording(bool recording);
        /// <summary>쌓인 압축 음성을 한 블록으로 꺼낸다. 없으면 0.</summary>
        int ReadFrame(byte[] destination);
        /// <summary>화자 한 명용 디코더. 코덱이 상태를 가지므로 화자마다 따로 만든다.</summary>
        IVoiceDecoder CreateDecoder();
        /// <summary>최근 마이크 입력 크기(dBFS, 게인 적용 후). 녹음 중이 아니거나 조용하면 -120.</summary>
        float InputLevelDb { get; }
        /// <summary>노이즈 게이트가 지금 소리를 통과시키는가. 게이트를 끄거나 눌러서 말하기 모드면 녹음 중 항상 true.</summary>
        bool IsGateOpen { get; }
        /// <summary>처리된 내 목소리(다른 사람이 듣는 소리)를 내 스피커로 듣는다. 설정 창의 마이크 테스트용.</summary>
        bool IsMonitoring { get; set; }
    }
}
