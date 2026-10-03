namespace GhostHunter.Core.Voice
{
    /// <summary>화자 한 명의 압축 블록을 mono PCM 으로 푼다. 화자마다 한 개를 쓴다.</summary>
    public interface IVoiceDecoder
    {
        /// <summary>블록을 풀어 <paramref name="samples"/> 앞에서부터 채우고 샘플 수를 돌려준다. 손상된 블록이면 0.</summary>
        int Decode(byte[] compressed, int count, float[] samples);
    }
}
