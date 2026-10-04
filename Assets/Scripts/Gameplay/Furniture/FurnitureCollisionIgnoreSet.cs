using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.Furniture
{
    /// <summary>
    /// 가구 콜라이더 전체와 다른 콜라이더 사이의 충돌을 잠시 끄고, 다시 켤 때는 서로 떨어질 때까지 기다린다
    /// → docs/architecture/throw-system.md §3.3. 겹친 채로 켜면 PhysX 가 밀어내며 가구가 튀거나 플레이어가 끼인다.
    /// </summary>
    internal sealed class FurnitureCollisionIgnoreSet
    {
        private const float TouchTolerance = 0.001f;

        private readonly Collider[] _own;
        private readonly List<Collider> _ignored = new();
        private readonly List<Collider> _restoring = new();

        public FurnitureCollisionIgnoreSet(Collider[] own)
        {
            _own = own;
        }

        /// <summary>지금 충돌을 끄고 있는 콜라이더(다시 켜기를 기다리는 것 제외).</summary>
        public IReadOnlyList<Collider> Ignored => _ignored;

        public bool IsEmpty => _ignored.Count == 0 && _restoring.Count == 0;

        /// <summary>충돌이 꺼져 있는가 — 다시 켜기를 기다리는 콜라이더도 포함한다.</summary>
        public bool Contains(Collider other)
        {
            return other != null && (_ignored.Contains(other) || _restoring.Contains(other));
        }

        /// <summary>
        /// 충돌을 끈다. 꺼진 오브젝트의 콜라이더는 Unity 가 무시 상태를 보관하지 않으므로 받지 않는다.
        /// </summary>
        public void Ignore(Collider other)
        {
            if (!IsActive(other) || _ignored.Contains(other))
                return;

            _restoring.Remove(other);
            SetIgnored(other, true);
            _ignored.Add(other);
        }

        /// <summary>떨어지는 대로 충돌을 다시 켠다(<see cref="Tick"/>).</summary>
        public void Release(Collider other)
        {
            if (_ignored.Remove(other) && other != null)
                _restoring.Add(other);
        }

        public void ReleaseAll()
        {
            foreach (Collider other in _ignored)
            {
                if (other != null)
                    _restoring.Add(other);
            }

            _ignored.Clear();
        }

        /// <summary>다시 켜기를 기다리는 콜라이더 중 가구와 떨어진 것의 충돌을 켠다.</summary>
        public void Tick()
        {
            for (int i = _restoring.Count - 1; i >= 0; i--)
            {
                Collider other = _restoring[i];
                if (other == null)
                {
                    _restoring.RemoveAt(i);
                    continue;
                }

                if (IsActive(other) && IsTouching(other))
                    continue;

                SetIgnored(other, false);
                _restoring.RemoveAt(i);
            }
        }

        /// <summary>겹침과 관계없이 모두 즉시 되돌린다. 디스폰·파괴할 때만 쓴다.</summary>
        public void ClearImmediately()
        {
            foreach (Collider other in _ignored)
                SetIgnored(other, false);
            foreach (Collider other in _restoring)
                SetIgnored(other, false);
            _ignored.Clear();
            _restoring.Clear();
        }

        private bool IsTouching(Collider other)
        {
            Bounds otherBounds = other.bounds;
            foreach (Collider own in _own)
            {
                if (!IsActive(own) || own.isTrigger)
                    continue;

                // CharacterController 는 ComputePenetration 대상이 아니라 경계 상자로만 본다(보수적으로 오래 끈다).
                if (other is CharacterController)
                {
                    if (own.bounds.Intersects(otherBounds))
                        return true;
                    continue;
                }

                if (Physics.ComputePenetration(
                        own, own.transform.position, own.transform.rotation,
                        other, other.transform.position, other.transform.rotation,
                        out _, out float distance)
                    && distance > TouchTolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private void SetIgnored(Collider other, bool ignore)
        {
            if (!IsActive(other))
                return;

            foreach (Collider own in _own)
            {
                if (IsActive(own))
                    Physics.IgnoreCollision(own, other, ignore);
            }
        }

        private static bool IsActive(Collider collider)
        {
            return collider != null && collider.enabled && collider.gameObject.activeInHierarchy;
        }
    }
}
