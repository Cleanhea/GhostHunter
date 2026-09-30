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

        private void SetProne(SanityNetworkState player)
        {
            var motor = player.GetComponent<PlayerMotor>();
            ((NetworkVariable<bool>)Get(motor, "_isProne")).Value = true;
        }

        private SanityNetworkState SpawnPlayer(Vector3 position, ulong owner, bool withMotor = false)
        {
            GameObject player = Create("GhostAiTestPlayer");
            player.SetActive(false);
            player.transform.position = position;
            var networkObject = player.AddComponent<NetworkObject>();
            AssignHash(networkObject);
            if (withMotor)
            {
                var motor = player.AddComponent<PlayerMotor>();
                Set(motor, "_settings", _moveSettings);
                motor.enabled = false;
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
