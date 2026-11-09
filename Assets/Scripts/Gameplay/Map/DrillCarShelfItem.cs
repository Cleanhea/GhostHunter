using GhostHunter.Core.Steam;
using UnityEngine;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>
    /// 드릴카 선반에 쌓인 아이템 하나의 표시물(<see cref="DrillCarItemShelf"/> 가 만든다). 조준하고 E 를 누르면
    /// <c>PlayerInteractor</c> 가 서버에 그 품목을 꺼내 달라고 요청한다. 네트워크 상태가 없는 로컬 표시물이다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DrillCarShelfItem : MonoBehaviour
    {
        public ShopItem Item { get; private set; }

        public DrillCarItemShelf Shelf { get; private set; }

        public string Prompt => Shelf != null
            ? $"E: {StageShopRules.NameOf(Item)} 꺼내기 (선반에 {Shelf.CountOf(Item)}개)"
            : string.Empty;

        internal void Initialize(DrillCarItemShelf shelf, ShopItem item)
        {
            Shelf = shelf;
            Item = item;
        }
    }
}
