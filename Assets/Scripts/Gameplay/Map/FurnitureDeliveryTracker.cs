using System.Collections.Generic;
using GhostHunter.Gameplay.FurnitureDriver;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>
    /// 드릴카 반출 구역에서 목표 가구의 완료 상태를 서버가 확정한다. 랜덤 풀 목표 가구와
    /// 분해 가능 목표 가구(짐더미·온전한 모양 모두, MD-3)를 함께 본다.
    /// </summary>
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

            IReadOnlyList<FurnitureDriverPoolItem> pool = FurnitureDriverPoolItem.All;
            for (int i = pool.Count - 1; i >= 0; i--)
                if (pool[i] != null && pool[i].IsWorkTarget)
                    pool[i].ServerCompleteDelivery();
        }
    }
}
