using UnityEngine;

namespace GhostHunter.Gameplay.Furniture
{
    /// <summary>
    /// 2인 운반(Held) 가구의 두 손잡이 자세 풀이·추종 속도와, 마우스 휠 한 칸의 자세 변화를
    /// 계산하는 순수 함수 모음 → docs/architecture/throw-system.md §3.
    /// </summary>
    public static class FurnitureHeldControl
    {
        /// <summary>
        /// 두 손잡이를 잇는 축이 두 손 목표를 잇는 축을 따르도록 Held 진입 자세 앞에 곱할 회전.
        /// 수평 방향(yaw)은 그대로 따르고, 높낮이 차이로 생기는 기울기는 <paramref name="maxPitchDegrees"/>로 자른다.
        /// 손잡이 수평 간격이 <paramref name="minSpan"/>보다 짧거나 두 손이 거의 같은 수평 위치에 있으면
        /// 방향을 정할 수 없어 false — 호출자는 이전 값을 유지한다.
        /// </summary>
        /// <param name="entryGripAxis">Held 진입 순간 월드 기준 손잡이 0 → 1 벡터.</param>
        /// <param name="handAxis">지금 손 목표 0 → 1 벡터.</param>
        public static bool TryFollowRotation(
            Vector3 entryGripAxis,
            Vector3 handAxis,
            float minSpan,
            float maxPitchDegrees,
            out Quaternion follow)
        {
            follow = Quaternion.identity;
            Vector3 entryFlat = Flat(entryGripAxis);
            Vector3 handFlat = Flat(handAxis);
            float entrySpan = entryFlat.magnitude;
            float handSpan = handFlat.magnitude;
            if (entrySpan < Mathf.Max(0.0001f, minSpan) || handSpan < Mathf.Max(0.0001f, minSpan * 0.5f)
                || !IsFinite(entryGripAxis) || !IsFinite(handAxis))
                return false;

            float yaw = Vector3.SignedAngle(entryFlat, handFlat, Vector3.up);
            Quaternion yawRotation = Quaternion.AngleAxis(yaw, Vector3.up);
            if (maxPitchDegrees <= 0f)
            {
                follow = yawRotation;
                return true;
            }

            float entryElevation = Mathf.Atan2(entryGripAxis.y, entrySpan) * Mathf.Rad2Deg;
            float handElevation = Mathf.Atan2(handAxis.y, handSpan) * Mathf.Rad2Deg;
            float pitch = Mathf.Clamp(handElevation - entryElevation, -maxPitchDegrees, maxPitchDegrees);

            // 손 축의 수평 왼쪽을 축으로 양수만큼 돌리면 축 끝(손 1 쪽)이 올라간다.
            Vector3 pitchAxis = Vector3.Cross(handFlat / handSpan, Vector3.up);
            follow = (Quaternion.AngleAxis(pitch, pitchAxis) * yawRotation).normalized;
            return true;
        }

        /// <summary>손잡이 중점(바디 로컬)이 손 목표 중점에 오도록 하는 바디 위치.</summary>
        public static Vector3 SolveCarryPosition(Vector3 handMidpoint, Quaternion rotation, Vector3 gripMidpointLocal)
        {
            return handMidpoint - rotation * gripMidpointLocal;
        }

        /// <summary><paramref name="current"/>에서 <paramref name="desired"/>로 최대 <paramref name="maxDelta"/>만큼만 바꾼다.</summary>
        public static Vector3 LimitChange(Vector3 current, Vector3 desired, float maxDelta)
        {
            return current + Vector3.ClampMagnitude(desired - current, Mathf.Max(0f, maxDelta));
        }

        /// <summary>시간 상수 <paramref name="timeConstant"/>인 지수 평활의 한 스텝 보간 비율. 0 이하면 1(즉시).</summary>
        public static float SmoothingFactor(float deltaTime, float timeConstant)
        {
            if (timeConstant <= 0f)
                return 1f;

            return 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / timeConstant);
        }

        /// <summary>
        /// <paramref name="deltaTime"/> 안에 목표 위치에 닿는 선속도. <paramref name="maxSpeed"/>로 자른다.
        /// 물리 스텝을 넣으면 한 스텝 추종, 시간 상수를 넣으면 그 시간에 걸쳐 오차를 줄이는 속도다.
        /// </summary>
        public static Vector3 TrackingVelocity(Vector3 current, Vector3 target, float deltaTime, float maxSpeed)
        {
            if (deltaTime <= 0f)
                return Vector3.zero;

            return Vector3.ClampMagnitude((target - current) / deltaTime, Mathf.Max(0f, maxSpeed));
        }

        /// <summary>
        /// <paramref name="deltaTime"/> 안에 목표 자세에 닿는 각속도(rad/s). 짧은 쪽으로 돌고,
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

        private static Vector3 Flat(Vector3 value) => new(value.x, 0f, value.z);

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }
    }
}
