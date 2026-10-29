using GhostHunter.Gameplay.FurnitureDriver;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>짐더미 메시(기획서 §4.1)가 크기·축·표면 방향을 지키는지 검증한다 — 겉모양은 수동 확인.</summary>
    public sealed class FurnitureBundleMeshTests
    {
        private static readonly Vector3[] Sizes =
        {
            new(0.64f, 0.52f, 0.8f),   // 더블 침대 크기를 줄인 값 — 긴 변 z
            new(0.48f, 0.72f, 0.3f),   // 세로로 선 옷장 — 긴 변 y
            new(0.5f, 0.5f, 0.5f),     // 정육면체 — 긴 변·짧은 변이 겹치는 경우
            new(1.04f, 0.3f, 0.34f),   // 길쭉한 선반
        };

        [Test]
        public void 크기_정확히_메시_경계가_원하는_크기다([ValueSource(nameof(Sizes))] Vector3 size)
        {
            Mesh mesh = FurnitureBundleMesh.Build(size, FurnitureBundleShape.Default);
            try
            {
                Assert.AreEqual(size.x, mesh.bounds.size.x, 0.002f);
                Assert.AreEqual(size.y, mesh.bounds.size.y, 0.002f);
                Assert.AreEqual(size.z, mesh.bounds.size.z, 0.002f);
                Assert.AreEqual(0f, mesh.bounds.center.magnitude, 0.002f, "원점이 중심이어야 한다.");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void 서브메시는_천과_끈_둘이고_인덱스가_범위_안이다([ValueSource(nameof(Sizes))] Vector3 size)
        {
            Mesh mesh = FurnitureBundleMesh.Build(size, FurnitureBundleShape.Default);
            try
            {
                Assert.AreEqual(2, mesh.subMeshCount);
                int vertexCount = mesh.vertexCount;
                Assert.Less(vertexCount, 65000, "16비트 인덱스를 넘으면 안 된다.");
                for (int sub = 0; sub < 2; sub++)
                {
                    int[] triangles = mesh.GetTriangles(sub);
                    Assert.Greater(triangles.Length, 0, $"서브메시 {sub}가 비어 있다.");
                    Assert.AreEqual(0, triangles.Length % 3);
                    foreach (int index in triangles)
                        Assert.That(index, Is.InRange(0, vertexCount - 1));
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void 꼭짓점과_법선에_NaN이_없고_법선은_단위_길이다([ValueSource(nameof(Sizes))] Vector3 size)
        {
            Mesh mesh = FurnitureBundleMesh.Build(size, FurnitureBundleShape.Default);
            try
            {
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                Assert.AreEqual(vertices.Length, normals.Length);
                for (int i = 0; i < vertices.Length; i++)
                {
                    Assert.IsFalse(float.IsNaN(vertices[i].x + vertices[i].y + vertices[i].z), $"꼭짓점 {i}");
                    Assert.AreEqual(1f, normals[i].magnitude, 0.01f, $"법선 {i}");
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void 천은_바깥을_향한_닫힌_껍질이라_부피가_상자보다_조금_작다([ValueSource(nameof(Sizes))] Vector3 size)
        {
            Mesh mesh = FurnitureBundleMesh.Build(size, FurnitureBundleShape.Default);
            try
            {
                // 닫힌 껍질의 부호 있는 부피(발산 정리) — 안쪽을 향하게 감겼으면 음수가 된다.
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.GetTriangles(FurnitureBundleMesh.BodySubMesh);
                float volume = 0f;
                for (int i = 0; i < triangles.Length; i += 3)
                    volume += Vector3.Dot(vertices[triangles[i]],
                        Vector3.Cross(vertices[triangles[i + 1]], vertices[triangles[i + 2]])) / 6f;

                float box = size.x * size.y * size.z;
                Assert.Greater(volume, box * 0.55f, "부피가 너무 작거나 음수다 — 감는 방향을 확인한다.");
                Assert.Less(volume, box, "둥근 모서리와 끈 홈 때문에 상자보다 작아야 한다.");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void 끈은_천_바로_위에_얹혀_경계를_넘지_않는다([ValueSource(nameof(Sizes))] Vector3 size)
        {
            Mesh mesh = FurnitureBundleMesh.Build(size, FurnitureBundleShape.Default);
            try
            {
                Vector3[] vertices = mesh.vertices;
                Vector3 half = size * 0.5f + Vector3.one * 0.002f;
                foreach (int index in mesh.GetTriangles(FurnitureBundleMesh.StrapSubMesh))
                {
                    Vector3 v = vertices[index];
                    Assert.LessOrEqual(Mathf.Abs(v.x), half.x);
                    Assert.LessOrEqual(Mathf.Abs(v.y), half.y);
                    Assert.LessOrEqual(Mathf.Abs(v.z), half.z);
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void 짐더미_크기는_모든_변을_같은_비율로_줄이되_최소_두께를_지킨다()
        {
            Vector3 size = FurnitureBundleMesh.SizeFor(new Vector3(1.6f, 1.3f, 2f), 0.4f, 0.3f);
            Assert.AreEqual(0.64f, size.x, 0.0001f);
            Assert.AreEqual(0.52f, size.y, 0.0001f);
            Assert.AreEqual(0.8f, size.z, 0.0001f);

            Vector3 thin = FurnitureBundleMesh.SizeFor(new Vector3(1.2f, 0.4f, 1.8f), 0.4f, 0.3f);
            Assert.AreEqual(0.48f, thin.x, 0.0001f);
            Assert.AreEqual(0.3f, thin.y, 0.0001f, "0.4 × 0.4 = 0.16m는 최소 두께로 올린다.");
        }

        [Test]
        public void 같은_크기로_두_번_만들면_같은_메시가_나온다()
        {
            Mesh a = FurnitureBundleMesh.Build(Sizes[0], FurnitureBundleShape.Default);
            Mesh b = FurnitureBundleMesh.Build(Sizes[0], FurnitureBundleShape.Default);
            try
            {
                Assert.AreEqual(a.vertexCount, b.vertexCount);
                CollectionAssert.AreEqual(a.vertices, b.vertices);
                CollectionAssert.AreEqual(a.triangles, b.triangles);
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }
    }
}
