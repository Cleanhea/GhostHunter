using System.Collections.Generic;
using GhostHunter.Gameplay.FurnitureDriver;
using UnityEngine;
using UnityEngine.Rendering;

namespace GhostHunter.UI
{
    /// <summary>
    /// 조립 영역 판정 트리거를 게임 화면에 그린다 — 바닥 반투명 채움 + 상자 윤곽선. 색은 서버가 복제한
    /// 판정 상태(기획서 §6.4 실루엣 색 — 부족·빈 영역 흰색, 조립 가능 초록, 혼입·초과 빨강)를 따른다.
    ///
    /// <para>로컬 표시 전용이다. 영역은 <see cref="FurnitureAssemblyZone.All"/>(스폰 등록부)에서 얻는다 —
    /// 전역 검색을 하지 않는다. 트리거는 지면 아래로도 뻗어 있지만 보이지 않으니 바닥에서 잘라 그린다.
    /// 머티리얼 에셋 없이 항상 포함 셰이더(<c>Sprites/Default</c>)로 만든다 — 벽에 가려지고 뒷면도 보인다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FurnitureAssemblyZoneView : MonoBehaviour
    {
        private const string ShaderName = "Sprites/Default";
        // 바닥과 같은 높이에 그리면 깊이가 겹쳐 깜빡인다.
        private const float FloorLift = 0.02f;

        [SerializeField] private FurnitureDriverUiSettings _uiSettings;

        private readonly Dictionary<FurnitureAssemblyZone, ZoneVisual> _visuals = new();
        private readonly List<FurnitureAssemblyZone> _stale = new();
        private Material _material;

        private sealed class ZoneVisual
        {
            public GameObject Root;
            public LineRenderer[] Lines;
            public Mesh FillMesh;
            public Bounds DrawnBounds;
            public float DrawnFloor = float.NaN;
            public FurnitureAssemblyState DrawnState = (FurnitureAssemblyState)(-1);
            public int SeenFrame;
        }

        private void Awake()
        {
            Shader shader = Shader.Find(ShaderName);
            if (_uiSettings == null || shader == null)
            {
                Debug.LogError(
                    $"{nameof(FurnitureAssemblyZoneView)}: FurnitureDriverUiSettings 미할당 또는 {ShaderName} 셰이더 없음.",
                    this);
                enabled = false;
                return;
            }

            _material = new Material(shader) { name = "FurnitureAssemblyZoneViewRuntime" };
        }

        private void LateUpdate()
        {
            int frame = Time.frameCount;
            IReadOnlyList<FurnitureAssemblyZone> zones = FurnitureAssemblyZone.All;
            for (int i = 0; i < zones.Count; i++)
            {
                FurnitureAssemblyZone zone = zones[i];
                if (zone == null || !zone.IsSpawned)
                    continue;

                if (!_visuals.TryGetValue(zone, out ZoneVisual visual) || visual.Root == null)
                {
                    if (visual != null)
                        DestroyVisual(visual);
                    visual = CreateVisual(zone);
                    _visuals[zone] = visual;
                }

                visual.SeenFrame = frame;
                Refresh(zone, visual);
            }

            RemoveStale(frame);
        }

        private void OnDisable()
        {
            foreach (ZoneVisual visual in _visuals.Values)
            {
                if (visual.Root != null)
                    visual.Root.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            foreach (ZoneVisual visual in _visuals.Values)
                DestroyVisual(visual);
            _visuals.Clear();
            if (_material != null)
                Destroy(_material);
        }

        private void Refresh(FurnitureAssemblyZone zone, ZoneVisual visual)
        {
            Bounds bounds = zone.TriggerBounds;
            float floor = zone.FloorHeight;
            float bottom = Mathf.Max(bounds.min.y, floor) + FloorLift;
            bool visible = _uiSettings.ShowAssemblyZone && bounds.max.y > bottom;
            if (visual.Root.activeSelf != visible)
                visual.Root.SetActive(visible);
            if (!visible)
                return;

            if (bounds != visual.DrawnBounds || !Mathf.Approximately(floor, visual.DrawnFloor))
            {
                visual.DrawnBounds = bounds;
                visual.DrawnFloor = floor;
                Rebuild(zone, visual, bounds, bottom);
                visual.DrawnState = (FurnitureAssemblyState)(-1);
            }

            FurnitureAssemblyState state = zone.Silhouette;
            if (state != visual.DrawnState)
            {
                visual.DrawnState = state;
                Recolor(visual, _uiSettings.ZoneColorFor(state));
            }
        }

        private ZoneVisual CreateVisual(FurnitureAssemblyZone zone)
        {
            // 영역 밑에 두어 Game 씬과 함께 사라지게 한다(활성 씬이 Bootstrap 이면 루트에 두면 남는다).
            var root = new GameObject("AssemblyZoneView");
            root.transform.SetParent(zone.transform, false);

            var visual = new ZoneVisual { Root = root, Lines = new LineRenderer[6] };
            for (int i = 0; i < visual.Lines.Length; i++)
            {
                var lineObject = new GameObject($"Edge{i}");
                lineObject.transform.SetParent(root.transform, false);
                LineRenderer line = lineObject.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.sharedMaterial = _material;
                line.widthMultiplier = _uiSettings.ZoneLineWidth;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.numCapVertices = 2;
                visual.Lines[i] = line;
            }

            var fillObject = new GameObject("Fill");
            fillObject.transform.SetParent(root.transform, false);
            visual.FillMesh = new Mesh { name = "AssemblyZoneFill" };
            fillObject.AddComponent<MeshFilter>().sharedMesh = visual.FillMesh;
            MeshRenderer fill = fillObject.AddComponent<MeshRenderer>();
            fill.sharedMaterial = _material;
            fill.shadowCastingMode = ShadowCastingMode.Off;
            fill.receiveShadows = false;
            return visual;
        }

        private void Rebuild(FurnitureAssemblyZone zone, ZoneVisual visual, Bounds bounds, float bottom)
        {
            Vector3 min = new(bounds.min.x, bottom, bounds.min.z);
            Vector3 max = new(bounds.max.x, bounds.max.y, bounds.max.z);
            Vector3[] floorCorners =
            {
                new(min.x, bottom, min.z), new(max.x, bottom, min.z),
                new(max.x, bottom, max.z), new(min.x, bottom, max.z),
            };

            // 바닥 사각형, 윗면 사각형(loop), 세로 모서리 4개.
            SetLoop(visual.Lines[0], floorCorners, bottom);
            SetLoop(visual.Lines[1], floorCorners, max.y);
            for (int i = 0; i < 4; i++)
            {
                LineRenderer vertical = visual.Lines[2 + i];
                vertical.loop = false;
                vertical.positionCount = 2;
                vertical.SetPosition(0, floorCorners[i]);
                vertical.SetPosition(1, new Vector3(floorCorners[i].x, max.y, floorCorners[i].z));
            }

            // 채움 메시는 영역 밑 자식이라 영역 기준 좌표로 둔다.
            var vertices = new Vector3[4];
            for (int i = 0; i < 4; i++)
                vertices[i] = zone.transform.InverseTransformPoint(floorCorners[i]);
            visual.FillMesh.Clear();
            visual.FillMesh.vertices = vertices;
            visual.FillMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            visual.FillMesh.RecalculateBounds();
        }

        private static void SetLoop(LineRenderer line, Vector3[] floorCorners, float height)
        {
            line.loop = true;
            line.positionCount = 4;
            for (int i = 0; i < 4; i++)
                line.SetPosition(i, new Vector3(floorCorners[i].x, height, floorCorners[i].z));
        }

        private void Recolor(ZoneVisual visual, Color color)
        {
            foreach (LineRenderer line in visual.Lines)
            {
                line.startColor = color;
                line.endColor = color;
            }

            Color fill = color;
            fill.a *= _uiSettings.ZoneFillAlphaScale;
            visual.FillMesh.colors = new[] { fill, fill, fill, fill };
        }

        private void RemoveStale(int frame)
        {
            _stale.Clear();
            foreach (KeyValuePair<FurnitureAssemblyZone, ZoneVisual> entry in _visuals)
            {
                if (entry.Value.SeenFrame != frame)
                    _stale.Add(entry.Key);
            }

            foreach (FurnitureAssemblyZone zone in _stale)
            {
                DestroyVisual(_visuals[zone]);
                _visuals.Remove(zone);
            }
        }

        private static void DestroyVisual(ZoneVisual visual)
        {
            if (visual.FillMesh != null)
                Destroy(visual.FillMesh);
            if (visual.Root != null)
                Destroy(visual.Root);
        }
    }
}
