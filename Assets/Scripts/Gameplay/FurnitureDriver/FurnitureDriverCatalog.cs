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

        /// <summary>부품(재료) ID가 속한 레시피. 크기가 다른 변형끼리는 부품 ID가 달라 하나로 정해진다(MD-1).</summary>
        public FurnitureDisassemblyRecipe FindByPartId(string partId)
        {
            foreach (FurnitureDisassemblyRecipe recipe in _recipes)
                if (recipe != null && recipe.ContainsPart(partId))
                    return recipe;
            return null;
        }
    }
}
