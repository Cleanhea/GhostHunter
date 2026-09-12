using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 퀵슬롯 한 칸에 담기는 항목의 표시용 데이터. 인벤토리/아이템 시스템이 아직 없어(사용자
    /// 확정 2026-09-12) 지금은 휠 UI가 읽는 더미 데이터로만 쓰인다 — 장착·소모 로직은 없다.
    /// </summary>
    [CreateAssetMenu(fileName = "QuickSlotItem_", menuName = "GhostHunter/QuickSlot/Item Definition")]
    public sealed class QuickSlotItemDefinition : ScriptableObject
    {
        [SerializeField] private string _id = "item";
        [SerializeField] private string _displayName = "아이템";
        [SerializeField, TextArea] private string _description = "";
        [SerializeField] private Sprite _icon;

        [Tooltip("아이콘 에셋이 없을 때(QS-10) 대신 그리는 단색 도형 자리표시자 색.")]
        [SerializeField] private Color _placeholderColor = Color.white;

        public string Id => _id;
        public string DisplayName => _displayName;
        public string Description => _description;
        public Sprite Icon => _icon;
        public Color PlaceholderColor => _placeholderColor;
    }
}
