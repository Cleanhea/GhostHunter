using System.Collections.Generic;
using GhostHunter.Gameplay.FurnitureDriver;
using UnityEngine;
using UnityEngine.Rendering;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>
    /// 반출 구역에 넣은 목표 가구마다 드릴카 화물칸에 짐이 하나씩 쌓이는 시각 연출(2026-10-29 사용자 요청).
    /// 게임 규칙과 무관한 로컬 표시다 — 반출 완료 개수는 <see cref="RandomFurnitureItem.IsDelivered"/>(복제되는 배치 상태)에서
    /// 읽으므로 서버·클라이언트가 따로 통신하지 않고, 늦게 들어온 접속자와 호스트 이전 복구도 같은 더미를 본다.
    /// 충돌체가 없고 모든 도형은 이 오브젝트의 자식이라 드릴카와 함께 사라진다.
    ///
    /// <para>이 오브젝트의 위치는 화물칸 <b>바닥 가운데</b>, 로컬 +Y 가 위다. 크기와 짐 모양은
    /// <see cref="DeliveredCargoSettings"/>, 쌓는 규칙은 <see cref="DeliveredCargoLayout"/>.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeliveredCargoPile : MonoBehaviour
    {
        private const float PollSeconds = 0.2f;

        [SerializeField] private DeliveredCargoSettings _settings;

        [Tooltip("짐을 쌓을 상자 크기(m). 오브젝트 위치가 바닥 가운데다. 높이는 쌓을 수 있는 한계다. 스케일은 1로 둔다.")]
        [SerializeField] private Vector3 _areaSize = new(3.2f, 2.4f, 1.5f);

        private sealed class Crate
        {
            public GameObject Object;
            public Vector3 RestPosition;
            public float StartTime;
            public bool Dropping;
        }

        private readonly List<Crate> _crates = new();
        private Mesh[] _meshes;
        private DeliveredCargoLayout _layout;
        private FurnitureSpawnController _furniture;
        private float _nextPoll;
        private bool _initialized;

        /// <summary>쌓을 상자 크기(m).</summary>
        public Vector3 AreaSize => _areaSize;

        /// <summary>지금 화면에 놓인 짐 수(구역 높이를 넘어 못 쌓은 것은 뺀다).</summary>
        public int CrateCount => _crates.Count;

        private void Start()
        {
            // 그래픽 장치가 없는 환경(전용 서버·일부 배치 실행)에서는 그릴 것이 없다.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                enabled = false;
                return;
            }

            if (_settings == null || _settings.Bundle == null || _settings.CrateSizes.Length == 0)
            {
                Debug.LogError($"{nameof(DeliveredCargoPile)}: 짐 설정 또는 짐더미 설정이 없습니다.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            if (Time.time >= _nextPoll)
            {
                _nextPoll = Time.time + PollSeconds;
                Poll();
            }

            AnimateDrops();
        }

        private void OnDestroy()
        {
            if (_meshes == null)
                return;

            for (int i = 0; i < _meshes.Length; i++)
                DestroyObject(_meshes[i]);
        }

        // 에디터 모드 테스트에서는 Destroy 를 부를 수 없다.
        private static void DestroyObject(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        private void Poll()
        {
            int delivered = CountDelivered();
            if (delivered < 0)
                return;

            SyncTo(delivered);
        }

        /// <summary>반출 완료 수에 맞춰 짐을 늘리거나 다시 쌓는다. 처음 맞출 때는 연출 없이 바로 놓는다.</summary>
        internal void SyncTo(int delivered)
        {
            if (_layout == null)
                _layout = NewLayout();

            if (delivered < _layout.Placed)
            {
                // 새 판·복구로 반출 기록이 줄었다.
                Clear();
                _layout = NewLayout();
                _initialized = false;
            }

            bool animate = _initialized;
            _initialized = true;
            int added = 0;
            while (_layout.Placed < delivered)
            {
                if (_layout.TryPlaceNext(out CargoPlacement placement))
                    AddCrate(placement, animate, added++);
            }
        }

        private DeliveredCargoLayout NewLayout() => new(_areaSize, _settings.CrateSizes, _settings.Seed,
            _settings.YawJitterDegrees, _settings.OffsetJitter);

        /// <summary>목표 가구 중 반출 완료된 수. 가구 풀이 아직 준비되지 않았으면 -1.</summary>
        private int CountDelivered()
        {
            if (_furniture == null)
                _furniture = FindFirstObjectByType<FurnitureSpawnController>();
            if (_furniture == null || !_furniture.IsReady)
                return -1;

            int delivered = 0;
            RandomFurnitureItem[] items = _furniture.Items;
            for (int i = 0; i < items.Length; i++)
                if (items[i] != null && items[i].IsDelivered)
                    delivered++;
            return delivered;
        }

        private void AddCrate(CargoPlacement placement, bool animate, int order)
        {
            if (_meshes == null)
                _meshes = new Mesh[_settings.CrateSizes.Length];
            if (_meshes[placement.Variant] == null)
                _meshes[placement.Variant] = FurnitureBundleMesh.Build(
                    _settings.CrateSizes[placement.Variant], _settings.Bundle.Shape);

            var crateObject = new GameObject($"Cargo_{_crates.Count}");
            crateObject.transform.SetParent(transform, false);
            crateObject.transform.SetLocalPositionAndRotation(placement.Center,
                Quaternion.Euler(0f, placement.YawDegrees, 0f));
            crateObject.AddComponent<MeshFilter>().sharedMesh = _meshes[placement.Variant];
            crateObject.AddComponent<MeshRenderer>().sharedMaterials =
                new[] { _settings.Bundle.BodyMaterial, _settings.Bundle.StrapMaterial };

            var crate = new Crate { Object = crateObject, RestPosition = placement.Center };
            if (animate)
            {
                crate.Dropping = true;
                crate.StartTime = Time.time + order * _settings.DropStaggerSeconds;
                // 시작 시각이 되기 전에는 보이지 않는다.
                crateObject.SetActive(false);
            }

            _crates.Add(crate);
        }

        private void AnimateDrops()
        {
            for (int i = 0; i < _crates.Count; i++)
            {
                Crate crate = _crates[i];
                if (!crate.Dropping)
                    continue;

                float t = (Time.time - crate.StartTime) / _settings.DropSeconds;
                if (t < 0f)
                    continue;

                if (!crate.Object.activeSelf)
                    crate.Object.SetActive(true);

                // 위에서 떨어져 바닥(또는 아래 짐)에 튕기며 자리 잡는다. 스케일은 건드리지 않는다(스케일 1 규칙).
                float settled = EaseOutBounce(Mathf.Clamp01(t));
                crate.Object.transform.localPosition =
                    crate.RestPosition + Vector3.up * (_settings.DropHeight * (1f - settled));
                if (t >= 1f)
                {
                    crate.Object.transform.localPosition = crate.RestPosition;
                    crate.Dropping = false;
                }
            }
        }

        private void Clear()
        {
            for (int i = 0; i < _crates.Count; i++)
                DestroyObject(_crates[i].Object);
            _crates.Clear();
        }

        private static float EaseOutBounce(float t)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;
            if (t < 1f / d1)
                return n1 * t * t;
            if (t < 2f / d1)
            {
                t -= 1.5f / d1;
                return n1 * t * t + 0.75f;
            }

            if (t < 2.5f / d1)
            {
                t -= 2.25f / d1;
                return n1 * t * t + 0.9375f;
            }

            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
            Gizmos.DrawWireCube(Vector3.up * (_areaSize.y * 0.5f), _areaSize);
        }
    }
}
