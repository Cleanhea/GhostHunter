using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Map;
using GhostHunter.Gameplay.Recovery;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace GhostHunter.Gameplay.FurnitureDriver
{
    /// <summary>
    /// 분해 가능한 큰 가구의 <b>짐더미 형태</b>(기획서 furniture-multidriver-system.md §4.1). 분해하면 큰 가구는 사라지고
    /// 포장된 짐더미 1개가 나타난다 — 구현은 같은 네트워크 오브젝트가 모양만 바꾸는 것이다(ADR-0009: 런타임 스폰 없음).
    /// 그래서 무게(<c>Rigidbody.mass</c>)·명칭(<see cref="FurnitureDriverPoolItem.PoolKey"/>)·내구도·가구 정의가
    /// <b>따로 옮기지 않아도 그대로</b> 남고, 바뀌는 것은 눈에 보이는 메시와 충돌체의 크기뿐이다.
    ///
    /// <para>짐더미도 일반 가구(<see cref="FurnitureGrabTarget"/>)라서 별도 작업 없이 잡고·옮기고·던져 드릴카까지 가져갈 수 있다.</para>
    ///
    /// <para><b>형태 전환:</b> 큰 가구의 직계 자식(파츠·윤곽선·표지 등)을 모두 끄고 짐더미 자식을 켠다. 원래 꺼져 있던 자식은
    /// 되돌릴 때도 꺼 둔다. 짐더미 자식은 <see cref="Awake"/>에서 코드로 만든다 — 이 컴포넌트의 실행 순서를 앞당겨(-100)
    /// 같은 오브젝트의 다른 컴포넌트가 콜라이더·렌더러 목록을 캐시하기 전에 끝낸다.</para>
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject), typeof(NetworkTransform), typeof(Rigidbody))]
    public sealed class FurnitureBundle : NetworkBehaviour
    {
        [SerializeField] private FurnitureBundleSettings _settings;

        private readonly NetworkVariable<bool> _bundled = new(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private Transform[] _formChildren;
        private bool[] _formChildActive;
        private GameObject _bundleRoot;
        private MeshFilter _bodyFilter;
        private MeshFilter _outlineFilter;
        private Vector3 _bundleSize;
        private Mesh _mesh;

        private NetworkTransform _networkTransform;
        private Rigidbody _body;

        /// <summary>짐더미 형태를 만들 수 있는가 — 설정이 배선되고 원본 크기를 잴 수 있었을 때.</summary>
        public bool IsReady => _bundleRoot != null;

        public bool IsBundled => IsSpawned && _bundled.Value;

        /// <summary>짐더미의 크기(m, 가구 로컬 축). <see cref="IsReady"/>가 아니면 0.</summary>
        public Vector3 BundleSize => _bundleSize;

        private void Awake()
        {
            _networkTransform = GetComponent<NetworkTransform>();
            _body = GetComponent<Rigidbody>();
            if (_settings == null)
            {
                Debug.LogError($"{nameof(FurnitureBundle)}: 설정이 없어 짐더미 형태를 만들지 못합니다.", this);
                return;
            }

            if (!TryMeasureSource(out Bounds source))
            {
                Debug.LogError($"{nameof(FurnitureBundle)}: 원본 가구의 크기를 잴 메시가 없습니다.", this);
                return;
            }

            // 짐더미 자식을 만들기 전에 원래 자식 목록과 켜짐 상태를 기억한다.
            _formChildren = new Transform[transform.childCount];
            _formChildActive = new bool[_formChildren.Length];
            for (int i = 0; i < _formChildren.Length; i++)
            {
                _formChildren[i] = transform.GetChild(i);
                _formChildActive[i] = _formChildren[i].gameObject.activeSelf;
            }

            CreateBundleObject(source);
        }

        public override void OnNetworkSpawn()
        {
            _bundled.OnValueChanged += HandleBundledChanged;
            if (IsServer && !StageRecoveryGate.Restoring)
                _bundled.Value = false;
            ApplyForm(_bundled.Value);
        }

        public override void OnNetworkDespawn() => _bundled.OnValueChanged -= HandleBundledChanged;

        public override void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
            base.OnDestroy();
        }

        /// <summary>
        /// 서버 전용 — 큰 가구를 짐더미로 바꾼다. 잡고 있던 플레이어는 놓게 하고, 짐더미는 큰 가구가 있던 바닥 위
        /// <paramref name="dropHeight"/>(§6.2)에서 떨어진다. 이미 짐더미이거나 만들 수 없으면 거절한다.
        /// </summary>
        public bool ServerConvert(float dropHeight)
        {
            if (!IsServer || !IsSpawned || !IsReady || _bundled.Value
                || _networkTransform == null || !_networkTransform.IsSpawned)
                return false;

            GetComponent<FurnitureGrabTarget>()?.ServerResetForPool();
            ServerSetBundled(true);

            Vector3 position = transform.position + Vector3.up * dropHeight;
            _networkTransform.Teleport(position, transform.rotation, transform.localScale);
            // 보간 바디는 트랜스폼만 옮기면 다음 물리 스텝에 이전 자세로 되돌아간다.
            RoomPreset.TeleportBody(_body, position, transform.rotation);
            if (!_body.isKinematic)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
                _body.WakeUp();
            }

            GetComponent<FurnitureNetworkPhysics>()?.ServerProtectPlacement();
            return true;
        }

        /// <summary>서버 전용 — 형태만 바꾼다(위치는 옮기지 않는다). 복구·큰 가구 재활성화가 쓴다.</summary>
        public void ServerSetBundled(bool bundled)
        {
            if (!IsServer || !IsSpawned)
                return;
            _bundled.Value = bundled;
            ApplyForm(bundled);
        }

        private void HandleBundledChanged(bool previous, bool current) => ApplyForm(current);

        private void ApplyForm(bool bundled)
        {
            if (!IsReady)
                return;
            if (bundled)
                EnsureMesh();
            for (int i = 0; i < _formChildren.Length; i++)
                if (_formChildren[i] != null)
                    _formChildren[i].gameObject.SetActive(!bundled && _formChildActive[i]);
            _bundleRoot.SetActive(bundled);
        }

        private void EnsureMesh()
        {
            if (_mesh != null)
                return;
            _mesh = FurnitureBundleMesh.Build(_bundleSize, _settings.Shape);
            _bodyFilter.sharedMesh = _mesh;
            if (_outlineFilter != null)
                _outlineFilter.sharedMesh = _mesh;
        }

        /// <summary>원본(큰 가구) 형태의 렌더러 메시를 가구 로컬 좌표로 모아 경계를 잰다.</summary>
        private bool TryMeasureSource(out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || !filter.TryGetComponent(out MeshRenderer _))
                    continue;

                Bounds mesh = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mesh.center + Vector3.Scale(mesh.extents, new Vector3(
                        (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    Vector3 local = transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (found)
                    {
                        bounds.Encapsulate(local);
                    }
                    else
                    {
                        bounds = new Bounds(local, Vector3.zero);
                        found = true;
                    }
                }
            }
            return found && bounds.size.sqrMagnitude > 0f;
        }

        private void CreateBundleObject(Bounds source)
        {
            _bundleSize = _settings.SizeFor(source.size);

            _bundleRoot = new GameObject("FurnitureBundle");
            _bundleRoot.SetActive(false);
            _bundleRoot.layer = ResolveLayer();
            Transform root = _bundleRoot.transform;
            root.SetParent(transform, false);
            // 짐더미 바닥이 큰 가구 바닥과 같고, 가로·세로 중심도 같다.
            root.localPosition = new Vector3(source.center.x, source.min.y + _bundleSize.y * 0.5f, source.center.z);

            _bodyFilter = _bundleRoot.AddComponent<MeshFilter>();
            var renderer = _bundleRoot.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { _settings.BodyMaterial, _settings.StrapMaterial };
            var box = _bundleRoot.AddComponent<BoxCollider>();
            box.size = _bundleSize;

            if (_settings.OutlineMaterial != null && TryGetComponent(out FurnitureOutline outline))
            {
                var shell = new GameObject("FurnitureBundle_Outline") { layer = _bundleRoot.layer };
                shell.transform.SetParent(root, false);
                _outlineFilter = shell.AddComponent<MeshFilter>();
                var shellRenderer = shell.AddComponent<MeshRenderer>();
                shellRenderer.sharedMaterial = _settings.OutlineMaterial;
                shellRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                shellRenderer.enabled = false;
                outline.AddOutlineRenderer(shellRenderer);
            }
        }

        /// <summary>짐더미 자식의 레이어 — 큰 가구의 콜라이더와 같은 레이어(잡기·충돌 판정이 레이어로 걸러진다).</summary>
        private int ResolveLayer()
        {
            Collider own = GetComponentInChildren<Collider>(true);
            return own != null ? own.gameObject.layer : gameObject.layer;
        }
    }
}
