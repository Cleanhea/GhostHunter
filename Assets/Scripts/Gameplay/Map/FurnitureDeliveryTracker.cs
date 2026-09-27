using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>드릴카 반출 구역에서 목표 가구의 완료 상태를 서버가 확정한다.</summary>
    [DisallowMultipleComponent]
    public sealed class FurnitureDeliveryTracker : MonoBehaviour
    {
        private FurnitureSpawnController _furniture;

        private void FixedUpdate()
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer
                || GhostHunter.Gameplay.Recovery.StageRecoveryGate.Restoring)
                return;

            if (_furniture == null)
                _furniture = FindFirstObjectByType<FurnitureSpawnController>();
            if (_furniture == null || !_furniture.IsReady)
                return;

            RandomFurnitureItem[] items = _furniture.Items;
            for (int i = 0; i < items.Length; i++)
                if (items[i] != null && items[i].IsWorkTarget)
                    items[i].ServerCompleteDelivery();
        }
    }
}
