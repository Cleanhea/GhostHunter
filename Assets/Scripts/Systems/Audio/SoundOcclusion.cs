using UnityEngine;

namespace GhostHunter.Systems.Audio
{
    /// <summary>벽을 몇 겹 지났는지에서 음량 배율과 저역 통과 차단 주파수를 계산한다.</summary>
    public static class SoundOcclusion
    {
        public static float Gain(int walls, float factor, int maximumWalls, float floor) =>
            Mathf.Max(floor, Mathf.Pow(factor, Mathf.Clamp(walls, 0, maximumWalls)));

        public static float Cutoff(int walls, float openCutoff, float blockedCutoff, float factor, int maximumWalls) =>
            Mathf.Max(blockedCutoff, openCutoff * Mathf.Pow(factor, Mathf.Clamp(walls, 0, maximumWalls)));
    }
}
