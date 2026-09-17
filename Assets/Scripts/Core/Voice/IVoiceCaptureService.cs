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
    }
}
