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
            if (!IsServer || PlayerSpawnRegistry.Instance == null)
                return;

            if (!PlayerSpawnRegistry.Instance.TryGetSpawn(OwnerClientId, out Vector3 position, out Quaternion rotation))
                return;

            if (IsOwner)
                ApplySpawn(position, rotation);

            var sendParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { OwnerClientId },
                },
            };

            ApplySpawnClientRpc(position, rotation, sendParams);
        }

        [ClientRpc]
        private void ApplySpawnClientRpc(
            Vector3 position,
            Quaternion rotation,
            ClientRpcParams clientRpcParams = default)
        {
            if (IsOwner)
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
