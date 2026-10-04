using UnityEngine;
using UnityEngine.Rendering;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>
    /// 반출 구역을 모든 플레이어 눈에 보이게 꾸민다(2026-10-04 사용자 요청). 어두운 밤 씬에서도 멀리서 읽히도록
    /// <b>빛으로만</b> 그린다 — 바닥 반투명 채움 + 이중 윤곽선, 네 모서리의 위로 사라지는 빛기둥, 은은한 초록 조명,
    /// 천천히 맥동. 색은 UI 의 활성 초록(<c>#78C664</c>, MoleSkillUiSettings.CastingColor)과 같아 조립 영역 표시
    /// (<c>FurnitureAssemblyZoneView</c>)·스킬 아이콘과 한 계열이다.
    ///
    /// <para>로컬 표시 전용(복제 없음)이고 <see cref="FurnitureDeliveryZone"/> 이 만든다. 머티리얼 에셋 없이 항상 포함
    /// 셰이더(<c>Sprites/Default</c>)로 런타임 생성 — 벽에 가려지고 뒷면도 보이며 조명 영향을 받지 않아 어둠 속에서도 보인다.
    /// 모든 도형은 구역 오브젝트의 자식이라 드릴카·씬과 함께 사라지고 충돌체가 없다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FurnitureDeliveryZoneView : MonoBehaviour
    {
        private const string ShaderName = "Sprites/Default";

        // UI 활성 초록(120, 198, 100).
        private static readonly Color Green = new(0.47058824f, 0.7764706f, 0.39215687f, 1f);

        // 바닥과 같은 높이에 그리면 깊이가 겹쳐 깜빡인다.
        private const float FloorLift = 0.03f;
        // 구역 바닥은 땅 아래로 뻗어 있어 땅을 못 찾으면 바닥에서 이만큼 위를 땅으로 본다(FurnitureDeliveryZone.BelowGround).
        private const float FallbackGroundAboveBottom = 0.5f;
        private const float PillarHeight = 2.8f;
        private const float PillarWidth = 0.22f;
        private const float OuterLineWidth = 0.14f;
        private const float InnerLineWidth = 0.04f;
        private const float InnerInset = 0.4f;
        private const float FillAlpha = 0.3f;
        private const float PillarBottomAlpha = 0.75f;
        private const float InnerLineAlpha = 0.5f;
        private const float PulsePeriodSeconds = 3f;
        private const float PulseMinimum = 0.7f;
        private const float GlowIntensity = 1.4f;
        private const float GlowHeight = 1.2f;
        private const float GlowRangeScale = 1.4f;

        private FurnitureDeliveryZone _zone;
        private Material _material;
        private Mesh _fillMesh;
        private Mesh _pillarMesh;
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
            if (_material == null)
                return;

            // 0~1 → PulseMinimum~1. 머티리얼 색의 알파가 도형 전체에 곱해진다.
            float wave = Mathf.Sin(Time.time * (Mathf.PI * 2f / PulsePeriodSeconds)) * 0.5f + 0.5f;
            float pulse = Mathf.Lerp(PulseMinimum, 1f, wave);
            _material.color = new Color(1f, 1f, 1f, pulse);
            if (_glow != null)
                _glow.intensity = GlowIntensity * pulse;
        }

        private void OnDestroy()
        {
            DestroyObject(_material);
            DestroyObject(_fillMesh);
            DestroyObject(_pillarMesh);
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

            DestroyObject(_root);
            if (_material == null)
                _material = new Material(shader) { name = "FurnitureDeliveryZoneViewRuntime" };

            _root = new GameObject("DeliveryZoneView");
            _root.transform.SetParent(transform, false);

            Vector3 size = _zone.Size;
            float floorY = FindFloorLocalY(size);
            float halfX = size.x * 0.5f;
            float halfZ = size.z * 0.5f;
            float y = floorY + FloorLift;
            float height = Mathf.Min(PillarHeight, size.y * 0.5f - floorY);

            BuildFill(halfX, halfZ, y);
            BuildLoop("OuterEdge", halfX, halfZ, y, OuterLineWidth, Green);
            Color inner = Green;
            inner.a = InnerLineAlpha;
            BuildLoop("InnerEdge", Mathf.Max(0.1f, halfX - InnerInset), Mathf.Max(0.1f, halfZ - InnerInset),
                y, InnerLineWidth, inner);
            BuildPillars(halfX, halfZ, y, Mathf.Max(0.5f, height));
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

        private void BuildFill(float halfX, float halfZ, float y)
        {
            var vertices = new[]
            {
                new Vector3(-halfX, y, -halfZ), new Vector3(halfX, y, -halfZ),
                new Vector3(halfX, y, halfZ), new Vector3(-halfX, y, halfZ),
            };
            Color fill = Green;
            fill.a = FillAlpha;

            if (_fillMesh == null)
                _fillMesh = new Mesh { name = "DeliveryZoneFill" };
            _fillMesh.Clear();
            _fillMesh.vertices = vertices;
            _fillMesh.colors = new[] { fill, fill, fill, fill };
            _fillMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _fillMesh.RecalculateBounds();
            AddMesh("Fill", _fillMesh);
        }

        /// <summary>
        /// 네 모서리에 십자(서로 직각인 두 장) 빛기둥. 바닥에서 불투명하게 시작해 위로 갈수록 투명해진다 —
        /// 멀리서도 구역 위치가 읽히고, 어느 각도에서 봐도 얇아지지 않는다.
        /// </summary>
        private void BuildPillars(float halfX, float halfZ, float y, float height)
        {
            var corners = new[]
            {
                new Vector2(-halfX, -halfZ), new Vector2(halfX, -halfZ),
                new Vector2(halfX, halfZ), new Vector2(-halfX, halfZ),
            };
            Color bottom = Green;
            bottom.a = PillarBottomAlpha;
            Color top = Green;
            top.a = 0f;

            var vertices = new Vector3[corners.Length * 8];
            var colors = new Color[vertices.Length];
            var triangles = new int[corners.Length * 12];
            float w = PillarWidth * 0.5f;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 c = corners[i];
                int v = i * 8;
                // x 방향 판, z 방향 판.
                vertices[v + 0] = new Vector3(c.x - w, y, c.y);
                vertices[v + 1] = new Vector3(c.x + w, y, c.y);
                vertices[v + 2] = new Vector3(c.x + w, y + height, c.y);
                vertices[v + 3] = new Vector3(c.x - w, y + height, c.y);
                vertices[v + 4] = new Vector3(c.x, y, c.y - w);
                vertices[v + 5] = new Vector3(c.x, y, c.y + w);
                vertices[v + 6] = new Vector3(c.x, y + height, c.y + w);
                vertices[v + 7] = new Vector3(c.x, y + height, c.y - w);
                for (int k = 0; k < 8; k += 4)
                {
                    colors[v + k + 0] = bottom;
                    colors[v + k + 1] = bottom;
                    colors[v + k + 2] = top;
                    colors[v + k + 3] = top;
                }

                int t = i * 12;
                triangles[t + 0] = v + 0; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 0; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
                triangles[t + 6] = v + 4; triangles[t + 7] = v + 6; triangles[t + 8] = v + 5;
                triangles[t + 9] = v + 4; triangles[t + 10] = v + 7; triangles[t + 11] = v + 6;
            }

            if (_pillarMesh == null)
                _pillarMesh = new Mesh { name = "DeliveryZonePillars" };
            _pillarMesh.Clear();
            _pillarMesh.vertices = vertices;
            _pillarMesh.colors = colors;
            _pillarMesh.triangles = triangles;
            _pillarMesh.RecalculateBounds();
            AddMesh("Pillars", _pillarMesh);
        }

        private void BuildLoop(string name, float halfX, float halfZ, float y, float width, Color color)
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
            line.sharedMaterial = _material;
            line.textureMode = LineTextureMode.Stretch;
            line.numCornerVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
        }

        private void BuildGlow(float y, float range)
        {
            var glowObject = new GameObject("DeliveryGlow");
            glowObject.transform.SetParent(_root.transform, false);
            glowObject.transform.localPosition = new Vector3(0f, y, 0f);
            _glow = glowObject.AddComponent<Light>();
            _glow.type = LightType.Point;
            _glow.color = Green;
            _glow.intensity = GlowIntensity;
            _glow.range = range;
            _glow.shadows = LightShadows.None;
        }

        private void AddMesh(string name, Mesh mesh)
        {
            var meshObject = new GameObject(name);
            meshObject.transform.SetParent(_root.transform, false);
            meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = meshObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }
}
