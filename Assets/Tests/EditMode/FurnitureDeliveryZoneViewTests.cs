using System.Collections.Generic;
using GhostHunter.Gameplay.Map;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>반출 구역 꾸미기(FurnitureDeliveryZoneView, stage-system.md §2.1) — 구성·페인트 질감·충돌체 없음(렌더는 PlayMode FurnitureDeliveryZoneRenderTests).</summary>
    public sealed class FurnitureDeliveryZoneViewTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
            Physics.SyncTransforms();
        }

        [Test]
        public void 표시는_칠_빛무리_외곽선_조명으로_이루어지고_충돌체가_없다()
        {
            FurnitureDeliveryZoneView view = CreateView(out GameObject zone);

            view.Rebuild();

            Transform root = zone.transform.Find("DeliveryZoneView");
            Assert.IsNotNull(root, "표시 루트가 없습니다.");
            Assert.IsNotNull(root.Find("Paint"), "바닥에 칠한 페인트");
            Assert.IsNotNull(root.Find("Halo"), "외곽선 둘레 빛무리");
            Assert.IsNotNull(root.Find("OuterEdge"), "빛나는 외곽선");
            Assert.IsNotNull(root.Find("CoreEdge"), "외곽선 가운데 심지");
            Assert.IsNull(root.Find("Pillars"), "빛기둥은 없앴다(형광 느낌).");
            Assert.IsNull(root.Find("InnerEdge"), "안쪽 선은 페인트 띠로 바뀌었다.");

            Light glow = root.Find("DeliveryGlow").GetComponent<Light>();
            Assert.AreEqual(LightType.Point, glow.type);
            Assert.AreEqual(LightShadows.None, glow.shadows, "그림자 없는 조명이어야 비용이 없다");
            Assert.Greater(glow.color.r, glow.color.g * 3f, "빨간 계열이어야 합니다.");
            Assert.Greater(glow.color.r, glow.color.b * 3f, "빨간 계열이어야 합니다.");

            Assert.IsEmpty(root.GetComponentsInChildren<Collider>(true), "표시는 충돌체가 없어야 가구·플레이어를 막지 않는다.");
            Assert.AreEqual(Vector3.one, root.lossyScale, "스케일 1 규칙");
        }

        [Test]
        public void 다시_만들어도_표시가_겹쳐_쌓이지_않는다()
        {
            FurnitureDeliveryZoneView view = CreateView(out GameObject zone);

            view.Rebuild();
            view.Rebuild();

            int roots = 0;
            foreach (Transform child in zone.transform)
            {
                if (child.name == "DeliveryZoneView")
                    roots++;
            }

            Assert.AreEqual(1, roots, "다시 만들 때 이전 표시를 지워야 한다.");
        }

        [Test]
        public void 빛무리는_외곽선에서_불투명하고_양쪽_끝은_투명하다()
        {
            FurnitureDeliveryZoneView view = CreateView(out GameObject zone);
            view.Rebuild();

            Mesh halo = zone.transform.Find("DeliveryZoneView/Halo").GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = halo.vertices;
            Color[] colors = halo.colors;
            Assert.AreEqual(12, vertices.Length, "안쪽·선·바깥쪽 세 겹 × 모서리 4개");

            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(0f, colors[i].a, 0.001f, $"안쪽 끝 {i}");
                Assert.Greater(colors[4 + i].a, 0.3f, $"외곽선 위 {i}");
                Assert.AreEqual(0f, colors[8 + i].a, 0.001f, $"바깥쪽 끝 {i}");
            }

            // 모서리 0 은 (−x, −z): 안쪽 → 선 → 바깥쪽으로 갈수록 더 바깥(더 음수)에 있다.
            Assert.Greater(vertices[0].x, vertices[4].x, "안쪽 끝이 선보다 안쪽");
            Assert.Less(vertices[8].x, vertices[4].x, "바깥쪽 끝이 선보다 바깥쪽");
        }

        [Test]
        public void 페인트는_탁한_빨강이고_벗겨진_곳과_띠_사이_틈이_있다()
        {
            const float size = 4f;
            Texture2D texture = FurnitureDeliveryZonePaint.CreateTexture(size, size);
            _created.Add(texture);

            Assert.AreEqual(FurnitureDeliveryZonePaint.ResolutionFor(size), texture.width);
            Assert.AreEqual(FurnitureDeliveryZonePaint.ResolutionFor(size), texture.height);

            int opaque = 0;
            int bare = 0;
            foreach (Color32 pixel in texture.GetPixels32())
            {
                if (pixel.a >= 128)
                {
                    opaque++;
                    Assert.Greater(pixel.r, pixel.g * 1.8f, "칠한 곳은 빨강이 지배해야 한다.");
                    Assert.Greater(pixel.r, pixel.b * 1.8f, "칠한 곳은 빨강이 지배해야 한다.");
                    Assert.LessOrEqual(pixel.r, 200, "형광처럼 번쩍이지 않게 어둡고 탁해야 한다.");
                }
                else if (pixel.a < 40)
                {
                    bare++;
                }
            }

            int total = texture.width * texture.height;
            Assert.Greater(opaque, total * 0.5f, $"대부분은 칠해져 있어야 빨간 바닥으로 읽힌다(칠한 {opaque} / 전체 {total}).");
            Assert.Greater(bare, total * 0.03f, $"벗겨진 곳·틈이 있어야 스티커가 아니라 페인트로 읽힌다(빈 {bare} / 전체 {total}).");

            // 띠(가장자리 0~0.3m)와 안쪽 칠(0.4m~) 사이 0.35m 선은 비어 있다. 띠 한가운데(0.15m)는 대부분 칠해져 있다.
            int samples = 0;
            int gapClear = 0;
            int bandPainted = 0;
            for (float z = 0.5f; z < size - 0.5f; z += 0.05f)
            {
                samples++;
                if (FurnitureDeliveryZonePaint.Paint(0.35f, z, size, size).a < 40)
                    gapClear++;
                if (FurnitureDeliveryZonePaint.Paint(0.15f, z, size, size).a >= 128)
                    bandPainted++;
            }

            Assert.GreaterOrEqual(gapClear, samples * 0.95f, "띠와 안쪽 칠 사이에는 틈이 있어야 한다.");
            Assert.GreaterOrEqual(bandPainted, samples * 0.6f, "가장자리 띠는 대부분 칠해져 있어야 한다.");
        }

        [Test]
        public void 같은_크기면_페인트가_항상_같은_그림이다()
        {
            Texture2D first = FurnitureDeliveryZonePaint.CreateTexture(2f, 3f);
            Texture2D second = FurnitureDeliveryZonePaint.CreateTexture(2f, 3f);
            _created.Add(first);
            _created.Add(second);

            CollectionAssert.AreEqual(first.GetPixels32(), second.GetPixels32(), "접속자마다 같은 모양이어야 한다.");
            Assert.AreNotEqual(first.width, first.height, "가로·세로 크기를 따로 반영한다.");
        }

        private FurnitureDeliveryZoneView CreateView(out GameObject zone)
        {
            zone = new GameObject("FurnitureDeliveryZone");
            _created.Add(zone);
            zone.transform.position = new Vector3(0f, 1.25f, 0f);
            zone.AddComponent<FurnitureDeliveryZone>();
            return zone.AddComponent<FurnitureDeliveryZoneView>();
        }
    }
}
