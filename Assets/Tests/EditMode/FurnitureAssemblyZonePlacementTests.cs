using GhostHunter.EditorTools;
using GhostHunter.Gameplay.FurnitureDriver;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 조립 영역 트리거가 지면에 붙어 있는지 검증한다.
    ///
    /// <para>회귀 대상(2026-09-17) — 설치 도구가 <c>DrillCarSafeZone</c>의 좌표를 그대로 복사했는데,
    /// 세이프 존은 오브젝트 위치가 상자 <b>중심</b>이라 트리거가 지면 1.5m 위(월드 y 1.5~4.0)에
    /// 떴다. 바닥에 놓인 부품이 영역에 들어오지 못해 상태가 <c>Empty</c>에서 바뀌지 않았고,
    /// 조립이 오류 하나 없이 영원히 시작되지 않았다.</para>
    /// </summary>
    public sealed class FurnitureAssemblyZonePlacementTests
    {
        // 2026-09-12 설치 당시의 실제 씬 값 — 이 조합이 버그를 만들었다.
        private static readonly Vector3 SafeZoneCenter = new(0.975f, 1.5f, -9.74f);
        private static readonly Vector3 SafeZoneSize = new(4f, 3f, 4f);

        [Test]
        public void 조립_영역은_세이프_존_상자의_바닥에_놓인다()
        {
            Vector3 origin = FurnitureMultiDriverSetup.AssemblyZoneOrigin(SafeZoneCenter, SafeZoneSize);
            Assert.That(origin.y, Is.EqualTo(0f).Within(0.0001f), "세이프 존 중심(1.5) − 높이 절반(1.5) = 지면");
            Assert.That(origin.x, Is.EqualTo(SafeZoneCenter.x).Within(0.0001f));
            Assert.That(origin.z, Is.EqualTo(SafeZoneCenter.z).Within(0.0001f));
        }

        [Test]
        public void 트리거_바닥이_지면과_같다()
        {
            float groundY = FurnitureMultiDriverSetup.AssemblyZoneOrigin(SafeZoneCenter, SafeZoneSize).y;
            float bottomY = FurnitureMultiDriverSetup.AssemblyTriggerBottom(groundY,
                FurnitureMultiDriverSetup.AssemblyTriggerCenter, FurnitureMultiDriverSetup.AssemblyTriggerSize);
            Assert.That(bottomY, Is.EqualTo(groundY).Within(0.0001f));
        }

        [Test]
        public void 세이프_존_좌표를_그대로_쓰면_침대_부품이_닿지_못한다()
        {
            // 수정 전 동작의 재현 — 회귀하면 이 기대값이 깨지도록 남긴다.
            float wrongBottom = FurnitureMultiDriverSetup.AssemblyTriggerBottom(SafeZoneCenter.y,
                FurnitureMultiDriverSetup.AssemblyTriggerCenter, FurnitureMultiDriverSetup.AssemblyTriggerSize);
            const float tallestBedPartHeight = 0.9f; // BedHead. 매트리스 0.25 · 다리 0.15
            Assert.That(wrongBottom, Is.GreaterThan(tallestBedPartHeight),
                "이 값이 부품 높이보다 낮아졌다면 버그 재현 조건이 바뀐 것이다 — 테스트를 갱신한다");

            float fixedBottom = FurnitureMultiDriverSetup.AssemblyTriggerBottom(
                FurnitureMultiDriverSetup.AssemblyZoneOrigin(SafeZoneCenter, SafeZoneSize).y,
                FurnitureMultiDriverSetup.AssemblyTriggerCenter, FurnitureMultiDriverSetup.AssemblyTriggerSize);
            Assert.That(fixedBottom, Is.LessThan(tallestBedPartHeight), "고친 뒤에는 바닥 부품이 영역에 들어와야 한다");
        }

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
            Vector3 eye = new(SafeZoneCenter.x + 1.2f, 1.6f, SafeZoneCenter.z);
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
            root.transform.position = FurnitureMultiDriverSetup.AssemblyZoneOrigin(SafeZoneCenter, SafeZoneSize);
            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = FurnitureMultiDriverSetup.AssemblyTriggerSize;
            trigger.center = FurnitureMultiDriverSetup.AssemblyTriggerCenter;
            zone = root.AddComponent<FurnitureAssemblyZone>();
            zone.Configure(null, trigger);
        }
    }
}
