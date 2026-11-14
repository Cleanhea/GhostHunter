using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 피로 쓴 명령 글씨 메시(사용자 요청 2026-11-15 "피로 쓰이는 느낌"). <see cref="InkerStrokeFont"/> 의 획을 손가락 한 번에 긋듯
    /// 진행도만큼 그리고, 획 시작에 피가 고인 덩어리·끝으로 갈수록 가늘어지는 획·쓴 자리에서 흘러내리는 피·튄 방울을 만든다.
    /// 글자마다 기울기·크기·기준선이 조금씩 흔들린다. 같은 씨앗이면 모든 피어에서 같은 모양이다.
    /// 좌표는 글씨 면 로컬(m) — 글씨 면의 +Z 가 읽는 사람 쪽이고, 읽는 사람의 오른쪽은 면의 −X 다.
    /// 메시 데이터 채널: 정점색 = 피 색, UV0 = (획 따라 거리, 획 가로 0~1), UV1 = (젖음, 종류 0 획·1 흘러내림·2 방울).
    /// </summary>
    public sealed class InkerBloodMesh
    {
        private const float StrokeWidth = 0.14f;
        private const float ResampleStep = 0.04f;
        private const float SharpCornerDegrees = 65f;
        private const int DiscSegments = 10;
        private const int MaxDripsPerLetter = 3;

        /// <summary>UV1.y 종류 값: 획 끝을 둥글게 막는 원판(셰이더가 광택 줄 대신 가운데 반짝임을 쓴다).</summary>
        private const float CapKind = 0.3f;

        private sealed class Ribbon
        {
            public Vector2[] Points;
            public float[] Distance;  // 획 처음부터 누적(글자 높이 단위)
            public float[] Width;
            public float[] Wet;       // 1 = 손가락에 피가 듬뿍, 0 쪽으로 갈수록 말라서 끊긴다
            public float StrokeStart; // 전체 쓰기 순서상 시작 거리
            public float Length;
            public Color32 Color;
            public float UvOffset;
        }

        private sealed class Drip
        {
            public Vector2 Origin;
            public float RevealAt;    // 전체 쓰기 순서상 거리
            public float Delay;       // 그 자리가 쓰인 뒤 흐르기 시작할 때까지(초)
            public float MaxLength;
            public float Tau;         // 길이가 지수적으로 다가가는 시간 상수(초)
            public float Width;
            public float WobblePhase;
            public Color32 Color;
        }

        private struct Splat
        {
            public Vector2 Center;
            public float Radius;
            public float Stretch;
            public float Angle;
            public float RevealAt;
            public Color32 Color;
        }

        private readonly List<Ribbon> _ribbons = new(96);
        private readonly List<Drip> _drips = new(48);
        private readonly List<Splat> _splats = new(32);
        private readonly float _totalLength;
        private float _settleSeconds;

        /// <summary>글씨 폭·높이(글자 높이 단위, 흘러내림 제외).</summary>
        public float Width { get; }
        public float Height { get; }
        public int LineCount { get; }

        public InkerBloodMesh(string text, float wrapWidth, int seed)
        {
            InkerTextLayout layout = InkerStrokeFont.Layout(text, wrapWidth);
            Width = layout.Width;
            Height = layout.Height;
            LineCount = layout.LineCount;

            var random = new System.Random(seed);
            float cursor = 0f;
            int letterKey = -1;
            float letterAngle = 0f, letterScale = 1f, letterLift = 0f, letterShift = 0f;
            Vector2 letterPivot = Vector2.zero;
            var lineTilt = new float[Mathf.Max(1, layout.LineCount)];
            for (int i = 0; i < lineTilt.Length; i++)
                lineTilt[i] = Range(random, -2.5f, 2.5f) * Mathf.Deg2Rad;

            var dripsPerLetter = new Dictionary<int, int>();
            foreach (InkerTextLayout.Stroke stroke in layout.Strokes)
            {
                int key = stroke.Line * 1000 + stroke.Letter;
                if (key != letterKey)
                {
                    letterKey = key;
                    letterAngle = Range(random, -8f, 8f) * Mathf.Deg2Rad;
                    letterScale = Range(random, 0.93f, 1.08f);
                    letterLift = Range(random, -0.06f, 0.06f);
                    letterShift = Range(random, -0.03f, 0.03f);
                    letterPivot = stroke.Points[0];
                }

                // 손글씨 흔들림: 글자 단위 회전·크기·기준선 + 줄 기울기 + 점마다 부드러운 떨림.
                var shaped = new Vector2[stroke.Points.Length];
                for (int p = 0; p < stroke.Points.Length; p++)
                {
                    Vector2 local = (stroke.Points[p] - letterPivot) * letterScale;
                    local = Rotate(local, letterAngle) + letterPivot + new Vector2(letterShift, letterLift);
                    float tilt = lineTilt[Mathf.Clamp(stroke.Line, 0, lineTilt.Length - 1)];
                    local.y += local.x * Mathf.Tan(tilt);
                    shaped[p] = local;
                }

                List<Vector2> points = Resample(shaped, seed + key);
                if (points.Count < 2)
                    continue;

                float strokeLength = 0f;
                for (int p = 1; p < points.Count; p++)
                    strokeLength += Vector2.Distance(points[p - 1], points[p]);

                byte tone = (byte)Mathf.Clamp(Mathf.RoundToInt(Range(random, 88f, 120f)), 0, 255);
                var color = new Color32(tone, (byte)(tone * 0.03f), (byte)(tone * 0.045f), 255);
                float pressure = Range(random, 0.85f, 1.15f);

                // 날카로운 모서리(M·N·A 꼭짓점 등)에서 리본을 끊는다 — 법선을 평균하면 뾰족하게 튀어나온다.
                int segmentStart = 0;
                float distanceSoFar = 0f;
                var distances = new float[points.Count];
                for (int p = 1; p < points.Count; p++)
                {
                    distanceSoFar += Vector2.Distance(points[p - 1], points[p]);
                    distances[p] = distanceSoFar;
                }

                for (int p = 1; p < points.Count; p++)
                {
                    bool last = p == points.Count - 1;
                    bool corner = !last && TurnDegrees(points[p - 1], points[p], points[p + 1]) > SharpCornerDegrees;
                    if (!corner && !last)
                        continue;

                    AddRibbon(points, distances, segmentStart, p, cursor, strokeLength, pressure, color, random);
                    segmentStart = p;
                }

                // 흘러내림: 획의 낮은 지점·시작·끝에서 잘 생긴다. 글자당 몇 개로 제한한다.
                dripsPerLetter.TryGetValue(key, out int dripCount);
                for (int p = 0; p < points.Count && dripCount < MaxDripsPerLetter; p++)
                {
                    bool start = p == 0;
                    bool end = p == points.Count - 1;
                    bool low = p > 0 && p < points.Count - 1
                        && points[p].y <= points[p - 1].y && points[p].y <= points[p + 1].y;
                    double chance = start ? 0.32 : end ? 0.22 : low ? 0.45 : 0.012;
                    if (random.NextDouble() >= chance)
                        continue;

                    float r = (float)random.NextDouble();
                    _drips.Add(new Drip
                    {
                        Origin = points[p] + Vector2.down * (StrokeWidth * 0.3f),
                        RevealAt = cursor + distances[p],
                        Delay = Range(random, 0.15f, 0.9f),
                        MaxLength = 0.12f + 1.05f * Mathf.Pow(r, 2.3f),
                        Tau = Range(random, 0.8f, 2.4f),
                        Width = StrokeWidth * Range(random, 0.38f, 0.62f) * pressure,
                        WobblePhase = Range(random, 0f, 6.28f),
                        Color = new Color32((byte)Mathf.Min(255, tone + 18), (byte)(tone * 0.04f), (byte)(tone * 0.05f), 255),
                    });
                    dripCount++;
                }

                dripsPerLetter[key] = dripCount;

                // 튄 방울: 획 근처에 몇 개.
                int splats = random.NextDouble() < 0.55 ? random.Next(1, 3) : 0;
                for (int s = 0; s < splats; s++)
                {
                    int at = random.Next(0, points.Count);
                    float angle = Range(random, 0f, Mathf.PI * 2f);
                    float distance = Range(random, 0.1f, 0.34f);
                    _splats.Add(new Splat
                    {
                        Center = points[at] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance,
                        Radius = Range(random, 0.012f, 0.04f),
                        Stretch = Range(random, 1f, 1.9f),
                        Angle = angle,
                        RevealAt = cursor + distances[at],
                        Color = new Color32((byte)(tone * 0.85f), 2, 3, 255),
                    });
                }

                cursor += strokeLength;
            }

            _totalLength = Mathf.Max(0.001f, cursor);

            // 흘러내림이 멈추는 데 걸리는 최대 시간(지수 접근의 99%) — 그 뒤로는 메시를 다시 지을 필요가 없다.
            foreach (Drip drip in _drips)
                _settleSeconds = Mathf.Max(_settleSeconds, drip.Delay + drip.Tau * 4.6f);
        }

        public int DripCount => _drips.Count;

        /// <summary>쓰기 진행도(0~1)에 해당하는 손끝 위치(글자 높이 단위, 레이아웃 좌표). 획이 없으면 false.</summary>
        public bool TryGetTip(float progress, out Vector2 tip)
        {
            tip = Vector2.zero;
            if (_ribbons.Count == 0)
                return false;

            float reveal = Mathf.Clamp01(progress) * _totalLength;
            foreach (Ribbon ribbon in _ribbons)
            {
                float local = reveal - ribbon.StrokeStart;
                if (local > ribbon.Distance[ribbon.Distance.Length - 1] + 1e-4f)
                    continue;
                tip = PointAt(ribbon, Mathf.Max(0f, local));
                return true;
            }

            Ribbon lastRibbon = _ribbons[_ribbons.Count - 1];
            tip = lastRibbon.Points[lastRibbon.Points.Length - 1];
            return true;
        }

        /// <summary>
        /// 다 쓰고 흘러내림도 다 멈췄는가 — 그 뒤로는 메시를 다시 지을 필요가 없다.
        /// <paramref name="secondsSinceStart"/> 는 쓰기 시작부터 지난 시간, <paramref name="revealSeconds"/> 는 쓰는 데 걸리는 시간.
        /// </summary>
        public bool IsSettled(float progress, float secondsSinceStart, float revealSeconds)
            => progress >= 1f && secondsSinceStart >= revealSeconds + _settleSeconds;

        /// <summary>
        /// 메시 데이터를 채운다. <paramref name="letterHeight"/> 는 글자 높이(m). 리스트는 비우고 채운다.
        /// </summary>
        public void Build(float progress, float secondsSinceStart, float revealSeconds, float letterHeight,
            List<Vector3> vertices, List<Color32> colors, List<Vector2> uv0, List<Vector2> uv1, List<int> triangles)
        {
            vertices.Clear();
            colors.Clear();
            uv0.Clear();
            uv1.Clear();
            triangles.Clear();

            float reveal = Mathf.Clamp01(progress) * _totalLength;
            float secondsPerUnit = Mathf.Max(0.01f, revealSeconds) / _totalLength;
            var writer = new Writer(vertices, colors, uv0, uv1, triangles, letterHeight);

            // 그리는 순서 = 겹침 순서. 흘러내림 → 획 → 방울(투명 블렌드라 겹친 곳은 더 진해진다).
            foreach (Drip drip in _drips)
            {
                if (drip.RevealAt > reveal)
                    continue;
                float age = secondsSinceStart - drip.RevealAt * secondsPerUnit - drip.Delay;
                if (progress >= 1f && drip.RevealAt * secondsPerUnit > secondsSinceStart)
                    age = secondsSinceStart - drip.Delay; // 다가오면 즉시 완성 — 늦게 쓰인 자리도 지금부터 흐른다.
                if (age <= 0f)
                    continue;
                float length = drip.MaxLength * (1f - Mathf.Exp(-age / drip.Tau));
                if (length > 0.01f)
                    writer.WriteDrip(drip.Origin, length, drip.Width, drip.WobblePhase, drip.Color);
            }

            for (int i = 0; i < _ribbons.Count; i++)
            {
                Ribbon ribbon = _ribbons[i];
                float visible = reveal - ribbon.StrokeStart;
                if (visible <= 0f)
                    break;
                bool partial = visible < ribbon.Length - 1e-4f;
                writer.WriteRibbon(ribbon, Mathf.Min(visible, ribbon.Length));

                // 리본 시작(획 시작·꺾인 모서리)을 둥글게 막는다 — 획 폭과 같은 크기라 따로 튀어 보이지 않는다.
                // 획 시작은 폭 자체가 넓어(고인 피) 덩어리처럼 보인다.
                writer.WriteDisc(ribbon.Points[0], ribbon.Width[0] * 0.5f, 1f, 0f, ribbon.Color, ribbon.UvOffset,
                    CapKind, ribbon.Wet[0]);
                if (partial)
                {
                    // 지금 쓰고 있는 손끝 — 젖은 방울.
                    Vector2 tip = PointAt(ribbon, visible);
                    writer.WriteDisc(tip, StrokeWidth * 0.48f, 1f, 0f, ribbon.Color, ribbon.UvOffset + visible, CapKind);
                    break;
                }
            }

            foreach (Splat splat in _splats)
            {
                if (splat.RevealAt <= reveal)
                    writer.WriteDisc(splat.Center, splat.Radius, splat.Stretch, splat.Angle, splat.Color,
                        splat.Center.x * 7f, 2f);
            }
        }

        // ───────────────────────── 만들기 ─────────────────────────

        private void AddRibbon(List<Vector2> points, float[] distances, int from, int to, float strokeStart,
            float strokeLength, float pressure, Color32 color, System.Random random)
        {
            int count = to - from + 1;
            if (count < 2)
                return;

            var ribbon = new Ribbon
            {
                Points = new Vector2[count],
                Distance = new float[count],
                Width = new float[count],
                Wet = new float[count],
                StrokeStart = strokeStart + distances[from],
                Color = color,
                UvOffset = Range(random, 0f, 50f),
            };

            float noisePhase = Range(random, 0f, 100f);
            for (int i = 0; i < count; i++)
            {
                int p = from + i;
                float s = distances[p];
                ribbon.Points[i] = points[p];
                ribbon.Distance[i] = s - distances[from];
                // 획 폭: 시작에 고인 피 → 손가락 압력 떨림 → 끝으로 가늘어짐(피가 묻어 나오다 마르는 느낌).
                float pool = 1f + 0.4f * Mathf.Exp(-s / 0.06f);
                float taper = Mathf.Lerp(1f, 0.5f, Mathf.SmoothStep(0f, 1f, (s - (strokeLength - 0.16f)) / 0.16f));
                float wobble = 1f + 0.2f * (Mathf.PerlinNoise(noisePhase + s * 5.5f, 0.37f) - 0.5f) * 2f;
                ribbon.Width[i] = StrokeWidth * pressure * pool * taper * wobble;
                // 획을 그을수록 손가락의 피가 줄어 끝은 말라 갈라진다(긴 획일수록 더).
                float used = Mathf.Clamp01(s / Mathf.Max(0.35f, strokeLength));
                ribbon.Wet[i] = Mathf.Lerp(1f, 0.25f, used * used);
            }

            ribbon.Length = ribbon.Distance[count - 1];
            _ribbons.Add(ribbon);
        }

        private List<Vector2> Resample(Vector2[] source, int seed)
        {
            var points = new List<Vector2>(32);
            if (source.Length == 0)
                return points;

            float phase = (seed & 1023) * 0.731f;
            float travelled = 0f;
            points.Add(Jitter(source[0], 0f, phase));
            for (int i = 1; i < source.Length; i++)
            {
                Vector2 a = source[i - 1];
                Vector2 b = source[i];
                float length = Vector2.Distance(a, b);
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / ResampleStep));
                for (int s = 1; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    float d = travelled + length * t;
                    points.Add(Jitter(Vector2.Lerp(a, b, t), d, phase));
                }

                travelled += length;
            }

            return points;
        }

        private static Vector2 Jitter(Vector2 point, float distance, float phase)
        {
            float x = (Mathf.PerlinNoise(phase + distance * 3.1f, 1.7f) - 0.5f) * 0.06f;
            float y = (Mathf.PerlinNoise(phase + distance * 3.1f, 8.3f) - 0.5f) * 0.06f;
            return point + new Vector2(x, y);
        }

        private static float TurnDegrees(Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 ab = b - a;
            Vector2 bc = c - b;
            if (ab.sqrMagnitude < 1e-8f || bc.sqrMagnitude < 1e-8f)
                return 0f;
            return Vector2.Angle(ab, bc);
        }

        private static Vector2 PointAt(Ribbon ribbon, float distance)
        {
            for (int i = 1; i < ribbon.Points.Length; i++)
            {
                if (ribbon.Distance[i] < distance)
                    continue;
                float span = ribbon.Distance[i] - ribbon.Distance[i - 1];
                float t = span > 1e-6f ? (distance - ribbon.Distance[i - 1]) / span : 1f;
                return Vector2.Lerp(ribbon.Points[i - 1], ribbon.Points[i], t);
            }

            return ribbon.Points[ribbon.Points.Length - 1];
        }

        private static Vector2 Rotate(Vector2 v, float radians)
        {
            float c = Mathf.Cos(radians);
            float s = Mathf.Sin(radians);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        private static float Range(System.Random random, float min, float max)
            => min + (float)random.NextDouble() * (max - min);

        // ───────────────────────── 메시 쓰기 ─────────────────────────

        private readonly struct Writer
        {
            private readonly List<Vector3> _vertices;
            private readonly List<Color32> _colors;
            private readonly List<Vector2> _uv0;
            private readonly List<Vector2> _uv1;
            private readonly List<int> _triangles;
            private readonly float _scale;

            public Writer(List<Vector3> vertices, List<Color32> colors, List<Vector2> uv0, List<Vector2> uv1,
                List<int> triangles, float scale)
            {
                _vertices = vertices;
                _colors = colors;
                _uv0 = uv0;
                _uv1 = uv1;
                _triangles = triangles;
                _scale = scale;
            }

            /// <summary>레이아웃 좌표(+x = 읽는 사람의 오른쪽) → 글씨 면 로컬(읽는 사람의 오른쪽 = −X).</summary>
            private Vector3 Face(Vector2 p) => new(-p.x * _scale, p.y * _scale, 0f);

            private void Add(Vector2 p, Color32 color, Vector2 uv, float wet, float kind)
            {
                _vertices.Add(Face(p));
                _colors.Add(color);
                _uv0.Add(uv);
                _uv1.Add(new Vector2(wet, kind));
            }

            public void WriteRibbon(Ribbon ribbon, float visible)
            {
                int start = _vertices.Count;
                int pairs = 0;
                for (int i = 0; i < ribbon.Points.Length; i++)
                {
                    float d = ribbon.Distance[i];
                    Vector2 point = ribbon.Points[i];
                    float width = ribbon.Width[i];
                    bool cut = d > visible;
                    if (cut)
                    {
                        point = PointAt(ribbon, visible);
                        float span = ribbon.Distance[i] - ribbon.Distance[i - 1];
                        float t = span > 1e-6f ? (visible - ribbon.Distance[i - 1]) / span : 1f;
                        width = Mathf.Lerp(ribbon.Width[i - 1], ribbon.Width[i], t);
                        d = visible;
                    }

                    Vector2 tangent = Tangent(ribbon.Points, i, cut ? point : ribbon.Points[i]);
                    Vector2 normal = new(-tangent.y, tangent.x);
                    float u = ribbon.UvOffset + d;
                    float wet = cut ? 1f : ribbon.Wet[i]; // 지금 쓰는 손끝은 젖어 있다
                    Add(point + normal * (width * 0.5f), ribbon.Color, new Vector2(u, 1f), wet, 0f);
                    Add(point - normal * (width * 0.5f), ribbon.Color, new Vector2(u, 0f), wet, 0f);
                    pairs++;
                    if (cut)
                        break;
                }

                for (int i = 0; i < pairs - 1; i++)
                {
                    int a = start + i * 2;
                    _triangles.Add(a);
                    _triangles.Add(a + 2);
                    _triangles.Add(a + 1);
                    _triangles.Add(a + 1);
                    _triangles.Add(a + 2);
                    _triangles.Add(a + 3);
                }
            }

            public void WriteDrip(Vector2 origin, float length, float width, float phase, Color32 color)
            {
                int steps = Mathf.Max(2, Mathf.CeilToInt(length / 0.05f));
                int start = _vertices.Count;
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    float y = -length * t;
                    // 흐르면서 살짝 갈지자로 휘고, 아래로 갈수록 가늘어진다.
                    float x = Mathf.Sin(phase + y * 11f) * 0.012f * t;
                    float w = width * Mathf.Lerp(1f, 0.55f, t);
                    var center = new Vector2(origin.x + x, origin.y + y);
                    Add(center + Vector2.left * (w * 0.5f), color, new Vector2(phase * 10f + y, 0f), 0.8f, 1f);
                    Add(center + Vector2.right * (w * 0.5f), color, new Vector2(phase * 10f + y, 1f), 0.8f, 1f);
                }

                for (int i = 0; i < steps; i++)
                {
                    int a = start + i * 2;
                    _triangles.Add(a);
                    _triangles.Add(a + 2);
                    _triangles.Add(a + 1);
                    _triangles.Add(a + 1);
                    _triangles.Add(a + 2);
                    _triangles.Add(a + 3);
                }

                // 끝에 맺힌 방울.
                Vector2 bottom = new(origin.x + Mathf.Sin(phase - length * 11f) * 0.012f, origin.y - length);
                WriteDisc(bottom, width * 0.55f * 0.85f, 1.25f, Mathf.PI * 0.5f, color, phase * 10f - length, 2f);
            }

            public void WriteDisc(Vector2 center, float radius, float stretch, float angle, Color32 color, float u,
                float kind = 0f, float wet = 1f)
            {
                int start = _vertices.Count;
                if (kind >= 2f)
                    wet = 0.5f;
                Add(center, color, new Vector2(u, 0.5f), wet, kind);
                for (int i = 0; i < DiscSegments; i++)
                {
                    float a = i / (float)DiscSegments * Mathf.PI * 2f;
                    var offset = new Vector2(Mathf.Cos(a) * radius * stretch, Mathf.Sin(a) * radius);
                    offset = Rotate(offset, angle);
                    Add(center + offset, color, new Vector2(u + a * 0.15f, 0f), wet, kind);
                }

                for (int i = 0; i < DiscSegments; i++)
                {
                    _triangles.Add(start);
                    _triangles.Add(start + 1 + i);
                    _triangles.Add(start + 1 + (i + 1) % DiscSegments);
                }
            }

            private static Vector2 Tangent(Vector2[] points, int index, Vector2 at)
            {
                Vector2 before = index > 0 ? points[index - 1] : at;
                Vector2 after = index < points.Length - 1 ? points[index + 1] : at;
                Vector2 tangent = after - before;
                if (tangent.sqrMagnitude < 1e-10f)
                    tangent = index > 0 ? at - points[index - 1] : points[Mathf.Min(1, points.Length - 1)] - at;
                return tangent.sqrMagnitude < 1e-10f ? Vector2.right : tangent.normalized;
            }
        }
    }
}
