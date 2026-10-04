using System.Collections.Generic;
using GhostHunter.Gameplay.Map;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>반출 구역 꾸미기(FurnitureDeliveryZoneView, stage-system.md §2.1) — 구성과 충돌체 없음(렌더는 PlayMode FurnitureDeliveryZoneRenderTests).</summary>
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
        public void 표시는_채움_윤곽선_빛기둥_조명으로_이루어지고_충돌체가_없다()
        {
            FurnitureDeliveryZoneView view = CreateView(out GameObject zone);

            view.Rebuild();

            Transform root = zone.transform.Find("DeliveryZoneView");
            Assert.IsNotNull(root, "표시 루트가 없습니다.");
            Assert.IsNotNull(root.Find("Fill"), "바닥 채움");
            Assert.IsNotNull(root.Find("Pillars"), "모서리 빛기둥");
            Assert.IsNotNull(root.Find("OuterEdge"), "바깥 윤곽선");
            Assert.IsNotNull(root.Find("InnerEdge"), "안쪽 윤곽선");

            Light glow = root.Find("DeliveryGlow").GetComponent<Light>();
            Assert.AreEqual(LightType.Point, glow.type);
            Assert.AreEqual(LightShadows.None, glow.shadows, "그림자 없는 조명이어야 비용이 없다");
            Assert.Greater(glow.color.g, glow.color.r, "UI 활성 초록 계열이어야 합니다.");

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
        public void 빛기둥은_바닥에서_불투명하고_위로_투명해진다()
        {
            FurnitureDeliveryZoneView view = CreateView(out GameObject zone);
            view.Rebuild();

            Mesh pillars = zone.transform.Find("DeliveryZoneView/Pillars").GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = pillars.vertices;
            Color[] colors = pillars.colors;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            foreach (Vector3 vertex in vertices)
            {
                minY = Mathf.Min(minY, vertex.y);
                maxY = Mathf.Max(maxY, vertex.y);
            }

            for (int i = 0; i < vertices.Length; i++)
            {
                bool bottom = Mathf.Approximately(vertices[i].y, minY);
                Assert.AreEqual(bottom ? 0.75f : 0f, colors[i].a, 0.001f, $"정점 {i}");
            }

            Assert.Greater(maxY - minY, 1.5f, "기둥은 사람 키보다 높아야 멀리서 보인다.");
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
