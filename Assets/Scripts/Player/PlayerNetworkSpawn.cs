using GhostHunter.Core;
using GhostHunter.Core.Player;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerNetworkSpawn : NetworkBehaviour
    {
        [SerializeField] private PlayerMotor _motor;

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                return;

            IPlayerSpawnRegistry spawnRegistry = Services.Get<IPlayerSpawnRegistry>();
            if (!spawnRegistry.TryGetSpawn(OwnerClientId, out Vector3 position, out Quaternion rotation))
                return;

            // SendTo.Owner 는 호스트가 자기 플레이어의 Owner 일 때도 그대로 도달한다.
            ApplySpawnRpc(position, rotation);
        }

        [Rpc(SendTo.Owner)]
        private void ApplySpawnRpc(Vector3 position, Quaternion rotation)
        {
            ApplySpawn(position, rotation);
        }

        private void ApplySpawn(Vector3 position, Quaternion rotation)
        {
            if (_motor != null)
                _motor.Teleport(position, rotation);
            else
                transform.SetPositionAndRotation(position, rotation);
        }
    }
}
