using System.Collections.Generic;
using GhostHunter.Core;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Sanity;
using Unity.Netcode;
using UnityEngine;

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
        private const float RoamGiveUpSeconds = 10f;
        private const float ShakePulseInterval = 0.05f;
        private const float ShakeAngularFrequency = Mathf.PI * 2f * 9f; // 초당 9회 좌우 왕복

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

        [Header("Phenomena")]
        [SerializeField] private GhostPhenomenaPlayer _phenomenaPlayer;

        private readonly NetworkVariable<GhostPhase> _phase = new(
            GhostPhase.Idle,
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

        private CharacterController _controller;
        private GhostStateMachine _machine;
        private GhostPhenomenaDirector _phenomena;
        private ISanityTeamService _sanity;
        private Mesh _generatedConeMesh;
        private bool _serverReady;

        private FurnitureGrabTarget[] _sceneFurniture;
        private DoorInteractable[] _sceneDoors;
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
        private Vector3 _roamExtents = new(6f, 1.5f, 5f);
        private Vector3 _roamDestination;
        private float _roamGiveUpAt;

        private Pursuit _pursuit;
        private SanityNetworkState _target;
        private Vector3 _lastKnownPosition;
        private readonly HashSet<HidingSpot> _checkedHidingSpots = new();

        // 플레이어별 '침대 밑 은신' 성립 판정(§9.5, 사용자 확정 2026-09-03). 어택 중에만 돈다.
        private readonly Dictionary<ulong, BedHideEvaluator> _bedHide = new();
        private float _searchRemaining;
        private float _repathRemaining;
        private float _catchCooldownRemaining;

        private int _cleaningProgress;
        private bool _forceActive;

        public GhostPhase Phase => _phase.Value;
        public float SecondsUntilNextRoll => _machine != null ? _machine.SecondsUntilNextRoll : 0f;
        public bool CleaningBoostActive => _machine != null && _machine.CleaningBoostActive;
        public int CleaningProgress => _cleaningProgress;
        public bool ForceActive => _forceActive;

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
            _controller = GetComponent<CharacterController>();
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
            _debugForceVisible.OnValueChanged += HandleDebugVisibleChanged;
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
            _roamCenter = transform.position;
            _roamDestination = transform.position;
            _phase.Value = GhostPhase.Idle;
            _serverReady = true;
        }

        public override void OnNetworkDespawn()
        {
            _phase.OnValueChanged -= HandlePhaseChanged;
            _debugForceVisible.OnValueChanged -= HandleDebugVisibleChanged;
            _sanity = null;
            _machine = null;
            _phenomena = null;
            _sceneFurniture = null;
            _sceneDoors = null;
            _shakeTargets.Clear();
            _shakeRemaining = 0f;
            _target = null;
            _serverReady = false;
            _previousPlayerPositions.Clear();
            _playerSpeeds.Clear();
            _checkedHidingSpots.Clear();
            _bedHide.Clear();

            if (_generatedConeMesh != null)
            {
                Destroy(_generatedConeMesh);
                _generatedConeMesh = null;
            }
        }

        private void Update()
        {
            if (IsSpawned && _phase.Value == GhostPhase.Warning)
                PulseWarningLight();

            if (!IsServer || !_serverReady)
                return;

            ServerTick(Time.deltaTime);
        }

        /// <summary>스포너가 스폰 직후 집 내부 배회 범위를 넣어 준다.</summary>
        public void ServerConfigureRoam(Vector3 center, Vector3 size)
        {
            if (!IsServer)
                return;

            _roamCenter = center;
            _roamExtents = new Vector3(
                Mathf.Max(0.5f, size.x * 0.5f),
                Mathf.Max(0.5f, size.y * 0.5f),
                Mathf.Max(0.5f, size.z * 0.5f));
            _roamGiveUpAt = 0f;
            ClampToHouseBounds();
        }

        public void ServerSetCleaningProgress(int percent)
        {
            if (IsServer)
                _cleaningProgress = Mathf.Clamp(percent, 0, 100);
        }

        public void ServerSetForceActive(bool forceActive)
        {
            if (!IsServer)
                return;

            _forceActive = forceActive;
            _machine?.SetForceActive(forceActive);
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

            int playerCount = _sanity.CopyPlayerStates(_players);
            UpdatePlayerSpeeds(playerCount, deltaTime);

            bool hasLiving = _sanity.TryGetTeamAverage(out _, out int teamSanity, out int living)
                && living > 0;
            if (!hasLiving)
                teamSanity = 100;

            GhostPhase previous = _phase.Value;
            GhostPhase current = _machine.Tick(deltaTime, teamSanity, hasLiving, _cleaningProgress);
            if (current != previous)
            {
                _phase.Value = current;
                if (current != GhostPhase.Attack)
                {
                    ResetPursuit();
                    _bedHide.Clear();
                }
            }

            if (_catchCooldownRemaining > 0f)
                _catchCooldownRemaining = Mathf.Max(0f, _catchCooldownRemaining - deltaTime);

            if (current == GhostPhase.Attack)
                ServerTickAttack(deltaTime, playerCount);
            else
                ServerTickRoam(deltaTime, _settings.RoamSpeed);

            ServerTickPhenomena(deltaTime, current, teamSanity);
            ServerTickShake(deltaTime);
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
            _sceneDoors ??= FindObjectsByType<DoorInteractable>(FindObjectsSortMode.None);
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
            // 이전 틱의 추격 상태로 침대 밑 은신 성립 여부를 먼저 갱신한다(TryDetectPlayer 가 이 결과를 읽는다).
            EvaluateBedHide(playerCount, deltaTime);

            _repathRemaining -= deltaTime;
            bool detected = TryDetectPlayer(playerCount, out SanityNetworkState seen);

            if (detected)
            {
                _target = seen;
                _pursuit = Pursuit.Chase;
                _lastKnownPosition = seen.transform.position;
            }
            else if (_pursuit == Pursuit.Chase)
            {
                _pursuit = Pursuit.Search;
                _searchRemaining = _settings.SearchDuration;
                _checkedHidingSpots.Clear();
            }

            switch (_pursuit)
            {
                case Pursuit.Chase:
                    MoveToward(_lastKnownPosition, _settings.ChaseSpeed, deltaTime);
                    TryCatch();
                    break;

                case Pursuit.Search:
                    _searchRemaining -= deltaTime;
                    MoveToward(_lastKnownPosition, _settings.ChaseSpeed, deltaTime);
                    // 들어가는 걸 본 침대 밑 대상은 은신이 성립하기 전까지 수색 중에도 잡는다
                    // (IsBedHidden 이면 TryCatch 가 건너뛴다).
                    TryCatch();
                    ServerTickHidingSpots(playerCount);
                    if (_searchRemaining <= 0f)
                        ResetPursuit();
                    break;

                default:
                    ServerTickRoam(deltaTime, _settings.ChaseSpeed);
                    break;
            }

            if (_repathRemaining <= 0f)
            {
                _repathRemaining = _settings.RepathInterval;
                if (_pursuit != Pursuit.Roam)
                    TryOpenBlockingDoor(_lastKnownPosition);
            }
        }

        /// <summary>
        /// §9.5 일반 은신처. 수색 중 은신처에 처음 접근하면 그 자리에서 딱 1회,
        /// <see cref="GhostPrototypeSettings.HidingSpotCheckChance"/>(30% [임시]) 확률로 안을
        /// 들여다본다. 이번 수색(<see cref="Pursuit.Search"/>) 동안은 같은 은신처를 다시
        /// 확인하지 않는다 — G-8(판정 시점·주기·재검사)이 아직 미정이라, 사용자 확정(2026-08-31)에
        /// 따라 "최초 접근 시 1회"만 임시로 구현했다. 정식 규칙이 정해지면 이 메서드를 고친다.
        /// </summary>
        private void ServerTickHidingSpots(int playerCount)
        {
            IReadOnlyList<HidingSpot> spots = HidingSpot.Registry;
            if (spots.Count == 0)
                return;

            float radiusSqr = _settings.HidingSpotCheckRadius * _settings.HidingSpotCheckRadius;

            for (int i = 0; i < spots.Count; i++)
            {
                HidingSpot spot = spots[i];
                if (spot == null || _checkedHidingSpots.Contains(spot))
                    continue;

                if ((spot.transform.position - transform.position).sqrMagnitude > radiusSqr)
                    continue;

                // 발견 자체는 확인 여부와 무관하게 이번 수색에서 소모한다 — 실패해도 다시 안 본다.
                _checkedHidingSpots.Add(spot);

                if (Random.value >= _settings.HidingSpotCheckChance)
                    continue;

                for (int p = 0; p < playerCount; p++)
                {
                    SanityNetworkState player = _players[p];
                    if (player == null || !player.IsSpawned || !player.HasSanity)
                        continue;

                    if (!spot.ContainsPoint(player.transform.position))
                        continue;

                    if (player.ServerMarkDead())
                    {
                        Debug.Log(
                            $"[GhostPrototype] 은신처 검사 성공 — Client {player.OwnerClientId} 를 잡았습니다. " +
                            "어택은 계속됩니다.",
                            this);
                    }

                    break; // 은신처 하나엔 한 명만 있다고 가정한다.
                }
            }
        }

        private void ServerTickRoam(float deltaTime, float speed)
        {
            Vector3 flat = _roamDestination - transform.position;
            flat.y = 0f;

            if (flat.magnitude <= _settings.ReachDistance || Time.time >= _roamGiveUpAt)
                PickRoamDestination();

            MoveToward(_roamDestination, speed, deltaTime);
        }

        private void PickRoamDestination()
        {
            _roamGiveUpAt = Time.time + RoamGiveUpSeconds;

            for (int attempt = 0; attempt < 8; attempt++)
            {
                float x = Random.Range(_roamCenter.x - _roamExtents.x, _roamCenter.x + _roamExtents.x);
                float z = Random.Range(_roamCenter.z - _roamExtents.z, _roamCenter.z + _roamExtents.z);
                var origin = new Vector3(x, _roamCenter.y + _roamExtents.y + 2f, z);
                float rayLength = _roamExtents.y * 2f + 5f;

                if (Physics.Raycast(
                        origin,
                        Vector3.down,
                        out RaycastHit hit,
                        rayLength,
                        GameLayers.NonGhostPrototypeRaycastMask,
                        QueryTriggerInteraction.Ignore)
                    && hit.collider.gameObject.layer != GameLayers.Player)
                {
                    _roamDestination = new Vector3(x, hit.point.y, z);
                    return;
                }
            }

            _roamDestination = _roamCenter;
        }

        private void MoveToward(Vector3 target, float speed, float deltaTime)
        {
            if (deltaTime <= 0f)
                return;

            target = ClampToHouseBounds(target, _roamCenter, _roamExtents);

            Vector3 flat = target - transform.position;
            flat.y = 0f;
            float distance = flat.magnitude;

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
        }

        private bool TryDetectPlayer(int playerCount, out SanityNetworkState detected)
        {
            detected = null;
            Vector3 eye = transform.position + Vector3.up * _settings.GhostEyeHeight;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < playerCount; i++)
            {
                SanityNetworkState player = _players[i];
                if (player == null || !player.IsSpawned || !player.HasSanity)
                    continue;

                // 두더지 스킬 시스템 기획서 §5.2 — 굴착으로 땅속에 있는 동안은 완전히 탐지되지 않는다.
                if (player.IsBurrowed)
                    continue;

                // 침대 밑 은신이 성립하면(들어가는 걸 귀신이 못 봤다) 완전히 탐지에서 빠진다
                // (§9.5, 사용자 확정 2026-09-03). 성립 전에는 아래 일반 판정을 그대로 받는다.
                if (IsBedHidden(player))
                    continue;

                // 귀신은 집 밖 플레이어를 탐지·추격하지 않는다(기획서 §3.1·§11.1).
                if (!IsInsideHouseBounds(player.transform.position))
                    continue;

                // 드릴 카 세이프 존 안의 플레이어는 탐지 대상에서 제외한다(§11.1, 임시 구현).
                if (DrillCarSafeZone.Contains(player.transform.position))
                    continue;

                // 일반 은신처(§9.5) 안이면 정상 탐지에서 제외된다 — 수색 중 명시적 검사만 통한다.
                if (HidingSpot.Contains(player.transform.position))
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
                    _settings.VisionAngle,
                    distance,
                    _settings.VisionDistance);

                if ((nearby || inCone) && HasLineOfSight(eye, center, player.transform))
                {
                    detected = player;
                    bestDistance = distance;
                }
            }

            return detected != null;
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
                bool chased = _pursuit != Pursuit.Roam && _target == player;
                bool visible = eligible && !chased && IsVisibleInVisionCone(player);

                evaluator.Tick(deltaTime, eligible, chased, visible, _settings.BedHideConcealSeconds);
            }
        }

        /// <summary>침대 밑 은신이 성립해 귀신의 탐지·잡힘·수색 훔쳐보기에서 완전히 빠지는가.</summary>
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
                _settings.VisionAngle,
                distance,
                _settings.VisionDistance);

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

            Vector3 delta = _target.transform.position - transform.position;
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
            _target = null;
            _pursuit = Pursuit.Search;
            _searchRemaining = _settings.SearchDuration;
        }

        private void TryOpenBlockingDoor(Vector3 destination)
        {
            Vector3 from = transform.position + Vector3.up;
            Vector3 to = destination + Vector3.up;

            if (!Physics.Linecast(
                    from,
                    to,
                    out RaycastHit hit,
                    GameLayers.NonGhostPrototypeRaycastMask,
                    QueryTriggerInteraction.Ignore))
            {
                return;
            }

            if (Vector3.Distance(transform.position, hit.point) > _settings.DoorOpenRange)
                return;

            DoorInteractable door = hit.collider.GetComponentInParent<DoorInteractable>();
            if (door != null && !door.IsOpen)
                door.ServerForceOpen();
        }

        private void ResetPursuit()
        {
            _pursuit = Pursuit.Roam;
            _target = null;
            _searchRemaining = 0f;
            _checkedHidingSpots.Clear();
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
            return IsInsideHouseBounds(position, _roamCenter, _roamExtents);
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
                _settings.VisionDistance,
                _settings.VisionAngle);
            _visionConeFilter.sharedMesh = _generatedConeMesh;
        }

        private void HandlePhaseChanged(GhostPhase previous, GhostPhase current)
        {
            ApplyPhaseVisual(current);
        }

        private void HandleDebugVisibleChanged(bool previous, bool current)
        {
            ApplyPhaseVisual(_phase.Value);
        }

        private void ApplyPhaseVisual(GhostPhase phase)
        {
            // §3.3: 평소엔 본체를 숨긴다. 경고·어택에만 노출. 디버그 토글은 그 위에 얹힌다.
            bool visible = phase is GhostPhase.Warning or GhostPhase.Attack || _debugForceVisible.Value;

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

            if (_visionConeRoot != null)
                _visionConeRoot.SetActive(phase == GhostPhase.Attack);

            if (_stateLight == null)
                return;

            switch (phase)
            {
                case GhostPhase.Warning:
                    _stateLight.enabled = true;
                    _stateLight.color = new Color(1f, 0.55f, 0.12f);
                    break;

                case GhostPhase.Attack:
                    _stateLight.enabled = true;
                    _stateLight.color = new Color(1f, 0.15f, 0.12f);
                    _stateLight.intensity = 5f;
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
            _stateLight.intensity = Mathf.Lerp(1.2f, 4.5f, Mathf.PingPong(Time.time * 2.4f, 1f));
        }
    }
}
