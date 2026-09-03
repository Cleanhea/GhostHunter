using System.Collections.Generic;
using GhostHunter.Gameplay.Furniture;
using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 침대 밑 은신 공간. 침대 프리팹의 자식(<c>UnderBedHide</c>)으로 구워져 침대와 함께
    /// 움직인다. 엎드린 플레이어가 이 상자 안에 완전히 들어가면 "침대 밑 은신" 후보가 된다 —
    /// 실제 성립 여부는 서버(귀신)의 <see cref="BedHideEvaluator"/> 가 정한다.
    ///
    /// <para><see cref="HidingSpot"/> 과 같은 정적 레지스트리 패턴이며 <c>NetworkObject</c> 가
    /// 아니다(판정은 서버 하나뿐이고 상자는 순수 기하다). 다만 침대는 던질 수 있으므로, 부모
    /// 침대가 Idle 이 아니면(잡힘·발사 중) 은신 공간으로 치지 않는다 — 던지는 침대 밑에 숨을
    /// 수는 없다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BedHideZone : MonoBehaviour
    {
        private static readonly List<BedHideZone> Zones = new();

        /// <summary>지금 씬에 살아 있는 침대 밑 은신 공간들.</summary>
        public static IReadOnlyList<BedHideZone> Registry => Zones;

        [Tooltip("은신 공간 상자 크기(m). 오브젝트 위치가 상자 중심이다. 스케일은 1로 둔다(하드 룰 §3.5).")]
        [SerializeField] private Vector3 _size = new(0.9f, 0.9f, 1.8f);

        private FurnitureGrabTarget _bed;
        private bool _resolvedBed;

        /// <summary>주어진 월드 좌표가 지금 살아 있는(그리고 Idle 인) 침대 밑 은신 공간 안에 있는가.</summary>
        public static bool Contains(Vector3 worldPosition)
        {
            for (int i = 0; i < Zones.Count; i++)
            {
                if (Zones[i] != null && Zones[i].ContainsPoint(worldPosition))
                    return true;
            }

            return false;
        }

        private void OnEnable()
        {
            if (!Zones.Contains(this))
                Zones.Add(this);
        }

        private void OnDisable()
        {
            Zones.Remove(this);
        }

        /// <summary>부모 침대가 Idle 이고, 주어진 점이 상자 로컬 경계 안인가.</summary>
        public bool ContainsPoint(Vector3 worldPosition)
        {
            if (!_resolvedBed)
            {
                _bed = GetComponentInParent<FurnitureGrabTarget>();
                _resolvedBed = true;
            }

            if (_bed != null && _bed.State != FurnitureState.Idle)
                return false;

            Vector3 local = transform.InverseTransformPoint(worldPosition);
            Vector3 half = _size * 0.5f;
            return Mathf.Abs(local.x) <= half.x
                && Mathf.Abs(local.y) <= half.y
                && Mathf.Abs(local.z) <= half.z;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.15f);
            Gizmos.DrawCube(Vector3.zero, _size);
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireCube(Vector3.zero, _size);
        }
    }
}
