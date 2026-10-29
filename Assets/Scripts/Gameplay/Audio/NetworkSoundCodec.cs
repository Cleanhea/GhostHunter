using UnityEngine;

namespace GhostHunter.Gameplay.Audio
{
    /// <summary>소리 알림 RPC 의 음량을 1바이트로 줄이고 되돌린다.</summary>
    public static class NetworkSoundCodec
    {
        public static byte QuantizeVolume(float volume) =>
            (byte)Mathf.RoundToInt(Mathf.Clamp01(volume) * byte.MaxValue);

        public static float DequantizeVolume(byte volume) => volume / (float)byte.MaxValue;
    }
}
