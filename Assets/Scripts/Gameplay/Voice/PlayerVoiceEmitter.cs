using System;
using System.Collections.Generic;
using GhostHunter.Core;
using GhostHunter.Core.Voice;
using GhostHunter.Core.Scenes;
using GhostHunter.Data;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Sanity;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Voice
{
    /// <summary>소유자의 음성을 서버에서 검증·컬링해 가청 대상에게만 중계한다.</summary>
    public sealed class PlayerVoiceEmitter : NetworkBehaviour, IVoiceParticipant
    {
        [SerializeField] private VoiceChatSettings _settings;
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private PlayerLook _look;
        [SerializeField] private PlayerMotor _motor;
        [SerializeField] private SanityNetworkState _sanity;
        [SerializeField] private VoiceReceiver _receiver;
        private readonly byte[] _frame = new byte[VoiceFrameQueue.MaximumPayload];
        private readonly byte[] _packet = new byte[VoiceFrameQueue.MaximumPayload];
        private readonly float[] _pcm = new float[24000];
        private readonly VoiceActivityGate _speech = new();
        private readonly VoiceFrameQueue _pending = new();
        private readonly List<ulong> _targets = new(4);
        private readonly VoicePacketLimiter _limiter = new();
        private IVoiceChatService _chat;
        private IVoiceCaptureService _capture;
        private IVoiceCaptureService _activeCapture;
        // 이 송신기가 SetRecording(true) 로 마이크를 켰는가 — 스테이지 밖에서 남의 녹음을 끄지 않으려고 기억한다.
        private bool _ownsRecording;
        // Opus 디코더는 상태를 가진다. 이 화자 전용으로 코덱마다 하나씩 둔다.
        private IVoiceDecoder _decoder;
        private IVoiceDecoder _testDecoder;
        private IVoiceDecoder _localDecoder;
        private double _nextSend;
        private double _lastCapture;
        private double _nextRateWarning;
        private ushort _sequence;
        private ushort _lastReceivedSequence;
        private bool _hasSequence;
        private double _lastReceivedTime;
        private bool _wasMuted;
        private VoiceMode _lastMode;
        private float _volume = 1f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly EchoSpeaker _echo = new();
        private bool _echoReady;
        private bool _wasSelfMonitor;
        private float _level = -120f;
#endif
        internal int AcceptedPackets { get; private set; }
        internal int ReceivedPackets { get; private set; }
        public ulong ClientId => OwnerClientId;
        public bool IsAlive => _sanity != null && _sanity.HasSanity;
        /// <summary>"말하는 중" 표시용. 송신 여부와 별개로 소리가 발화 표시 임계값을 넘었는지 본다.</summary>
        public bool IsSpeaking => IsOwner ? _chat != null && _chat.IsTransmitting && _speech.IsOpen : _receiver != null && _receiver.IsSpeaking;
        public Vector3 MouthPosition => transform.position + Vector3.up * (_motor != null ? _motor.CameraLocalHeight : 0f);
        public Transform Ear => _look != null && _look.PlayerCamera != null ? _look.PlayerCamera.transform : null;
        public string DisplayName { get; private set; }
        public float Volume { get => _volume; set { _volume = Mathf.Clamp01(value); if (_volume == 0f && _receiver != null) _receiver.Flush(); } }
        public override void OnNetworkSpawn()
        {
            if (_settings == null || _receiver == null || _sanity == null)
            { Debug.LogError("[PlayerVoiceEmitter] 음성 설정/재생/상태 배선 누락", this); enabled = false; return; }
            _chat = Services.Get<IVoiceChatService>();
            _capture = Services.Get<IVoiceCaptureService>();
            DisplayName = IsOwner ? "나" : $"Player {OwnerClientId}";
            _chat.Register(this, IsOwner);
            _sanity.AliveStateChanged += HandleAliveChanged;
            if (IsClient && !IsOwner) _receiver.Initialize(_settings, this, _chat, _capture.SampleRate);
            if (IsOwner) { _activeCapture = _chat.Capture; _localDecoder = _activeCapture.CreateDecoder(); _lastMode = _chat.Mode; }
        }
        public override void OnNetworkDespawn()
        {
            if (_sanity != null) _sanity.AliveStateChanged -= HandleAliveChanged;
            if (IsOwner) StopCapture();
            _chat?.Unregister(this);
            _chat = null;
            if (_receiver != null) _receiver.Flush();
            _limiter.Clear();
            _hasSequence = false;
        }
        private void OnDisable()
        {
            if (IsOwner) StopCapture();
        }
        private void Update()
        {
            if (!IsSpawned || !IsOwner || _chat == null) return;
            // 스테이지 밖(인게임 로비·정산)은 LobbyVoiceService 가 같은 마이크로 전원 채널을 송신한다. 여기서 매 프레임
            // 녹음을 끄면 Unity Microphone 이 프레임마다 껐다 켜져 소리가 하나도 모이지 않는다(2026-10-04 인게임 로비 마이크 불통).
            // 그래서 이 송신기가 켠 녹음만 놓는다.
            if (Services.TryGet(out ISceneFlow sceneFlow) && !sceneFlow.Current.IsStage())
            {
                ReleaseCapture();
                return;
            }
            if (_input == null) return;
            if (!_chat.IsActive) { StopCapture(); return; }
            if (_input.VoiceMutePressedThisFrame) _chat.IsMuted = !_chat.IsMuted;
            if (!ReferenceEquals(_activeCapture, _chat.Capture) || _lastMode != _chat.Mode || _wasMuted != _chat.IsMuted)
            {
                StopCapture();
                _activeCapture = _chat.Capture;
                _localDecoder = _activeCapture.CreateDecoder();
                _lastMode = _chat.Mode;
                _wasMuted = _chat.IsMuted;
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UpdateSelfMonitor();
#endif
            if (_chat.IsMuted || !_activeCapture.IsAvailable) { StopCapture(); return; }
            _activeCapture.SetRecording(true);
            _ownsRecording = true;
            double now = Time.unscaledTimeAsDouble;
            if (now < _nextSend) return;
            _nextSend = now + 1d / _settings.SendHz;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _level = -120f;
#endif
            // 20Hz 고정 송신이다. 렌더 프레임마다 RPC를 보내지 않는다 (기획 §5.2).
            // 소리 크기로 송신을 거르지 않는다(2026-09-27 사용자 결정) — 오픈 마이크는 잡힌 소리를 전부,
            // PTT는 누르는 동안 전부 보낸다. 레벨 판정은 "말하는 중" 표시에만 쓴다.
            int count = _activeCapture.ReadFrame(_frame);
            bool transmit = ShouldTransmit(_chat.Mode, _input.VoiceHeld);
            if (count > 0)
            {
                _lastCapture = now;
                UpdateSpeech(count);
                if (transmit) _pending.Push(_frame, count, now);
            }
            else if (now - _lastCapture > _settings.HangoverSeconds) _speech.Reset();
            if (!transmit) _pending.Clear();
            int bytes = transmit ? _pending.Pack(_packet) : 0;
            _chat.IsTransmitting = transmit;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UpdateDiagnostics(bytes);
#endif
            if (bytes == 0) return;
            using var payload = new NativeArray<byte>(bytes, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<byte>.Copy(_packet, payload, bytes);
            SubmitVoiceRpc(payload, _activeCapture.Codec, _sequence++, IsAlive);
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner, Delivery = RpcDelivery.Unreliable)]
        private void SubmitVoiceRpc(NativeArray<byte> frame, byte codec, ushort sequence, bool aliveChannel, RpcParams rpcParams = default)
        {
            if (!IsServer || _chat == null || !_chat.IsActive || rpcParams.Receive.SenderClientId != OwnerClientId ||
                !ValidatePacket(frame) || !IsCodecAllowed(codec) || aliveChannel != IsAlive) return;
            double now = Time.unscaledTimeAsDouble;
            if (!_limiter.Accept(now, _settings.ServerPacketsPerSecond))
            {
                if (now >= _nextRateWarning)
                { Debug.LogWarning($"[Voice] client {OwnerClientId}: 전송률 상한 초과", this); _nextRateWarning = now + 1d; }
                return;
            }
            AcceptedPackets++;
            _targets.Clear();
            for (int index = 0; index < _chat.Participants.Count; index++)
            {
                IVoiceParticipant listener = _chat.Participants[index];
                if (!NetworkManager.ConnectedClients.ContainsKey(listener.ClientId)) continue;
                if (listener.ClientId == OwnerClientId)
                {
                    // 평소에는 화자에게 자기 목소리를 되돌리지 않는다(기획 §5.3 ③).
                    // 자가 모니터를 켠 호스트만 예외 — 혼자 검증하려면 되돌아와야 한다.
                    if (SelfMonitorActive) _targets.Add(listener.ClientId);
                    continue;
                }
                Vector3 delta = MouthPosition - listener.MouthPosition;
                if (VoiceAttenuation.CanRelay(IsAlive, listener.IsAlive, new Vector2(delta.x, delta.z).magnitude,
                    delta.y, _settings.MaximumDistance, _settings.VerticalCut, _settings.ServerMarginXZ,
                    _settings.ServerMarginY, _chat.IsResultChannel))
                    _targets.Add(listener.ClientId);
            }
            if (_targets.Count > 0) PlayVoiceRpc(frame, codec, sequence, aliveChannel, RpcTarget.Group(_targets, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server, Delivery = RpcDelivery.Unreliable)]
        private void PlayVoiceRpc(NativeArray<byte> frame, byte codec, ushort sequence, bool aliveChannel, RpcParams rpcParams = default)
        {
            if (!IsClient || _chat == null || !_chat.IsActive || rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId ||
                !ValidatePacket(frame) || !IsCodecAllowed(codec) || aliveChannel != IsAlive) return;
            if (IsOwner && !SelfMonitorActive) return;
            IVoiceDecoder decoder = DecoderFor(codec);
            if (decoder == null) return;
            if (_hasSequence && Time.unscaledTimeAsDouble - _lastReceivedTime < 2d && (short)(sequence - _lastReceivedSequence) <= 0) return;
            _lastReceivedTime = Time.unscaledTimeAsDouble;
            _hasSequence = true;
            _lastReceivedSequence = sequence;
            IVoiceParticipant listener = _chat.LocalParticipant;
            if (listener == null || (!_chat.IsResultChannel && listener.IsAlive != IsAlive) || Volume <= 0f) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (IsOwner && !EnsureEcho()) return;
#endif
            ReceivedPackets++;
            int offset = 0;
            while (offset < frame.Length)
            {
                int count = frame[offset] | frame[offset + 1] << 8;
                offset += 2;
                for (int i = 0; i < count; i++) _frame[i] = frame[offset + i];
                offset += count;
                int samples = decoder.Decode(_frame, count, _pcm);
                _receiver.Enqueue(_pcm, samples);
            }
        }
        internal static bool ValidatePacket(NativeArray<byte> frame)
        {
            if (!frame.IsCreated || frame.Length < 3 || frame.Length > VoiceFrameQueue.MaximumPayload) return false;
            int offset = 0;
            int blocks = 0;
            while (offset < frame.Length)
            {
                if (++blocks > 8) return false;
                if (offset + 2 > frame.Length) return false;
                int count = frame[offset] | frame[offset + 1] << 8;
                offset += 2;
                if (count == 0 || count > frame.Length - offset) return false;
                offset += count;
            }
            return true;
        }
        private static bool IsCodecAllowed(byte codec)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return codec == VoiceCodecs.Opus || codec == VoiceCodecs.TestTone;
#else
            return codec == VoiceCodecs.Opus;
#endif
        }
        /// <summary>이 화자의 디코더. 처음 받은 코덱에서 만든다. 개발용 사인파는 HUD 가 건 테스트 디코더로 푼다.</summary>
        private IVoiceDecoder DecoderFor(byte codec)
        {
            if (codec == _capture.Codec) return _decoder ??= _capture.CreateDecoder();
            IVoiceCaptureService test = _chat.TestDecoder;
            if (test == null || test.Codec != codec) return null;
            return _testDecoder ??= test.CreateDecoder();
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private bool SelfMonitorActive => _chat != null && _chat.SelfMonitor;
        /// <summary>자가 모니터 토글을 처리한다. <b>켠 자리</b>가 테스트 스피커가 된다 — 거기서 걸어
        /// 나가며 거리·벽·층 감쇠를 혼자 듣는다. 자리를 옮기려면 껐다 켠다.</summary>
        private void UpdateSelfMonitor()
        {
            if (_chat.SelfMonitor == _wasSelfMonitor) return;
            _wasSelfMonitor = _chat.SelfMonitor;
            if (_chat.SelfMonitor) _echo.Bind(this, MouthPosition);
            else { _receiver.Flush(); _chat.Diagnostics = null; }
        }
        /// <summary>자가 모니터용 재생 경로를 준비한다. 소유자는 평소에 수신하지 않으므로 여기서 연다.</summary>
        private bool EnsureEcho()
        {
            if (_echoReady) return true;
            if (_activeCapture == null) return false;
            _echo.Bind(this, MouthPosition);
            // 클립은 한 번만 만든다. 토글할 때마다 만들면 AudioClip 이 샌다.
            _receiver.Initialize(_settings, _echo, _chat, _activeCapture.SampleRate);
            _echoReady = true;
            return true;
        }
        private void UpdateDiagnostics(int bytes)
        {
            string mode = _chat.Mode == VoiceMode.OpenMic ? "오픈 마이크" : $"PTT {(_input.VoiceHeld ? "누름" : "뗌")}";
            string line = $"{mode} · 입력 {_level:F0} dBFS · 발화 {(_speech.IsOpen ? "감지" : "없음")} · " +
                          $"{(bytes > 0 ? bytes + "B 송신" : "송신 없음")}";
            _chat.Diagnostics = SelfMonitorActive
                ? $"{line}\n스피커까지 {_receiver.Distance:F1} m · 음량 {_receiver.Gain:F2} · " +
                  $"가림 {_receiver.Occlusion:F2} · 컷오프 {_receiver.Cutoff:F0} Hz"
                : line;
        }
        /// <summary>자가 모니터가 쓰는 고정 위치 화자. 생사 채널은 본인을 따라간다.</summary>
        private sealed class EchoSpeaker : IVoiceParticipant
        {
            private PlayerVoiceEmitter _owner;
            private Vector3 _position;
            public void Bind(PlayerVoiceEmitter owner, Vector3 position) { _owner = owner; _position = position; }
            public ulong ClientId => _owner != null ? _owner.OwnerClientId : 0;
            public bool IsAlive => _owner != null && _owner.IsAlive;
            public bool IsSpeaking => false;
            public Vector3 MouthPosition => _position;
            public Transform Ear => null;
            public string DisplayName => "자가 모니터 스피커";
            public float Volume { get => 1f; set { } }
        }
#else
        private bool SelfMonitorActive => false;
#endif
        /// <summary>송신 여부는 모드와 PTT 키만으로 정한다. 소리 크기는 보지 않는다.</summary>
        internal static bool ShouldTransmit(VoiceMode mode, bool pushToTalkHeld)
            => mode == VoiceMode.OpenMic || pushToTalkHeld;
        /// <summary>
        /// 방금 잡은 블록을 로컬에서 풀어 "말하는 중" 표시와 개발 진단 레벨만 갱신한다.
        /// 송신에는 관여하지 않는다 — 원시 PCM은 네트워크에 보내지 않는다.
        /// </summary>
        private void UpdateSpeech(int count)
        {
            int samples = _localDecoder.Decode(_frame, count, _pcm);
            int window = Math.Max(1, _activeCapture.SampleRate / 50);
            for (int offset = 0; offset < samples; offset += window)
            {
                int length = Math.Min(window, samples - offset);
                float decibels = VoiceActivityGate.Decibels(_pcm, offset, length);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                _level = Math.Max(_level, decibels);
#endif
                _speech.Step(decibels, length / (float)_activeCapture.SampleRate,
                    _settings.OpenThreshold, _settings.CloseThreshold, _settings.HangoverSeconds);
            }
        }
        private void HandleAliveChanged(bool alive)
        {
            if (_receiver != null) _receiver.Flush();
            if (IsOwner) { _pending.Clear(); _speech.Reset(); _chat.IsTransmitting = false; }
        }
        /// <summary>이 송신기가 켠 녹음이면 끈다. 다른 송신기(로비 음성)가 쓰는 마이크는 건드리지 않는다.</summary>
        private void ReleaseCapture()
        {
            if (_ownsRecording)
            {
                StopCapture();
                return;
            }

            _pending.Clear();
            _speech.Reset();
        }

        private void StopCapture()
        {
            _ownsRecording = false;
            _activeCapture?.SetRecording(false);
            _pending.Clear();
            _speech.Reset();
            if (_chat != null) _chat.IsTransmitting = false;
        }
    }
}

