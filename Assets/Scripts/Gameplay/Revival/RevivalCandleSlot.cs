using UnityEngine;

namespace GhostHunter.Gameplay.Revival
{
    /// <summary>
    /// 소환진의 촛불 자리 하나 — 각 피어가 로컬로 만드는 표시물이다. <c>PlayerInteractor</c> 가 조준해서 E 를 넘긴다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RevivalCandleSlot : MonoBehaviour
    {
        public RevivalRitual Ritual { get; private set; }
        public int Index { get; private set; }

        public void Bind(RevivalRitual ritual, int index)
        {
            Ritual = ritual;
            Index = index;
        }

        /// <summary>HUD 프롬프트. 빈 문자열이면 표시하지 않는다.</summary>
        public string Prompt => Ritual != null ? Ritual.PromptFor(Index) : string.Empty;

        public void RequestInteract()
        {
            if (Ritual != null)
                Ritual.RequestInteract(Index);
        }
    }
}
