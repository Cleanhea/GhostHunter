using System;
using System.Collections.Generic;
using GhostHunter.Core.Networking;
using GhostHunter.Core.Scenes;
using GhostHunter.Core.Settings;
using GhostHunter.Core.Steam;
using GhostHunter.Core.Voice;
using GhostHunter.Data;
using GhostHunter.Gameplay.Voice;
using Steamworks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.Systems.Steam
{
    /// <summary>Lobby·Result에서 Steam 로비 멤버에게만 2D 음성을 보낸다.</summary>
    [DisallowMultipleComponent]
    public sealed class LobbyVoiceService : MonoBehaviour
    {
        private const int VoiceChannel = 7;
        private const int HeaderLength = 3;

        private readonly Dictionary<ulong, LobbySpeaker> _speakers = new(4);
        private readonly List<ulong> _removed = new(4);
        private readonly byte[] _outgoing = new byte[VoiceFrameQueue.MaximumPayload + HeaderLength];
        private readonly byte[] _incoming = new byte[VoiceFrameQueue.MaximumPayload + HeaderLength];
        private readonly byte[] _frame = new byte[VoiceFrameQueue.MaximumPayload];
        private readonly float[] _pcm = new float[24000];

        private ISteamLobbyService _lobby;
        private IVoiceCaptureService _capture;
        private ISceneFlow _sceneFlow;
        private IConnectionService _connection;
        private VoiceChatSettings _settings;
        private IUserSettings _userSettings;
        private VoiceChatService _chat;
        private LobbySpeaker _local;
        private bool _ownsSettings;
        private bool _active;
        private ushort _sequence;
        private double _nextSend;

        public void Initialize(ISteamLobbyService lobby, IVoiceCaptureService capture,
            ISceneFlow sceneFlow, IConnectionService connection, VoiceChatSettings settings,
            IUserSettings userSettings = null)
        {
            _userSettings = userSettings;
            _lobby = lobby;
            _capture = capture;
            _sceneFlow = sceneFlow;
            _connection = connection;
            _settings = settings;
            if (_settings == null)
            {
                _settings = ScriptableObject.CreateInstance<VoiceChatSettings>();
                _ownsSettings = true;
            }
            _lobby.LobbyUpdated += RefreshMembers;
            _lobby.LobbyLeft += StopSession;
        }

        private void Update()
        {
            if (_lobby == null || _capture == null || _sceneFlow == null)
                return;

            // 인게임 로비(ADR-0018)는 정산 화면과 같은 전원 채널이다 — 플레이어 송신기는 Game 에서만 켜진다.
            bool shouldRun = _lobby.IsInLobby && _capture.IsAvailable
                && (_sceneFlow.Current == SceneId.Result
                    || _sceneFlow.Current == SceneId.InGameLobby
                    || _sceneFlow.Current == SceneId.Lobby
                    && (_connection == null || !_connection.IsRunning));
            if (!shouldRun)
            {
                if (_active)
                    StopSession();
                return;
            }

            if (!_active)
                StartSession();

            if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
                _chat.IsMuted = !_chat.IsMuted;

            bool transmitting = !_chat.IsMuted && (_chat.Mode == VoiceMode.OpenMic
                || Keyboard.current != null && Keyboard.current.vKey.isPressed);
            _capture.SetRecording(transmitting);
            _chat.IsTransmitting = transmitting;

            ReceiveFrames();
            if (!transmitting || Time.unscaledTimeAsDouble < _nextSend)
                return;

            _nextSend = Time.unscaledTimeAsDouble + 1d / _settings.SendHz;
            int length = _capture.ReadFrame(_frame);
            if (length <= 0 || length > _outgoing.Length - HeaderLength)
                return;

            _outgoing[0] = (byte)_sequence;
            _outgoing[1] = (byte)(_sequence >> 8);
            _outgoing[2] = _capture.Codec;
            Buffer.BlockCopy(_frame, 0, _outgoing, HeaderLength, length);
            _sequence++;
            foreach (LobbySpeaker speaker in _speakers.Values)
                SteamNetworking.SendP2PPacket(speaker.ClientId, _outgoing,
                    length + HeaderLength, VoiceChannel, P2PSend.UnreliableNoDelay);
        }

        private void OnDisable() => StopSession();

        private void OnDestroy()
        {
            StopSession();
            if (_lobby != null)
            {
                _lobby.LobbyUpdated -= RefreshMembers;
                _lobby.LobbyLeft -= StopSession;
            }
            if (_ownsSettings && _settings != null)
                Destroy(_settings);
        }

        private void StartSession()
        {
            _chat = new VoiceChatService(_capture, _settings, _sceneFlow, allowLobby: true, _userSettings);
            _local = new LobbySpeaker(_lobby.LocalSteamId, _lobby.LocalName);
            _chat.Register(_local, true);
            _active = true;
            _sequence = 0;
            _nextSend = 0d;
            SteamNetworking.OnP2PSessionRequest += HandleSessionRequest;
            RefreshMembers();
        }

        private void StopSession()
        {
            if (!_active)
                return;

            _capture.SetRecording(false);
            _active = false;
            SteamNetworking.OnP2PSessionRequest -= HandleSessionRequest;
            foreach (LobbySpeaker speaker in _speakers.Values)
            {
                _chat.Unregister(speaker);
                // 같은 Steam P2P 세션의 채널 8을 공동 상점도 쓴다.
                // 음성 정리 시 피어 세션 전체를 닫지 않는다.
                if (speaker.Receiver != null)
                    Destroy(speaker.Receiver.gameObject);
            }
            _speakers.Clear();
            _chat.Unregister(_local);
            _chat = null;
            _local = null;
        }

        private void RefreshMembers()
        {
            if (!_active)
                return;

            IReadOnlyList<LobbyMemberInfo> members = _lobby.GetMembers();
            _removed.Clear();
            foreach (ulong id in _speakers.Keys)
            {
                bool stillPresent = false;
                for (int i = 0; i < members.Count; i++)
                    if (members[i].SteamId == id) { stillPresent = true; break; }
                if (!stillPresent)
                    _removed.Add(id);
            }
            foreach (ulong id in _removed)
            {
                LobbySpeaker speaker = _speakers[id];
                _chat.Unregister(speaker);
                if (speaker.Receiver != null)
                    Destroy(speaker.Receiver.gameObject);
                _speakers.Remove(id);
            }

            for (int i = 0; i < members.Count; i++)
            {
                LobbyMemberInfo member = members[i];
                if (member.SteamId == _lobby.LocalSteamId || _speakers.ContainsKey(member.SteamId))
                    continue;

                var speaker = new LobbySpeaker(member.SteamId, member.DisplayName);
                var root = new GameObject($"LobbyVoice_{member.SteamId}");
                root.transform.SetParent(transform, false);
                AudioSource source = root.AddComponent<AudioSource>();
                // 필터는 컴포넌트 순서대로 걸린다 — 로우패스가 VoiceReceiver 보다 뒤에 있어야 음성에 걸린다.
                VoiceReceiver receiver = root.AddComponent<VoiceReceiver>();
                AudioLowPassFilter filter = root.AddComponent<AudioLowPassFilter>();
                receiver.Configure(source, filter);
                receiver.Initialize(_settings, speaker, _chat, _capture.SampleRate);
                speaker.Receiver = receiver;
                _speakers.Add(member.SteamId, speaker);
                _chat.Register(speaker, false);
            }
        }

        private void HandleSessionRequest(SteamId sender)
        {
            if (_active && _speakers.ContainsKey(sender.Value))
                SteamNetworking.AcceptP2PSessionWithUser(sender);
        }

        private void ReceiveFrames()
        {
            for (int packet = 0; packet < 32 && SteamNetworking.IsP2PPacketAvailable(out uint available, VoiceChannel); packet++)
            {
                if (available < HeaderLength + 1 || available > _incoming.Length)
                {
                    SteamNetworking.ReadP2PPacket(VoiceChannel);
                    continue;
                }

                uint size = available;
                SteamId sender = default;
                if (!SteamNetworking.ReadP2PPacket(_incoming, ref size, ref sender, VoiceChannel)
                    || size < HeaderLength + 1 || !_speakers.TryGetValue(sender.Value, out LobbySpeaker speaker)
                    || _incoming[2] != _capture.Codec)
                    continue;

                ushort sequence = (ushort)(_incoming[0] | _incoming[1] << 8);
                if (speaker.HasSequence && (short)(sequence - speaker.LastSequence) <= 0)
                    continue;
                speaker.HasSequence = true;
                speaker.LastSequence = sequence;
                int length = (int)size - HeaderLength;
                Buffer.BlockCopy(_incoming, HeaderLength, _frame, 0, length);
                int samples = _capture.Decode(_frame, length, _pcm);
                if (samples > 0)
                    speaker.Receiver.Enqueue(_pcm, samples);
            }
        }

        private sealed class LobbySpeaker : IVoiceParticipant
        {
            public LobbySpeaker(ulong id, string name) { ClientId = id; DisplayName = name; }
            public ulong ClientId { get; }
            public bool IsAlive => true;
            public bool IsSpeaking => Receiver != null && Receiver.IsSpeaking;
            public Vector3 MouthPosition => Vector3.zero;
            public Transform Ear => null;
            public string DisplayName { get; }
            public float Volume { get; set; } = 1f;
            public VoiceReceiver Receiver { get; set; }
            public ushort LastSequence { get; set; }
            public bool HasSequence { get; set; }
        }
    }
}
