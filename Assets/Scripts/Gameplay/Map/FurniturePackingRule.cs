using UnityEngine;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>
    /// 반출 구역의 가구 포장 판정 계산(2026-11-03 기획 "2-2. 가구 포장 처리") — 가구의 85% 이상이 구역 안에 있는 상태가
    /// 1초 이상 이어져야 한다. 씬·네트워크와 무관한 순수 계산이라 <see cref="FurnitureDeliveryTracker"/> 가 서버에서 부르고
    /// EditMode 테스트가 그대로 검증한다. 비율·시간·속력 값은 <see cref="FurnitureSpawnSettings"/> 에 있다.
    /// </summary>
    internal static class FurniturePackingRule
    {
        /// <summary>한 축당 표본 수. 가구 경계 상자를 8×8×8 칸으로 나눠 칸 중심이 구역 안인지 센다(오차 0.2% 안팎).</summary>
        internal const int SamplesPerAxis = 8;

        /// <summary>
        /// 가구 월드 경계 상자(<paramref name="worldBounds"/>) 중 구역 상자 안에 든 비율(0~1). 구역은 위치·회전을 가진 상자
        /// (<paramref name="zoneWorldToLocal"/> 로 구역 로컬로 옮겨 ±<paramref name="zoneSize"/>/2 와 비교)라 구역이 회전해 있어도 맞다.
        /// 경계 상자는 회전한 가구에서 실제 몸보다 크므로 비율은 보수적(낮게)으로 나온다.
        /// </summary>
        internal static float InsideFraction(Bounds worldBounds, Matrix4x4 zoneWorldToLocal, Vector3 zoneSize)
        {
            Vector3 min = worldBounds.min;
            Vector3 step = worldBounds.size / SamplesPerAxis;
            Vector3 half = zoneSize * 0.5f;

            // 부피가 없는 가구(콜라이더가 점·선)는 칸이 한 점으로 겹쳐도 같은 규칙으로 센다.
            int inside = 0;
            for (int x = 0; x < SamplesPerAxis; x++)
            {
                for (int y = 0; y < SamplesPerAxis; y++)
                {
                    for (int z = 0; z < SamplesPerAxis; z++)
                    {
                        var point = new Vector3(
                            min.x + (x + 0.5f) * step.x,
                            min.y + (y + 0.5f) * step.y,
                            min.z + (z + 0.5f) * step.z);
                        Vector3 local = zoneWorldToLocal.MultiplyPoint3x4(point);
                        if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y
                            && Mathf.Abs(local.z) <= half.z)
                            inside++;
                    }
                }
            }

            return inside / (float)(SamplesPerAxis * SamplesPerAxis * SamplesPerAxis);
        }

        /// <summary>
        /// 조건이 이어진 시간을 한 물리 스텝만큼 늘린다. 조건이 한 번이라도 어긋나면 0 으로 돌아가 처음부터 센다 —
        /// 잡았다 놓거나 구역 가장자리를 들락거리는 가구는 포장되지 않는다.
        /// </summary>
        internal static float Advance(float elapsed, bool ready, float deltaTime)
        {
            return ready ? elapsed + deltaTime : 0f;
        }

        /// <summary>이어진 시간이 필요 시간에 닿았는가.</summary>
        internal static bool IsComplete(float elapsed, float holdSeconds)
        {
            return elapsed >= holdSeconds;
        }
    }
}
