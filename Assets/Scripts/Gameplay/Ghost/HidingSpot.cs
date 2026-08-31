using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 일반 은신처의 임시 버전(기획서 §9.5 — 옷장·침대 밑·책상 밑). 정식 가구가 아직
    /// 프리팹화되지 않아(docs/todo/TODO-미정.md) 특정 가구에 붙이지 않고, 방마다 순수 상자
    /// 경계만 둔다. 이 안에 있는 플레이어는 귀신의 일반 시야·소리 탐지·잡힘에서 제외된다 —
    /// 대신 귀신이 수색 중 이 은신처를 발견하면 <see cref="GhostPrototypeSettings.HidingSpotCheckChance"/>
    /// 확률로 직접 검사해 걸릴 수 있다(<see cref="GhostPrototypeController"/> 참고).
    ///
    /// <c>NetworkObject</c> 가 아니다 — 판정은 서버 하나뿐이고 상자는 씬에 고정된 순수 기하라
    /// 복제할 게 없다(<see cref="DrillCarSafeZone"/> 과 동일한 패턴). 정식 은신처가 생기면
    /// 이 컴포넌트와 씬 오브젝트는 통째로 삭제한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HidingSpot : MonoBehaviour
    {
        private static readonly List<HidingSpot> Zones = new();

        /// <summary>지금 씬에 살아 있는 은신처들.</summary>
        public static IReadOnlyList<HidingSpot> Registry => Zones;

        [Tooltip("은신처 상자의 크기(m). 오브젝트 위치가 상자 중심이다. 스케일은 1로 둔다(하드 룰 §3.5).")]
        [SerializeField] private Vector3 _size = new(1.2f, 1.4f, 1.2f);

        /// <summary>주어진 월드 좌표가 지금 살아 있는 은신처 중 하나라도 안에 있는가.</summary>
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

        /// <summary>이 은신처 하나만 콕 집어 검사할 때 쓴다(귀신의 수색 중 은신처 판정).</summary>
        public bool ContainsPoint(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            Vector3 half = _size * 0.5f;
            return Mathf.Abs(local.x) <= half.x
                && Mathf.Abs(local.y) <= half.y
                && Mathf.Abs(local.z) <= half.z;
        }

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.55f, 0.35f, 0.9f, 0.15f);
            Gizmos.DrawCube(Vector3.zero, _size);
            Gizmos.color = new Color(0.55f, 0.35f, 0.9f, 0.9f);
            Gizmos.DrawWireCube(Vector3.zero, _size);
        }
    }
}
