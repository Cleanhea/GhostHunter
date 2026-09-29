using GhostHunter.Core;
using GhostHunter.Core.Steam;
using GhostHunter.Gameplay.Ghost;
using UnityEngine;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>팀 촛불 재고를 드릴카 선반에 표시한다. 촛불은 부활 의식(<c>RevivalRitual</c>)이 재고 수로 꺼내 쓴다.</summary>
    [DisallowMultipleComponent]
    public sealed class TemporaryShopShelf : MonoBehaviour
    {
        private void Start()
        {
            if (!Services.TryGet(out IStageShopService shop) || !shop.IsAvailable)
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

            // 공동 품목은 지금 촛불뿐이다. 스테이지 시작 때 재고를 촛불 하나당 작은 원기둥으로 늘어놓는다(표시만 —
            // 의식은 재고 수로 꺼내 쓴다). 선반을 넘치지 않게 20개까지만 그린다.
            int count = Mathf.Min(shop.CandleCount, 20);
            for (int index = 0; index < count; index++)
            {
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                box.name = $"Candle_Shared_{index}";
                box.transform.SetParent(parent, false);
                box.transform.localPosition = firstSlot + Vector3.right * (index * slotSpacing * 0.5f);
                box.transform.localScale = new Vector3(boxSize * 0.4f, boxSize * 0.5f, boxSize * 0.4f);
                box.GetComponent<Renderer>().material.color = new Color(1f, 0.85f, 0.4f);
                Destroy(box.GetComponent<Collider>());
            }
        }
    }
}
