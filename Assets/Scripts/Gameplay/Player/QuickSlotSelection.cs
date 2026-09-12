using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 누적 포인터 벡터를 퀵슬롯 휠의 인덱스로 바꾸는 순수 계산. UI·입력과 분리해 EditMode에서
    /// 검증한다. 12시 방향이 0번이고 시계 방향으로 증가하며, 각 슬롯의 경계는 슬롯 중심
    /// ±(360/N/2)다.
    /// </summary>
    public static class QuickSlotSelection
    {
        /// <param name="pointer">누적된 포인터 벡터(화면 픽셀 단위). y+가 12시 방향이다.</param>
        /// <param name="slotCount">휠 슬롯 개수.</param>
        /// <param name="deadZone">이 길이 미만이면 선택 없음(-1)으로 취급한다.</param>
        /// <param name="currentIndex">확정(장착)된 마지막 인덱스. 데드존 안에서는 참조하지 않는다
        /// — "변경 없이 닫힌다"는 이 함수가 아니라 확정 단계에서 -1을 무시해 구현한다.</param>
        public static int Resolve(Vector2 pointer, int slotCount, float deadZone, int currentIndex)
        {
            if (slotCount <= 0)
                return -1;

            if (pointer.magnitude < deadZone)
                return -1;

            // atan2(x, y) 는 12시(양의 Y축) 기준 시계 방향 각도를 [-180, 180] 도로 준다.
            float angle = Mathf.Atan2(pointer.x, pointer.y) * Mathf.Rad2Deg;
            if (angle < 0f)
                angle += 360f;

            float slotSize = 360f / slotCount;
            int index = Mathf.FloorToInt((angle + slotSize * 0.5f) / slotSize) % slotCount;
            return index;
        }
    }
}
