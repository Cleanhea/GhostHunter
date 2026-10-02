using System.Collections.Generic;
using GhostHunter.Core;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Sanity;
using GhostHunter.Gameplay.Recovery;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 서버 권위 귀신 프로토타입. 서버는 <see cref="GhostStateMachine"/> 로 상태를 정하고, 어택 중에는
    /// 원뿔 시야·소리로 플레이어를 탐지해 추격·수색·공격(§8·§9)한다. 잡히면 기존 정신력 시스템의
    /// <see cref="SanityNetworkState.ServerMarkDead"/> 를 호출한다. 클라이언트는 복제된
    /// <see cref="GhostPhase"/> 로 본체·점광원·빨간 시야 표시만 재생한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(CharacterController))]
    public sealed class GhostPrototypeController : NetworkBehaviour
    {
        private const float TurnSpeedDegrees = 540f;
        private const int MaxTrackedPlayers = 4;
        private const float MinRoamGiveUpSeconds = 4f;
        private const int RoamPickAttempts = 20;
        private const int SearchWanderPickAttempts = 6;
        private const float RoamScoreJitterSeconds = 4f;
        private const float LookAroundTurnDegrees = 150f;
        private const float DoorRayHeight = 1f;
        private const float DoorCollisionRefreshInterval = 0.2f;
        private const float NavSampleRadius = 1f;
        private const float NavSampleFallbackRadius = 2.5f;
        // 층고 3m 보다 작게 — 경로 코너·목적지가 바로 위/아래 층이면 '도착'으로 치지 않는다.
        private const float ArrivalVerticalTolerance = 1.2f;
        private const float StuckCheckInterval = 1f;
        private const float StuckMinProgress = 0.25f;
        private const float ShakePulseInterval = 0.05f;
        private const float ShakeAngularFrequency = Mathf.PI * 2f * 9f; // 초당 9회 좌우 왕복
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private enum Pursuit : byte
        {
            Roam = 0,
            Chase = 1,
            Search = 2,
        }

        [SerializeField] private GhostPrototypeSettings _settings;

        [Header("Visuals")]
        [SerializeField] private GameObject _body;
        [SerializeField] private Renderer[] _bodyRenderers;
        [SerializeField] private Light _stateLight;
        [SerializeField] private GameObject _visionConeRoot;
        [SerializeField] private MeshFilter _visionConeFilter;
        [SerializeField] private AudioClip _warningHeartbeatClip;

        [Header("Phenomena")]
        [SerializeField] private GhostPhenomenaPlayer _phenomenaPlayer;

        private readonly NetworkVariable<GhostPhase> _phase = new(
            GhostPhase.Active,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _highRisk = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        // 디버그 전용. 상태 노출 정책(§3.3)과 무관하게 본체를 강제로 보이게 한다. F1 HUD 토글.
        private readonly NetworkVariable<bool> _debugForceVisible = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly SanityNetworkState[] _players = new SanityNetworkState[MaxTrackedPlayers];
        private readonly Dictionary<ulong, Vector3> _previousPlayerPositions = new();
        private readonly Dictionary<ulong, float> _playerSpeeds = new();
        private readonly Dictionary<ulong, float> _bodyWitnessTimers = new();

        private CharacterController _controller;
        private GhostStateMachine _machine;
        private GhostPhenomenaDirector _phenomena;
        private ISanityTeamService _sanity;
        private SanityNetworkState _localViewer;
        private Mesh _generatedConeMesh;
        private AudioSource _warningAudio;
        private AudioClip _generatedHeartbeat;
        private MaterialPropertyBlock _bodyBlock;
        private bool _serverReady;

        private FurnitureGrabTarget[] _sceneFurniture;
        private DoorInteractable[] _sceneDoors;
        private Collider[][] _sceneDoorColliders;
        private bool[] _doorCollisionIgnored;
        private float _doorCollisionRefreshRemaining;
        private GhostPhenomenonKind _lastPhenomenon;

        private readonly struct ShakeTarget
        {
            public readonly Rigidbody Body;
            public readonly Vector3 Axis;

            public ShakeTarget(Rigidbody body, Vector3 axis)
            {
                Body = body;
                Axis = axis;
            }
        }

        private readonly List<ShakeTarget> _shakeTargets = new();
        private float _shakeSign;
        private float _shakeElapsed;
        private float _shakeRemaining;
        private float _shakePulseRemaining;

        private Vector3 _roamCenter;
        private bool _restrictRoamHeight;
        private Vector3 _roamExtents = new(6f, 1.5f, 5f);
        private Vector3 _roamDestination;
        private float _roamGiveUpAt;
        private GhostRoamMemory _roamMemory;
        private float _lookAroundRemaining;
        private float _lookYaw;

        private Pursuit _pursuit;
        private SanityNetworkState _target;
        private bool _targetWasVisible;
        private SanityNetworkState _witnessedHidingPlayer;
        private SanityNetworkState _witnessedBurrowPlayer;
        private Vector3 _lastKnownPosition;

        // 플레이어별 '침대 밑 은신' 성립 판정(§9.5, 사용자 확정 2026-09-03). 어택 중에만 돈다.
        private readonly Dictionary<ulong, BedHideEvaluator> _bedHide = new();

        // 플레이어별 '굴착 은신 무효' 판정(두더지 스킬 §5.2.1, 사용자 확정 2026-09-05).
        // 감지된 상태에서 매몰하면 땅속에서도 계속 감지된다. 어택 중에만 돈다.
        private readonly Dictionary<ulong, BurrowExposureTracker> _burrowExposure = new();
        private float _searchRemaining;
        private float _targetSelectionRemaining;
        private float _repathRemaining;
        private float _pathRebuildRemaining;
        private float _catchCooldownRemaining;
        private NavMeshPath _navPath;
        private readonly Vector3[] _pathCorners = new Vector3[64];
        private readonly Vector3[] _probeCorners = new Vector3[64];
        private int _pathCornerCount;
        private int _pathCornerIndex;
        private Vector3 _pathDestination;
        private bool _hasPath;
        private bool _navigationWarningLogged;
        private float _stuckCheckRemaining;
        private Vector3 _stuckCheckPosition;

        // 서버가 집 루트로 구운 귀신 전용 NavMesh 와, 배회 목적지를 면적 비례로 뽑기 위한 삼각형 캐시.
        private NavMeshData _navMeshData;
        private NavMeshDataInstance _navMeshInstance;
        private Vector3[] _navVertices;
        private int[] _navIndices;
        private float[] _navCumulativeArea;
        private float _navTotalArea;

        private Vector3 _searchDestination;
        private bool _searchWandering;

        private readonly GhostTrackingMemory _trackingMemory = new();
        private GhostSearchMemory _searchMemory;
        private float _searchScanRemaining;
        private int _aiFeatureMask;
        private bool _hasPendingImpact;
        private Vector3 _pendingImpactPosition;
        private float _pendingImpactAt;
        private float _nextImpactAt;
        private float _tension;
        private SanityNetworkState _witnessedBedPlayer;

        private int _cleaningProgress;
        private StageRecoverySnapshot.GhostState _pendingRecoveryState;

        public GhostPhase Phase => _phase.Value;
        public float SecondsUntilNextRoll => _machine != null ? _machine.SecondsUntilNextRoll : 0f;
        public bool CleaningBoostActive => _machine != null && _machine.CleaningBoostActive;
        public int CleaningProgress => _cleaningProgress;

        public StageRecoverySnapshot.GhostState CaptureStageState()
        {
            var snapshot = new StageRecoverySnapshot.GhostState
            {
                Exists = IsSpawned,
                Position = transform.position,
                Rotation = transform.rotation,
                PhenomenonCooldown = _phenomena != null ? _phenomena.SecondsUntilNext : 0f,
                LastPhenomenon = (int)_lastPhenomenon,
                PhenomenaPoolMask = _phenomena != null ? _phenomena.PoolMask : 0,
                CleaningProgress = _cleaningProgress,
                Pursuit = (int)_pursuit,
                TargetSteamId = SteamIdOf(_target),
                WitnessedHidingSteamId = SteamIdOf(_witnessedHidingPlayer),
                WitnessedBurrowSteamId = SteamIdOf(_witnessedBurrowPlayer),
                TargetWasVisible = _targetWasVisible,
                LastKnownPosition = _lastKnownPosition,
                RoamDestination = _roamDestination,
                RoamGiveUpRemaining = Mathf.Max(0f, _roamGiveUpAt - Time.time),
                SearchRemaining = _searchRemaining,
                TargetSelectionRemaining = _targetSelectionRemaining,
                RepathRemaining = _repathRemaining,
                PathRebuildRemaining = _pathRebuildRemaining,
                CatchCooldownRemaining = _catchCooldownRemaining,
            };
            var tracked = new List<StageRecoverySnapshot.GhostTrackedPlayerState>(4);
            for (int i = 0; i < _players.Length; i++)
            {
                SanityNetworkState player = _players[i];
                ulong steamId = SteamIdOf(player);
                if (steamId == 0)
                    continue;
                ulong clientId = player.OwnerClientId;
                _bedHide.TryGetValue(clientId, out BedHideEvaluator bed);
                _burrowExposure.TryGetValue(clientId, out BurrowExposureTracker burrow);
                _bodyWitnessTimers.TryGetValue(clientId, out float witness);
                tracked.Add(new StageRecoverySnapshot.GhostTrackedPlayerState
                {
                    SteamId = steamId,
                    BodyWitnessRemaining = witness,
                    BedConcealTimer = bed != null ? bed.ConcealTimer : 0f,
                    BedGranted = bed != null && bed.Granted,
                    WasBurrowed = burrow != null && burrow.WasBurrowed,
                    BurrowExposed = burrow != null && burrow.Exposed,
                });
            }
            snapshot.TrackedPlayers = tracked.ToArray();
            if (_machine != null)
            {
                GhostStateMachine.Snapshot machine = _machine.CaptureSnapshot();
                snapshot.Phase = (int)machine.Phase;
                snapshot.PhaseElapsed = machine.PhaseElapsed;
                snapshot.NextRoll = machine.SecondsUntilNextRoll;
                snapshot.CleaningThresholdReached = machine.CleaningThresholdReached;
                snapshot.HighRiskAttack = machine.HighRiskAttack;
                snapshot.AttackStartedBelowRecoveryThreshold =
                    machine.AttackStartedBelowRecoveryThreshold;
                snapshot.LastTeamSanity = machine.LastTeamSanity;
            }
            return snapshot;
        }

        public void ServerRestoreStageState(StageRecoverySnapshot.GhostState snapshot)
        {
            if (!IsServer || !IsSpawned || _machine == null)
                return;
            _machine.RestoreSnapshot(new GhostStateMachine.Snapshot((GhostPhase)snapshot.Phase,
                snapshot.PhaseElapsed, snapshot.NextRoll, snapshot.CleaningThresholdReached,
                snapshot.HighRiskAttack, snapshot.AttackStartedBelowRecoveryThreshold,
                snapshot.LastTeamSanity));
            if (_phenomena != null)
                _phenomena.RestorePool(snapshot.PhenomenaPoolMask);
            _phenomena?.RestoreStageState(snapshot.PhenomenonCooldown,
                (GhostPhenomenonKind)snapshot.LastPhenomenon);
            _lastPhenomenon = (GhostPhenomenonKind)snapshot.LastPhenomenon;
            _cleaningProgress = snapshot.CleaningProgress;
            _pendingRecoveryState = snapshot;
            _phase.Value = _machine.Phase;
            _highRisk.Value = _machine.IsHighRiskAttack;
            _controller.enabled = false;
            transform.SetPositionAndRotation(snapshot.Position, snapshot.Rotation);
            _controller.enabled = true;
            _roamDestination = snapshot.RoamDestination;
            _roamGiveUpAt = Time.time + Mathf.Max(0f, snapshot.RoamGiveUpRemaining);
            _lastKnownPosition = snapshot.LastKnownPosition;
            _target = null;
            _witnessedHidingPlayer = null;
            _witnessedBurrowPlayer = null;
            _pursuit = snapshot.Pursuit is >= 0 and <= 2
                ? (Pursuit)snapshot.Pursuit : Pursuit.Roam;
            _targetWasVisible = snapshot.TargetWasVisible;
            _searchRemaining = snapshot.SearchRemaining;
            _targetSelectionRemaining = snapshot.TargetSelectionRemaining;
            _repathRemaining = snapshot.RepathRemaining;
            _pathRebuildRemaining = snapshot.PathRebuildRemaining;
            _catchCooldownRemaining = snapshot.CatchCooldownRemaining;
            _hasPath = false;
            _trackingMemory.Clear();
            _hasPendingImpact = false;
            _searchWandering = false;
            _searchScanRemaining = 0f;
            _witnessedBedPlayer = null;
            if (_searchMemory != null)
                _searchMemory.Clear();
            _bedHide.Clear();
            _burrowExposure.Clear();
            _bodyWitnessTimers.Clear();
            _serverReady = true;
        }

        public void ServerRebindRecoveredPlayers()
        {
            if (!IsServer || !IsSpawned || _pendingRecoveryState.TrackedPlayers == null)
                return;
            if (_target == null)
                _target = FindPlayerBySteamId(_pendingRecoveryState.TargetSteamId);
            if (_settings.EvidenceTrackingEnabled && _pursuit == Pursuit.Search
                && _target != null && _target.IsProne && BedHideZone.Contains(_target.transform.position))
                _witnessedBedPlayer = _target;
            if (_witnessedHidingPlayer == null)
                _witnessedHidingPlayer = FindPlayerBySteamId(
                    _pendingRecoveryState.WitnessedHidingSteamId);
            if (_witnessedBurrowPlayer == null)
                _witnessedBurrowPlayer = FindPlayerBySteamId(
                    _pendingRecoveryState.WitnessedBurrowSteamId);
            foreach (StageRecoverySnapshot.GhostTrackedPlayerState state
                in _pendingRecoveryState.TrackedPlayers)
            {
                SanityNetworkState player = FindPlayerBySteamId(state.SteamId);
                if (player == null)
                    continue;
                ulong id = player.OwnerClientId;
                if (!_bedHide.ContainsKey(id))
                {
                    var bed = new BedHideEvaluator();
                    bed.Restore(state.BedConcealTimer, state.BedGranted);
                    _bedHide[id] = bed;
                }
                if (!_burrowExposure.ContainsKey(id))
                {
                    var burrow = new BurrowExposureTracker();
                    burrow.Restore(state.WasBurrowed, state.BurrowExposed);
                    _burrowExposure[id] = burrow;
                }
                if (state.BodyWitnessRemaining > 0f && !_bodyWitnessTimers.ContainsKey(id))
                    _bodyWitnessTimers[id] = state.BodyWitnessRemaining;
            }
        }

        private static ulong SteamIdOf(SanityNetworkState player)
            => player != null ? player.GetComponent<Player.PlayerNameTag>()?.SteamId ?? 0 : 0;

        private static SanityNetworkState FindPlayerBySteamId(ulong steamId)
        {
            if (steamId == 0)
                return null;
            foreach (Player.PlayerNameTag tag in FindObjectsByType<Player.PlayerNameTag>(
                FindObjectsSortMode.None))
                if (tag.IsSpawned && tag.SteamId == steamId)
                    return tag.GetComponent<SanityNetworkState>();
            return null;
        }

        public GhostPhenomenonKind LastPhenomenon => _lastPhenomenon;
        public float SecondsUntilNextPhenomenon => _phenomena != null ? _phenomena.SecondsUntilNext : 0f;
        public bool DebugForceVisible => _debugForceVisible.Value;

        public string PhenomenonSummary => _serverReady && _phase.Value is GhostPhase.Idle or GhostPhase.Active
            ? $"{(_lastPhenomenon == GhostPhenomenonKind.None ? "-" : _lastPhenomenon.ToString())}" +
              $"  (다음 {SecondsUntilNextPhenomenon:0.0}s)"
            : "-";

        public string PursuitSummary => _serverReady && _phase.Value == GhostPhase.Attack
            ? _pursuit.ToString().ToUpperInvariant()
            : "-";

        private void Awake()
        {
            _navPath = new NavMeshPath();
            _controller = GetComponent<CharacterController>();
            _warningAudio = GetComponent<AudioSource>();
            if (_warningAudio == null)
                _warningAudio = gameObject.AddComponent<AudioSource>();
            _warningAudio.spatialBlend = 0f;
            _warningAudio.playOnAwake = false;
            _warningAudio.loop = true;
            _warningAudio.volume = 0.5f;
            _generatedHeartbeat = _warningHeartbeatClip == null ? CreateHeartbeatClip() : null;
            _warningAudio.clip = _warningHeartbeatClip != null ? _warningHeartbeatClip : _generatedHeartbeat;
            IgnoreLevelActorCollisions();

            if (_settings == null)
            {
                Debug.LogError($"{nameof(GhostPrototypeController)}: 설정 에셋이 없습니다.", this);
                enabled = false;
            }
        }

        /// <summary>
        /// 귀신은 유령이다 — 플레이어·가구 콜라이더를 밀거나 막지 않는다. 벽·바닥(Default)과는
        /// 계속 충돌해 이동이 정상적으로 미끄러진다. 레이어 간 무시라 전역 1회면 되고 idempotent 하다.
        /// (물건 흔들기가 귀신 캡슐에 걸려 이상하게 튀던 문제도 이걸로 사라진다.)
        /// </summary>
        private static void IgnoreLevelActorCollisions()
        {
            int ghost = GameLayers.GhostPrototype;
            if (ghost < 0)
                return;

            if (GameLayers.Player >= 0)
                Physics.IgnoreLayerCollision(ghost, GameLayers.Player, true);
            if (GameLayers.Furniture >= 0)
                Physics.IgnoreLayerCollision(ghost, GameLayers.Furniture, true);
        }

        public override void OnNetworkSpawn()
        {
            if (_settings == null)
                return;

            RebuildVisionCone();
            IgnoreLevelActorCollisions();

            _phase.OnValueChanged += HandlePhaseChanged;
            _highRisk.OnValueChanged += HandleHighRiskChanged;
            _debugForceVisible.OnValueChanged += HandleDebugVisibleChanged;
            TryBindLocalViewer();
            ApplyPhaseVisual(_phase.Value);

            if (!IsServer)
                return;

            if (!Services.TryGet(out _sanity))
            {
                Debug.LogError(
                    $"{nameof(GhostPrototypeController)}: ISanityTeamService 를 찾지 못했습니다. " +
                    "Game 씬에서 스폰됐는지 확인하세요.",
                    this);
                enabled = false;
                return;
            }

            _machine = new GhostStateMachine(_settings);
            _phenomena = new GhostPhenomenaDirector(_settings);
            _roamMemory = new GhostRoamMemory(_settings.RoamCellSize, _settings.RoamMemorySeconds);
            _roamCenter = transform.position;
            _roamDestination = transform.position;
            _phase.Value = GhostPhase.Active;
            _searchMemory = new GhostSearchMemory(_settings.RoamCellSize, _settings.SearchDuration);
            _aiFeatureMask = _settings.AiFeatureMask;
            _trackingMemory.Clear();
            FurnitureNetworkPhysics.ServerImpactReported += HandleServerImpact;
            _serverReady = true;
        }

        public override void OnNetworkDespawn()
        {
            FurnitureNetworkPhysics.ServerImpactReported -= HandleServerImpact;
            _trackingMemory.Clear();
            _searchMemory = null;
            _hasPendingImpact = false;
            _witnessedBedPlayer = null;
            _tension = 0f;
            _phase.OnValueChanged -= HandlePhaseChanged;
            _highRisk.OnValueChanged -= HandleHighRiskChanged;
            _debugForceVisible.OnValueChanged -= HandleDebugVisibleChanged;
            if (_localViewer != null)
            {
                _localViewer.AliveStateChanged -= HandleLocalViewerAliveChanged;
                _localViewer.SanityChanged -= HandleLocalViewerSanityChanged;
            }
            _localViewer = null;
            _sanity = null;
            _machine = null;
            _phenomena = null;
            _sceneFurniture = null;
            _sceneDoors = null;
            _sceneDoorColliders = null;
            _doorCollisionIgnored = null;
            _roamMemory = null;
            _shakeTargets.Clear();
            _shakeRemaining = 0f;
            _target = null;
            _serverReady = false;
            _previousPlayerPositions.Clear();
            _playerSpeeds.Clear();
            _bodyWitnessTimers.Clear();
            _bedHide.Clear();
            _burrowExposure.Clear();
            RemoveNavMesh();
            ApplyEnvironmentLights(GhostPhase.Active);
            if (_warningAudio != null)
                _warningAudio.Stop();

            if (_generatedConeMesh != null)
            {
                Destroy(_generatedConeMesh);
                _generatedConeMesh = null;
            }
            if (_generatedHeartbeat != null)
            {
                Destroy(_generatedHeartbeat);
                _generatedHeartbeat = null;
            }
        }

        public override void OnDestroy()
        {
            FurnitureNetworkPhysics.ServerImpactReported -= HandleServerImpact;
            // NavMesh 데이터는 씬과 무관하게 전역에 남으므로 despawn 없이 파괴되는 경우도 치운다.
            RemoveNavMesh();
            base.OnDestroy();
        }

        private void Update()
        {
            if (IsSpawned && _localViewer == null)
                TryBindLocalViewer();

            if (IsSpawned && _phase.Value == GhostPhase.Warning)
            {
                PulseWarningLight();
                ApplyEnvironmentLights(GhostPhase.Warning);
            }
            else if (IsSpawned && _phase.Value == GhostPhase.Attack)
            {
                ApplyEnvironmentLights(GhostPhase.Attack);
            }

            if (!IsServer || !_serverReady || StageRecoveryGate.Restoring)
                return;

            ServerTick(Time.deltaTime);
        }

        /// <summary>
        /// 스폰 직후 스포너가 부른다. 집 X/Z 경계를 정하고 집 루트의 충돌체로 귀신 전용 NavMesh 를 굽는다.
        /// 배회 목적지는 이 NavMesh 에서 뽑으므로 계단으로 이어진 모든 층이 대상이다(MAP-11).
        /// </summary>
        public void ServerConfigureRoam(Vector3 center, Vector3 size, Transform navigationRoot,
            bool restrictRoamHeight = false)
        {
            if (!IsServer)
                return;

            _roamCenter = center;
            _restrictRoamHeight = restrictRoamHeight;
            _roamExtents = new Vector3(
                Mathf.Max(0.5f, size.x * 0.5f),
                Mathf.Max(0.5f, size.y * 0.5f),
                Mathf.Max(0.5f, size.z * 0.5f));
            _roamGiveUpAt = 0f;
            ClampToHouseBounds();
            EnsureNavMesh(navigationRoot);
        }

        public void ServerSetCleaningProgress(int percent)
        {
            if (IsServer)
                _cleaningProgress = Mathf.Clamp(percent, 0, 100);
        }

        public bool ServerForceSpecialAttack()
        {
            return IsServer && _machine != null && _machine.ForceSpecialAttack();
        }

        public bool ServerForceSuppression()
        {
            return IsServer && _machine != null && _machine.ForceSuppression();
        }

        /// <summary>디버그 — 상태와 무관하게 본체 렌더를 강제로 켠다/끈다(§3.3 정책은 그대로).</summary>
        public void ServerSetDebugForceVisible(bool value)
        {
            if (IsServer && IsSpawned)
                _debugForceVisible.Value = value;
        }

        private void ServerTick(float deltaTime)
        {
            ClampToHouseBounds();
            RefreshAiFeatures();
            _roamMemory.MarkVisited(transform.position, Time.time);
            RefreshDoorCollisions(deltaTime);

            int playerCount = _sanity.CopyPlayerStates(_players);
            UpdatePlayerSpeeds(playerCount, deltaTime);

            bool hasLiving = _sanity.TryGetTeamAverage(out _, out int teamSanity, out int living)
                && living > 0;
            if (!hasLiving)
                teamSanity = 100;

            GhostPhase previous = _phase.Value;
            GhostPhase current = _machine.Tick(deltaTime, teamSanity, hasLiving, _cleaningProgress);
            if (_highRisk.Value != _machine.IsHighRiskAttack)
                _highRisk.Value = _machine.IsHighRiskAttack;
            ServerTickBodyWitness(deltaTime, playerCount, current);
            if (current != previous)
            {
                _phase.Value = current;
                if (current != GhostPhase.Attack)
                {
                    ResetPursuit();
                    _bedHide.Clear();
                    _burrowExposure.Clear();
                }
            }

            if (_catchCooldownRemaining > 0f)
                _catchCooldownRemaining = Mathf.Max(0f, _catchCooldownRemaining - deltaTime);

            if (current == GhostPhase.Attack)
                ServerTickAttack(deltaTime, playerCount);
            else
                ServerTickRoam(deltaTime, _settings.RoamSpeed, attacking: false);

            UpdateTension(deltaTime, current, playerCount);
            ServerTickPhenomena(deltaTime, current, teamSanity);
            ServerTickShake(deltaTime);
        }

        private void RefreshAiFeatures()
        {
            int mask = _settings.AiFeatureMask;
            if (_aiFeatureMask == mask)
                return;
            _aiFeatureMask = mask;
            _targetSelectionRemaining = 0f;
            _pathRebuildRemaining = 0f;
            _searchScanRemaining = 0f;
            _searchWandering = false;
            _hasPendingImpact = false;
            _trackingMemory.Clear();
            _searchMemory.Clear();
            if (!_settings.EvidenceTrackingEnabled)
                _witnessedBedPlayer = null;
        }

        private void HandleServerImpact(Vector3 position, float speed)
        {
            if (!IsServer || !_serverReady || !IsSpawned || StageRecoveryGate.Restoring
                || !_settings.ImpactInvestigationEnabled || _phase.Value != GhostPhase.Attack
                || _pursuit == Pursuit.Chase || _witnessedHidingPlayer != null
                || _witnessedBurrowPlayer != null || _witnessedBedPlayer != null
                || speed < _settings.ImpactMinimumSpeed || Time.time < _nextImpactAt
                || !IsInsideHouseBounds(position) || DrillCarSafeZone.Contains(position))
                return;

            float radius = _settings.ImpactHearingRadius;
            Vector3 ear = transform.position + Vector3.up * _settings.GhostEyeHeight;
            int mask = GameLayers.NonGhostPrototypeRaycastMask;
            if (GameLayers.Furniture >= 0)
                mask &= ~(1 << GameLayers.Furniture);
            if (GameLayers.Player >= 0)
                mask &= ~(1 << GameLayers.Player);
            if (Physics.Linecast(ear, position + Vector3.up * DoorRayHeight,
                    mask, QueryTriggerInteraction.Ignore))
                radius *= _settings.OccludedImpactMultiplier;
            if (Vector3.Distance(ear, position) > radius
                || !TryMeasurePath(position, out float length) || length > radius)
                return;

            _hasPendingImpact = true;
            _pendingImpactPosition = position;
            _pendingImpactAt = Time.time;
            _nextImpactAt = Time.time + _settings.ImpactInvestigateCooldown;
        }

        private void UpdateTension(float deltaTime, GhostPhase phase, int playerCount)
        {
            if (!_settings.TensionPacingEnabled)
            {
                _tension = 0f;
                _phenomena.SetTension(0f);
                return;
            }
            bool pressure = phase is GhostPhase.Warning or GhostPhase.Attack;
            if (!pressure)
            {
                for (int i = 0; i < playerCount; i++)
                {
                    SanityNetworkState player = _players[i];
                    if (player != null && player.HasSanity && IsInsideHouseBounds(player.transform.position)
                        && !DrillCarSafeZone.Contains(player.transform.position)
                        && Vector3.Distance(player.transform.position, transform.position)
                            <= _settings.PhenomenonRadius && CanWitnessGhostBody(player))
                    {
                        pressure = true;
                        break;
                    }
                }
            }
            _tension = pressure ? 1f : Mathf.MoveTowards(_tension, 0f,
                deltaTime / Mathf.Max(0.1f, _settings.TensionReleaseSeconds));
            _phenomena.SetTension(_tension);
        }

        private void ServerTickPhenomena(float deltaTime, GhostPhase phase, int teamSanity)
        {
            // 디렉터가 상태를 스스로 걸러 낸다 — 평상시·활동에서만 현상이 나온다(§4.1·§6.1).
            GhostPhenomenonKind fired = _phenomena.Tick(
                deltaTime,
                phase,
                teamSanity,
                _machine.CleaningBoostActive);

            if (fired != GhostPhenomenonKind.None)
                ServerExecutePhenomenon(fired);
        }

        private void ServerTickBodyWitness(float deltaTime, int playerCount, GhostPhase phase)
        {
            for (int i = 0; i < playerCount; i++)
            {
                SanityNetworkState player = _players[i];
                if (player == null || !player.IsSpawned)
                    continue;

                ulong id = player.OwnerClientId;
                if (phase != GhostPhase.Active || !CanWitnessGhostBody(player))
                {
                    _bodyWitnessTimers.Remove(id);
                    continue;
                }

                if (!_bodyWitnessTimers.TryGetValue(id, out float remaining))
                {
                    player.ServerApplyGhostBodyWitnessed();
                    _bodyWitnessTimers[id] = 5f;
                    continue;
                }

                remaining -= deltaTime;
                if (remaining <= 0f)
                {
                    player.ServerApplyGhostBodyWitnessed();
                    remaining = 5f;
                }

                _bodyWitnessTimers[id] = remaining;
            }
        }

        private bool CanWitnessGhostBody(SanityNetworkState player)
        {
            if (!player.HasSanity || player.IsBurrowed
                || HidingSpot.Contains(player.transform.position)
                || (player.IsProne && BedHideZone.Contains(player.transform.position)))
            {
                return false;
            }

            Vector3 eye = player.transform.position + Vector3.up * _settings.PhenomenonWitnessEyeHeight;
            Vector3 ghostCenter = transform.position + Vector3.up * _settings.GhostEyeHeight;
            Vector3 toGhost = ghostCenter - eye;
            float distance = toGhost.magnitude;
            return GhostVision.IsInsideCone(
                    player.transform.forward,
                    toGhost,
                    _settings.PhenomenonWitnessAngle,
                    distance,
                    _settings.PhenomenonWitnessDistance)
                && HasLineOfSight(eye, ghostCenter, transform);
        }

        /// <summary>F1 디버그 — 주기를 무시하고 현상 하나를 즉시 실행한다.</summary>
        public GhostPhenomenonKind ServerForcePhenomenon()
        {
            if (!IsServer || !_serverReady)
                return GhostPhenomenonKind.None;

            GhostPhenomenonKind kind = _phenomena.ForceNext(
                _phase.Value,
                CurrentTeamSanity(),
                _machine.CleaningBoostActive);

            if (kind != GhostPhenomenonKind.None)
                ServerExecutePhenomenon(kind);

            return kind;
        }

        /// <summary>F1 디버그 — 지정한 현상 하나를 상태·주기와 무관하게 즉시 실행한다(기능별 패턴 시험).</summary>
        public GhostPhenomenonKind ServerRunPhenomenon(GhostPhenomenonKind kind)
        {
            if (!IsServer || !_serverReady || kind == GhostPhenomenonKind.None)
                return GhostPhenomenonKind.None;

            if (!ServerExecutePhenomenon(kind))
            {
                Debug.Log(
                    $"[GhostPrototype] {kind} 시험 — 귀신 반경 {_settings.PhenomenonRadius:0.#}m 안에 대상 가구·문이 없습니다.",
                    this);
            }

            return kind;
        }

        private int CurrentTeamSanity()
        {
            return _sanity != null
                && _sanity.TryGetTeamAverage(out _, out int teamSanity, out int living)
                && living > 0
                    ? teamSanity
                    : 100;
        }

        /// <summary>현상을 실행하고 RPC로 연출을 뿌린다. 물리·문 현상이 대상을 찾았으면 true.</summary>
        private bool ServerExecutePhenomenon(GhostPhenomenonKind kind)
        {
            _lastPhenomenon = kind;
            Vector3 origin = transform.position;
            int seed = Random.Range(1, int.MaxValue);
            bool affected = true;

            switch (kind)
            {
                case GhostPhenomenonKind.ObjectShake:
                    affected = ServerShakeNearbyFurniture();
                    break;

                case GhostPhenomenonKind.SmallObjectDrop:
                    affected = ServerDropNearbyProp();
                    break;

                case GhostPhenomenonKind.DoorMove:
                    affected = ServerMoveNearbyDoor();
                    break;

                // DrawerOpen / LightFlicker / Apparition 은 연출뿐이라 RPC 로만 재생한다(§10).
                // WallKnock / Footsteps 는 오디오 에셋 대기 — 디렉터 Pool 에서 이미 빠져 있다.
            }

            ServerCheckPhenomenonWitnessed(origin);
            PlayPhenomenonRpc(kind, origin, seed);
            if (_settings.TensionPacingEnabled && kind == GhostPhenomenonKind.Apparition)
            {
                _tension = 1f;
                _phenomena.SetTension(_tension);
            }
            return affected;
        }

        /// <summary>
        /// §6.3 귀신 이벤트 목격 (G-6 해결 — 사용자 확정 2026-08-31): 어떤 초자연현상이든 실제로
        /// "본" 플레이어의 정신력을 <see cref="SanitySystemSettings.GhostEventDecrease"/> 만큼 깎는다.
        /// 물리적으로 대상을 찾았는지(<c>affected</c>)와 무관하게, 현상 발생 지점을 볼 수 있었는가만
        /// 본다. 서버는 카메라 피치를 모르므로(<see cref="Sanity.SanityWitnessProp"/> 과 같은 이유)
        /// 플레이어 정면(요) 기준 각도·거리·가림만으로 판정한다.
        /// </summary>
        private void ServerCheckPhenomenonWitnessed(Vector3 origin)
        {
            int playerCount = _sanity.CopyPlayerStates(_players);

            for (int i = 0; i < playerCount; i++)
            {
                SanityNetworkState player = _players[i];
                if (player == null || !player.IsSpawned || !player.HasSanity || player.IsBurrowed)
                    continue;

                Vector3 eye = player.transform.position + Vector3.up * _settings.PhenomenonWitnessEyeHeight;
                Vector3 toOrigin = origin - eye;
                float distance = toOrigin.magnitude;

                bool inCone = GhostVision.IsInsideCone(
                    player.transform.forward,
                    toOrigin,
                    _settings.PhenomenonWitnessAngle,
                    distance,
                    _settings.PhenomenonWitnessDistance);

                if (inCone && HasLineOfSight(eye, origin, transform))
                    player.ServerApplyGhostEventWitnessed();
            }
        }

        /// <summary>귀신 반경(`PhenomenonRadius`) 안의 Idle 가구를 전부 동시에 흔든다(§6.5 #1·#9).</summary>
        private bool ServerShakeNearbyFurniture()
        {
            EnsureSceneCollections();
            ClearShake();

            float radiusSqr = _settings.PhenomenonRadius * _settings.PhenomenonRadius;

            for (int i = 0; i < _sceneFurniture.Length; i++)
            {
                FurnitureGrabTarget furniture = _sceneFurniture[i];
                if (furniture == null || !furniture.IsSpawned
                    || furniture.State != FurnitureState.Idle)
                {
                    continue;
                }

                if ((furniture.transform.position - transform.position).sqrMagnitude > radiusSqr)
                    continue;

                var body = furniture.GetComponent<Rigidbody>();
                if (body == null || body.isKinematic)
                    continue;

                body.WakeUp();
                // 가구마다 축을 따로 잡아 저마다 부르르 떨게 한다. 대부분 수직이라 넘어가지 않는다.
                Vector3 axis = new Vector3(
                    Random.Range(-0.5f, 0.5f),
                    1f,
                    Random.Range(-0.5f, 0.5f)).normalized;
                _shakeTargets.Add(new ShakeTarget(body, axis));
            }

            if (_shakeTargets.Count == 0)
                return false;

            _shakeSign = 1f;
            _shakeElapsed = 0f;
            _shakeRemaining = _settings.ShakeDuration;
            _shakePulseRemaining = 0f;
            return true;
        }

        /// <summary>귀신 반경 안의 작은 소품(§6.5 #2)을 전부 위로 튕겨 올린다. 질량과 무관하게 속도를 직접 준다.</summary>
        private bool ServerDropNearbyProp()
        {
            EnsureSceneCollections();

            float radiusSqr = _settings.PhenomenonRadius * _settings.PhenomenonRadius;
            int count = 0;

            for (int i = 0; i < _sceneFurniture.Length; i++)
            {
                FurnitureGrabTarget furniture = _sceneFurniture[i];
                if (furniture == null || !furniture.IsSpawned
                    || furniture.State != FurnitureState.Idle
                    || !IsSmallProp(furniture))
                {
                    continue;
                }

                if ((furniture.transform.position - transform.position).sqrMagnitude > radiusSqr)
                    continue;

                var body = furniture.GetComponent<Rigidbody>();
                if (body == null || body.isKinematic)
                    continue;

                body.WakeUp();
                Vector3 pop = (Vector3.up
                    + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized * 0.6f)
                    .normalized * _settings.DropSpeed;
                body.linearVelocity = pop;
                body.angularVelocity = Random.onUnitSphere * (_settings.DropSpeed * 2.5f);
                count++;
            }

            return count > 0;
        }

        private void ServerTickShake(float deltaTime)
        {
            if (_shakeRemaining <= 0f)
                return;

            _shakeRemaining -= deltaTime;
            _shakeElapsed += deltaTime;
            _shakePulseRemaining -= deltaTime;

            // 사인파 각속도로 좌우로 크게 비튼다. 왕복이라 알짜 회전은 0에 가깝고, 질량과 무관하게
            // 진폭(ShakeTorque)만큼 확실히 흔들린다.
            float wave = Mathf.Sin(_shakeElapsed * ShakeAngularFrequency) * _settings.ShakeTorque;

            bool shove = _shakePulseRemaining <= 0f;
            if (shove)
            {
                _shakePulseRemaining = ShakePulseInterval;
                _shakeSign = -_shakeSign;
            }

            for (int i = _shakeTargets.Count - 1; i >= 0; i--)
            {
                Rigidbody body = _shakeTargets[i].Body;
                if (body == null || body.isKinematic)
                {
                    _shakeTargets.RemoveAt(i);
                    continue;
                }

                body.angularVelocity = _shakeTargets[i].Axis * wave;
                if (shove)
                {
                    // 바닥에서 달그락 — 좌우로 번갈아 밀고 살짝만 튀어 오른다. 번갈이라 제자리를 지킨다.
                    Vector3 push = new Vector3(
                        _shakeSign,
                        0.12f,
                        _shakeSign * Random.Range(-0.6f, 0.6f));
                    body.AddForce(push * _settings.ShakeShoveSpeed, ForceMode.VelocityChange);
                }
            }

            if (_shakeRemaining <= 0f || _shakeTargets.Count == 0)
                ClearShake();
        }

        private void ClearShake()
        {
            for (int i = 0; i < _shakeTargets.Count; i++)
            {
                Rigidbody body = _shakeTargets[i].Body;
                if (body != null && !body.isKinematic)
                    body.angularVelocity = Vector3.zero;
            }

            _shakeTargets.Clear();
            _shakeRemaining = 0f;
        }

        private bool ServerMoveNearbyDoor()
        {
            EnsureSceneCollections();

            DoorInteractable nearest = null;
            float nearestSqr = _settings.PhenomenonRadius * _settings.PhenomenonRadius;

            for (int i = 0; i < _sceneDoors.Length; i++)
            {
                DoorInteractable door = _sceneDoors[i];
                if (door == null || !door.IsSpawned)
                    continue;

                float sqr = (door.transform.position - transform.position).sqrMagnitude;
                if (sqr <= nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = door;
                }
            }

            if (nearest == null)
                return false;

            nearest.ServerForceToggle();
            return true;
        }

        private void EnsureSceneCollections()
        {
            _sceneFurniture ??= FindObjectsByType<FurnitureGrabTarget>(FindObjectsSortMode.None);
            if (_sceneDoors != null)
                return;

            _sceneDoors = FindObjectsByType<DoorInteractable>(FindObjectsSortMode.None);
            _sceneDoorColliders = new Collider[_sceneDoors.Length][];
            _doorCollisionIgnored = new bool[_sceneDoors.Length];
            for (int i = 0; i < _sceneDoors.Length; i++)
            {
                _sceneDoorColliders[i] = System.Array.FindAll(
                    _sceneDoors[i].GetComponentsInChildren<Collider>(true),
                    collider => !collider.isTrigger);
            }
        }

        /// <summary>
        /// '작은 물건' 기준: Light 무게 등급 + 렌더러 바운즈의 최대 변이 `SmallPropMaxSize` 이하.
        /// 무게 등급만으로는 커피 테이블·TV까지 걸려 "작은 물건"이 안 된다.
        /// </summary>
        private bool IsSmallProp(FurnitureGrabTarget furniture)
        {
            var physics = furniture.GetComponent<FurnitureNetworkPhysics>();
            if (physics == null || physics.Definition == null
                || physics.Definition.WeightClass != FurnitureWeightClass.Light)
            {
                return false;
            }

            return TryGetMaxDimension(furniture.transform, out float size)
                && size <= _settings.SmallPropMaxSize;
        }

        private static bool TryGetMaxDimension(Transform root, out float maxDimension)
        {
            maxDimension = 0f;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return false;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            Vector3 size = bounds.size;
            maxDimension = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            return true;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PlayPhenomenonRpc(GhostPhenomenonKind kind, Vector3 position, int seed)
        {
            if (_phenomenaPlayer != null)
                _phenomenaPlayer.Play(kind, position, seed);
        }

        private void ServerTickAttack(float deltaTime, int playerCount)
        {
            EvaluateBedHide(playerCount, deltaTime);
            EvaluateBurrowExposure(playerCount);
            _repathRemaining -= deltaTime;
            _targetSelectionRemaining -= deltaTime;
            if (_pursuit == Pursuit.Chase && _target != null
                && HidingSpot.Contains(_target.transform.position))
            {
                if (_targetWasVisible)
                {
                    _witnessedHidingPlayer = _target;
                    _lastKnownPosition = _target.transform.position;
                }
                BeginSearch();
            }

            if (_targetSelectionRemaining <= 0f)
            {
                _targetSelectionRemaining = _settings.TargetSelectionInterval;
                SanityNetworkState detected = null;
                bool hasTarget = false;
                if (_witnessedHidingPlayer == null && _witnessedBurrowPlayer == null
                    && _witnessedBedPlayer == null)
                {
                    hasTarget = _settings.EvidenceTrackingEnabled
                        ? TrySelectObservedTarget(playerCount, out detected)
                        : _pursuit == Pursuit.Roam
                            ? TryDetectPlayer(playerCount, out detected)
                            : TrySelectChaseTarget(playerCount, out detected);
                }

                if (hasTarget)
                {
                    _target = detected;
                    _pursuit = Pursuit.Chase;
                    _searchWandering = false;
                    _searchScanRemaining = 0f;
                    _hasPendingImpact = false;
                    _witnessedHidingPlayer = null;
                    ObserveTarget(detected);
                }
                else if (_pursuit == Pursuit.Chase)
                {
                    BeginSearch();
                }
                else if (_settings.ImpactInvestigationEnabled && _hasPendingImpact
                    && _witnessedHidingPlayer == null && _witnessedBurrowPlayer == null
                    && _witnessedBedPlayer == null)
                {
                    if (Time.time - _pendingImpactAt <= _settings.EvidenceMemorySeconds)
                    {
                        _trackingMemory.Clear();
                        _lastKnownPosition = _pendingImpactPosition;
                        BeginSearch();
                    }
                    _hasPendingImpact = false;
                }
            }

            if (_pursuit == Pursuit.Chase && _target != null)
            {
                if (!_settings.EvidenceTrackingEnabled)
                    _lastKnownPosition = _target.transform.position;
                _targetWasVisible = IsVisibleInVisionCone(_target);
            }

            switch (_pursuit)
            {
                case Pursuit.Chase:
                    if (!MoveToward(ChaseDestination(), _settings.ChaseSpeed, deltaTime, allowPartial: true))
                        _repathRemaining = 0f;
                    TryCatch();
                    break;
                case Pursuit.Search:
                    if (_witnessedHidingPlayer == null && _witnessedBurrowPlayer == null
                        && _witnessedBedPlayer == null)
                        _searchRemaining -= deltaTime;
                    if (_witnessedHidingPlayer != null)
                        _lastKnownPosition = _witnessedHidingPlayer.transform.position;
                    else if (_witnessedBurrowPlayer != null)
                        _lastKnownPosition = _witnessedBurrowPlayer.transform.position;
                    else if (_witnessedBedPlayer != null)
                        _lastKnownPosition = _witnessedBedPlayer.transform.position;
                    MoveSearch(deltaTime);
                    TryCatch();
                    ServerTickWitnessedShelter();
                    ServerTickWitnessedBed();
                    if (_searchRemaining <= 0f && _witnessedHidingPlayer == null
                        && _witnessedBurrowPlayer == null && _witnessedBedPlayer == null)
                        ResetPursuit();
                    break;
                default:
                    ServerTickRoam(deltaTime, _settings.ChaseSpeed, attacking: true);
                    break;
            }

            if (_repathRemaining <= 0f)
            {
                _repathRemaining = _settings.RepathInterval;
                TryOpenBlockingDoor();
            }
        }

        private void BeginSearch()
        {
            _target = _witnessedBedPlayer;
            _pursuit = Pursuit.Search;
            _searchRemaining = _settings.SearchDuration;
            _searchWandering = false;
            _searchScanRemaining = 0f;
            if (_searchMemory != null)
                _searchMemory.Clear();
        }

        private void ObserveTarget(SanityNetworkState player)
        {
            _lastKnownPosition = player.transform.position;
            _trackingMemory.Observe(player.OwnerClientId, _lastKnownPosition,
                Time.time, _settings.ObservedSpeedLimit);
        }

        private bool TrySelectObservedTarget(int playerCount, out SanityNetworkState selected)
        {
            selected = CanChasePlayer(_target) && CanSensePlayer(_target) ? _target : null;
            bool retainingCurrent = selected != null;
            float bestDistance = retainingCurrent
                ? Vector3.Distance(transform.position, selected.transform.position) : float.MaxValue;
            for (int i = 0; i < playerCount; i++)
            {
                SanityNetworkState candidate = _players[i];
                if (candidate == selected || !CanChasePlayer(candidate) || !CanSensePlayer(candidate))
                    continue;
                float distance = Vector3.Distance(transform.position, candidate.transform.position);
                if (selected == null || (retainingCurrent
                        ? ShouldSwitchTarget(bestDistance, distance, _settings.TargetSwitchDistance)
                        : distance < bestDistance))
                {
                    selected = candidate;
                    bestDistance = distance;
                }
            }
            return selected != null;
        }

        private bool CanSensePlayer(SanityNetworkState player)
        {
            Vector3 eye = transform.position + Vector3.up * _settings.GhostEyeHeight;
            Vector3 center = player.transform.position + Vector3.up * _settings.TargetCenterHeight;
            float distance = Vector3.Distance(eye, center);
            return IsAudible(player, distance)
                || ((_settings.NearDetectRadius > 0f && distance <= _settings.NearDetectRadius)
                    && HasLineOfSight(eye, center, player.transform))
                || IsVisibleInVisionCone(player);
        }

        private Vector3 ChaseDestination()
        {
            if (!_settings.PredictiveChaseEnabled || !_trackingMemory.HasObservation)
                return _lastKnownPosition;
            Vector3 predicted = _trackingMemory.Predict(Time.time, _settings.EvidenceMemorySeconds,
                _settings.PredictionSeconds, _settings.PredictionMaxDistance);
            if (!IsInsideHouseBounds(predicted)
                || !NavMesh.SamplePosition(predicted, out NavMeshHit hit, NavSampleRadius, NavMesh.AllAreas)
                || Mathf.Abs(hit.position.y - _lastKnownPosition.y) > ArrivalVerticalTolerance
                || Vector3.Distance(hit.position, _lastKnownPosition) > _settings.PredictionMaxDistance
                || !TrySampleNavMesh(_lastKnownPosition, out Vector3 origin)
                || NavMesh.Raycast(origin, hit.position, out _, NavMesh.AllAreas))
                return _lastKnownPosition;
            return hit.position;
        }

        /// <summary>
        /// §9.3 수색 이동. 목격한 은신·굴착은 그 플레이어에게 곧장 간다. 아니면 마지막 위치로 간 뒤
        /// 그 주변(`SearchWanderRadius`)을 돌아다니며 시야·소리 재탐지 기회를 만든다.
        /// </summary>
        private void MoveSearch(float deltaTime)
        {
            if (_witnessedHidingPlayer != null || _witnessedBurrowPlayer != null
                || _witnessedBedPlayer != null)
            {
                MoveToward(_lastKnownPosition, _settings.ChaseSpeed, deltaTime, allowPartial: true);
                return;
            }

            if (_settings.UtilitySearchEnabled && _searchScanRemaining > 0f)
            {
                _searchScanRemaining -= deltaTime;
                LookAround(deltaTime);
                return;
            }
            Vector3 destination = _searchWandering ? _searchDestination : _lastKnownPosition;
            bool arrived = HasArrived(destination);
            if (arrived || !MoveToward(destination, _settings.ChaseSpeed, deltaTime, allowPartial: true))
            {
                if (_settings.UtilitySearchEnabled && _searchMemory != null)
                {
                    _searchMemory.MarkVisited(destination, Time.time);
                    if (arrived)
                    {
                        _searchScanRemaining = _settings.SearchScanSeconds;
                        _lookYaw = transform.eulerAngles.y + 90f;
                    }
                }
                PickSearchWanderPoint();
            }
        }

        private void PickSearchWanderPoint()
        {
            if (_settings.UtilitySearchEnabled)
            {
                PickUtilitySearchPoint();
                return;
            }
            _searchWandering = true;
            for (int attempt = 0; attempt < SearchWanderPickAttempts; attempt++)
            {
                Vector2 offset = Random.insideUnitCircle * _settings.SearchWanderRadius;
                Vector3 probe = _lastKnownPosition + new Vector3(offset.x, 0f, offset.y);
                if (NavMesh.SamplePosition(probe, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)
                    && Mathf.Abs(hit.position.y - _lastKnownPosition.y) <= ArrivalVerticalTolerance
                    && !HasArrived(hit.position)
                    && TryMeasurePath(hit.position, out _))
                {
                    _searchDestination = hit.position;
                    return;
                }
            }

            _searchDestination = transform.position;
        }

        private void PickUtilitySearchPoint()
        {
            _searchWandering = true;
            float bestScore = float.MinValue;
            _searchDestination = transform.position;
            for (int i = 0; i < _settings.SearchCandidateCount; i++)
            {
                Vector2 offset = Random.insideUnitCircle * _settings.SearchWanderRadius;
                EvaluateSearchCandidate(_lastKnownPosition + new Vector3(offset.x, 0f, offset.y),
                    ref bestScore);
            }

            EnsureSceneCollections();
            Vector3 nearestDoor = default;
            float nearestSqr = _settings.SearchWanderRadius * _settings.SearchWanderRadius;
            bool foundDoor = false;
            for (int i = 0; i < _sceneDoors.Length; i++)
            {
                DoorInteractable door = _sceneDoors[i];
                if (door == null || !door.IsSpawned
                    || Mathf.Abs(door.transform.position.y - _lastKnownPosition.y) > ArrivalVerticalTolerance)
                    continue;
                float sqr = (door.transform.position - _lastKnownPosition).sqrMagnitude;
                if (sqr < nearestSqr && !HasArrived(door.transform.position))
                {
                    nearestSqr = sqr;
                    nearestDoor = door.transform.position;
                    foundDoor = true;
                }
            }
            if (foundDoor)
                EvaluateSearchCandidate(nearestDoor, ref bestScore);
        }

        private void EvaluateSearchCandidate(Vector3 candidate, ref float bestScore)
        {
            if (!IsInsideHouseBounds(candidate)
                || !NavMesh.SamplePosition(candidate, out NavMeshHit hit, NavSampleRadius, NavMesh.AllAreas)
                || !IsInsideHouseBounds(hit.position)
                || Mathf.Abs(hit.position.y - _lastKnownPosition.y) > ArrivalVerticalTolerance
                || HasArrived(hit.position) || !TryMeasurePath(hit.position, out float length))
                return;
            float novelty = _searchMemory != null
                ? _searchMemory.Staleness(hit.position, Time.time) / Mathf.Max(0.1f, _settings.SearchDuration)
                : 1f;
            float confidence = _trackingMemory.Confidence(Time.time, _settings.EvidenceMemorySeconds);
            float score = GhostTrackingMemory.SearchScore(hit.position, _lastKnownPosition,
                _trackingMemory.Velocity, confidence, novelty, length, _settings);
            if (score <= bestScore)
                return;
            bestScore = score;
            _searchDestination = hit.position;
        }

        /// <summary>목격한 일반 은신처와 굴착 은신을 확률 없이 검사한다.</summary>
        private void ServerTickWitnessedShelter()
        {
            bool hidingSpot = _witnessedHidingPlayer != null;
            SanityNetworkState player = hidingSpot
                ? _witnessedHidingPlayer
                : _witnessedBurrowPlayer;
            if (player == null || !player.IsSpawned || !player.HasSanity
                || !IsInsideHouseBounds(player.transform.position)
                || DrillCarSafeZone.Contains(player.transform.position)
                || (hidingSpot
                    ? !HidingSpot.Contains(player.transform.position)
                    : !player.IsBurrowed))
            {
                if (hidingSpot)
                    _witnessedHidingPlayer = null;
                else
                    _witnessedBurrowPlayer = null;
                return;
            }

            Vector3 offset = player.transform.position - transform.position;
            if (_settings.EvidenceTrackingEnabled && Mathf.Abs(offset.y) > ArrivalVerticalTolerance)
                return;
            offset.y = 0f;
            if (offset.magnitude <= _settings.CatchRadius)
            {
                player.ServerMarkDead();
                if (hidingSpot)
                    _witnessedHidingPlayer = null;
                else
                    _witnessedBurrowPlayer = null;
            }
        }

        /// <summary>
        /// 집 내부 배회(§9.1). 활동 중에는 목적지에 닿으면 잠깐 멈춰 두리번거리고, 어택 중(<paramref name="attacking"/>)
        /// 에는 멈추지 않고 곧장 다음 곳으로 간다. 문은 어택 중에만 열 수 있으므로(§9.4) 활동 중에는 닫힌 문 너머를
        /// 목적지로 고르지 않는다.
        /// </summary>
        private void ServerTickRoam(float deltaTime, float speed, bool attacking)
        {
            if (attacking)
                _lookAroundRemaining = 0f;

            if (_lookAroundRemaining > 0f)
            {
                _lookAroundRemaining -= deltaTime;
                LookAround(deltaTime);
                return;
            }

            bool arrived = HasArrived(_roamDestination);
            if (arrived || Time.time >= _roamGiveUpAt)
            {
                PickRoamDestination(speed, avoidClosedDoors: !attacking);
                if (arrived && !attacking && _settings.RoamLookAroundMax > 0f)
                {
                    _lookAroundRemaining = Random.Range(_settings.RoamLookAroundMin, _settings.RoamLookAroundMax);
                    _lookYaw = transform.eulerAngles.y + Random.Range(60f, 150f) * (Random.value < 0.5f ? -1f : 1f);
                    // 포기 시간은 걷는 시간 기준이다 — 서서 두리번거리는 시간만큼 미룬다.
                    _roamGiveUpAt += _lookAroundRemaining;
                    ResetStuckCheck();
                    return;
                }
            }

            // 경로가 끊겼거나(닫힌 문 등) 제자리에 걸렸으면 다음 틱에 다른 목적지를 고른다.
            if (!MoveToward(_roamDestination, speed, deltaTime, allowPartial: false))
                _roamGiveUpAt = 0f;
        }

        /// <summary>제자리에서 좌우로 고개를 돌린다. 한쪽을 다 보면 반대쪽으로 새 각도를 잡는다.</summary>
        private void LookAround(float deltaTime)
        {
            Quaternion look = Quaternion.Euler(0f, _lookYaw, 0f);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, look, LookAroundTurnDegrees * deltaTime);
            if (Quaternion.Angle(transform.rotation, look) < 1f)
                _lookYaw += Random.Range(70f, 140f) * (Random.value < 0.5f ? -1f : 1f);

            _controller.Move(Vector3.down * _settings.Gravity * deltaTime);
        }

        /// <summary>
        /// NavMesh 삼각형을 면적 비례로 뽑은 후보 중 <see cref="GhostRoamMemory"/> 점수(오래 안 간 곳 − 걸리는 시간)가
        /// 가장 높은 곳을 배회 목적지로 정한다. 계단으로 이어진 모든 층이 후보이고, 완전한 경로가 있는 곳만 고른다
        /// (지붕 위 같은 끊긴 섬 제외). `RoamMinDistance` 이상 떨어진 곳을 우선한다.
        /// </summary>
        private void PickRoamDestination(float speed, bool avoidClosedDoors)
        {
            float minSqr = _settings.RoamMinDistance * _settings.RoamMinDistance;
            int wanted = Mathf.Max(1, _settings.RoamCandidateCount);
            int found = 0;
            bool hasBest = false;
            bool bestIsFar = false;
            Vector3 best = default;
            float bestLength = 0f;
            float bestScore = float.MinValue;

            for (int attempt = 0; attempt < RoamPickAttempts && found < wanted; attempt++)
            {
                if (!TryRandomNavPoint(out Vector3 candidate))
                    break;
                if (!TryMeasurePath(candidate, out float length, avoidClosedDoors))
                    continue;

                bool far = (candidate - transform.position).sqrMagnitude >= minSqr;
                if (far)
                    found++;
                // 가까운 후보는 먼 후보가 하나도 없을 때만 쓴다.
                if (bestIsFar && !far)
                    continue;

                float score = _roamMemory.Score(candidate, length, speed, Time.time)
                    + Random.value * RoamScoreJitterSeconds;
                if (!hasBest || (far && !bestIsFar) || score > bestScore)
                {
                    hasBest = true;
                    bestIsFar = far;
                    best = candidate;
                    bestLength = length;
                    bestScore = score;
                }
            }

            if (hasBest)
            {
                SetRoamDestination(best, bestLength, speed);
                return;
            }

            _roamDestination = transform.position;
            _roamGiveUpAt = Time.time + 1f;
        }

        private void SetRoamDestination(Vector3 destination, float pathLength, float speed)
        {
            _roamDestination = destination;
            _roamGiveUpAt = Time.time + RoamGiveUpSeconds(pathLength, speed);
        }

        /// <summary>경로 길이를 걸어갈 시간에 여유를 더한 배회 포기 시간. 층을 건너는 긴 경로도 끝까지 간다.</summary>
        internal static float RoamGiveUpSeconds(float pathLength, float speed)
        {
            if (speed <= 0.01f)
                return MinRoamGiveUpSeconds;
            return Mathf.Max(MinRoamGiveUpSeconds, pathLength / speed * 1.5f + 2f);
        }

        /// <summary>평면 거리가 도착 반경 안이고 같은 층(높이 차 <see cref="ArrivalVerticalTolerance"/> 이하)인가.</summary>
        internal static bool HasArrived(Vector3 position, Vector3 destination, float reachDistance)
        {
            Vector3 flat = destination - position;
            if (Mathf.Abs(flat.y) > ArrivalVerticalTolerance)
                return false;
            flat.y = 0f;
            return flat.sqrMagnitude <= reachDistance * reachDistance;
        }

        private bool HasArrived(Vector3 destination)
        {
            return HasArrived(transform.position, destination, _settings.ReachDistance);
        }

        private bool TryRandomNavPoint(out Vector3 point)
        {
            point = default;
            if (_navCumulativeArea == null || _navCumulativeArea.Length == 0)
                return false;

            int triangle = System.Array.BinarySearch(_navCumulativeArea, Random.value * _navTotalArea);
            if (triangle < 0)
                triangle = ~triangle;
            triangle = Mathf.Min(triangle, _navCumulativeArea.Length - 1);

            Vector3 a = _navVertices[_navIndices[triangle * 3]];
            Vector3 b = _navVertices[_navIndices[triangle * 3 + 1]];
            Vector3 c = _navVertices[_navIndices[triangle * 3 + 2]];
            float u = Random.value;
            float v = Random.value;
            if (u + v > 1f)
            {
                u = 1f - u;
                v = 1f - v;
            }

            point = a + (b - a) * u + (c - a) * v;
            return true;
        }

        /// <summary>
        /// 현재 위치에서 목적지까지 완전한 경로가 있으면 그 길이를 돌려준다. 현재 경로는 건드리지 않는다.
        /// NavMesh 는 문을 굽지 않으므로 <paramref name="avoidClosedDoors"/> 면 닫힌 문을 지나는 경로를 따로 거른다.
        /// </summary>
        private bool TryMeasurePath(Vector3 destination, out float length, bool avoidClosedDoors = false)
        {
            length = 0f;
            if (!TrySampleNavMesh(transform.position, out Vector3 from)
                || !TrySampleNavMesh(destination, out Vector3 to)
                || !NavMesh.CalculatePath(from, to, NavMesh.AllAreas, _navPath)
                || _navPath.status != NavMeshPathStatus.PathComplete)
            {
                return false;
            }

            int count = _navPath.GetCornersNonAlloc(_probeCorners);
            if (avoidClosedDoors && PathCrossesClosedDoor(_probeCorners, count))
                return false;

            for (int i = 1; i < count; i++)
                length += Vector3.Distance(_probeCorners[i - 1], _probeCorners[i]);
            return true;
        }

        /// <summary>경로 코너를 잇는 선분(허리 높이)이 닫힌 문짝의 충돌체를 지나는가.</summary>
        private bool PathCrossesClosedDoor(Vector3[] corners, int count)
        {
            EnsureSceneCollections();
            for (int d = 0; d < _sceneDoors.Length; d++)
            {
                DoorInteractable door = _sceneDoors[d];
                if (door == null || !door.IsSpawned || door.IsOpen)
                    continue;

                Collider[] colliders = _sceneDoorColliders[d];
                for (int i = 1; i < count; i++)
                {
                    Vector3 a = corners[i - 1] + Vector3.up * DoorRayHeight;
                    Vector3 segment = corners[i] + Vector3.up * DoorRayHeight - a;
                    float length = segment.magnitude;
                    if (length < 1e-3f)
                        continue;

                    var ray = new Ray(a, segment / length);
                    for (int c = 0; c < colliders.Length; c++)
                    {
                        if (colliders[c] != null
                            && colliders[c].bounds.IntersectRay(ray, out float hit)
                            && hit <= length)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 열린 문짝은 귀신을 막지 않는다 — NavMesh 가 문을 빼고 구워서 경로가 열린 문짝을 스쳐 지나가면 캡슐이
        /// 걸려 제자리에 멈췄다. 닫힌 문은 계속 막는다(어택 중에는 <see cref="TryOpenBlockingDoor"/> 가 연다).
        /// </summary>
        private void RefreshDoorCollisions(float deltaTime)
        {
            _doorCollisionRefreshRemaining -= deltaTime;
            if (_doorCollisionRefreshRemaining > 0f)
                return;

            _doorCollisionRefreshRemaining = DoorCollisionRefreshInterval;
            EnsureSceneCollections();
            for (int d = 0; d < _sceneDoors.Length; d++)
            {
                DoorInteractable door = _sceneDoors[d];
                bool ignore = door != null && door.IsSpawned && door.IsOpen;
                if (_doorCollisionIgnored[d] == ignore)
                    continue;

                _doorCollisionIgnored[d] = ignore;
                Collider[] colliders = _sceneDoorColliders[d];
                for (int c = 0; c < colliders.Length; c++)
                {
                    if (colliders[c] != null)
                        Physics.IgnoreCollision(_controller, colliders[c], ignore);
                }
            }
        }

        private static bool TrySampleNavMesh(Vector3 position, out Vector3 onMesh)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, NavSampleRadius, NavMesh.AllAreas)
                || NavMesh.SamplePosition(position, out hit, NavSampleFallbackRadius, NavMesh.AllAreas))
            {
                onMesh = hit.position;
                return true;
            }

            onMesh = position;
            return false;
        }

        /// <summary>
        /// NavMesh 경로의 코너를 따라 이동한다. 경로가 없거나 1초 동안 거의 못 움직였으면(닫힌 문·걸림)
        /// false 를 돌려주고 다음 틱에 경로를 다시 만든다.
        /// </summary>
        private bool MoveToward(Vector3 target, float speed, float deltaTime, bool allowPartial)
        {
            if (deltaTime <= 0f)
                return true;

            target = ClampToHouseBounds(target, _roamCenter, _roamExtents);
            _pathRebuildRemaining -= deltaTime;
            if (_pathRebuildRemaining <= 0f
                || (target - _pathDestination).sqrMagnitude >
                    _settings.ReachDistance * _settings.ReachDistance)
            {
                RebuildPath(target, allowPartial);
            }

            if (!_hasPath)
            {
                _controller.Move(Vector3.down * _settings.Gravity * deltaTime);
                ResetStuckCheck();
                return false;
            }

            Vector3 waypoint = _pathCornerIndex < _pathCornerCount
                ? _pathCorners[_pathCornerIndex]
                : target;
            Vector3 flat = waypoint - transform.position;
            flat.y = 0f;
            float distance = flat.magnitude;

            while (_pathCornerIndex < _pathCornerCount - 1
                && HasArrived(waypoint))
            {
                _pathCornerIndex++;
                waypoint = _pathCorners[_pathCornerIndex];
                flat = waypoint - transform.position;
                flat.y = 0f;
                distance = flat.magnitude;
            }

            Vector3 direction = distance > 1e-3f ? flat / distance : Vector3.zero;
            if (direction != Vector3.zero)
            {
                Quaternion look = Quaternion.LookRotation(direction, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    look,
                    TurnSpeedDegrees * deltaTime);
            }

            float step = Mathf.Min(speed, distance / deltaTime);
            Vector3 move = direction * step;
            move.y = -_settings.Gravity;
            _controller.Move(move * deltaTime);
            ClampToHouseBounds();

            return !IsStuck(target, deltaTime);
        }

        private bool IsStuck(Vector3 target, float deltaTime)
        {
            _stuckCheckRemaining -= deltaTime;
            if (_stuckCheckRemaining > 0f)
                return false;

            bool stuck = (transform.position - _stuckCheckPosition).sqrMagnitude
                    < StuckMinProgress * StuckMinProgress
                && !HasArrived(target);
            ResetStuckCheck();
            if (stuck)
                _pathRebuildRemaining = 0f;
            return stuck;
        }

        private void ResetStuckCheck()
        {
            _stuckCheckRemaining = StuckCheckInterval;
            _stuckCheckPosition = transform.position;
        }

        private void EnsureNavMesh(Transform house)
        {
            RemoveNavMesh();
            if (house == null)
            {
                Debug.LogError("[GhostPrototype] 집 루트가 없어 경로를 만들 수 없습니다.", this);
                return;
            }

            int includedLayers = ~0;
            if (GameLayers.Player >= 0)
                includedLayers &= ~(1 << GameLayers.Player);
            if (GameLayers.Furniture >= 0)
                includedLayers &= ~(1 << GameLayers.Furniture);
            if (GameLayers.GhostPrototype >= 0)
                includedLayers &= ~(1 << GameLayers.GhostPrototype);

            _navMeshData = BuildHouseNavMesh(
                house,
                includedLayers,
                _settings.NavAgentRadius,
                _controller.height,
                Mathf.Max(0.3f, _controller.stepOffset));
            if (_navMeshData == null)
            {
                Debug.LogError("[GhostPrototype] 집 루트에서 NavMesh 를 만들 충돌체를 찾지 못했습니다.", this);
                return;
            }

            _navMeshInstance = NavMesh.AddNavMeshData(_navMeshData);
            CacheRoamTriangles();

            if (!NavMesh.SamplePosition(transform.position, out _, NavSampleFallbackRadius, NavMesh.AllAreas))
                Debug.LogError("[GhostPrototype] 집 내부 NavMesh 생성 후에도 스폰 위치를 찾지 못했습니다.", this);
        }

        /// <summary>
        /// 집 루트 자식의 물리 충돌체로 귀신 전용 NavMesh 를 굽는다. 에이전트 종류는 기본(0)을 쓰되
        /// 반경·높이·턱 높이만 귀신에 맞춘다 — 기본 Humanoid 반경 0.5 로는 1.0m 문틀이 막힌다.
        /// 트리거(은신처·세이프 존)와 문(열고 닫혀 움직임)은 장애물에서 뺀다. 닫힌 문은 이동 중에 연다.
        /// </summary>
        internal static NavMeshData BuildHouseNavMesh(
            Transform root, int layerMask, float agentRadius, float agentHeight, float agentClimb)
        {
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(
                root,
                layerMask,
                NavMeshCollectGeometry.PhysicsColliders,
                0,
                new List<NavMeshBuildMarkup>(),
                sources);

            for (int i = sources.Count - 1; i >= 0; i--)
            {
                if (sources[i].component is Collider collider
                    && (collider.isTrigger || collider.GetComponentInParent<DoorInteractable>() != null))
                {
                    sources.RemoveAt(i);
                }
            }

            bool hasBounds = false;
            Bounds bounds = default;
            foreach (Collider collider in root.GetComponentsInChildren<Collider>())
            {
                if (collider.isTrigger || (layerMask & (1 << collider.gameObject.layer)) == 0)
                    continue;
                if (hasBounds)
                {
                    bounds.Encapsulate(collider.bounds);
                }
                else
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
            }

            if (!hasBounds || sources.Count == 0)
                return null;

            bounds.Expand(1f);
            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = agentRadius;
            settings.agentHeight = agentHeight;
            settings.agentClimb = agentClimb;
            return NavMeshBuilder.BuildNavMeshData(
                settings, sources, bounds, Vector3.zero, Quaternion.identity);
        }

        /// <summary>집 X/Z 경계 안의 NavMesh 삼각형과 누적 면적을 캐시한다(배회 목적지 추첨용).</summary>
        private void CacheRoamTriangles()
        {
            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            Vector3[] vertices = triangulation.vertices;
            int[] indices = triangulation.indices;
            var kept = new List<int>(indices.Length);
            var cumulative = new List<float>(indices.Length / 3);
            float total = 0f;

            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                Vector3 a = vertices[indices[i]];
                Vector3 b = vertices[indices[i + 1]];
                Vector3 c = vertices[indices[i + 2]];
                if (!IsInsideHouseBounds((a + b + c) / 3f))
                    continue;

                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                if (area <= 1e-4f)
                    continue;

                total += area;
                kept.Add(indices[i]);
                kept.Add(indices[i + 1]);
                kept.Add(indices[i + 2]);
                cumulative.Add(total);
            }

            _navVertices = vertices;
            _navIndices = kept.ToArray();
            _navCumulativeArea = cumulative.ToArray();
            _navTotalArea = total;
        }

        private void RemoveNavMesh()
        {
            if (_navMeshInstance.valid)
                _navMeshInstance.Remove();
            _navMeshInstance = default;
            if (_navMeshData != null)
            {
                Destroy(_navMeshData);
                _navMeshData = null;
            }

            _navVertices = null;
            _navIndices = null;
            _navCumulativeArea = null;
            _navTotalArea = 0f;
            _hasPath = false;
        }

        private bool RebuildPath(Vector3 target, bool allowPartial)
        {
            _pathRebuildRemaining = _settings.RepathInterval;
            _pathDestination = target;
            _hasPath = false;

            if (!TrySampleNavMesh(transform.position, out Vector3 from)
                || !TrySampleNavMesh(target, out Vector3 to)
                || !NavMesh.CalculatePath(from, to, NavMesh.AllAreas, _navPath)
                || _navPath.status == NavMeshPathStatus.PathInvalid
                || (!allowPartial && _navPath.status != NavMeshPathStatus.PathComplete))
            {
                if (!_navigationWarningLogged)
                {
                    Debug.LogWarning(
                        $"[GhostPrototype] {target} 까지 NavMesh 경로가 없습니다. 다른 목적지·경로를 찾습니다.",
                        this);
                    _navigationWarningLogged = true;
                }
                return false;
            }

            _pathCornerCount = _navPath.GetCornersNonAlloc(_pathCorners);
            _pathCornerIndex = _pathCornerCount > 1 ? 1 : 0;
            _hasPath = _pathCornerCount > 0;
            _navigationWarningLogged = false;
            return _hasPath;
        }

        private bool TryDetectPlayer(int playerCount, out SanityNetworkState detected)
        {
            detected = null;
            Vector3 eye = transform.position + Vector3.up * _settings.GhostEyeHeight;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < playerCount; i++)
            {
                SanityNetworkState player = _players[i];
                if (!CanChasePlayer(player))
                    continue;

                Vector3 center = player.transform.position + Vector3.up * _settings.TargetCenterHeight;
                Vector3 toTarget = center - eye;
                float distance = toTarget.magnitude;
                if (distance >= bestDistance)
                    continue;

                if (IsAudible(player, distance))
                {
                    detected = player;
                    bestDistance = distance;
                    continue;
                }

                bool nearby = _settings.NearDetectRadius > 0f
                    && distance <= _settings.NearDetectRadius;
                bool inCone = GhostVision.IsInsideCone(
                    transform.forward,
                    toTarget,
                    _machine != null && _machine.IsHighRiskAttack
                        ? _settings.HighRiskVisionAngle
                        : _settings.VisionAngle,
                    distance,
                    _machine != null && _machine.IsHighRiskAttack
                        ? _settings.HighRiskVisionDistance
                        : _settings.VisionDistance);

                if ((nearby || inCone) && HasLineOfSight(eye, center, player.transform))
                {
                    detected = player;
                    bestDistance = distance;
                }
            }

            return detected != null;
        }

        private bool TrySelectChaseTarget(int playerCount, out SanityNetworkState selected)
        {
            selected = CanChasePlayer(_target) ? _target : null;
            bool retainingCurrent = selected != null;
            float bestDistance = selected != null
                ? Vector3.Distance(transform.position, selected.transform.position)
                : float.MaxValue;

            for (int i = 0; i < playerCount; i++)
            {
                SanityNetworkState candidate = _players[i];
                if (candidate == selected || !CanChasePlayer(candidate))
                    continue;

                float distance = Vector3.Distance(transform.position, candidate.transform.position);
                if (selected == null || (retainingCurrent
                        ? ShouldSwitchTarget(bestDistance, distance, _settings.TargetSwitchDistance)
                        : distance < bestDistance))
                {
                    selected = candidate;
                    bestDistance = distance;
                }
            }

            return selected != null;
        }

        internal static bool ShouldSwitchTarget(float currentDistance, float candidateDistance, float margin)
        {
            return candidateDistance + margin <= currentDistance;
        }

        private bool CanChasePlayer(SanityNetworkState player)
        {
            return player != null && player.IsSpawned && player.HasSanity
                && !player.IsBurrowed
                && !IsBedHidden(player)
                && IsInsideHouseBounds(player.transform.position)
                && !DrillCarSafeZone.Contains(player.transform.position)
                && !HidingSpot.Contains(player.transform.position);
        }

        /// <summary>
        /// 어택 틱마다 플레이어별 '침대 밑 은신' 성립 여부를 갱신한다(§9.5, 사용자 확정 2026-09-03).
        /// 이전 틱의 추격 상태(<see cref="_pursuit"/>·<see cref="_target"/>)를 보고 판정하므로
        /// <see cref="TryDetectPlayer"/> 보다 먼저 부른다.
        /// </summary>
        private void EvaluateBedHide(int playerCount, float deltaTime)
        {
            for (int i = 0; i < playerCount; i++)
            {
                SanityNetworkState player = _players[i];
                if (player == null || !player.IsSpawned)
                    continue;

                ulong id = player.OwnerClientId;
                if (!_bedHide.TryGetValue(id, out BedHideEvaluator evaluator))
                {
                    evaluator = new BedHideEvaluator();
                    _bedHide[id] = evaluator;
                }

                if (!player.HasSanity)
                {
                    evaluator.Reset();
                    continue;
                }

                bool eligible = player.IsProne
                    && BedHideZone.Contains(player.transform.position);

                // 들어가는 걸 귀신이 봤다 = 지금도 이 플레이어를 쫓거나 마지막 위치로 수색 중이다.
                // 놓쳐서 배회로 돌아가면(_pursuit == Roam) 그제서야 성립할 수 있다.
                bool chased = _pursuit != Pursuit.Roam
                    && (_target == player || _witnessedBedPlayer == player);
                bool visible = eligible && !chased && IsVisibleInVisionCone(player);
                if (_settings.EvidenceTrackingEnabled && eligible && !evaluator.Granted
                    && _witnessedBedPlayer == null && ((chased && _targetWasVisible) || visible))
                {
                    _witnessedBedPlayer = player;
                    _lastKnownPosition = player.transform.position;
                    BeginSearch();
                }
                if (_witnessedBedPlayer == player && (!eligible || !player.HasSanity))
                    _witnessedBedPlayer = null;

                evaluator.Tick(deltaTime, eligible, chased, visible, _settings.BedHideConcealSeconds);
            }
        }

        /// <summary>
        /// 어택 틱마다 감지된 상태에서 굴착했는지 기록하고, 목격한 구멍을 별도 수색 대상으로 둔다.
        /// 굴착 중에는 일반 추격 타깃 후보에서 제외한다.
        /// </summary>
        private void EvaluateBurrowExposure(int playerCount)
        {
            for (int i = 0; i < playerCount; i++)
            {
                SanityNetworkState player = _players[i];
                if (player == null || !player.IsSpawned)
                    continue;

                ulong id = player.OwnerClientId;
                if (!_burrowExposure.TryGetValue(id, out BurrowExposureTracker tracker))
                {
                    tracker = new BurrowExposureTracker();
                    _burrowExposure[id] = tracker;
                }

                if (!player.HasSanity)
                {
                    tracker.Reset();
                    continue;
                }

                // 침대 밑과 같은 기준의 "귀신이 봤다" — 쫓는 중이거나 마지막 위치를 수색 중이다.
                bool chased = _pursuit != Pursuit.Roam && _target == player;
                tracker.Tick(player.IsBurrowed, chased);
                if (tracker.Exposed && _witnessedBurrowPlayer == null
                    && IsInsideHouseBounds(player.transform.position)
                    && !DrillCarSafeZone.Contains(player.transform.position))
                {
                    _witnessedBurrowPlayer = player;
                    _lastKnownPosition = player.transform.position;
                    _target = null;
                    _targetWasVisible = false;
                    _pursuit = Pursuit.Search;
                    _searchRemaining = _settings.SearchDuration;
                }
            }
        }

        /// <summary>침대 밑 은신이 성립해 귀신의 탐지·잡힘·수색 훔쳐보기에서 완전히 빠지는가.</summary>
        private void ServerTickWitnessedBed()
        {
            SanityNetworkState player = _witnessedBedPlayer;
            if (player == null)
                return;
            if (!player.IsSpawned || !player.HasSanity || !player.IsProne
                || !BedHideZone.Contains(player.transform.position)
                || !IsInsideHouseBounds(player.transform.position)
                || DrillCarSafeZone.Contains(player.transform.position))
            {
                _witnessedBedPlayer = null;
                return;
            }
            Vector3 delta = player.transform.position - transform.position;
            if (Mathf.Abs(delta.y) <= ArrivalVerticalTolerance
                && new Vector2(delta.x, delta.z).magnitude <= _settings.CatchRadius)
            {
                player.ServerMarkDead();
                _witnessedBedPlayer = null;
            }
        }

        private bool IsBedHidden(SanityNetworkState player)
        {
            return player != null
                && _bedHide.TryGetValue(player.OwnerClientId, out BedHideEvaluator evaluator)
                && evaluator.Granted;
        }

        /// <summary>귀신 원뿔 시야 + 시야선에만 걸리는지(근거리·소리 무시). 침대 밑 은신 판정 전용.</summary>
        private bool IsVisibleInVisionCone(SanityNetworkState player)
        {
            Vector3 eye = transform.position + Vector3.up * _settings.GhostEyeHeight;
            Vector3 center = player.transform.position + Vector3.up * _settings.TargetCenterHeight;
            Vector3 toTarget = center - eye;
            float distance = toTarget.magnitude;

            bool inCone = GhostVision.IsInsideCone(
                transform.forward,
                toTarget,
                _machine != null && _machine.IsHighRiskAttack
                    ? _settings.HighRiskVisionAngle
                    : _settings.VisionAngle,
                distance,
                _machine != null && _machine.IsHighRiskAttack
                    ? _settings.HighRiskVisionDistance
                    : _settings.VisionDistance);

            return inCone && HasLineOfSight(eye, center, player.transform);
        }

        private bool IsAudible(SanityNetworkState player, float distance)
        {
            // 기획서 §8.3: 웅크려 이동하면 속력과 무관하게 소리 탐지에서 제외한다.
            // 엎드려 기어서 이동하는 것도 소리를 내지 않는 것으로 친다(P1 확장, player-controller.md).
            if (player.IsCrouching
                || player.IsProne
                || !_playerSpeeds.TryGetValue(player.OwnerClientId, out float speed))
            {
                return false;
            }

            float radius = speed >= _settings.RunSpeedThreshold
                ? _settings.RunHearingRadius
                : speed >= _settings.WalkSpeedThreshold
                    ? _settings.WalkHearingRadius
                    : 0f;

            return radius > 0f && distance <= radius;
        }

        private bool HasLineOfSight(Vector3 from, Vector3 to, Transform player)
        {
            if (!Physics.Linecast(
                    from,
                    to,
                    out RaycastHit hit,
                    GameLayers.NonGhostPrototypeRaycastMask,
                    QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            return hit.transform == player || hit.transform.IsChildOf(player);
        }

        private void UpdatePlayerSpeeds(int playerCount, float deltaTime)
        {
            for (int i = 0; i < playerCount; i++)
            {
                SanityNetworkState player = _players[i];
                if (player == null || !player.IsSpawned)
                    continue;

                ulong id = player.OwnerClientId;
                Vector3 position = player.transform.position;

                if (deltaTime > 0f && _previousPlayerPositions.TryGetValue(id, out Vector3 previous))
                {
                    Vector3 delta = position - previous;
                    delta.y = 0f;
                    float instantaneous = delta.magnitude / deltaTime;
                    float smoothed = _playerSpeeds.TryGetValue(id, out float last)
                        ? Mathf.Lerp(last, instantaneous, 0.5f)
                        : instantaneous;
                    _playerSpeeds[id] = smoothed;
                }

                _previousPlayerPositions[id] = position;
            }
        }

        private void TryCatch()
        {
            if (_target == null || _catchCooldownRemaining > 0f || !_target.HasSanity)
                return;

            if (!IsInsideHouseBounds(_target.transform.position))
                return;

            // 드릴 카 세이프 존 안에서는 잡히지 않는다(§11.1, 임시 구현).
            if (DrillCarSafeZone.Contains(_target.transform.position))
                return;

            // 은신처 안이면 일반적으로 잡히지 않는다(§9.5) — 수색 중 명시적 검사만 통한다.
            if (HidingSpot.Contains(_target.transform.position))
                return;

            // 침대 밑 은신이 성립했으면 잡지 않는다. 성립 전(들어가는 걸 봤을 때)에는
            // 침대 밑까지 쫓아와 그대로 잡는다(사용자 확정 2026-09-03).
            if (IsBedHidden(_target))
                return;

            // 굴착 중에는 일반 잡힘 판정에서 빠진다. 목격한 굴착은 수색의 별도 검사로 처치한다.
            if (_target.IsBurrowed)
                return;

            Vector3 delta = _target.transform.position - transform.position;
            if (_settings.EvidenceTrackingEnabled
                && (Mathf.Abs(delta.y) > ArrivalVerticalTolerance
                    || !HasLineOfSight(transform.position + Vector3.up * _settings.GhostEyeHeight,
                        _target.transform.position + Vector3.up * _settings.TargetCenterHeight,
                        _target.transform)))
                return;
            delta.y = 0f;
            if (delta.magnitude > _settings.CatchRadius)
                return;

            if (_target.ServerMarkDead())
            {
                Debug.Log(
                    $"[GhostPrototype] Client {_target.OwnerClientId} 를 잡았습니다. 어택은 계속됩니다.",
                    this);
            }

            _catchCooldownRemaining = _settings.CatchCooldown;
            _lastKnownPosition = transform.position;
            _trackingMemory.Clear();
            BeginSearch();
        }

        /// <summary>
        /// 진행 방향(다음 경로 코너) 앞 `DoorOpenRange` 안의 닫힌 방문을 연다(§9.4). 목적지까지의 직선이
        /// 아니라 경로를 보므로 다른 층·모퉁이 너머의 대상을 쫓을 때도 길을 막은 문을 연다.
        /// NavMesh 는 문을 장애물로 굽지 않으므로 닫힌 문은 이렇게 열어야 지나간다.
        /// </summary>
        private void TryOpenBlockingDoor()
        {
            Vector3 waypoint = _hasPath && _pathCornerIndex < _pathCornerCount
                ? _pathCorners[_pathCornerIndex]
                : _lastKnownPosition;
            Vector3 direction = waypoint - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f)
                direction = transform.forward;

            if (!Physics.Raycast(
                    transform.position + Vector3.up,
                    direction.normalized,
                    out RaycastHit hit,
                    _settings.DoorOpenRange,
                    GameLayers.NonGhostPrototypeRaycastMask,
                    QueryTriggerInteraction.Ignore))
            {
                return;
            }

            DoorInteractable door = hit.collider.GetComponentInParent<DoorInteractable>();
            if (door != null && !door.IsOpen)
                door.ServerForceOpen();
        }

        private void ResetPursuit()
        {
            _pursuit = Pursuit.Roam;
            _searchWandering = false;
            _target = null;
            _targetWasVisible = false;
            _witnessedHidingPlayer = null;
            _witnessedBurrowPlayer = null;
            _searchRemaining = 0f;
            _searchScanRemaining = 0f;
            _witnessedBedPlayer = null;
            _trackingMemory.Clear();
            if (_searchMemory != null)
                _searchMemory.Clear();
        }

        /// <summary>집 내부 활동 경계는 평면(X/Z)만 제한한다. 높이는 계단·단차를 위해 보존한다.</summary>
        internal static bool IsInsideHouseBounds(Vector3 position, Vector3 center, Vector3 extents)
        {
            return position.x >= center.x - extents.x
                && position.x <= center.x + extents.x
                && position.z >= center.z - extents.z
                && position.z <= center.z + extents.z;
        }

        /// <summary>집 내부 활동 경계로 평면 좌표만 보정하고 높이는 유지한다.</summary>
        internal static Vector3 ClampToHouseBounds(Vector3 position, Vector3 center, Vector3 extents)
        {
            position.x = Mathf.Clamp(position.x, center.x - extents.x, center.x + extents.x);
            position.z = Mathf.Clamp(position.z, center.z - extents.z, center.z + extents.z);
            return position;
        }

        private bool IsInsideHouseBounds(Vector3 position)
        {
            return IsInsideHouseBounds(position, _roamCenter, _roamExtents)
                && (!_restrictRoamHeight || Mathf.Abs(position.y - _roamCenter.y) <= _roamExtents.y);
        }

        private void ClampToHouseBounds()
        {
            Vector3 clamped = ClampToHouseBounds(transform.position, _roamCenter, _roamExtents);
            if (clamped != transform.position)
                transform.position = clamped;
        }

        private void RebuildVisionCone()
        {
            if (_settings == null || _visionConeFilter == null)
                return;

            if (_generatedConeMesh != null)
                Destroy(_generatedConeMesh);

            _generatedConeMesh = GhostVision.BuildConeMesh(
                _highRisk.Value ? _settings.HighRiskVisionDistance : _settings.VisionDistance,
                _highRisk.Value ? _settings.HighRiskVisionAngle : _settings.VisionAngle);
            _visionConeFilter.sharedMesh = _generatedConeMesh;
        }

        private void HandlePhaseChanged(GhostPhase previous, GhostPhase current)
        {
            ApplyPhaseVisual(current);
        }

        private void HandleHighRiskChanged(bool previous, bool current)
        {
            RebuildVisionCone();
        }

        private void HandleDebugVisibleChanged(bool previous, bool current)
        {
            ApplyPhaseVisual(_phase.Value);
        }

        private void TryBindLocalViewer()
        {
            if (!Services.TryGet(out ISanityTeamService team)
                || !team.TryGetLocalState(out SanityNetworkState viewer))
                return;

            _localViewer = viewer;
            _localViewer.AliveStateChanged += HandleLocalViewerAliveChanged;
            _localViewer.SanityChanged += HandleLocalViewerSanityChanged;
            ApplyPhaseVisual(_phase.Value);
        }

        private void HandleLocalViewerAliveChanged(bool alive)
        {
            ApplyPhaseVisual(_phase.Value);
        }

        private void HandleLocalViewerSanityChanged(int previous, int current)
        {
            ApplyBodyClarity();
        }

        /// <summary>
        /// 이 피어의 로컬 플레이어 정신력이 낮을수록 본체를 선명하게(알파↑) 그린다. 연출 전용이라
        /// 피어마다 다르고 복제하지 않는다. 정신력을 잃은 관전자는 가장 선명한 값으로 본다.
        /// </summary>
        private void ApplyBodyClarity()
        {
            if (_settings == null || _bodyRenderers == null)
                return;

            float alpha;
            if (_localViewer == null)
                alpha = _settings.BodyAlphaAtFullSanity;
            else if (!_localViewer.HasSanity)
                alpha = _settings.BodyAlphaAtZeroSanity;
            else
                alpha = _settings.BodyAlphaForSanity(
                    _localViewer.Sanity, _localViewer.MinimumSanity, _localViewer.MaximumSanity);

            _bodyBlock ??= new MaterialPropertyBlock();
            for (int i = 0; i < _bodyRenderers.Length; i++)
            {
                Renderer bodyRenderer = _bodyRenderers[i];
                if (bodyRenderer == null)
                    continue;

                Material material = bodyRenderer.sharedMaterial;
                if (material == null || !material.HasProperty(BaseColorId))
                    continue;

                Color color = material.GetColor(BaseColorId);
                color.a = alpha;
                bodyRenderer.GetPropertyBlock(_bodyBlock);
                _bodyBlock.SetColor(BaseColorId, color);
                bodyRenderer.SetPropertyBlock(_bodyBlock);
            }
        }

        private void ApplyPhaseVisual(GhostPhase phase)
        {
            // §3.3: 평소엔 본체를 숨긴다. 경고·어택에만 노출. 디버그 토글은 그 위에 얹힌다.
            bool visible = phase is GhostPhase.Active or GhostPhase.Warning or GhostPhase.Attack
                || _debugForceVisible.Value
                || (_localViewer != null && !_localViewer.HasSanity);

            if (_body != null)
                _body.SetActive(visible);

            if (_bodyRenderers != null)
            {
                for (int i = 0; i < _bodyRenderers.Length; i++)
                {
                    if (_bodyRenderers[i] != null)
                        _bodyRenderers[i].enabled = visible;
                }
            }
            ApplyBodyClarity();

            if (_visionConeRoot != null)
                _visionConeRoot.SetActive(phase == GhostPhase.Attack);

            ApplyEnvironmentLights(phase);
            if (_warningAudio != null)
            {
                if (phase == GhostPhase.Warning)
                {
                    if (!_warningAudio.isPlaying)
                        _warningAudio.Play();
                }
                else if (_warningAudio.isPlaying)
                {
                    _warningAudio.Stop();
                }
            }

            if (_stateLight == null)
                return;

            // 불 꺼진 집에서 몸 조명이 크면 귀신이 등불을 들고 다니는 꼴이 된다 — 반경·밝기는 설정에서 작게.
            _stateLight.range = _settings.StateLightRange;
            switch (phase)
            {
                case GhostPhase.Warning:
                    _stateLight.enabled = true;
                    _stateLight.color = new Color(1f, 0.55f, 0.12f);
                    break;

                case GhostPhase.Attack:
                    _stateLight.enabled = true;
                    _stateLight.color = new Color(1f, 0.15f, 0.12f);
                    _stateLight.intensity = _settings.AttackLightIntensity;
                    break;

                default:
                    _stateLight.enabled = false;
                    break;
            }
        }

        private void PulseWarningLight()
        {
            if (_stateLight == null || !_stateLight.enabled)
                return;

            // 심장 박동 연출(§10.3)의 시각 버전. 오디오 에셋이 나오면 소리를 얹는다.
            _stateLight.intensity = Mathf.Lerp(
                _settings.WarningLightMin,
                _settings.WarningLightMax,
                Mathf.PingPong(Time.time * 2.4f, 1f));
        }

        private void ApplyEnvironmentLights(GhostPhase phase)
        {
            IReadOnlyList<GhostAmbientLight> lights = GhostAmbientLight.Registry;
            bool warningOn = Mathf.PingPong(Time.time * 2.4f, 1f) >= 0.5f;
            for (int i = 0; i < lights.Count; i++)
            {
                GhostAmbientLight ambient = lights[i];
                if (ambient == null || ambient.Light == null)
                    continue;

                Light light = ambient.Light;
                switch (phase)
                {
                    case GhostPhase.Warning:
                        light.color = ambient.BaseColor;
                        light.intensity = warningOn ? ambient.BaseIntensity : 0f;
                        break;
                    case GhostPhase.Attack:
                        light.color = Color.red;
                        light.intensity = ambient.BaseIntensity;
                        break;
                    default:
                        light.color = ambient.BaseColor;
                        light.intensity = ambient.BaseIntensity;
                        break;
                }
            }
        }

        private static AudioClip CreateHeartbeatClip()
        {
            const int sampleRate = 22050;
            float[] samples = new float[sampleRate];
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)sampleRate;
                float first = HeartbeatPulse(time - 0.08f);
                float second = HeartbeatPulse(time - 0.36f) * 0.7f;
                samples[i] = first + second;
            }

            AudioClip clip = AudioClip.Create("GhostWarningHeartbeat", sampleRate, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static float HeartbeatPulse(float time)
        {
            if (time < 0f || time > 0.18f)
                return 0f;

            return Mathf.Sin(2f * Mathf.PI * 68f * time) * Mathf.Exp(-time * 25f);
        }
    }
}
