using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.FurnitureDriver
{
    /// <summary>분해 가능한 큰 가구 6종(MD-1)의 레시피 목록. 서버·조립 판정이 공유한다.</summary>
    [CreateAssetMenu(fileName = "FurnitureDriverCatalog_", menuName = "GhostHunter/Furniture Driver/Catalog")]
    public sealed class FurnitureDriverCatalog : ScriptableObject
    {
        [SerializeField] private FurnitureDisassemblyRecipe[] _recipes = System.Array.Empty<FurnitureDisassemblyRecipe>();

        public IReadOnlyList<FurnitureDisassemblyRecipe> Recipes => _recipes;

        /// <summary>에디터 설치 도구가 레시피 목록을 지정한다.</summary>
        public void Configure(FurnitureDisassemblyRecipe[] recipes) =>
            _recipes = recipes ?? System.Array.Empty<FurnitureDisassemblyRecipe>();

        public FurnitureDisassemblyRecipe FindByLargeFurnitureId(string largeFurnitureId)
        {
            foreach (FurnitureDisassemblyRecipe recipe in _recipes)
                if (recipe != null && recipe.LargeFurnitureId == largeFurnitureId)
                    return recipe;
            return null;
        }
    }
}
