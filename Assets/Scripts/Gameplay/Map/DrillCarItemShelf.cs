using System.Collections.Generic;
using GhostHunter.Core;
using GhostHunter.Core.Steam;
using GhostHunter.Gameplay.Ghost;
using UnityEngine;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>
    /// 드릴카 선반(2026-11-09 사용자 확정). 상점에서 산 팀 공동 아이템(<see cref="IStageShopService.Shelf"/>)을 선반 면에
    /// 품목별로 차곡차곡 쌓아 보여 주고, 각 상자(<see cref="DrillCarShelfItem"/>)에서 E 를 누르면 그 사람이 하나를 꺼내 갖는다
    /// (서버 판정은 <c>PlayerInteractor</c>). 재고가 바뀔 때마다 모든 피어가 같은 배치로 다시 쌓는다 — 복제할 상태가 없다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DrillCarItemShelf : MonoBehaviour
    {
        /// <summary>한 줄에 위로 쌓는 상자 수. 넘치면 옆 줄로 간다.</summary>
        private const int StackHeight = 4;
        /// <summary>품목마다 그리는 최대 줄 수 — 선반을 넘치지 않게 한다(품목당 최대 12개 표시, 꺼내기는 재고 전부).</summary>
        private const int MaxColumnsPerItem = 3;
        private const float ColumnGap = 0.02f;
        private const float SectionGap = 0.06f;

        // 선반 면 재기 — 앵커 아래로 같은 선반 충돌체의 칸 윗면을 차례로 찾아 높이로 가운데 칸을 고르고, 그 칸과 같은 높이
        // (±ProbeTolerance)로 이어지는 구간의 가운데에 쌓는다. 드릴카 모델 선반은 바닥에서 2.05·1.62·1.14·0.66·0.18m 다섯 면이고
        // 앵커는 맨 윗면보다 0.19m 위·한쪽 끝 근처에 있다(2026-11-09 측정).
        private const int MaxTiers = 10;
        private const float TierSearchDepth = 3f;
        /// <summary>찾은 윗면 바로 아래(판 안)에서 다음 레이를 쏜다 — 판 아랫면은 뒷면이라 맞지 않고 다음 칸 윗면이 맞는다.</summary>
        private const float TierSkip = 0.005f;
        private const float ProbeStep = 0.02f;
        private const float ProbeRangeX = 3f;
        private const float ProbeRangeZ = 0.6f;
        private const float ProbeAbove = 0.05f;
        private const float ProbeDepth = 0.4f;
        private const float ProbeTolerance = 0.02f;

        /// <summary>서버가 꺼내기를 받아 주는 선반(앵커)과 플레이어 사이 최대 거리(m).</summary>
        public const float InteractDistance = 3f;

        private readonly List<GameObject> _views = new();
        private readonly List<float> _tiers = new();
        private readonly Dictionary<ShopItem, Mesh> _meshes = new();
        private readonly Dictionary<ShopItem, Material> _materials = new();
        private IStageShopService _shop;
        private Transform _root;
        private ShelfStock _shown;
        private bool _built;
        // 상자 묶음의 바닥 가운데(앵커 로컬) — 선반 면 높이, 길이·깊이 방향 가운데.
        private Vector3 _stackBase;

        /// <summary>상자 묶음의 바닥 가운데(월드). 서버 거리 판정에 쓴다.</summary>
        public Vector3 Anchor => _root != null ? _root.TransformPoint(_stackBase) : transform.position;

        /// <summary>앵커 아래에서 선반 면을 찾아 그 위 가운데에 쌓는가. 못 찾았으면 앵커 자리에 쌓는다.</summary>
        public bool FoundShelfSurface { get; private set; }

        /// <summary>상자 묶음의 가운데(앵커 로컬 X).</summary>
        public float StackCenterX => _stackBase.x;

        /// <summary>상자를 올린 칸 윗면 높이(앵커 로컬 Y).</summary>
        public float StackBaseHeight => _stackBase.y;

        /// <summary>찾은 선반 칸 윗면 높이들(앵커 로컬 Y, 위에서 아래 순).</summary>
        public IReadOnlyList<float> TierHeights => _tiers;

        /// <summary>쌓는 기준 면(앵커). 테스트가 상자 위치를 이 좌표계로 잰다.</summary>
        public Transform StackRoot => _root;

        public int CountOf(ShopItem item) => _shop != null && _shop.IsAvailable ? _shop.Shelf.CountOf(item) : 0;

        private void Start()
        {
            if (!Services.TryGet(out _shop))
                return;

            DrillCarSafeZone zone = FindFirstObjectByType<DrillCarSafeZone>();
            if (zone == null)
                return;

            _root = zone.ItemShelfAnchor;
            if (_root == null)
                _root = BuildTemporaryShelf(zone);
            // 상자를 만들기 전에 잰다 — 상자 충돌체가 선반 면으로 잡히지 않게.
            _stackBase = MeasureShelf();

            _shop.Changed += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_shop != null)
                _shop.Changed -= Refresh;
            foreach (Mesh mesh in _meshes.Values)
                Destroy(mesh);
            _meshes.Clear();
            foreach (Material material in _materials.Values)
                Destroy(material);
            _materials.Clear();
        }

        /// <summary>재고가 달라졌을 때만 다시 쌓는다(Steam 로비 갱신·로컬 상태 재전송은 같은 값으로도 자주 온다).</summary>
        private void Refresh()
        {
            if (_root == null)
                return;
            ShelfStock stock = _shop != null && _shop.IsAvailable ? _shop.Shelf : default;
            if (_built && stock.Equals(_shown))
                return;
            _built = true;
            _shown = stock;
            Rebuild(stock);
        }

        /// <summary>
        /// 앵커 로컬 X(선반 길이 방향)로 품목 구역을 차례로 놓고, 구역 안에서는 상자를 아래부터 <see cref="StackHeight"/>개씩 쌓은 뒤
        /// 옆 줄로 간다. 전체 묶음이 선반 면 위 가운데(<see cref="MeasureShelf"/>)에 오도록 한다(2026-11-09 사용자 요청).
        /// 재고가 0 인 품목은 자리를 차지하지 않는다.
        /// </summary>
        private void Rebuild(ShelfStock stock)
        {
            foreach (GameObject view in _views)
                Destroy(view);
            _views.Clear();

            float x = _stackBase.x - LayoutWidth(stock) * 0.5f;
            foreach (ShopItem item in StageShopRules.Items)
            {
                int shown = Mathf.Min(stock.CountOf(item), StackHeight * MaxColumnsPerItem);
                if (shown <= 0)
                    continue;

                Vector3 size = SizeOf(item);
                for (int index = 0; index < shown; index++)
                {
                    int column = index / StackHeight;
                    int level = index % StackHeight;
                    var position = new Vector3(
                        x + column * (size.x + ColumnGap) + size.x * 0.5f,
                        _stackBase.y + level * size.y + size.y * 0.5f,
                        _stackBase.z);
                    _views.Add(CreateView(item, size, position, index));
                }

                int columns = (shown + StackHeight - 1) / StackHeight;
                x += columns * (size.x + ColumnGap) + SectionGap;
            }
        }

        /// <summary>
        /// 상자 묶음의 바닥 가운데(앵커 로컬). 앵커는 선반 근처 한 점일 뿐 칸 위·가운데라는 보장이 없어서(모델 임포트 콜라이더),
        /// 앵커 아래로 같은 선반 충돌체의 칸 윗면을 모두 찾아 <b>높이로 가운데 칸</b>을 고르고(2026-11-09 사용자 요청 "높이에서 중앙"),
        /// 그 높이로 이어지는 구간을 앵커 X(길이)·Z(깊이) 축으로 훑어 가운데를 쓴다.
        /// 앵커 아래에 면이 없으면(콜라이더가 없는 임시 선반 등) 앵커 자리에 쌓는다.
        /// </summary>
        private Vector3 MeasureShelf()
        {
            CollectTiers();
            FoundShelfSurface = _tiers.Count > 0;
            if (!FoundShelfSurface)
                return Vector3.zero;
            float surface = _tiers[(_tiers.Count - 1) / 2];

            float minX = 0f, maxX = 0f;
            while (minX - ProbeStep >= -ProbeRangeX && IsSurface(minX - ProbeStep, 0f, surface))
                minX -= ProbeStep;
            while (maxX + ProbeStep <= ProbeRangeX && IsSurface(maxX + ProbeStep, 0f, surface))
                maxX += ProbeStep;
            float centerX = (minX + maxX) * 0.5f;

            float minZ = 0f, maxZ = 0f;
            while (minZ - ProbeStep >= -ProbeRangeZ && IsSurface(centerX, minZ - ProbeStep, surface))
                minZ -= ProbeStep;
            while (maxZ + ProbeStep <= ProbeRangeZ && IsSurface(centerX, maxZ + ProbeStep, surface))
                maxZ += ProbeStep;
            return new Vector3(centerX, surface, (minZ + maxZ) * 0.5f);
        }

        /// <summary>앵커 바로 아래로 내려가며 첫 충돌체(선반)의 칸 윗면 높이를 위에서부터 모은다. 다른 충돌체(바닥)가 나오면 멈춘다.</summary>
        private void CollectTiers()
        {
            _tiers.Clear();
            Collider shelf = null;
            float from = ProbeAbove;
            for (int i = 0; i < MaxTiers; i++)
            {
                if (!TryHitBelow(0f, 0f, from, TierSearchDepth, out RaycastHit hit)
                    || (shelf != null && hit.collider != shelf))
                    break;
                shelf = hit.collider;
                float height = _root.InverseTransformPoint(hit.point).y;
                _tiers.Add(height);
                from = height - TierSkip;
            }
        }

        /// <summary>그 칸 높이 바로 위에서 쏜 레이가 같은 높이 면에 맞는가 — 칸 밖이면 아래 칸·바닥에 맞거나 아무것도 없다.</summary>
        private bool IsSurface(float localX, float localZ, float surface) =>
            TryHitBelow(localX, localZ, surface + ProbeAbove, ProbeAbove + ProbeDepth, out RaycastHit hit)
            && Mathf.Abs(_root.InverseTransformPoint(hit.point).y - surface) <= ProbeTolerance;

        /// <summary>앵커 로컬 (x, fromY, z) 에서 앵커 아래 방향으로 레이를 쏜다. 플레이어는 맞히지 않는다.</summary>
        private bool TryHitBelow(float localX, float localZ, float fromY, float distance, out RaycastHit hit)
        {
            int mask = GameLayers.Player >= 0 ? ~(1 << GameLayers.Player) : ~0;
            Vector3 origin = _root.TransformPoint(new Vector3(localX, fromY, localZ));
            return Physics.Raycast(origin, -_root.up, out hit, distance, mask, QueryTriggerInteraction.Ignore);
        }

        /// <summary><see cref="Rebuild"/> 이 쌓는 상자 묶음의 선반 방향 폭(m). 마지막 줄 뒤의 간격은 빼고 잰다.</summary>
        private static float LayoutWidth(ShelfStock stock)
        {
            float width = 0f;
            bool any = false;
            foreach (ShopItem item in StageShopRules.Items)
            {
                int shown = Mathf.Min(stock.CountOf(item), StackHeight * MaxColumnsPerItem);
                if (shown <= 0)
                    continue;
                int columns = (shown + StackHeight - 1) / StackHeight;
                width += columns * (SizeOf(item).x + ColumnGap) + SectionGap;
                any = true;
            }
            return any ? width - ColumnGap - SectionGap : 0f;
        }

        private GameObject CreateView(ShopItem item, Vector3 size, Vector3 localPosition, int index)
        {
            // 기본 상자의 머티리얼·충돌체를 쓰되, 스케일 대신 크기에 맞춘 메시를 넣는다(스케일 1 규칙).
            GameObject view = GameObject.CreatePrimitive(PrimitiveType.Cube);
            view.name = $"ShelfItem_{item}_{index}";
            view.transform.SetParent(_root, false);
            view.transform.SetLocalPositionAndRotation(localPosition, Quaternion.identity);
            view.GetComponent<MeshFilter>().sharedMesh = MeshOf(item, size);
            view.GetComponent<BoxCollider>().size = size;
            Renderer renderer = view.GetComponent<Renderer>();
            renderer.sharedMaterial = MaterialOf(item, renderer.sharedMaterial);
            view.AddComponent<DrillCarShelfItem>().Initialize(this, item);
            return view;
        }

        private Mesh MeshOf(ShopItem item, Vector3 size)
        {
            if (!_meshes.TryGetValue(item, out Mesh mesh))
            {
                mesh = BuildBoxMesh(size);
                mesh.name = $"ShelfItem_{item}";
                _meshes[item] = mesh;
            }
            return mesh;
        }

        private Material MaterialOf(ShopItem item, Material source)
        {
            if (!_materials.TryGetValue(item, out Material material))
            {
                material = new Material(source) { name = $"ShelfItem_{item}", color = ColorOf(item) };
                _materials[item] = material;
            }
            return material;
        }

        /// <summary>
        /// 상자 크기(앵커 로컬 X = 선반 길이, Y = 높이, Z = 깊이). 2026-11-09 사용자 요청("선반 아이템이 너무 작다")으로 키웠다.
        /// 칸 간격이 약 0.48m 라 <see cref="StackHeight"/>단을 쌓아도 윗칸에 닿지 않도록 높이는 0.10m 이하로 둔다.
        /// </summary>
        private static Vector3 SizeOf(ShopItem item) => item switch
        {
            ShopItem.IronDriver => new Vector3(0.13f, 0.08f, 0.32f),
            ShopItem.IronLighter => new Vector3(0.12f, 0.08f, 0.17f),
            ShopItem.CandleSet => new Vector3(0.17f, 0.10f, 0.26f),
            _ => new Vector3(0.16f, 0.10f, 0.16f),
        };

        private static Color ColorOf(ShopItem item) => item switch
        {
            ShopItem.IronDriver => new Color(0.55f, 0.58f, 0.62f),
            ShopItem.IronLighter => new Color(0.75f, 0.2f, 0.15f),
            ShopItem.CandleSet => new Color(1f, 0.85f, 0.4f),
            _ => Color.white,
        };

        /// <summary>면마다 정점을 따로 둔 상자(법선이 면에 수직). 원점이 상자 가운데다.</summary>
        private static Mesh BuildBoxMesh(Vector3 size)
        {
            Vector3 h = size * 0.5f;
            Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            var vertices = new Vector3[24];
            var meshNormals = new Vector3[24];
            var triangles = new int[36];
            for (int face = 0; face < 6; face++)
            {
                Vector3 n = normals[face];
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(n, u);
                for (int corner = 0; corner < 4; corner++)
                {
                    float su = corner is 0 or 3 ? -1f : 1f;
                    float sv = corner is 0 or 1 ? -1f : 1f;
                    Vector3 p = n + su * u + sv * v;
                    vertices[face * 4 + corner] = Vector3.Scale(p, h);
                    meshNormals[face * 4 + corner] = n;
                }
                int b = face * 4;
                // u × v = n 이라 0→1→2 는 바깥에서 반시계다. 유니티 앞면은 시계 방향이라 (0,2,1)(0,3,2) 로 감는다.
                triangles[face * 6 + 0] = b;
                triangles[face * 6 + 1] = b + 2;
                triangles[face * 6 + 2] = b + 1;
                triangles[face * 6 + 3] = b;
                triangles[face * 6 + 4] = b + 3;
                triangles[face * 6 + 5] = b + 2;
            }

            var mesh = new Mesh { vertices = vertices, normals = meshNormals, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>앵커가 없는 임시 드릴카 상자(ProtoTypeGame) — 상자 가운데에 판을 깔고 그 위를 선반 면으로 쓴다.</summary>
        private Transform BuildTemporaryShelf(DrillCarSafeZone zone)
        {
            var root = new GameObject("SharedItemShelf").transform;
            root.SetParent(zone.transform, false);
            root.localPosition = new Vector3(0.1f, -zone.Size.y * 0.5f + 0.75f, 0f);

            var boardSize = new Vector3(1.4f, 0.05f, 0.4f);
            GameObject board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "Board";
            board.transform.SetParent(root, false);
            board.transform.localPosition = new Vector3(0f, -boardSize.y * 0.5f, 0f);
            Mesh boardMesh = BuildBoxMesh(boardSize);
            _meshes[ShopItem.None] = boardMesh;
            board.GetComponent<MeshFilter>().sharedMesh = boardMesh;
            Destroy(board.GetComponent<Collider>());
            return root;
        }
    }
}
