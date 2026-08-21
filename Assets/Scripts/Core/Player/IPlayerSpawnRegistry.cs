using UnityEngine;

namespace GhostHunter.Core.Player
{
    /// <summary>클라이언트 ID에 대응하는 게임 씬의 플레이어 시작 위치를 제공한다.</summary>
    public interface IPlayerSpawnRegistry
    {
        /// <param name="spawning">
        /// 자리를 잡으려는 플레이어의 루트. 빈자리 판정에서 <b>자기 콜라이더는 빼기 위해</b>
        /// 넘긴다 — 플레이어 오브젝트는 자리를 정하기 전에 이미 씬에 만들어져 있다.
        /// null 을 넘겨도 되지만, 그러면 자기 자신 때문에 모든 자리가 막힌 것처럼 보일 수 있다.
        /// </param>
        bool TryGetSpawn(
            ulong clientId,
            Transform spawning,
            out Vector3 position,
            out Quaternion rotation);
    }
}
