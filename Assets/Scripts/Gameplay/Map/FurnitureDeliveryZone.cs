using GhostHunter.Gameplay.Ghost;
using UnityEngine;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>드릴카 안의 목표 가구 반출 완료 구역. 안전 구역과 별도로 판정한다.</summary>
    [DisallowMultipleComponent]
    public sealed class FurnitureDeliveryZone : MonoBehaviour
    {
        private static FurnitureDeliveryZone _active;
        private Vector3 _size;

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

        public static void CreateInDrillCar()
        {
            if (_active != null)
                return;
            DrillCarSafeZone car = FindFirstObjectByType<DrillCarSafeZone>();
            if (car == null)
                return;

            GameObject root = new("FurnitureDeliveryZone");
            root.transform.SetParent(car.transform, false);
            root.transform.localPosition = new Vector3(0f, 0f, car.Size.z * 0.2f);
            FurnitureDeliveryZone zone = root.AddComponent<FurnitureDeliveryZone>();
            zone._size = new Vector3(car.Size.x * 0.6f, car.Size.y, car.Size.z * 0.25f);
            _active = zone;

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "DeliveryAreaMarker";
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0f, -car.Size.y * 0.5f + 0.04f, 0f);
            floor.transform.localScale = new Vector3(zone._size.x, 0.08f, zone._size.z);
            floor.GetComponent<Renderer>().material.color = Color.green;
            Destroy(floor.GetComponent<Collider>());
        }

        private void OnDestroy()
        {
            if (_active == this)
                _active = null;
        }
    }
}
