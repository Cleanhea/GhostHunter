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
    /// 실제 마우스 입력으로 가구를 잡을 때의 사거리와, 서버가 잡기를 거부한 뒤의 클라이언트 상태를 검증한다.
    /// 호스트(서버=소유자)로 돌리므로 RPC 는 즉시 서버 검증을 지난다.
    /// 배경: 서버가 플레이어 발~가구 피벗 거리로 잡기를 거부해 큰 가구(침대·소파·리프트)가 서 있는 자리에 따라 안 잡혔고,
    /// 거부 뒤 0.5초 안에 떼면 타게터가 거부된 가구에 묶여 다음 가구도 못 잡았다.
    /// </summary>
    public sealed class GrabReachFlowTests
    {
        private readonly List<GameObject> _objects = new();
        private SanitySystemSettings _sanitySettings;
        private SanityTeamService _team;
        private LocalPlayerContext _context;
        private NetworkManager _network;
        private Mouse _mouse;
        private InputSettings.BackgroundBehavior _previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode _previousBehavior;
        private uint _hash = 0x6F600000;
        private GameObject _host;
        private Camera _camera;
        private FurnitureTargeter _targeter;
        private FurnitureThrowSettings _throwSettings;
        private FurnitureGrabTarget _long;
        private FurnitureGrabTarget _crate;

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
            _throwSettings = AssetDatabase.LoadAssetAtPath<FurnitureThrowSettings>(
                "Assets/Settings/Gameplay/FurnitureThrowSettings_Default.asset");
            _team = Create("GrabReachTeam").AddComponent<SanityTeamService>();
            _context = new LocalPlayerContext();
            Services.Bind<ISanityTeamService>(_team);
            Services.Bind<ILocalPlayerContext>(_context);

            GameObject root = Create("GrabReachNetwork");
            root.SetActive(false);
            _network = root.AddComponent<NetworkManager>();
            var transport = root.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", 17796, "127.0.0.1");
            _network.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
            root.SetActive(true);
            Assert.IsTrue(_network.StartHost());
            yield return null;

            SpawnHost(_network.LocalClientId);
            // 큰 가구: 피벗(루트)은 z=5, 몸통은 z=1..5. 플레이어(원점)에서 피벗까지 5m, 표면까지는 1m 남짓.
            _long = SpawnFurniture("GrabReachLong", new Vector3(0f, 0f, 5f),
                new Vector3(0f, 0.5f, -2f), new Vector3(1f, 1f, 4f));
            // 작은 가구: 플레이어 가까이.
            _crate = SpawnFurniture("GrabReachCrate", new Vector3(-1f, 0.25f, 1.2f),
                Vector3.zero, new Vector3(0.5f, 0.5f, 0.5f));
            for (int i = 0; i < 3; i++)
                yield return new WaitForFixedUpdate();
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
            if (_camera != null)
                Object.DestroyImmediate(_camera.gameObject);
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null)
                    Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
            Object.DestroyImmediate(_sanitySettings);
            InputSystem.RemoveDevice(_mouse);
            InputSystem.settings.editorInputBehaviorInPlayMode = _previousBehavior;
            InputSystem.settings.backgroundBehavior = _previousBackground;
        }

        [UnityTest]
        public IEnumerator 표면_거리는_피벗이_아니라_콜라이더_표면_기준이다()
        {
            yield return null;
            var eye = new Vector3(0f, 1.2f, 0f);
            Assert.AreEqual(1.02f, _long.DistanceToSurface(eye), 0.02f,
                "큰 가구의 앞면까지는 1m 남짓이다(피벗까지는 5m).");
            Assert.AreEqual(0f, _long.DistanceToSurface(new Vector3(0f, 0.5f, 3f)), 0.001f,
                "콜라이더 안의 점은 0 이다.");
            Assert.Greater(Vector3.Distance(Vector3.zero, _long.transform.position),
                _throwSettings.MaxTargetDistance * GrabController.GrabReachSlack,
                "이 배치는 예전 피벗 기준 검사라면 거부되는 자리여야 한다.");
        }

        [UnityTest]
        public IEnumerator 작은_가구는_누르면_잡힌다()
        {
            Aim(_crate.transform.position);
            yield return null;
            yield return null;
            Assert.AreSame(_crate, _targeter.CurrentTarget);

            Press();
            yield return Wait(0.4f);
            Assert.AreEqual(1, _crate.HolderCount);
            Release();
            yield return Wait(0.3f);
        }

        [UnityTest]
        public IEnumerator 피벗이_멀어도_표면이_사거리_안이면_큰_가구가_잡힌다()
        {
            Aim(new Vector3(0f, 0.5f, 1f));
            yield return null;
            yield return null;
            Assert.AreSame(_long, _targeter.CurrentTarget, "클라이언트 레이는 큰 가구 앞면에 닿는다.");

            Press();
            yield return Wait(0.4f);
            Assert.AreEqual(1, _long.HolderCount,
                "표면까지 2m 안이면 피벗이 멀어도 서버가 받아들여야 한다(예전에는 조용히 거부).");
            Release();
            yield return Wait(0.3f);
        }

        [UnityTest]
        public IEnumerator 서버가_거부한_잡기를_짧게_떼도_타게터는_다음_가구를_잡는다()
        {
            // 서버가 거부하도록 카메라를 플레이어 루트에서 AimOriginMaxOffset 밖으로 떼어 놓는다 —
            // 클라이언트 레이는 작은 가구에 닿지만 서버는 조준 원점이 비정상이라 거부한다.
            _camera.transform.SetParent(null);
            _camera.transform.position = new Vector3(-1f, 1.2f, 3f);
            Assert.Greater(Vector3.Distance(_host.transform.position, _camera.transform.position),
                GrabController.AimOriginMaxOffset);
            Aim(_crate.transform.position);
            yield return null;
            yield return null;
            Assert.AreSame(_crate, _targeter.CurrentTarget, "클라이언트는 작은 가구를 타게팅한다.");

            Press();
            yield return null;
            Release();               // 서버 응답·0.5초 타임아웃 전에 뗀다(탭)
            yield return Wait(1.0f); // 타임아웃보다 한참 뒤
            Assert.AreEqual(0, _crate.HolderCount, "서버가 거부해야 이 시나리오가 성립한다.");
            Assert.IsNull(Get<FurnitureGrabTarget>(_targeter, "_holdTarget"),
                "거부된 가구에 타게터가 묶여 있으면 안 된다.");

            // 카메라를 머리 위치로 되돌리고 큰 가구를 겨눈다.
            _camera.transform.SetParent(_host.transform, false);
            _camera.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            Aim(new Vector3(0f, 0.5f, 1f));
            yield return null;
            yield return null;
            yield return null;
            Assert.AreSame(_long, _targeter.CurrentTarget, "타게터가 새 가구를 겨누고 있어야 한다.");

            Press();
            yield return Wait(0.5f);
            Assert.AreEqual(1, _long.HolderCount, "거부 직후의 정상 잡기는 평소처럼 된다.");
            Assert.AreEqual(0, _crate.HolderCount, "거부된 가구를 대신 잡으면 안 된다.");
            Release();
            yield return Wait(0.3f);
        }

        [UnityTest]
        public IEnumerator 서버가_거부한_잡기를_길게_누르고_있어도_상태가_풀린다()
        {
            _camera.transform.SetParent(null);
            _camera.transform.position = new Vector3(-1f, 1.2f, 3f);
            Aim(_crate.transform.position);
            yield return null;
            yield return null;

            Press();
            yield return Wait(0.9f);
            Release();
            yield return Wait(0.3f);
            Assert.AreEqual(0, _crate.HolderCount);
            Assert.IsNull(Get<FurnitureGrabTarget>(_targeter, "_holdTarget"));
        }

        [UnityTest]
        public IEnumerator 장비는_서버가_잡기를_승인한_동안만_빛나고_해제와_사망에_꺼진다()
        {
            PlayerGrabToolVisuals presentation = CreateToolPresentation();
            yield return null;
            AssertToolLit(presentation, false);
            Aim(_crate.transform.position);
            yield return null;
            Press();
            yield return Wait(0.4f);
            Assert.AreEqual(1, _crate.HolderCount);
            AssertToolLit(presentation, true);

            _crate.ServerForceRelease(_network.LocalClientId);
            yield return null;
            yield return null;
            AssertToolLit(presentation, false);
            Release();
            yield return null;
            Press();
            yield return Wait(0.4f);
            AssertToolLit(presentation, true);
            Assert.IsTrue(_host.GetComponent<SanityNetworkState>().ServerMarkDead());
            yield return null;
            yield return null;
            AssertToolLit(presentation, false);
            foreach (Renderer part in Get<GrabToolView>(presentation, "_thirdPerson").GetComponentsInChildren<Renderer>())
                Assert.IsTrue(part.forceRenderingOff, "사망 시 장비 메시도 숨긴다.");
        }

        [UnityTest]
        public IEnumerator 서버가_잡기를_거절하면_장비가_빛나지_않는다()
        {
            PlayerGrabToolVisuals presentation = CreateToolPresentation();
            _camera.transform.SetParent(null);
            _camera.transform.position = new Vector3(-1f, 1.2f, 3f);
            Aim(_crate.transform.position);
            yield return null;
            Press();
            yield return Wait(0.4f);
            Assert.AreEqual(0, _crate.HolderCount);
            AssertToolLit(presentation, false);
            Release();
        }

        [UnityTest]
        public IEnumerator 소유자_카메라는_화면_장비만_다른_카메라는_손_장비만_그린다()
        {
            PlayerGrabToolVisuals presentation = CreateToolPresentation();
            GrabToolView first = Get<GrabToolView>(presentation, "_firstPerson");
            GrabToolView third = Get<GrabToolView>(presentation, "_thirdPerson");
            Camera ownerCamera = Get<Camera>(presentation, "_ownerCamera");
            Camera observer = Create("GrabToolObserver").AddComponent<Camera>();
            observer.transform.position = presentation.transform.position + new Vector3(1.6f, 1.1f, 1.5f);
            observer.transform.LookAt(presentation.transform.position + Vector3.up * 0.65f);
            observer.fieldOfView = 45f;
            var firstTarget = new RenderTexture(960, 540, 24);
            var thirdTarget = new RenderTexture(960, 540, 24);
            ownerCamera.targetTexture = firstTarget;
            observer.targetTexture = thirdTarget;
            ownerCamera.enabled = true;
            ownerCamera.clearFlags = observer.clearFlags = CameraClearFlags.SolidColor;
            ownerCamera.backgroundColor = observer.backgroundColor = new Color(0.085f, 0.10f, 0.13f);
            Light key = Create("GrabToolPreviewLight").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 2f;
            key.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            Renderer[] firstParts = first.GetComponentsInChildren<Renderer>();
            Renderer[] thirdParts = third.GetComponentsInChildren<Renderer>();
            SkinnedMeshRenderer body = presentation.GetComponentInChildren<SkinnedMeshRenderer>();
            bool ownerSeen = false;
            bool observerSeen = false;
            void OnRender(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
            {
                if (camera != ownerCamera && camera != observer)
                    return;
                bool own = camera == ownerCamera;
                foreach (Renderer part in firstParts)
                    Assert.AreEqual(!own, part.forceRenderingOff);
                foreach (Renderer part in thirdParts)
                    Assert.AreEqual(own, part.forceRenderingOff);
                // 생존 몸 숨김은 PlayerVisuals가 담당하므로 이 표현 전용 픽스처에서만 대신한다.
                body.forceRenderingOff = own;
                if (own) ownerSeen = true;
                else observerSeen = true;
            }
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnRender;
            try
            {
                yield return Wait(0.3f);
                Assert.IsTrue(ownerSeen && observerSeen, "실제 두 카메라 렌더를 거쳐야 한다.");
                SaveToolPreview(firstTarget, "FirstPerson_Idle");
                SaveToolPreview(thirdTarget, "ThirdPerson_Idle");
                Aim(_crate.transform.position);
                yield return null;
                Press();
                yield return Wait(0.4f);
                AssertToolLit(presentation, true);
                SaveToolPreview(firstTarget, "FirstPerson_Grab");
                SaveToolPreview(thirdTarget, "ThirdPerson_Grab");
                Release();
            }
            finally
            {
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnRender;
                ownerCamera.targetTexture = null;
                observer.targetTexture = null;
                Object.DestroyImmediate(firstTarget);
                Object.DestroyImmediate(thirdTarget);
            }
        }

        private static void SaveToolPreview(RenderTexture source, string name)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = source;
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            string directory = System.IO.Path.Combine(Application.dataPath, "../Logs/GrabToolPreview");
            System.IO.Directory.CreateDirectory(directory);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, name + ".png"), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        private PlayerGrabToolVisuals CreateToolPresentation()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            GameObject player = Object.Instantiate(prefab, new Vector3(20f, 0f, 20f), Quaternion.identity);
            _objects.Add(player);
            var presentation = player.GetComponent<PlayerGrabToolVisuals>();
            Assert.IsNotNull(presentation, "실제 Player 프리팹에 장비가 배선돼 있어야 한다.");
            // 표현 프리팹에는 이 픽스처의 실제 Host 상태를 주입한다.
            foreach (MonoBehaviour behaviour in player.GetComponents<MonoBehaviour>())
                if (behaviour != presentation)
                    behaviour.enabled = false;
            Set(presentation, "_grab", _host.GetComponent<GrabController>());
            Set(presentation, "_sanity", _host.GetComponent<SanityNetworkState>());
            return presentation;
        }

        private static void AssertToolLit(PlayerGrabToolVisuals presentation, bool expected)
        {
            foreach (string field in new[] { "_firstPerson", "_thirdPerson" })
            {
                GrabToolView view = Get<GrabToolView>(presentation, field);
                Renderer emitter = Get<Renderer>(view, "_emitter");
                var block = new MaterialPropertyBlock();
                emitter.GetPropertyBlock(block);
                Color emission = block.GetColor("_EmissionColor");
                Assert.AreEqual(expected, emission.maxColorComponent > 0.01f, field);
                Assert.AreEqual(Color.black, emitter.sharedMaterial.GetColor("_EmissionColor"),
                    "다른 플레이어와 공유하는 머티리얼은 바꾸지 않는다.");
            }
            Assert.AreEqual(expected, Get<Light>(Get<GrabToolView>(presentation, "_thirdPerson"), "_grabLight").enabled);
        }

        private void Aim(Vector3 worldPoint) => _camera.transform.LookAt(worldPoint);

        private static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
                yield return null;
        }

        private void Press() => InputSystem.QueueStateEvent(_mouse, new MouseState().WithButton(MouseButton.Left));
        private void Release() => InputSystem.QueueStateEvent(_mouse, new MouseState());

        private void SpawnHost(ulong ownerId)
        {
            _host = Create("GrabReachHost");
            _host.SetActive(false);
            _host.transform.position = Vector3.zero;
            AddNetworkObject(_host);

            var cameraObject = new GameObject("GrabReachHostCamera");
            cameraObject.transform.SetParent(_host.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            _camera = cameraObject.AddComponent<Camera>();

            var sanity = _host.AddComponent<SanityNetworkState>();
            Set(sanity, "_settings", _sanitySettings);
            var input = _host.AddComponent<PlayerInputReader>();
            Set(input, "_inputActions",
                AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/InputSystem_Actions.inputactions"));

            _targeter = _host.AddComponent<FurnitureTargeter>();
            Set(_targeter, "_camera", _camera);
            Set(_targeter, "_settings", _throwSettings);

            var grab = _host.AddComponent<GrabController>();
            Set(grab, "_input", input);
            Set(grab, "_camera", _camera);
            Set(grab, "_settings", _throwSettings);
            Set(grab, "_targeter", _targeter);

            _host.SetActive(true);
            _host.GetComponent<NetworkObject>().SpawnAsPlayerObject(ownerId);
            foreach (InputActionMap map in Get<InputActionAsset>(input, "_runtimeActions").actionMaps)
                map.devices = new InputDevice[] { _mouse };
        }

        private FurnitureGrabTarget SpawnFurniture(
            string name, Vector3 rootPosition, Vector3 colliderCenter, Vector3 colliderSize)
        {
            GameObject root = Create(name);
            root.SetActive(false);
            root.transform.position = rootPosition;
            root.layer = LayerMask.NameToLayer("Furniture");
            AddNetworkObject(root);
            root.AddComponent<Rigidbody>().isKinematic = true;

            var colliderObject = new GameObject(name + "_Collider")
            {
                layer = LayerMask.NameToLayer("Furniture"),
            };
            colliderObject.transform.SetParent(root.transform, false);
            BoxCollider box = colliderObject.AddComponent<BoxCollider>();
            box.center = colliderCenter;
            box.size = colliderSize;

            var target = root.AddComponent<FurnitureGrabTarget>();
            Set(target, "_settings", _throwSettings);
            root.SetActive(true);
            root.GetComponent<NetworkObject>().Spawn();
            return target;
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
