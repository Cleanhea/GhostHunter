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
    /// <summary>
    /// '박치기 공룡' 귀신(project/ghost-dino.md)의 서버 이동·충돌·피해를 실제 컨트롤러와 캐릭터 컨트롤러로 확인한다.
    /// 어택 틱(<c>ServerTickAttack</c>)을 직접 불러 시간을 진행한다 — 어택 시간·정신력으로 어택이 시작되는 흐름과
    /// 클라이언트 연출은 덮지 않는다.
    /// </summary>
    public sealed class GhostDinoChargeFlowTests
    {
        private const float Dt = 0.02f;

        private readonly List<GameObject> _objects = new();
        private GhostPrototypeSettings _settings;
        private SanitySystemSettings _sanitySettings;
        private SanityTeamService _team;
        private LocalPlayerContext _localPlayer;
        private NetworkManager _network;
        private GhostPrototypeController _ghost;
        private uint _hash = 0x6F710000;
        private bool _previousGhostFurniture;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousGhostFurniture = Physics.GetIgnoreLayerCollision(GameLayers.GhostPrototype, GameLayers.Furniture);
            // 박치기는 기본 귀신 설정을 상속한 박치기 설정 타입일 때만 돈다(ADR-0024).
            _settings = ScriptableObject.CreateInstance<GhostDinoSettings>();
            _sanitySettings = ScriptableObject.CreateInstance<SanitySystemSettings>();
            // 첫 틱에 곧바로 준비로, 준비는 1초(앞 0.6초 조정 / 뒤 0.4초 고정). 이동·피해 수치는 기본값.
            Set(_settings, "_dinoChaseSeconds", 0f);
            Set(_settings, "_dinoPrepareSeconds", 1f);
            Set(_settings, "_dinoDirectionLockSeconds", 0.4f);
            _team = Create("DinoTestTeam").AddComponent<SanityTeamService>();
            Services.Bind<ISanityTeamService>(_team);
            _localPlayer = new LocalPlayerContext();
            Services.Bind<ILocalPlayerContext>(_localPlayer);

            GameObject root = Create("DinoTestNetwork");
            root.SetActive(false);
            _network = root.AddComponent<NetworkManager>();
            var transport = root.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", 17794, "127.0.0.1");
            _network.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false,
            };
            root.SetActive(true);
            Assert.IsTrue(_network.StartHost());
            yield return null;

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _objects.Add(floor);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(60f, 1f, 60f);

            GameObject ghost = Create("DinoTestGhost");
            ghost.SetActive(false);
            ghost.layer = GameLayers.GhostPrototype;
            ghost.transform.position = new Vector3(0f, 0.01f, 0f);
            var networkObject = ghost.AddComponent<NetworkObject>();
            AssignHash(networkObject);
            _ghost = ghost.AddComponent<GhostPrototypeController>();
            Set(_ghost, "_settings", _settings);
            var body = ghost.GetComponent<CharacterController>();
            body.height = 2f;
            body.radius = 0.5f;
            body.center = new Vector3(0f, 1f, 0f);
            ghost.SetActive(true);
            networkObject.Spawn();
            // Update 가 어택 틱을 돌리지 않게 막는다 — 컨트롤러는 켠 채로 둬야 충돌 콜백(OnControllerColliderHit)이 온다.
            Set(_ghost, "_serverReady", false);
            Assert.IsTrue(_ghost.IsServer);
            Physics.SyncTransforms();
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
            {
                if (_objects[i] != null)
                    Object.DestroyImmediate(_objects[i]);
            }

            _objects.Clear();
            Object.DestroyImmediate(_settings);
            Object.DestroyImmediate(_sanitySettings);
            Physics.IgnoreLayerCollision(GameLayers.GhostPrototype, GameLayers.Furniture, _previousGhostFurniture);
        }

        [Test]
        public void 준비_중_방향은_타깃을_따라가다_고정_구간에서_굳는다()
        {
            SanityNetworkState player = SpawnPlayer(new Vector3(0f, 0f, 4f), 999);
            Set(_ghost, "_target", player);

            TickFor(0.1f);
            Assert.AreEqual(GhostDinoCharge.Stage.Prepare, Dino.Current);

            player.transform.position = new Vector3(4f, 0f, 0f);
            TickFor(0.2f);
            Assert.Greater(Vector3.Dot(DinoDirection, Vector3.right), 0.95f, "앞 구간에는 타깃을 따라 방향이 바뀐다");

            // 고정 구간(경과 0.6초 이후)에 들어간 뒤.
            TickFor(0.5f);
            Assert.IsTrue(Dino.IsDirectionLocked);
            Vector3 locked = DinoDirection;
            player.transform.position = new Vector3(-4f, 0f, 0f);
            TickFor(0.1f);

            Assert.Greater(Vector3.Dot(DinoDirection, locked), 0.999f, "고정된 뒤에는 타깃이 움직여도 방향이 바뀌지 않는다");
            Assert.Greater(Vector3.Dot(DinoDirection, Vector3.right), 0.95f);
        }

        [Test]
        public void 준비와_돌진_중에는_어택_종료를_미루고_스턴에서는_미루지_않는다()
        {
            SanityNetworkState player = SpawnPlayer(new Vector3(0f, 0f, 4f), 999);
            Set(_ghost, "_target", player);
            Assert.IsFalse(HoldsAttackEnd);

            TickFor(0.1f);
            Assert.IsTrue(HoldsAttackEnd, "준비 중");

            TickUntilStage(GhostDinoCharge.Stage.Charge);
            Assert.IsTrue(HoldsAttackEnd, "돌진 중");

            TickUntilStage(GhostDinoCharge.Stage.Stun);
            Assert.IsFalse(HoldsAttackEnd, "스턴 중 — 어택 시간이 끝났으면 스턴 없이 종료");
        }

        [Test]
        public void 돌진은_가구와_부딪히면_거리에_비례해_내구도를_깎고_스턴한다()
        {
            FurnitureNetworkPhysics furniture = SpawnFurniture(new Vector3(0f, 0.5f, 4f));
            SanityNetworkState player = SpawnPlayer(new Vector3(0f, 0f, 4.8f), 999);
            Set(_ghost, "_target", player);

            TickUntilStage(GhostDinoCharge.Stage.Stun);

            float distance = (float)Get(_ghost, "_dinoChargeDistance");
            Assert.Greater(distance, 2f, $"돌진 거리 {distance:F2}m");
            Assert.Less(distance, 3.5f, "가구 앞면(z=3.5)에서 귀신 반경만큼 못 미쳐 멈춘다");
            Assert.AreEqual(100 - GhostDinoCharge.FurnitureDamage(distance, 5f), furniture.Durability,
                "내구도 손실 = ⌊돌진 거리 × 5⌋");
            Assert.Less(furniture.Durability, 100);
            Assert.Less(_ghost.transform.position.z, 3.5f, "가구를 통과하지 못한다");
            Assert.IsTrue(player.HasSanity, "플레이어는 가구 뒤라 맞지 않는다");
        }

        [Test]
        public void 돌진은_플레이어와_부딪히면_즉사시키고_스턴한다()
        {
            SanityNetworkState victim = SpawnPlayer(new Vector3(0f, 0f, 3f), 999);
            Set(_ghost, "_target", victim);

            TickUntilStage(GhostDinoCharge.Stage.Stun);

            Assert.IsFalse(victim.HasSanity, "돌진 경로의 플레이어는 사망");
            Assert.Less((float)Get(_ghost, "_dinoChargeDistance"), 3f);
        }

        [Test]
        public void 돌진은_벽에_부딪히면_내구도_손실_없이_스턴한다()
        {
            GameObject wall = Create("DinoTestWall");
            wall.transform.position = new Vector3(0f, 1f, 3f);
            wall.AddComponent<BoxCollider>().size = new Vector3(10f, 4f, 0.1f);
            FurnitureNetworkPhysics bystander = SpawnFurniture(new Vector3(3f, 0.5f, 1f));
            Physics.SyncTransforms();
            SanityNetworkState player = SpawnPlayer(new Vector3(0f, 0f, 4.5f), 999);
            Set(_ghost, "_target", player);

            TickUntilStage(GhostDinoCharge.Stage.Stun);

            Assert.Less(_ghost.transform.position.z, 3f, "벽을 통과하지 못한다");
            Assert.AreEqual(100, bystander.Durability, "벽 충돌은 가구 내구도와 무관");
            Assert.IsTrue(player.HasSanity);
        }

        [Test]
        public void 돌진은_비스듬한_벽을_스쳐도_미끄러져_꺾이지_않고_거기서_끝난다()
        {
            // 돌진(+Z)과 25° 로 만나는 벽. x=0 줄과는 z≈1.7 에서 만난다. 2026-11-09 사용자 보고 "돌진이 직선이 아니다"의 재현.
            GameObject wall = Create("DinoTestGlancingWall");
            wall.transform.SetPositionAndRotation(new Vector3(0.6f, 1f, 3f), Quaternion.Euler(0f, -65f, 0f));
            wall.AddComponent<BoxCollider>().size = new Vector3(10f, 4f, 0.1f);
            Physics.SyncTransforms();
            SanityNetworkState player = SpawnPlayer(new Vector3(0f, 0f, 6f), 999);
            Set(_ghost, "_target", player);

            TickUntilStage(GhostDinoCharge.Stage.Stun);

            Vector3 end = _ghost.transform.position;
            Assert.Less(Mathf.Abs(end.x), 0.3f, $"벽을 따라 옆으로 미끄러지지 않는다 — 끝 {end}");
            Assert.Less(end.z, 1.7f, "벽에 닿은 자리에서 끝난다");
            Assert.IsTrue(player.HasSanity);
        }

        [Test]
        public void 스턴은_5초_뒤_풀리고_추격에서_다시_시작한다()
        {
            SanityNetworkState player = SpawnPlayer(new Vector3(0f, 0f, 4.8f), 999);
            SpawnFurniture(new Vector3(0f, 0.5f, 4f));
            Set(_ghost, "_target", player);
            TickUntilStage(GhostDinoCharge.Stage.Stun);
            // 테스트는 추격 0초로 시작하므로, 스턴 뒤 추격 단계가 보이게 8초로 되돌린다.
            Set(_settings, "_dinoChaseSeconds", 8f);

            Vector3 stunned = _ghost.transform.position;
            TickFor(4.8f);
            Assert.AreEqual(GhostDinoCharge.Stage.Stun, Dino.Current);
            Assert.Less(Vector3.Distance(stunned, _ghost.transform.position), 0.01f, "스턴 중에는 행동 불가");

            TickFor(0.4f);
            Assert.AreEqual(GhostDinoCharge.Stage.Chase, Dino.Current, "5초가 지나면 추격으로 돌아간다");
            Assert.IsFalse(HoldsAttackEnd, "추격 중에는 어택 종료를 미루지 않는다");
        }

        [Test]
        public void 기본_귀신_설정으로_바뀌면_사이클을_버리고_기존_어택_틱만_돈다()
        {
            SanityNetworkState player = SpawnPlayer(new Vector3(0f, 0f, 4f), 999);
            Set(_ghost, "_target", player);
            TickFor(0.1f);
            Assert.AreEqual(GhostDinoCharge.Stage.Prepare, Dino.Current);

            GhostPrototypeSettings basic = ScriptableObject.CreateInstance<GhostPrototypeSettings>();
            Set(_ghost, "_settings", basic);
            TickFor(0.1f);
            Object.DestroyImmediate(basic);

            Assert.IsNull(Get(_ghost, "_dino"));
            Assert.IsFalse(HoldsAttackEnd);
        }

        // ---- 보조 ----

        private GhostDinoCharge Dino => (GhostDinoCharge)Get(_ghost, "_dino");

        private Vector3 DinoDirection => (Vector3)Get(_ghost, "_dinoDirection");

        private bool HoldsAttackEnd => (bool)typeof(GhostPrototypeController)
            .GetProperty("DinoHoldsAttackEnd", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_ghost);

        private void TickFor(float seconds)
        {
            int ticks = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < ticks; i++)
                TickAttack();
        }

        private void TickUntilStage(GhostDinoCharge.Stage stage, float maxSeconds = 12f)
        {
            int ticks = Mathf.RoundToInt(maxSeconds / Dt);
            for (int i = 0; i < ticks; i++)
            {
                TickAttack();
                if (Dino != null && Dino.Current == stage)
                    return;
            }

            Assert.Fail($"{maxSeconds}초 안에 {stage} 단계에 들어가지 못했다. 지금 {Dino?.Current}, " +
                $"귀신 위치 {_ghost.transform.position}");
        }

        private void TickAttack()
        {
            var players = (SanityNetworkState[])Get(_ghost, "_players");
            int count = _team.CopyPlayerStates(players);
            typeof(GhostPrototypeController).GetMethod("ServerTickAttack", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_ghost, new object[] { Dt, count });
            Physics.SyncTransforms();
        }

        private SanityNetworkState SpawnPlayer(Vector3 position, ulong owner)
        {
            GameObject player = Create("DinoTestPlayer");
            player.SetActive(false);
            player.transform.position = position;
            var networkObject = player.AddComponent<NetworkObject>();
            AssignHash(networkObject);
            var state = player.AddComponent<SanityNetworkState>();
            Set(state, "_settings", _sanitySettings);
            player.SetActive(true);
            networkObject.SpawnWithOwnership(owner);
            return state;
        }

        private FurnitureNetworkPhysics SpawnFurniture(Vector3 position)
        {
            GameObject go = Create("DinoTestFurniture");
            go.SetActive(false);
            go.layer = GameLayers.Furniture;
            go.transform.position = position;
            go.AddComponent<BoxCollider>().size = Vector3.one;
            var networkObject = go.AddComponent<NetworkObject>();
            AssignHash(networkObject);
            var physics = go.AddComponent<FurnitureNetworkPhysics>();
            go.SetActive(true);
            networkObject.Spawn();
            Physics.SyncTransforms();
            return physics;
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
    }
}
