using GhostHunter.Gameplay.FurnitureDriver;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 조립 영역의 조준 기준점이 상자 중심인지 검증한다.
    ///
    /// <para>조립 영역은 오브젝트 원점이 지면이고 트리거 상자가 그 위로 선다(2026-09-17 회귀 수정 — 세이프 존
    /// 좌표를 그대로 복사해 트리거가 지면 1.5m 위에 떴던 버그). 조준 판정까지 바닥 원점을 쓰면 부품 옆에서
    /// 정면을 볼 때 원뿔을 벗어난다.</para>
    /// </summary>
    public sealed class FurnitureAssemblyZonePlacementTests
    {
        // 2026-09-12 설치 당시의 실제 씬 값 — 세이프 존 바닥(y 0)에 영역 원점을 둔다.
        private static readonly Vector3 ZoneOrigin = new(0.975f, 0f, -9.74f);
        private static readonly Vector3 TriggerSize = new(3f, 2.5f, 3f);
        private static readonly Vector3 TriggerCenter = new(0f, 1.25f, 0f);

        [Test]
        public void 조준점은_바닥이_아니라_상자_중심이다()
        {
            Zone(out GameObject root, out FurnitureAssemblyZone zone);
            try
            {
                Assert.That(zone.AimPoint.y, Is.EqualTo(1.25f).Within(0.001f), "상자 중심 높이");
                Assert.That(zone.transform.position.y, Is.EqualTo(0f).Within(0.001f), "오브젝트 원점은 바닥");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void 부품_바로_옆에_서서_정면을_봐도_조준_원뿔_안이다()
        {
            // 영역에서 수평 1.2m, 눈높이 1.6m, 정면(수평)을 보는 자세 — 부품을 내려놓은 직후의 자리다.
            Vector3 eye = new(ZoneOrigin.x + 1.2f, 1.6f, ZoneOrigin.z);
            Vector3 forward = Vector3.left;
            Zone(out GameObject root, out FurnitureAssemblyZone zone);
            try
            {
                float aimed = Vector3.Dot(forward, (zone.AimPoint - eye).normalized);
                Assert.That(aimed, Is.GreaterThan(0.7f), "상자 중심을 기준으로 하면 판정이 선다");

                // 회귀 고정 — 바닥 원점을 기준으로 삼으면 같은 자리에서 조립을 시작할 수 없었다.
                float atOrigin = Vector3.Dot(forward, (zone.transform.position - eye).normalized);
                Assert.That(atOrigin, Is.LessThan(0.7f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void Zone(out GameObject root, out FurnitureAssemblyZone zone)
        {
            root = new GameObject("AssemblyZoneTest");
            root.transform.position = ZoneOrigin;
            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = TriggerSize;
            trigger.center = TriggerCenter;
            zone = root.AddComponent<FurnitureAssemblyZone>();
            zone.Configure(null, trigger);
        }
    }
}
