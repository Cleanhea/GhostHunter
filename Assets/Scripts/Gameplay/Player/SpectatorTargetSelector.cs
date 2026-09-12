using System.Collections.Generic;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 관전 대상 순회의 순수 로직(관전 기획서 SP-1·SP-4, 사용자 확정 2026-09-12). Unity API에
    /// 의존하지 않아 EditMode에서 그대로 테스트한다. 후보 목록은 호출자가 이미 "스폰됨·생존·
    /// 자신 제외"로 걸러 클라이언트 ID 오름차순으로 넘긴다고 가정한다.
    /// </summary>
    public static class SpectatorTargetSelector
    {
        /// <summary>현재 대상이 후보 목록에 여전히 있는가. 대상이 없거나(null) 목록이 비었으면 false.</summary>
        public static bool IsValid(IReadOnlyList<ulong> candidates, ulong? current)
        {
            return IndexOf(candidates, current) >= 0;
        }

        /// <summary>
        /// 다음 대상을 고른다. 현재 대상이 없거나 후보 목록에 없으면(무효) 목록의 첫 후보로
        /// 간다 — 대상 사망·이탈 시 "다음 생존자로 전환"과 최초 선택에 같은 규칙을 쓴다.
        /// 후보가 하나도 없으면 false — 호출자가 자유시점으로 전환해야 한다(SP-1).
        /// </summary>
        public static bool TryGetNext(IReadOnlyList<ulong> candidates, ulong? current, out ulong next)
        {
            return TryGetRelative(candidates, current, step: 1, out next);
        }

        /// <summary>이전 대상을 고른다. 무효·빈 목록 처리 규칙은 <see cref="TryGetNext"/>와 같다.</summary>
        public static bool TryGetPrevious(IReadOnlyList<ulong> candidates, ulong? current, out ulong previous)
        {
            return TryGetRelative(candidates, current, step: -1, out previous);
        }

        private static bool TryGetRelative(
            IReadOnlyList<ulong> candidates,
            ulong? current,
            int step,
            out ulong result)
        {
            result = default;
            if (candidates == null || candidates.Count == 0)
                return false;

            int currentIndex = IndexOf(candidates, current);
            if (currentIndex < 0)
            {
                result = candidates[0];
                return true;
            }

            int count = candidates.Count;
            int nextIndex = ((currentIndex + step) % count + count) % count;
            result = candidates[nextIndex];
            return true;
        }

        private static int IndexOf(IReadOnlyList<ulong> candidates, ulong? current)
        {
            if (current == null || candidates == null)
                return -1;

            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] == current.Value)
                    return i;
            }

            return -1;
        }
    }
}
