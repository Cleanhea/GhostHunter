using UnityEngine;

namespace GhostHunter.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerSpawnRegistry : MonoBehaviour
    {
        [SerializeField] private Transform[] _spawnPoints;

        public static PlayerSpawnRegistry Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

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
