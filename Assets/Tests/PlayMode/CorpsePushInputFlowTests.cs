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
    /// 실제 마우스 입력으로 시체를 가구처럼 잡기·던지기·2인 운반·취소하는 서버 RPC 경로와
    /// 플레이어 밀치기를 검증한다.
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
        private Camera _camera;
        private GrabController _grab;
        private PlayerInputReader _input;
        private SanityNetworkState _hostSanity;
        private PlayerVisuals _corpseOwner;
        private Rigidbody _corpseBody;
        private FurnitureThrowSettings _throwSettings;

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
            Set(_deathSettings, "_corpseOutlineMaterial",
                AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Furniture_Outline.mat"));
            _throwSettings = AssetDatabase.LoadAssetAtPath<FurnitureThrowSettings>(
                "Assets/Settings/Gameplay/FurnitureThrowSettings_Default.asset");
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
        public IEnumerator 호스트가_짧게_좌클릭하면_원격_플레이어_시체가_가구처럼_발사된다()
        {
            yield return PrepareCorpse();
            Vector3 eye = _camera.transform.position;
            eye.y = _corpseBody.position.y;
            _camera.transform.position = eye;
            _camera.transform.LookAt(_corpseBody.position);
            Vector3 before = _corpseBody.position;
            yield return BeginGrab();
            Assert.AreEqual(FurnitureState.ThrowReady, _corpseOwner.GrabState, "첫 홀더는 투척 준비다.");
            Release();
            yield return WaitFor(() => _corpseOwner.HolderCount == 0, 2f);
            for (int i = 0; i < 30; i++)
                yield return new WaitForFixedUpdate();

            Assert.Greater((_corpseBody.position - before).magnitude, 0.2f,
                "짧게 눌렀다 떼면 서버가 가구와 같은 발사 힘을 준다.");
        }

        [UnityTest]
        public IEnumerator 누르고_있는_동안_차징하고_떼면_홀더를_해제한다()
        {
            yield return PrepareCorpse();
            yield return BeginGrab();
            Assert.IsFalse(_grab.IsChargingShove, "시체가 플레이어 밀치기보다 먼저 입력을 소비한다.");
            Assert.IsTrue(_grab.TryGetHeldCorpse(out _), "게이지가 읽을 수 있게 홀더로 복제돼야 한다.");
            yield return WaitFor(() => _corpseOwner.GrabCharge > 0.05f, 2f);
            Assert.Greater(_corpseOwner.GrabCharge, 0.05f, "누르고 있는 동안 차지가 오른다.");
            Release();
            yield return WaitFor(() => _corpseOwner.HolderCount == 0, 2f);
            Assert.AreEqual(0, _corpseOwner.HolderCount);
            Assert.AreEqual(GrabController.NoObjectId, Get<ulong>(_grab, "_serverHeldCorpseId"));
        }

        [UnityTest]
        public IEnumerator 메뉴를_열기_전_강제해제하면_발사하지_않고_버튼을_떼도_던지지_않는다()
        {
            yield return PrepareCorpse();
            yield return BeginGrab();
            Vector3 before = _corpseBody.position;
            _grab.ForceRelease();
            _input.SetGameplayInputLocked(true);
            yield return WaitFor(() => _corpseOwner.HolderCount == 0, 2f);
            Assert.AreEqual(0, _corpseOwner.HolderCount);
            Assert.AreEqual(FurnitureState.Idle, _corpseOwner.GrabState, "취소는 발사가 아니라 그냥 놓는 것이다.");
            Release();
            yield return null;
            for (int i = 0; i < 10; i++)
                yield return new WaitForFixedUpdate();
            Assert.Less(Vector3.ProjectOnPlane(_corpseBody.position - before, Vector3.up).magnitude, 0.1f);
        }

        [UnityTest]
        public IEnumerator 홀더가_사망하면_서버의_시체_홀더도_해제한다()
        {
            yield return PrepareCorpse();
            yield return BeginGrab();
            Assert.IsTrue(_hostSanity.ServerMarkDead());
            yield return null;
            Assert.AreEqual(0, _corpseOwner.HolderCount);
            Assert.AreEqual(GrabController.NoObjectId, Get<ulong>(_grab, "_serverHeldCorpseId"));
        }

        [UnityTest]
        public IEnumerator 홀더가_디스폰하면_서버의_시체_홀더도_해제한다()
        {
            yield return PrepareCorpse();
            yield return BeginGrab();
            _grab.NetworkObject.Despawn();
            yield return null;
            Assert.AreEqual(0, _corpseOwner.HolderCount);
        }

        [UnityTest]
        public IEnumerator 두번째_홀더가_붙으면_2인_운반으로_손_목표를_따라온다()
        {
            yield return PrepareCorpse();
            yield return BeginGrab();
            float startHeight = _corpseBody.position.y;

            // 두 사람 모두 위쪽 앞을 보고 든다 — 시체가 눈높이 근처까지 떠올라야 한다.
            Vector3 up = new Vector3(0f, 0.5f, 1f).normalized;
            _camera.transform.rotation = Quaternion.LookRotation(up);
            Vector3 origin = _camera.transform.position;
            Assert.IsTrue(_corpseOwner.ServerTryAddHolder(998, origin + Vector3.right * 0.3f, up));
            Assert.AreEqual(FurnitureState.Held, _corpseOwner.GrabState);
            Assert.IsFalse(_corpseBody.useGravity, "2인 운반 중에는 중력을 끈다.");

            for (int i = 0; i < 60; i++)
                yield return new WaitForFixedUpdate();

            Assert.Greater(_corpseBody.position.y, startHeight + 0.4f, "두 손 목표를 향해 시체가 들려 올라온다.");
        }

        [UnityTest]
        public IEnumerator 가상_파트너를_붙이면_혼자서도_시체_2인_운반이_된다()
        {
            // 개발자 테스트용 가상 두 번째 홀더(FurnitureVirtualPartner) — 시체 경로도 같은 서버 API 로 붙는다.
            yield return PrepareCorpse();
            yield return BeginGrab();
            _camera.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0.5f, 1f).normalized);

            var partner = new FurnitureVirtualPartner();
            Assert.IsTrue(partner.TryAttach(
                _corpseOwner, 0UL, _camera.transform.position, _camera.transform.forward), partner.Message);
            Assert.AreEqual(FurnitureState.Held, _corpseOwner.GrabState);
            Assert.AreEqual(2, _corpseOwner.HolderCount);

            for (int i = 0; i < 10; i++)
            {
                yield return new WaitForFixedUpdate();
                partner.Tick(_camera.transform.position, _camera.transform.forward);
            }

            Assert.IsTrue(partner.IsAttached, partner.Message);
            Assert.AreEqual(FurnitureState.Held, _corpseOwner.GrabState);

            partner.Detach();
            Assert.AreEqual(1, _corpseOwner.HolderCount);
            Assert.AreEqual(FurnitureState.ThrowReady, _corpseOwner.GrabState);
        }

        [UnityTest]
        public IEnumerator 둘이_들다가_한_명이_놓으면_남은_사람_방향으로_즉시_발사한다()
        {
            yield return PrepareCorpse();
            yield return BeginGrab();
            Vector3 up = new Vector3(0f, 0.5f, 1f).normalized;
            _camera.transform.rotation = Quaternion.LookRotation(up);
            Assert.IsTrue(_corpseOwner.ServerTryAddHolder(998, _camera.transform.position, up));
            for (int i = 0; i < 20; i++)
                yield return new WaitForFixedUpdate();

            _corpseOwner.ServerRelease(998, up, false);

            Assert.AreEqual(FurnitureState.Launched, _corpseOwner.GrabState);
            Assert.AreEqual(0, _corpseOwner.HolderCount, "남은 홀더도 같이 풀린다.");
            yield return new WaitForFixedUpdate();
            Assert.Greater(_corpseBody.linearVelocity.magnitude, 1f, "발사 속도가 실려 있어야 한다.");
        }

        [UnityTest]
        public IEnumerator 조준하면_시체_윤곽선이_켜지고_시선을_돌리면_꺼진다()
        {
            yield return PrepareCorpse();
            var shells = Get<Renderer[]>(_corpseOwner, "_outlineShells");
            Assert.IsNotNull(shells, "윤곽선 재질이 있으면 셸이 만들어진다.");
            Assert.IsNotEmpty(shells);
            foreach (Renderer shell in shells)
                Assert.IsFalse(shell.enabled, "평소에는 꺼져 있다.");

            _corpseOwner.SetTargeted(true);
            foreach (Renderer shell in shells)
                Assert.IsTrue(shell.enabled);
            _corpseOwner.SetTargeted(false);
            foreach (Renderer shell in shells)
                Assert.IsFalse(shell.enabled);
        }

        [UnityTest]
        public IEnumerator 플레이어를_누르면_차징하고_떼면_서버가_소유자에게_넉백을_보낸다()
        {
            SanityNetworkState target = SpawnVictim(999, Vector3.zero);
            SpawnHost(0, new Vector3(0f, 0f, -1.3f));
            target.NetworkObject.ChangeOwnership(0);
            _camera.transform.LookAt(target.transform.position + Vector3.up * 0.6f);
            yield return null;
            Press();
            yield return null;
            Assert.IsTrue(_grab.IsChargingShove);
            Assert.AreEqual(target.NetworkObjectId, Get<ulong>(_grab, "_serverShoveTargetId"));
            float initialPower = _grab.ShovePower;
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.GreaterOrEqual(_grab.ShovePower, initialPower);
            Release();
            yield return null;
            Assert.IsFalse(_grab.IsChargingShove);
            Assert.AreEqual(0f, _grab.ShovePower);
            Assert.Greater(Get<float>(_grab, "_serverNextShoveAt"), 0f);
            Assert.Greater(Get<Vector3>(target.GetComponent<PlayerMotor>(), "_knockbackVelocity").sqrMagnitude, 0f);
        }

        [UnityTest]
        public IEnumerator 강제해제는_플레이어_차징을_접고_버튼을_떼도_넉백을_보내지_않는다()
        {
            SanityNetworkState target = SpawnVictim(999, Vector3.zero);
            SpawnHost(0, new Vector3(0f, 0f, -1.3f));
            target.NetworkObject.ChangeOwnership(0);
            _camera.transform.LookAt(target.transform.position + Vector3.up * 0.6f);
            yield return null;
            Press();
            yield return null;
            Assert.IsTrue(_grab.IsChargingShove);
            _grab.ForceRelease();
            Assert.IsFalse(_grab.IsChargingShove);
            Release();
            yield return null;
            Assert.AreEqual(0f, Get<float>(_grab, "_serverNextShoveAt"));
            Assert.AreEqual(Vector3.zero, Get<Vector3>(target.GetComponent<PlayerMotor>(), "_knockbackVelocity"));
        }

        [UnityTest]
        public IEnumerator 플레이어보다_가까운_벽이_있으면_밀치기_차징을_시작하지_않는다()
        {
            SanityNetworkState target = SpawnVictim(999, Vector3.zero);
            SpawnHost(0, new Vector3(0f, 0f, -1.3f));
            _camera.transform.LookAt(target.transform.position + Vector3.up * 0.6f);
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _objects.Add(wall);
            wall.transform.position = new Vector3(0f, 1f, -0.8f);
            wall.transform.localScale = new Vector3(2f, 3f, 0.1f);
            yield return null;
            Press();
            yield return null;
            Assert.IsFalse(_grab.IsChargingShove);
            Assert.AreEqual(GrabController.NoObjectId, Get<ulong>(_grab, "_serverShoveTargetId"));
        }

        private IEnumerator PrepareCorpse()
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
            _corpseOwner = victim.GetComponent<PlayerVisuals>();
            _corpseBody = Get<Rigidbody>(_corpseOwner, "_corpseBody");
            Assert.IsNotNull(_corpseBody, "사망 처리하면 시체가 생겨야 한다");
            float deadline = Time.realtimeSinceStartup + 6f;
            while (_corpseBody.isKinematic && Time.realtimeSinceStartup < deadline)
                yield return new WaitForFixedUpdate();
            // kinematic 해제 직후에는 속도가 0이어도 공중에 있다. 낙하·회전까지 끝난 뒤 조준한다.
            for (int i = 0; i < 60; i++)
                yield return new WaitForFixedUpdate();
            while ((_corpseBody.linearVelocity.sqrMagnitude > 0.01f
                    || _corpseBody.angularVelocity.sqrMagnitude > 0.01f)
                   && Time.realtimeSinceStartup < deadline)
                yield return new WaitForFixedUpdate();
            Assert.IsFalse(_corpseBody.isKinematic);

            Physics.SyncTransforms();
            _camera.transform.LookAt(_corpseBody.position);
            Assert.IsTrue(Physics.Raycast(_camera.transform.position, _camera.transform.forward, out RaycastHit hit, 2f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), "카메라 앞에 아무것도 없다");
            Assert.IsTrue(PlayerVisuals.TryGetCorpseOwner(hit.collider, out _),
                $"카메라가 시체 대신 {hit.collider.name} 을(를) 맞힌다");
        }

        private IEnumerator BeginGrab()
        {
            Press();
            yield return WaitFor(() => _corpseOwner.HolderCount > 0, 2f);
            Assert.AreEqual(1, _corpseOwner.HolderCount, "시체를 조준하고 누르면 홀더로 등록돼야 한다.");
            Assert.AreEqual(_corpseOwner.NetworkObjectId, Get<ulong>(_grab, "_serverHeldCorpseId"));
        }

        private static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
                yield return null;
        }

        private void Press() => InputSystem.QueueStateEvent(_mouse, new MouseState().WithButton(MouseButton.Left));
        private void Release() => InputSystem.QueueStateEvent(_mouse, new MouseState());

        private SanityNetworkState SpawnVictim(ulong ownerId, Vector3 position)
        {
            GameObject root = Create($"CorpseTestVictim_{ownerId}");
            root.SetActive(false);
            root.transform.position = position;
            AddNetworkObject(root);
            root.AddComponent<CharacterController>();
            var motor = root.AddComponent<PlayerMotor>();
            Set(motor, "_settings", AssetDatabase.LoadAssetAtPath<PlayerMoveSettings>(
                "Assets/Settings/Gameplay/PlayerMoveSettings_Default.asset"));
            SanityNetworkState sanity = root.AddComponent<SanityNetworkState>();
            Set(sanity, "_settings", _sanitySettings);
            var spectator = root.AddComponent<SpectatorController>();
            Set(spectator, "_settings", _deathSettings);
            PlayerVisuals visuals = root.AddComponent<PlayerVisuals>();
            Set(root.AddComponent<GrabController>(), "_settings", _throwSettings);
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
            _camera = camera;

            var sanity = root.AddComponent<SanityNetworkState>();
            Set(sanity, "_settings", _sanitySettings);
            _hostSanity = sanity;
            var input = root.AddComponent<PlayerInputReader>();
            Set(input, "_inputActions", AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/InputSystem_Actions.inputactions"));
            _input = input;
            var grab = root.AddComponent<GrabController>();
            Set(grab, "_input", input);
            Set(grab, "_camera", camera);
            Set(grab, "_settings", _throwSettings);
            _grab = grab;
            root.SetActive(true);
            root.GetComponent<NetworkObject>().SpawnWithOwnership(ownerId);
            foreach (InputActionMap map in Get<InputActionAsset>(input, "_runtimeActions").actionMaps)
                map.devices = new InputDevice[] { _mouse };
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

        private static T Get<T>(object target, string fieldName) =>
            (T)target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }
}
#endif
