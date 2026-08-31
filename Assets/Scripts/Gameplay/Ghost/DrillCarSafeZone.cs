using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 드릴 카 세이프 존의 임시 버전(기획서 §11.1). 정식 드릴 카 없이 집 현관 앞에 상자 경계만
    /// 친다. 이 상자 안에 있는 플레이어는 귀신의 탐지·잡힘 판정에서 **완전히 제외**된다.
    /// 다른 플레이어의 어택이나 귀신 상태·타이머는 건드리지 않는다 — 그냥 이 플레이어만 안전하다.
    ///
    /// <c>NetworkObject</c> 가 아니다. 판정은 서버 하나뿐이고 상자는 씬에 고정된 순수 기하라
    /// 복제할 게 없다. 정식 드릴 카가 생기면 이 컴포넌트와 씬 오브젝트는 통째로 삭제한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DrillCarSafeZone : MonoBehaviour
    {
        private static readonly List<DrillCarSafeZone> Zones = new();

        [Tooltip("세이프 존 상자의 크기(m). 오브젝트 위치가 상자 중심이다. 스케일은 1로 둔다(하드 룰 §3.5).")]
        [SerializeField] private Vector3 _size = new(4f, 3f, 4f);

        /// <summary>주어진 월드 좌표가 지금 살아 있는 세이프 존 중 하나라도 안에 있는가.</summary>
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

        private bool ContainsPoint(Vector3 worldPosition)
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
            Gizmos.color = new Color(0.2f, 0.9f, 0.5f, 0.15f);
            Gizmos.DrawCube(Vector3.zero, _size);
            Gizmos.color = new Color(0.2f, 0.9f, 0.5f, 0.9f);
            Gizmos.DrawWireCube(Vector3.zero, _size);
        }
    }
}
