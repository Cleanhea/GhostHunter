using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GhostHunter.Core;
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
    public sealed class DeathSystemFlowTests
    {
        private readonly List<GameObject> _objects = new();
        private SanitySystemSettings _sanitySettings;
        private SpectatorSettings _deathSettings;
        private SanityTeamService _team;
        private NetworkManager _network;
        private uint _hash = 0x6F300000;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _sanitySettings = ScriptableObject.CreateInstance<SanitySystemSettings>();
            _deathSettings = ScriptableObject.CreateInstance<SpectatorSettings>();
            _team = Create("DeathTestTeam").AddComponent<SanityTeamService>();
            Services.Bind<ISanityTeamService>(_team);

            GameObject root = Create("DeathTestNetwork");
            root.SetActive(false);
            _network = root.AddComponent<NetworkManager>();
            var transport = root.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", 17784, "127.0.0.1");
            _network.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false,
            };
            root.SetActive(true);
            Assert.IsTrue(_network.StartHost());
            yield return null;
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
            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                if (_objects[i] != null)
                    Object.DestroyImmediate(_objects[i]);
            }
            _objects.Clear();
            Object.DestroyImmediate(_sanitySettings);
            Object.DestroyImmediate(_deathSettings);
        }

        [UnityTest]
        public IEnumerator 사망_시체_생성과_부활_삭제_그리고_전멸_전_부활()
        {
            SanityNetworkState first = SpawnPlayer(999, out PlayerVisuals visuals);
            SpawnPlayer(998, out _);
            yield return null;

            Assert.IsTrue(first.ServerMarkDead());
            yield return null;
            Assert.IsTrue(visuals.HasCorpse);
            Collider corpseCollider = GameObject.Find("Corpse_999").GetComponent<Collider>();
            Assert.IsTrue(PlayerVisuals.TryGetCorpseOwner(corpseCollider, out PlayerVisuals owner));
            Assert.AreSame(visuals, owner);
            Assert.AreEqual(1, _team.DeadPlayerCount);
            Assert.IsFalse(_team.IsTeamWiped);

            Assert.IsTrue(first.ServerRevive());
            yield return null;
            Assert.IsFalse(visuals.HasCorpse);
            Assert.IsFalse(PlayerVisuals.TryGetCorpseOwner(corpseCollider, out _));
            Assert.AreEqual(0, _team.DeadPlayerCount);
        }

        [UnityTest]
        public IEnumerator 스킨_모델_시체는_본까지_복제해_캡슐_중심에_눕힌다()
        {
            SanityNetworkState player = SpawnPlayer(999, out PlayerVisuals visuals, withSkinnedModel: true);
            SpawnPlayer(998, out _);
            yield return null;

            var liveSkin = ((Renderer[])GetField(visuals, "_bodyRenderers"))[0] as SkinnedMeshRenderer;
            Assert.IsNotNull(liveSkin);
            Assert.IsTrue(liveSkin.enabled, "원격 플레이어의 살아 있는 몸은 보인다");

            Assert.IsTrue(player.ServerMarkDead());
            yield return null;

            GameObject corpse = GameObject.Find("Corpse_999");
            Assert.IsNotNull(corpse);
            var collider = corpse.GetComponent<CapsuleCollider>();
            Assert.IsNotNull(collider, "콜라이더는 목격·운반 판정이 쓰는 시체 루트에 붙는다");
            Assert.AreEqual(_deathSettings.CorpseHeight, collider.height, 0.0001f);
            Assert.AreEqual(_deathSettings.CorpseRadius, collider.radius, 0.0001f);
            Assert.IsTrue(PlayerVisuals.TryGetCorpseOwner(collider, out PlayerVisuals owner));
            Assert.AreSame(visuals, owner);

            Transform model = corpse.transform.Find("Character");
            Assert.IsNotNull(model, "모델 루트째 복제한다");
            Assert.AreEqual(-_deathSettings.CorpseHeight * 0.5f, model.localPosition.y, 0.0001f,
                "발밑 피벗 모델은 캡슐 중심에서 절반 높이만큼 내려간다");

            var corpseSkin = model.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.IsTrue(corpseSkin.enabled);
            Assert.IsTrue(corpseSkin.bones[0].IsChildOf(corpse.transform),
                "본이 원래 플레이어에 남아 있으면 시체 메시가 시체 자리에 그려지지 않는다");
            Assert.IsFalse(model.GetComponent<Animator>().enabled, "시체 자세는 멈춘다");
            Assert.IsFalse(liveSkin.enabled, "죽은 플레이어의 살아 있는 몸은 숨긴다");
        }

        [UnityTest]
        public IEnumerator 마지막_생존자_사망은_전멸을_한번만_확정하고_부활을_막는다()
        {
            SanityNetworkState first = SpawnPlayer(999, out _);
            SanityNetworkState second = SpawnPlayer(998, out _);
            int wipeEvents = 0;
            _team.TeamWiped += () => wipeEvents++;
            yield return null;

            Assert.IsTrue(first.ServerMarkDead());
            Assert.IsFalse(_team.IsTeamWiped);
            Assert.IsTrue(second.ServerMarkDead());
            yield return null;

            Assert.IsTrue(_team.IsTeamWiped);
            Assert.AreEqual(2, _team.DeadPlayerCount);
            Assert.AreEqual(1, wipeEvents);
            Assert.IsFalse(first.ServerRevive());
            Assert.IsFalse(second.ServerResetForStage());
            Assert.IsFalse(second.ServerMarkDead());
            Assert.AreEqual(1, wipeEvents);
        }

        private SanityNetworkState SpawnPlayer(ulong ownerId, out PlayerVisuals visuals,
            bool withSkinnedModel = false)
        {
            GameObject root = Create($"DeathTestPlayer_{ownerId}");
            root.SetActive(false);
            NetworkObject identity = root.AddComponent<NetworkObject>();
            typeof(NetworkObject).GetField("GlobalObjectIdHash",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SetValue(identity, ++_hash);

            root.AddComponent<CharacterController>();
            SanityNetworkState sanity = root.AddComponent<SanityNetworkState>();
            Set(sanity, "_settings", _sanitySettings);
            var spectator = root.AddComponent<SpectatorController>();
            Set(spectator, "_settings", _deathSettings);

            visuals = root.AddComponent<PlayerVisuals>();
            if (withSkinnedModel)
                AttachSkinnedModel(root.transform, visuals);
            else
            {
                GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "RemoteBody";
                body.transform.SetParent(root.transform, false);
                Object.DestroyImmediate(body.GetComponent<Collider>());
                Set(visuals, "_bodyRenderers", new[] { body.GetComponent<Renderer>() });
            }

            root.SetActive(true);
            identity.SpawnWithOwnership(ownerId);
            return sanity;
        }

        /// <summary>Player 프리팹과 같은 모양: RemoteBody(빈 컨테이너) / Character(Animator, 발밑 피벗) / 본·스킨 메시.</summary>
        private static void AttachSkinnedModel(Transform root, PlayerVisuals visuals)
        {
            var body = new GameObject("RemoteBody");
            body.transform.SetParent(root, false);
            var model = new GameObject("Character");
            model.transform.SetParent(body.transform, false);
            model.AddComponent<Animator>();
            var hips = new GameObject("Hips");
            hips.transform.SetParent(model.transform, false);
            hips.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            var mesh = new GameObject("Mesh");
            mesh.transform.SetParent(model.transform, false);
            var skin = mesh.AddComponent<SkinnedMeshRenderer>();
            skin.bones = new[] { hips.transform };
            skin.rootBone = hips.transform;

            Set(visuals, "_bodyRenderers", new Renderer[] { skin });
            Set(visuals, "_corpseModel", model.transform);
        }

        private GameObject Create(string name)
        {
            var value = new GameObject(name);
            _objects.Add(value);
            return value;
        }

        private static void Set(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private static object GetField(object target, string fieldName)
        {
            return target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }
    }
}
