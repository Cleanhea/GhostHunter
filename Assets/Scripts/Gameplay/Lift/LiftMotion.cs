using UnityEngine;

namespace GhostHunter.Gameplay.Lift
{
    /// <summary>
    /// 승강 발판의 한 프레임 이동량(순수 계산) — 가속으로 최대 속력까지 올리고, 남은 거리에서 멈출 수 있는
    /// 속력으로 줄여 목표에 부드럽게 닿는다. 정지 포인트에서 덜컹거리지 않게 하려는 것이다.
    /// </summary>
    public static class LiftMotion
    {
        /// <summary>목표 근처에서 멈춰 서지 않도록 두는 최소 속력(m/s).</summary>
        public const float MinimumSpeed = 0.05f;

        /// <summary>도착으로 보는 거리(m).</summary>
        public const float ArriveDistance = 0.0005f;

        /// <summary>
        /// 이번 프레임에 움직일 부호 있는 거리. <paramref name="velocity"/>는 갱신된다.
        /// 도착하면 <paramref name="arrived"/>가 참이고 이동량은 남은 거리 그대로(목표에 딱 맞춘다).
        /// </summary>
        public static float Step(
            float height, float target, ref float velocity,
            float maxSpeed, float acceleration, float deltaTime, out bool arrived)
        {
            float remaining = target - height;
            float distance = Mathf.Abs(remaining);
            arrived = false;

            if (distance <= ArriveDistance)
            {
                velocity = 0f;
                arrived = true;
                return remaining;
            }

            if (deltaTime <= 0f || maxSpeed <= 0f || acceleration <= 0f)
                return 0f;

            float direction = Mathf.Sign(remaining);
            // 남은 거리에서 멈출 수 있는 최대 속력(v² = 2·a·d).
            float brakeSpeed = Mathf.Sqrt(2f * acceleration * distance);
            float desired = Mathf.Max(MinimumSpeed, Mathf.Min(maxSpeed, brakeSpeed)) * direction;
            velocity = Mathf.MoveTowards(velocity, desired, acceleration * deltaTime);

            float move = velocity * deltaTime;
            if (Mathf.Abs(move) >= distance)
            {
                velocity = 0f;
                arrived = true;
                return remaining;
            }

            return move;
        }
    }
}
