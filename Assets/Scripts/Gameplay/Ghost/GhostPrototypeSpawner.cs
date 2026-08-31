using System.Text;
using GhostHunter.Core;
using GhostHunter.Gameplay.Sanity;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// Game 씬의 Host 전용 서비스. F1 HUD에서 귀신 프로토타입 프리팹을 동적으로 스폰·제거하고,
    /// 특수 어택·강제 진정·활동 강제·청소 진행도 스텁으로 <see cref="GhostStateMachine"/> 전이를
    /// 시험한다. 팀 정신력 표시는 <see cref="ISanityTeamService"/> 값을 그대로 읽는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GhostPrototypeSpawner : MonoBehaviour, IGhostDebug
    {
        [SerializeField] private GameObject _ghostPrefab;
        [SerializeField] private Transform _spawnPoint;

        [Header("집 내부 활동 경계 (§3.1 · §9.1)")]
        [Tooltip("귀신의 이동·탐지·잡힘을 제한하는 집 내부 X/Z 상자. 집 밖 플레이어는 대상에서 제외한다.")]
        [SerializeField] private Vector3 _roamCenter = new(0f, 0.1f, -1f);
        [SerializeField] private Vector3 _roamSize = new(16f, 3f, 9f);

        private readonly StringBuilder _summaryBuilder = new(256);

        private ISanityTeamService _sanity;
        private GhostPrototypeController _active;
        private NetworkObject _activeObject;

        private int _cleaningProgress;
        private bool _forceActive;
        private int _pushedCleaningProgress = -1;
        private bool _pushedForceActive;
        private string _lastStatus = "귀신 프로토타입 대기";

        public bool CanControl
        {
            get
            {
                NetworkManager network = NetworkManager.Singleton;
                return network != null && network.IsServer && network.IsListening;
            }
        }

        public bool HasGhost => _active != null && _active.IsSpawned;

        public bool IsGhostForcedVisible => HasGhost && _active.DebugForceVisible;

        public string StatusSummary
        {
            get
            {
                BuildSummary();
                return _summaryBuilder.ToString();
            }
        }

        public string LastStatus => _lastStatus;

        private void Awake()
        {
            Services.TryGet(out _sanity);

            if (_ghostPrefab == null)
                Debug.LogError($"{nameof(GhostPrototypeSpawner)}: 귀신 프리팹이 배선되지 않았습니다.", this);
        }

        private void Update()
        {
            PruneDeadGhost();

            if (_active == null || !CanControl)
                return;

            if (_pushedCleaningProgress != _cleaningProgress)
            {
                _active.ServerSetCleaningProgress(_cleaningProgress);
                _pushedCleaningProgress = _cleaningProgress;
            }

            if (_pushedForceActive != _forceActive)
            {
                _active.ServerSetForceActive(_forceActive);
                _pushedForceActive = _forceActive;
            }
        }

        public void SpawnGhost()
        {
            Vector3 position = _spawnPoint != null ? _spawnPoint.position : transform.position;
            Quaternion rotation = _spawnPoint != null ? _spawnPoint.rotation : Quaternion.identity;
            SpawnGhostAt(position, rotation, "스폰 지점");
        }

        /// <summary>Host 로컬 플레이어의 현재 위치에 바로 스폰한다(디버그). 배회 경계 밖이면 안으로 보정된다.</summary>
        public void SpawnGhostAtPlayer()
        {
            if (!TryGetLocalPlayerPose(out Vector3 position, out Quaternion rotation))
            {
                _lastStatus = "귀신 스폰 실패: 내 플레이어 오브젝트를 찾지 못했습니다.";
                return;
            }

            SpawnGhostAt(position, rotation, "내 위치");
        }

        private void SpawnGhostAt(Vector3 position, Quaternion rotation, string source)
        {
            if (!CanControl)
            {
                _lastStatus = "귀신 스폰 실패: Local Host(서버)가 필요합니다.";
                return;
            }

            if (_active != null && _active.IsSpawned)
            {
                _lastStatus = "이미 귀신이 스폰되어 있습니다.";
                return;
            }

            if (_ghostPrefab == null)
            {
                _lastStatus = "귀신 프리팹이 배선되지 않았습니다.";
                return;
            }

            GameObject instance = Instantiate(_ghostPrefab, position, rotation);
            _activeObject = instance.GetComponent<NetworkObject>();
            _active = instance.GetComponent<GhostPrototypeController>();
            _activeObject.Spawn();

            _active.ServerConfigureRoam(_roamCenter, _roamSize);
            _pushedForceActive = _forceActive;
            _pushedCleaningProgress = _cleaningProgress;
            _active.ServerSetForceActive(_forceActive);
            _active.ServerSetCleaningProgress(_cleaningProgress);

            _lastStatus = $"귀신을 스폰했습니다 ({source}).";
        }

        private static bool TryGetLocalPlayerPose(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            NetworkManager network = NetworkManager.Singleton;
            NetworkObject player = network != null && network.IsListening
                ? network.LocalClient?.PlayerObject
                : null;
            if (player == null)
                return false;

            position = player.transform.position;
            rotation = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);
            return true;
        }

        public void DespawnGhost()
        {
            if (!CanControl)
            {
                _lastStatus = "귀신 제거 실패: Local Host(서버)가 필요합니다.";
                return;
            }

            if (_activeObject != null && _activeObject.IsSpawned)
                _activeObject.Despawn(true);

            _active = null;
            _activeObject = null;
            _lastStatus = "귀신을 제거했습니다.";
        }

        public void ForceSpecialAttack()
        {
            if (!RequireGhost())
                return;

            _lastStatus = _active.ServerForceSpecialAttack()
                ? "특수 어택 Trigger: 경고 상태로 전환했습니다."
                : "지금 상태에서는 특수 어택을 걸 수 없습니다.";
        }

        public void ForceSuppression()
        {
            if (!RequireGhost())
                return;

            _lastStatus = _active.ServerForceSuppression()
                ? "강제 진정: 어택을 끊고 10초 억제합니다."
                : "이미 강제 진정 중입니다.";
        }

        public void ForcePhenomenon()
        {
            if (!RequireGhost())
                return;

            GhostPhenomenonKind kind = _active.ServerForcePhenomenon();
            _lastStatus = kind == GhostPhenomenonKind.None
                ? "현상을 실행할 수 없습니다."
                : $"초자연현상(랜덤): {kind}.";
        }

        public void ForcePhenomenon(GhostPhenomenonKind kind)
        {
            if (!RequireGhost())
                return;

            GhostPhenomenonKind fired = _active.ServerRunPhenomenon(kind);
            _lastStatus = fired == GhostPhenomenonKind.None
                ? $"{kind} 실행 실패."
                : $"초자연현상 시험: {kind}.";
        }

        public void ToggleGhostVisible()
        {
            if (!RequireGhost())
                return;

            bool next = !_active.DebugForceVisible;
            _active.ServerSetDebugForceVisible(next);
            _lastStatus = next
                ? "귀신 본체 강제 표시 ON (상태 무관)."
                : "귀신 본체 강제 표시 OFF — §3.3 노출 정책 복귀.";
        }

        public void ToggleForceActive()
        {
            if (!CanControl)
            {
                _lastStatus = "활동 강제 실패: Local Host(서버)가 필요합니다.";
                return;
            }

            _forceActive = !_forceActive;
            _lastStatus = _forceActive ? "활동 강제 ON." : "활동 강제 OFF.";
        }

        public void AddCleaningProgress(int delta)
        {
            if (!CanControl)
            {
                _lastStatus = "청소 진행도 변경 실패: Local Host(서버)가 필요합니다.";
                return;
            }

            _cleaningProgress = Mathf.Clamp(_cleaningProgress + delta, 0, 100);
            _lastStatus = $"청소 진행도 {_cleaningProgress}%.";
        }

        public void ResetCleaningProgress()
        {
            if (!CanControl)
            {
                _lastStatus = "청소 진행도 초기화 실패: Local Host(서버)가 필요합니다.";
                return;
            }

            _cleaningProgress = 0;
            _lastStatus = "청소 진행도를 0%로 되돌렸습니다.";
        }

        private bool RequireGhost()
        {
            if (!CanControl)
            {
                _lastStatus = "조작 실패: Local Host(서버)가 필요합니다.";
                return false;
            }

            if (_active == null || !_active.IsSpawned)
            {
                _lastStatus = "먼저 귀신을 스폰하세요.";
                return false;
            }

            return true;
        }

        private void PruneDeadGhost()
        {
            if (_active != null && !_active.IsSpawned)
            {
                _active = null;
                _activeObject = null;
            }
        }

        private void BuildSummary()
        {
            _summaryBuilder.Clear();

            if (!CanControl)
            {
                _summaryBuilder.Append("세션이 시작되면 표시됩니다 (Host 전용).");
                return;
            }

            string state = _active != null && _active.IsSpawned
                ? _active.Phase.ToString().ToUpperInvariant()
                : "(스폰 안 됨)";
            _summaryBuilder.Append("Ghost State      : ").AppendLine(state);

            if (_sanity != null
                && _sanity.TryGetTeamAverage(out _, out int rounded, out int living))
            {
                _summaryBuilder.Append("Team Sanity      : ").Append(rounded)
                    .Append("  (생존 ").Append(living).AppendLine(")");
            }
            else
            {
                _summaryBuilder.AppendLine("Team Sanity      : -  (생존 0)");
            }

            if (_active != null && _active.IsSpawned && _active.Phase == GhostPhase.Active)
            {
                _summaryBuilder.Append("Attack Check     : ")
                    .Append(_active.SecondsUntilNextRoll.ToString("0.0")).AppendLine(" sec");
            }
            else
            {
                _summaryBuilder.AppendLine("Attack Check     : -");
            }

            _summaryBuilder.Append("Cleaning Progress: ").Append(_cleaningProgress).Append('%');
            if (_active != null && _active.IsSpawned && _active.CleaningBoostActive)
                _summaryBuilder.Append("  (방해 증가)");
            _summaryBuilder.AppendLine();

            if (_active != null && _active.IsSpawned && _active.Phase == GhostPhase.Attack)
                _summaryBuilder.Append("Pursuit          : ").AppendLine(_active.PursuitSummary);

            if (_active != null && _active.IsSpawned
                && _active.Phase is GhostPhase.Idle or GhostPhase.Active)
            {
                _summaryBuilder.Append("Phenomena        : ").AppendLine(_active.PhenomenonSummary);
            }

            if (_forceActive)
                _summaryBuilder.AppendLine("(활동 강제 ON)");
        }
    }
}
