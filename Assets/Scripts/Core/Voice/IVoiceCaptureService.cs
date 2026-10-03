namespace GhostHunter.Core.Voice
{
    /// <summary>플랫폼 타입 없이 압축 음성과 mono PCM을 교환한다. 버퍼는 호출자가 소유한다.</summary>
    public interface IVoiceCaptureService
    {
        bool IsAvailable { get; }
        bool IsRecording { get; }
        string Status { get; }
        byte Codec { get; }
        int SampleRate { get; }
        void SetRecording(bool recording);
        int ReadFrame(byte[] destination);
        int Decode(byte[] compressed, int count, float[] samples);
        /// <summary>최근에 읽힌 마이크 입력 크기(dBFS). 녹음 중이 아니거나 한동안 읽힌 프레임이 없으면 -120.
        /// 누가 <see cref="ReadFrame"/> 을 부르든 갱신된다 — 설정 창의 입력 막대가 이 값을 읽는다.</summary>
        float InputLevelDb { get; }
        /// <summary>입력 장치·게인은 플랫폼이 쥐고 있다(기획 §5.1 제약 1). 열 수 없으면 false.</summary>
        bool OpenSettings();
    }
}
