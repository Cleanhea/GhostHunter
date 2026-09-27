using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using GhostHunter.Core.Scenes;
using GhostHunter.Core.Steam;
using GhostHunter.Gameplay.Recovery;
using GhostHunter.Gameplay.Sanity;
using ConnectionManager = GhostHunter.Networking.ConnectionManager;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Systems.Steam
{
    /// <summary>
    /// Steam P2P 복제본을 각 참가자가 보유한다. 원래 NGO 서버가 사라지면 로비가 선출한
    /// 새 방장이 확인된 마지막 복제본으로 같은 Game 씬에 서버를 다시 열고 기존 멤버만 붙인다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageRecoveryCoordinator : MonoBehaviour
    {
        private const int Channel = 9;
        private const int HeaderSize = 17;
        private const int ChunkSize = 8192;
        private const float SnapshotInterval = 0.2f;
        private const float RecoveryTimeout = 45f;
        private const float ReconnectTimeout = 18f;
        private const byte SnapshotChunk = 1;
        private const byte RecoveryReplicaChunk = 3;
        private const byte ReplicaUnavailable = 4;
        private const byte MigratedSnapshotChunk = 5;

        private SteamLobbyManager _lobby;
        private ConnectionManager _connection;
        private ISceneFlow _sceneFlow;
        private readonly HashSet<ulong> _restoredClients = new();
        private readonly HashSet<ulong> _replicaReports = new();
        private StageRecoverySnapshot _latestSnapshot;
        private StageRecoverySnapshot _recoveryPlayerSnapshot;
        private string _stageId = string.Empty;
        private uint _sequence;
        private float _nextSnapshotAt;
        private float _recoveryStartedAt;
        private float _hostReadyAt;
        private float _electedAt;
        private float _nextReplicaAt;
        private bool _recovering;
        private bool _hostStartRequested;
        private bool _worldRestored;
        private bool _receivedMigratedSnapshot;
        private bool _serializerVerified;
        private bool _clientConnectRequested;
        private float _nextConnectAttemptAt;
        private byte[] _incoming = new byte[HeaderSize + ChunkSize];
        private readonly Dictionary<ulong, ChunkAccumulator> _chunks = new();

        private sealed class ChunkAccumulator
        {
            internal uint Sequence;
            internal ulong Sender;
            internal int TotalLength;
            internal uint Checksum;
            internal byte[][] Parts;
        }

        public void Initialize(SteamLobbyManager lobby, ConnectionManager connection,
            ISceneFlow sceneFlow)
        {
            _lobby = lobby;
            _connection = connection;
            _sceneFlow = sceneFlow;
            if (_connection != null)
                _connection.StageRecoveryRequired += BeginRecovery;
            if (_lobby != null)
                _lobby.LobbyLeft += HandleLobbyLeft;
        }

        private void OnDestroy()
        {
            if (_connection != null)
                _connection.StageRecoveryRequired -= BeginRecovery;
            if (_lobby != null)
                _lobby.LobbyLeft -= HandleLobbyLeft;
            StageRecoveryGate.Restoring = false;
        }

        private void Update()
        {
            if (_lobby == null || !_lobby.IsSteamReady || _sceneFlow == null)
                return;
            ReadPackets();
            if (!_sceneFlow.Current.IsStage() || !_lobby.IsInLobby
                || !_lobby.IsGameStarted)
            {
                if (!_recovering)
                    ResetForStageExit();
                return;
            }

            string stageId = _lobby.CurrentStageId;
            if (stageId != _stageId)
            {
                _stageId = stageId;
                _sequence = 0;
                _latestSnapshot = null;
                _chunks.Clear();
                _serializerVerified = false;
            }

            if (_recovering)
                TickRecovery();
            else if (_connection != null && _connection.IsHost
                && Time.unscaledTime >= _nextSnapshotAt)
            {
                RestorePendingPlayers(NetworkManager.Singleton);
                _nextSnapshotAt = Time.unscaledTime + SnapshotInterval;
                PublishSnapshot();
            }
        }

        private void PublishSnapshot()
        {
            StageRecoverySnapshot snapshot = StageRecoverySnapshotUtility.Capture(_stageId,
                _sequence + 1);
            if (snapshot == null)
                return;
            MergePendingPlayers(snapshot);
            _sequence = snapshot.Sequence;
            _latestSnapshot = snapshot;
            string json = JsonUtility.ToJson(snapshot);
            if (!_serializerVerified)
            {
                StageRecoverySnapshot check = JsonUtility.FromJson<StageRecoverySnapshot>(json);
                if (check?.Players == null || check.Players.Length != snapshot.Players.Length
                    || (check.Players.Length > 0
                        && check.Players[0].SteamId != snapshot.Players[0].SteamId)
                    || check.Sequence != snapshot.Sequence
                    || check.Furniture == null
                    || check.Furniture.Length != snapshot.Furniture.Length)
                {
                    Debug.LogError("[StageRecovery] Unity JSON 왕복 검증 실패. 복제본 전송을 중단합니다.", this);
                    return;
                }
                _serializerVerified = true;
            }
            byte[] compressed = Compress(Encoding.UTF8.GetBytes(json));
            IReadOnlyList<LobbyMemberInfo> members = _lobby.GetMembers();
            for (int m = 0; m < members.Count; m++)
            {
                ulong peer = members[m].SteamId;
                if (peer == _lobby.LocalSteamId)
                    continue;
                SendCompressedSnapshot(peer, snapshot.Sequence, compressed, SnapshotChunk);
            }
        }

        private void SendCompressedSnapshot(ulong peer, uint sequence, byte[] compressed,
            byte kind)
        {
            uint checksum = Checksum(compressed);
            int count = (compressed.Length + ChunkSize - 1) / ChunkSize;
            if (count == 0 || count > 128)
            {
                Debug.LogError("[StageRecovery] 스냅샷이 너무 큽니다.", this);
                return;
            }
            for (int i = 0; i < count; i++)
            {
                int offset = i * ChunkSize;
                int length = Math.Min(ChunkSize, compressed.Length - offset);
                byte[] packet = new byte[HeaderSize + length];
                packet[0] = kind;
                WriteUInt(packet, 1, sequence);
                WriteUShort(packet, 5, (ushort)i);
                WriteUShort(packet, 7, (ushort)count);
                WriteUInt(packet, 9, (uint)compressed.Length);
                WriteUInt(packet, 13, checksum);
                Buffer.BlockCopy(compressed, offset, packet, HeaderSize, length);
                SteamNetworking.SendP2PPacket(peer, packet, packet.Length, Channel,
                    P2PSend.Reliable);
            }
        }

        private void ReadPackets()
        {
            for (int packet = 0; packet < 128
                && SteamNetworking.IsP2PPacketAvailable(out uint available, Channel); packet++)
            {
                if (available > 1024 * 1024)
                {
                    SteamNetworking.ReadP2PPacket(Channel);
                    continue;
                }
                if (_incoming.Length < available)
                    _incoming = new byte[available];
                uint length = available;
                SteamId sender = default;
                if (!SteamNetworking.ReadP2PPacket(_incoming, ref length, ref sender, Channel))
                    continue;
                if (length == 0)
                    continue;
                if (_incoming[0] is SnapshotChunk or RecoveryReplicaChunk
                    or MigratedSnapshotChunk)
                    ReadSnapshotChunk(sender.Value, (int)length, _incoming[0]);
                else if (_incoming[0] == ReplicaUnavailable && _recovering
                    && _lobby.IsLobbyOwner && IsMember(sender.Value))
                    _replicaReports.Add(sender.Value);
            }
        }

        private void ReadSnapshotChunk(ulong sender, int length, byte kind)
        {
            if (length < HeaderSize || !_lobby.IsInLobby || sender == _lobby.LocalSteamId)
                return;
            bool ordinary = kind == SnapshotChunk && !_recovering
                && sender == _lobby.CurrentHostSteamId;
            bool resumedOrdinary = kind == SnapshotChunk && _recovering
                && _lobby.IsMigratedStageResumed && !_lobby.IsLobbyOwner
                && sender == _lobby.CurrentHostSteamId;
            bool replica = kind == RecoveryReplicaChunk && _recovering
                && _lobby.IsLobbyOwner && !_worldRestored && IsMember(sender);
            bool migrated = kind == MigratedSnapshotChunk && _recovering
                && sender == _lobby.CurrentHostSteamId && !_lobby.IsLobbyOwner;
            if (!ordinary && !resumedOrdinary && !replica && !migrated)
                return;
            uint sequence = ReadUInt(_incoming, 1);
            int index = ReadUShort(_incoming, 5);
            int count = ReadUShort(_incoming, 7);
            int total = (int)ReadUInt(_incoming, 9);
            uint checksum = ReadUInt(_incoming, 13);
            if (replica && sequence <= _sequence)
            {
                _replicaReports.Add(sender);
                return;
            }
            if ((!migrated && !resumedOrdinary && sequence <= _sequence)
                || count < 1 || count > 128
                || index >= count
                || total < 1 || total > 1024 * 1024
                || length - HeaderSize > ChunkSize)
                return;
            if (!_chunks.TryGetValue(sender, out ChunkAccumulator chunks)
                || chunks.Sequence != sequence)
            {
                chunks = new ChunkAccumulator
                {
                    Sequence = sequence, Sender = sender, TotalLength = total,
                    Checksum = checksum, Parts = new byte[count][],
                };
                _chunks[sender] = chunks;
            }
            if (chunks.Parts.Length != count || chunks.TotalLength != total
                || chunks.Checksum != checksum)
                return;
            byte[] part = new byte[length - HeaderSize];
            Buffer.BlockCopy(_incoming, HeaderSize, part, 0, part.Length);
            chunks.Parts[index] = part;
            int received = 0;
            for (int i = 0; i < count; i++)
            {
                if (chunks.Parts[i] == null)
                    return;
                received += chunks.Parts[i].Length;
            }
            if (received != total)
            {
                _chunks.Remove(sender);
                return;
            }
            byte[] compressed = new byte[total];
            int offset = 0;
            for (int i = 0; i < count; i++)
            {
                part = chunks.Parts[i];
                Buffer.BlockCopy(part, 0, compressed, offset, part.Length);
                offset += part.Length;
            }
            _chunks.Remove(sender);
            if (Checksum(compressed) != checksum)
                return;
            try
            {
                StageRecoverySnapshot snapshot = JsonUtility.FromJson<StageRecoverySnapshot>(
                    Encoding.UTF8.GetString(Decompress(compressed)));
                if (snapshot == null || snapshot.StageId != _lobby.CurrentStageId
                    || snapshot.Sequence != sequence || snapshot.Players == null
                    || snapshot.Furniture == null || snapshot.Stains == null
                    || snapshot.PoolFurniture == null || snapshot.Doors == null)
                    return;
                _latestSnapshot = snapshot;
                _sequence = sequence;
                if (replica)
                    _replicaReports.Add(sender);
                if (migrated || resumedOrdinary)
                    _receivedMigratedSnapshot = true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[StageRecovery] 스냅샷 해석 실패: {exception.Message}", this);
            }
        }

        private bool IsMember(ulong steamId)
        {
            foreach (LobbyMemberInfo member in _lobby.GetMembers())
                if (member.SteamId == steamId)
                    return true;
            return false;
        }

        private void BeginRecovery()
        {
            if (_recovering || _sceneFlow == null || !_sceneFlow.Current.IsStage())
                return;
            _recovering = true;
            _hostStartRequested = false;
            _worldRestored = false;
            _clientConnectRequested = false;
            _nextConnectAttemptAt = 0f;
            _restoredClients.Clear();
            _replicaReports.Clear();
            _receivedMigratedSnapshot = false;
            _recoveryStartedAt = Time.unscaledTime;
            _electedAt = 0f;
            _nextReplicaAt = 0f;
            StageRecoveryGate.Restoring = true;
            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.IsListening && !network.ShutdownInProgress)
                network.Shutdown();
            Debug.Log("[StageRecovery] 호스트 이탈. 마지막 수신 스냅샷을 확인합니다.", this);
        }

        private void TickRecovery()
        {
            if (Time.unscaledTime - _recoveryStartedAt > RecoveryTimeout)
            {
                FailRecovery("새 호스트 연결 시간이 초과됐습니다.");
                return;
            }
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || network.ShutdownInProgress)
                return;
            if (!_lobby.IsLobbyOwner && _lobby.IsMigratedStageResumed
                && network.IsConnectedClient && _receivedMigratedSnapshot)
            {
                FinishRecovery();
                return;
            }
            if (_lobby.CurrentHostSteamId == _lobby.LocalSteamId && _lobby.IsLobbyOwner)
                TickNewHost(network);
            else
            {
                ShareReplicaWithNewHost();
                TickReconnectingClient(network);
            }
        }

        private void ShareReplicaWithNewHost()
        {
            ulong selectedHost = _lobby.CurrentHostSteamId;
            if (selectedHost == 0 || !IsMember(selectedHost)
                || Time.unscaledTime < _nextReplicaAt)
                return;
            _nextReplicaAt = Time.unscaledTime + 2f;
            if (_latestSnapshot == null || _latestSnapshot.StageId != _lobby.CurrentStageId)
            {
                SteamNetworking.SendP2PPacket(selectedHost, new[] { ReplicaUnavailable },
                    1, Channel, P2PSend.Reliable);
                return;
            }
            byte[] compressed = Compress(Encoding.UTF8.GetBytes(
                JsonUtility.ToJson(_latestSnapshot)));
            SendCompressedSnapshot(selectedHost, _latestSnapshot.Sequence, compressed,
                RecoveryReplicaChunk);
        }

        private void TickNewHost(NetworkManager network)
        {
            if (_electedAt <= 0f)
                _electedAt = Time.unscaledTime;
            // 모든 현재 멤버의 복제본/부재 응답을 기다린다. 응답 불능 멤버 때문에
            // 영구 대기하지 않도록 5초 상한을 둔다.
            if (_replicaReports.Count < _lobby.GetMembers().Count - 1
                && Time.unscaledTime - _electedAt < 5f)
                return;
            if (_latestSnapshot == null || _latestSnapshot.StageId != _lobby.CurrentStageId)
                return;
            if (!_hostStartRequested)
            {
                if (network.IsListening)
                    return;
                _hostStartRequested = true;
                _connection.StartHost();
                return;
            }
            if (!_connection.IsHost)
                return;
            if (!_worldRestored)
            {
                _worldRestored = StageRecoverySnapshotUtility.RestoreWorld(_latestSnapshot);
                if (!_worldRestored)
                    return;
                _recoveryPlayerSnapshot = _latestSnapshot;
                _hostReadyAt = Time.unscaledTime;
                _lobby.MarkMigratedHostReady();
            }
            RestorePendingPlayers(network);
            if ((_restoredClients.Count >= _lobby.GetMembers().Count
                && network.ConnectedClients.Count >= _lobby.GetMembers().Count)
                || Time.unscaledTime - _hostReadyAt >= ReconnectTimeout)
            {
                if (!StageRecoverySnapshotUtility.RestoreWorld(_latestSnapshot))
                    return;
                _restoredClients.Clear();
                RestorePendingPlayers(network);
                StageRecoveryGate.Restoring = false;
                UnityEngine.Object.FindFirstObjectByType<SanityTeamService>()?.ServerEvaluateTeamWipe();
                PublishMigratedSnapshot();
                _lobby.MarkMigratedStageResumed();
                FinishRecovery();
            }
        }

        private void RestorePendingPlayers(NetworkManager network)
        {
            if (network == null || !network.IsServer || _recoveryPlayerSnapshot == null)
                return;
            bool restoredAny = false;
            foreach (NetworkClient client in network.ConnectedClientsList)
            {
                if (_restoredClients.Contains(client.ClientId) || client.PlayerObject == null)
                    continue;
                if (StageRecoverySnapshotUtility.RestorePlayer(client.PlayerObject,
                    _recoveryPlayerSnapshot))
                {
                    _restoredClients.Add(client.ClientId);
                    restoredAny = true;
                }
            }
            if (restoredAny)
                StageRecoverySnapshotUtility.RebindGhostPlayers();
        }

        private void MergePendingPlayers(StageRecoverySnapshot current)
        {
            if (_recoveryPlayerSnapshot?.Players == null || current.Players == null)
                return;
            var merged = new List<StageRecoverySnapshot.PlayerState>(current.Players);
            foreach (StageRecoverySnapshot.PlayerState previous in _recoveryPlayerSnapshot.Players)
            {
                if (!IsMember(previous.SteamId))
                    continue;
                bool found = false;
                foreach (StageRecoverySnapshot.PlayerState player in current.Players)
                    if (player.SteamId == previous.SteamId)
                    {
                        found = true;
                        break;
                    }
                if (!found)
                    merged.Add(previous);
            }
            current.Players = merged.ToArray();
        }

        private void PublishMigratedSnapshot()
        {
            if (_latestSnapshot == null)
                return;
            _latestSnapshot.Sequence = ++_sequence;
            byte[] compressed = Compress(Encoding.UTF8.GetBytes(
                JsonUtility.ToJson(_latestSnapshot)));
            foreach (LobbyMemberInfo member in _lobby.GetMembers())
                if (member.SteamId != _lobby.LocalSteamId)
                    SendCompressedSnapshot(member.SteamId, _sequence, compressed,
                        MigratedSnapshotChunk);
        }

        private void TickReconnectingClient(NetworkManager network)
        {
            if (!_lobby.IsMigratedHostReady || _lobby.CurrentHostSteamId == 0
                || network.IsConnectedClient)
                return;
            if (_clientConnectRequested && Time.unscaledTime >= _nextConnectAttemptAt
                && network.IsListening && !network.ShutdownInProgress)
            {
                network.Shutdown();
                _clientConnectRequested = false;
                _nextConnectAttemptAt = Time.unscaledTime + 1f;
                return;
            }
            if (Time.unscaledTime < _nextConnectAttemptAt)
                return;
            if (network.IsListening)
                return;
            _clientConnectRequested = true;
            _nextConnectAttemptAt = Time.unscaledTime + 8f;
            _connection.ConnectToSteamHost(_lobby.CurrentHostSteamId);
        }

        private void FinishRecovery()
        {
            _recovering = false;
            StageRecoveryGate.Restoring = false;
            _connection.CompleteStageRecovery();
            Debug.Log("[StageRecovery] 기존 스테이지 복원을 마쳤습니다.", this);
        }

        private void FailRecovery(string reason)
        {
            Debug.LogError($"[StageRecovery] {reason}", this);
            _recovering = false;
            StageRecoveryGate.Restoring = false;
            _connection.FailStageRecovery();
        }

        private void HandleLobbyLeft()
        {
            if (_recovering)
                FailRecovery("Steam 로비를 떠나 복원할 수 없습니다.");
            ResetForStageExit();
        }

        private void ResetForStageExit()
        {
            _latestSnapshot = null;
            _recoveryPlayerSnapshot = null;
            _stageId = string.Empty;
            _sequence = 0;
            _chunks.Clear();
            _serializerVerified = false;
        }

        private void OnGUI()
        {
            if (_recovering)
                GUI.Box(new Rect((Screen.width - 360f) * 0.5f,
                    (Screen.height - 70f) * 0.5f, 360f, 70f),
                    "호스트 연결이 끊겼습니다. 스테이지 상태 복원 중...");
        }

        private static byte[] Compress(byte[] source)
        {
            using var stream = new MemoryStream();
            using (var gzip = new GZipStream(stream,
                System.IO.Compression.CompressionLevel.Fastest, true))
                gzip.Write(source, 0, source.Length);
            return stream.ToArray();
        }

        private static byte[] Decompress(byte[] source)
        {
            using var input = new MemoryStream(source);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            byte[] buffer = new byte[8192];
            int read;
            while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > 4 * 1024 * 1024)
                    throw new InvalidDataException("복구 데이터 제한 초과");
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }

        private static uint Checksum(byte[] data)
        {
            uint hash = 2166136261;
            for (int i = 0; i < data.Length; i++)
                hash = (hash ^ data[i]) * 16777619;
            return hash;
        }

        private static void WriteUInt(byte[] data, int offset, uint value)
        {
            for (int i = 0; i < 4; i++) data[offset + i] = (byte)(value >> (i * 8));
        }

        private static uint ReadUInt(byte[] data, int offset)
        {
            uint value = 0;
            for (int i = 0; i < 4; i++) value |= (uint)data[offset + i] << (i * 8);
            return value;
        }

        private static void WriteUShort(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
        }

        private static ushort ReadUShort(byte[] data, int offset)
            => (ushort)(data[offset] | data[offset + 1] << 8);
    }
}
