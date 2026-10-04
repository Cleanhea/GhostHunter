using UnityEngine;

namespace GhostHunter.Gameplay.Revival
{
    /// <summary>
    /// 바닥 마법진 텍스처를 런타임에 그린다 — 이중 바깥 원, 룬 띠, 오망성, 꼭짓점 작은 원, 안쪽 원, 가운데 원.
    /// 오망성 꼭짓점 각도는 촛대 자리(<see cref="RevivalRitual.SlotPosition"/>)와 같고, 마법진 크기는 꼭짓점이 촛대 위에 오도록
    /// 촛대 원 반경 / <see cref="StarRadius"/> 로 정한다: 텍스처 u = 월드 +X, v = 월드 +Z.
    /// 아트 에셋이 들어오면 교체한다. 한 번 만들어 모든 피어·스테이지에서 같이 쓴다.
    /// </summary>
    public static class RevivalCircleTexture
    {
        public const int Resolution = 768;

        private static Texture2D _texture;
        private static Sprite _sprite;

        /// <summary>반경 1 = 텍스처 가장자리. 꼭짓점이 놓이는 반경.</summary>
        public const float StarRadius = 0.86f;

        public static Sprite Sprite
        {
            get
            {
                if (_sprite == null)
                {
                    _texture = Build(Resolution);
                    _sprite = UnityEngine.Sprite.Create(_texture, new Rect(0, 0, Resolution, Resolution),
                        new Vector2(0.5f, 0.5f), Resolution * 0.5f);
                    _sprite.name = "RevivalCircle";
                }
                return _sprite;
            }
        }

        /// <summary>꼭짓점 i 의 각도(라디안) — 촛대 자리와 같은 식.</summary>
        public static float VertexAngle(int index) => index * Mathf.PI * 2f / RevivalRules.CandleCount;

        /// <param name="keepReadable">검사용 — 픽셀을 다시 읽을 수 있게 CPU 사본을 남긴다. 게임에서는 버린다.</param>
        internal static Texture2D Build(int resolution, bool keepReadable = false)
        {
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, true)
            {
                name = "RevivalCircle",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
            };

            var vertices = new Vector2[RevivalRules.CandleCount];
            for (int i = 0; i < vertices.Length; i++)
            {
                float angle = VertexAngle(i);
                vertices[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * StarRadius;
            }

            float pixel = 2f / resolution;
            var pixels = new Color32[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    var p = new Vector2((x + 0.5f) * pixel - 1f, (y + 0.5f) * pixel - 1f);
                    float line = Coverage(p, vertices, pixel);
                    float glow = Glow(p, vertices);
                    float alpha = Mathf.Clamp01(Mathf.Max(line, glow * 0.35f));
                    // 선 가운데는 밝은 주황빛 빨강, 번짐은 짙은 빨강.
                    Color color = Color.Lerp(new Color(0.55f, 0.02f, 0.02f), new Color(1f, 0.25f, 0.12f), line);
                    color.a = alpha;
                    pixels[y * resolution + x] = color;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(true, !keepReadable);
            return texture;
        }

        /// <summary>선 모양의 불투명도(0~1). 안티에일리어싱은 픽셀 크기로 가장자리를 부드럽게 한다.</summary>
        private static float Coverage(Vector2 p, Vector2[] vertices, float pixel)
        {
            float r = p.magnitude;
            float angle = Mathf.Atan2(p.y, p.x);
            float distance = float.MaxValue;

            // 바깥 이중 원과 안쪽·가운데 원
            distance = Mathf.Min(distance, Mathf.Abs(r - 0.97f) - 0.008f);
            distance = Mathf.Min(distance, Mathf.Abs(r - 0.90f) - 0.006f);
            distance = Mathf.Min(distance, Mathf.Abs(r - 0.56f) - 0.006f);
            distance = Mathf.Min(distance, Mathf.Abs(r - 0.20f) - 0.005f);

            // 오망성 — 꼭짓점 i 와 i+2 를 잇는다.
            for (int i = 0; i < vertices.Length; i++)
                distance = Mathf.Min(distance,
                    SegmentDistance(p, vertices[i], vertices[(i + 2) % vertices.Length]) - 0.006f);

            // 꼭짓점 작은 원
            foreach (Vector2 vertex in vertices)
                distance = Mathf.Min(distance, Mathf.Abs((p - vertex).magnitude - 0.10f) - 0.006f);

            // 바깥 두 원 사이 룬 띠 — 짧은 방사선과 작은 호를 번갈아 새긴다(꼭짓점 원 근처는 비운다).
            if (r > 0.905f && r < 0.965f && !NearVertex(p, vertices, 0.13f))
            {
                float step = Mathf.PI * 2f / 60f;
                float local = Mathf.Repeat(angle, step) - step * 0.5f;
                int glyph = Mathf.FloorToInt(Mathf.Repeat(angle, Mathf.PI * 2f) / step);
                float arc = r * Mathf.Abs(local);
                float mark = glyph % 3 == 0
                    ? Mathf.Abs(r - 0.935f) - 0.003f + Mathf.Max(0f, arc - 0.012f)
                    : arc - 0.003f + Mathf.Max(0f, Mathf.Abs(r - 0.935f) - (glyph % 3 == 1 ? 0.02f : 0.01f));
                distance = Mathf.Min(distance, mark);
            }

            // 안쪽 원과 오망성 사이 점 고리
            if (r > 0.6f && r < 0.66f)
            {
                float step = Mathf.PI * 2f / 30f;
                float local = (Mathf.Repeat(angle, step) - step * 0.5f) * r;
                distance = Mathf.Min(distance, new Vector2(local, r - 0.63f).magnitude - 0.008f);
            }

            return Mathf.Clamp01(0.5f - distance / pixel);
        }

        /// <summary>선 둘레의 은은한 번짐.</summary>
        private static float Glow(Vector2 p, Vector2[] vertices)
        {
            float r = p.magnitude;
            float distance = Mathf.Min(Mathf.Abs(r - 0.97f), Mathf.Abs(r - 0.90f));
            for (int i = 0; i < vertices.Length; i++)
                distance = Mathf.Min(distance, SegmentDistance(p, vertices[i], vertices[(i + 2) % vertices.Length]));
            return r > 1f ? 0f : Mathf.Exp(-distance * 40f);
        }

        private static bool NearVertex(Vector2 p, Vector2[] vertices, float radius)
        {
            foreach (Vector2 vertex in vertices)
                if ((p - vertex).sqrMagnitude < radius * radius)
                    return true;
            return false;
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }
    }
}
