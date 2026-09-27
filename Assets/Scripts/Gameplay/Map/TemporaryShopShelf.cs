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

            GameObject shelf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shelf.name = "SharedItemShelf";
            shelf.transform.SetParent(zone.transform, false);
            shelf.transform.localPosition = new Vector3(0f,
                -zone.Size.y * 0.5f + 0.7f, 0f);
            shelf.transform.localScale = new Vector3(2.8f, 0.1f, 0.5f);
            Destroy(shelf.GetComponent<Collider>());

            int slot = 0;
            for (int item = 1; item <= 3; item++)
            {
                int count = lobby.GetPurchasedTempItemCount(item);
                for (int index = 0; index < count; index++)
                {
                    GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    box.name = $"Temp{item}_Shared_{index}";
                    box.transform.SetParent(zone.transform, false);
                    box.transform.localPosition = new Vector3(-1.2f + slot * 0.4f,
                        -zone.Size.y * 0.5f + 0.9f, 0f);
                    box.transform.localScale = Vector3.one * 0.2f;
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
