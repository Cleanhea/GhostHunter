using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Map;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 반출한 가구마다 드릴카 화물칸에 짐이 하나씩 쌓이는 연출(DeliveredCargoPile, stage-system.md §2.1) —
    /// 쌓는 규칙, 개수 동기화, 그리고 Stage1·Tutorial 의 화물칸 안에 실제로 들어가는지.
    /// </summary>
    public sealed class DeliveredCargoPileTests
    {
        private const string SettingsPath = "Assets/Settings/Gameplay/DeliveredCargoSettings_Default.asset";
        private const float FloorTolerance = 0.05f;

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

        private static DeliveredCargoSettings LoadSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<DeliveredCargoSettings>(SettingsPath);
            Assert.IsNotNull(settings, $"{SettingsPath} 가 없습니다.");
            Assert.IsNotNull(settings.Bundle, "짐더미 설정이 연결되지 않았습니다.");
            return settings;
        }

        private static DeliveredCargoLayout NewLayout(DeliveredCargoSettings settings, Vector3 area) => new(
            area, settings.CrateSizes, settings.Seed, settings.YawJitterDegrees, settings.OffsetJitter);

        [Test]
        public void 짐은_구역_안에_서로_겹치지_않게_쌓인다()
        {
            DeliveredCargoSettings settings = LoadSettings();
            var area = new Vector3(3.2f, 2.4f, 1.5f);
            DeliveredCargoLayout layout = NewLayout(settings, area);
            var boxes = new List<Bounds>();

            for (int i = 0; i < 16; i++)
            {
                Assert.IsTrue(layout.TryPlaceNext(out CargoPlacement placement), $"{i}번째 짐이 구역에 안 들어갑니다.");
                Vector3 size = settings.CrateSizes[placement.Variant];
                var box = new Bounds(placement.Center, size);
                Assert.GreaterOrEqual(box.min.y, -0.0001f, "짐이 바닥 아래로 내려갑니다.");
                Assert.LessOrEqual(box.max.y, area.y + 0.0001f, "짐이 구역 높이를 넘습니다.");
                Assert.GreaterOrEqual(box.min.x, -area.x * 0.5f, "짐이 구역 왼쪽을 넘습니다.");
                Assert.LessOrEqual(box.max.x, area.x * 0.5f, "짐이 구역 오른쪽을 넘습니다.");
                Assert.GreaterOrEqual(box.min.z, -area.z * 0.5f, "짐이 구역 뒤를 넘습니다.");
                Assert.LessOrEqual(box.max.z, area.z * 0.5f, "짐이 구역 앞을 넘습니다.");
                foreach (Bounds other in boxes)
                {
                    Vector3 overlap = Vector3.Min(box.max, other.max) - Vector3.Max(box.min, other.min);
                    Assert.IsFalse(overlap.x > 0.001f && overlap.y > 0.001f && overlap.z > 0.001f,
                        $"{i}번째 짐이 앞 짐과 겹칩니다.");
                }

                boxes.Add(box);
            }
        }

        [Test]
        public void 짐은_가장_낮은_칸부터_채워_한_층씩_오른다()
        {
            DeliveredCargoSettings settings = LoadSettings();
            DeliveredCargoLayout layout = NewLayout(settings, new Vector3(3.2f, 2.4f, 1.5f));

            var firstLayer = new List<float>();
            for (int i = 0; i < layout.CellCount; i++)
            {
                Assert.IsTrue(layout.TryPlaceNext(out CargoPlacement placement));
                firstLayer.Add(placement.Center.y - settings.CrateSizes[placement.Variant].y * 0.5f);
            }

            Assert.IsTrue(firstLayer.All(bottom => Mathf.Abs(bottom) < 0.0001f),
                "칸마다 한 개씩 바닥에 먼저 놓여야 합니다.");

            Assert.IsTrue(layout.TryPlaceNext(out CargoPlacement next));
            Assert.Greater(next.Center.y - settings.CrateSizes[next.Variant].y * 0.5f, 0.5f,
                "바닥 층이 다 차면 그 위에 올라야 합니다.");
        }

        [Test]
        public void 같은_시드면_같은_더미가_되고_개수가_늘어도_앞_짐은_움직이지_않는다()
        {
            DeliveredCargoSettings settings = LoadSettings();
            var area = new Vector3(3.2f, 2.4f, 1.5f);
            DeliveredCargoLayout longer = NewLayout(settings, area);
            DeliveredCargoLayout shorter = NewLayout(settings, area);

            for (int i = 0; i < 10; i++)
            {
                longer.TryPlaceNext(out CargoPlacement a);
                if (i >= 6)
                    continue;

                shorter.TryPlaceNext(out CargoPlacement b);
                Assert.AreEqual(a.Center, b.Center, $"{i}번째 짐 위치가 접속자마다 다릅니다.");
                Assert.AreEqual(a.YawDegrees, b.YawDegrees, 0.0001f);
                Assert.AreEqual(a.Variant, b.Variant);
            }
        }

        [Test]
        public void 구역이_꽉_차면_더_올리지_않고_짐_번호만_넘어간다()
        {
            DeliveredCargoSettings settings = LoadSettings();
            DeliveredCargoLayout layout = NewLayout(settings, new Vector3(3.2f, 1.0f, 1.5f));

            int placed = 0;
            for (int i = 0; i < 40; i++)
                if (layout.TryPlaceNext(out _))
                    placed++;

            Assert.Greater(placed, 0);
            Assert.Less(placed, 40, "높이 한계를 넘어 쌓았습니다.");
            Assert.AreEqual(40, layout.Placed);
        }

        [Test]
        public void 반출_수에_맞춰_짐이_늘고_줄고_충돌체가_없다()
        {
            DeliveredCargoSettings settings = LoadSettings();
            var go = new GameObject("CargoPileTest");
            _created.Add(go);
            var pile = go.AddComponent<DeliveredCargoPile>();
            typeof(DeliveredCargoPile).GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(pile, settings);

            pile.SyncTo(3);
            Assert.AreEqual(3, pile.CrateCount);
            pile.SyncTo(7);
            Assert.AreEqual(7, pile.CrateCount);
            pile.SyncTo(7);
            Assert.AreEqual(7, pile.CrateCount, "같은 수로 다시 맞추면 변하지 않아야 합니다.");

            Assert.IsEmpty(go.GetComponentsInChildren<Collider>(true), "짐은 시각 연출이라 충돌체가 없어야 합니다.");
            foreach (Transform child in go.transform)
                Assert.AreEqual(Vector3.one, child.localScale, "짐 스케일은 1이어야 합니다.");

            pile.SyncTo(2);
            Assert.AreEqual(2, pile.CrateCount, "반출 기록이 줄면 다시 쌓아야 합니다.");
            Assert.AreEqual(2, go.transform.childCount);
            pile.SyncTo(0);
            Assert.AreEqual(0, pile.CrateCount);
        }

        [TestCase("Assets/Scenes/Stage1.unity")]
        [TestCase("Assets/Scenes/Tutorial.unity")]
        public void 짐더미_구역은_드릴카_앞쪽_화물칸_바닥_위에_들어간다(string scenePath)
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool openedHere = !scene.isLoaded;
            if (openedHere)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                Physics.SyncTransforms();
                DeliveredCargoPile pile = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<DeliveredCargoPile>(true))
                    .FirstOrDefault();
                Assert.IsNotNull(pile, $"{scenePath} 의 드릴카에 짐더미 구역이 없습니다.");
                DrillCarSafeZone safe = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<DrillCarSafeZone>(true))
                    .First();

                // 실내 상자 안, 그리고 칸막이(실내 뒤쪽 방과 화물칸 사이)보다 앞쪽(+Z)이어야 한다.
                Vector3 half = pile.AreaSize * 0.5f;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? -half.x : half.x,
                        (i & 2) == 0 ? 0f : pile.AreaSize.y,
                        (i & 4) == 0 ? -half.z : half.z);
                    Vector3 inSafe = safe.transform.InverseTransformPoint(pile.transform.TransformPoint(corner));
                    Vector3 safeHalf = safe.Size * 0.5f;
                    Assert.LessOrEqual(Mathf.Abs(inSafe.x), safeHalf.x, $"짐더미 모서리 {i} 가 실내 좌우 밖입니다.");
                    Assert.LessOrEqual(inSafe.y, safeHalf.y, $"짐더미 모서리 {i} 가 실내 천장 위입니다.");
                    Assert.LessOrEqual(inSafe.z, safeHalf.z, $"짐더미 모서리 {i} 가 실내 앞벽 밖입니다.");
                }

                Vector3 pivot = pile.transform.position;
                Assert.IsTrue(Physics.Raycast(pivot + Vector3.up * 0.3f, Vector3.down, out RaycastHit floor, 1f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), "화물칸 바닥이 없습니다.");
                Assert.IsTrue(floor.collider.transform.IsChildOf(safe.transform.root), "바닥이 드릴카 모델이어야 합니다.");
                Assert.AreEqual(pivot.y, floor.point.y, FloorTolerance, "짐더미 바닥이 화물칸 바닥 높이가 아닙니다.");

                // 상자 안에 벽·칸막이·장비가 없어야 짐이 파묻히지 않는다(바닥 5cm 위부터).
                Vector3 center = pivot + pile.transform.up * (0.05f + (pile.AreaSize.y - 0.05f) * 0.5f);
                Vector3 extents = new(half.x, (pile.AreaSize.y - 0.05f) * 0.5f, half.z);
                List<string> blockers = Physics.OverlapBox(center, extents, pile.transform.rotation,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    .Select(c => c.name)
                    .ToList();
                Assert.IsEmpty(blockers, $"짐더미 구역 안에 장애물이 있습니다: {string.Join(", ", blockers)}");
            }
            finally
            {
                if (openedHere && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
                Physics.SyncTransforms();
            }
        }
    }
}
