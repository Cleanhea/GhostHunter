using GhostHunter.Core;
using GhostHunter.Core.Steam;
using GhostHunter.Gameplay.Ghost;
using UnityEngine;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>구매한 임시 공동 아이템을 드릴카에 표시한다. 아이템 효과는 별도 기획 대상이다.</summary>
    [DisallowMultipleComponent]
    public sealed class TemporaryShopShelf : MonoBehaviour
    {
        private void Start()
        {
            if (!Services.TryGet(out ISteamLobbyService lobby) || !lobby.IsInLobby)
                return;

            DrillCarSafeZone zone = FindFirstObjectByType<DrillCarSafeZone>();
            if (zone == null)
                return;

            // 드릴카 모델은 실제 선반 면(앵커)에 늘어놓는다. 임시 상자는 가운데에 판을 만든다.
            Transform parent = zone.ItemShelfAnchor;
            Vector3 firstSlot = Vector3.zero;
            float slotSpacing = 0.2f;
            float boxSize = 0.15f;
            if (parent != null)
            {
                firstSlot = new Vector3(0f, boxSize * 0.5f, 0f);
            }
            else
            {
                parent = zone.transform;
                GameObject shelf = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shelf.name = "SharedItemShelf";
                shelf.transform.SetParent(parent, false);
                shelf.transform.localPosition = new Vector3(0f,
                    -zone.Size.y * 0.5f + 0.7f, 0f);
                shelf.transform.localScale = new Vector3(2.8f, 0.1f, 0.5f);
                Destroy(shelf.GetComponent<Collider>());
                firstSlot = new Vector3(-1.2f, -zone.Size.y * 0.5f + 0.9f, 0f);
                slotSpacing = 0.4f;
                boxSize = 0.2f;
            }

            int slot = 0;
            for (int item = 1; item <= 3; item++)
            {
                int count = lobby.GetPurchasedTempItemCount(item);
                for (int index = 0; index < count; index++)
                {
                    GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    box.name = $"Temp{item}_Shared_{index}";
                    box.transform.SetParent(parent, false);
                    box.transform.localPosition = firstSlot + Vector3.right * (slot * slotSpacing);
                    box.transform.localScale = Vector3.one * boxSize;
                    Renderer renderer = box.GetComponent<Renderer>();
                    renderer.material.color = item == 1 ? Color.cyan
                        : item == 2 ? Color.yellow : Color.magenta;
                    Destroy(box.GetComponent<Collider>());
                    slot++;
                }
            }
        }
    }
}
