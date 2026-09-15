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
            SetPrivateField(physics, "_definition", Track(ScriptableObject.CreateInstance<FurnitureDefinition>()));
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
