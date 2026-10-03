using System;
using Concentus.Structs;
using GhostHunter.Core.Settings;
using GhostHunter.Core.Voice;
using GhostHunter.Data;
using GhostHunter.Gameplay.Voice;
using UnityEngine;

namespace GhostHunter.Systems.Voice
{
    /// <summary>
    /// Unity Microphone 캡처 → 48kHz → 게인 → 노이즈 게이트 → Opus(Concentus) 압축.
    /// 입력 장치·게인·게이트는 개인 설정을 따른다. Bootstrap 에 상주하며 BootstrapInstaller 가 초기화·등록한다.
    ///
    /// <para>2026-10-03 Steam Voice 를 대체했다(ADR-0022) — 장치 선택·게인을 게임 안에서 바꾸려면 압축 전 PCM 이
    /// 필요한데 Steam 은 압축된 블록만 준다. 처리 흐름은 docs/architecture/voice-chat.md "캡처".</para>
    /// </summary>
    public sealed class MicrophoneVoiceCapture : MonoBehaviour, IVoiceCaptureService
    {
        private const float SilenceDb = -120f;
        private const float LevelHoldSeconds = 0.3f;
        private const int ClipSeconds = 1;
        // 프레임이 멈췄다 돌아오면(씬 로드) 마이크 클립에 밀린 소리가 쌓인다. 이보다 오래된 것은 버린다 — 늦은 목소리보다 낫다.
        private const float MaximumBacklogSeconds = 0.2f;
        // 아무도 읽지 않을 때 쌓아 두는 압축 프레임 수(200ms).
        private const int MaximumQueuedFrames = 10;
        private const float DeviceCheckSeconds = 1f;
        private const float FrameSeconds = 0.02f;
        private const string DefaultDeviceLabel = "기본 장치";

        private readonly float[] _frame = new float[OpusVoiceBlock.FrameSamples];
        private readonly float[] _pending = new float[OpusVoiceBlock.SampleRate];
        private readonly byte[] _encoded = new byte[OpusVoiceBlock.MaximumFrameBytes];
        private readonly short[] _encodeScratch = new short[OpusVoiceBlock.FrameSamples];
        private readonly FrameRing _queue = new(MaximumQueuedFrames);
        private readonly FrameRing _preroll = new(10);
        private readonly VoiceNoiseGate _gate = new();

        private IUserSettings _userSettings;
        private VoiceChatSettings _settings;
        private bool _ownsSettings;
        private OpusEncoder _encoder;
        private MicrophoneMonitorPlayer _monitor;

        private AudioClip _clip;
        private string _device;
        private string _requestedDevice;
        private int _readPosition;
        private float[] _chunk = Array.Empty<float>();
        private float[] _resampled = Array.Empty<float>();
        private VoiceResampler _resampler;
        private int _pendingCount;

        private bool _hasDevices;
        private float _nextDeviceCheck;
        private float _nextStartAttempt;
        private string _failure;
        private string _recordingStatus = string.Empty;
        private float _levelDb = SilenceDb;
        private float _levelTime = float.NegativeInfinity;
        private bool _monitoring;

        public bool IsAvailable => _failure == null && _hasDevices;
        public bool IsRecording { get; private set; }
        public string Status => _failure != null ? "마이크를 열 수 없음" : !_hasDevices ? "마이크 장치 없음"
            : IsRecording ? _recordingStatus : "마이크 꺼짐";
        public byte Codec => VoiceCodecs.Opus;
        public int SampleRate => OpusVoiceBlock.SampleRate;
        public float InputLevelDb => IsRecording && Time.unscaledTime - _levelTime <= LevelHoldSeconds ? _levelDb : SilenceDb;
        public bool IsGateOpen => IsRecording && (!IsGateActive || _gate.IsOpen);

        public bool IsMonitoring
        {
            get => _monitoring;
            set
            {
                _monitoring = value;
                if (!value && _monitor != null)
                    _monitor.Stop();
            }
        }

        /// <summary>오픈 마이크에서만 거른다. 눌러서 말하기는 누른 것 자체가 말하겠다는 뜻이다.</summary>
        private bool IsGateActive => _userSettings != null && _userSettings.NoiseGateEnabled
            && _userSettings.VoiceMode == VoiceMode.OpenMic;

        private void Awake()
        {
            CheckDevices();
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextDeviceCheck)
                CheckDevices();

            if (!IsRecording)
                return;

            // 설정 창에서 장치를 바꾸면 녹음을 다시 연다.
            string requested = _userSettings != null ? _userSettings.MicDevice : string.Empty;
            if (requested != _requestedDevice || !Microphone.IsRecording(_device))
            {
                StopMicrophone();
                if (!StartMicrophone())
                {
                    IsRecording = false;
                    _nextStartAttempt = Time.unscaledTime + DeviceCheckSeconds;
                    return;
                }
            }

            ReadMicrophone();
        }

        private void OnDisable() => SetRecording(false);

        private void OnDestroy()
        {
            SetRecording(false);
            if (_ownsSettings && _settings != null)
                Destroy(_settings);
        }

        /// <param name="userSettings">장치·게인·게이트. 없으면(단독 실행) 기본 장치·게인 0·게이트 없음.</param>
        /// <param name="settings">게이트 여유·유지·앞당김과 Opus 비트레이트. 없으면 기본값 에셋을 만든다.</param>
        public void Initialize(IUserSettings userSettings, VoiceChatSettings settings)
        {
            _userSettings = userSettings;
            _settings = settings;
            if (_settings == null)
            {
                _settings = ScriptableObject.CreateInstance<VoiceChatSettings>();
                _ownsSettings = true;
            }
        }

        public void SetRecording(bool recording)
        {
            if (recording == IsRecording)
                return;

            if (!recording)
            {
                StopMicrophone();
                IsRecording = false;
                return;
            }

            // 장치가 열리지 않을 때 매 프레임 다시 시도하며 경고를 쏟지 않게 1초 쉰다.
            if (!IsAvailable || Time.unscaledTime < _nextStartAttempt)
                return;

            IsRecording = StartMicrophone();
            if (!IsRecording)
                _nextStartAttempt = Time.unscaledTime + DeviceCheckSeconds;
        }

        public int ReadFrame(byte[] destination)
        {
            if (!IsRecording)
                return 0;

            int written = 0;
            while (_queue.Count > 0)
            {
                int length = _queue.PeekLength();
                if (written + 1 + length > destination.Length)
                    break;

                destination[written++] = (byte)length;
                written += _queue.Dequeue(destination, written);
            }

            return written;
        }

        public IVoiceDecoder CreateDecoder() => new OpusVoiceBlock.Decoder();

        #region 마이크

        private void CheckDevices()
        {
            _nextDeviceCheck = Time.unscaledTime + DeviceCheckSeconds;
            // 1초에 한 번만 — Microphone.devices 는 부를 때마다 배열을 새로 만든다.
            _hasDevices = Microphone.devices.Length > 0;
        }

        private bool StartMicrophone()
        {
            try
            {
                _requestedDevice = _userSettings != null ? _userSettings.MicDevice : string.Empty;
                _device = ResolveDevice(_requestedDevice);
                Microphone.GetDeviceCaps(_device, out int minimum, out int maximum);
                // 0,0 은 아무 주파수나 된다는 뜻이다. 가능하면 48kHz 로 받아 리샘플을 피한다(대부분 장치의 기본값).
                int rate = minimum == 0 && maximum == 0 ? SampleRate : Mathf.Clamp(SampleRate, minimum, maximum);
                _clip = Microphone.Start(_device, true, ClipSeconds, rate);
                if (_clip == null)
                {
                    Debug.LogWarning($"[MicrophoneVoiceCapture] 마이크를 열지 못했다: {_device ?? DefaultDeviceLabel}", this);
                    return false;
                }

                int deviceRate = _clip.frequency;
                _chunk = new float[Math.Max(1, deviceRate / 50)];
                _resampler = deviceRate == SampleRate ? null : new VoiceResampler(deviceRate, SampleRate);
                _resampled = _resampler != null ? new float[_resampler.MaximumOutput(_chunk.Length)] : Array.Empty<float>();
                _readPosition = Math.Max(0, Microphone.GetPosition(_device));
                _pendingCount = 0;
                _queue.Clear();
                _preroll.Clear();
                _gate.Reset();
                _encoder ??= OpusVoiceBlock.CreateEncoder(_settings.OpusBitrate, _settings.OpusComplexity);
                _recordingStatus = "마이크 켜짐 · " + (_device ?? DefaultDeviceLabel);
                return true;
            }
            catch (Exception exception)
            {
                if (_failure == null)
                    Debug.LogWarning($"[MicrophoneVoiceCapture] 음성 비활성: {exception.Message}", this);
                _failure = exception.Message;
                return false;
            }
        }

        private void StopMicrophone()
        {
            if (_clip != null)
            {
                Microphone.End(_device);
                Destroy(_clip);
                _clip = null;
            }

            _queue.Clear();
            _preroll.Clear();
            _gate.Reset();
            _pendingCount = 0;
            _resampler?.Reset();
            _levelDb = SilenceDb;
            if (_monitor != null)
                _monitor.Stop();
        }

        /// <summary>저장된 장치가 지금 꽂혀 있으면 그 이름, 아니면 null(운영체제 기본 장치).</summary>
        private static string ResolveDevice(string requested)
        {
            if (string.IsNullOrEmpty(requested))
                return null;

            string[] devices = Microphone.devices;
            for (int i = 0; i < devices.Length; i++)
            {
                if (devices[i] == requested)
                    return requested;
            }

            return null;
        }

        private void ReadMicrophone()
        {
            int position = Microphone.GetPosition(_device);
            int clipSamples = _clip.samples;
            if (position < 0 || clipSamples <= 0)
                return;

            int available = (position - _readPosition + clipSamples) % clipSamples;
            int backlog = (int)(_clip.frequency * MaximumBacklogSeconds);
            if (available > backlog)
            {
                _readPosition = (position - _chunk.Length + clipSamples) % clipSamples;
                available = _chunk.Length;
            }

            float gain = Mathf.Pow(10f, (_userSettings != null ? _userSettings.MicGainDb : 0f) / 20f);
            while (available >= _chunk.Length)
            {
                // 끝을 넘는 읽기는 클립 앞으로 이어서 읽힌다(루프 클립).
                _clip.GetData(_chunk, _readPosition);
                _readPosition = (_readPosition + _chunk.Length) % clipSamples;
                available -= _chunk.Length;

                if (_resampler != null)
                    Append(_resampled, _resampler.Process(_chunk, _chunk.Length, _resampled), gain);
                else
                    Append(_chunk, _chunk.Length, gain);
            }
        }

        private void Append(float[] samples, int count, float gain)
        {
            int room = _pending.Length - _pendingCount;
            count = Math.Min(count, room);
            Array.Copy(samples, 0, _pending, _pendingCount, count);
            _pendingCount += count;

            int consumed = 0;
            while (_pendingCount - consumed >= _frame.Length)
            {
                Array.Copy(_pending, consumed, _frame, 0, _frame.Length);
                consumed += _frame.Length;
                ProcessFrame(gain);
            }

            if (consumed > 0)
            {
                Array.Copy(_pending, consumed, _pending, 0, _pendingCount - consumed);
                _pendingCount -= consumed;
            }
        }

        #endregion

        #region 처리

        /// <summary>
        /// 20ms 한 프레임: 게인 → 크기 측정 → 게이트 → 압축. 게이트가 닫혀 있어도 압축은 계속한다 —
        /// 인코더 상태가 이어져야 여는 순간 앞당겨 보낸 프레임이 자연스럽게 풀린다.
        /// </summary>
        private void ProcessFrame(float gain)
        {
            for (int i = 0; i < _frame.Length; i++)
                _frame[i] = Mathf.Clamp(_frame[i] * gain, -1f, 1f);

            float decibels = VoiceActivityGate.Decibels(_frame, 0, _frame.Length);
            _levelDb = decibels;
            _levelTime = Time.unscaledTime;

            bool gateActive = IsGateActive;
            bool justOpened = gateActive && _gate.Step(decibels, FrameSeconds, _userSettings.NoiseGateThresholdDb,
                _settings.GateHysteresisDb, _settings.GateHoldSeconds);
            bool pass = !gateActive || _gate.IsOpen;

            int length = OpusVoiceBlock.EncodeFrame(_encoder, _frame, _encodeScratch, _encoded);
            if (length > 0)
            {
                if (pass)
                {
                    if (justOpened)
                    {
                        while (_preroll.Count > 0)
                            _queue.Enqueue(_preroll.DequeueBuffer(out int prerollLength), prerollLength);
                    }

                    _queue.Enqueue(_encoded, length);
                }
                else
                {
                    _preroll.Enqueue(_encoded, length, _settings.GatePrerollFrames);
                }
            }

            if (_monitoring)
            {
                if (!pass)
                    Array.Clear(_frame, 0, _frame.Length);
                if (_monitor == null)
                    _monitor = MicrophoneMonitorPlayer.Create(transform);
                _monitor.Push(_frame, _frame.Length);
            }
        }

        #endregion

        /// <summary>압축 프레임 고정 링. 가득 차면 가장 오래된 프레임을 버린다. 할당 없음.</summary>
        private sealed class FrameRing
        {
            private readonly byte[][] _frames;
            private readonly int[] _lengths;
            private readonly byte[] _scratch = new byte[OpusVoiceBlock.MaximumFrameBytes];
            private int _start;

            public FrameRing(int capacity)
            {
                _frames = new byte[capacity][];
                _lengths = new int[capacity];
                for (int i = 0; i < capacity; i++)
                    _frames[i] = new byte[OpusVoiceBlock.MaximumFrameBytes];
            }

            public int Count { get; private set; }

            public void Enqueue(byte[] source, int length) => Enqueue(source, length, _frames.Length);

            /// <param name="limit">이 링에서 쓸 최대 칸 수. 앞당김 프레임 수가 설정으로 바뀌어도 링을 다시 만들지 않는다.</param>
            public void Enqueue(byte[] source, int length, int limit)
            {
                limit = Math.Min(limit, _frames.Length);
                if (limit <= 0)
                    return;

                while (Count >= limit)
                    Drop();

                int index = (_start + Count) % _frames.Length;
                Buffer.BlockCopy(source, 0, _frames[index], 0, length);
                _lengths[index] = length;
                Count++;
            }

            public int PeekLength() => _lengths[_start];

            /// <summary>가장 오래된 프레임을 <paramref name="destination"/> 의 <paramref name="offset"/> 에 복사하고 길이를 돌려준다.</summary>
            public int Dequeue(byte[] destination, int offset)
            {
                int length = _lengths[_start];
                Buffer.BlockCopy(_frames[_start], 0, destination, offset, length);
                Drop();
                return length;
            }

            /// <summary>가장 오래된 프레임을 내부 임시 버퍼로 꺼낸다. 다음 호출 전까지만 유효하다.</summary>
            public byte[] DequeueBuffer(out int length)
            {
                length = Dequeue(_scratch, 0);
                return _scratch;
            }

            public void Clear()
            {
                _start = 0;
                Count = 0;
            }

            private void Drop()
            {
                _start = (_start + 1) % _frames.Length;
                Count--;
            }
        }
    }
}
