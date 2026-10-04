using GhostHunter.Gameplay.Ghost;
using UnityEngine;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>
    /// 목표 가구 반출 완료 구역. 2026-10-04 사용자 요청으로 드릴카 <b>밖</b>, 뒷문 램프 옆 땅에 둔다 — 좁은 뒷문(폭 약 1.15m)과
    /// 램프로 가구를 들고 올라가지 않아도 된다. 안전 구역(드릴카 실내)과는 별도로 판정한다.
    /// 드릴카 모델 프리팹은 구역을 저장해 두고, 임시 상자(ProtoTypeGame)에서는 <see cref="CreateBesideDrillCar"/> 가
    /// 상자 옆 땅에 런타임으로 만든다. 플레이어에게는 <see cref="FurnitureDeliveryZoneView"/> 가 빛으로 구역을 보여 준다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FurnitureDeliveryZone : MonoBehaviour
    {
        // 임시 상자 옆에 만들 때의 크기·간격(m). 드릴카 모델 프리팹은 저장된 값을 쓴다.
        private static readonly Vector3 BesideSize = new(4f, 3.5f, 4f);
        private const float BesideGap = 0.5f;
        private const float BelowGround = 0.5f;

        private static FurnitureDeliveryZone _active;

        [Tooltip("반출 구역 상자 크기(m). 오브젝트 위치가 상자 중심이다. 바닥에 놓인 가구의 원점이 들어가도록 땅보다 조금 아래까지 덮는다.")]
        [SerializeField] private Vector3 _size = BesideSize;

        [Tooltip("바닥 채움·윤곽선·모서리 빛기둥·초록 조명으로 구역을 꾸민다(FurnitureDeliveryZoneView, 아래로 땅을 찾아 그 높이에). " +
            "필드 이름은 예전 바닥 표시판 때의 것을 직렬화 호환을 위해 그대로 둔다.")]
        [SerializeField] private bool _showFloorMarker = true;

        /// <summary>구역 상자 크기(m, 로컬). 중심은 오브젝트 위치다.</summary>
        public Vector3 Size => _size;

        public static bool Contains(Vector3 position)
        {
            if (_active == null)
                return false;
            Vector3 local = _active.transform.InverseTransformPoint(position);
            Vector3 half = _active._size * 0.5f;
            return Mathf.Abs(local.x) <= half.x
                && Mathf.Abs(local.y) <= half.y
                && Mathf.Abs(local.z) <= half.z;
        }

        /// <summary>
        /// 씬에 반출 구역이 없으면 임시 드릴카 상자의 오른쪽(+X) 바깥 땅에 만든다. 드릴카 모델에는 이미 들어 있다.
        /// 상자를 스폰 줄 뒤로 옮긴 <b>다음에</b> 불러야 한다(상자 폭이 그때 정해진다).
        /// </summary>
        public static void CreateBesideDrillCar()
        {
            // 설치자가 씬 오브젝트의 OnEnable 보다 먼저 불릴 수 있어 _active 만으로는 모른다.
            if (_active != null || FindFirstObjectByType<FurnitureDeliveryZone>() != null)
                return;
            DrillCarSafeZone car = FindFirstObjectByType<DrillCarSafeZone>();
            if (car == null)
                return;

            GameObject root = new("FurnitureDeliveryZone");
            root.transform.SetParent(car.transform, false);
            // 상자 중심은 바닥 + 높이 절반이다. 구역 바닥을 땅보다 BelowGround 만큼 내린다.
            root.transform.localPosition = new Vector3(
                car.Size.x * 0.5f + BesideGap + BesideSize.x * 0.5f,
                -car.Size.y * 0.5f + BesideSize.y * 0.5f - BelowGround,
                0f);
            FurnitureDeliveryZone zone = root.AddComponent<FurnitureDeliveryZone>();
            zone._size = BesideSize;
            zone._showFloorMarker = true;
        }

        private void OnEnable()
        {
            if (_active == null)
                _active = this;
        }

        private void Start()
        {
            if (_showFloorMarker && GetComponent<FurnitureDeliveryZoneView>() == null)
                gameObject.AddComponent<FurnitureDeliveryZoneView>();
        }

        private void OnDisable()
        {
            if (_active == this)
                _active = null;
        }

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.9f);
            Gizmos.DrawWireCube(Vector3.zero, _size);
        }
    }
}
