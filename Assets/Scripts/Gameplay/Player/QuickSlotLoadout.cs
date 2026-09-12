using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 퀵슬롯 휠에 표시할 항목 배열. 빈 슬롯(null)을 허용한다 — 가리킬 수는 있으나
    /// 확정(장착)할 수 없다(QS-2, 사용자 확정 2026-09-12).
    /// </summary>
    [CreateAssetMenu(fileName = "QuickSlotLoadout_", menuName = "GhostHunter/QuickSlot/Loadout")]
    public sealed class QuickSlotLoadout : ScriptableObject
    {
        [SerializeField] private QuickSlotItemDefinition[] _slots = new QuickSlotItemDefinition[4];

        public int SlotCount => _slots.Length;

        public QuickSlotItemDefinition GetSlot(int index)
        {
            if (index < 0 || index >= _slots.Length)
                return null;

            return _slots[index];
        }
    }
}
