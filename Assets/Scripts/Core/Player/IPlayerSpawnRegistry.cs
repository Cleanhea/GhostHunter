using UnityEngine;

namespace GhostHunter.Core.Player
{
    /// <summary>클라이언트 ID에 대응하는 게임 씬의 플레이어 시작 위치를 제공한다.</summary>
    public interface IPlayerSpawnRegistry
    {
        bool TryGetSpawn(ulong clientId, out Vector3 position, out Quaternion rotation);
    }
}
