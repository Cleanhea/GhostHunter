using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GhostHunter.Core;
using GhostHunter.Core.Voice;
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
        private uint _hash = 0x6F100000;
        private PlayerVoiceEmitter _owner;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _settings = ScriptableObject.CreateInstance<VoiceChatSettings>();
            _sanitySettings = ScriptableObject.CreateInstance<SanitySystemSettings>();
            _capture = new FakeCapture();
            _chat = new VoiceChatService(_capture, _settings) { TestDecoder = _capture };
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
            var filter = root.AddComponent<AudioLowPassFilter>();
            var receiver = root.AddComponent<VoiceReceiver>();
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
        private sealed class FakeCapture : IVoiceCaptureService
        {
            public bool IsAvailable => true;
            public bool IsRecording { get; private set; }
            public string Status => "Test";
            public byte Codec => 1;
            public int SampleRate => 24000;
            public int DecodeCount { get; private set; }
            public void SetRecording(bool value) => IsRecording = value;
            public bool OpenSettings() => false;
            public int ReadFrame(byte[] destination) { destination[0] = 7; return IsRecording ? 1 : 0; }
            public int Decode(byte[] source, int count, float[] destination)
            { DecodeCount++; for (int i = 0; i < 2400; i++) destination[i] = 0.1f; return 2400; }
        }
    }
}
