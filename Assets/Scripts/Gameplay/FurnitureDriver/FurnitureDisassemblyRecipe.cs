using UnityEngine;

namespace GhostHunter.Gameplay.FurnitureDriver
{
    /// <summary>
    /// 큰 가구 1종의 분해·조립 규격(기획서 §4·MD-1, 2026-09-12 사용자 확정 6종).
    /// <see cref="LargeFurnitureId"/>는 <see cref="FurnitureDriverPoolItem.PoolKey"/>와 맞춰
    /// 씬의 큰 가구 인스턴스를 식별한다.
    /// </summary>
    [CreateAssetMenu(fileName = "FurnitureDisassemblyRecipe_", menuName = "GhostHunter/Furniture Driver/Disassembly Recipe")]
    public sealed class FurnitureDisassemblyRecipe : ScriptableObject
    {
        [SerializeField] private string _largeFurnitureId = "";
        [SerializeField] private string _displayName = "";
        [SerializeField] private FurniturePartRequirement[] _parts = System.Array.Empty<FurniturePartRequirement>();

        public string LargeFurnitureId => _largeFurnitureId;
        public string DisplayName => _displayName;
        public FurniturePartRequirement[] Parts => _parts;

        /// <summary>에디터 설치 도구·테스트가 레시피 내용을 지정한다.</summary>
        public void Configure(string largeFurnitureId, string displayName, FurniturePartRequirement[] parts)
        {
            _largeFurnitureId = largeFurnitureId;
            _displayName = displayName;
            _parts = parts ?? System.Array.Empty<FurniturePartRequirement>();
        }

        public int RequiredCount(string partId)
        {
            foreach (FurniturePartRequirement requirement in _parts)
                if (requirement.PartId == partId)
                    return requirement.Count;
            return 0;
        }

        public bool ContainsPart(string partId) => RequiredCount(partId) > 0;
    }
}
