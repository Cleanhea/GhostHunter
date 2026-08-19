using GhostHunter.Core.Player;
using UnityEngine;

namespace GhostHunter.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerSpawnRegistry : MonoBehaviour, IPlayerSpawnRegistry
    {
        [SerializeField] private Transform[] _spawnPoints;

        public bool TryGetSpawn(ulong clientId, out Vector3 position, out Quaternion rotation)
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0)
            {
                position = default;
                rotation = default;
                return false;
            }

            Transform spawn = _spawnPoints[(int)(clientId % (ulong)_spawnPoints.Length)];
            position = spawn.position;
            rotation = spawn.rotation;
            return true;
        }
    }
}
