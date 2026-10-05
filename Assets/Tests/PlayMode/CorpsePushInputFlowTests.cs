#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GhostHunter.Core;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Sanity;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// 호스트가 마우스 좌클릭으로 원격 플레이어 시체를 미는 전체 경로 —
    /// 입력 → <see cref="GrabController"/> 시체 조준 → 서버 RPC → <see cref="PlayerVisuals.ServerTryPush"/> → 물리.
    /// </summary>
    public sealed class CorpsePushInputFlowTests
    {
        private readonly List<GameObject> _objects = new();
        private SanitySystemSettings _sanitySettings;
        private SpectatorSettings _deathSettings;
        private SanityTeamService _team;
        private LocalPlayerContext _context;
        private NetworkManager _network;
        private Mouse _mouse;
        private InputSettings.BackgroundBehavior _previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode _previousBehavior;
        private uint _hash = 0x6F400000;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousBackground = InputSystem.settings.backgroundBehavior;
            _previousBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _mouse = InputSystem.AddDevice<Mouse>();

            _sanitySettings = ScriptableObject.CreateInstance<SanitySystemSettings>();
            _deathSettings = ScriptableObject.CreateInstance<SpectatorSettings>();
            _team = Create("CorpseTestTeam").AddComponent<SanityTeamService>();
            _context = new LocalPlayerContext();
            Services.Bind<ISanityTeamService>(_team);
            Services.Bind<ILocalPlayerContext>(_context);

            GameObject root = Create("CorpseTestNetwork");
            root.SetActive(false);
            _network = root.AddComponent<NetworkManager>();
            var transport = root.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", 17785, "127.0.0.1");
            _network.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
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
            Services.Unbind<ILocalPlayerContext>(_context);
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null)
                    Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
            Object.DestroyImmediate(_sanitySettings);
            Object.DestroyImmediate(_deathSettings);
            InputSystem.RemoveDevice(_mouse);
            InputSystem.settings.editorInputBehaviorInPlayMode = _previousBehavior;
            InputSystem.settings.backgroundBehavior = _previousBackground;
        }

        [UnityTest]
        public IEnumerator 호스트가_짧게_좌클릭하면_원격_플레이어_시체가_밀린다()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _objects.Add(ground);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(40f, 1f, 40f);

            SanityNetworkState victim = SpawnVictim(999, new Vector3(0f, 0f, 0f));
            SpawnHost(0, new Vector3(0f, 0f, -1.3f));
            yield return null;

            Assert.IsTrue(victim.ServerMarkDead());
            yield return null;
            GameObject corpse = GameObject.Find("Corpse_999");
            Assert.IsNotNull(corpse, "사망 처리하면 시체가 생겨야 한다");

            Rigidbody body = corpse.GetComponent<Rigidbody>();
            float deadline = Time.realtimeSinceStartup + 6f;
            while ((body.isKinematic || body.linearVelocity.sqrMagnitude > 0.01f)
                   && Time.realtimeSinceStartup < deadline)
                yield return new WaitForFixedUpdate();
            Assert.IsFalse(body.isKinematic);

            Camera camera = GameObject.Find("CorpseTestHostCamera").GetComponent<Camera>();
            camera.transform.LookAt(corpse.transform.position);
            Assert.IsTrue(Physics.Raycast(camera.transform.position, camera.transform.forward, out RaycastHit hit, 2f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), "카메라 앞에 아무것도 없다");
            Assert.IsTrue(PlayerVisuals.TryGetCorpseOwner(hit.collider, out _),
                $"카메라가 시체 대신 {hit.collider.name} 을(를) 맞힌다");

            Vector3 before = corpse.transform.position;
            InputSystem.QueueStateEvent(_mouse, new MouseState().WithButton(MouseButton.Left));
            yield return null;
            InputSystem.QueueStateEvent(_mouse, new MouseState());
            yield return null;
            for (int i = 0; i < 30; i++)
                yield return new WaitForFixedUpdate();

            Assert.Greater((corpse.transform.position - before).magnitude, 0.2f,
                $"좌클릭으로 밀었는데 시체가 움직이지 않았다 (before {before}, after {corpse.transform.position})");
        }

        private SanityNetworkState SpawnVictim(ulong ownerId, Vector3 position)
        {
            GameObject root = Create($"CorpseTestVictim_{ownerId}");
            root.SetActive(false);
            root.transform.position = position;
            AddNetworkObject(root);
            root.AddComponent<CharacterController>();
            SanityNetworkState sanity = root.AddComponent<SanityNetworkState>();
            Set(sanity, "_settings", _sanitySettings);
            var spectator = root.AddComponent<SpectatorController>();
            Set(spectator, "_settings", _deathSettings);
            PlayerVisuals visuals = root.AddComponent<PlayerVisuals>();
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "RemoteBody";
            body.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            Set(visuals, "_bodyRenderers", new[] { body.GetComponent<Renderer>() });
            root.SetActive(true);
            root.GetComponent<NetworkObject>().SpawnWithOwnership(ownerId);
            return sanity;
        }

        private void SpawnHost(ulong ownerId, Vector3 position)
        {
            GameObject root = Create("CorpseTestHost");
            root.SetActive(false);
            root.transform.position = position;
            AddNetworkObject(root);

            var cameraObject = new GameObject("CorpseTestHostCamera");
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            Camera camera = cameraObject.AddComponent<Camera>();

            var sanity = root.AddComponent<SanityNetworkState>();
            Set(sanity, "_settings", _sanitySettings);
            var input = root.AddComponent<PlayerInputReader>();
            Set(input, "_inputActions", AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/InputSystem_Actions.inputactions"));
            var grab = root.AddComponent<GrabController>();
            Set(grab, "_input", input);
            Set(grab, "_camera", camera);
            Set(grab, "_settings", AssetDatabase.LoadAssetAtPath<FurnitureThrowSettings>(
                "Assets/Settings/Gameplay/FurnitureThrowSettings_Default.asset"));
            root.SetActive(true);
            root.GetComponent<NetworkObject>().SpawnWithOwnership(ownerId);
        }

        private NetworkObject AddNetworkObject(GameObject root)
        {
            NetworkObject identity = root.AddComponent<NetworkObject>();
            typeof(NetworkObject).GetField("GlobalObjectIdHash",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(identity, ++_hash);
            return identity;
        }

        private GameObject Create(string name)
        {
            var value = new GameObject(name);
            _objects.Add(value);
            return value;
        }

        private static void Set(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }
    }
}
#endif
