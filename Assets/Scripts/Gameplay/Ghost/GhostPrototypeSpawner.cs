using System.Text;
using GhostHunter.Core;
using GhostHunter.Gameplay.Cleaning;
using GhostHunter.Gameplay.Sanity;
using GhostHunter.Gameplay.Recovery;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 스테이지 씬의 Host 전용 서비스. 귀신 1마리를 자동 생성하고 실제 청소 진행도를 전달한다.
    /// 귀신 종류 목록(<see cref="_ghostTypes"/>)이 있으면 시작할 때 무작위 후보 중 하나를 고를 수 있고, Hub(Tab)에서
    /// 기존 귀신을 지우고 고른 종류로 다시 스폰할 수 있다. Hub는 스폰·제거와 어택·청소 조건을 시험한다.
    /// 팀 정신력 표시는 <see cref="ISanityTeamService"/> 값을 그대로 읽는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GhostPrototypeSpawner : MonoBehaviour, IGhostDebug
    {
        [Tooltip("기본 귀신 프리팹. 종류 목록이 비어 있으면 이것 하나만 쓴다.")]
        [SerializeField] private GameObject _ghostPrefab;
        [SerializeField] private Transform _spawnPoint;

        [Header("귀신 종류 (Hub 선택 · 시작 랜덤)")]
        [Tooltip("Hub 에서 고를 수 있는 귀신 종류. 비어 있으면 기본 귀신 프리팹 하나.")]
        [SerializeField] private GhostTypeOption[] _ghostTypes = System.Array.Empty<GhostTypeOption>();
        [Tooltip("스테이지 시작 때 종류 목록의 무작위 후보(In Random Pool) 중 하나를 고른다. 끄면 기본 귀신 프리팹.")]
        [SerializeField] private bool _randomizeInitialType;

        [Tooltip("귀신 경로(NavMesh)를 구울 집 루트. 자식의 물리 충돌체로 굽는다 — 스테이지 씬마다 그 씬의 집을 넣는다.")]
        [SerializeField] private Transform _navigationRoot;

        [Header("집 내부 활동 경계 (§3.1 · §9.1)")]
        [Tooltip("귀신의 이동·탐지·잡힘을 제한하는 집 내부 X/Z 상자. 집 밖 플레이어는 대상에서 제외한다.")]
        [SerializeField] private Vector3 _roamCenter = new(0f, 0.1f, -1f);
        [Tooltip("기본은 X/Z만 제한한다. Restrict Roam Height를 켜면 높이도 제한하여 한 층만 사용한다.")]
        [SerializeField] private Vector3 _roamSize = new(16f, 3f, 9f);

        [SerializeField] private bool _restrictRoamHeight;

        private readonly StringBuilder _summaryBuilder = new(256);

        private ISanityTeamService _sanity;
        private ICleaningService _cleaning;
        private GhostPrototypeController _active;
        private NetworkObject _activeObject;
        private GhostTypeOption[] _types;
        private int _inkerTypeIndex = int.MinValue; // 아직 안 찾음
        private int _activeTypeIndex = -1;
        private int _selectedTypeIndex;

        private int _cleaningProgress;
        private int _debugCleaningProgress;
        private bool _initialSpawnAttempted;
        private int _pushedCleaningProgress = -1;
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
        public GhostPrototypeController ActiveGhost => HasGhost ? _active : null;

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

        public int GhostTypeCount => Types.Length;

        /// <summary>지금 스폰된 귀신의 종류 인덱스. 없으면 -1. 호스트 이전 스냅샷에 담는다.</summary>
        public int ActiveGhostTypeIndex => HasGhost ? _activeTypeIndex : -1;

        public int SelectedGhostTypeIndex
        {
            get => Mathf.Clamp(_selectedTypeIndex, 0, Mathf.Max(0, Types.Length - 1));
            set => _selectedTypeIndex = Mathf.Clamp(value, 0, Mathf.Max(0, Types.Length - 1));
        }

        public string GetGhostTypeLabel(int index)
        {
            GhostTypeOption[] types = Types;
            if (index < 0 || index >= types.Length)
                return "-";
            return string.IsNullOrEmpty(types[index].Label)
                ? (types[index].Prefab != null ? types[index].Prefab.name : "(없음)")
                : types[index].Label;
        }

        /// <summary>종류 목록. 비어 있으면 기본 귀신 프리팹 하나로 만든다.</summary>
        private GhostTypeOption[] Types
        {
            get
            {
                if (_types != null)
                    return _types;
                _types = _ghostTypes != null && GhostTypePicker.FirstWithPrefab(_ghostTypes) >= 0
                    ? _ghostTypes
                    : new[]
                    {
                        // 목록이 없는 씬(Tutorial·DinoTest 등)은 이 씬의 귀신 하나 — 프리팹 이름으로 보인다.
                        new GhostTypeOption { Label = null, Prefab = _ghostPrefab, InRandomPool = true },
                    };
                return _types;
            }
        }

        private void Awake()
        {
            Services.TryGet(out _sanity);
            Services.TryGet(out _cleaning);
            int defaultIndex = GhostTypePicker.IndexOf(Types, _ghostPrefab);
            _selectedTypeIndex = defaultIndex >= 0 ? defaultIndex : Mathf.Max(0, GhostTypePicker.FirstWithPrefab(Types));

            if (_ghostPrefab == null)
                Debug.LogError($"{nameof(GhostPrototypeSpawner)}: 귀신 프리팹이 배선되지 않았습니다.", this);
            if (_navigationRoot == null)
                Debug.LogError($"{nameof(GhostPrototypeSpawner)}: 경로를 구울 집 루트가 배선되지 않았습니다.", this);
        }

        private void Update()
        {
            PruneDeadGhost();

            if (!CanControl || StageRecoveryGate.Restoring)
                return;

            if (_cleaning == null)
                Services.TryGet(out _cleaning);
            if (_sanity == null)
                Services.TryGet(out _sanity);
            _cleaningProgress = Mathf.Max(
                _debugCleaningProgress,
                _cleaning != null ? _cleaning.ProgressPercent : 0);

            if (!_initialSpawnAttempted)
            {
                _initialSpawnAttempted = true;
                if (_active == null)
                {
                    // 시작 종류: 무작위 후보 중 하나(Stage1 — 기본·인커) 또는 기본 귀신.
                    if (_randomizeInitialType)
                        _selectedTypeIndex = Mathf.Max(0, GhostTypePicker.PickRandom(Types, Random.value));
                    SpawnGhost();
                }
            }

            if (_active == null)
                return;

            if (_pushedCleaningProgress != _cleaningProgress)
            {
                _active.ServerSetCleaningProgress(_cleaningProgress);
                _pushedCleaningProgress = _cleaningProgress;
            }

        }

        /// <summary>Hub 에서 고른 종류(<see cref="SelectedGhostTypeIndex"/>)를 스폰 지점에 스폰한다.</summary>
        public void SpawnGhost()
        {
            Vector3 position = _spawnPoint != null ? _spawnPoint.position : transform.position;
            Quaternion rotation = _spawnPoint != null ? _spawnPoint.rotation : Quaternion.identity;
            SpawnGhostAt(position, rotation, "스폰 지점", SelectedGhostTypeIndex);
        }

        /// <summary>
        /// Hub — 기존 귀신을 지우고 고른 종류로 다시 스폰한다. <paramref name="atPlayer"/> 면 Host 플레이어 위치에.
        /// 새 귀신은 처음부터 다시 시작한다(상태·청소 강제 어택 기록·인커 글씨는 넘기지 않는다).
        /// </summary>
        public void ReplaceGhostWithSelected(bool atPlayer)
        {
            if (!CanControl)
            {
                _lastStatus = "귀신 교체 실패: Local Host(서버)가 필요합니다.";
                return;
            }

            Vector3 position;
            Quaternion rotation;
            string source;
            if (atPlayer)
            {
                if (!TryGetLocalPlayerPose(out position, out rotation))
                {
                    _lastStatus = "귀신 교체 실패: 내 플레이어 오브젝트를 찾지 못했습니다.";
                    return;
                }

                source = "내 위치";
            }
            else
            {
                position = _spawnPoint != null ? _spawnPoint.position : transform.position;
                rotation = _spawnPoint != null ? _spawnPoint.rotation : Quaternion.identity;
                source = "스폰 지점";
            }

            if (HasGhost)
                DespawnGhost();
            SpawnGhostAt(position, rotation, source, SelectedGhostTypeIndex);
        }

        public bool ServerRestoreStageState(StageRecoverySnapshot.GhostState snapshot)
        {
            if (!CanControl)
                return false;
            _initialSpawnAttempted = true;
            if (!snapshot.Exists)
                return true;
            // 예전 스냅샷은 종류가 없어 0 으로 읽힌다 — 범위 밖이면 기본 귀신.
            int typeIndex = snapshot.TypeIndex >= 0 && snapshot.TypeIndex < Types.Length
                ? snapshot.TypeIndex
                : SelectedGhostTypeIndex;
            _selectedTypeIndex = typeIndex;
            SpawnGhostAt(snapshot.Position, snapshot.Rotation, "호스트 이전", typeIndex);
            if (_active == null || !_active.IsSpawned)
                return false;
            _active.ServerRestoreStageState(snapshot);
            return true;
        }

        /// <summary>Host 로컬 플레이어의 현재 위치에 바로 스폰한다(디버그). 배회 경계 밖이면 안으로 보정된다.</summary>
        public void SpawnGhostAtPlayer()
        {
            if (!TryGetLocalPlayerPose(out Vector3 position, out Quaternion rotation))
            {
                _lastStatus = "귀신 스폰 실패: 내 플레이어 오브젝트를 찾지 못했습니다.";
                return;
            }

            SpawnGhostAt(position, rotation, "내 위치", SelectedGhostTypeIndex);
        }

        private void SpawnGhostAt(Vector3 position, Quaternion rotation, string source, int typeIndex)
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

            GhostTypeOption[] types = Types;
            if (typeIndex < 0 || typeIndex >= types.Length || types[typeIndex].Prefab == null)
                typeIndex = GhostTypePicker.FirstWithPrefab(types);
            GameObject prefab = typeIndex >= 0 ? types[typeIndex].Prefab : null;
            if (prefab == null)
            {
                _lastStatus = "귀신 프리팹이 배선되지 않았습니다.";
                return;
            }

            GameObject instance = Instantiate(prefab, position, rotation);
            _activeObject = instance.GetComponent<NetworkObject>();
            _active = instance.GetComponent<GhostPrototypeController>();
            _activeObject.Spawn();
            _activeTypeIndex = typeIndex;

            _active.ServerConfigureRoam(_roamCenter, _roamSize, _navigationRoot, _restrictRoamHeight);
            _pushedCleaningProgress = _cleaningProgress;
            _active.ServerSetCleaningProgress(_cleaningProgress);

            _lastStatus = $"귀신을 스폰했습니다 ({GetGhostTypeLabel(typeIndex)}, {source}).";
        }

        /// <summary>
        /// Hub — 인커 현상 하나를 바로 시작한다. 지금 귀신이 인커가 아니면(없어도) 종류 목록의 인커로
        /// 스폰 지점에 교체한 뒤 시작한다 — 인커 현상은 귀신 위치가 아니라 대상 플레이어 기준이라 자리는 상관없다.
        /// </summary>
        public void ForceInkerPhenomenon(InkerPhenomenon kind)
        {
            if (!CanControl)
            {
                _lastStatus = "인커 현상 실패: Local Host(서버)가 필요합니다.";
                return;
            }

            string prefix = string.Empty;
            if (!HasInkerGhost)
            {
                int inker = InkerTypeIndex;
                if (inker < 0)
                {
                    _lastStatus = "인커 현상 실패: 이 씬의 귀신 종류 목록에 인커가 없습니다.";
                    return;
                }

                _selectedTypeIndex = inker;
                ReplaceGhostWithSelected(false);
                if (!HasInkerGhost)
                    return; // 교체 실패 — _lastStatus 는 스폰 쪽이 채웠다.
                prefix = "인커로 교체 → ";
            }

            _lastStatus = prefix + _active.ServerForceInkerPhenomenon(kind);
        }

        /// <summary>Hub 에서 인커 현상을 시험할 수 있는가 — 지금 귀신이 인커이거나 종류 목록에 인커가 있다.</summary>
        public bool CanTestInker => HasInkerGhost || InkerTypeIndex >= 0;

        /// <summary>종류 목록에서 설정이 <see cref="GhostInkerSettings"/> 인 첫 프리팹의 인덱스. 없으면 -1.</summary>
        private int InkerTypeIndex
        {
            get
            {
                if (_inkerTypeIndex != int.MinValue)
                    return _inkerTypeIndex;

                _inkerTypeIndex = -1;
                GhostTypeOption[] types = Types;
                for (int i = 0; i < types.Length; i++)
                {
                    GameObject prefab = types[i].Prefab;
                    if (prefab != null && prefab.TryGetComponent(out GhostPrototypeController controller)
                        && controller.Settings is GhostInkerSettings)
                    {
                        _inkerTypeIndex = i;
                        break;
                    }
                }

                return _inkerTypeIndex;
            }
        }

        /// <summary>Hub — 인커 글씨를 모두 지운다.</summary>
        public void ClearInkerWritings()
        {
            if (!RequireGhost())
                return;
            _active.ServerClearInkerWritings();
            _lastStatus = "인커 글씨를 모두 지웠습니다.";
        }

        public bool HasInkerGhost => HasGhost && _active.IsInker;
        public bool HasClassicPhenomena => HasGhost && _active.HasClassicPhenomena;

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
                ? "강제 진정: 어택을 끊고 30초 자연 진정에 들어갑니다."
                : "어택 중에만 강제 종료할 수 있습니다.";
        }

        public void ForcePhenomenon()
        {
            if (!RequireGhost())
                return;

            GhostPhenomenonKind kind = _active.ServerForcePhenomenon();
            _lastStatus = kind == GhostPhenomenonKind.None
                ? _active.HasClassicPhenomena ? "현상을 실행할 수 없습니다." : "이 귀신은 공통 현상이 없습니다."
                : $"초자연현상(랜덤): {kind}.";
        }

        public void ForcePhenomenon(GhostPhenomenonKind kind)
        {
            if (!RequireGhost())
                return;

            GhostPhenomenonKind fired = _active.ServerRunPhenomenon(kind);
            _lastStatus = fired == GhostPhenomenonKind.None
                ? _active.HasClassicPhenomena ? $"{kind} 실행 실패." : "이 귀신은 공통 현상이 없습니다."
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

            _lastStatus = "귀신은 스테이지 시작부터 활동 상태입니다.";
        }

        public void AddCleaningProgress(int delta)
        {
            if (!CanControl)
            {
                _lastStatus = "청소 진행도 변경 실패: Local Host(서버)가 필요합니다.";
                return;
            }

            _debugCleaningProgress = Mathf.Clamp(_debugCleaningProgress + delta, 0, 100);
            _lastStatus = $"디버그 청소 진행도 {_debugCleaningProgress}%.";
        }

        public void ResetCleaningProgress()
        {
            if (!CanControl)
            {
                _lastStatus = "청소 진행도 초기화 실패: Local Host(서버)가 필요합니다.";
                return;
            }

            _debugCleaningProgress = 0;
            _lastStatus = "디버그 청소 진행도를 0%로 되돌렸습니다.";
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
            _summaryBuilder.Append("Ghost Type       : ")
                .AppendLine(HasGhost ? GetGhostTypeLabel(_activeTypeIndex) : "-");
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
            if (_cleaningProgress >= 40)
                _summaryBuilder.Append("  (어택 조건 충족)");
            _summaryBuilder.AppendLine();

            if (_active != null && _active.IsSpawned && _active.Phase == GhostPhase.Attack)
            {
                _summaryBuilder.Append("Pursuit          : ").AppendLine(_active.PursuitSummary);
                string dino = _active.DinoSummary;
                if (dino != "-")
                    _summaryBuilder.Append("Dino Charge      : ").AppendLine(dino);
            }

            if (_active != null && _active.IsSpawned
                && _active.Phase is GhostPhase.Idle or GhostPhase.Active)
            {
                _summaryBuilder.Append("Phenomena        : ").AppendLine(_active.PhenomenonSummary);
            }

            if (_active != null && _active.IsSpawned && _active.IsInker)
                _summaryBuilder.Append("Inker            : ").AppendLine(_active.InkerSummary);

        }
    }
}
