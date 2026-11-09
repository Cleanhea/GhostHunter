using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using GhostHunter.Core;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Sanity;
using GhostHunter.Gameplay.Voice;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// 귀신 공통 기획서 원문 0.3·0.4 규칙 중 컨트롤러가 하는 것 — 어택 시작 때 플레이어 근처 7m 스폰, 쉬움 난이도의 사망 시 어택 종료,
    /// 말소리 크기에 따른 소음 반응. 실제 컨트롤러·NavMesh·SanityNetworkState 로 확인한다(클라이언트 음성 송신 경로는 덮지 않는다).
    /// </summary>
    public sealed class GhostAttackRulesFlowTests
    {
        private readonly List<GameObject> _objects = new();
        private GhostPrototypeSettings _settings;
        private SanitySystemSettings _sanitySettings;
        private SanityTeamService _team;
        private LocalPlayerContext _localPlayer;
        private NetworkManager _network;
        private GhostPrototypeController _ghost;
        private NavMeshData _navData;
        private NavMeshDataInstance _navInstance;
        private GameObject _house;
        private uint _hash = 0x6F720000;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _settings = ScriptableObject.CreateInstance<GhostPrototypeSettings>();
            _sanitySettings = ScriptableObject.CreateInstance<SanitySystemSettings>();
            Set(_settings, "_dinoChargeEnabled", false);
            _team = Create("RulesTestTeam").AddComponent<SanityTeamService>();
            Services.Bind<ISanityTeamService>(_team);
            _localPlayer = new LocalPlayerContext();
            Services.Bind<ILocalPlayerContext>(_localPlayer);

            GameObject root = Create("RulesTestNetwork");
            root.SetActive(false);
            _network = root.AddComponent<NetworkManager>();
            var transport = root.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", 17795, "127.0.0.1");
            _network.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false,
            };
            root.SetActive(true);
            Assert.IsTrue(_network.StartHost());
            yield return null;

            // 넓은 평지 — 물리 바닥이자 귀신 NavMesh 의 원본.
            GameObject house = _house = Create("RulesTestHouse");
            GameObject floor = Create("RulesTestFloor");
            floor.transform.SetParent(house.transform, false);
            floor.transform.position = new Vector3(0f, -0.1f, 0f);
            floor.AddComponent<BoxCollider>().size = new Vector3(120f, 0.2f, 120f);
            Physics.SyncTransforms();
            _navData = GhostNavigation.BuildHouseNavMesh(house.transform, ~0, 0.25f, 1.8f, 0.35f);
            Assert.IsNotNull(_navData);
            _navInstance = NavMesh.AddNavMeshData(_navData);

            GameObject ghost = Create("RulesTestGhost");
            ghost.SetActive(false);
            ghost.layer = GameLayers.GhostPrototype;
            ghost.transform.position = new Vector3(-25f, 0.01f, 0f);
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
            Set(_ghost, "_serverReady", false);
            Set(_ghost, "_roamCenter", Vector3.zero);
            Set(_ghost, "_roamExtents", new Vector3(55f, 5f, 55f));
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

            if (_navInstance.valid)
                _navInstance.Remove();
            if (_navData != null)
                Object.DestroyImmediate(_navData);
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
        }

        // ---- 7m 스폰 ----

        [Test]
        public void 어택이_시작될_때_주변에_플레이어가_없으면_가까운_플레이어_7m_근처로_스폰한다()
        {
            SanityNetworkState player = SpawnPlayer(new Vector3(10f, 0f, 0f), 999);
            Assert.Greater(Vector3.Distance(_ghost.transform.position, player.transform.position), 30f);

            BeginAttackRules(living: 1);

            float distance = Vector3.Distance(_ghost.transform.position, player.transform.position);
            Assert.That(distance, Is.InRange(5f, 9f), $"플레이어에게서 약 7m. 지금 {distance:F2}m");
            Assert.AreEqual(0f, _ghost.transform.position.y, 0.3f, "같은 층");
        }

        [Test]
        public void 이미_플레이어_근처_7m_안이면_스폰하지_않는다()
        {
            _ghost.GetComponent<CharacterController>().enabled = false;
            _ghost.transform.position = new Vector3(5f, 0.01f, 0f);
            _ghost.GetComponent<CharacterController>().enabled = true;
            SpawnPlayer(new Vector3(10f, 0f, 0f), 999);
            Vector3 before = _ghost.transform.position;

            BeginAttackRules(living: 1);

            Assert.AreEqual(before, _ghost.transform.position);
        }

        [Test]
        public void 스폰_기능을_끄거나_추격_가능한_플레이어가_없으면_제자리에_있다()
        {
            Vector3 before = _ghost.transform.position;
            BeginAttackRules(living: 0);
            Assert.AreEqual(before, _ghost.transform.position, "플레이어가 없으면 옮길 곳이 없다");

            SpawnPlayer(new Vector3(10f, 0f, 0f), 999);
            Set(_settings, "_attackSpawnNearPlayerEnabled", false);
            BeginAttackRules(living: 1);
            Assert.AreEqual(before, _ghost.transform.position, "설정을 끄면 예전처럼 그대로");
        }

        [Test]
        public void 스폰은_플레이어_시야에_안_걸리는_지점을_먼저_고른다()
        {
            SanityNetworkState player = SpawnPlayer(new Vector3(10f, 0f, 0f), 999);
            // 플레이어 오른쪽(+X) 2m 앞에 폭 8m 담 — 담 뒤(원 위의 +X 쪽)는 시야가 막히고 담을 돌아가는 길은 열려 있다.
            BuildBlock(new Vector3(12f, 2f, 0f), new Vector3(0.4f, 4f, 8f));
            RebuildNavMesh();

            for (int attempt = 0; attempt < 5; attempt++)
            {
                MoveGhostTo(new Vector3(-25f, 0.01f, 0f));
                BeginAttackRules(living: 1);

                Vector3 eye = _ghost.transform.position + Vector3.up * _settings.GhostEyeHeight;
                Vector3 target = player.transform.position + Vector3.up * _settings.TargetCenterHeight;
                Assert.IsFalse(GhostPerception.HasLineOfSight(eye, target, player.transform),
                    $"시야에 안 걸리는 지점이 있으면 그곳에 스폰한다 ({attempt}번째, {_ghost.transform.position})");
                Assert.Less(Vector3.Distance(_ghost.transform.position, player.transform.position), 9f);
            }
        }

        [UnityTest]
        public IEnumerator 갈_수_있는_7m_지점이_없으면_제자리에_있다()
        {
            SanityNetworkState player = SpawnPlayer(new Vector3(10f, 0f, 0f), 999);
            // 플레이어를 반경 6m 담으로 가둔다 — 7m 원 위 지점은 모두 담 밖이라 플레이어까지 걸어갈 수 없다.
            BuildWall(player.transform.position, 6f);
            RebuildNavMesh();
            // 새로 구운 NavMesh 는 다음 프레임부터 질의에 반영된다.
            yield return null;
            yield return null;
            Vector3 before = _ghost.transform.position;

            BeginAttackRules(living: 1);

            Assert.AreEqual(before, _ghost.transform.position, "벽 너머 방에 떨어뜨리지 않는다");
        }

        // ---- 쉬움 난이도 ----

        [Test]
        public void 쉬움_난이도는_어택_중_한_명이라도_죽으면_어택이_끝난다()
        {
            Set(_settings, "_difficulty", GhostDifficulty.Easy);
            Set(_settings, "_attackSpawnNearPlayerEnabled", false);   // 스폰하면 귀신이 긴 테스트 틱 안에 플레이어를 잡아 버린다
            SanityNetworkState victim = SpawnPlayer(new Vector3(0f, 0f, 3f), 999);
            SpawnPlayer(new Vector3(0f, 0f, -3f), 998);
            StartAttack();
            Assert.AreEqual(GhostPhase.Attack, _ghost.Phase);

            Assert.IsTrue(victim.ServerMarkDead());
            Tick(0.1f);

            Assert.AreEqual(GhostPhase.Calming, _ghost.Phase, "쉬움: 죽는 순간 어택 자동 종료 → 자연 진정");
        }

        [TestCase(GhostDifficulty.Normal)]
        [TestCase(GhostDifficulty.Hard)]
        public void 보통과_어려움은_팀원이_죽어도_어택이_계속된다(GhostDifficulty difficulty)
        {
            Set(_settings, "_difficulty", difficulty);
            Set(_settings, "_attackSpawnNearPlayerEnabled", false);
            SanityNetworkState victim = SpawnPlayer(new Vector3(0f, 0f, 3f), 999);
            SpawnPlayer(new Vector3(0f, 0f, -3f), 998);
            StartAttack();
            Assert.AreEqual(GhostPhase.Attack, _ghost.Phase);

            Assert.IsTrue(victim.ServerMarkDead());
            Tick(0.1f);

            Assert.AreEqual(GhostPhase.Attack, _ghost.Phase, $"{difficulty}: 어택은 계속");
        }

        // ---- 말소리 소음 ----

        [Test]
        public void 조용한_말소리는_가까이서만_큰_말소리는_멀리서도_소음으로_기억한다()
        {
            (SanityNetworkState player, PlayerVoiceEmitter voice) = SpawnSpeaker(new Vector3(10f, 0f, 0f), 999);
            MoveGhostTo(new Vector3(0f, 0.01f, 0f));
            Set(_ghost, "_noiseScanAttacking", true);

            ReportSpeech(voice, -45f);
            ReportVoiceNoise();
            Assert.IsFalse(Noise.HasNoise, "조용한 소리(보통 8m)는 10m 밖이라 못 듣는다");

            ReportSpeech(voice, -10f);
            ReportVoiceNoise();
            Assert.IsTrue(Noise.HasNoise, "큰 소리(높음 12m)는 10m 밖에서도 듣는다");
            Assert.Less(Vector3.Distance(Noise.Position, player.transform.position), 0.01f, "소리가 난 곳 = 말한 플레이어 위치");
            Assert.AreEqual(GhostNoiseLevel.High, Noise.Level);
        }

        [Test]
        public void 말하지_않거나_보고가_끊겼거나_죽었거나_어택이_아니면_반응하지_않는다()
        {
            (SanityNetworkState player, PlayerVoiceEmitter voice) = SpawnSpeaker(new Vector3(4f, 0f, 0f), 999);
            MoveGhostTo(new Vector3(0f, 0.01f, 0f));
            Set(_ghost, "_noiseScanAttacking", true);

            ReportVoiceNoise();
            Assert.IsFalse(Noise.HasNoise, "한 번도 보고가 없었다");

            ReportSpeech(voice, -10f);
            Set(voice, "_speechReportedAt", Time.unscaledTimeAsDouble - 2d);
            ReportVoiceNoise();
            Assert.IsFalse(Noise.HasNoise, "보고가 끊긴 지 오래됐다");

            ReportSpeech(voice, -10f);
            Set(_ghost, "_noiseScanAttacking", false);
            ReportVoiceNoise();
            Assert.IsFalse(Noise.HasNoise, "어택 중이 아니면 소리에 반응하지 않는다");

            Set(_ghost, "_noiseScanAttacking", true);
            Set(_settings, "_voiceNoiseEnabled", false);
            ReportVoiceNoise();
            Assert.IsFalse(Noise.HasNoise, "설정을 끄면 반응하지 않는다");

            Set(_settings, "_voiceNoiseEnabled", true);
            Assert.IsTrue(player.ServerMarkDead());
            ReportVoiceNoise();
            Assert.IsFalse(Noise.HasNoise, "죽은 플레이어의 말소리는 듣지 않는다");
        }

        // ---- 보조 ----

        private GhostNoiseMemory Noise => (GhostNoiseMemory)Get(_ghost, "_noise");

        private void BeginAttackRules(int living)
        {
            var players = (SanityNetworkState[])Get(_ghost, "_players");
            int count = _team.CopyPlayerStates(players);
            Call("ServerBeginAttackRules", count, living);
        }

        private void ReportVoiceNoise()
        {
            var players = (SanityNetworkState[])Get(_ghost, "_players");
            int count = _team.CopyPlayerStates(players);
            Call("ReportVoiceNoise", count);
        }

        private static void ReportSpeech(PlayerVoiceEmitter voice, float decibels)
        {
            Set(voice, "_speechLevelDb", decibels);
            Set(voice, "_speechReportedAt", Time.unscaledTimeAsDouble);
        }

        /// <summary>경고를 거쳐 어택에 들어가게 한다 — 상태 기계를 직접 돌린다.</summary>
        private void StartAttack()
        {
            var machine = (GhostStateMachine)Get(_ghost, "_machine");
            Assert.IsTrue(machine.ForceSpecialAttack());
            Tick(5.1f);
        }

        private void Tick(float seconds) => Call("ServerTick", seconds);

        private void MoveGhostTo(Vector3 position)
        {
            var controller = _ghost.GetComponent<CharacterController>();
            controller.enabled = false;
            _ghost.transform.position = position;
            controller.enabled = true;
            Physics.SyncTransforms();
        }

        /// <summary>플레이어를 가두는 원형 담 — 24조각 상자를 둘레에 겹쳐 세운다(사각 담이면 대각선 방향의 7m 지점이 안쪽에 들어간다).</summary>
        private void BuildWall(Vector3 center, float radius)
        {
            const int Segments = 24;
            GameObject wall = Create("RulesTestWallRing");
            wall.transform.SetParent(_house.transform, false);
            wall.transform.position = center;
            float width = 2f * radius * Mathf.Tan(Mathf.PI / Segments) * 1.3f;
            for (int i = 0; i < Segments; i++)
            {
                GameObject side = Create("Side");
                side.transform.SetParent(wall.transform, false);
                Vector3 direction = Quaternion.Euler(0f, i * (360f / Segments), 0f) * Vector3.forward;
                side.transform.localPosition = direction * radius + Vector3.up * 2f;
                side.transform.localRotation = Quaternion.LookRotation(direction);
                side.AddComponent<BoxCollider>().size = new Vector3(width, 4f, 0.4f);
            }

            Physics.SyncTransforms();
        }

        private void BuildBlock(Vector3 position, Vector3 size)
        {
            GameObject block = Create("RulesTestBlock");
            block.transform.SetParent(_house.transform, false);
            block.transform.position = position;
            block.AddComponent<BoxCollider>().size = size;
            Physics.SyncTransforms();
        }

        private void RebuildNavMesh()
        {
            if (_navInstance.valid)
                _navInstance.Remove();
            if (_navData != null)
                Object.DestroyImmediate(_navData);
            _navData = GhostNavigation.BuildHouseNavMesh(_house.transform, ~0, 0.25f, 1.8f, 0.35f);
            Assert.IsNotNull(_navData);
            _navInstance = NavMesh.AddNavMeshData(_navData);
        }

        private SanityNetworkState SpawnPlayer(Vector3 position, ulong owner)
        {
            GameObject player = Create("RulesTestPlayer");
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

        /// <summary>말소리 보고를 받을 수 있는 플레이어 — 음성 송신 배선(설정·수신기)이 없어 emitter 가 스스로 꺼진다는 오류 로그를 예상한다.</summary>
        private (SanityNetworkState, PlayerVoiceEmitter) SpawnSpeaker(Vector3 position, ulong owner)
        {
            GameObject player = Create("RulesTestSpeaker");
            player.SetActive(false);
            player.transform.position = position;
            var networkObject = player.AddComponent<NetworkObject>();
            AssignHash(networkObject);
            var state = player.AddComponent<SanityNetworkState>();
            Set(state, "_settings", _sanitySettings);
            var voice = player.AddComponent<PlayerVoiceEmitter>();
            player.SetActive(true);
            LogAssert.Expect(LogType.Error, new Regex("음성 설정/재생/상태 배선 누락"));
            networkObject.SpawnWithOwnership(owner);
            return (state, voice);
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
