using System;
using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.Furniture
{
    /// <summary>
    /// 가구 콜라이더 전체를 다른 바디 자세로 옮겨 놓았다고 가정하고 주변과 겹치는 깊이를 잰다(서버 전용, 끼임 보조
    /// → docs/architecture/throw-system.md §3.2). 바닥·천장처럼 위아래로 밀어내는 겹침은 옆으로 비켜 지나가는
    /// 판단과 무관해 무시한다. 플레이어처럼 검사에서 뺄 레이어는 생성할 때 마스크로 준다.
    /// </summary>
    internal sealed class FurnitureClearanceProbe
    {
        /// <summary>이 깊이(m)까지는 닿은 것으로 보고 겹침으로 치지 않는다(PhysX 접촉 여유 정도).</summary>
        public const float PenetrationTolerance = 0.01f;

        private const float VerticalNormalLimit = 0.7f;
        // 보정으로 옮겨 가는 도중 겹침이 지금보다 이만큼 넘게 깊어지면 실제로는 못 지나가는 경로다 — 밀고 있는 접촉면으로는
        // 몇 mm 도 더 들어가지 못한다.
        private const float SweepSlack = 0.002f;
        // 도중 자세 사이에 가구 끝이 움직이는 최대 거리(m). 회전 샘플 간격은 가구 크기로 정한다.
        private const float SweepMaxTravel = 0.02f;
        private const float SweepMaxYawStep = 2f;

        private readonly Rigidbody _body;
        private readonly Collider[] _colliders;
        private readonly Collider[] _overlaps;
        private readonly int _layerMask;

        public FurnitureClearanceProbe(Rigidbody body, Collider[] colliders, int layerMask, int bufferSize = 64)
        {
            _body = body;
            _colliders = colliders;
            _layerMask = layerMask;
            _overlaps = new Collider[Mathf.Max(1, bufferSize)];
        }

        /// <summary>바디를 그 자세에 두면 다른 것과 (닿는 정도를 넘어) 겹치지 않는가.</summary>
        public bool IsPoseClear(Vector3 bodyPosition, Quaternion bodyRotation, Predicate<Collider> ignore)
        {
            return Penetration(bodyPosition, bodyRotation, ignore) <= PenetrationTolerance;
        }

        /// <summary>바디를 그 자세에 두었을 때 옆으로 겹치는 가장 깊은 깊이(m). 겹치지 않으면 0.</summary>
        public float Penetration(Vector3 bodyPosition, Quaternion bodyRotation, Predicate<Collider> ignore)
        {
            Transform root = _body.transform;
            if (!TryGetLocalBounds(root, out Vector3 centerLocal, out float radius))
                return 0f;

            Quaternion inverse = Quaternion.Inverse(root.rotation);
            Vector3 center = bodyPosition + bodyRotation * centerLocal;
            int count = Physics.OverlapSphereNonAlloc(
                center, radius + PenetrationTolerance, _overlaps, _layerMask, QueryTriggerInteraction.Ignore);

            float deepest = 0f;
            foreach (Collider own in _colliders)
            {
                if (!IsSolid(own))
                    continue;

                Transform part = own.transform;
                Vector3 position = bodyPosition + bodyRotation * (inverse * (part.position - root.position));
                Quaternion rotation = bodyRotation * (inverse * part.rotation);

                for (int i = 0; i < count; i++)
                {
                    Collider other = _overlaps[i];
                    if (other == null || other.attachedRigidbody == _body || (ignore != null && ignore(other)))
                        continue;

                    if (Physics.ComputePenetration(
                            own, position, rotation,
                            other, other.transform.position, other.transform.rotation,
                            out Vector3 direction, out float distance)
                        && Mathf.Abs(direction.y) < VerticalNormalLimit
                        && distance > deepest)
                    {
                        deepest = distance;
                    }
                }
            }

            return deepest;
        }

        /// <summary>
        /// 지금 자세에서 수직축 <paramref name="yaw"/>도 회전·<paramref name="offset"/> 이동으로 옮겨 갈 수 있는가.
        /// 도중 자세를 촘촘히 샘플해 겹침이 지금보다 깊어지지 않아야 하고, 도착 자세는 비어 있어야 한다 —
        /// 도착 자세만 보면 모서리가 문틀을 파고들어야 하는 회전도 "된다"고 잘못 고른다.
        /// </summary>
        public bool CanShift(
            Vector3 bodyPosition,
            Quaternion bodyRotation,
            float yaw,
            Vector3 offset,
            Predicate<Collider> ignore)
        {
            float allowed = Penetration(bodyPosition, bodyRotation, ignore) + SweepSlack;
            float yawStep = SweepMaxYawStep;
            if (TryGetLocalBounds(_body.transform, out Vector3 centerLocal, out float radius))
            {
                float reach = centerLocal.magnitude + radius;
                if (reach > 0.0001f)
                    yawStep = Mathf.Min(yawStep, SweepMaxTravel / reach * Mathf.Rad2Deg);
            }

            int samples = Mathf.Max(1, Mathf.Max(
                Mathf.CeilToInt(Mathf.Abs(yaw) / yawStep),
                Mathf.CeilToInt(offset.magnitude / SweepMaxTravel)));

            for (int i = 1; i <= samples; i++)
            {
                float t = i / (float)samples;
                float depth = Penetration(
                    bodyPosition + offset * t,
                    Quaternion.AngleAxis(yaw * t, Vector3.up) * bodyRotation,
                    ignore);
                if (depth > (i == samples ? PenetrationTolerance : allowed))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 진행 방향 <paramref name="moveDirection"/>으로 <paramref name="probeDistance"/>만큼 나아갈 수 있게 하는 가장 작은 보정을
        /// <paramref name="candidates"/> 순서대로 찾는다. 보정은 지금 자세에서 옮겨 갈 수 있어야 하고(<see cref="CanShift"/>),
        /// 보정한 자세로 앞으로 민 자리가 비어 있어야 한다. 회전은 바디 원점 기준 월드 수직축, 옆 이동은 <paramref name="lateralAxis"/> 방향.
        /// 보정 없이도 나아갈 수 있으면 보정 없음 후보를 돌려준다.
        /// </summary>
        public bool TryFindClearPose(
            Vector3 bodyPosition,
            Quaternion bodyRotation,
            Vector3 moveDirection,
            float probeDistance,
            Vector3 lateralAxis,
            IReadOnlyList<FurnitureSqueezeAssist.Candidate> candidates,
            Predicate<Collider> ignore,
            out FurnitureSqueezeAssist.Candidate found)
        {
            Vector3 probe = moveDirection * probeDistance;
            for (int i = 0; i < candidates.Count; i++)
            {
                FurnitureSqueezeAssist.Candidate candidate = candidates[i];
                Vector3 offset = lateralAxis * candidate.Lateral;
                Quaternion rotation = Quaternion.AngleAxis(candidate.Yaw, Vector3.up) * bodyRotation;
                if (!candidate.IsIdentity && !CanShift(bodyPosition, bodyRotation, candidate.Yaw, offset, ignore))
                    continue;

                if (IsPoseClear(bodyPosition + offset + probe, rotation, ignore))
                {
                    found = candidate;
                    return true;
                }
            }

            found = default;
            return false;
        }

        /// <summary>지금 자세의 콜라이더를 모두 감싸는 구 — 중심은 바디 로컬, 반지름은 월드 단위.</summary>
        public bool TryGetLocalBounds(Transform root, out Vector3 centerLocal, out float radius)
        {
            bool found = false;
            Bounds bounds = default;
            foreach (Collider own in _colliders)
            {
                if (!IsSolid(own))
                    continue;

                if (found)
                    bounds.Encapsulate(own.bounds);
                else
                    bounds = own.bounds;
                found = true;
            }

            centerLocal = found ? Quaternion.Inverse(root.rotation) * (bounds.center - root.position) : Vector3.zero;
            radius = found ? bounds.extents.magnitude : 0f;
            return found;
        }

        private static bool IsSolid(Collider collider)
        {
            return collider != null && collider.enabled && !collider.isTrigger
                && collider.gameObject.activeInHierarchy;
        }
    }
}
