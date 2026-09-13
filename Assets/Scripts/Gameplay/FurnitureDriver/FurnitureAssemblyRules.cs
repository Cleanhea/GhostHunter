using System.Collections.Generic;

namespace GhostHunter.Gameplay.FurnitureDriver
{
    public enum FurnitureAssemblyState
    {
        /// <summary>조립 영역이 비었다 — 실루엣 없음.</summary>
        Empty,
        /// <summary>부품이 일부만 있다 — 흰색 실루엣(§6.4).</summary>
        Partial,
        /// <summary>올바른 부품이 전부 있다 — 초록 실루엣, 조립 가능(§6.4).</summary>
        Ready,
        /// <summary>다른 가구 혼입 또는 개수 초과 — 붉은 실루엣, 조립 불가(§4.3 규칙 1).</summary>
        Invalid,
    }

    public readonly struct FurnitureAssemblyEvaluation
    {
        public FurnitureAssemblyState State { get; }
        public FurnitureDisassemblyRecipe MatchedRecipe { get; }

        public FurnitureAssemblyEvaluation(FurnitureAssemblyState state, FurnitureDisassemblyRecipe matchedRecipe)
        {
            State = state;
            MatchedRecipe = matchedRecipe;
        }

        public static readonly FurnitureAssemblyEvaluation EmptyResult =
            new(FurnitureAssemblyState.Empty, null);
        public static readonly FurnitureAssemblyEvaluation InvalidResult =
            new(FurnitureAssemblyState.Invalid, null);
    }

    /// <summary>
    /// 조립 성립 판정을 순수 함수로 분리했다(기획서 §4.3). 부품의 출처(어느 개체에서 나왔는지)는
    /// 입력에 아예 없다 — 조립 영역에 놓인 부품 ID별 개수만 본다. 이것이 규칙 2(같은 종류 부품은
    /// 출처를 구분하지 않는다)를 자동으로 만족시키는 방식이다: 부품 ID가 이미 "같은 종류"의
    /// 단위이므로 어떤 개체에서 나왔는지는 애초에 판정에 들어오지 않는다.
    /// </summary>
    public static class FurnitureAssemblyRules
    {
        /// <summary>
        /// <paramref name="presentPartCounts"/>(영역 안 부품 ID → 개수)를 <paramref name="recipes"/>와
        /// 대조한다. 0 이하 개수는 무시한다.
        /// </summary>
        public static FurnitureAssemblyEvaluation Evaluate(
            IReadOnlyDictionary<string, int> presentPartCounts,
            IReadOnlyList<FurnitureDisassemblyRecipe> recipes)
        {
            bool hasAny = false;
            if (presentPartCounts != null)
            {
                foreach (KeyValuePair<string, int> entry in presentPartCounts)
                {
                    if (entry.Value > 0)
                    {
                        hasAny = true;
                        break;
                    }
                }
            }

            if (!hasAny)
                return FurnitureAssemblyEvaluation.EmptyResult;

            if (recipes == null)
                return FurnitureAssemblyEvaluation.InvalidResult;

            // 후보 레시피 — 영역에 있는 모든 부품이 그 레시피에 속하고, 어떤 부품도 요구 개수를
            // 넘지 않아야 한다. 하나라도 어긋나면(다른 가구 혼입 또는 개수 초과) 후보에서 빠진다.
            foreach (FurnitureDisassemblyRecipe recipe in recipes)
            {
                if (recipe == null || !IsCandidate(recipe, presentPartCounts))
                    continue;

                return IsComplete(recipe, presentPartCounts)
                    ? new FurnitureAssemblyEvaluation(FurnitureAssemblyState.Ready, recipe)
                    : new FurnitureAssemblyEvaluation(FurnitureAssemblyState.Partial, recipe);
            }

            return FurnitureAssemblyEvaluation.InvalidResult;
        }

        private static bool IsCandidate(FurnitureDisassemblyRecipe recipe,
            IReadOnlyDictionary<string, int> presentPartCounts)
        {
            foreach (KeyValuePair<string, int> entry in presentPartCounts)
            {
                if (entry.Value <= 0)
                    continue;
                int required = recipe.RequiredCount(entry.Key);
                if (required <= 0 || entry.Value > required)
                    return false;
            }
            return true;
        }

        private static bool IsComplete(FurnitureDisassemblyRecipe recipe,
            IReadOnlyDictionary<string, int> presentPartCounts)
        {
            foreach (FurniturePartRequirement requirement in recipe.Parts)
            {
                presentPartCounts.TryGetValue(requirement.PartId, out int present);
                if (present != requirement.Count)
                    return false;
            }
            return true;
        }
    }
}
