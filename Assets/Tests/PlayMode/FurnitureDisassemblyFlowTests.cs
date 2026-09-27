using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GhostHunter.Core;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.FurnitureDriver;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Player;
using GhostHunter.UI;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>Local Host에서 분해 완료·거절·취소와 가구 풀의 상태 전환을 검증한다.</summary>
    public sealed class FurnitureDisassemblyFlowTests : NetworkFurnitureFixture
    {
        private readonly List<Object> _created = new();
        private readonly List<FurnitureDriverPoolItem> _parts = new();
        private LocalPlayerContext _context;
        private PlayerFurnitureDriverController _driver;
        private PlayerCleaningController _cleaning;
        private QuickSlotLoadout _loadout;
        private PlayerInputReader _input;
        private FurnitureDriverPoolItem _large;
        private uint _nextHash = 0x5F300001;

        [UnityTest]
        public IEnumerator 부품_파손은_트리거_이탈_콜백_없이도_조립_영역에서_즉시_제외된다()
        {
            CreateScenario();
            var part = _parts[0];
            part.ServerActivate(Vector3.up, Quaternion.identity, 3);
            var instance = Track(new GameObject("TestAssemblyZone"));
            instance.SetActive(false);
            var networkObject = instance.AddComponent<NetworkObject>();
            AssignHash(networkObject);
            var trigger = instance.AddComponent<BoxCollider>();
            trigger.size = Vector3.one * 5f;
            var zone = instance.AddComponent<FurnitureAssemblyZone>();
            zone.Configure(GetPrivateField<FurnitureDriverCatalog>(_driver, "_catalog"), trigger);
            instance.SetActive(true);
            networkObject.Spawn();
            InvokePrivate(zone, "OnTriggerEnter", part.GetComponent<Collider>());
            var candidates = GetPrivateField<HashSet<FurnitureDriverPoolItem>>(zone, "_candidates");
            Assert.IsTrue(candidates.Contains(part));
            var physics = part.GetComponent<FurnitureNetworkPhysics>();
            var partDefinition = Track(ScriptableObject.CreateInstance<FurnitureDefinition>());
            // 사라짐은 기본값이 꺼짐(FD-10 재확정 2026-09-16) — 파손 경로 검증에만 켠다.
            SetPrivateField(partDefinition, "_destroyAtZeroDurability", true);
            SetPrivateField(physics, "_definition", partDefinition);
            SetPrivateField(physics, "_protectedUntil", 0d);
            physics.ServerApplyCollisionSpeed(20f);
            Assert.IsFalse(candidates.Contains(part));
            yield return null;
        }

        [UnityTest]
        public IEnumerator 파손된_가구는_풀에서_재사용되지_않고_개발_복구로만_되살아난다()
        {
            CreateScenario();
            var physics = _large.GetComponent<FurnitureNetworkPhysics>();
            var definition = Track(ScriptableObject.CreateInstance<FurnitureDefinition>());
            SetPrivateField(definition, "_destroyAtZeroDurability", true);
            SetPrivateField(physics, "_definition", definition);
            SetPrivateField(physics, "_protectedUntil", 0d);
            physics.ServerSetDurability(3);
            physics.ServerApplyCollisionSpeed(20f);
            Assert.IsTrue(_large.IsBroken);
            Assert.IsFalse(_large.IsActive);
            Assert.IsFalse(FurnitureDriverPoolItem.TryFindInactive("TestLarge", out _));
            Assert.IsFalse(_large.ServerActivate(Vector3.up, Quaternion.identity, 100));
            physics.ServerResetDurability(true);
            Assert.IsTrue(_large.IsActive);
            Assert.AreEqual(100, _large.Durability);
            Assert.IsTrue(_large.GetComponent<Collider>().enabled);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 비활성_분해_부품은_전체_내구도_복구로_나타나지_않는다()
        {
            CreateScenario();
            FurnitureNetworkPhysics.ServerResetAll(true);
            foreach (var part in _parts)
            {
                Assert.IsFalse(part.IsActive);
                Assert.IsFalse(part.GetComponent<Renderer>().enabled);
                Assert.IsFalse(part.GetComponent<Collider>().enabled);
                Assert.AreEqual(100, part.Durability);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator 배치된_가구는_스폰부터_보이고_잡을_수_있다()
        {
            CreateScenario();
            Assert.IsTrue(_large.IsActive);
            Assert.IsTrue(_large.GetComponent<Renderer>().enabled);
            Assert.IsTrue(_large.GetComponent<Collider>().enabled);
            Assert.IsTrue(_large.GetComponent<FurnitureGrabTarget>().CanGrab(HostClientId));
            yield return null;
        }

        [UnityTest]
        public IEnumerator 대기_부품은_숨겨지고_잡을_수_없다()
        {
            CreateScenario();
            foreach (FurnitureDriverPoolItem part in _parts)
            {
                Assert.IsFalse(part.IsActive);
                Assert.IsFalse(part.GetComponent<Renderer>().enabled);
                Assert.IsFalse(part.GetComponent<Collider>().enabled);
                Assert.IsFalse(part.GetComponent<FurnitureGrabTarget>().CanGrab(HostClientId));
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator 분해_성공은_원본을_숨기고_내구도를_상속한_부품으로_바꾼다()
        {
            CreateScenario();
            _large.ServerActivate(_large.transform.position, Quaternion.identity, 60);
            RequestDisassemble();
            Assert.IsFalse(_large.IsActive);
            Assert.AreEqual(95, _driver.ItemDurability);
            foreach (FurnitureDriverPoolItem part in _parts)
            {
                Assert.IsTrue(part.IsActive);
                Assert.AreEqual(60, part.Durability);
                Assert.IsTrue(part.GetComponent<Collider>().enabled);
                Assert.IsFalse(part.GetComponent<Rigidbody>().isKinematic);
                Assert.IsTrue(part.GetComponent<Rigidbody>().useGravity);
                Assert.AreEqual(2f, part.transform.position.y, 0.001f);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator 중복_완료_요청은_내구도를_추가로_깎지_않는다()
        {
            CreateScenario();
            RequestDisassemble();
            RequestDisassemble();
            Assert.AreEqual(95, _driver.ItemDurability);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 미장착_완료_요청은_거절된다()
        {
            CreateScenario();
            SetPrivateField(_driver, "_serverSlot", -1);
            RequestDisassemble();
            AssertUnchanged();
            yield return null;
        }

        [UnityTest]
        public IEnumerator 거리_밖_완료_요청은_거절된다()
        {
            CreateScenario();
            _driver.transform.position = Vector3.right * 20f;
            RequestDisassemble();
            AssertUnchanged();
            yield return null;
        }

        [UnityTest]
        public IEnumerator 부품_부족이면_이미_활성화한_부품도_되돌린다()
        {
            CreateScenario(secondPartCount: 1);
            LogAssert.Expect(LogType.Warning, "[PlayerFurnitureDriverController] 부품 풀 부족: TestPartB");
            RequestDisassemble();
            AssertUnchanged();
            yield return null;
        }

        [UnityTest]
        public IEnumerator 행동_시간이_끝나야_분해가_완료된다()
        {
            CreateScenario();
            BeginDisassemble();
            HoldUseDriver(true);
            Assert.AreEqual(3f, _driver.ActionSecondsTotal);
            float started = Time.time;
            while (_driver.CurrentAction != FurnitureDriverActionKind.None && Time.time - started < 5f)
            {
                if (Time.time - started < 2.5f)
                    Assert.IsTrue(_large.IsActive, "3초 행동 완료 전에 분해됐습니다.");
                yield return null;
                InvokePrivate(_driver, "TickAction");
            }
            Assert.IsFalse(_large.IsActive, "행동 완료 후에도 원본이 활성 상태입니다.");
            Assert.AreEqual(95, _driver.ItemDurability);
        }

        [UnityTest]
        public IEnumerator 이동하면_분해를_취소하고_내구도를_유지한다()
        {
            CreateScenario();
            BeginDisassemble();
            HoldUseDriver(true);
            SetPrivateField(_input, "<Move>k__BackingField", Vector2.up);
            InvokePrivate(_driver, "TickAction");
            Assert.AreEqual(FurnitureDriverActionKind.None, _driver.CurrentAction);
            AssertUnchanged();
            yield return null;
        }

        [UnityTest]
        public IEnumerator 우클릭을_떼면_분해를_취소하고_내구도를_유지한다()
        {
            CreateScenario();
            BeginDisassemble();
            HoldUseDriver(true);
            InvokePrivate(_driver, "TickAction");
            Assert.AreEqual(FurnitureDriverActionKind.Disassemble, _driver.CurrentAction,
                "우클릭을 누르고 있는 동안에는 행동이 이어져야 합니다.");
            HoldUseDriver(false);
            InvokePrivate(_driver, "TickAction");
            Assert.AreEqual(FurnitureDriverActionKind.None, _driver.CurrentAction);
            AssertUnchanged();
            yield return null;
        }

        [UnityTest]
        public IEnumerator 행동_중에는_중앙_원형_게이지가_진행도만큼_채워진다()
        {
            CreateScenario();
            FurnitureDriverActionHud hud = CreateActionHud();
            BeginDisassemble();
            SetPrivateField(_driver, "_actionTimer", 1.5f);
            yield return null;
            Assert.IsTrue(GetPrivateField<RectTransform>(hud, "_gaugeRoot").gameObject.activeSelf);
            Assert.AreEqual(0.5f, GetPrivateField<Image>(hud, "_fill").fillAmount, 0.01f);
            Assert.AreEqual("분해 중", GetPrivateField<Text>(hud, "_label").text);
        }

        [UnityTest]
        public IEnumerator 분해가_중단되면_게이지가_멈춘_채_실패_문구를_띄우고_사라진다()
        {
            CreateScenario();
            FurnitureDriverActionHud hud = CreateActionHud(failDisplaySeconds: 0.1f);
            yield return null;
            BeginDisassemble();
            SetPrivateField(_driver, "_actionTimer", 1.5f);
            HoldUseDriver(false);
            InvokePrivate(_driver, "TickAction");
            yield return null;

            RectTransform gauge = GetPrivateField<RectTransform>(hud, "_gaugeRoot");
            Assert.IsTrue(gauge.gameObject.activeSelf);
            Assert.AreEqual(0.5f, GetPrivateField<Image>(hud, "_fill").fillAmount, 0.01f);
            Assert.AreEqual("분해 실패", GetPrivateField<Text>(hud, "_label").text);

            float started = Time.unscaledTime;
            while (gauge.gameObject.activeSelf && Time.unscaledTime - started < 2f)
                yield return null;
            Assert.IsFalse(gauge.gameObject.activeSelf, "실패 연출이 끝나면 게이지가 사라져야 합니다.");
        }

        [UnityTest]
        public IEnumerator 대상이_먼저_분해되면_진행_중인_행동을_취소한다()
        {
            CreateScenario();
            BeginDisassemble();
            _large.ServerDeactivate();
            InvokePrivate(_driver, "TickAction");
            Assert.AreEqual(FurnitureDriverActionKind.None, _driver.CurrentAction);
            Assert.AreEqual(100, _driver.ItemDurability);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 잡힌_가구가_분해되면_숨겨진_원본에_홀더가_남지_않는다()
        {
            CreateScenario();
            FurnitureGrabTarget target = _large.GetComponent<FurnitureGrabTarget>();
            Assert.IsTrue(target.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection));
            RequestDisassemble();
            Assert.IsFalse(_large.IsActive);
            Assert.AreEqual(0, target.HolderCount);
            Assert.AreEqual(FurnitureState.Idle, target.State);
            Assert.AreEqual(0f, target.Charge);
            _large.ServerActivate(new Vector3(0f, 1f, 2f), Quaternion.identity, 60);
            Assert.IsTrue(target.CanGrab(HostClientId), "재활성화한 가구를 다시 잡을 수 있어야 합니다.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator 퀵슬롯에서_드라이버를_선택하면_실제로_장착된다()
        {
            CreateScenario(equipDriver: false);
            QuickSlotWheelUi wheel = CreateWheel();
            InvokePrivate(wheel, "Confirm", 0);
            Assert.IsTrue(_driver.IsDriverEquipped);
            Assert.IsTrue(_driver.ServerHasDriver);
            Assert.AreEqual(0, _cleaning.EquippedSlot);
            RequestDisassemble();
            Assert.IsFalse(_large.IsActive);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 대걸레로_바꾸면_드라이버를_해제하고_분해를_취소한다()
        {
            CreateScenario();
            QuickSlotWheelUi wheel = CreateWheel();
            BeginDisassemble();
            InvokePrivate(wheel, "Confirm", 2);
            Assert.IsTrue(_cleaning.ServerHasMop);
            Assert.IsFalse(_driver.IsDriverEquipped);
            Assert.IsFalse(_driver.ServerHasDriver);
            Assert.AreEqual(FurnitureDriverActionKind.None, _driver.CurrentAction);
            RequestDisassemble();
            AssertUnchanged();
            yield return null;
        }

        [UnityTest]
        public IEnumerator 맨손으로_바꾸면_두_도구가_모두_해제된다()
        {
            CreateScenario();
            QuickSlotWheelUi wheel = CreateWheel();
            InvokePrivate(wheel, "Confirm", 1);
            Assert.IsFalse(_cleaning.ServerHasMop);
            Assert.IsFalse(_driver.ServerHasDriver);
            RequestDisassemble();
            AssertUnchanged();
            yield return null;
        }

        [TearDown]
        public void DestroyDriverObjects()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            _created.Clear();
            _parts.Clear();
            if (_context != null)
                Services.Unbind<ILocalPlayerContext>(_context);
            _context = null;
        }

        [UnityTest]
        public IEnumerator 조립하면_완성_가구가_부품_내구도의_평균을_가진다()
        {
            // 기획서 §5 예시 — 60·60·30 → 50(소수점 버림).
            CreateScenario();
            FurnitureAssemblyZone zone = CreateZone();
            _large.ServerDeactivate();
            ActivateInZone(zone, _parts[0], 60);
            ActivateInZone(zone, _parts[1], 60);
            ActivateInZone(zone, _parts[2], 30);

            Assert.IsTrue(zone.ServerTryAssemble(0f, out FurnitureDisassemblyRecipe recipe, out int durability));
            Assert.AreEqual("TestLarge", recipe.LargeFurnitureId);
            Assert.AreEqual(50, durability);
            Assert.IsTrue(_large.IsActive);
            Assert.AreEqual(50, _large.Durability);
            foreach (FurnitureDriverPoolItem part in _parts)
                Assert.IsFalse(part.IsActive, "조립에 쓰인 부품은 비활성화됩니다.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator 내구도가_0인_부품도_조립에_쓰이고_평균이_그대로_적용된다()
        {
            // 사라짐이 꺼져 있으면(기본값) 0인 부품이 그대로 남아 조립 대상이 된다 — 2026-09-16.
            CreateScenario();
            FurnitureAssemblyZone zone = CreateZone();
            _large.ServerDeactivate();
            ActivateInZone(zone, _parts[0], 0);
            ActivateInZone(zone, _parts[1], 0);
            ActivateInZone(zone, _parts[2], 30);

            Assert.IsTrue(zone.ServerTryAssemble(0f, out _, out int durability));
            Assert.AreEqual(10, durability);
            Assert.IsTrue(_large.IsActive);
            Assert.AreEqual(10, _large.Durability);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 영역_안의_아무_재료나_조준해_조립한다()
        {
            // 부품 B(2개 중 두 번째)를 겨눈다 — 영역 중심이 아니라 재료 자체가 조준 대상이다.
            CreateScenario();
            FurnitureAssemblyZone zone = CreateZone();
            _large.ServerDeactivate();
            foreach (FurnitureDriverPoolItem part in _parts)
                ActivateInZone(zone, part, 80);
            Recompute(zone);

            AimAtPart(_parts[2]);
            Assert.AreEqual(FurnitureDriverActionKind.Assemble, _driver.CurrentAction);
            Assert.AreEqual(0, _driver.FeedbackSerial);

            RequestAssemble(zone, _parts[2]);
            Assert.IsTrue(_large.IsActive, "조준한 재료로 조립이 완료돼야 합니다.");
            Assert.AreEqual(80, _large.Durability);
            Assert.AreEqual(95, _driver.ItemDurability);
            foreach (FurnitureDriverPoolItem part in _parts)
                Assert.IsFalse(part.IsActive);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 재료가_부족하면_조립을_시작하지_않고_재료가_부족합니다를_띄운다()
        {
            CreateScenario();
            FurnitureDriverActionHud hud = CreateActionHud();
            FurnitureAssemblyZone zone = CreateZone();
            _large.ServerDeactivate();
            ActivateInZone(zone, _parts[0], 100);
            ActivateInZone(zone, _parts[1], 100);
            Recompute(zone);
            yield return null;

            AimAtPart(_parts[0]);
            Assert.AreEqual(FurnitureDriverActionKind.None, _driver.CurrentAction, "재료가 모자라면 게이지를 시작하지 않는다");
            Assert.AreEqual(FurnitureDriverFeedback.NotEnoughMaterials, _driver.LastFeedback);
            yield return null;

            Assert.IsTrue(GetPrivateField<RectTransform>(hud, "_gaugeRoot").gameObject.activeSelf);
            Assert.AreEqual("재료가 부족합니다", GetPrivateField<Text>(hud, "_label").text);
            Assert.IsFalse(GetPrivateField<Image>(hud, "_fill").enabled, "시작 못 한 행동이라 게이지 링은 숨긴다");
            Assert.AreEqual(100, _driver.ItemDurability);
        }

        [UnityTest]
        public IEnumerator 같은_재료가_너무_많으면_재료_구성이_맞지_않는다고_알린다()
        {
            CreateScenario(secondPartCount: 3);
            FurnitureAssemblyZone zone = CreateZone();
            _large.ServerDeactivate();
            foreach (FurnitureDriverPoolItem part in _parts)
                ActivateInZone(zone, part, 100);
            Recompute(zone);

            AimAtPart(_parts[0]);
            Assert.AreEqual(FurnitureDriverActionKind.None, _driver.CurrentAction);
            Assert.AreEqual(FurnitureDriverFeedback.MismatchedMaterials, _driver.LastFeedback);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 영역_밖의_재료를_조준하면_영역에_모아_달라고_알린다()
        {
            CreateScenario();
            CreateZone();
            _large.ServerDeactivate();
            Assert.IsTrue(_parts[0].ServerActivate(Vector3.up, Quaternion.identity, 100));

            AimAtPart(_parts[0]);
            Assert.AreEqual(FurnitureDriverActionKind.None, _driver.CurrentAction);
            Assert.AreEqual(FurnitureDriverFeedback.OutsideAssemblyZone, _driver.LastFeedback);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 영역에_완성된_큰_가구가_있어도_재료는_조립할_수_있다()
        {
            // 조립한 가구는 영역 가운데 떨어진다. 그 가구를 재료로 세면 영역이 혼입(붉은색)이 돼 다음 조립이 막혔다.
            CreateScenario();
            FurnitureAssemblyZone zone = CreateZone();
            InvokePrivate(zone, "OnTriggerEnter", _large.GetComponent<Collider>());
            FurnitureDriverPoolItem secondLarge = SpawnPoolItem("TestLarge", false);
            foreach (FurnitureDriverPoolItem part in _parts)
                ActivateInZone(zone, part, 100);
            Recompute(zone);

            Assert.AreEqual(FurnitureAssemblyState.Ready, zone.Silhouette);
            RequestAssemble(zone, _parts[0]);
            Assert.IsTrue(secondLarge.IsActive);
            Assert.IsTrue(_large.IsActive, "이미 있던 완성 가구는 그대로 남는다");
            yield return null;
        }

        [UnityTest]
        public IEnumerator 완료_순간_재료가_빠졌으면_서버가_거절하고_이유를_알린다()
        {
            CreateScenario();
            FurnitureAssemblyZone zone = CreateZone();
            _large.ServerDeactivate();
            foreach (FurnitureDriverPoolItem part in _parts)
                ActivateInZone(zone, part, 100);
            Recompute(zone);
            AimAtPart(_parts[0]);
            Assert.AreEqual(FurnitureDriverActionKind.Assemble, _driver.CurrentAction);

            // 게이지가 도는 동안 다른 플레이어가 재료 하나를 가져갔다(서버는 완료 시점에 다시 센다).
            _parts[2].ServerDeactivate();
            RequestAssemble(zone, _parts[0]);
            yield return null;

            Assert.IsFalse(_large.IsActive);
            Assert.AreEqual(100, _driver.ItemDurability, "거절된 조립은 드라이버 내구도를 깎지 않는다");
            Assert.AreEqual(FurnitureDriverFeedback.NotEnoughMaterials, _driver.LastFeedback);
        }

        [UnityTest]
        public IEnumerator 조립_영역을_바닥부터_그리고_판정_상태에_따라_색을_바꾼다()
        {
            CreateScenario();
            FurnitureAssemblyZone zone = CreateZone();
            var uiSettings = Track(ScriptableObject.CreateInstance<FurnitureDriverUiSettings>());
            var viewObject = Track(new GameObject("TestAssemblyZoneView"));
            viewObject.SetActive(false);
            var view = viewObject.AddComponent<FurnitureAssemblyZoneView>();
            SetPrivateField(view, "_uiSettings", uiSettings);
            viewObject.SetActive(true);
            yield return null;

            Transform drawn = zone.transform.Find("AssemblyZoneView");
            Assert.IsNotNull(drawn, "영역마다 표시가 생긴다");
            Assert.IsTrue(drawn.gameObject.activeSelf);
            LineRenderer[] lines = drawn.GetComponentsInChildren<LineRenderer>();
            Assert.AreEqual(6, lines.Length, "바닥·윗면 사각형과 세로 모서리 4개");
            // 트리거(5m 정육면체, 원점 중심)는 지면 아래로 2.5m 뻗지만 보이는 바닥에서 자른다.
            Assert.AreEqual(zone.FloorHeight + 0.02f, lines[0].GetPosition(0).y, 0.001f);
            Assert.AreEqual(zone.TriggerBounds.max.y, lines[1].GetPosition(0).y, 0.001f);
            AssertColor(uiSettings.ZoneColorFor(FurnitureAssemblyState.Empty), lines[0].startColor);

            _large.ServerDeactivate();
            foreach (FurnitureDriverPoolItem part in _parts)
                ActivateInZone(zone, part, 100);
            Recompute(zone);
            yield return null;
            AssertColor(uiSettings.ZoneColorFor(FurnitureAssemblyState.Ready), lines[0].startColor,
                "조립 가능하면 초록으로 바뀐다");

            SetPrivateField(uiSettings, "_showAssemblyZone", false);
            yield return null;
            Assert.IsFalse(drawn.gameObject.activeSelf, "설정으로 끌 수 있다");
        }

        // LineRenderer 는 색을 8비트로 저장한다(0.35 → 0.349).
        private static void AssertColor(Color expected, Color actual, string message = null)
        {
            Assert.AreEqual(expected.r, actual.r, 0.005f, message);
            Assert.AreEqual(expected.g, actual.g, 0.005f, message);
            Assert.AreEqual(expected.b, actual.b, 0.005f, message);
            Assert.AreEqual(expected.a, actual.a, 0.005f, message);
        }

        private void AimAtPart(FurnitureDriverPoolItem part) =>
            InvokePrivate(_driver, "TryBeginAssembleOnPart", part);

        private static void Recompute(FurnitureAssemblyZone zone) => InvokePrivate(zone, "RecomputeState");

        private void RequestAssemble(FurnitureAssemblyZone zone, FurnitureDriverPoolItem part) =>
            InvokePrivate(_driver, "RequestAssembleRpc", zone.NetworkObjectId, part.NetworkObjectId, default(RpcParams));

        /// <summary>조립 판정에 쓸 트리거 영역 하나를 만든다(실제 씬의 AssemblyZone과 같은 구성).</summary>
        private FurnitureAssemblyZone CreateZone()
        {
            var instance = Track(new GameObject("TestAssemblyZone"));
            instance.SetActive(false);
            var networkObject = instance.AddComponent<NetworkObject>();
            AssignHash(networkObject);
            var trigger = instance.AddComponent<BoxCollider>();
            trigger.size = Vector3.one * 5f;
            var zone = instance.AddComponent<FurnitureAssemblyZone>();
            zone.Configure(GetPrivateField<FurnitureDriverCatalog>(_driver, "_catalog"), trigger);
            instance.SetActive(true);
            networkObject.Spawn();
            return zone;
        }

        /// <summary>부품을 지정한 내구도로 활성화하고 영역 점유로 등록한다(트리거 콜백 대체).</summary>
        private void ActivateInZone(FurnitureAssemblyZone zone, FurnitureDriverPoolItem part, int durability)
        {
            Assert.IsTrue(part.ServerActivate(Vector3.up, Quaternion.identity, durability));
            Assert.AreEqual(durability, part.Durability);
            InvokePrivate(zone, "OnTriggerEnter", part.GetComponent<Collider>());
        }

        private void CreateScenario(int secondPartCount = 2, bool equipDriver = true)
        {
            var floor = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            floor.name = "TestDriverFloor";
            floor.transform.position = Vector3.down * 0.5f;
            floor.transform.localScale = new Vector3(20f, 1f, 20f);
            _context = new LocalPlayerContext();
            Services.Bind<ILocalPlayerContext>(_context);
            var settings = Track(ScriptableObject.CreateInstance<FurnitureDriverSettings>());
            var recipe = Track(ScriptableObject.CreateInstance<FurnitureDisassemblyRecipe>());
            recipe.Configure("TestLarge", "TestLarge", new[]
            {
                new FurniturePartRequirement("TestPartA", 1),
                new FurniturePartRequirement("TestPartB", 2),
            });
            var catalog = Track(ScriptableObject.CreateInstance<FurnitureDriverCatalog>());
            catalog.Configure(new[] { recipe });
            var item = Track(ScriptableObject.CreateInstance<QuickSlotItemDefinition>());
            SetPrivateField(item, "_isDriver", true);
            var bareHands = Track(ScriptableObject.CreateInstance<QuickSlotItemDefinition>());
            var mop = Track(ScriptableObject.CreateInstance<QuickSlotItemDefinition>());
            SetPrivateField(mop, "_isMop", true);
            _loadout = Track(ScriptableObject.CreateInstance<QuickSlotLoadout>());
            SetPrivateField(_loadout, "_slots", new[] { item, bareHands, mop });

            // 입력 장치는 건드리지 않고 입력 리더가 전달한 프레임 값을 주입한다.
            var inputObject = Track(new GameObject("TestDriverInput"));
            inputObject.AddComponent<NetworkObject>();
            _input = inputObject.AddComponent<PlayerInputReader>();
            _input.enabled = false;

            var player = Track(new GameObject("TestDriverPlayer"));
            player.SetActive(false);
            var networkObject = player.AddComponent<NetworkObject>();
            AssignHash(networkObject);
            _driver = player.AddComponent<PlayerFurnitureDriverController>();
            SetPrivateField(_driver, "_input", _input);
            SetPrivateField(_driver, "_settings", settings);
            SetPrivateField(_driver, "_catalog", catalog);
            SetPrivateField(_driver, "_loadout", _loadout);
            _cleaning = player.AddComponent<PlayerCleaningController>();
            SetPrivateField(_cleaning, "_input", _input);
            SetPrivateField(_cleaning, "_loadout", _loadout);
            player.SetActive(true);
            networkObject.SpawnWithOwnership(HostClientId);
            _driver.enabled = false;
            if (equipDriver)
            {
                Assert.IsTrue(_driver.EquipSlot(0));
                Assert.IsTrue(_driver.ServerHasDriver);
            }

            _large = SpawnPoolItem("TestLarge", true);
            _parts.Add(SpawnPoolItem("TestPartA", false));
            for (int i = 0; i < secondPartCount; i++)
                _parts.Add(SpawnPoolItem("TestPartB", false));
        }

        private FurnitureDriverPoolItem SpawnPoolItem(string key, bool startActive)
        {
            var instance = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            instance.name = key;
            instance.SetActive(false);
            instance.transform.position = new Vector3(0f, 1f, 2f);
            instance.AddComponent<Rigidbody>().isKinematic = true;
            var networkObject = instance.AddComponent<NetworkObject>();
            AssignHash(networkObject);
            instance.AddComponent<NetworkTransform>();
            var poolItem = instance.AddComponent<FurnitureDriverPoolItem>();
            poolItem.Configure(key);
            SetPrivateField(poolItem, "_startActive", startActive);
            var grabTarget = instance.AddComponent<FurnitureGrabTarget>();
            SetPrivateField(grabTarget, "_settings", Settings);
            instance.SetActive(true);
            networkObject.Spawn();
            return poolItem;
        }

        private QuickSlotWheelUi CreateWheel()
        {
            var settings = Track(ScriptableObject.CreateInstance<QuickSlotUiSettings>());
            var instance = Track(new GameObject("TestQuickSlotWheel"));
            instance.SetActive(false);
            var wheel = instance.AddComponent<QuickSlotWheelUi>();
            SetPrivateField(wheel, "_uiSettings", settings);
            SetPrivateField(wheel, "_loadout", _loadout);
            instance.SetActive(true);
            return wheel;
        }

        private void BeginDisassemble() => InvokePrivate(_driver, "BeginAction",
            FurnitureDriverActionKind.Disassemble, _large.NetworkObjectId);

        // 입력 리더가 꺼져 있으므로 우클릭 유지 상태를 프레임 값으로 직접 주입한다.
        private void HoldUseDriver(bool held) =>
            SetPrivateField(_input, "<UseDriverHeld>k__BackingField", held);

        private FurnitureDriverActionHud CreateActionHud(float failDisplaySeconds = 0.9f)
        {
            var settings = Track(ScriptableObject.CreateInstance<FurnitureDriverUiSettings>());
            SetPrivateField(settings, "_failDisplaySeconds", failDisplaySeconds);
            var instance = Track(new GameObject("TestDriverActionHud"));
            instance.SetActive(false);
            var hud = instance.AddComponent<FurnitureDriverActionHud>();
            SetPrivateField(hud, "_uiSettings", settings);
            instance.SetActive(true);
            return hud;
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"{target.GetType().Name}.{fieldName} 를 찾지 못했습니다.");
            return (T)field.GetValue(target);
        }

        // 실제 NGO 생성 RPC 래퍼를 통과한다. 입력 타겟팅·원격 클라이언트 검증과는 별개다.
        private void RequestDisassemble() => InvokePrivate(_driver, "RequestDisassembleRpc",
            _large.NetworkObjectId, default(RpcParams));

        private void AssertUnchanged()
        {
            Assert.IsTrue(_large.IsActive);
            Assert.AreEqual(100, _driver.ItemDurability);
            foreach (FurnitureDriverPoolItem part in _parts)
                Assert.IsFalse(part.IsActive);
        }

        private T Track<T>(T value) where T : Object
        {
            _created.Add(value);
            return value;
        }

        private void AssignHash(NetworkObject networkObject) => typeof(NetworkObject)
            .GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(networkObject, _nextHash++);

        private static void InvokePrivate(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(target, arguments);
        }
    }
}
