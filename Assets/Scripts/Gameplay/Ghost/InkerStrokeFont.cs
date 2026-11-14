using System;
using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 인커 명령 글씨의 손글씨 획 글꼴(사용자 요청 2026-11-15 "피로 쓰이는 느낌"). 글자마다 손가락이 지나가는 순서대로
    /// 꺾은선 획을 갖는다 — 글꼴 텍스처가 아니라 획이라 "쓰는 중" 손끝 위치·흘러내림을 만들 수 있다.
    /// 단위는 글자 높이(대문자 높이 = 1, 기준선 y = 0)이고, 글자 폭은 글자마다 다르다(<see cref="Glyph.Advance"/>).
    /// 순수 계산이라 서버(판정 사각형 크기)와 피어(그리기)가 같은 결과를 낸다.
    /// </summary>
    public static class InkerStrokeFont
    {
        /// <summary>글자 사이 간격(글자 높이 단위).</summary>
        public const float LetterSpacing = 0.2f;

        /// <summary>띄어쓰기 폭.</summary>
        public const float SpaceAdvance = 0.45f;

        /// <summary>줄 간격(기준선 사이, 글자 높이 단위) — 흘러내린 피가 아랫줄과 겹치지 않을 만큼 띄운다.</summary>
        public const float LineAdvance = 1.55f;

        public sealed class Glyph
        {
            public readonly float Advance;
            public readonly Vector2[][] Strokes;

            public Glyph(float advance, Vector2[][] strokes)
            {
                Advance = advance;
                Strokes = strokes;
            }
        }

        private static Dictionary<char, Glyph> s_glyphs;

        /// <summary>정의된 글자인가(대문자·작은따옴표·마침표·느낌표·물음표).</summary>
        public static bool Has(char c) => c == ' ' || c == '\n' || Glyphs.ContainsKey(char.ToUpperInvariant(c));

        public static bool TryGetGlyph(char c, out Glyph glyph) => Glyphs.TryGetValue(char.ToUpperInvariant(c), out glyph);

        /// <summary>
        /// 문구를 낱말 단위로 줄바꿈해 획을 배치한다. <paramref name="maxLineWidth"/> 는 글자 높이 단위의 최대 줄 폭이다
        /// (0 이하면 줄바꿈 안 함). 결과의 좌표는 글자 높이 단위이고 원점은 글씨 사각형 중심이다
        /// (+x = 읽는 사람의 오른쪽, +y = 위).
        /// </summary>
        public static InkerTextLayout Layout(string text, float maxLineWidth)
        {
            var layout = new InkerTextLayout();
            if (string.IsNullOrEmpty(text))
                return layout;

            List<string> lines = Wrap(text.ToUpperInvariant(), maxLineWidth);
            float widest = 0f;
            foreach (string line in lines)
                widest = Mathf.Max(widest, MeasureLine(line));

            int lineCount = lines.Count;
            float height = 1f + (lineCount - 1) * LineAdvance;
            layout.Width = Mathf.Max(0.2f, widest);
            layout.Height = height;
            layout.LineCount = lineCount;

            for (int l = 0; l < lineCount; l++)
            {
                string line = lines[l];
                // 줄은 가운데 정렬. 첫 줄 기준선이 맨 위 글자 줄이다.
                float x = -MeasureLine(line) * 0.5f;
                float baseline = height * 0.5f - 1f - l * LineAdvance;
                int letterIndex = 0;
                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];
                    if (c == ' ')
                    {
                        x += SpaceAdvance;
                        continue;
                    }

                    if (!TryGetGlyph(c, out Glyph glyph))
                        continue;

                    foreach (Vector2[] stroke in glyph.Strokes)
                    {
                        var points = new Vector2[stroke.Length];
                        for (int p = 0; p < stroke.Length; p++)
                            points[p] = new Vector2(x + stroke[p].x, baseline + stroke[p].y);
                        layout.Strokes.Add(new InkerTextLayout.Stroke(points, l, letterIndex));
                    }

                    letterIndex++;
                    x += glyph.Advance + LetterSpacing;
                }
            }

            return layout;
        }

        /// <summary>판정 사각형이 글자 둘레로 더 잡는 여백(글자 높이 단위).</summary>
        public const float RectPadding = 0.15f;

        /// <summary>
        /// 서버 — 글씨 판정 사각형과 피어 그리기에 쓸 크기를 정한다. 줄이 <paramref name="maxWidth"/>(m)를 넘으면 낱말 단위로
        /// 줄을 바꾸고, 한 낱말만으로도 넓으면 글자 높이를 줄인다(문짝 폭 등). <paramref name="maxWidth"/> 가 0 이하면 한 줄.
        /// </summary>
        public static void Size(string text, float letterHeight, float maxWidth,
            out float width, out float height, out float wrapWidth, out float finalLetterHeight)
        {
            letterHeight = Mathf.Max(0.02f, letterHeight);
            wrapWidth = maxWidth > 0f ? maxWidth / letterHeight : 0f;
            InkerTextLayout layout = Layout(text, wrapWidth);
            finalLetterHeight = letterHeight;
            if (maxWidth > 0f && layout.Width * letterHeight > maxWidth)
                finalLetterHeight = maxWidth / layout.Width;
            width = (layout.Width + RectPadding * 2f) * finalLetterHeight;
            height = (layout.Height + RectPadding * 2f) * finalLetterHeight;
        }

        /// <summary>한 줄 폭(글자 높이 단위). 마지막 글자 뒤 간격은 넣지 않는다.</summary>
        public static float MeasureLine(string line)
        {
            float width = 0f;
            bool any = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == ' ')
                {
                    width += SpaceAdvance;
                    continue;
                }

                if (!TryGetGlyph(c, out Glyph glyph))
                    continue;
                width += glyph.Advance + LetterSpacing;
                any = true;
            }

            return any ? Mathf.Max(0f, width - LetterSpacing) : width;
        }

        private static List<string> Wrap(string text, float maxLineWidth)
        {
            var lines = new List<string>(3);
            foreach (string paragraph in text.Split('\n'))
            {
                string[] words = paragraph.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string current = string.Empty;
                foreach (string word in words)
                {
                    string candidate = current.Length == 0 ? word : current + " " + word;
                    if (current.Length > 0 && maxLineWidth > 0f && MeasureLine(candidate) > maxLineWidth + 1e-4f)
                    {
                        lines.Add(current);
                        current = word;
                    }
                    else
                    {
                        current = candidate;
                    }
                }

                if (current.Length > 0)
                    lines.Add(current);
            }

            if (lines.Count == 0)
                lines.Add(string.Empty);
            return lines;
        }

        // ───────────────────────── 글자 정의 ─────────────────────────

        private static Dictionary<char, Glyph> Glyphs => s_glyphs ??= BuildGlyphs();

        private static Vector2[] P(params float[] xy)
        {
            var points = new Vector2[xy.Length / 2];
            for (int i = 0; i < points.Length; i++)
                points[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
            return points;
        }

        /// <summary>타원 호. 끝 각이 시작 각보다 작으면 시계 방향으로 돈다(도 단위, 0 = +x, 90 = +y).</summary>
        private static Vector2[] Arc(float cx, float cy, float rx, float ry, float fromDeg, float toDeg, int segments = 10)
        {
            var points = new Vector2[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.Lerp(fromDeg, toDeg, i / (float)segments) * Mathf.Deg2Rad;
                points[i] = new Vector2(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry);
            }

            return points;
        }

        /// <summary>꺾은선 여러 조각을 한 획으로 잇는다(같은 점이 겹치면 하나로).</summary>
        private static Vector2[] Join(params Vector2[][] parts)
        {
            var points = new List<Vector2>(32);
            foreach (Vector2[] part in parts)
            {
                foreach (Vector2 point in part)
                {
                    if (points.Count > 0 && (points[points.Count - 1] - point).sqrMagnitude < 1e-6f)
                        continue;
                    points.Add(point);
                }
            }

            return points.ToArray();
        }

        private static Dictionary<char, Glyph> BuildGlyphs()
        {
            var g = new Dictionary<char, Glyph>
            {
                ['A'] = new(0.72f, new[] { P(0f, 0f, 0.36f, 1f, 0.72f, 0f), P(0.16f, 0.4f, 0.56f, 0.4f) }),
                ['B'] = new(0.6f, new[]
                {
                    P(0f, 0f, 0f, 1f),
                    Join(P(0f, 1f, 0.34f, 1f), Arc(0.34f, 0.77f, 0.22f, 0.23f, 90f, -90f, 8), P(0.34f, 0.54f, 0f, 0.54f)),
                    Join(P(0f, 0.54f, 0.36f, 0.54f), Arc(0.36f, 0.27f, 0.24f, 0.27f, 90f, -90f, 8), P(0.36f, 0f, 0f, 0f)),
                }),
                ['C'] = new(0.66f, new[] { Arc(0.38f, 0.5f, 0.38f, 0.5f, 45f, 315f, 14) }),
                ['D'] = new(0.66f, new[]
                {
                    P(0f, 0f, 0f, 1f),
                    Join(P(0f, 1f, 0.24f, 1f), Arc(0.24f, 0.5f, 0.42f, 0.5f, 90f, -90f, 12), P(0.24f, 0f, 0f, 0f)),
                }),
                ['E'] = new(0.56f, new[] { P(0.56f, 1f, 0f, 1f, 0f, 0f, 0.56f, 0f), P(0f, 0.52f, 0.44f, 0.52f) }),
                ['F'] = new(0.54f, new[] { P(0.54f, 1f, 0f, 1f, 0f, 0f), P(0f, 0.52f, 0.42f, 0.52f) }),
                ['G'] = new(0.72f, new[]
                {
                    Join(Arc(0.38f, 0.5f, 0.38f, 0.5f, 45f, 345f, 14), P(0.74f, 0.42f, 0.74f, 0.44f, 0.42f, 0.44f)),
                }),
                ['H'] = new(0.62f, new[] { P(0f, 1f, 0f, 0f), P(0.62f, 1f, 0.62f, 0f), P(0f, 0.52f, 0.62f, 0.52f) }),
                ['I'] = new(0.08f, new[] { P(0.04f, 1f, 0.04f, 0f) }),
                ['J'] = new(0.5f, new[] { Join(P(0.5f, 1f, 0.5f, 0.28f), Arc(0.25f, 0.28f, 0.25f, 0.28f, 0f, -180f, 8)) }),
                ['K'] = new(0.6f, new[] { P(0f, 1f, 0f, 0f), P(0.6f, 1f, 0f, 0.4f), P(0.2f, 0.58f, 0.62f, 0f) }),
                ['L'] = new(0.5f, new[] { P(0f, 1f, 0f, 0f, 0.5f, 0f) }),
                ['M'] = new(0.82f, new[] { P(0f, 0f, 0f, 1f, 0.41f, 0.32f, 0.82f, 1f, 0.82f, 0f) }),
                ['N'] = new(0.66f, new[] { P(0f, 0f, 0f, 1f, 0.66f, 0f, 0.66f, 1f) }),
                ['O'] = new(0.76f, new[] { Arc(0.38f, 0.5f, 0.38f, 0.5f, 100f, 465f, 18) }),
                ['P'] = new(0.58f, new[]
                {
                    Join(P(0f, 0f, 0f, 1f, 0.32f, 1f), Arc(0.32f, 0.74f, 0.26f, 0.26f, 90f, -90f, 8), P(0.32f, 0.48f, 0f, 0.48f)),
                }),
                ['Q'] = new(0.78f, new[] { Arc(0.38f, 0.5f, 0.38f, 0.5f, 100f, 465f, 18), P(0.46f, 0.26f, 0.8f, -0.06f) }),
                ['R'] = new(0.6f, new[]
                {
                    Join(P(0f, 0f, 0f, 1f, 0.32f, 1f), Arc(0.32f, 0.74f, 0.26f, 0.26f, 90f, -90f, 8), P(0.32f, 0.48f, 0f, 0.48f)),
                    P(0.28f, 0.48f, 0.62f, 0f),
                }),
                ['S'] = new(0.6f, new[]
                {
                    Join(Arc(0.3f, 0.75f, 0.28f, 0.25f, 20f, 270f, 10), Arc(0.3f, 0.25f, 0.3f, 0.25f, 90f, -160f, 10)),
                }),
                ['T'] = new(0.64f, new[] { P(0f, 1f, 0.64f, 1f), P(0.32f, 1f, 0.32f, 0f) }),
                ['U'] = new(0.64f, new[] { Join(P(0f, 1f, 0f, 0.3f), Arc(0.32f, 0.3f, 0.32f, 0.3f, 180f, 360f, 10), P(0.64f, 0.3f, 0.64f, 1f)) }),
                ['V'] = new(0.7f, new[] { P(0f, 1f, 0.35f, 0f, 0.7f, 1f) }),
                ['W'] = new(0.96f, new[] { P(0f, 1f, 0.22f, 0f, 0.48f, 0.66f, 0.74f, 0f, 0.96f, 1f) }),
                ['X'] = new(0.64f, new[] { P(0f, 1f, 0.64f, 0f), P(0.64f, 1f, 0f, 0f) }),
                ['Y'] = new(0.68f, new[] { P(0f, 1f, 0.34f, 0.5f, 0.68f, 1f), P(0.34f, 0.5f, 0.34f, 0f) }),
                ['Z'] = new(0.62f, new[] { P(0f, 1f, 0.62f, 1f, 0f, 0f, 0.62f, 0f) }),
                ['\''] = new(0.06f, new[] { P(0.05f, 1.02f, 0.01f, 0.72f) }),
                ['.'] = new(0.06f, new[] { P(0.03f, 0.06f, 0.03f, 0f) }),
                ['!'] = new(0.08f, new[] { P(0.04f, 1f, 0.04f, 0.3f), P(0.04f, 0.07f, 0.04f, 0f) }),
                ['?'] = new(0.52f, new[]
                {
                    Join(Arc(0.26f, 0.74f, 0.26f, 0.26f, 160f, -40f, 9), P(0.26f, 0.42f, 0.26f, 0.28f)),
                    P(0.26f, 0.07f, 0.26f, 0f),
                }),
            };
            return g;
        }
    }

    /// <summary><see cref="InkerStrokeFont.Layout"/> 결과 — 글자 높이 단위, 원점은 글씨 사각형 중심.</summary>
    public sealed class InkerTextLayout
    {
        public readonly struct Stroke
        {
            public readonly Vector2[] Points;
            public readonly int Line;
            public readonly int Letter;

            public Stroke(Vector2[] points, int line, int letter)
            {
                Points = points;
                Line = line;
                Letter = letter;
            }
        }

        public readonly List<Stroke> Strokes = new(64);
        public float Width;
        public float Height;
        public int LineCount;

        /// <summary>모든 획 길이의 합(글자 높이 단위) — 쓰는 진행도를 손끝 위치로 바꾸는 데 쓴다.</summary>
        public float TotalLength
        {
            get
            {
                float total = 0f;
                foreach (Stroke stroke in Strokes)
                    for (int i = 1; i < stroke.Points.Length; i++)
                        total += Vector2.Distance(stroke.Points[i - 1], stroke.Points[i]);
                return total;
            }
        }
    }
}
