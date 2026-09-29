using GhostHunter.Gameplay.Ghost;
using UnityEngine;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>
    /// 드릴카 안의 목표 가구 반출 완료 구역. 안전 구역과 별도로 판정한다.
    /// 드릴카 모델 프리팹은 실내를 감싸는 구역을 저장해 두고, 임시 상자(ProtoTypeGame)에서는
    /// <see cref="CreateInDrillCar"/> 가 상자 안쪽 일부에 런타임으로 만든다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FurnitureDeliveryZone : MonoBehaviour
    {
        private static FurnitureDeliveryZone _active;

        [Tooltip("반출 구역 상자 크기(m). 오브젝트 위치가 상자 중심이다.")]
        [SerializeField] private Vector3 _size = new(2.6f, 2.2f, 5f);

        [Tooltip("바닥에 초록 표시판을 깐다. 드릴카 실내 전체가 구역이면 끈다.")]
        [SerializeField] private bool _showFloorMarker = true;

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

        /// <summary>씬에 반출 구역이 없으면 임시 드릴카 상자 안에 만든다. 드릴카 모델에는 이미 들어 있다.</summary>
        public static void CreateInDrillCar()
        {
            // 설치자가 씬 오브젝트의 OnEnable 보다 먼저 불릴 수 있어 _active 만으로는 모른다.
            if (_active != null || FindFirstObjectByType<FurnitureDeliveryZone>() != null)
                return;
            DrillCarSafeZone car = FindFirstObjectByType<DrillCarSafeZone>();
            if (car == null)
                return;

            GameObject root = new("FurnitureDeliveryZone");
            root.transform.SetParent(car.transform, false);
            root.transform.localPosition = new Vector3(0f, 0f, car.Size.z * 0.2f);
            FurnitureDeliveryZone zone = root.AddComponent<FurnitureDeliveryZone>();
            zone._size = new Vector3(car.Size.x * 0.6f, car.Size.y, car.Size.z * 0.25f);
            zone._showFloorMarker = true;
            zone.CreateFloorMarker();
        }

        private void OnEnable()
        {
            if (_active == null)
                _active = this;
        }

        private void Start()
        {
            if (_showFloorMarker && transform.Find("DeliveryAreaMarker") == null)
                CreateFloorMarker();
        }

        private void OnDisable()
        {
            if (_active == this)
                _active = null;
        }

        private void CreateFloorMarker()
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "DeliveryAreaMarker";
            floor.transform.SetParent(transform, false);
            floor.transform.localPosition = new Vector3(0f, -_size.y * 0.5f + 0.04f, 0f);
            floor.transform.localScale = new Vector3(_size.x, 0.08f, _size.z);
            floor.GetComponent<Renderer>().material.color = Color.green;
            Destroy(floor.GetComponent<Collider>());
        }

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.9f);
            Gizmos.DrawWireCube(Vector3.zero, _size);
        }
    }
}
