using UnityEngine;

namespace GhostHunter.Gameplay.Furniture
{
    /// <summary>
    /// 2인 잡기(Held) 가구가 목표 위치·자세에 고정되도록 주는 속도와, 마우스 휠 한 칸의 자세 변화를
    /// 계산하는 순수 함수 모음 → docs/architecture/throw-system.md §3.
    /// </summary>
    public static class FurnitureHeldControl
    {
        /// <summary>한 물리 스텝 안에 목표 위치에 닿는 선속도. <paramref name="maxSpeed"/>로 자른다.</summary>
        public static Vector3 TrackingVelocity(Vector3 current, Vector3 target, float deltaTime, float maxSpeed)
        {
            if (deltaTime <= 0f)
                return Vector3.zero;

            return Vector3.ClampMagnitude((target - current) / deltaTime, Mathf.Max(0f, maxSpeed));
        }

        /// <summary>
        /// 한 물리 스텝 안에 목표 자세에 닿는 각속도(rad/s). 짧은 쪽으로 돌고,
        /// <paramref name="maxDegreesPerSecond"/>로 자른다.
        /// </summary>
        public static Vector3 TrackingAngularVelocity(
            Quaternion current,
            Quaternion target,
            float deltaTime,
            float maxDegreesPerSecond)
        {
            if (deltaTime <= 0f)
                return Vector3.zero;

            Quaternion delta = target * Quaternion.Inverse(current);

            // q 와 -q 는 같은 자세다. w 를 양수로 맞춰야 180° 넘게 반대로 돌지 않는다.
            if (delta.w < 0f)
                delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);

            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle < 0.001f || !IsFinite(axis) || axis.sqrMagnitude < 0.0001f)
                return Vector3.zero;

            float degreesPerSecond = Mathf.Min(angle / deltaTime, Mathf.Max(0f, maxDegreesPerSecond));
            return axis.normalized * (degreesPerSecond * Mathf.Deg2Rad);
        }

        /// <summary>휠 <paramref name="steps"/>칸만큼 자세를 바꾼다. 각도 제한은 없다.</summary>
        public static Quaternion ApplyWheel(
            Quaternion rotation,
            int steps,
            FurnitureRotateMode mode,
            Vector3 aimDirection,
            float stepDegrees)
        {
            if (steps == 0)
                return rotation;

            Vector3 axis = mode == FurnitureRotateMode.Tilt ? TiltAxis(aimDirection) : Vector3.up;
            return (Quaternion.AngleAxis(steps * stepDegrees, axis) * rotation).normalized;
        }

        /// <summary>조준 방향의 수평 성분 기준 오른쪽 축. 수직으로 조준하면 월드 X축을 쓴다.</summary>
        public static Vector3 TiltAxis(Vector3 aimDirection)
        {
            Vector3 flat = new(aimDirection.x, 0f, aimDirection.z);
            if (flat.sqrMagnitude < 0.0001f)
                return Vector3.right;

            return Vector3.Cross(Vector3.up, flat.normalized);
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }
    }
}
