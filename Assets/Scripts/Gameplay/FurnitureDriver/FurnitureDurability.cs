using System.Collections.Generic;

namespace GhostHunter.Gameplay.FurnitureDriver
{
    /// <summary>
    /// 가구·아이템 내구도 계산을 순수 함수로 분리했다(기획서 §3.3·§5). 네트워크 상태는 이 값을
    /// 호출부(서버)에서 읽고 쓸 뿐, 계산 자체는 여기서 검증한다.
    /// </summary>
    public static class FurnitureDurability
    {
        /// <summary>1회 사용 성공 시 아이템 내구도 감소(§3.3) — 0 미만으로 내려가지 않는다.</summary>
        public static int ApplyItemUse(int current, int decreasePerUse) =>
            current <= 0 ? 0 : System.Math.Max(0, current - decreasePerUse);

        /// <summary>분해 시 모든 부품이 큰 가구와 동일한 내구도를 물려받는다(§5).</summary>
        public static int InheritOnDisassemble(int largeFurnitureDurability) => largeFurnitureDurability;

        /// <summary>조립 시 완성 가구 내구도는 사용된 부품 내구도의 평균(소수점 버림, §5).</summary>
        public static int AverageOnAssemble(IReadOnlyList<int> partDurabilities)
        {
            if (partDurabilities == null || partDurabilities.Count == 0)
                return 0;
            int sum = 0;
            foreach (int value in partDurabilities)
                sum += value;
            return sum / partDurabilities.Count;
        }
    }
}
