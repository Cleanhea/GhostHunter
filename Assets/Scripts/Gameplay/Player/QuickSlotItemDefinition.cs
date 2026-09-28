using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 퀵슬롯 항목의 표시 정보와 도구 종류(대걸레·드라이버·라이터)를 정의한다.
    /// </summary>
    [CreateAssetMenu(fileName = "QuickSlotItem_", menuName = "GhostHunter/QuickSlot/Item Definition")]
    public sealed class QuickSlotItemDefinition : ScriptableObject
    {
        [SerializeField] private string _id = "item";
        [SerializeField] private string _displayName = "아이템";
        [SerializeField, TextArea] private string _description = "";
        [SerializeField] private Sprite _icon;
        [SerializeField] private bool _isMop;
        [SerializeField] private bool _isDriver;

        [Tooltip("들고 있는 동안 불이 붙는 라이터(PlayerLighter) — docs/project/lighter-system.md.")]
        [SerializeField] private bool _isLighter;

        [Tooltip("아이콘 에셋이 없을 때(QS-10) 대신 그리는 단색 도형 자리표시자 색.")]
        [SerializeField] private Color _placeholderColor = Color.white;

        public string Id => _id;
        public string DisplayName => _displayName;
        public string Description => _description;
        public Sprite Icon => _icon;
        public bool IsMop => _isMop;
        public bool IsDriver => _isDriver;
        public bool IsLighter => _isLighter;
        public Color PlaceholderColor => _placeholderColor;
    }
}
