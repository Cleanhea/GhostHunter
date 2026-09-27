#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GhostHunter.Core;
using GhostHunter.Core.Networking;
using GhostHunter.Core.Scenes;
using GhostHunter.Gameplay.Sanity;
using GhostHunter.Gameplay.Furniture;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GhostHunter.DebugTools
{
    /// <summary>
    /// 개발 빌드 전용 다중 프로세스 세션 검증 도구. <c>-gh-auto=host|client</c> 인자가 있을 때만 생성되어
    /// Local(UTP) 세션을 자동으로 열거나 접속하고, <c>-gh-leave-after=초</c> 가 있으면 일시정지 메뉴
    /// "타이틀로"와 같은 순서(세션 종료 → Title 로드)로 떠난다. <c>-gh-return-on-end</c> 가 있으면
    /// 요청하지 않은 세션 종료 뒤 끊김 모달의 확인과 같은 경로로 Title 로 돌아간다.
    /// 클라이언트의 <c>-gh-bot-follow=번호</c>는 Host 추종 이동을 켜고,
    /// <c>-gh-quit-on-end</c>와 <c>-gh-quit-after=초</c>는 봇 프로세스를 정리한다.
    /// 2초마다 세션·플레이어·씬 상태와 오류 수를 <c>[GhAuto]</c> 로그로 남긴다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocalSessionAutomation : MonoBehaviour
    {
        private const string LogTag = "[GhAuto]";
        private const float ReportInterval = 2f;
        private const float RetryDelay = 3f;
        private const float ReturnDelay = 2f;
        private const int MaxStoredErrors = 8;
        private const int MaxTrackedPlayers = 8;
        private const float BotFollowDistance = 2.5f;
        private const float BotSideDistance = 1.2f;
        private const float BotMoveSpeed = 2f;
        private const float BotTurnSpeed = 360f;

        private enum Phase
        {
            WaitingForTitle,
            Starting,
            InSession,
            Leaving,
            Left,
        }

        private readonly SanityNetworkState[] _states = new SanityNetworkState[MaxTrackedPlayers];
        private readonly List<string> _errors = new(MaxStoredErrors);
        private readonly StringBuilder _report = new(256);

        private string _role;
        private float _leaveAfter = -1f;
        private bool _returnOnEnd;
        private bool _quitOnEnd;
        private int _botFollowSlot;
        private float _quitAfter = -1f;
        private float _createdAt;
        private float _quitAt = -1f;
        private bool _quitRequested;
        private Transform _botTransform;
        private Transform _hostTransform;
        private CharacterController _botController;
        private SanityNetworkState _botSanity;

        private IConnectionService _connection;
        private ISceneFlow _sceneFlow;
        private Phase _phase = Phase.WaitingForTitle;
        private float _sessionStartedAt;
        private float _nextReportAt;
        private float _retryAt = -1f;
        private float _returnAt = -1f;
        private int _errorCount;
        private int _sessionEndedCount;
        private ulong _watchedFurnitureId;
        private bool _watchFurniture;
        private bool _probeFurnitureAuthority;
        private bool _authorityProbed;
        private FurnitureNetworkPhysics _watchedFurniture;
        private Renderer[] _watchedRenderers;
        private Collider[] _watchedColliders;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateFromCommandLine()
        {
            if (!TryGetArgument("-gh-auto=", out string role))
                return;

            var host = new GameObject(nameof(LocalSessionAutomation));
            DontDestroyOnLoad(host);
            LocalSessionAutomation automation = host.AddComponent<LocalSessionAutomation>();
            automation._role = role.ToLowerInvariant();
            automation._returnOnEnd = HasArgument("-gh-return-on-end");
            automation._quitOnEnd = HasArgument("-gh-quit-on-end");
            automation._createdAt = Time.realtimeSinceStartup;
            if (automation._role == "client"
                && TryGetArgument("-gh-bot-follow=", out string slot)
                && int.TryParse(slot, out int parsedSlot))
                automation._botFollowSlot = Mathf.Clamp(parsedSlot, 1, MaxTrackedPlayers);
            automation._watchFurniture = TryGetArgument("-gh-watch-furniture=", out string furnitureId)
                && ulong.TryParse(furnitureId, out automation._watchedFurnitureId);
            automation._probeFurnitureAuthority = HasArgument("-gh-probe-furniture-authority");

            if (TryGetArgument("-gh-leave-after=", out string seconds)
                && float.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                automation._leaveAfter = parsed;
            }

            if (TryGetArgument("-gh-quit-after=", out string quitSeconds)
                && float.TryParse(quitSeconds, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float parsedQuit))
                automation._quitAfter = Mathf.Max(1f, parsedQuit);

            // 여러 인스턴스를 한 PC 에서 돌리므로 프레임을 제한해 CPU 를 나눠 쓴다.
            Application.targetFrameRate = 30;
            Application.runInBackground = true;
            Debug.Log($"{LogTag} 시작 role={automation._role} leaveAfter={automation._leaveAfter} " +
                $"returnOnEnd={automation._returnOnEnd} botFollow={automation._botFollowSlot}");
        }

        private void OnEnable()
        {
            Application.logMessageReceived += HandleLogMessage;
        }

        private void Update()
        {
            float now = Time.realtimeSinceStartup;

            if (!_quitRequested
                && ((_quitAt >= 0f && now >= _quitAt)
                    || (_quitAfter >= 0f && now - _createdAt >= _quitAfter)))
            {
                _quitRequested = true;
                if (_connection != null && _connection.IsRunning)
                    _connection.Disconnect(leaveLobby: false);
                Debug.Log($"{LogTag} 봇 프로세스 종료");
                Application.Quit();
                return;
            }

            switch (_phase)
            {
                case Phase.WaitingForTitle:
                    TickWaitingForTitle();
                    break;

                case Phase.Starting:
                    TickStarting(now);
                    break;

                case Phase.InSession:
                    TickInSession(now);
                    break;

                case Phase.Leaving:
                    if (!_connection.IsRunning)
                    {
                        _sceneFlow.Load(SceneId.Title);
                        _phase = Phase.Left;
                        Debug.Log($"{LogTag} 세션 종료 확인 → Title 로드 요청");
                    }
                    break;
            }

            if (now >= _nextReportAt)
            {
                _nextReportAt = now + ReportInterval;
                Report(now);
            }
        }

        private void LateUpdate()
        {
            if (_botFollowSlot <= 0 || _phase != Phase.InSession
                || _sceneFlow == null || !_sceneFlow.Current.IsStage()
                || _botController == null || !_botController.enabled
                || _botSanity == null || !_botSanity.HasSanity)
                return;

            NetworkManager net = NetworkManager.Singleton;
            if (net == null || !net.IsClient || net.IsServer || net.SpawnManager == null)
                return;

            if (_hostTransform == null)
            {
                NetworkObject host = net.SpawnManager.GetPlayerNetworkObject(NetworkManager.ServerClientId);
                if (host == null)
                    return;
                _hostTransform = host.transform;
            }

            if (_hostTransform == _botTransform)
                return;

            float side = (_botFollowSlot % 2 == 0 ? 1f : -1f)
                * BotSideDistance * (1 + (_botFollowSlot - 1) / 2);
            Vector3 target = _hostTransform.position
                - _hostTransform.forward * BotFollowDistance + _hostTransform.right * side;
            Vector3 planar = target - _botTransform.position;
            planar.y = 0f;
            if (planar.sqrMagnitude > 0.36f)
                _botController.Move(Vector3.ClampMagnitude(planar, BotMoveSpeed * Time.deltaTime));

            Vector3 faceHost = _hostTransform.position - _botTransform.position;
            faceHost.y = 0f;
            if (faceHost.sqrMagnitude > 0.01f)
            {
                _botTransform.rotation = Quaternion.RotateTowards(_botTransform.rotation,
                    Quaternion.LookRotation(faceHost), BotTurnSpeed * Time.deltaTime);
            }
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= HandleLogMessage;

            if (_connection != null)
                _connection.SessionEnded -= HandleSessionEnded;
        }

        private void TickWaitingForTitle()
        {
            if (!Services.TryGet(out _connection) || !Services.TryGet(out _sceneFlow))
                return;

            if (_sceneFlow.IsLoading || _sceneFlow.Current != SceneId.Title)
                return;

            _connection.SessionEnded += HandleSessionEnded;
            StartSession();
            _phase = Phase.Starting;
        }

        private void TickStarting(float now)
        {
            NetworkManager net = NetworkManager.Singleton;
            bool joined = _connection.IsRunning
                && _sceneFlow.Current.IsStage()
                && net != null
                && net.LocalClient != null
                && net.LocalClient.PlayerObject != null;

            if (joined)
            {
                if (_botFollowSlot > 0)
                {
                    NetworkObject player = net.LocalClient.PlayerObject;
                    _botTransform = player.transform;
                    player.TryGetComponent(out _botController);
                    player.TryGetComponent(out _botSanity);
                }
                _phase = Phase.InSession;
                _sessionStartedAt = now;
                _retryAt = -1f;
                Debug.Log($"{LogTag} 세션 참여 완료 role={_role} clientId={net.LocalClientId}");
                return;
            }

            if (_retryAt > 0f && now >= _retryAt && !_connection.IsRunning)
            {
                _retryAt = -1f;
                Debug.Log($"{LogTag} 접속 재시도");
                StartSession();
            }
        }

        private void TickInSession(float now)
        {
            if (_quitOnEnd && _botFollowSlot > 0 && _quitAt < 0f && !_connection.IsRunning)
            {
                _quitAt = now + 1f;
                return;
            }

            if (_returnAt > 0f)
            {
                if (now < _returnAt)
                    return;

                // 끊김 모달의 "확인"과 같은 경로 — 세션이 이미 없으므로 Title 만 로드한다.
                _returnAt = -1f;
                _phase = Phase.Left;
                if (_connection.IsRunning)
                    _connection.Disconnect(leaveLobby: false);
                _sceneFlow.Load(SceneId.Title);
                Debug.Log($"{LogTag} 요청하지 않은 종료 뒤 Title 로드 요청");
                return;
            }

            if (_leaveAfter < 0f || now - _sessionStartedAt < _leaveAfter)
                return;

            // 일시정지 메뉴 "타이틀로"와 같은 순서 — 게스트는 로비에 남고 호스트는 로비까지 나간다.
            Debug.Log($"{LogTag} 자발적 이탈 시작 host={_connection.IsHost}");
            _connection.Disconnect(leaveLobby: _connection.IsHost);
            _phase = Phase.Leaving;
        }

        private void StartSession()
        {
            _connection.SetTransportMode(TransportMode.Local);

            if (_role == "host")
                _connection.StartHostInGameScene(SceneId.ProtoTypeGame);
            else
                _connection.StartLocalClient();

            Debug.Log($"{LogTag} 세션 시작 요청 role={_role}");
        }

        private void HandleSessionEnded()
        {
            _sessionEndedCount++;
            Debug.Log($"{LogTag} SessionEnded 수신 phase={_phase}");

            float now = Time.realtimeSinceStartup;
            if (_quitOnEnd)
            {
                _quitAt = now + 1f;
                return;
            }
            if (_phase == Phase.Starting)
                _retryAt = now + RetryDelay;
            else if (_phase == Phase.InSession && _returnOnEnd)
                _returnAt = now + ReturnDelay;
        }

        private void HandleLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type is not (LogType.Error or LogType.Exception or LogType.Assert))
                return;

            _errorCount++;
            if (_errors.Count < MaxStoredErrors)
                _errors.Add($"{type}: {condition}");
        }

        private void Report(float now)
        {
            NetworkManager net = NetworkManager.Singleton;
            string role = net == null ? "none"
                : net.IsHost ? "host"
                : net.IsServer ? "server"
                : net.IsClient ? "client"
                : "stopped";

            int connected = net != null && net.IsServer ? net.ConnectedClientsIds.Count : -1;
            int spawnedPlayers = CountSpawnedPlayers(net);

            int team = -1;
            int living = -1;
            if (Services.TryGet(out ISanityTeamService teamService))
            {
                team = teamService.CopyPlayerStates(_states);
                living = 0;
                for (int i = 0; i < team; i++)
                {
                    if (_states[i] != null && _states[i].HasSanity)
                        living++;
                }
            }

            _report.Clear();
            _report.Append(LogTag)
                .Append(" t=").Append(now.ToString("0.0", CultureInfo.InvariantCulture))
                .Append(" phase=").Append(_phase)
                .Append(" net=").Append(role)
                .Append(" connected=").Append(connected)
                .Append(" players=").Append(spawnedPlayers)
                .Append(" team=").Append(team).Append('/').Append(living)
                .Append(" flow=").Append(_sceneFlow != null ? _sceneFlow.Current.ToString() : "-")
                .Append(" scenes=[");

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (i > 0)
                    _report.Append(',');
                _report.Append(SceneManager.GetSceneAt(i).name);
            }

            _report.Append("] errors=").Append(_errorCount)
                .Append(" sessionEnded=").Append(_sessionEndedCount);

            Debug.Log(_report.ToString());
            ReportFurniture(net);

            if (_errors.Count > 0)
            {
                foreach (string error in _errors)
                    Debug.Log($"{LogTag} 기록된 오류 → {error}");
                _errors.Clear();
            }
        }

        private void ReportFurniture(NetworkManager net)
        {
            if (!_watchFurniture || net == null || !net.IsListening || net.SpawnManager == null)
                return;
            if (_watchedFurniture == null)
            {
                if (!net.SpawnManager.SpawnedObjects.TryGetValue(_watchedFurnitureId, out NetworkObject target)
                    || !target.TryGetComponent(out _watchedFurniture))
                    return;
                _watchedRenderers = target.GetComponentsInChildren<Renderer>(true);
                _watchedColliders = target.GetComponentsInChildren<Collider>(true);
            }

            // 명시적 검증 인자가 있는 원격 클라이언트에서만 서버 API 거절을 검사한다.
            if (_probeFurnitureAuthority && !_authorityProbed && !net.IsServer)
            {
                _authorityProbed = true;
                int before = _watchedFurniture.Durability;
                bool accepted = _watchedFurniture.ServerSetDurability(1);
                _watchedFurniture.ServerApplyCollisionSpeed(100f);
                _watchedFurniture.ServerResetDurability(false);
                Debug.Log($"[GhFurniture] authority accepted={accepted} before={before} after={_watchedFurniture.Durability}", this);
            }

            int visible = 0;
            int colliders = 0;
            foreach (Renderer renderer in _watchedRenderers)
                if (renderer != null && renderer.enabled && !renderer.forceRenderingOff && renderer.gameObject.activeInHierarchy)
                    visible++;
            foreach (Collider collider in _watchedColliders)
                if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy)
                    colliders++;
            Rigidbody body = _watchedFurniture.Rigidbody;
            Debug.Log($"[GhFurniture] id={_watchedFurnitureId} server={_watchedFurniture.IsServer} "
                + $"durability={_watchedFurniture.Durability} broken={_watchedFurniture.IsBroken} "
                + $"available={_watchedFurniture.IsAvailable} visible={visible} colliders={colliders} "
                + $"kinematic={body.isKinematic} collisions={body.detectCollisions} position={body.position}", this);
        }

        private static int CountSpawnedPlayers(NetworkManager net)
        {
            if (net == null || !net.IsListening || net.SpawnManager == null)
                return -1;

            int count = 0;
            foreach (NetworkObject networkObject in net.SpawnManager.SpawnedObjects.Values)
            {
                if (networkObject != null && networkObject.IsPlayerObject)
                    count++;
            }

            return count;
        }

        private static bool TryGetArgument(string prefix, out string value)
        {
            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (!argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                value = argument.Substring(prefix.Length);
                return true;
            }

            value = null;
            return false;
        }

        private static bool HasArgument(string name)
        {
            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (string.Equals(argument, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
#endif
