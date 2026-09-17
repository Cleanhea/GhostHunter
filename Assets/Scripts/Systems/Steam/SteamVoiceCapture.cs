using System;
using System.IO;
using GhostHunter.Core.Voice;
using Steamworks;
using UnityEngine;

namespace GhostHunter.Systems.Steam
{
    /// <summary>Steam 클라이언트의 압축 음성 캡처와 24kHz mono PCM 디코딩 어댑터.</summary>
    public sealed class SteamVoiceCapture : MonoBehaviour, IVoiceCaptureService
    {
        private readonly byte[] _compressed = new byte[65536];
        private readonly byte[] _decoded = new byte[192000];
        private MemoryStream _captureStream;
        private MemoryStream _inputStream;
        private MemoryStream _outputStream;
        private bool _failed;
        public bool IsAvailable => !_failed && SteamClient.IsValid;
        public bool IsRecording { get; private set; }
        public string Status => _failed ? "음성 API 사용 불가" : !SteamClient.IsValid ? "Steam 미연결" : IsRecording ? "마이크 켜짐 (입력 장치는 Steam 설정)" : "마이크 꺼짐";
        public byte Codec => 0;
        public int SampleRate => 24000;
        private void Awake()
        {
            _captureStream = new MemoryStream(_compressed, 0, _compressed.Length, true, true);
            _inputStream = new MemoryStream(_compressed, 0, _compressed.Length, false, true);
            _outputStream = new MemoryStream(_decoded, 0, _decoded.Length, true, true);
        }
        private void OnDisable() => SetRecording(false);
        private void OnDestroy()
        {
            SetRecording(false);
            _captureStream?.Dispose();
            _inputStream?.Dispose();
            _outputStream?.Dispose();
        }
        public void SetRecording(bool recording)
        {
            if (recording == IsRecording) return;
            if (!IsAvailable) { IsRecording = false; return; }
            try
            {
                SteamUser.SampleRate = (uint)SampleRate;
                SteamUser.VoiceRecord = recording;
                IsRecording = recording;
                // 재개 전에 이전 캡처 꼬리를 비워 뮤트 중 음성이 뒤늦게 나가지 않게 한다.
                if (recording && SteamUser.HasVoiceData)
                {
                    _captureStream.Position = 0;
                    SteamUser.ReadVoiceData(_captureStream);
                }
            }
            catch (Exception exception) { Fail(exception); }
        }
        public int ReadFrame(byte[] destination)
        {
            if (!IsAvailable || !IsRecording) return 0;
            try
            {
                if (!SteamUser.HasVoiceData) return 0;
                _captureStream.Position = 0;
                int count = SteamUser.ReadVoiceData(_captureStream);
                if (count <= 0 || count > destination.Length) return 0;
                Buffer.BlockCopy(_compressed, 0, destination, 0, count);
                return count;
            }
            catch (Exception exception) { Fail(exception); return 0; }
        }
        public bool OpenSettings()
        {
            // 게임 안에 입력 장치 선택이 없다(기획 §5.1 제약 1). 오버레이의 Steam 설정으로 보낸다.
            if (!SteamClient.IsValid || !SteamUtils.IsOverlayEnabled) return false;
            try { SteamFriends.OpenOverlay("settings"); return true; }
            catch (Exception exception)
            {
                Debug.LogWarning($"[SteamVoiceCapture] Steam 설정 오버레이를 열지 못했다: {exception.Message}", this);
                return false;
            }
        }
        public int Decode(byte[] compressed, int count, float[] samples)
        {
            if (!IsAvailable || count <= 0 || count > _compressed.Length) return 0;
            try
            {
                SteamUser.SampleRate = (uint)SampleRate;
                Buffer.BlockCopy(compressed, 0, _compressed, 0, count);
                _inputStream.Position = 0;
                _outputStream.Position = 0;
                int bytes = SteamUser.DecompressVoice(_inputStream, count, _outputStream);
                int length = Math.Min(bytes / 2, samples.Length);
                for (int i = 0; i < length; i++)
                    samples[i] = (short)(_decoded[i * 2] | _decoded[i * 2 + 1] << 8) / 32768f;
                return length;
            }
            catch (DllNotFoundException exception) { Fail(exception); return 0; }
            catch (EntryPointNotFoundException exception) { Fail(exception); return 0; }
            // 손상된 원격 압축 블록 때문에 정상 로컬 마이크까지 꺼지지 않게 한다.
            catch (Exception) { return 0; }
        }
        private void Fail(Exception exception)
        {
            if (!_failed) Debug.LogWarning($"[SteamVoiceCapture] 음성 비활성: {exception.Message}", this);
            if (SteamClient.IsValid)
            {
                try { SteamUser.VoiceRecord = false; } catch (Exception) { }
            }
            _failed = true;
            IsRecording = false;
        }
    }
}
