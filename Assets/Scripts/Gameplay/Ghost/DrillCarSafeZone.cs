using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 드릴 카 세이프 존(기획서 §11.1). 이 상자 안에 있는 플레이어는 귀신의 탐지·잡힘 판정에서 **완전히 제외**된다.
    /// 다른 플레이어의 어택이나 귀신 상태·타이머는 건드리지 않는다 — 그냥 이 플레이어만 안전하다.
    /// 두 가지로 쓴다:
    /// <list type="bullet">
    /// <item>임시 상자(ProtoTypeGame) — 모델 없이 집 앞 스폰 줄 뒤에 런타임으로 세운다(<see cref="PlaceBehindSpawns"/>).</item>
    /// <item>드릴카 모델(<c>Prefabs/Map/DrillCar.prefab</c>, Stage1) — 실내를 감싸는 상자. 씬에 저장한 자리에 그대로 두고
    /// (<see cref="PlacesBehindSpawnsAtRuntime"/> 끔), 종료 단말기·공동 아이템을 앵커 자리에 놓는다.</item>
    /// </list>
    ///
    /// <c>NetworkObject</c> 가 아니다. 판정은 서버 하나뿐이고 상자는 씬에 고정된 순수 기하라
    /// 복제할 게 없다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DrillCarSafeZone : MonoBehaviour
    {
        private static readonly List<DrillCarSafeZone> Zones = new();

        /// <summary>바닥을 찾는 레이를 스폰 높이보다 이만큼 위에서 쏜다.</summary>
        private const float GroundProbeHeight = 3f;
        /// <summary>바닥을 찾는 레이가 스폰 높이 아래로 내려가는 최대 깊이.</summary>
        private const float GroundProbeDepth = 10f;

        [Tooltip("세이프 존 상자의 크기(m). 오브젝트 위치가 상자 중심이다. 스케일은 1로 둔다(하드 룰 §3.5).")]
        [SerializeField] private Vector3 _size = new(4f, 3f, 4f);

        [Tooltip("스폰 줄 뒤쪽 끝에서 드릴카 앞면까지 거리(m). 앞마당이 얕은 B안에서 현관을 비우려고 " +
                 "스폰 뒤 땅에 세운다. [TEMP]")]
        [SerializeField, Min(0f)] private float _gapBehindSpawns = 2.5f;

        [Tooltip("켜면 스테이지 시작 때 스폰 줄 뒤로 옮긴다(임시 상자). 씬에 배치한 드릴카 모델은 끈다.")]
        [SerializeField] private bool _placeBehindSpawnsAtRuntime = true;

        [Tooltip("스테이지 종료 단말기를 놓을 자리. 비우면 상자 뒤쪽 끝(+Z)에 놓는다.")]
        [SerializeField] private Transform _exitTerminalAnchor;

        [Tooltip("구매한 공동 아이템을 늘어놓을 선반 면. 로컬 X 로 늘어놓는다. 비우면 상자 가운데에 임시 선반을 만든다.")]
        [SerializeField] private Transform _itemShelfAnchor;

        /// <summary>상자 크기(m). <b>오브젝트 위치는 중심이지 바닥이 아니다</b> — 이 자리에 다른 것을
        /// 맞출 때 바닥 높이가 필요하다(조립 영역이 그렇다). 읽기 전용.</summary>
        public Vector3 Size => _size;

        /// <summary>스테이지 시작 때 <see cref="PlaceBehindSpawns"/> 로 옮겨야 하는 임시 상자인가.</summary>
        public bool PlacesBehindSpawnsAtRuntime => _placeBehindSpawnsAtRuntime;

        /// <summary>종료 단말기 자리. 없으면 null.</summary>
        public Transform ExitTerminalAnchor => _exitTerminalAnchor;

        /// <summary>공동 아이템 선반 자리. 없으면 null.</summary>
        public Transform ItemShelfAnchor => _itemShelfAnchor;

        /// <summary>
        /// 임시 드릴카 구역을 스폰 줄 <b>뒤쪽</b>(스폰 지점이 바라보는 반대편)에 세운다. 스폰 자리에
        /// 겹쳐 두면 앞마당이 4m 뿐인 B안에서 상자·반출판·종료 단말기가 현관을 막는다.
        /// 폭은 스폰 줄만큼 넓힌다. 앞마당 밖 땅은 앞마당보다 낮아서 바닥 높이는 레이로 찾는다.
        /// </summary>
        public void PlaceBehindSpawns(Bounds spawnBounds, Vector3 spawnFacing)
        {
            Vector3 back = new(-spawnFacing.x, 0f, -spawnFacing.z);
            back = back.sqrMagnitude > 0.0001f ? back.normalized : Vector3.back;
            _size.x = Mathf.Max(_size.x, spawnBounds.size.x + 0.7f);

            float spawnHalfDepth = Mathf.Abs(back.x) * spawnBounds.extents.x
                + Mathf.Abs(back.z) * spawnBounds.extents.z;
            float zoneHalfDepth = (Mathf.Abs(back.x) * _size.x + Mathf.Abs(back.z) * _size.z) * 0.5f;
            Vector3 center = spawnBounds.center
                + back * (spawnHalfDepth + _gapBehindSpawns + zoneHalfDepth);

            // 스폰 지점은 바닥보다 5cm 떠 있다 — 바닥을 못 찾으면 그만큼 내린 높이를 바닥으로 본다.
            float floor = spawnBounds.min.y - 0.05f;
            var probe = new Vector3(center.x, spawnBounds.max.y + GroundProbeHeight, center.z);
            if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit,
                    GroundProbeHeight + GroundProbeDepth, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
                floor = hit.point.y;
            else
                Debug.LogWarning($"{nameof(DrillCarSafeZone)}: 스폰 뒤 {center} 아래에서 바닥을 찾지 못해 " +
                                 "스폰 높이에 세웠다.", this);

            transform.position = new Vector3(center.x, floor + _size.y * 0.5f, center.z);
        }

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
