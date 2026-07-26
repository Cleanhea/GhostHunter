using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.Furniture
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class DevFurnitureSpawner : NetworkBehaviour
    {
        [SerializeField] private NetworkObject _lightFurniturePrefab;
        [SerializeField] private NetworkObject _heavyFurniturePrefab;
        [SerializeField] private Transform[] _spawnPoints;
        [SerializeField] private Key _resetKey = Key.R;

        private readonly List<NetworkObject> _spawnedFurniture = new();

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
            {
                enabled = false;
                return;
            }

            SpawnAll();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard[_resetKey].wasPressedThisFrame)
                ResetAll();
        }

        private void SpawnAll()
        {
            if (_spawnPoints == null)
                return;

            for (int i = 0; i < _spawnPoints.Length; i++)
            {
                NetworkObject prefab = i % 3 == 2 ? _heavyFurniturePrefab : _lightFurniturePrefab;
                Transform spawn = _spawnPoints[i];

                if (prefab == null || spawn == null)
                    continue;

                NetworkObject instance = Instantiate(prefab, spawn.position, spawn.rotation);
                instance.Spawn();
                _spawnedFurniture.Add(instance);
            }
        }

        private void ResetAll()
        {
            for (int i = _spawnedFurniture.Count - 1; i >= 0; i--)
            {
                NetworkObject furniture = _spawnedFurniture[i];
                if (furniture != null && furniture.IsSpawned)
                    furniture.Despawn(true);
            }

            _spawnedFurniture.Clear();
            SpawnAll();
        }
    }
}
