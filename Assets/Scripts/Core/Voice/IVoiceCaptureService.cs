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
        /// <summary>입력 장치·게인은 플랫폼이 쥐고 있다(기획 §5.1 제약 1). 열 수 없으면 false.</summary>
        bool OpenSettings();
    }
}
