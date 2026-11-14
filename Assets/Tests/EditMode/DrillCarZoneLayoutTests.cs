using System.Collections.Generic;
using System.Linq;
using GhostHunter.Gameplay.FurnitureDriver;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Interaction;
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

        /// <summary>
        /// 2026-11-12: 드릴 메시 콜라이더는 윗면이 단면이라 위에서 보면 없는 셈이었다 — 위에서 드릴 쪽으로 떨어진 플레이어가
        /// 드릴 속 나선 홈(폭 0.5~0.9m, 깊이 0.9m의 V자)에 빠져 본체 앞벽·가파른 홈 벽 때문에 나오지 못했다.
        /// 드릴 콜라이더를 Convex(솔리드 외피)로 두고, 윗면 위에서 떨어뜨린 플레이어 캡슐이 홈 없이 윗면에 앉는지 본다.
        /// </summary>
        [TestCase("Assets/Scenes/Stage1.unity")]
        [TestCase("Assets/Scenes/Tutorial.unity")]
        public void 드릴_윗면은_홈_없이_솔리드라_위에서_떨어져도_빠지지_않는다(string scenePath)
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool openedHere = !scene.isLoaded;
            if (openedHere)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                Physics.SyncTransforms();
                Transform car = Find<DrillCarSafeZone>(scene).transform.root;
                MeshCollider drill = car.GetComponentsInChildren<MeshCollider>(true).FirstOrDefault(c => c.name == "DrillCar_Drill");
                Assert.IsNotNull(drill, "드릴 콜라이더가 없습니다.");
                Assert.IsTrue(drill.convex, "드릴 콜라이더는 Convex 여야 합니다(단면 메시라 위에서 빠진다).");

                // 드릴 윗면 위(차 로컬 x ±0.4, z 4.4~6.0)에서 플레이어 캡슐(r 0.3, 높이 1.3)을 떨어뜨려 착지 높이를 잰다.
                const float radius = 0.3f;
                const float height = 1.3f;
                float previous = float.NaN;
                for (float z = 4.4f; z <= 6.0f; z += 0.1f)
                {
                    for (float x = -0.4f; x <= 0.41f; x += 0.1f)
                    {
                        Vector3 feet = car.TransformPoint(new Vector3(x, 9f, z));
                        Assert.IsTrue(Physics.CapsuleCast(feet + car.up * radius, feet + car.up * (height - radius), radius,
                            -car.up, out RaycastHit hit, 30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore),
                            $"드릴 위 ({x:F1}, {z:F1}) 에 착지할 면이 없습니다.");
                        float landed = car.InverseTransformPoint(feet - car.up * hit.distance).y;
                        // 이전엔 중앙 홈에서 1.4m 까지 가라앉았다(윗면 약 3.0~3.3m).
                        Assert.GreaterOrEqual(landed, 2.3f, $"드릴 위 ({x:F1}, {z:F1}) 에서 {landed:F2}m 까지 가라앉습니다 — 홈이 있습니다.");
                        if (!float.IsNaN(previous) && Mathf.Abs(x + 0.4f) > 0.001f)
                            Assert.LessOrEqual(Mathf.Abs(landed - previous), 0.15f, $"드릴 위 ({x:F1}, {z:F1}) 에 가파른 홈 벽이 있습니다.");
                        previous = landed;
                    }
                    previous = float.NaN;
                }
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
                // 배터리 팩(2026-10-29)은 일부러 드릴카 실내 벽 앞에 놓은 오브젝트다.
                .Where(c => c.GetComponentInParent<BatteryPack>() == null)
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
