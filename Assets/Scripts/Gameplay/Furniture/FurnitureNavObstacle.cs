using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace GhostHunter.Gameplay.Furniture
{
    /// <summary>
    /// 가구를 귀신 경로의 장애물로 반영한다 — 귀신이 가구와 물리 충돌하므로(<c>GhostPrototypeSettings.CollideWithFurniture</c>)
    /// 가구를 뚫는 경로를 만들면 가구 앞에서 걸려 같은 경로를 반복한다.
    /// 가구는 던져지고 옮겨지므로 굽는 NavMesh 에는 넣지 않고(<c>GhostNavigation.EnsureNavMesh</c> 는 가구 레이어를 뺀다),
    /// 서버에서만 <see cref="NavMeshObstacle"/> 의 carving 으로 지금 놓인 자리를 뚫어 둔다.
    /// 움직이는 동안(운반·발사)은 carve 하지 않고(`carveOnlyStationary`) 멈춘 뒤에 다시 뚫는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FurnitureNavObstacle : MonoBehaviour
    {
        /// <summary>귀신 캐릭터 컨트롤러의 턱 높이(0.3)보다 낮은 가구는 그냥 넘어가므로 뚫지 않는다.</summary>
        public const float MinObstacleHeight = 0.4f;
        /// <summary>가로세로가 이보다 작은 소품은 경로에 영향을 주지 않는다.</summary>
        public const float MinObstacleFootprint = 0.3f;

        private const float MoveThreshold = 0.1f;
        private const float TimeToStationary = 0.5f;
        // 바닥에 딱 붙은 가구도 NavMesh 면과 세로로 겹치게 아래로 조금 늘린다.
        private const float BottomMargin = 0.1f;

        private static readonly List<FurnitureNavObstacle> Registry = new();

        private NavMeshObstacle _obstacle;

        public bool IsCarving => _obstacle != null && _obstacle.enabled && _obstacle.carving;

        /// <summary>서버에서 가구에 장애물을 붙인다. 이미 있으면 그것을 돌려준다.</summary>
        public static FurnitureNavObstacle Attach(GameObject furniture)
        {
            if (furniture == null)
                return null;

            FurnitureNavObstacle existing = furniture.GetComponent<FurnitureNavObstacle>();
            return existing != null ? existing : furniture.AddComponent<FurnitureNavObstacle>();
        }

        /// <summary>
        /// 가구가 쓸 수 있고(<paramref name="active"/>) 충분히 크면 지금 콜라이더 경계로 장애물을 켠다.
        /// 보관·파손·풀 대기 가구나 작은 소품이면 끈다.
        /// </summary>
        public void Refresh(bool active, IReadOnlyList<Collider> colliders)
        {
            if (_obstacle == null)
                return;

            if (active && TryComputeLocalBounds(transform, colliders, out Bounds local)
                && IsLargeEnough(local.size))
            {
                // 아래로 늘린 만큼 중심도 내린다.
                var size = new Vector3(local.size.x, local.size.y + BottomMargin, local.size.z);
                _obstacle.size = size;
                _obstacle.center = local.center - new Vector3(0f, BottomMargin * 0.5f, 0f);
                _obstacle.enabled = true;
            }
            else
            {
                _obstacle.enabled = false;
            }
        }

        /// <summary>
        /// 귀신 NavMesh 가 (다시) 추가된 뒤 호출한다 — 이미 켜진 장애물이 새 NavMesh 에도 확실히 carve 하도록
        /// 껐다 켠다.
        /// </summary>
        public static void RefreshAll()
        {
            for (int i = 0; i < Registry.Count; i++)
            {
                FurnitureNavObstacle item = Registry[i];
                if (item == null || item._obstacle == null || !item._obstacle.enabled)
                    continue;

                item._obstacle.enabled = false;
                item._obstacle.enabled = true;
            }
        }

        /// <summary>가구 로컬 경계 크기 기준 — 낮거나 바닥 면적이 작으면 귀신이 그냥 넘어가는 소품이다.</summary>
        public static bool IsLargeEnough(Vector3 size)
        {
            return size.y >= MinObstacleHeight
                && Mathf.Max(size.x, size.z) >= MinObstacleFootprint;
        }

        /// <summary>
        /// 비트리거 콜라이더 전체를 가구 로컬 좌표의 경계 상자 하나로 묶는다. 물리 월드의 <c>Collider.bounds</c> 가 아니라
        /// 콜라이더 자신의 로컬 형상에서 계산한다 — 물리 동기화 시점이나 가구가 돌아간 정도에 따라 값이 달라지지 않고,
        /// 꺼진 콜라이더(풀 대기)도 같은 값이 나온다.
        /// </summary>
        public static bool TryComputeLocalBounds(
            Transform root, IReadOnlyList<Collider> colliders, out Bounds local)
        {
            local = default;
            bool found = false;
            Matrix4x4 rootToWorldInverse = root.worldToLocalMatrix;

            for (int i = 0; i < colliders.Count; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || collider.isTrigger || !TryGetShapeBounds(collider, out Bounds shape))
                    continue;

                Matrix4x4 toRoot = rootToWorldInverse * collider.transform.localToWorldMatrix;
                Vector3 min = shape.min;
                Vector3 max = shape.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = new Vector3(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    Vector3 localPoint = toRoot.MultiplyPoint3x4(point);
                    if (!found)
                    {
                        local = new Bounds(localPoint, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        local.Encapsulate(localPoint);
                    }
                }
            }

            return found;
        }

        /// <summary>콜라이더 로컬 좌표의 경계 상자. 알 수 없는 종류는 가져오지 않는다.</summary>
        private static bool TryGetShapeBounds(Collider collider, out Bounds bounds)
        {
            switch (collider)
            {
                case BoxCollider box:
                    bounds = new Bounds(box.center, box.size);
                    return true;
                case SphereCollider sphere:
                    bounds = new Bounds(sphere.center, Vector3.one * (sphere.radius * 2f));
                    return true;
                case CapsuleCollider capsule:
                {
                    float diameter = capsule.radius * 2f;
                    float length = Mathf.Max(capsule.height, diameter);
                    Vector3 size = capsule.direction switch
                    {
                        0 => new Vector3(length, diameter, diameter),
                        1 => new Vector3(diameter, length, diameter),
                        _ => new Vector3(diameter, diameter, length),
                    };
                    bounds = new Bounds(capsule.center, size);
                    return true;
                }
                case MeshCollider mesh when mesh.sharedMesh != null:
                    bounds = mesh.sharedMesh.bounds;
                    return true;
                default:
                    bounds = default;
                    return false;
            }
        }

        private void Awake()
        {
            _obstacle = gameObject.AddComponent<NavMeshObstacle>();
            _obstacle.shape = NavMeshObstacleShape.Box;
            _obstacle.carving = true;
            _obstacle.carveOnlyStationary = true;
            _obstacle.carvingMoveThreshold = MoveThreshold;
            _obstacle.carvingTimeToStationary = TimeToStationary;
            _obstacle.enabled = false;
        }

        private void OnEnable() => Registry.Add(this);

        private void OnDisable() => Registry.Remove(this);
    }
}
