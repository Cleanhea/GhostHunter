using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GhostHunter.Core;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Sanity;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    public sealed class GhostAiExperimentFlowTests
    {
        private readonly List<GameObject> _objects = new();
        private GhostPrototypeSettings _settings;
        private SanitySystemSettings _sanitySettings;
        private PlayerMoveSettings _moveSettings;
        private MoleBurrowSettings _burrowSettings;
        private SanityTeamService _team;
        private LocalPlayerContext _localPlayer;
        private NetworkManager _network;
        private GhostPrototypeController _ghost;
        private uint _hash = 0x6F700000;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _settings = ScriptableObject.CreateInstance<GhostPrototypeSettings>();
            _sanitySettings = ScriptableObject.CreateInstance<SanitySystemSettings>();
            _moveSettings = ScriptableObject.CreateInstance<PlayerMoveSettings>();
            _burrowSettings = ScriptableObject.CreateInstance<MoleBurrowSettings>();
            Set(_settings, "_aiExperimentsEnabled", true);
            _team = Create("GhostAiTestTeam").AddComponent<SanityTeamService>();
            Services.Bind<ISanityTeamService>(_team);
            _localPlayer = new LocalPlayerContext();
            Services.Bind<ILocalPlayerContext>(_localPlayer);
            GameObject root = Create("GhostAiTestNetwork");
            root.SetActive(false);
            _network = root.AddComponent<NetworkManager>();
            var transport = root.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", 17793, "127.0.0.1");
            _network.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false,
            };
            root.SetActive(true);
            Assert.IsTrue(_network.StartHost());
            yield return null;

            GameObject ghost = Create("GhostAiTestGhost");
            ghost.SetActive(false);
            ghost.layer = GameLayers.GhostPrototype;
            var networkObject = ghost.AddComponent<NetworkObject>();
            AssignHash(networkObject);
            _ghost = ghost.AddComponent<GhostPrototypeController>();
            Set(_ghost, "_settings", _settings);
            ghost.SetActive(true);
            networkObject.Spawn();
            _ghost.enabled = false;
            Assert.IsTrue(_ghost.IsServer);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_network != null)
            {
                _network.Shutdown();
                yield return null;
            }
            Services.Unbind<ISanityTeamService>(_team);
            Services.Unbind<ILocalPlayerContext>(_localPlayer);
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null)
                    Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
            Object.DestroyImmediate(_settings);
            Object.DestroyImmediate(_sanitySettings);
            Object.DestroyImmediate(_moveSettings);
            Object.DestroyImmediate(_burrowSettings);
        }

        [Test]
        public void 단서를_잃으면_마지막_위치를_수색하고_전체_OFF면_기존_추격으로_돌아간다()
        {
            SanityNetworkState player = SpawnPlayer(new Vector3(0f, 0f, 4f), 999);
            Call("ObserveTarget", player);
            Wall(2f);
            player.transform.position = new Vector3(2f, 0f, 4f);
            Physics.SyncTransforms();
            SetChase(player);
            TickAttack();
            Assert.AreEqual("Search", Get(_ghost, "_pursuit").ToString());
            Assert.AreEqual(new Vector3(0f, 0f, 4f), Get(_ghost, "_lastKnownPosition"),
                "벽 너머의 새 좌표가 마지막 관측 위치를 덮어쓰면 안 된다");

            Set(_settings, "_aiExperimentsEnabled", false);
            Call("RefreshAiFeatures");
            TickAttack();
            Assert.AreEqual("Chase", Get(_ghost, "_pursuit").ToString());
            Assert.AreSame(player, Get(_ghost, "_target"));
            Assert.AreEqual(player.transform.position, Get(_ghost, "_lastKnownPosition"));
        }

        [Test]
        public void 목격되지_않은_은신처는_실험_모드에서도_안전하다()
        {
            SanityNetworkState player = SpawnPlayer(new Vector3(0f, 0f, 4f), 999);
            GameObject shelter = Create("GhostAiTestShelter");
            shelter.transform.position = player.transform.position;
            shelter.AddComponent<HidingSpot>();
            Physics.SyncTransforms();
            TickAttack();
            Assert.IsNull(Get(_ghost, "_target"));
            Assert.IsTrue(player.HasSanity);
        }

        [Test]
        public void 단서_추적을_켜면_벽_너머_근거리_포획을_막는다()
        {
            SanityNetworkState player = SpawnPlayer(new Vector3(0f, 0f, 1f), 999);
            SpawnPlayer(new Vector3(4f, 0f, 4f), 998);
            Wall(0.5f);
            Physics.SyncTransforms();
            Set(_ghost, "_target", player);
            Call("TryCatch");
            Assert.IsTrue(player.HasSanity);
            Set(_settings, "_evidenceTrackingEnabled", false);
            Call("TryCatch");
            Assert.IsFalse(player.HasSanity, "OFF면 기존 포획 규칙과 비교할 수 있어야 한다");
        }

        [Test]
        public void 가구_소음은_위치_조사만_시작하며_토글_OFF면_무시한다()
        {
            GameObject floor = Create("GhostAiTestFloor");
            floor.transform.position = Vector3.down * 0.1f;
            floor.AddComponent<BoxCollider>().size = new Vector3(20f, 0.2f, 20f);
            _ghost.ServerConfigureRoam(Vector3.zero, new Vector3(10f, 3f, 10f), floor.transform);
            ((NetworkVariable<GhostPhase>)Get(_ghost, "_phase")).Value = GhostPhase.Attack;
            Vector3 impact = new(2f, 0f, 2f);
            Call("HandleServerImpact", impact, _settings.ImpactMinimumSpeed - 0.1f);
            Assert.IsFalse((bool)Get(_ghost, "_hasPendingImpact"));
            Call("HandleServerImpact", impact, _settings.ImpactMinimumSpeed + 1f);
            Assert.IsTrue((bool)Get(_ghost, "_hasPendingImpact"));
            TickAttack();
            Assert.AreEqual("Search", Get(_ghost, "_pursuit").ToString());
            Assert.AreEqual(impact, Get(_ghost, "_lastKnownPosition"));
            Assert.IsNull(Get(_ghost, "_target"), "소음으로 플레이어를 특정하지 않는다");

            Set(_settings, "_impactInvestigationEnabled", false);
            Call("RefreshAiFeatures");
            Call("HandleServerImpact", impact, 10f);
            Assert.IsFalse((bool)Get(_ghost, "_hasPendingImpact"));
        }

        [Test]
        public void 귀신_제거는_가구_충돌_이벤트_구독을_해제한다()
        {
            _ghost.GetComponent<NetworkObject>().Despawn(false);
            var callbacks = typeof(FurnitureNetworkPhysics).GetField("ServerImpactReported",
                BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) as Delegate;
            if (callbacks == null)
                return;
            foreach (Delegate callback in callbacks.GetInvocationList())
                Assert.AreNotSame(_ghost, callback.Target);
        }

        [Test]
        public void 들어가는_것을_본_침대_은신은_시야가_끊겨도_계속_추적한다()
        {
            SanityNetworkState player = SpawnPlayer(new Vector3(0f, 0f, 4f),
                _network.LocalClientId, withMotor: true);
            SpawnPlayer(new Vector3(4f, 0f, 4f), 998);
            SetProne(player);
            GameObject bed = Create("GhostAiTestBedHide");
            bed.transform.position = player.transform.position;
            bed.AddComponent<BedHideZone>();
            SetChase(player);
            Set(_ghost, "_targetWasVisible", true);
            Wall(2f);
            Physics.SyncTransforms();
            TickAttack();
            Set(_ghost, "_searchRemaining", 0f);
            TickAttack();
            Assert.AreSame(player, Get(_ghost, "_witnessedBedPlayer"));
            Assert.AreEqual("Search", Get(_ghost, "_pursuit").ToString());

            _ghost.transform.position = new Vector3(0f, 0f, 3f);
            Call("ServerTickWitnessedBed");
            Assert.IsFalse(player.HasSanity);
        }

        [Test]
        public void 들어가는_것을_못_본_침대_은신은_소음과_수색에도_안전하다()
        {
            SanityNetworkState player = SpawnPlayer(new Vector3(0f, 0f, 4f),
                _network.LocalClientId, withMotor: true);
            SetProne(player);
            GameObject bed = Create("GhostAiTestSafeBedHide");
            bed.transform.position = player.transform.position;
            bed.AddComponent<BedHideZone>();
            Wall(2f);
            Physics.SyncTransforms();
            var players = (SanityNetworkState[])Get(_ghost, "_players");
            int count = _team.CopyPlayerStates(players);
            Call("EvaluateBedHide", count, _settings.BedHideConcealSeconds + 1f);
            TickAttack();
            Assert.IsNull(Get(_ghost, "_witnessedBedPlayer"));
            Assert.IsNull(Get(_ghost, "_target"));
            Assert.IsTrue(player.HasSanity);
        }

        [TestCase(false, 3.1f, false)]
        [TestCase(false, 3f, true)]
        [TestCase(true, 3.1f, false)]
        [TestCase(true, 3f, true)]
        public void 타깃_교체는_실험과_무관하게_1m_이득부터_허용한다(
            bool experiments, float candidateDistance, bool switches)
        {
            Set(_settings, "_aiExperimentsEnabled", experiments);
            SanityNetworkState current = SpawnPlayer(new Vector3(0f, 0f, 4f), _network.LocalClientId);
            SanityNetworkState candidate = SpawnPlayer(new Vector3(0f, 0f, candidateDistance), 998);
            SetChase(current);
            TickAttack();
            Assert.AreSame(switches ? candidate : current, Get(_ghost, "_target"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 정지한_플레이어는_시야선이_열려_있을_때만_탐지한다(bool blocked)
        {
            Set(_settings, "_aiExperimentsEnabled", false);
            SanityNetworkState player = SpawnPlayer(new Vector3(0f, 0f, 4f), _network.LocalClientId);
            if (blocked)
                Wall(2f);
            Physics.SyncTransforms();
            TickAttack();
            Assert.AreSame(blocked ? null : player, Get(_ghost, "_target"));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void 시야_뒤쪽의_이동은_듣되_웅크림과_엎드림은_소리로_탐지하지_않는다(
            bool crouching, bool prone)
        {
            Set(_settings, "_aiExperimentsEnabled", false);
            Set(_settings, "_nearDetectRadius", 0f);
            SanityNetworkState player = SpawnPlayer(new Vector3(0f, 0f, -6f),
                _network.LocalClientId, withMotor: true);
            var motor = player.GetComponent<PlayerMotor>();
            ((NetworkVariable<bool>)Get(motor, "_isCrouching")).Value = crouching;
            ((NetworkVariable<bool>)Get(motor, "_isProne")).Value = prone;
            var players = (SanityNetworkState[])Get(_ghost, "_players");
            int count = _team.CopyPlayerStates(players);
            ((GhostPerception)Get(_ghost, "_perception")).UpdateSpeeds(players, count, 1f);
            player.transform.position = new Vector3(0f, 0f, -4f);
            ((GhostPerception)Get(_ghost, "_perception")).UpdateSpeeds(players, count, 1f);
            Physics.SyncTransforms();
            TickAttack();
            Assert.AreSame(crouching || prone ? null : player, Get(_ghost, "_target"));
        }

        [TestCase(false, false, false)]
        [TestCase(true, false, true)]
        [TestCase(false, true, false)]
        [TestCase(true, true, true)]
        public void 원뿔_시야는_고위험_거리각도를_구분하고_근거리_보정을_섞지_않는다(
            bool highRisk, bool side, bool expected)
        {
            Set(_settings, "_visionDistance", 2f);
            Set(_settings, "_highRiskVisionDistance", 6f);
            Set(_settings, "_visionAngle", 30f);
            Set(_settings, "_highRiskVisionAngle", 120f);
            SanityNetworkState player = SpawnPlayer(side ? new Vector3(1f, 0f, 1f) : new Vector3(0f, 0f, 4f),
                _network.LocalClientId);
            Physics.SyncTransforms();
            var perception = (GhostPerception)Get(_ghost, "_perception");
            Assert.AreEqual(expected, perception.CanSee(player, highRisk));
        }

        [Test]
        public void 복구는_경로_재계산_대기시간을_보존하고_이전_이동_경로는_버린다()
        {
            Vector3 center = BuildNavigationFloor();
            var navigation = (GhostNavigation)Get(_ghost, "_navigation");
            Assert.IsTrue(navigation.MoveToward(center + Vector3.forward * 3f, 2f, 0.1f,
                center, Vector3.one * 10f, allowPartial: false));
            Vector3 fallback = center + Vector3.down;
            Assert.AreNotEqual(fallback, navigation.GetWaypointOr(fallback));
            var snapshot = _ghost.CaptureStageState();
            snapshot.Position = center + Vector3.right;
            snapshot.PathRebuildRemaining = 0.375f;
            _ghost.ServerRestoreStageState(snapshot);
            Assert.AreEqual(snapshot.Position, _ghost.transform.position);
            Assert.AreEqual(snapshot.PathRebuildRemaining, _ghost.CaptureStageState().PathRebuildRemaining);
            Assert.AreEqual(fallback, navigation.GetWaypointOr(fallback));
        }

        [Test]
        public void 귀신_디스폰은_자신이_생성한_NavMesh를_해제한다()
        {
            Vector3 center = BuildNavigationFloor();
            _ghost.NetworkObject.Despawn(false);
            Assert.IsFalse(NavMesh.SamplePosition(center, out _, 1f, NavMesh.AllAreas));
        }

        private Vector3 BuildNavigationFloor()
        {
            Vector3 center = new(650f, 0f, 650f);
            _ghost.transform.position = center;
            GameObject house = Create("GhostAiNavigationHouse");
            house.transform.position = center;
            GameObject floor = Create("GhostAiNavigationFloor");
            floor.transform.SetParent(house.transform, false);
            floor.transform.localPosition = Vector3.down * 0.1f;
            floor.AddComponent<BoxCollider>().size = new Vector3(16f, 0.2f, 16f);
            Physics.SyncTransforms();
            _ghost.ServerConfigureRoam(center, Vector3.one * 20f, house.transform);
            Assert.IsTrue(NavMesh.SamplePosition(center, out _, 1f, NavMesh.AllAreas));
            return center;
        }

        private void SetProne(SanityNetworkState player)
        {
            var motor = player.GetComponent<PlayerMotor>();
            ((NetworkVariable<bool>)Get(motor, "_isProne")).Value = true;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 감지된_굴착은_수색을_유지하고_접근하면_포획한다(bool experiments)
        {
            Set(_settings, "_aiExperimentsEnabled", experiments);
            SanityNetworkState player = SpawnPlayer(new Vector3(0f, 0f, 4f),
                _network.LocalClientId, withBurrow: true);
            SetChase(player);
            SetBurrowed(player);

            TickAttack();
            Set(_ghost, "_searchRemaining", 0f);
            TickAttack();
            Assert.AreSame(player, Get(_ghost, "_witnessedBurrowPlayer"));
            Assert.AreEqual("Search", Get(_ghost, "_pursuit").ToString());
            Assert.IsTrue(player.HasSanity);

            _ghost.transform.position = new Vector3(0f, 0f, 3f);
            TickAttack();
            Assert.IsFalse(player.HasSanity, "감지된 채 굴착해도 접근한 귀신에게 잡혀야 한다.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 같은_틱에_굴착한_추격_대상은_새_타깃을_고르기_전에_노출을_기록한다(bool experiments)
        {
            Set(_settings, "_aiExperimentsEnabled", experiments);
            SanityNetworkState player = SpawnPlayer(new Vector3(0f, 0f, 4f),
                _network.LocalClientId, withBurrow: true);
            SpawnPlayer(new Vector3(1f, 0f, 3f), 998);
            SetChase(player);
            SetBurrowed(player);

            TickAttack();
            Assert.AreSame(player, Get(_ghost, "_witnessedBurrowPlayer"));
            Assert.IsNull(Get(_ghost, "_target"), "새 추격 대상을 고르기 전에 목격된 굴착 수색으로 전환해야 한다.");
            Assert.AreEqual("Search", Get(_ghost, "_pursuit").ToString());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 감지되지_않은_굴착은_가까이_수색해도_포획하지_않는다(bool experiments)
        {
            Set(_settings, "_aiExperimentsEnabled", experiments);
            SanityNetworkState player = SpawnPlayer(new Vector3(0f, 0f, 1f),
                _network.LocalClientId, withBurrow: true);
            SetBurrowed(player);

            TickAttack();
            Assert.IsNull(Get(_ghost, "_target"));
            Assert.IsNull(Get(_ghost, "_witnessedBurrowPlayer"));
            Assert.IsTrue(player.HasSanity, "들키기 전에 굴착한 플레이어는 가까워도 안전해야 한다.");
        }

        private void SetBurrowed(SanityNetworkState player) =>
            ((NetworkVariable<bool>)Get(player.GetComponent<MoleBurrowController>(), "_isBurrowed")).Value = true;

        private SanityNetworkState SpawnPlayer(Vector3 position, ulong owner, bool withMotor = false,
            bool withBurrow = false)
        {
            GameObject player = Create("GhostAiTestPlayer");
            player.SetActive(false);
            player.transform.position = position;
            var networkObject = player.AddComponent<NetworkObject>();
            AssignHash(networkObject);
            if (withMotor || withBurrow)
            {
                var motor = player.AddComponent<PlayerMotor>();
                Set(motor, "_settings", _moveSettings);
                motor.enabled = false;
            }
            if (withBurrow)
            {
                var burrow = player.AddComponent<MoleBurrowController>();
                Set(burrow, "_settings", _burrowSettings);
                burrow.enabled = false;
            }
            var state = player.AddComponent<SanityNetworkState>();
            Set(state, "_settings", _sanitySettings);
            player.SetActive(true);
            networkObject.SpawnWithOwnership(owner);
            return state;
        }

        private void SetChase(SanityNetworkState player)
        {
            Set(_ghost, "_target", player);
            FieldInfo pursuit = typeof(GhostPrototypeController).GetField("_pursuit",
                BindingFlags.Instance | BindingFlags.NonPublic);
            pursuit.SetValue(_ghost, Enum.Parse(pursuit.FieldType, "Chase"));
            Set(_ghost, "_targetSelectionRemaining", 0f);
        }

        private void TickAttack()
        {
            var players = (SanityNetworkState[])Get(_ghost, "_players");
            int count = _team.CopyPlayerStates(players);
            Call("ServerTickAttack", 0f, count);
        }

        private void Wall(float z)
        {
            GameObject wall = Create("GhostAiTestWall");
            wall.transform.position = new Vector3(0f, 1f, z);
            wall.AddComponent<BoxCollider>().size = new Vector3(10f, 4f, 0.1f);
        }

        private GameObject Create(string name)
        {
            var value = new GameObject(name);
            _objects.Add(value);
            return value;
        }

        private void AssignHash(NetworkObject value)
            => typeof(NetworkObject).GetField("GlobalObjectIdHash",
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, _hash++);

        private static object Get(object target, string name)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        private static void Set(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private void Call(string name, params object[] arguments)
            => typeof(GhostPrototypeController).GetMethod(name,
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_ghost, arguments);
    }
}
