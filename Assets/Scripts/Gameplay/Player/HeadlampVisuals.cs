using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 헤드라이트 겉모습 생성기 — 스포트 라이트 쿠키와 빛줄기 원뿔 메시. 둘 다 런타임 생성물이라 저장된 에셋이 없다.
    /// 구조는 docs/architecture/headlamp.md §4.1.
    /// </summary>
    public static class HeadlampVisuals
    {
        public const int CookieSize = 128;

        private const int BeamSegments = 32;
        private const float CookieRingWidth = 0.06f;
        private const float CookieEdgeStart = 0.9f;

        /// <summary>
        /// 쿠키 반지름 위치 <paramref name="radius"/>(0 = 원뿔 축, 1 = 바깥 각도)의 밝기. 핫스팟은 1, 그 밖은
        /// <paramref name="spill"/> 에서 가장자리로 갈수록 0, 핫스팟 테두리에 링을 더한다. 1 이상은 항상 0 —
        /// 정사각형 쿠키의 모서리가 비치지 않게 한다.
        /// </summary>
        public static float CookieProfile(float radius, float hotspot, float spill, float ring)
        {
            if (radius >= 1f)
                return 0f;

            float core = 1f - Smooth01((radius - hotspot * 0.55f) / (hotspot * 0.45f));
            float spillTerm = spill * (1f - Smooth01((radius - hotspot) / (1f - hotspot)));
            float ringOffset = (radius - hotspot) / CookieRingWidth;
            float ringTerm = ring * Mathf.Exp(-ringOffset * ringOffset);
            float edge = 1f - Smooth01((radius - CookieEdgeStart) / (1f - CookieEdgeStart));
            return Mathf.Clamp01((core + (1f - core) * spillTerm + ringTerm) * edge);
        }

        public static Texture2D CreateCookie(float hotspot, float spill, float ring)
        {
            var texture = new Texture2D(CookieSize, CookieSize, TextureFormat.RGBA32, false, true)
            {
                name = "HeadlampCookie",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[CookieSize * CookieSize];
            for (int y = 0; y < CookieSize; y++)
            {
                float v = (y + 0.5f) / CookieSize * 2f - 1f;
                for (int x = 0; x < CookieSize; x++)
                {
                    float u = (x + 0.5f) / CookieSize * 2f - 1f;
                    float value = CookieProfile(Mathf.Sqrt(u * u + v * v), hotspot, spill, ring);
                    byte b = (byte)Mathf.RoundToInt(value * 255f);
                    // URP 는 쿠키 형식에 따라 RGB 또는 알파를 읽는다 — 둘 다 같은 값으로 채운다.
                    pixels[y * CookieSize + x] = new Color32(b, b, b, b);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>
        /// 꼭짓점이 원점이고 +Z 로 길이 1 인 열린 원뿔. 셰이더가 정점을 빛줄기 길이만큼 늘리므로
        /// 컬링 경계는 최대 길이 <paramref name="maxLength"/> 기준으로 잡는다. uv.y 는 꼭짓점 0 → 끝 1.
        /// </summary>
        public static Mesh CreateBeamMesh(float angle, float maxLength)
        {
            float radius = Mathf.Tan(angle * 0.5f * Mathf.Deg2Rad);
            int ringCount = BeamSegments + 1;
            var vertices = new Vector3[ringCount * 2];
            var normals = new Vector3[ringCount * 2];
            var uvs = new Vector2[ringCount * 2];
            var triangles = new int[BeamSegments * 3];

            for (int i = 0; i < ringCount; i++)
            {
                float u = (float)i / BeamSegments;
                float theta = u * Mathf.PI * 2f;
                float cos = Mathf.Cos(theta);
                float sin = Mathf.Sin(theta);
                Vector3 normal = new Vector3(cos, sin, -radius).normalized;

                // 꼭짓점도 둘레마다 따로 둔다 — 법선이 방향마다 달라야 윤곽 흐림이 고르게 나온다.
                vertices[i * 2] = Vector3.zero;
                vertices[i * 2 + 1] = new Vector3(cos * radius, sin * radius, 1f);
                normals[i * 2] = normal;
                normals[i * 2 + 1] = normal;
                uvs[i * 2] = new Vector2(u, 0f);
                uvs[i * 2 + 1] = new Vector2(u, 1f);
            }

            for (int i = 0; i < BeamSegments; i++)
            {
                triangles[i * 3] = i * 2;
                triangles[i * 3 + 1] = i * 2 + 1;
                triangles[i * 3 + 2] = i * 2 + 3;
            }

            var mesh = new Mesh { name = "HeadlampBeam" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            float extent = radius * maxLength * 2f;
            mesh.bounds = new Bounds(new Vector3(0f, 0f, maxLength * 0.5f), new Vector3(extent, extent, maxLength));
            return mesh;
        }

        private static float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}
