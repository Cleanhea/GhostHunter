using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GhostHunter.Core;
using GhostHunter.Core.Voice;
using GhostHunter.Core.Scenes;
using GhostHunter.Data;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Sanity;
using GhostHunter.Gameplay.Voice;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    public sealed class VoiceFlowTests
    {
        private readonly List<GameObject> _objects = new();
        private NetworkManager _network;
        private VoiceChatSettings _settings;
        private SanitySystemSettings _sanitySettings;
        private VoiceChatService _chat;
        private LocalPlayerContext _local;
        private SanityTeamService _team;
        private FakeCapture _capture;
        private TestSceneFlow _sceneFlow;
        private uint _hash = 0x6F100000;
        private PlayerVoiceEmitter _owner;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _settings = ScriptableObject.CreateInstance<VoiceChatSettings>();
            _sanitySettings = ScriptableObject.CreateInstance<SanitySystemSettings>();
            _capture = new FakeCapture();
            _sceneFlow = new TestSceneFlow();
            _chat = new VoiceChatService(_capture, _settings, _sceneFlow) { TestDecoder = _capture };
            _local = new LocalPlayerContext();
            _team = Create("Team").AddComponent<SanityTeamService>();
            Services.Bind<IVoiceCaptureService>(_capture);
            Services.Bind<IVoiceChatService>(_chat);
            Services.Bind<ILocalPlayerContext>(_local);
            Services.Bind<ISanityTeamService>(_team);
            GameObject networkRoot = Create("VoiceTestNetwork");
            networkRoot.SetActive(false);
            _network = networkRoot.AddComponent<NetworkManager>();
            var transport = networkRoot.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", 17779, "127.0.0.1");
            _network.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
            networkRoot.SetActive(true);
            Assert.IsTrue(_network.StartHost());
            yield return null;
            _owner = Spawn(0);
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_network != null) { _network.Shutdown(); yield return null; }
            for (int i = _objects.Count - 1; i >= 0; i--) if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
            Services.Unbind<IVoiceCaptureService>(_capture);
            Services.Unbind<IVoiceChatService>(_chat);
            Services.Unbind<ILocalPlayerContext>(_local);
            Services.Unbind<ISanityTeamService>(_team);
            Object.DestroyImmediate(_settings);
            Object.DestroyImmediate(_sanitySettings);
        }
        [UnityTest]
        public IEnumerator OwnerPacket_PassesServerValidationButDoesNotEcho()
        {
            using (var packet = new NativeArray<byte>(new byte[] { 1, 0, 7 }, Allocator.Temp))
                Invoke(_owner, "SubmitVoiceRpc", packet, (byte)1, (ushort)1, true, default(RpcParams));
            yield return null;
            Assert.That(_owner.AcceptedPackets, Is.EqualTo(1));
            Assert.That(_owner.ReceivedPackets, Is.Zero);
        }
        [UnityTest]
        public IEnumerator SelfMonitor_ReturnsOwnVoiceThroughTheServer()
        {
            // 혼자 검증 경로 — 서버 검증·전송률·컬링을 그대로 지나 자기에게 되돌아와야 한다.
            _chat.SelfMonitor = true;
            using (var packet = new NativeArray<byte>(new byte[] { 1, 0, 7 }, Allocator.Temp))
                Invoke(_owner, "SubmitVoiceRpc", packet, (byte)1, (ushort)1, true, default(RpcParams));
            yield return null;
            Assert.That(_owner.AcceptedPackets, Is.EqualTo(1));
            Assert.That(_owner.ReceivedPackets, Is.EqualTo(1), "자가 모니터를 켜면 자기 목소리가 돌아와야 한다");
            Assert.That(_capture.DecodeCount, Is.EqualTo(1));
        }
        [UnityTest]
        public IEnumerator ServerRelay_DecodesOnHostAndRejectsReorderedOrWrongChannel()
        {
            PlayerVoiceEmitter remote = Spawn(999);
            var rpcTarget = (RpcTarget)typeof(NetworkBehaviour).GetProperty("RpcTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(remote);
            using (var packet = new NativeArray<byte>(new byte[] { 1, 0, 7 }, Allocator.Temp))
            {
                Invoke(remote, "PlayVoiceRpc", packet, (byte)1, (ushort)2, true, (RpcParams)rpcTarget.Single(0, RpcTargetUse.Temp));
                Invoke(remote, "PlayVoiceRpc", packet, (byte)1, (ushort)1, true, (RpcParams)rpcTarget.Single(0, RpcTargetUse.Temp));
                Invoke(remote, "PlayVoiceRpc", packet, (byte)1, (ushort)3, false, (RpcParams)rpcTarget.Single(0, RpcTargetUse.Temp));
            }
            yield return null;
            Assert.That(remote.ReceivedPackets, Is.EqualTo(1));
            Assert.That(_capture.DecodeCount, Is.EqualTo(1));
        }
        [UnityTest]
        public IEnumerator Death_RejectsOldLivingPackets_DespawnStopsCapture()
        {
            _owner.GetComponent<SanityNetworkState>().ServerMarkDead();
            using (var packet = new NativeArray<byte>(new byte[] { 1, 0, 7 }, Allocator.Temp))
                Invoke(_owner, "SubmitVoiceRpc", packet, (byte)1, (ushort)1, true, default(RpcParams));
            Assert.That(_owner.AcceptedPackets, Is.Zero);
            _capture.SetRecording(true);
            _owner.NetworkObject.Despawn();
            yield return null;
            Assert.IsFalse(_capture.IsRecording);
            Assert.That(_chat.Participants.Count, Is.Zero);
        }
        [UnityTest]
        public IEnumerator InGameLobby_PlayerEmitterDoesNotStopLobbyVoiceMicrophone()
        {
            // 인게임 로비에서는 LobbyVoiceService 가 같은 마이크를 켠다. 스폰된 내 플레이어 송신기가 매 프레임 꺼 버리면
            // Unity Microphone 이 껐다 켜지기를 반복해 아무 소리도 안 간다(2026-10-04 버그).
            _sceneFlow.Current = SceneId.InGameLobby;
            _capture.SetRecording(true);
            for (int i = 0; i < 3; i++)
                yield return null;
            Assert.IsTrue(_capture.IsRecording, "로비 음성이 켠 녹음을 플레이어 송신기가 꺼서는 안 된다.");
        }

        [UnityTest]
        public IEnumerator Result_JoinsDeadAndAliveVoice_ThenLobbySilencesIt()
        {
            typeof(VoiceChatService).GetField("_master", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_chat, 1f);
            PlayerVoiceEmitter remote = Spawn(999);
            Assert.IsTrue(remote.GetComponent<SanityNetworkState>().ServerMarkDead());
            var rpcTarget = (RpcTarget)typeof(NetworkBehaviour).GetProperty("RpcTarget",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(remote);
            // 중간에 yield 로 프레임을 넘기므로 Temp(프레임 끝에 자동 해제)가 아니라 Persistent 로 잡는다.
            using (var packet = new NativeArray<byte>(new byte[] { 1, 0, 7 }, Allocator.Persistent))
            {
                Invoke(remote, "PlayVoiceRpc", packet, (byte)1, (ushort)1, false,
                    (RpcParams)rpcTarget.Single(0, RpcTargetUse.Temp));
                Assert.That(remote.ReceivedPackets, Is.Zero, "Game에서는 생존자에게 사망자 음성이 들리지 않는다");

                _sceneFlow.Current = SceneId.Result;
                Invoke(remote, "PlayVoiceRpc", packet, (byte)1, (ushort)2, false,
                    (RpcParams)rpcTarget.Single(0, RpcTargetUse.Temp));
                yield return null;
                Assert.That(remote.ReceivedPackets, Is.EqualTo(1));
                Assert.That(remote.GetComponent<AudioSource>().spatialBlend, Is.Zero,
                    "Result 공용 채널은 위치와 무관한 2D 음성이다");

                _sceneFlow.Current = SceneId.Lobby;
                Invoke(remote, "PlayVoiceRpc", packet, (byte)1, (ushort)3, false,
                    (RpcParams)rpcTarget.Single(0, RpcTargetUse.Temp));
                Assert.That(remote.ReceivedPackets, Is.EqualTo(1));
                Assert.IsFalse(_chat.IsActive);
            }
        }
        private PlayerVoiceEmitter Spawn(ulong owner)
        {
            GameObject root = Create("VoiceTestPlayer");
            root.SetActive(false);
            var identity = root.AddComponent<NetworkObject>();
            typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(identity, ++_hash);
            var sanity = root.AddComponent<SanityNetworkState>();
            Set(sanity, "_settings", _sanitySettings);
            var source = root.AddComponent<AudioSource>();
            source.playOnAwake = false;
            // 프리팹과 같은 순서 — 수신기의 OnAudioFilterRead 가 로우패스보다 먼저 와야 한다.
            var receiver = root.AddComponent<VoiceReceiver>();
            var filter = root.AddComponent<AudioLowPassFilter>();
            Set(receiver, "_source", source); Set(receiver, "_filter", filter);
            var emitter = root.AddComponent<PlayerVoiceEmitter>();
            Set(emitter, "_settings", _settings); Set(emitter, "_sanity", sanity); Set(emitter, "_receiver", receiver);
            root.SetActive(true);
            identity.SpawnWithOwnership(owner);
            return emitter;
        }
        private GameObject Create(string name) { var value = new GameObject(name); _objects.Add(value); return value; }
        private static void Set(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void Invoke(object target, string name, params object[] arguments)
            => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);
        private sealed class TestSceneFlow : ISceneFlow
        {
            public SceneId Current { get; set; } = SceneId.Stage1;
            public bool IsLoading => false;
            public int StageFailureDeadCount => 0;
            public System.Collections.Generic.IReadOnlyList<StageSettlementRecord> SettlementHistory =>
                System.Array.Empty<StageSettlementRecord>();
            public event System.Action<SceneId> SceneChanged;
            public void RecordStageFailure(int deadCount) { }
            public void RecordStageSettlement(StageSettlementRecord record) { }
            public void Load(SceneId scene)
            {
                Current = scene;
                SceneChanged?.Invoke(scene);
            }
        }
        private sealed class FakeCapture : IVoiceCaptureService
        {
            public bool IsAvailable => true;
            public bool IsRecording { get; private set; }
            public string Status => "Test";
            public byte Codec => 1;
            public int SampleRate => 24000;
            public int DecodeCount { get; private set; }
            public float InputLevelDb => -120f;
            public bool IsGateOpen => IsRecording;
            public bool IsMonitoring { get; set; }
            public void SetRecording(bool value) => IsRecording = value;
            public int ReadFrame(byte[] destination) { destination[0] = 7; return IsRecording ? 1 : 0; }
            // 디코더는 화자마다 따로 만들어지지만, 테스트는 몇 번 풀었는지 한곳에서 센다.
            public IVoiceDecoder CreateDecoder() => new CountingDecoder(this);
            private sealed class CountingDecoder : IVoiceDecoder
            {
                private readonly FakeCapture _owner;
                public CountingDecoder(FakeCapture owner) => _owner = owner;
                public int Decode(byte[] source, int count, float[] destination)
                { _owner.DecodeCount++; for (int i = 0; i < 2400; i++) destination[i] = 0.1f; return 2400; }
            }
        }
    }
}
