using UnityEngine;
using UnityEngine.Rendering;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>
    /// 반출 구역을 모든 플레이어 눈에 보이게 꾸민다. 2026-10-04 에는 초록 빛기둥·반투명 채움이었는데 "너무 형광"이라는
    /// 요청(2026-11-03)으로 <b>바닥에 칠한 빨간 페인트 + 빛나는 외곽선</b>으로 다시 만들었다 — 가장자리 띠·틈·안쪽 칠에
    /// 롤러 자국과 벗겨진 곳이 있는 페인트 질감(<see cref="FurnitureDeliveryZonePaint"/>), 그 테두리를 따라 도는 밝은 빨강 선
    /// (가운데 가는 달아오른 심지) + 바닥으로 번지는 부드러운 빛무리, 구역 위의 은은한 붉은 조명이다. 외곽선만 숨 쉬듯 맥동한다.
    ///
    /// <para>로컬 표시 전용(복제 없음)이고 <see cref="FurnitureDeliveryZone"/> 이 만든다. 머티리얼 에셋 없이 항상 포함
    /// 셰이더(<c>Sprites/Default</c>)로 런타임 생성 — 벽에 가려지고 뒷면도 보이며 조명 영향을 받지 않아 어둠 속에서도 보인다.
    /// 칠은 어둡고 탁하게 그려 형광처럼 번쩍이지 않고, 밝은 것은 외곽선뿐이다.
    /// 모든 도형은 구역 오브젝트의 자식이라 드릴카·씬과 함께 사라지고 충돌체가 없다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FurnitureDeliveryZoneView : MonoBehaviour
    {
        private const string ShaderName = "Sprites/Default";

        // 색은 모두 sRGB 로 적는다(화면에 보이는 색). 정점 색은 선형 색공간에서 그대로 쓰이므로 ToVertexColor 로 바꿔 넣는다.
        private static readonly Color EdgeColor = new(1f, 0.16f, 0.1f, 1f);
        private static readonly Color CoreColor = new(1f, 0.5f, 0.38f, 1f);
        private static readonly Color HaloColor = new(1f, 0.1f, 0.06f, 1f);
        private static readonly Color LightColor = new(1f, 0.2f, 0.14f, 1f);

        // 바닥과 같은 높이에 그리면 깊이가 겹쳐 깜빡인다.
        private const float FloorLift = 0.03f;
        // 구역 바닥은 땅 아래로 뻗어 있어 땅을 못 찾으면 바닥에서 이만큼 위를 땅으로 본다(FurnitureDeliveryZone.BelowGround).
        private const float FallbackGroundAboveBottom = 0.5f;
        private const float EdgeLineWidth = 0.1f;
        private const float CoreLineWidth = 0.035f;
        // 빛무리: 외곽선에서 안쪽·바깥쪽으로 번지는 폭(m)과 외곽선 바로 옆의 불투명도.
        private const float HaloInward = 0.25f;
        private const float HaloOutward = 0.4f;
        private const float HaloEdgeAlpha = 0.4f;
        private const float PulsePeriodSeconds = 3f;
        private const float PulseMinimum = 0.7f;
        private const float GlowIntensity = 0.6f;
        private const float GlowHeight = 1f;
        private const float GlowRangeScale = 1.4f;

        // 투명 도형끼리 겹치는 순서(깊이 기록이 없어 정렬 순서로 결정된다): 칠 → 빛무리 → 외곽선 → 심지.
        private const int PaintOrder = 0;
        private const int HaloOrder = 1;
        private const int EdgeOrder = 2;
        private const int CoreOrder = 3;

        private FurnitureDeliveryZone _zone;
        private Material _paintMaterial;
        private Material _glowMaterial;
        private Texture2D _paintTexture;
        private Mesh _paintMesh;
        private Mesh _haloMesh;
        private Light _glow;
        private GameObject _root;

        private void Start()
        {
            // 그래픽 장치가 없는 환경(전용 서버·일부 배치 실행)에서는 그릴 것이 없다.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return;

            Rebuild();
        }

        private void Update()
        {
            if (_glowMaterial == null)
                return;

            // 0~1 → PulseMinimum~1. 머티리얼 색의 알파가 빛무리·외곽선 전체에 곱해진다. 칠은 맥동하지 않는다.
            float wave = Mathf.Sin(Time.time * (Mathf.PI * 2f / PulsePeriodSeconds)) * 0.5f + 0.5f;
            float pulse = Mathf.Lerp(PulseMinimum, 1f, wave);
            _glowMaterial.color = new Color(1f, 1f, 1f, pulse);
            if (_glow != null)
                _glow.intensity = GlowIntensity * pulse;
        }

        private void OnDestroy()
        {
            DestroyRuntimeObject(_paintMaterial);
            DestroyRuntimeObject(_glowMaterial);
            DestroyRuntimeObject(_paintTexture);
            DestroyRuntimeObject(_paintMesh);
            DestroyRuntimeObject(_haloMesh);
        }

        // 에디터 모드 테스트에서는 Destroy 를 부를 수 없다.
        private static void DestroyRuntimeObject(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        /// <summary>표시를 (다시) 만든다. 구역 크기·위치가 바뀌면 다시 부른다.</summary>
        internal void Rebuild()
        {
            _zone = GetComponent<FurnitureDeliveryZone>();
            Shader shader = Shader.Find(ShaderName);
            if (_zone == null || shader == null)
            {
                Debug.LogError($"{nameof(FurnitureDeliveryZoneView)}: 구역 컴포넌트 또는 {ShaderName} 셰이더가 없습니다.", this);
                enabled = false;
                return;
            }

            DestroyRuntimeObject(_root);
            if (_glowMaterial == null)
                _glowMaterial = new Material(shader) { name = "FurnitureDeliveryZoneGlowRuntime" };
            if (_paintMaterial == null)
                _paintMaterial = new Material(shader) { name = "FurnitureDeliveryZonePaintRuntime" };

            _root = new GameObject("DeliveryZoneView");
            _root.transform.SetParent(transform, false);

            Vector3 size = _zone.Size;
            float floorY = FindFloorLocalY(size);
            float halfX = size.x * 0.5f;
            float halfZ = size.z * 0.5f;
            float y = floorY + FloorLift;
            // 외곽선은 구역 안쪽에 선 폭 절반만큼 들여 그린다 — 선이 구역 밖으로 삐져나오지 않는다.
            float lineHalfX = Mathf.Max(0.1f, halfX - EdgeLineWidth * 0.5f);
            float lineHalfZ = Mathf.Max(0.1f, halfZ - EdgeLineWidth * 0.5f);

            BuildPaint(size, halfX, halfZ, y);
            BuildHalo(lineHalfX, lineHalfZ, y);
            BuildLoop("OuterEdge", lineHalfX, lineHalfZ, y, EdgeLineWidth, EdgeColor, EdgeOrder);
            BuildLoop("CoreEdge", lineHalfX, lineHalfZ, y, CoreLineWidth, CoreColor, CoreOrder);
            BuildGlow(floorY + GlowHeight, Mathf.Max(size.x, size.z) * GlowRangeScale);
        }

        /// <summary>구역 중심에서 아래로 땅을 찾아 구역 로컬 높이로 돌려준다. 못 찾으면 구역 바닥 + 0.5m.</summary>
        private float FindFloorLocalY(Vector3 size)
        {
            float bottom = -size.y * 0.5f;
            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, size.y,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return transform.InverseTransformPoint(hit.point).y;

            return bottom + FallbackGroundAboveBottom;
        }

        /// <summary>바닥에 칠한 페인트. 구역 전체 사각형에 페인트 텍스처 한 장을 입힌다.</summary>
        private void BuildPaint(Vector3 size, float halfX, float halfZ, float y)
        {
            DestroyRuntimeObject(_paintTexture);
            _paintTexture = FurnitureDeliveryZonePaint.CreateTexture(size.x, size.z);
            _paintMaterial.mainTexture = _paintTexture;

            if (_paintMesh == null)
                _paintMesh = new Mesh { name = "DeliveryZonePaint" };
            _paintMesh.Clear();
            _paintMesh.vertices = new[]
            {
                new Vector3(-halfX, y, -halfZ), new Vector3(halfX, y, -halfZ),
                new Vector3(halfX, y, halfZ), new Vector3(-halfX, y, halfZ),
            };
            _paintMesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            _paintMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            _paintMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _paintMesh.RecalculateBounds();
            AddMesh("Paint", _paintMesh, _paintMaterial, PaintOrder);
        }

        /// <summary>
        /// 외곽선 둘레로 번지는 빛무리. 안쪽 → 선 → 바깥쪽 세 겹의 사각 고리를 이어 선 위는 불투명하고
        /// 양쪽 끝은 투명하게 한다 — 바닥이 붉게 달아오른 것처럼 보인다.
        /// </summary>
        private void BuildHalo(float lineHalfX, float lineHalfZ, float y)
        {
            float[] offsets = { -HaloInward, 0f, HaloOutward };
            Color edge = HaloColor;
            edge.a = HaloEdgeAlpha;
            Color clear = HaloColor;
            clear.a = 0f;
            Color[] ringColors = { ToVertexColor(clear), ToVertexColor(edge), ToVertexColor(clear) };

            var vertices = new Vector3[offsets.Length * 4];
            var colors = new Color[vertices.Length];
            for (int ring = 0; ring < offsets.Length; ring++)
            {
                float hx = Mathf.Max(0.05f, lineHalfX + offsets[ring]);
                float hz = Mathf.Max(0.05f, lineHalfZ + offsets[ring]);
                int v = ring * 4;
                vertices[v + 0] = new Vector3(-hx, y, -hz);
                vertices[v + 1] = new Vector3(hx, y, -hz);
                vertices[v + 2] = new Vector3(hx, y, hz);
                vertices[v + 3] = new Vector3(-hx, y, hz);
                for (int k = 0; k < 4; k++)
                    colors[v + k] = ringColors[ring];
            }

            // 고리 사이마다 변 4개 × 사각형 1장(삼각형 2개).
            var triangles = new int[(offsets.Length - 1) * 4 * 6];
            int t = 0;
            for (int ring = 0; ring < offsets.Length - 1; ring++)
            {
                for (int k = 0; k < 4; k++)
                {
                    int a0 = ring * 4 + k;
                    int a1 = ring * 4 + (k + 1) % 4;
                    int b0 = (ring + 1) * 4 + k;
                    int b1 = (ring + 1) * 4 + (k + 1) % 4;
                    triangles[t++] = a0; triangles[t++] = b0; triangles[t++] = b1;
                    triangles[t++] = a0; triangles[t++] = b1; triangles[t++] = a1;
                }
            }

            if (_haloMesh == null)
                _haloMesh = new Mesh { name = "DeliveryZoneHalo" };
            _haloMesh.Clear();
            _haloMesh.vertices = vertices;
            _haloMesh.colors = colors;
            _haloMesh.triangles = triangles;
            _haloMesh.RecalculateBounds();
            AddMesh("Halo", _haloMesh, _glowMaterial, HaloOrder);
        }

        private void BuildLoop(string name, float halfX, float halfZ, float y, float width, Color color, int sortingOrder)
        {
            var lineObject = new GameObject(name);
            lineObject.transform.SetParent(_root.transform, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 4;
            line.SetPosition(0, new Vector3(-halfX, y, -halfZ));
            line.SetPosition(1, new Vector3(halfX, y, -halfZ));
            line.SetPosition(2, new Vector3(halfX, y, halfZ));
            line.SetPosition(3, new Vector3(-halfX, y, halfZ));
            line.widthMultiplier = width;
            line.startColor = color;
            line.endColor = color;
            line.sharedMaterial = _glowMaterial;
            line.textureMode = LineTextureMode.Stretch;
            line.numCornerVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = sortingOrder;
        }

        private void BuildGlow(float y, float range)
        {
            var glowObject = new GameObject("DeliveryGlow");
            glowObject.transform.SetParent(_root.transform, false);
            glowObject.transform.localPosition = new Vector3(0f, y, 0f);
            _glow = glowObject.AddComponent<Light>();
            _glow.type = LightType.Point;
            _glow.color = LightColor;
            _glow.intensity = GlowIntensity;
            _glow.range = range;
            _glow.shadows = LightShadows.None;
        }

        private void AddMesh(string name, Mesh mesh, Material material, int sortingOrder)
        {
            var meshObject = new GameObject(name);
            meshObject.transform.SetParent(_root.transform, false);
            meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = meshObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = sortingOrder;
        }

        // 메시 정점 색은 셰이더에 그대로 들어간다. 선형 색공간이면 화면에 보일 sRGB 색을 선형으로 바꿔 넣어야 의도한 색이 나온다.
        private static Color ToVertexColor(Color srgb)
        {
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? srgb.linear : srgb;
        }
    }
}
