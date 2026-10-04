using System.Collections.Generic;
using System.Linq;
using GhostHunter.Gameplay.FurnitureDriver;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Map;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 반출 구역·조립 영역은 드릴카 <b>오른쪽 밖</b> 땅에 있다(2026-10-04 사용자 요청, stage-system.md §2.1) —
    /// 드릴카(램프 포함)·실내 안전 구역과 겹치지 않고, 서로 겹치지 않으며, 드릴카와 같은 땅 높이이고, 계단·타이어 같은
    /// 장애물이 구역 안에 서 있지 않다.
    /// </summary>
    public sealed class DrillCarZoneLayoutTests
    {
        private const float GroundTolerance = 0.05f;
        // 주차선처럼 땅에 붙은 얇은 장식은 장애물로 치지 않는다.
        private const float ObstacleClearance = 0.2f;
        private const float MinimumInteriorWidth = 3.6f;

        [TestCase("Assets/Scenes/Stage1.unity")]
        [TestCase("Assets/Scenes/Tutorial.unity")]
        public void 반출_구역과_조립_영역은_드릴카_밖_땅에_있다(string scenePath)
        {
            // 다른 물리 테스트가 원점 근처에 상자를 만들고 검사하므로, 씬은 추가로 열고 끝나면 반드시 닫는다.
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool openedHere = !scene.isLoaded;
            if (openedHere)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                Physics.SyncTransforms();
                AssertLayout(scene, scenePath);
            }
            finally
            {
                if (openedHere && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
                Physics.SyncTransforms();
            }
        }

        private static void AssertLayout(Scene scene, string scenePath)
        {
            DrillCarSafeZone safe = Find<DrillCarSafeZone>(scene);
            Assert.IsNotNull(safe, $"{scenePath} 에 드릴카 안전 구역이 없습니다.");
            Transform car = safe.transform.root;
            float ground = car.position.y;
            Bounds carBounds = UnionOfRenderers(car);
            Bounds interior = OrientedBoxBounds(safe.transform, safe.Size);

            FurnitureDeliveryZone delivery = Find<FurnitureDeliveryZone>(scene);
            FurnitureAssemblyZone assembly = Find<FurnitureAssemblyZone>(scene);
            Assert.IsNotNull(delivery, "반출 구역이 없습니다.");
            Assert.IsNotNull(assembly, "조립 영역이 없습니다.");
            Bounds deliveryBounds = OrientedBoxBounds(delivery.transform, delivery.Size);
            Bounds assemblyBounds = assembly.TriggerBounds;

            Assert.Greater(car.InverseTransformPoint(delivery.transform.position).x, 0f,
                "반출 구역이 드릴카 기준 오른쪽(+X)에 없습니다.");
            Assert.Greater(car.InverseTransformPoint(assembly.transform.position).x, 0f,
                "조립 영역이 드릴카 기준 오른쪽(+X)에 없습니다.");

            Assert.IsFalse(deliveryBounds.Intersects(carBounds), $"반출 구역 {deliveryBounds} 이 드릴카 {carBounds} 와 겹칩니다.");
            Assert.IsFalse(assemblyBounds.Intersects(carBounds), $"조립 영역 {assemblyBounds} 이 드릴카 {carBounds} 와 겹칩니다.");
            Assert.IsFalse(deliveryBounds.Intersects(interior), "반출 구역이 실내 안전 구역과 겹칩니다.");
            Assert.IsFalse(assemblyBounds.Intersects(interior), "조립 영역이 실내 안전 구역과 겹칩니다.");
            Assert.IsFalse(deliveryBounds.Intersects(assemblyBounds), "반출 구역과 조립 영역이 겹칩니다.");

            Assert.AreEqual(ground, assembly.FloorHeight, GroundTolerance, "조립 영역 바닥이 드릴카가 선 땅 높이가 아닙니다.");
            Assert.LessOrEqual(deliveryBounds.min.y, ground, "반출 구역이 땅까지 내려오지 않아 바닥에 놓인 가구를 못 셉니다.");
            AssertGroundBelow(delivery.transform.position, ground, "반출 구역");
            AssertGroundBelow(assembly.AimPoint, ground, "조립 영역");

            AssertNoObstacle(deliveryBounds, ground, "반출 구역");
            AssertNoObstacle(assemblyBounds, ground, "조립 영역");

            // 2026-10-04 사용자 요청으로 1.4배(임포트 0.9 → 1.26). 커진 차체가 주변과 겹치지 않고 실내가 실제로 넓다.
            AssertNoObstacle(carBounds, ground, "드릴카", car);
            Assert.GreaterOrEqual(safe.Size.x, MinimumInteriorWidth, "드릴카 실내 폭이 좁습니다.");
            Vector3 probe = safe.ExitTerminalAnchor.position - safe.transform.forward * 0.8f;
            Assert.IsTrue(Physics.Raycast(probe, Vector3.down, out RaycastHit floor, 4f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), "종료 단말기 앞 바닥이 없습니다.");
            Assert.IsTrue(floor.collider.transform.IsChildOf(car), "종료 단말기 앞 바닥이 드릴카 모델이어야 합니다.");
            Assert.IsTrue(interior.Contains(floor.point + Vector3.up * 0.02f),
                "실제 실내 바닥에 선 플레이어 발 위치가 안전 구역에서 빠집니다.");
            Assert.IsFalse(interior.Contains(floor.point - Vector3.up * 0.2f), "차체 바닥 아래까지 안전 구역에 포함되면 안 됩니다.");
        }

        private static T Find<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .FirstOrDefault();
        }

        private static void AssertGroundBelow(Vector3 point, float ground, string label)
        {
            Assert.IsTrue(Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 10f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), $"{label} 아래에 땅이 없습니다.");
            Assert.AreEqual(ground, hit.point.y, GroundTolerance,
                $"{label} 아래 땅({hit.collider.name})이 드릴카가 선 높이가 아닙니다 — 단 위나 구덩이에 걸쳐 있습니다.");
        }

        private static void AssertNoObstacle(Bounds zone, float ground, string label, Transform ignoreRoot = null)
        {
            float bottom = ground + ObstacleClearance;
            if (zone.max.y <= bottom)
                return;

            var center = new Vector3(zone.center.x, (bottom + zone.max.y) * 0.5f, zone.center.z);
            var half = new Vector3(zone.extents.x, (zone.max.y - bottom) * 0.5f, zone.extents.z);
            List<string> blockers = Physics.OverlapBox(center, half, Quaternion.identity,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                .Where(c => ignoreRoot == null || !c.transform.IsChildOf(ignoreRoot))
                .Select(c => c.name)
                .ToList();
            Assert.IsEmpty(blockers, $"{label} 안에 장애물이 있습니다: {string.Join(", ", blockers)}");
        }

        private static Bounds UnionOfRenderers(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            Assert.IsNotEmpty(renderers, "드릴카 모델 렌더러가 없습니다.");
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static Bounds OrientedBoxBounds(Transform box, Vector3 size)
        {
            var bounds = new Bounds(box.position, Vector3.zero);
            Vector3 half = size * 0.5f;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? -half.x : half.x,
                    (i & 2) == 0 ? -half.y : half.y,
                    (i & 4) == 0 ? -half.z : half.z);
                bounds.Encapsulate(box.TransformPoint(corner));
            }

            return bounds;
        }
    }
}
