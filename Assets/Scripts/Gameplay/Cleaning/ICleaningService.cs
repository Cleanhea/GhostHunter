using UnityEngine;

namespace GhostHunter.Gameplay.Cleaning
{
    /// <summary>플레이어의 얼룩 조준과 디버그 HUD의 초기화 경로를 제공한다.</summary>
    public interface ICleaningService
    {
        bool CanReset { get; }
        int DirtyCount { get; }
        int ProgressPercent { get; }
        string Status { get; }

        /// <summary>얼룩 닦기·목표 가구 반출 두 작업의 현재 수치. 클라이언트에서도 복제 상태로 읽는다.</summary>
        CleaningTaskProgress TaskProgress { get; }

        bool TryRaycast(Vector3 origin, Vector3 direction, float distance, out CleaningStain stain,
            Transform ignoredRoot = null);
        bool IsObstructed(Vector3 origin, Vector3 destination, Transform ignoredRoot);
        void ResetStains();

        /// <summary>서버 전용 — 이 영역 바닥에는 얼룩을 놓지 않는다(부활 의식 방). 배치 전에 부른다.</summary>
        void ServerExcludeArea(System.Func<Vector3, bool> contains);
    }
}
