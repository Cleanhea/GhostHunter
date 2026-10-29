using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.FurnitureDriver
{
    /// <summary>
    /// 짐더미 모양 비율(기획서 furniture-multidriver-system.md §4.1). 모두 짐더미 <b>가장 짧은 변</b> 기준이라
    /// 크기가 다른 가구에서도 같은 인상이 나온다.
    /// </summary>
    public readonly struct FurnitureBundleShape
    {
        /// <summary>모서리 둥글기 — 가장 짧은 반변 길이에 대한 반지름 비율(0.2~0.9로 잘라 쓴다).</summary>
        public readonly float CornerRadiusRatio;

        /// <summary>끈이 파고든 깊이.</summary>
        public readonly float PinchDepthRatio;

        public readonly float StrapWidthRatio;

        /// <summary>끈이 홈 바닥 위로 솟은 두께. 끈 가장자리 자리의 천 높이를 넘지 않게 잘라 쓴다 — 끈이 천이 부푼 곳보다 낮게 앉는다.</summary>
        public readonly float StrapThicknessRatio;

        public FurnitureBundleShape(float cornerRadiusRatio, float pinchDepthRatio,
            float strapWidthRatio, float strapThicknessRatio)
        {
            CornerRadiusRatio = cornerRadiusRatio;
            PinchDepthRatio = pinchDepthRatio;
            StrapWidthRatio = strapWidthRatio;
            StrapThicknessRatio = strapThicknessRatio;
        }

        public static FurnitureBundleShape Default => new(0.6f, 0.06f, 0.12f, 0.04f);
    }

    /// <summary>
    /// 포장된 짐더미 메시를 코드로 만든다(레퍼런스: 둥글게 부푼 천 포대를 검은 끈으로 묶은 모양).
    /// 에셋 파일 없이 크기만 넘기면 같은 메시를 모든 접속자가 똑같이 만든다 — 가구마다 크기가 달라도 한 함수로 끝난다.
    ///
    /// <para>모양: 모서리가 크게 둥근 상자 + 긴 변을 삼등분하는 끈 2줄 + 가장 짧은 변 가운데를 두르는 허리 끈 1줄.
    /// 끈 자리는 천을 안쪽으로 눌러 홈을 파고, 끈은 그 홈을 따라 얹는다. 서브메시 0 = 천, 1 = 끈.</para>
    ///
    /// <para>메시는 원점이 중심이고 경계가 정확히 <c>size</c>다. 크기 축은 가장 긴/짧은 변을 찾아 정규 좌표로
    /// 옮겨 만든 뒤 원래 축으로 되돌린다 — 세로로 선 옷장은 세로로 선 짐더미가 된다.</para>
    /// </summary>
    public static class FurnitureBundleMesh
    {
        public const int BodySubMesh = 0;
        public const int StrapSubMesh = 1;

        private const int ArcSteps = 4;
        private const float MaxTickSpacing = 0.06f;
        private const float MergeEpsilon = 1e-4f;

        /// <summary>원본 크기에서 짐더미 크기를 구한다 — 모든 변을 같은 비율로 줄이되 너무 얇아지지 않게 한다.</summary>
        public static Vector3 SizeFor(Vector3 sourceSize, float scale, float minimumDimension) => new(
            Mathf.Max(minimumDimension, sourceSize.x * scale),
            Mathf.Max(minimumDimension, sourceSize.y * scale),
            Mathf.Max(minimumDimension, sourceSize.z * scale));

        public static Mesh Build(Vector3 size, FurnitureBundleShape shape)
        {
            var builder = new Builder(size, shape);
            return builder.Build();
        }

        private sealed class Builder
        {
            private readonly int _long;
            private readonly int _short;
            private readonly int _middle;
            private readonly Vector3 _half;       // 정규 좌표 반변 (x=긴 변, y=짧은 변, z=나머지)
            private readonly Vector3 _inner;      // 둥근 모서리를 뺀 평평한 구간의 반변
            private readonly float _radius;
            private readonly float _pinch;
            private readonly float _strapWidth;
            private readonly float _strapThickness;
            private readonly float _grooveHalfWidth;
            private readonly float _strapOffset;  // 긴 변 끈의 중심 (±)
            private readonly float[][] _ticks = new float[3][];

            private readonly List<Vector3> _positions = new();
            private readonly List<Vector3> _normals = new();
            private readonly List<int> _bodyTriangles = new();
            private readonly List<int> _strapTriangles = new();
            private readonly Dictionary<(int, int, int), int> _bodyIndex = new();

            public Builder(Vector3 size, FurnitureBundleShape shape)
            {
                _long = 0;
                _short = 0;
                for (int i = 1; i < 3; i++)
                {
                    if (size[i] > size[_long])
                        _long = i;
                    if (size[i] < size[_short])
                        _short = i;
                }

                // 세 변이 같으면 둘이 겹친다 — 임의로 나눈다.
                if (_long == _short)
                    _short = (_long + 1) % 3;
                _middle = 3 - _long - _short;

                _half = new Vector3(size[_long], size[_short], size[_middle]) * 0.5f;
                float minHalf = Mathf.Min(_half.x, Mathf.Min(_half.y, _half.z));
                float minSize = minHalf * 2f;

                _radius = Mathf.Clamp(shape.CornerRadiusRatio, 0.2f, 0.9f) * minHalf;
                _inner = _half - Vector3.one * _radius;
                _pinch = shape.PinchDepthRatio * minSize;
                _strapWidth = shape.StrapWidthRatio * minSize;
                _grooveHalfWidth = Mathf.Max(_strapWidth * 1.5f, 0.005f);
                // 끈 가장자리는 홈 바닥보다 얕은 곳에 놓인다 — 그 자리의 천 높이를 넘지 않게 두께를 자른다(경계 밖으로 솟지 않도록).
                _strapThickness = Mathf.Min(shape.StrapThicknessRatio * minSize, _pinch * Bump(_strapWidth * 0.5f) * 0.9f);
                _strapOffset = _half.x / 3f;
            }

            public Mesh Build()
            {
                _ticks[0] = Ticks(_half.x, _radius, GrooveTicks(-_strapOffset, _strapOffset));
                _ticks[1] = Ticks(_half.y, _radius, GrooveTicks(0f));
                _ticks[2] = Ticks(_half.z, _radius, null);

                BuildBody();
                // 허리 끈은 짧은 변에 수직인 면(정규 y), 긴 변 끈은 긴 변에 수직인 면(정규 x)이다.
                BuildStrap(axis: 0, -_strapOffset);
                BuildStrap(axis: 0, _strapOffset);
                BuildStrap(axis: 1, 0f);

                var mesh = new Mesh { name = "FurnitureBundle" };
                mesh.SetVertices(_positions);
                mesh.SetNormals(_normals);
                mesh.subMeshCount = 2;
                mesh.SetTriangles(_bodyTriangles, BodySubMesh);
                mesh.SetTriangles(_strapTriangles, StrapSubMesh);
                // 홈 때문에 법선이 달라지므로 정규 법선 대신 다시 계산한다(천은 용접돼 있어 이음매가 없다).
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }

            private IEnumerable<float> GrooveTicks(params float[] centers)
            {
                foreach (float center in centers)
                    for (int k = -3; k <= 3; k++)
                        yield return center + _grooveHalfWidth * k / 3f;
            }

            /// <summary>한 축의 격자 눈금. 모서리 호는 각도가 고르게, 평평한 구간은 간격이 고르게 나누고 끈 자리를 끼운다.</summary>
            private static float[] Ticks(float half, float radius, IEnumerable<float> extra)
            {
                float inner = half - radius;
                var list = new List<float>();
                for (int k = 0; k <= ArcSteps; k++)
                {
                    float theta = Mathf.PI * 0.25f * (ArcSteps - k) / ArcSteps;
                    list.Add(-inner - radius * Mathf.Tan(theta));
                }

                int middle = Mathf.Max(1, Mathf.CeilToInt(2f * inner / MaxTickSpacing));
                for (int k = 1; k < middle; k++)
                    list.Add(-inner + 2f * inner * k / middle);
                if (extra != null)
                    foreach (float value in extra)
                        if (Mathf.Abs(value) < inner - MergeEpsilon)
                            list.Add(value);

                for (int k = 0; k <= ArcSteps; k++)
                {
                    float theta = Mathf.PI * 0.25f * k / ArcSteps;
                    list.Add(inner + radius * Mathf.Tan(theta));
                }

                list.Sort();
                var result = new List<float>(list.Count);
                foreach (float value in list)
                    if (result.Count == 0 || value - result[result.Count - 1] > MergeEpsilon)
                        result.Add(value);
                return result.ToArray();
            }

            /// <summary>상자 겉면의 점 <paramref name="c"/>를 둥근 상자 표면으로 옮기고 끈 홈만큼 눌러 정규 좌표로 돌려준다.</summary>
            private Vector3 Surface(Vector3 c, out Vector3 normal)
            {
                var inner = new Vector3(
                    Mathf.Clamp(c.x, -_inner.x, _inner.x),
                    Mathf.Clamp(c.y, -_inner.y, _inner.y),
                    Mathf.Clamp(c.z, -_inner.z, _inner.z));
                normal = (c - inner).normalized;
                Vector3 point = inner + normal * _radius;
                return point - normal * (_pinch * Groove(point));
            }

            /// <summary>끈 홈의 깊이 비율(0~1). 세 끈 중 가장 깊은 값을 쓴다 — 끈이 겹치는 자리가 두 배로 파이지 않는다.</summary>
            private float Groove(Vector3 p)
            {
                float depth = Bump(p.y);
                depth = Mathf.Max(depth, Bump(p.x - _strapOffset));
                return Mathf.Max(depth, Bump(p.x + _strapOffset));
            }

            private float Bump(float distance)
            {
                distance = Mathf.Abs(distance);
                return distance >= _grooveHalfWidth ? 0f : 0.5f * (1f + Mathf.Cos(Mathf.PI * distance / _grooveHalfWidth));
            }

            private Vector3 ToObject(Vector3 canonical)
            {
                var v = new Vector3();
                v[_long] = canonical.x;
                v[_short] = canonical.y;
                v[_middle] = canonical.z;
                return v;
            }

            private int AddVertex(Vector3 canonicalPoint, Vector3 canonicalNormal)
            {
                _positions.Add(ToObject(canonicalPoint));
                _normals.Add(ToObject(canonicalNormal));
                return _positions.Count - 1;
            }

            private int BodyVertex((int x, int y, int z) lattice)
            {
                if (_bodyIndex.TryGetValue(lattice, out int found))
                    return found;
                var c = new Vector3(_ticks[0][lattice.x], _ticks[1][lattice.y], _ticks[2][lattice.z]);
                int index = AddVertex(Surface(c, out Vector3 normal), normal);
                _bodyIndex[lattice] = index;
                return index;
            }

            private void BuildBody()
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    int u = (axis + 1) % 3;
                    int v = (axis + 2) % 3;
                    for (int side = 0; side < 2; side++)
                    {
                        int fixedIndex = side == 0 ? 0 : _ticks[axis].Length - 1;
                        for (int iu = 0; iu < _ticks[u].Length - 1; iu++)
                        {
                            for (int iv = 0; iv < _ticks[v].Length - 1; iv++)
                            {
                                int a = BodyVertex(Index(axis, fixedIndex, u, iu, v, iv));
                                int b = BodyVertex(Index(axis, fixedIndex, u, iu + 1, v, iv));
                                int c = BodyVertex(Index(axis, fixedIndex, u, iu + 1, v, iv + 1));
                                int d = BodyVertex(Index(axis, fixedIndex, u, iu, v, iv + 1));
                                AddTriangle(_bodyTriangles, a, b, c);
                                AddTriangle(_bodyTriangles, a, c, d);
                            }
                        }
                    }
                }
            }

            private static (int, int, int) Index(int axis, int axisIndex, int u, int uIndex, int v, int vIndex)
            {
                var index = new int[3];
                index[axis] = axisIndex;
                index[u] = uIndex;
                index[v] = vIndex;
                return (index[0], index[1], index[2]);
            }

            /// <summary>
            /// <paramref name="axis"/>에 수직인 면을 한 바퀴 두르는 끈 한 줄. 천과 같은 표면 함수를 쓰므로 홈 바닥에 정확히 앉고,
            /// 가로 3줄(가장자리·가운데·가장자리)이라 홈의 굴곡을 따라 휜다.
            /// </summary>
            private void BuildStrap(int axis, float center)
            {
                int u = (axis + 1) % 3;
                int v = (axis + 2) % 3;
                float[] tu = _ticks[u];
                float[] tv = _ticks[v];
                float hu = _half[u];
                float hv = _half[v];

                // 직사각형 둘레를 한 바퀴 — 시작점을 되풀이하지 않는다.
                var loop = new List<Vector2>();
                for (int i = 0; i < tu.Length; i++)
                    loop.Add(new Vector2(tu[i], hv));
                for (int j = tv.Length - 2; j >= 0; j--)
                    loop.Add(new Vector2(hu, tv[j]));
                for (int i = tu.Length - 2; i >= 0; i--)
                    loop.Add(new Vector2(tu[i], -hv));
                for (int j = 1; j < tv.Length - 1; j++)
                    loop.Add(new Vector2(-hu, tv[j]));

                float[] offsets = { -_strapWidth * 0.5f, 0f, _strapWidth * 0.5f };
                var rows = new int[offsets.Length][];
                for (int column = 0; column < offsets.Length; column++)
                {
                    rows[column] = new int[loop.Count];
                    for (int k = 0; k < loop.Count; k++)
                    {
                        var c = new Vector3();
                        c[axis] = center + offsets[column];
                        c[u] = loop[k].x;
                        c[v] = loop[k].y;
                        Vector3 point = Surface(c, out Vector3 normal);
                        rows[column][k] = AddVertex(point + normal * _strapThickness, normal);
                    }
                }

                for (int column = 0; column < offsets.Length - 1; column++)
                {
                    for (int k = 0; k < loop.Count; k++)
                    {
                        int next = (k + 1) % loop.Count;
                        int a = rows[column][k];
                        int b = rows[column][next];
                        int c = rows[column + 1][next];
                        int d = rows[column + 1][k];
                        AddTriangle(_strapTriangles, a, b, c);
                        AddTriangle(_strapTriangles, a, c, d);
                    }
                }
            }

            /// <summary>삼각형이 바깥(꼭짓점 법선 평균)을 향하도록 감는 방향을 맞춘다 — 축 치환의 홀짝과 무관하다.</summary>
            private void AddTriangle(List<int> triangles, int a, int b, int c)
            {
                Vector3 facing = Vector3.Cross(_positions[b] - _positions[a], _positions[c] - _positions[a]);
                Vector3 outward = _normals[a] + _normals[b] + _normals[c];
                if (Vector3.Dot(facing, outward) < 0f)
                    (b, c) = (c, b);
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }
        }
    }
}
