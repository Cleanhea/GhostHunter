using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 귀신 원뿔형 시야(§8.1)의 순수 기하 판정과 시야 표시 메시 생성. Physics 가림 검사는
    /// 컨트롤러가 따로 한다. 생성 도구가 프리팹을 구울 때도 메시 빌더를 쓰므로 public 이다.
    /// </summary>
    public static class GhostVision
    {
        /// <summary>
        /// <paramref name="toTarget"/> 방향이 <paramref name="forward"/> 기준 시야각·거리 안에 드는지.
        /// 두 방향 인자는 정규화되어 있지 않아도 된다.
        /// </summary>
        public static bool IsInsideCone(
            Vector3 forward,
            Vector3 toTarget,
            float coneAngleDegrees,
            float distance,
            float maxDistance)
        {
            if (distance <= 0f || distance > maxDistance)
                return false;

            forward.y = 0f;
            toTarget.y = 0f;
            if (forward.sqrMagnitude < 1e-6f || toTarget.sqrMagnitude < 1e-6f)
                return false;

            float halfAngle = Mathf.Clamp(coneAngleDegrees, 0f, 360f) * 0.5f;
            float angle = Vector3.Angle(forward.normalized, toTarget.normalized);
            return angle <= halfAngle;
        }

        /// <summary>지면에 눕힌 부채꼴 시야 표시용 메시. 꼭짓점은 원점, 정면은 +Z.</summary>
        public static Mesh BuildConeMesh(float distance, float coneAngleDegrees, int segments = 24)
        {
            segments = Mathf.Max(3, segments);
            float halfAngle = Mathf.Clamp(coneAngleDegrees, 1f, 360f) * 0.5f * Mathf.Deg2Rad;

            var vertices = new Vector3[segments + 2];
            vertices[0] = Vector3.zero;
            for (int i = 0; i <= segments; i++)
            {
                float t = Mathf.Lerp(-halfAngle, halfAngle, (float)i / segments);
                vertices[i + 1] = new Vector3(Mathf.Sin(t), 0f, Mathf.Cos(t)) * distance;
            }

            // 양면으로 그린다. 위에서 봐도 아래에서 봐도 보여야 한다.
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                int baseIndex = i * 6;
                triangles[baseIndex + 0] = 0;
                triangles[baseIndex + 1] = i + 1;
                triangles[baseIndex + 2] = i + 2;
                triangles[baseIndex + 3] = 0;
                triangles[baseIndex + 4] = i + 2;
                triangles[baseIndex + 5] = i + 1;
            }

            var mesh = new Mesh { name = "GhostVisionCone" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
