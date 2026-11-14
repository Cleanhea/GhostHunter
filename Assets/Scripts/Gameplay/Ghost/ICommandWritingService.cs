using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 인커 귀신의 명령 글씨를 대걸레로 닦는 경로(project/ghost-inker.md §3, IK-7). 인커 귀신이 스폰된 동안만
    /// 각 피어에 등록된다. 대걸레(<c>PlayerCleaningController</c>)는 얼룩을 못 맞혔을 때 이 서비스로 글씨를 찾는다.
    /// </summary>
    public interface ICommandWritingService
    {
        /// <summary>다 쓴 글씨 중 광선이 앞면에 닿고 가리지 않은 가장 가까운 것. 클라이언트 조준용.</summary>
        bool TryRaycast(Vector3 origin, Vector3 direction, float distance, Transform ignoredRoot, out int writingId);

        /// <summary>서버 전용 — 서버 기준으로 다시 맞혀 보고 같은 글씨면 한 번 닦는다. 다 닦이면 명령이 풀린다.</summary>
        bool ServerTryWipe(int writingId, Vector3 origin, Vector3 direction, float distance, Transform ignoredRoot);
    }
}
