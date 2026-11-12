using UnityEngine;

namespace GhostHunter.Gameplay.Theft
{
    /// <summary>
    /// 훔칠 수 있는 귀중품 하나(theft-system.md). 조준하고 E를 누르면 서버가 확정하고, 확정되면 모든 피어에서 사라진다
    /// (TH-Q3 확정 — 한 번만 훔칠 수 있다). 네트워크 오브젝트가 아니다 — 씬 배치 <c>NetworkObject</c> 해시 문제를 피하려고
    /// 같은 씬 파일에서 모든 피어가 똑같이 계산하는 <see cref="Id"/>(씬 이름 + 시작 위치)로 서로를 가리킨다.
    /// 상태(훔쳤는지)는 <see cref="StageTheftService"/> 가 들고, 전달은 <see cref="PlayerTheft"/> RPC 가 한다.
    ///
    /// <b>[TEMP] 외형:</b> 귀중품 아트가 없어 프리팹의 <see cref="_size"/> 로 상자 메시를 코드에서 만든다(스케일 1 규칙).
    /// 가구 위가 아니라 고정된 바닥·붙박이 위에 둔다 — 가구에 얹으면 가구가 움직일 때 공중에 남는다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class Valuable : MonoBehaviour
    {
        [Tooltip("가격($). 정신력 금액표와 같은 4종(TH-Q1 확정).")]
        [SerializeField] private ValuablePrice _price = ValuablePrice.Two;

        [Tooltip("[TEMP] 코드로 만드는 상자 외형·콜라이더 크기(m). 원점이 바닥 가운데다.")]
        [SerializeField] private Vector3 _size = new(0.24f, 0.1f, 0.16f);

        [SerializeField] private MeshFilter _meshFilter;
        [SerializeField] private MeshRenderer _renderer;
        [SerializeField] private BoxCollider _collider;

        private Mesh _runtimeMesh;
        private bool _idComputed;
        private int _id;

        public int Price => (int)_price;
        public bool IsStolen { get; private set; }
        public Collider Collider => _collider;
        public Vector3 Size => _size;

        /// <summary>
        /// 씬 안에서 귀중품을 가리키는 번호. 처음 읽을 때 씬 이름과 <b>그때의</b> 위치(mm 반올림)로 정한다 —
        /// 씬을 연 직후라 모든 피어에서 같은 값이다. 같은 자리에 두 개를 두면 겹치므로 배치 검사가 막는다.
        /// </summary>
        public int Id
        {
            get
            {
                if (!_idComputed)
                {
                    _id = ComputeId(gameObject.scene.name, transform.position);
                    _idComputed = true;
                }
                return _id;
            }
        }

        private void Awake()
        {
            _ = Id;
            if (_collider == null)
                _collider = GetComponent<BoxCollider>();
            _collider.size = _size;
            _collider.center = new Vector3(0f, _size.y * 0.5f, 0f);
            if (_meshFilter != null && _meshFilter.sharedMesh == null)
            {
                _runtimeMesh = CreateBoxMesh(_size);
                _meshFilter.sharedMesh = _runtimeMesh;
            }
        }

        private void OnDestroy()
        {
            if (_runtimeMesh != null)
                Destroy(_runtimeMesh);
        }

        /// <summary>훔쳐진 귀중품을 숨긴다(모든 피어). 다시 부르면 아무것도 하지 않는다.</summary>
        public void MarkStolen()
        {
            if (IsStolen)
                return;
            IsStolen = true;
            if (_renderer != null)
                _renderer.enabled = false;
            if (_collider != null)
                _collider.enabled = false;
        }

        /// <summary>씬 이름과 위치(mm 반올림)의 FNV-1a 해시.</summary>
        public static int ComputeId(string sceneName, Vector3 position)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char c in sceneName ?? string.Empty)
                    hash = (hash ^ c) * 16777619u;
                hash = Mix(hash, Mathf.RoundToInt(position.x * 1000f));
                hash = Mix(hash, Mathf.RoundToInt(position.y * 1000f));
                hash = Mix(hash, Mathf.RoundToInt(position.z * 1000f));
                return (int)hash;
            }
        }

        private static uint Mix(uint hash, int value)
        {
            unchecked
            {
                for (int i = 0; i < 4; i++)
                    hash = (hash ^ (uint)((value >> (i * 8)) & 0xFF)) * 16777619u;
                return hash;
            }
        }

        private static Mesh CreateBoxMesh(Vector3 size)
        {
            float x = size.x * 0.5f;
            float z = size.z * 0.5f;
            float y = size.y;
            Vector3[] corners =
            {
                new(-x, 0f, -z), new(x, 0f, -z), new(x, 0f, z), new(-x, 0f, z),
                new(-x, y, -z), new(x, y, -z), new(x, y, z), new(-x, y, z),
            };
            int[][] faces =
            {
                new[] { 4, 5, 6, 7 }, // 위
                new[] { 3, 2, 1, 0 }, // 아래
                new[] { 0, 1, 5, 4 }, // 앞(-Z)
                new[] { 2, 3, 7, 6 }, // 뒤(+Z)
                new[] { 1, 2, 6, 5 }, // 오른쪽(+X)
                new[] { 3, 0, 4, 7 }, // 왼쪽(-X)
            };
            var vertices = new Vector3[24];
            var triangles = new int[36];
            for (int f = 0; f < faces.Length; f++)
            {
                for (int v = 0; v < 4; v++)
                    vertices[f * 4 + v] = corners[faces[f][v]];
                int t = f * 6;
                int b = f * 4;
                triangles[t] = b;
                triangles[t + 1] = b + 2;
                triangles[t + 2] = b + 1;
                triangles[t + 3] = b;
                triangles[t + 4] = b + 3;
                triangles[t + 5] = b + 2;
            }
            var mesh = new Mesh { name = "ValuableTempBox", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void OnDrawGizmos()
        {
            // 외형을 런타임에만 만들어서 Scene 뷰에서 자리가 보이도록 그린다.
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.82f, 0.2f, 0.9f);
            Gizmos.DrawCube(new Vector3(0f, _size.y * 0.5f, 0f), _size);
        }
    }
}
