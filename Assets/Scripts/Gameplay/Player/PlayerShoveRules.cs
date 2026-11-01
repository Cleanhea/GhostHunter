using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 맨손 플레이어 밀치기의 순수 계산(방향·속도·넉백 감속·서버 거리 허용치) → docs/architecture/throw-system.md §6.
    /// 네트워크와 무관해 EditMode 테스트로 덮는다.
    /// </summary>
    public static class PlayerShoveRules
    {
        /// <summary>
        /// 밀려나는 수평 방향. 밀친 사람의 조준 방향을 수평으로 눕힌 것이고, 거의 수직으로 보고 있으면
        /// (수평 성분이 거의 없으면) 밀친 사람 → 대상 방향으로 대신한다. 둘 다 없으면 0.
        /// </summary>
        public static Vector3 ResolveDirection(Vector3 aimDirection, Vector3 shoverToTarget)
        {
            Vector3 flat = new(aimDirection.x, 0f, aimDirection.z);
            if (flat.sqrMagnitude < 0.0025f)
                flat = new Vector3(shoverToTarget.x, 0f, shoverToTarget.z);

            return flat.sqrMagnitude < 1e-6f ? Vector3.zero : flat.normalized;
        }

        /// <summary>대상이 받는 속도: 수평 <c>direction × maxSpeed × ratio</c> + 위 <c>upwardSpeed × ratio</c>.</summary>
        public static Vector3 KnockbackVelocity(Vector3 direction, float forceRatio, float maxSpeed, float upwardSpeed)
        {
            float ratio = Mathf.Clamp01(forceRatio);
            return direction * (maxSpeed * ratio) + Vector3.up * (upwardSpeed * ratio);
        }

        /// <summary>수평 넉백 속도를 <c>deceleration × dt</c> 만큼 0 쪽으로 줄인다.</summary>
        public static Vector3 DecayKnockback(Vector3 knockback, float deceleration, float deltaTime)
        {
            return Vector3.MoveTowards(knockback, Vector3.zero, Mathf.Max(0f, deceleration) * deltaTime);
        }

        /// <summary>
        /// 서버가 밀치기를 받아들이는 두 몸 사이 수평 거리 상한. 클라이언트는 눈 → 몸 표면 거리로 재므로
        /// 몸 반지름 둘(<paramref name="bodyPadding"/>)과 지연 여유(×1.2)를 더한다.
        /// </summary>
        public static float ServerReach(float shoveMaxDistance, float bodyPadding = 0.8f)
        {
            return shoveMaxDistance * 1.2f + bodyPadding;
        }

        public static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }
    }
}
