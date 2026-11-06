namespace GhostHunter.Gameplay.Lift
{
    /// <summary>
    /// 리프트 정지 포인트 규칙(순수 계산) — 건물 한 층 높이마다 하나씩 있고(0, 1×, 2×…),
    /// 버튼은 입력 방향의 가장 가까운 정지 포인트로 보낸다. 막혀 중간에 선 뒤에도 같은 규칙이라
    /// 제자리에서 한 층을 다시 오르지 않고 가장 가까운 정지 포인트에서 멈춘다(기획서 §2.3).
    /// 높이는 맨 아래 정지 포인트(0)에서 올라간 거리(m)다.
    /// </summary>
    public static class LiftStopRules
    {
        /// <summary>이 오차 안이면 그 정지 포인트에 서 있는 것으로 본다(m).</summary>
        public const float Tolerance = 0.005f;

        public static float StopHeight(int index, float floorHeight) => index * floorHeight;

        /// <summary>
        /// 입력 방향으로 가장 가까운 정지 포인트. 이미 그 방향 끝이면 false —
        /// 정지 포인트에 서 있으면 인접한 다음 정지 포인트, 중간에 서 있으면 그 방향의 가장 가까운 정지 포인트다.
        /// </summary>
        public static bool TryFindTarget(
            float height, LiftDirection direction, float floorHeight, int stopCount, out float target)
        {
            target = height;
            if (floorHeight <= 0f || stopCount < 2)
                return false;

            int last = stopCount - 1;
            if (direction == LiftDirection.Up)
            {
                for (int i = 0; i <= last; i++)
                {
                    float stop = StopHeight(i, floorHeight);
                    if (stop > height + Tolerance)
                    {
                        target = stop;
                        return true;
                    }
                }

                return false;
            }

            for (int i = last; i >= 0; i--)
            {
                float stop = StopHeight(i, floorHeight);
                if (stop < height - Tolerance)
                {
                    target = stop;
                    return true;
                }
            }

            return false;
        }

        /// <summary>정지 포인트에 서 있는가. 그렇다면 그 번호(0부터)를 돌려준다.</summary>
        public static bool IsAtStop(float height, float floorHeight, int stopCount, out int index)
        {
            index = -1;
            if (floorHeight <= 0f || stopCount < 1)
                return false;

            for (int i = 0; i < stopCount; i++)
            {
                if (System.Math.Abs(height - StopHeight(i, floorHeight)) <= Tolerance)
                {
                    index = i;
                    return true;
                }
            }

            return false;
        }

        /// <summary>높이를 가장 아래~가장 위 정지 포인트 사이로 자른다.</summary>
        public static float Clamp(float height, float floorHeight, int stopCount)
        {
            float max = StopHeight(System.Math.Max(stopCount, 1) - 1, floorHeight);
            return height < 0f ? 0f : (height > max ? max : height);
        }
    }
}
