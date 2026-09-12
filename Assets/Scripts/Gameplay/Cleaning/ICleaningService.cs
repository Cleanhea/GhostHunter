using UnityEngine;

namespace GhostHunter.Gameplay.Cleaning
{
    /// <summary>플레이어의 얼룩 조준과 디버그 HUD의 초기화 경로를 제공한다.</summary>
    public interface ICleaningService
    {
        bool CanReset { get; }
        int DirtyCount { get; }
        string Status { get; }
        bool TryRaycast(Vector3 origin, Vector3 direction, float distance, out CleaningStain stain,
            Transform ignoredRoot = null);
        bool IsObstructed(Vector3 origin, Vector3 destination, Transform ignoredRoot);
        void ResetStains();
    }
}
