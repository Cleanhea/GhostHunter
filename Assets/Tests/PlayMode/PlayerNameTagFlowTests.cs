using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using GhostHunter.Core;
using GhostHunter.Core.Scenes;
using GhostHunter.Core.Steam;
using GhostHunter.Gameplay.Player;
using GhostHunter.UI;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// 머리 위 닉네임의 복제 흐름 — 소유자 요청 → 서버 정리·확정 → NetworkVariable.
    /// <see cref="VoiceFlowTests"/> 와 같이 Local Host 하나에 동적 NetworkObject 를 스폰한다.
    /// </summary>
    public sealed class PlayerNameTagFlowTests
    {
        private readonly List<GameObject> _objects = new();
        private readonly List<Object> _assets = new();
        private NetworkManager _network;
        private FakeLobby _lobby;
        private uint _hash = 0x6F200000;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameObject networkRoot = Create("NameTagTestNetwork");
            networkRoot.SetActive(false);
            _network = networkRoot.AddComponent<NetworkManager>();
            var transport = networkRoot.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", 17783, "127.0.0.1");
            _network.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
            networkRoot.SetActive(true);
            Assert.IsTrue(_network.StartHost());
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_network != null) { _network.Shutdown(); yield return null; }
            for (int i = _objects.Count - 1; i >= 0; i--) if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
            foreach (Object asset in _assets) if (asset != null) Object.DestroyImmediate(asset);
            _assets.Clear();
            if (_lobby != null) Services.Unbind<ISteamLobbyService>(_lobby);
            _lobby = null;
        }

        [UnityTest]
        public IEnumerator OwnerSteamName_IsSanitizedAndConfirmedByServer()
        {
            BindLobby(ready: true, "  고스트\n헌터​  ");
            PlayerNameTag tag = Spawn(0);
            yield return null;
            Assert.That(tag.DisplayName, Is.EqualTo("고스트헌터"));
        }

        [UnityTest]
        public IEnumerator WithoutSteam_ServerAssignsFallbackName()
        {
            BindLobby(ready: false, "(Steam 미연결)");
            PlayerNameTag tag = Spawn(0);
            yield return null;
            Assert.That(tag.DisplayName, Is.EqualTo("Player 0"), "Steam 미연결 안내 문구가 닉네임이 되면 안 된다");
        }

        [UnityTest]
        public IEnumerator SecondRequest_IsIgnored()
        {
            BindLobby(ready: true, "First");
            PlayerNameTag tag = Spawn(0);
            yield return null;
            Invoke(tag, "RequestDisplayNameRpc", new FixedString128Bytes("Second"));
            yield return null;
            Assert.That(tag.DisplayName, Is.EqualTo("First"));
        }

        [UnityTest]
        public IEnumerator RemoteOwnedPlayer_DoesNotRequestOnHostsBehalf()
        {
            BindLobby(ready: true, "HostName");
            PlayerNameTag remote = Spawn(999);
            yield return null;
            Assert.That(remote.DisplayName, Is.Empty, "호스트가 남의 플레이어에 자기 이름을 요청하면 안 된다");
        }

        [UnityTest]
        public IEnumerator View_ShowsRemotePlayerNameAboveEyes()
        {
            PlayerNameTagView view = SpawnWithView(999, out PlayerNameTag remote, out PlayerNameTagSettings settings);
            // 원격 소유 플레이어는 호스트가 요청하지 않으므로 서버가 직접 확정한 것처럼 값을 쓴다.
            var name = (NetworkVariable<FixedString128Bytes>)typeof(PlayerNameTag)
                .GetField("_displayName", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(remote);
            name.Value = new FixedString128Bytes("Remote");
            yield return null;
            yield return null;

            var label = (RectTransform)view.transform.Find("NameTag");
            Assert.IsTrue(label.GetComponent<Canvas>().enabled, "원격 플레이어 이름은 보여야 한다");
            Assert.That(label.GetComponentInChildren<UnityEngine.UI.Text>().text, Is.EqualTo("Remote"));
            Assert.That(label.localPosition.y, Is.EqualTo(settings.HeightAboveEyes).Within(1e-4f));
            // Canvas 기본값(Overlay)이 크기·배율을 덮어쓰면 글자가 화면 크기로 커진다.
            Assert.That(label.localScale.y, Is.EqualTo(settings.TextHeight / settings.FontSize).Within(1e-6f));
            Assert.That(label.sizeDelta.y, Is.EqualTo(settings.FontSize * 1.5f).Within(1e-3f));
        }

        [UnityTest]
        public IEnumerator View_HidesOwnName()
        {
            PlayerNameTagView view = SpawnWithView(0, out PlayerNameTag owner, out _);
            yield return null;
            yield return null;

            Assert.That(owner.DisplayName, Is.EqualTo("Player 0"));
            Assert.IsFalse(view.transform.Find("NameTag").GetComponent<Canvas>().enabled,
                "1인칭이라 자기 이름은 숨긴다");
        }

        /// <summary>
        /// 플레이어 화면 카메라 뒤에 다른 카메라가 같은 프레임에 그려도(에디터의 Scene 뷰가 그렇다) 이름표는
        /// 플레이어 화면을 정면으로 봐야 한다. 옆 카메라 쪽을 보면 화면에서 종이처럼 옆면이 보여 폭이 0에 가깝다.
        /// 화면은 읽을 수 없으니 같은 자리에 렌더 텍스처 카메라를 하나 더 둬서 본다.
        /// </summary>
        [UnityTest]
        public IEnumerator View_FacesPlayersScreenCamera_EvenWhenAnotherCameraRendersAfterIt()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("-nographics 에서는 렌더링을 검사할 수 없다.");

            RectTransform label = SpawnNamedRemote("WWWWWWWW");
            yield return null;

            // 이름표 기본 방향(월드 정면)과 겹치지 않게 비스듬히 본다 — 아무것도 안 돌려도 통과하지 않도록.
            Quaternion screenRotation = Quaternion.Euler(0f, 40f, 0f);
            Vector3 front = label.position - screenRotation * Vector3.forward * 3f;
            Camera screen = CreateCamera("NameTagScreenCamera", front, screenRotation, 0f, renderToTexture: false);
            Camera probe = CreateCamera("NameTagScreenProbe", front, screenRotation, 1f, renderToTexture: true);
            Quaternion sideRotation = screenRotation * Quaternion.Euler(0f, -90f, 0f);
            CreateCamera("NameTagSideCamera", label.position - sideRotation * Vector3.forward * 3f,
                sideRotation, 2f, renderToTexture: true);
            for (int i = 0; i < 5; i++)
                yield return null;

            Assert.That(Quaternion.Angle(label.rotation, screen.transform.rotation), Is.LessThan(0.5f),
                "이름표가 플레이어 화면 카메라가 아닌 다른 카메라 쪽을 보고 있다");
            int width = LitColumns(probe.targetTexture);
            Assert.Greater(width, 40, $"플레이어 화면에서 이름표 폭이 {width}px — 옆면이 보인다");
        }

        /// <summary>시선을 돌린 그 프레임에 바로 따라와야 한다 — 한 프레임 늦으면 빠르게 돌 때 비스듬히 보인다.</summary>
        [UnityTest]
        public IEnumerator View_FollowsScreenCameraTurnInSameFrame()
        {
            RectTransform label = SpawnNamedRemote("Remote");
            yield return null;

            Camera screen = CreateCamera("NameTagTurningCamera", label.position + new Vector3(0f, 0f, -3f),
                Quaternion.identity, 0f, renderToTexture: false);
            yield return null;
            yield return null;

            screen.transform.rotation = Quaternion.Euler(10f, 35f, 0f);
            yield return null;

            Assert.That(Quaternion.Angle(label.rotation, screen.transform.rotation), Is.LessThan(0.5f));
        }

        private RectTransform SpawnNamedRemote(string displayName)
        {
            PlayerNameTagView view = SpawnWithView(999, out PlayerNameTag remote, out _);
            var name = (NetworkVariable<FixedString128Bytes>)typeof(PlayerNameTag)
                .GetField("_displayName", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(remote);
            name.Value = new FixedString128Bytes(displayName);
            return (RectTransform)view.transform.Find("NameTag");
        }

        private Camera CreateCamera(string name, Vector3 position, Quaternion rotation, float depth, bool renderToTexture)
        {
            GameObject cameraObject = Create(name);
            cameraObject.transform.SetPositionAndRotation(position, rotation);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.fieldOfView = 60f;
            camera.depth = depth;
            if (renderToTexture)
            {
                var target = new RenderTexture(256, 256, 24);
                _assets.Add(target);
                camera.targetTexture = target;
            }

            return camera;
        }

        /// <summary>밝은(흰 글자) 픽셀이 하나라도 있는 열의 수 — 화면에 보이는 이름표 폭.</summary>
        private int LitColumns(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            _assets.Add(pixels);
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            pixels.Apply();
            RenderTexture.active = previous;

            int columns = 0;
            for (int x = 0; x < target.width; x++)
            {
                for (int y = 0; y < target.height; y++)
                {
                    if (pixels.GetPixel(x, y).grayscale > 0.5f)
                    {
                        columns++;
                        break;
                    }
                }
            }

            return columns;
        }

        private PlayerNameTagView SpawnWithView(ulong owner, out PlayerNameTag tag, out PlayerNameTagSettings settings)
        {
            var moveSettings = ScriptableObject.CreateInstance<PlayerMoveSettings>();
            settings = ScriptableObject.CreateInstance<PlayerNameTagSettings>();
            // 테스트 씬에는 카메라가 없어 이전 테스트의 시점이 남을 수 있다. 거리 제한을 끈다.
            Set(settings, "_maxVisibleDistance", 0f);
            _assets.Add(moveSettings);
            _assets.Add(settings);

            GameObject root = Create("NameTagViewTestPlayer");
            root.SetActive(false);
            var identity = root.AddComponent<NetworkObject>();
            typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(identity, ++_hash);
            var motor = root.AddComponent<PlayerMotor>();
            Set(motor, "_settings", moveSettings);
            tag = root.AddComponent<PlayerNameTag>();
            var view = root.AddComponent<PlayerNameTagView>();
            Set(view, "_settings", settings);
            Set(view, "_nameTag", tag);
            Set(view, "_motor", motor);
            root.SetActive(true);
            identity.SpawnWithOwnership(owner);
            return view;
        }

        private static void Set(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private void BindLobby(bool ready, string name)
        {
            _lobby = new FakeLobby(ready, name);
            Services.Bind<ISteamLobbyService>(_lobby);
        }

        private PlayerNameTag Spawn(ulong owner)
        {
            GameObject root = Create("NameTagTestPlayer");
            root.SetActive(false);
            var identity = root.AddComponent<NetworkObject>();
            typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(identity, ++_hash);
            var tag = root.AddComponent<PlayerNameTag>();
            root.SetActive(true);
            identity.SpawnWithOwnership(owner);
            return tag;
        }

        private GameObject Create(string name) { var value = new GameObject(name); _objects.Add(value); return value; }

        private static void Invoke(object target, string name, params object[] arguments)
            => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);

        private sealed class FakeLobby : ISteamLobbyService
        {
            public FakeLobby(bool ready, string name) { IsSteamReady = ready; LocalName = name; }
            public bool IsSteamReady { get; }
            public string LocalName { get; }
            public ulong LocalSteamId => 0UL;
            public bool IsInLobby => false;
            public bool IsLobbyOwner => false;
            public bool IsGameStarted => false;
            public bool IsGameLoading => false;
            public int ShopBalance => StageShopRules.StartingBalance;
            public int CandleCount => 0;
            public bool TryConsumeCandle() => false;
            public bool TryGrantStageReward() => false;
            public bool TrySetShopBalanceForDebug(int balance) => false;
            public MemberGear GetMemberGear(ulong steamId) => MemberGear.Starting;
            public bool TryPurchase(ShopItem item, ulong forSteamId) => false;
            public bool TryRepairDriver(ulong steamId) => false;
            public bool TrySaveDriverDurability(ulong steamId, int durability) => false;
            public int PublishedSettlementCount => 0;
            public bool TryPublishStageSettlement(StageSettlementRecord record) => false;
            public bool TryGetPublishedSettlement(int index, out StageSettlementRecord record)
            {
                record = default;
                return false;
            }
            public string CurrentRoomCode => string.Empty;
            public ulong CurrentHostSteamId => 0UL;
            public string CurrentStageId => string.Empty;
            public int CurrentHostGeneration => 0;
            public bool IsMigratedHostReady => false;
            public bool IsMigratedStageResumed => false;
            public bool TryGetAuthenticatedSteamId(ulong ngoClientId, out ulong steamId)
            {
                steamId = 0;
                return false;
            }
            public void MarkMigratedHostReady() { }
            public void MarkMigratedStageResumed() { }
            public event Action<string> StatusChanged { add { } remove { } }
            public event Action LobbyUpdated { add { } remove { } }
            public event Action HostLobbyReady { add { } remove { } }
            public event Action<ulong> JoinTargetResolved { add { } remove { } }
            public event Action LobbyLeft { add { } remove { } }
            public UniTask CreateLobbyAsync() => UniTask.CompletedTask;
            public UniTask JoinLobbyByCodeAsync(string rawCode) => UniTask.CompletedTask;
            public void OpenInviteOverlay() { }
            public bool TrySetConnectionTarget(ulong hostSteamId) => false;
            public void MarkGameStarted() { }
            public void MarkGameLoading() { }
            public void MarkGameEnded() { }
            public void LeaveLobby() { }
            public void SetLocalReady(bool ready) { }
            public bool AllGuestsReady() => true;
            public IReadOnlyList<LobbyMemberInfo> GetMembers() => Array.Empty<LobbyMemberInfo>();
            public UniTask<Texture2D> GetAvatarAsync(ulong steamId) => UniTask.FromResult<Texture2D>(null);
        }
    }
}
