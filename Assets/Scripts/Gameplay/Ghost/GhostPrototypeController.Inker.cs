using System.Collections.Generic;
using System.Text;
using GhostHunter.Core;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Sanity;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// '명령(인커)' 귀신 — 메모 "말을 거는 귀신"(project/ghost-inker.md). 설정이 <see cref="GhostInkerSettings"/> 일 때만 돈다(ADR-0024).
    /// 서버: 어택이 아닐 때 주기마다 현상 1(문틈 응시)·2(벽 글씨)를 확률로 발동하고, 눈 맞춤·글씨 발견·명령 위반·닦기를 판정한다.
    /// 피어: 복제된 글씨 목록과 진행 상태로 글씨·인형·문·소리를 재생한다. 공통 어택·배회 규칙은 그대로다.
    /// 원문에 없어 정한 값은 [임시 해석] — 기획서 §9 IK-n.
    /// </summary>
    public sealed partial class GhostPrototypeController : ICommandWritingService
    {
        private const float InkerPerceptionInterval = 0.1f;
        private const float InkerDefaultEyeHeight = 1.6f;
        private const float InkerFaceHeight = 1.45f;
        private const float InkerWritingSurfaceOffset = 0.015f;
        private const float InkerEyeContactMaxDistance = 15f;
        private const float InkerWritingSpacing = 1.2f;
        private const int InkerMaxWritings = 16;
        private const int InkerWallRayCount = 11;
        private const float InkerWallSweepDegrees = 45f;

        private sealed class InkerWatch
        {
            public readonly InkerTurnAroundDetector Turn = new();
            public int LastJumpCount;
            public bool JumpBaselineSet;
            public float EyeHold;
            public bool EyeContactApplied;

            /// <summary>다음 판정 틱까지 쌓인 이벤트 행동(문 열기·가구 만지기) — 서버 이벤트로 들어온다.</summary>
            public InkerActions PendingActions;
        }

        private NetworkList<InkerWritingState> _inkerWritings;
        private readonly NetworkVariable<InkerSequenceState> _inkerSequence = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        // 서버 전용.
        private readonly GhostInkerSequence _inkerRun = new();
        private readonly Dictionary<ulong, InkerWatch> _inkerWatch = new();
        private readonly Dictionary<int, HashSet<ulong>> _inkerDiscovered = new();
        private readonly List<int> _inkerRemoveIds = new(4);
        private readonly RaycastHit[] _inkerHits = new RaycastHit[12];
        private InkerSequenceState _inkerRunBase;
        private DoorInteractable _inkerDoor;
        private Vector3 _inkerFacePosition;
        private int _inkerRunWritingId;
        private int _inkerNextWritingId = 1;
        private int _inkerDoorPeekCount;
        private int _inkerWallWritingCount;
        private int _inkerViolationCount;
        private float _inkerCheckRemaining;
        private float _inkerPerceptionRemaining;
        private string _inkerLastEvent = "-";
        private bool _inkerServiceBound;
        private bool _inkerEventsBound;

        // 피어 연출.
        private readonly Dictionary<int, InkerWritingView> _inkerWritingViews = new();
        private readonly List<int> _inkerStaleViewIds = new(4);
        private bool _inkerViewsDirty;
        private InkerPuppetView _inkerPuppet;
        private InkerPuppetView _inkerScarePuppet;
        private float _inkerScareRemaining;
        private InkerStep _inkerPlayedStep;
        private AudioSource _inkerLoopAudio;
        private InkerAtmosphere _inkerAtmosphere;
        private InkerStep _inkerAtmosphereStep;

        private GhostInkerSettings InkerSettings => _settings as GhostInkerSettings;

        /// <summary>이 귀신이 인커인가(설정 타입).</summary>
        public bool IsInker => InkerSettings != null;

        /// <summary>복제된 명령 글씨 수(모든 피어).</summary>
        public int InkerWritingCount => _inkerWritings != null && IsSpawned ? _inkerWritings.Count : 0;

        /// <summary>복제된 현상 진행 상태(모든 피어).</summary>
        public InkerSequenceState InkerSequence => _inkerSequence.Value;

        public int InkerDoorPeekCount => _inkerDoorPeekCount;
        public int InkerWallWritingCount => _inkerWallWritingCount;
        public int InkerViolationCount => _inkerViolationCount;

        /// <summary>복제된 글씨 하나(모든 피어). 테스트·HUD 용.</summary>
        public bool TryGetInkerWriting(int index, out InkerWritingState writing)
        {
            writing = default;
            if (_inkerWritings == null || !IsSpawned || index < 0 || index >= _inkerWritings.Count)
                return false;
            writing = _inkerWritings[index];
            return true;
        }

        // ───────────────────────── 수명 ─────────────────────────

        /// <summary>NetworkList 는 스폰 전에 반드시 만들어 둬야 한다(모든 귀신 프리팹 — 인커가 아니면 비어 있다).</summary>
        private void InkerAwake()
        {
            _inkerWritings ??= new NetworkList<InkerWritingState>();
        }

        private void InkerSpawn()
        {
            _inkerWritings.OnListChanged += HandleInkerWritingsChanged;
            _inkerViewsDirty = true;
            _inkerPlayedStep = InkerStep.None;

            GhostInkerSettings settings = InkerSettings;
            if (settings == null)
                return;

            if (!Services.TryGet(out ICommandWritingService _))
            {
                Services.Bind<ICommandWritingService>(this);
                _inkerServiceBound = true;
            }

            if (!IsServer)
                return;

            // 문 열기·가구 만지기는 그 순간의 서버 이벤트로 받는다(명령 'DON'T OPEN THE DOOR'·'DON'T TOUCH THE FURNITURE').
            if (!_inkerEventsBound)
            {
                DoorInteractable.ServerOpenedByPlayer += HandleInkerDoorOpened;
                FurnitureGrabTarget.ServerTouchedByPlayer += HandleInkerFurnitureTouched;
                _inkerEventsBound = true;
            }

            _inkerRun.Cancel();
            _inkerWatch.Clear();
            _inkerDiscovered.Clear();
            _inkerDoorPeekCount = 0;
            _inkerWallWritingCount = 0;
            _inkerViolationCount = 0;
            _inkerNextWritingId = 1;
            _inkerCheckRemaining = settings.InkerCheckInterval;
            _inkerPerceptionRemaining = 0f;
            _inkerLastEvent = "-";
        }

        private void InkerDespawn()
        {
            if (_inkerWritings != null)
                _inkerWritings.OnListChanged -= HandleInkerWritingsChanged;

            if (IsServer && _inkerDoor != null)
                _inkerDoor.ServerReleaseGhostHold();
            _inkerDoor = null;
            _inkerRun.Cancel();
            _inkerWatch.Clear();
            _inkerDiscovered.Clear();
            InkerUnbindService();
            InkerDestroyViews();
        }

        /// <summary>닦기 서비스와 서버 이벤트 구독을 푼다(디스폰·파괴 모두에서 부른다).</summary>
        private void InkerUnbindService()
        {
            if (_inkerEventsBound)
            {
                DoorInteractable.ServerOpenedByPlayer -= HandleInkerDoorOpened;
                FurnitureGrabTarget.ServerTouchedByPlayer -= HandleInkerFurnitureTouched;
                _inkerEventsBound = false;
            }

            if (!_inkerServiceBound)
                return;
            Services.Unbind<ICommandWritingService>(this);
            _inkerServiceBound = false;
        }

        private void HandleInkerDoorOpened(DoorInteractable door, ulong clientId)
            => MarkInkerAction(clientId, InkerActions.OpenedDoor);

        private void HandleInkerFurnitureTouched(GameObject furniture, ulong clientId)
            => MarkInkerAction(clientId, InkerActions.TouchedFurniture);

        private void MarkInkerAction(ulong clientId, InkerActions action)
        {
            if (!IsServer || !IsSpawned || InkerSettings == null)
                return;
            GetInkerWatch(clientId).PendingActions |= action;
        }

        private void InkerDestroyViews()
        {
            foreach (InkerWritingView view in _inkerWritingViews.Values)
            {
                if (view != null)
                    Destroy(view.gameObject);
            }

            _inkerWritingViews.Clear();
            if (_inkerPuppet != null)
                Destroy(_inkerPuppet.gameObject);
            if (_inkerScarePuppet != null)
                Destroy(_inkerScarePuppet.gameObject);
            _inkerPuppet = null;
            _inkerScarePuppet = null;
            _inkerScareRemaining = 0f;
            InkerSounds.Stop(_inkerLoopAudio);
            _inkerLoopAudio = null;
            if (_inkerAtmosphere != null)
                Destroy(_inkerAtmosphere.gameObject);
            _inkerAtmosphere = null;
            _inkerAtmosphereStep = InkerStep.None;
        }

        private void HandleInkerWritingsChanged(NetworkListEvent<InkerWritingState> change) => _inkerViewsDirty = true;

        private double InkerServerNow()
            => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

        // ───────────────────────── 서버 틱 ─────────────────────────

        private void ServerTickInker(float deltaTime, GhostPhase phase, int playerCount)
        {
            GhostInkerSettings settings = InkerSettings;
            if (settings == null)
                return;

            ServerTickInkerWatch(settings, playerCount, deltaTime);

            if (_inkerRun.IsRunning)
            {
                // 진행 중 어택 타임이 시작되면 귀신을 지우고 현상별로 정리한다(원문 §2-5·§3-5).
                if (phase == GhostPhase.Attack)
                    ServerCancelInkerRun("어택 시작");
                else
                    ServerTickInkerRun(settings, deltaTime, playerCount);
                return;
            }

            // 어택 타임이 아닐 때만(경고 5초도 곧 어택이라 뺀다). [임시 해석 IK-9] 주기마다 현상별 확률을 굴린다.
            if (phase is not (GhostPhase.Active or GhostPhase.Calming))
                return;

            _inkerCheckRemaining -= deltaTime;
            if (_inkerCheckRemaining > 0f)
                return;

            _inkerCheckRemaining = settings.InkerCheckInterval;
            ServerRollInkerPhenomena(settings, playerCount);
        }

        private void ServerRollInkerPhenomena(GhostInkerSettings settings, int playerCount)
        {
            bool doorFirst = Random.value < 0.5f;
            for (int pass = 0; pass < 2; pass++)
            {
                bool door = (pass == 0) == doorFirst;
                if (door)
                {
                    if (_inkerDoorPeekCount >= settings.DoorPeekMaxCount || Random.value >= settings.DoorPeekChance)
                        continue;
                    if (ServerTryStartDoorPeek(settings, playerCount, false, out _))
                    {
                        _inkerDoorPeekCount++;
                        return;
                    }
                }
                else
                {
                    if (_inkerWallWritingCount >= settings.WallWritingMaxCount || Random.value >= settings.WallWritingChance)
                        continue;
                    if (ServerTryStartWallWriting(settings, playerCount, false, out _))
                    {
                        _inkerWallWritingCount++;
                        return;
                    }
                }
            }
        }

        private void ServerTickInkerRun(GhostInkerSettings settings, float deltaTime, int playerCount)
        {
            bool eyeContact = _inkerRun.Step == InkerStep.DoorWatching
                && ServerEvaluateEyeContact(settings, playerCount, deltaTime);
            bool playerClose = _inkerRun.Step == InkerStep.WallWriting
                && AnyLivingPlayerWithin(_inkerRunBase.PuppetPosition, settings.WallRushDistance, playerCount);

            // 한 틱에 여러 단계를 넘길 수 있다(긴 프레임). 바뀐 단계마다 서버 효과를 적용한다.
            for (int guard = 0; guard < 8; guard++)
            {
                if (!_inkerRun.Tick(guard == 0 ? deltaTime : 0f, eyeContact, playerClose))
                    break;
                ServerOnInkerStepChanged(settings);
                eyeContact = false;
                playerClose = false;
                if (!_inkerRun.IsRunning)
                    break;
            }
        }

        private void ServerOnInkerStepChanged(GhostInkerSettings settings)
        {
            InkerTimings timings = settings.Timings;
            switch (_inkerRun.Step)
            {
                case InkerStep.DoorOpening:
                    if (_inkerDoor != null)
                        _inkerDoor.ServerBeginGhostPeek(settings.DoorPeekOpenDegrees, timings.DoorOpen);
                    break;
                case InkerStep.DoorTilt:
                    _inkerLastEvent = "문틈 귀신과 눈이 마주침";
                    break;
                case InkerStep.DoorSlam:
                    if (_inkerDoor != null)
                        _inkerDoor.ServerGhostSlam();
                    break;
                case InkerStep.DoorScratch:
                    _inkerRunWritingId = ServerAddDoorWriting(settings, timings.DoorScratch);
                    break;
                case InkerStep.Done:
                    // 다 쓴 글씨가 명령 글씨로 활성화된다. 현상 2는 다 쓰면 바로 끝난다(2026-11-16 — 목 돌리기·응시 없음).
                    ServerCompleteWriting(_inkerRunWritingId);
                    if (_inkerRunBase.Kind == InkerPhenomenon.DoorPeek)
                    {
                        if (_inkerDoor != null)
                            _inkerDoor.ServerReleaseGhostHold();
                    }
                    else if (_inkerRun.Rushed)
                    {
                        _inkerLastEvent = "벽 글씨 — 다가와서 즉시 완성";
                    }

                    ServerFinishInkerRun();
                    return;
            }

            ServerPublishInkerStep();
        }

        private void ServerPublishInkerStep()
        {
            InkerSequenceState state = _inkerRunBase;
            state.Step = _inkerRun.Step;
            state.StepStartTime = InkerServerNow();
            state.WritingId = _inkerRunWritingId;
            _inkerSequence.Value = state;
        }

        private void ServerFinishInkerRun()
        {
            _inkerRun.Cancel();
            _inkerRunBase = default;
            _inkerSequence.Value = default;
            _inkerDoor = null;
            _inkerRunWritingId = 0;
        }

        /// <summary>
        /// 진행 중 현상을 끊는다. 현상 1은 글씨·귀신을 지우고 문을 닫힌 상태로 되돌린다. 현상 2는 다 쓰기 전이면 쓰던 글씨를 지우고,
        /// 다 썼으면 글씨를 남긴다(원문 §3-5).
        /// </summary>
        private void ServerCancelInkerRun(string reason)
        {
            if (!_inkerRun.IsRunning)
                return;

            if (_inkerRunBase.Kind == InkerPhenomenon.DoorPeek)
            {
                ServerRemoveWriting(_inkerRunWritingId);
                if (_inkerDoor != null)
                {
                    _inkerDoor.ServerReleaseGhostHold();
                    _inkerDoor.ServerForceClose();
                }
            }
            else if (TryFindWriting(_inkerRunWritingId, out InkerWritingState writing, out _) && !writing.Complete)
            {
                ServerRemoveWriting(_inkerRunWritingId);
            }

            _inkerLastEvent = $"현상 취소({reason})";
            ServerFinishInkerRun();
        }

        // ───────────────────────── 현상 1 — 문틈 ─────────────────────────

        private bool ServerTryStartDoorPeek(GhostInkerSettings settings, int playerCount, bool force, out string reason)
        {
            reason = null;
            EnsureSceneCollections();
            DoorInteractable bestDoor = null;
            SanityNetworkState bestPlayer = null;
            Bounds bestShape = default;
            float bestScore = float.MaxValue;
            float maxDistance = settings.DoorPeekMaxDistance + (force ? 4f : 0f);
            float minDistance = force ? Mathf.Min(1.2f, settings.DoorPeekMinDistance) : settings.DoorPeekMinDistance;

            for (int p = 0; p < playerCount; p++)
            {
                SanityNetworkState player = _players[p];
                if (!IsInkerCandidate(player))
                    continue;
                GetInkerPlayerView(player, out Vector3 eye, out Vector3 forward, out _);

                for (int d = 0; d < _sceneDoors.Length; d++)
                {
                    DoorInteractable door = _sceneDoors[d];
                    if (door == null || !door.IsSpawned || door.IsGhostHeld || (!force && door.IsOpen))
                        continue;
                    if (!TryGetDoorShape(door, out Bounds shape))
                        continue;

                    Vector3 center = DoorWorld(door, shape.center);
                    if (!IsInsideHouseBounds(center))
                        continue;

                    Vector3 planar = center - player.transform.position;
                    planar.y = 0f;
                    float distance = planar.magnitude;
                    if (distance < minDistance || distance > maxDistance)
                        continue;
                    if (AnyLivingPlayerWithin(center, settings.DoorPeekClearRadius, playerCount))
                        continue;

                    float angle = Vector3.Angle(forward, center - eye);
                    if (!force && angle > settings.DoorVisibleAngle)
                        continue;
                    Vector3 normal = door.ClosedWorldRotation * Vector3.forward;
                    float side = Mathf.Sign(Vector3.Dot(eye - center, normal));
                    if (!force && !HasInkerLineOfSight(eye, center + normal * (side * 0.08f), player.transform, door.transform))
                        continue;

                    float score = angle + (force ? distance * 2f : 0f);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestDoor = door;
                        bestPlayer = player;
                        bestShape = shape;
                    }
                }
            }

            if (bestDoor == null)
            {
                reason = "조건에 맞는 문이 없습니다(집 안 생존자, 2~6m, 닫힌 문, 문 1m 안에 사람 없음, 시야 안).";
                return false;
            }

            ServerBeginDoorPeek(settings, bestDoor, bestShape, bestPlayer);
            return true;
        }

        private void ServerBeginDoorPeek(GhostInkerSettings settings, DoorInteractable door, Bounds shape,
            SanityNetworkState target)
        {
            GetInkerPlayerView(target, out Vector3 eye, out _, out _);
            Quaternion closed = door.ClosedWorldRotation;
            Vector3 center = DoorWorld(door, shape.center);
            Vector3 normal = closed * Vector3.forward;
            float side = Vector3.Dot(eye - center, normal) >= 0f ? 1f : -1f;
            float freeX = Mathf.Abs(shape.max.x) >= Mathf.Abs(shape.min.x) ? shape.max.x : shape.min.x;
            Vector3 lateral = closed * Vector3.right * Mathf.Sign(freeX == 0f ? 1f : freeX);
            Vector3 toGhost = -normal * side;

            // 플레이어 반대편, 문짝이 열리는 쪽 끝(손잡이 쪽) 뒤에 숨어 있다가 틈으로 몸을 내민다.
            Vector3 edge = DoorWorld(door, new Vector3(freeX, shape.min.y, shape.center.z));
            Vector3 lean = edge - lateral * 0.2f + toGhost * 0.6f;
            Vector3 hidden = lean - lateral * 0.45f;
            _inkerFacePosition = lean + Vector3.up * InkerFaceHeight;
            float faceYaw = YawTowards(_inkerFacePosition, eye);

            foreach (InkerWatch watch in _inkerWatch.Values)
            {
                watch.EyeHold = 0f;
                watch.EyeContactApplied = false;
            }

            bool wasOpen = door.IsOpen;
            _inkerDoor = door;
            _inkerRunWritingId = 0;
            _inkerRunBase = new InkerSequenceState
            {
                Kind = InkerPhenomenon.DoorPeek,
                PuppetPosition = hidden,
                LeanOffset = lean - hidden,
                BodyYaw = faceYaw,
                FaceYaw = faceYaw,
                DoorId = door.NetworkObjectId,
            };
            _inkerRun.StartDoorPeek(settings.Timings, wasOpen);
            if (wasOpen)
                door.ServerGhostSlam();
            else
                door.ServerBeginGhostPeek(settings.DoorPeekOpenDegrees, settings.Timings.DoorOpen);
            _inkerLastEvent = $"현상 1 시작 — 문 '{door.name}'";
            ServerPublishInkerStep();
        }

        private int ServerAddDoorWriting(GhostInkerSettings settings, float revealSeconds)
        {
            if (_inkerDoor == null || !TryGetDoorShape(_inkerDoor, out Bounds shape))
                return 0;

            // 글씨는 대상 플레이어가 서 있던 쪽 면에 쓴다(IK-16 임시 해석). 인형이 숨은 쪽의 반대 면이다.
            // 문 로컬 경계와 같은 기준(닫힌 자세)으로 숨은 자리를 바꿔 어느 면인지 본다.
            Vector3 hiddenLocal = Quaternion.Inverse(_inkerDoor.ClosedWorldRotation)
                * (_inkerRunBase.PuppetPosition - _inkerDoor.transform.position);
            float side = hiddenLocal.z - shape.center.z > 0f ? -1f : 1f;

            // 문짝 폭에 맞춰 줄을 바꾸고("I SEE / YOU"), 그래도 넓으면 글자를 줄인다.
            float leafWidth = shape.size.x;
            string text = InkerCommandText.Of(InkerCommand.None);
            float maxWidth = Mathf.Max(0.3f, leafWidth * 0.9f);
            InkerStrokeFont.Size(text, settings.WritingHeight, maxWidth,
                out float width, out float height, out float wrapWidth, out float letterHeight);

            float faceZ = side > 0f ? shape.max.z : shape.min.z;
            var local = new Vector3(shape.center.x,
                Mathf.Min(shape.min.y + settings.WallWritingHeight, shape.max.y - height * 0.5f - 0.05f),
                faceZ + side * InkerWritingSurfaceOffset);
            Quaternion rotation = Quaternion.LookRotation(Vector3.forward * side, Vector3.up);
            return ServerAddWriting(settings, InkerCommand.None, InkerPhenomenon.DoorPeek,
                _inkerDoor.NetworkObjectId, local, rotation, width, height, letterHeight, wrapWidth, revealSeconds);
        }

        /// <summary>③ 대기 중 눈 맞춤: 카메라 정면 15° 안에 귀신 얼굴이 0.3초 이상(원문 §2-3-1). 맞춘 플레이어마다 한 번 −5.</summary>
        private bool ServerEvaluateEyeContact(GhostInkerSettings settings, int playerCount, float deltaTime)
        {
            bool contact = false;
            for (int i = 0; i < playerCount; i++)
            {
                SanityNetworkState player = _players[i];
                if (player == null || !player.IsSpawned || !player.HasSanity || player.IsBurrowed)
                    continue;

                InkerWatch watch = GetInkerWatch(player.OwnerClientId);
                GetInkerPlayerView(player, out Vector3 eye, out Vector3 forward, out _);
                Vector3 toFace = _inkerFacePosition - eye;
                bool looking = toFace.magnitude <= InkerEyeContactMaxDistance
                    && Vector3.Angle(forward, toFace) <= settings.EyeContactAngle
                    && HasInkerLineOfSight(eye, _inkerFacePosition, player.transform,
                        _inkerDoor != null ? _inkerDoor.transform : null);
                watch.EyeHold = looking ? watch.EyeHold + deltaTime : 0f;
                if (watch.EyeHold + 1e-4f < settings.EyeContactSeconds)
                    continue;

                contact = true;
                if (!watch.EyeContactApplied)
                {
                    watch.EyeContactApplied = true;
                    if (settings.EyeContactSanityDecrease > 0)
                        player.ServerApplyDecrease(settings.EyeContactSanityDecrease);
                }
            }

            return contact;
        }

        // ───────────────────────── 현상 2 — 벽 글씨 ─────────────────────────

        private bool ServerTryStartWallWriting(GhostInkerSettings settings, int playerCount, bool force, out string reason)
        {
            reason = null;
            float bestScore = float.MaxValue;
            SanityNetworkState bestPlayer = null;
            Vector3 bestPoint = default;
            Vector3 bestNormal = default;
            Vector3 bestFeet = default;
            float minDistance = force ? Mathf.Min(1.2f, settings.WallWritingMinDistance) : settings.WallWritingMinDistance;
            int wallMask = GameLayers.NonGhostPrototypeRaycastMask & ~GameLayers.FurnitureMask
                & ~(GameLayers.Player >= 0 ? 1 << GameLayers.Player : 0);

            for (int p = 0; p < playerCount; p++)
            {
                SanityNetworkState player = _players[p];
                if (!IsInkerCandidate(player))
                    continue;
                GetInkerPlayerView(player, out Vector3 eye, out Vector3 forward, out float yaw);
                Vector3 feetOrigin = player.transform.position;
                Vector3 origin = feetOrigin + Vector3.up * settings.WallWritingHeight;

                for (int k = 0; k < InkerWallRayCount; k++)
                {
                    // 정면부터 좌우로 번갈아 훑는다: 0, +9, -9, +18, …
                    int step = (k + 1) / 2;
                    float offset = (k % 2 == 1 ? 1f : -1f) * step * (InkerWallSweepDegrees * 2f / (InkerWallRayCount - 1));
                    Vector3 direction = Quaternion.Euler(0f, yaw + offset, 0f) * Vector3.forward;
                    if (!Physics.Raycast(origin, direction, out RaycastHit hit, settings.WallWritingMaxDistance,
                            wallMask, QueryTriggerInteraction.Ignore))
                        continue;
                    if (hit.distance < minDistance)
                        continue;
                    if (hit.collider.GetComponentInParent<DoorInteractable>() != null
                        || (hit.rigidbody != null && !hit.rigidbody.isKinematic))
                        continue;

                    Vector3 normal = hit.normal;
                    if (Mathf.Abs(normal.y) > 0.3f)
                        continue;
                    normal.y = 0f;
                    normal.Normalize();
                    if (Vector3.Dot(normal, -direction) < 0.5f || !IsInsideHouseBounds(hit.point)
                        || IsWritingNear(hit.point, InkerWritingSpacing))
                        continue;

                    Vector3 stand = hit.point + normal * settings.WallStandOffset;
                    if (!Physics.Raycast(stand, Vector3.down, out RaycastHit floor, settings.WallWritingHeight + 0.6f,
                            wallMask, QueryTriggerInteraction.Ignore))
                        continue;
                    Vector3 feet = floor.point;
                    if (Mathf.Abs(feet.y - feetOrigin.y) > 1f)
                        continue;
                    if (Physics.CheckCapsule(feet + Vector3.up * 0.4f, feet + Vector3.up * 1.5f, 0.22f, wallMask,
                            QueryTriggerInteraction.Ignore))
                        continue;

                    Vector3 writingCenter = hit.point + normal * InkerWritingSurfaceOffset;
                    float viewAngle = Vector3.Angle(forward, writingCenter - eye);
                    if (!force && viewAngle > settings.DoorVisibleAngle)
                        continue;

                    float score = Mathf.Abs(offset);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestPlayer = player;
                        bestPoint = writingCenter;
                        bestNormal = normal;
                        bestFeet = feet;
                    }
                }
            }

            if (bestPlayer == null)
            {
                reason = "글씨를 쓸 벽이 없습니다(집 안 생존자 정면 2~7m 의 빈 벽, 그 앞 빈 바닥).";
                return false;
            }

            ServerBeginWallWriting(settings, bestPoint, bestNormal, bestFeet);
            return true;
        }

        /// <summary>
        /// 현상 2 시작 — 글씨만 저절로 써진다(2026-11-16 사용자 요청: 귀신이 쓰고 돌아보는 모습 없이 허공이 쓰는 느낌).
        /// <paramref name="feet"/>(글씨 앞 바닥)는 2m 접근·쓰는 소리·화면 연출의 기준점이다.
        /// </summary>
        private void ServerBeginWallWriting(GhostInkerSettings settings, Vector3 writingCenter, Vector3 normal, Vector3 feet)
        {
            InkerCommand command = settings.PickWallCommand(Random.value);
            string text = InkerCommandText.Of(command);
            // 긴 명령은 낱말 단위로 두 줄에 쓴다("DON'T TOUCH / THE FURNITURE").
            InkerStrokeFont.Size(text, settings.WritingHeight, settings.WritingMaxLineWidth,
                out float width, out float height, out float wrapWidth, out float letterHeight);
            Quaternion rotation = Quaternion.LookRotation(normal, Vector3.up);
            InkerTimings timings = settings.Timings;

            _inkerDoor = null;
            _inkerRunWritingId = ServerAddWriting(settings, command, InkerPhenomenon.WallWriting, 0, writingCenter,
                rotation, width, height, letterHeight, wrapWidth, timings.WallWrite);
            _inkerRunBase = new InkerSequenceState
            {
                Kind = InkerPhenomenon.WallWriting,
                PuppetPosition = feet,
            };
            _inkerRun.StartWallWriting(timings);
            _inkerLastEvent = $"현상 2 시작 — '{text}'";
            ServerPublishInkerStep();
        }

        // ───────────────────────── 발견·위반 ─────────────────────────

        private void ServerTickInkerWatch(GhostInkerSettings settings, int playerCount, float deltaTime)
        {
            _inkerPerceptionRemaining -= deltaTime;
            bool discover = _inkerPerceptionRemaining <= 0f;
            if (discover)
                _inkerPerceptionRemaining = InkerPerceptionInterval;

            float now = Time.time;
            for (int i = 0; i < playerCount; i++)
            {
                SanityNetworkState player = _players[i];
                if (player == null || !player.IsSpawned)
                    continue;

                ulong id = player.OwnerClientId;
                InkerWatch watch = GetInkerWatch(id);
                GetInkerPlayerView(player, out Vector3 eye, out Vector3 forward, out float yaw);
                bool turned = watch.Turn.Sample(now, yaw, settings.TurnAroundWindowSeconds, settings.TurnAroundDegrees);
                bool jumped = false;
                if (player.TryGetComponent(out PlayerMotor motor))
                {
                    int count = motor.JumpCount;
                    jumped = watch.JumpBaselineSet && count != watch.LastJumpCount;
                    watch.LastJumpCount = count;
                    watch.JumpBaselineSet = true;
                }

                // 문 열기·가구 만지기는 서버 이벤트로 쌓여 있다가 여기서 한 번에 판정한다.
                InkerActions actions = watch.PendingActions;
                watch.PendingActions = InkerActions.None;
                if (turned)
                    actions |= InkerActions.TurnedAround;
                if (jumped)
                    actions |= InkerActions.Jumped;

                if (!player.HasSanity)
                    continue;
                // 이번 틱에 명령 글씨를 처음 봤으면, 보기 직전까지 한 동작(돌아보며 발견·문을 열고 발견)은 세지 않는다.
                if (discover && ServerInkerDiscover(settings, player, id, eye, forward, watch))
                    actions = InkerActions.None;

                if (actions != InkerActions.None)
                    ServerInkerCheckViolation(settings, player, id, actions);
            }
        }

        /// <summary>
        /// 다 쓴 글씨를 처음 본 플레이어를 기록한다. [임시 해석 IK-4] 본 플레이어만 그 명령을 지켜야 한다.
        /// 현상 1의 "I SEE YOU" 는 문 앞에서 발견하면 −3(원문 §2-4). 명령이 있는 글씨를 새로 봤으면 true.
        /// </summary>
        private bool ServerInkerDiscover(GhostInkerSettings settings, SanityNetworkState player, ulong id,
            Vector3 eye, Vector3 forward, InkerWatch watch)
        {
            bool boundCommand = false;
            for (int i = 0; i < _inkerWritings.Count; i++)
            {
                InkerWritingState writing = _inkerWritings[i];
                if (!writing.Complete)
                    continue;
                if (!_inkerDiscovered.TryGetValue(writing.Id, out HashSet<ulong> seen))
                {
                    seen = new HashSet<ulong>();
                    _inkerDiscovered[writing.Id] = seen;
                }

                if (seen.Contains(id) || !TryGetWritingPose(writing, out Vector3 center, out Quaternion rotation))
                    continue;
                if (!IsWritingVisible(settings, eye, forward, center, rotation, player.transform))
                    continue;

                seen.Add(id);
                if (writing.Command != InkerCommand.None)
                {
                    watch.Turn.Reset();
                    boundCommand = true;
                }

                if (writing.Source == InkerPhenomenon.DoorPeek && settings.DoorWritingSanityDecrease > 0)
                    player.ServerApplyDecrease(settings.DoorWritingSanityDecrease);
            }

            return boundCommand;
        }

        private void ServerInkerCheckViolation(GhostInkerSettings settings, SanityNetworkState player, ulong id,
            InkerActions actions)
        {
            _inkerRemoveIds.Clear();
            InkerCommand violated = InkerCommand.None;
            for (int i = 0; i < _inkerWritings.Count; i++)
            {
                InkerWritingState writing = _inkerWritings[i];
                if (!writing.Complete)
                    continue;
                bool broke = InkerCommandRules.Breaks(writing.Command, actions);
                if (!broke || !_inkerDiscovered.TryGetValue(writing.Id, out HashSet<ulong> seen) || !seen.Contains(id))
                    continue;
                _inkerRemoveIds.Add(writing.Id);
                violated = writing.Command;
            }

            if (_inkerRemoveIds.Count == 0)
                return;

            // 닦지 않은 상태에서 명령 위반 → 정신력 10% 감소 → 귀신이 화냄(갑툭튀) → 글씨 자동 제거(원문 §3).
            foreach (int writingId in _inkerRemoveIds)
                ServerRemoveWriting(writingId);
            if (settings.ViolationSanityDecrease > 0)
                player.ServerApplyDecrease(settings.ViolationSanityDecrease);
            _inkerViolationCount++;
            _inkerLastEvent = $"명령 위반 '{InkerCommandText.Of(violated)}' (client {id}) → 정신력 −{settings.ViolationSanityDecrease}, 갑툭튀";
            InkerJumpScareRpc(RpcTarget.Single(id, RpcTargetUse.Temp));
        }

        // ───────────────────────── 글씨 목록 ─────────────────────────

        private int ServerAddWriting(GhostInkerSettings settings, InkerCommand command, InkerPhenomenon source,
            ulong anchorId, Vector3 localPosition, Quaternion localRotation, float width, float height,
            float letterHeight, float wrapWidth, float revealSeconds)
        {
            while (_inkerWritings.Count >= InkerMaxWritings)
            {
                _inkerDiscovered.Remove(_inkerWritings[0].Id);
                _inkerWritings.RemoveAt(0);
            }

            int id = _inkerNextWritingId++;
            byte strokes = (byte)settings.WritingWipeStrokes;
            _inkerWritings.Add(new InkerWritingState
            {
                Id = id,
                Command = command,
                Source = source,
                AnchorId = anchorId,
                LocalPosition = localPosition,
                LocalRotation = localRotation,
                Width = width,
                Height = height,
                LetterHeight = letterHeight,
                WrapWidth = wrapWidth,
                RevealStartTime = InkerServerNow(),
                RevealSeconds = Mathf.Max(0.05f, revealSeconds),
                Complete = false,
                WipeRemaining = strokes,
                WipeRequired = strokes,
            });
            _inkerDiscovered[id] = new HashSet<ulong>();
            return id;
        }

        private void ServerCompleteWriting(int id)
        {
            if (!TryFindWriting(id, out InkerWritingState writing, out int index) || writing.Complete)
                return;
            writing.Complete = true;
            _inkerWritings[index] = writing;
        }

        private void ServerRemoveWriting(int id)
        {
            if (id == 0 || !TryFindWriting(id, out _, out int index))
                return;
            _inkerWritings.RemoveAt(index);
            _inkerDiscovered.Remove(id);
        }

        private bool TryFindWriting(int id, out InkerWritingState writing, out int index)
        {
            if (id != 0 && _inkerWritings != null)
            {
                for (int i = 0; i < _inkerWritings.Count; i++)
                {
                    if (_inkerWritings[i].Id != id)
                        continue;
                    writing = _inkerWritings[i];
                    index = i;
                    return true;
                }
            }

            writing = default;
            index = -1;
            return false;
        }

        private bool IsWritingNear(Vector3 point, float spacing)
        {
            for (int i = 0; i < _inkerWritings.Count; i++)
            {
                if (TryGetWritingPose(_inkerWritings[i], out Vector3 center, out _)
                    && (center - point).sqrMagnitude < spacing * spacing)
                    return true;
            }

            return false;
        }

        private bool TryGetWritingPose(in InkerWritingState writing, out Vector3 position, out Quaternion rotation)
            => InkerWritingGeometry.TryGetWorldPose(writing, ResolveInkerAnchor(writing.AnchorId), out position, out rotation);

        private Transform ResolveInkerAnchor(ulong id)
        {
            if (id == 0 || NetworkManager == null || NetworkManager.SpawnManager == null)
                return null;
            return NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject anchor) && anchor != null
                ? anchor.transform
                : null;
        }

        // ───────────────────────── 대걸레 닦기 (ICommandWritingService) ─────────────────────────

        public bool TryRaycast(Vector3 origin, Vector3 direction, float distance, Transform ignoredRoot, out int writingId)
        {
            writingId = 0;
            if (!IsSpawned || _inkerWritings == null || direction.sqrMagnitude < 1e-6f)
                return false;

            direction.Normalize();
            float best = float.MaxValue;
            for (int i = 0; i < _inkerWritings.Count; i++)
            {
                InkerWritingState writing = _inkerWritings[i];
                if (!writing.Complete || !TryGetWritingPose(writing, out Vector3 center, out Quaternion rotation))
                    continue;
                if (InkerWritingGeometry.RaycastRect(origin, direction, distance, center, rotation,
                        writing.Width, writing.Height, out float hit) && hit < best)
                {
                    best = hit;
                    writingId = writing.Id;
                }
            }

            if (writingId == 0)
                return false;

            // 글씨 앞을 다른 물체가 가리면 닦지 못한다.
            int count = Physics.RaycastNonAlloc(origin, direction, _inkerHits, Mathf.Max(0f, best - 0.03f),
                GameLayers.NonGhostPrototypeRaycastMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform blocker = _inkerHits[i].transform;
                if (ignoredRoot != null && (blocker == ignoredRoot || blocker.IsChildOf(ignoredRoot)))
                    continue;
                writingId = 0;
                return false;
            }

            return true;
        }

        public bool ServerTryWipe(int writingId, Vector3 origin, Vector3 direction, float distance, Transform ignoredRoot)
        {
            if (!IsServer || !TryRaycast(origin, direction, distance, ignoredRoot, out int hitId) || hitId != writingId
                || !TryFindWriting(writingId, out InkerWritingState writing, out int index))
                return false;

            if (writing.WipeRemaining > 1)
            {
                writing.WipeRemaining--;
                _inkerWritings[index] = writing;
                return true;
            }

            // 전부 닦으면 명령이 해제된다 — 무시해도 된다(원문 §3).
            ServerRemoveWriting(writingId);
            _inkerLastEvent = $"글씨 '{writing.Text}' 를 다 닦음 — 명령 해제";
            return true;
        }

        // ───────────────────────── 판정 도우미 ─────────────────────────

        private InkerWatch GetInkerWatch(ulong id)
        {
            if (!_inkerWatch.TryGetValue(id, out InkerWatch watch))
            {
                watch = new InkerWatch();
                _inkerWatch[id] = watch;
            }

            return watch;
        }

        /// <summary>[임시 해석 IK-9] '방에 들어가 있는' 플레이어 = 집 안·세이프 존 밖·은신하지 않은 생존자.</summary>
        private bool IsInkerCandidate(SanityNetworkState player) => CanChasePlayer(player);

        private bool AnyLivingPlayerWithin(Vector3 point, float radius, int playerCount)
        {
            for (int i = 0; i < playerCount; i++)
            {
                SanityNetworkState player = _players[i];
                if (player == null || !player.IsSpawned || !player.HasSanity)
                    continue;
                Vector3 offset = player.transform.position - point;
                if (Mathf.Abs(offset.y) > 2f)
                    continue;
                offset.y = 0f;
                if (offset.sqrMagnitude <= radius * radius)
                    return true;
            }

            return false;
        }

        /// <summary>서버에서 본 플레이어 카메라: 몸 방향(요) + 복제된 피치, 복제된 카메라 높이.</summary>
        private static void GetInkerPlayerView(SanityNetworkState player, out Vector3 eye, out Vector3 forward, out float yaw)
        {
            Transform root = player.transform;
            yaw = root.eulerAngles.y;
            float height = InkerDefaultEyeHeight;
            float pitch = 0f;
            if (player.TryGetComponent(out PlayerMotor motor) && motor.CameraLocalHeight > 0.2f)
                height = motor.CameraLocalHeight;
            if (player.TryGetComponent(out PlayerLook look))
                pitch = look.Pitch;
            eye = root.position + Vector3.up * height;
            forward = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
        }

        private static bool IsWritingVisible(GhostInkerSettings settings, Vector3 eye, Vector3 forward, Vector3 center,
            Quaternion rotation, Transform viewer)
        {
            Vector3 toWriting = center - eye;
            float distance = toWriting.magnitude;
            if (distance < 0.01f || distance > settings.DiscoverDistance)
                return false;
            if (!InkerWritingGeometry.IsInFront(eye, center, rotation)
                || Vector3.Angle(forward, toWriting) > settings.DiscoverAngle)
                return false;
            return GhostPerception.HasLineOfSight(eye, center + rotation * Vector3.forward * 0.05f, viewer);
        }

        /// <summary>시야선 — <paramref name="ignored"/>(문짝) 와 보는 사람 자신은 가리지 않는 것으로 본다.</summary>
        private bool HasInkerLineOfSight(Vector3 from, Vector3 to, Transform viewer, Transform ignored)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 1e-3f)
                return true;
            int count = Physics.RaycastNonAlloc(from, delta / distance, _inkerHits, distance,
                GameLayers.NonGhostPrototypeRaycastMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform hit = _inkerHits[i].transform;
                if (viewer != null && (hit == viewer || hit.IsChildOf(viewer)))
                    continue;
                if (ignored != null && (hit == ignored || hit.IsChildOf(ignored)))
                    continue;
                return false;
            }

            return true;
        }

        /// <summary>문짝 충돌체의 문 기준 로컬 경계. 문이 돌아도 변하지 않는다.</summary>
        private static bool TryGetDoorShape(DoorInteractable door, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            Transform root = door.transform;
            foreach (Collider collider in door.GetComponentsInChildren<Collider>(true))
            {
                if (collider.isTrigger)
                    continue;
                if (collider is BoxCollider box)
                {
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var sign = new Vector3((corner & 1) == 0 ? -0.5f : 0.5f, (corner & 2) == 0 ? -0.5f : 0.5f,
                            (corner & 4) == 0 ? -0.5f : 0.5f);
                        Vector3 world = box.transform.TransformPoint(box.center + Vector3.Scale(box.size, sign));
                        Encapsulate(ref bounds, ref any, root.InverseTransformPoint(world));
                    }
                }
                else
                {
                    Bounds world = collider.bounds;
                    Encapsulate(ref bounds, ref any, root.InverseTransformPoint(world.min));
                    Encapsulate(ref bounds, ref any, root.InverseTransformPoint(world.max));
                }
            }

            return any && bounds.size.x > 0.2f && bounds.size.y > 0.5f;
        }

        private static void Encapsulate(ref Bounds bounds, ref bool any, Vector3 point)
        {
            if (!any)
            {
                bounds = new Bounds(point, Vector3.zero);
                any = true;
                return;
            }

            bounds.Encapsulate(point);
        }

        /// <summary>문 로컬 점을 <b>닫힌 상태</b>의 월드 점으로 바꾼다.</summary>
        private static Vector3 DoorWorld(DoorInteractable door, Vector3 local)
            => door.transform.position + door.ClosedWorldRotation * local;

        private static float YawTowards(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            delta.y = 0f;
            return delta.sqrMagnitude < 1e-6f ? 0f : Quaternion.LookRotation(delta).eulerAngles.y;
        }

        // ───────────────────────── 디버그(Hub) ─────────────────────────

        /// <summary>Hub — 인커 현상 하나를 확률·횟수·주기와 무관하게 바로 시작한다(문은 열려 있어도 닫고 시작).</summary>
        public string ServerForceInkerPhenomenon(InkerPhenomenon kind)
        {
            GhostInkerSettings settings = InkerSettings;
            if (!IsServer || !_serverReady)
                return "Local Host(서버)가 필요합니다.";
            if (settings == null)
                return "지금 귀신은 인커가 아닙니다 — 귀신 종류에서 인커를 고르세요.";
            if (_phase.Value == GhostPhase.Attack)
                return "어택 중에는 인커 현상이 나오지 않습니다(원문 — 어택 타임이 아닌 상태).";

            ServerCancelInkerRun("Hub 재시작");
            int playerCount = _sanity.CopyPlayerStates(_players);
            bool started = kind == InkerPhenomenon.DoorPeek
                ? ServerTryStartDoorPeek(settings, playerCount, true, out string reason)
                : ServerTryStartWallWriting(settings, playerCount, true, out reason);
            return started ? $"인커 현상 시작: {_inkerLastEvent}" : $"인커 현상 실패: {reason}";
        }

        /// <summary>Hub — 진행 중 현상을 끊고 남은 글씨를 모두 지운다.</summary>
        public void ServerClearInkerWritings()
        {
            if (!IsServer || _inkerWritings == null)
                return;
            ServerCancelInkerRun("Hub 지우기");
            _inkerWritings.Clear();
            _inkerDiscovered.Clear();
            _inkerLastEvent = "글씨를 모두 지움";
        }

        /// <summary>Hub 요약(서버).</summary>
        public string InkerSummary
        {
            get
            {
                GhostInkerSettings settings = InkerSettings;
                if (settings == null || !IsServer)
                    return "-";

                var builder = new StringBuilder(160);
                builder.Append("문틈 ").Append(_inkerDoorPeekCount).Append('/').Append(settings.DoorPeekMaxCount)
                    .Append(" · 벽 ").Append(_inkerWallWritingCount).Append('/').Append(settings.WallWritingMaxCount)
                    .Append(" · 위반 ").Append(_inkerViolationCount)
                    .Append(" · 다음 판정 ").Append(Mathf.Max(0f, _inkerCheckRemaining).ToString("0")).Append('s');
                if (_inkerRun.IsRunning)
                    builder.Append("\n진행: ").Append(_inkerRun.Kind).Append(" / ").Append(_inkerRun.Step);
                for (int i = 0; i < _inkerWritings.Count; i++)
                {
                    InkerWritingState writing = _inkerWritings[i];
                    int seen = _inkerDiscovered.TryGetValue(writing.Id, out HashSet<ulong> set) ? set.Count : 0;
                    builder.Append("\n#").Append(writing.Id).Append(' ').Append(writing.Text)
                        .Append(writing.Complete ? string.Empty : " (쓰는 중)")
                        .Append(" · 닦기 ").Append(writing.WipeRequired - writing.WipeRemaining).Append('/')
                        .Append(writing.WipeRequired).Append(" · 발견 ").Append(seen).Append("명");
                }

                builder.Append("\n최근: ").Append(_inkerLastEvent);
                return builder.ToString();
            }
        }

        // ───────────────────────── 피어 연출 ─────────────────────────

        [Rpc(SendTo.SpecifiedInParams)]
        private void InkerJumpScareRpc(RpcParams rpcParams)
        {
            GhostInkerSettings settings = InkerSettings;
            _inkerScareRemaining = settings != null ? settings.JumpScareSeconds : 0.8f;
            InkerSounds.Play2D(InkerSounds.Scream, 0.9f);
            if (settings != null && settings.AtmosphereEnabled)
                EnsureInkerAtmosphere(settings).Shock(1f, _inkerScareRemaining + 0.4f);
        }

        private void TickInkerVisual()
        {
            GhostInkerSettings settings = InkerSettings;
            if (!IsSpawned || settings == null)
                return;

            double now = InkerServerNow();
            if (_inkerViewsDirty)
                SyncInkerWritingViews();
            foreach (InkerWritingView view in _inkerWritingViews.Values)
            {
                if (view != null)
                    view.Refresh(ResolveInkerAnchor(view.State.AnchorId), now);
            }

            TickInkerPuppet(settings, now);
            TickInkerScare();
            TickInkerAtmosphere(settings, now);
        }

        private InkerAtmosphere EnsureInkerAtmosphere(GhostInkerSettings settings)
        {
            if (_inkerAtmosphere == null)
                _inkerAtmosphere = InkerAtmosphere.Create();
            return _inkerAtmosphere;
        }

        /// <summary>
        /// 화면 분위기: 현상이 진행 중이고 내 카메라가 현상 가까이 있을수록 진하게, 단계마다 세기를 바꾸고
        /// 문이 쾅 닫힘·눈 맞춤(고개 기울임) 순간에 충격을 준다. 각 피어의 로컬 연출이다.
        /// </summary>
        private void TickInkerAtmosphere(GhostInkerSettings settings, double now)
        {
            InkerSequenceState sequence = _inkerSequence.Value;
            float target = 0f;
            Camera viewCamera = settings.AtmosphereEnabled ? ResolveInkerLocalCamera() : null;
            if (sequence.IsRunning && viewCamera != null)
            {
                Vector3 focus = sequence.PuppetPosition + sequence.LeanOffset + Vector3.up * InkerFaceHeight;
                float distance = Vector3.Distance(viewCamera.transform.position, focus);
                float proximity = 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(settings.AtmosphereNearDistance, settings.AtmosphereFarDistance, distance));
                float t = Mathf.Max(0f, (float)(now - sequence.StepStartTime));
                target = proximity * InkerStepMood(sequence.Step, t, settings.Timings);

                if (sequence.Step != _inkerAtmosphereStep)
                {
                    _inkerAtmosphereStep = sequence.Step;
                    float shock = sequence.Step switch
                    {
                        InkerStep.DoorTilt => 0.45f,
                        InkerStep.DoorSlam => 0.85f,
                        _ => 0f,
                    };
                    // 늦게 들어온 피어는 지난 충격을 다시 받지 않는다.
                    if (shock > 0f && t < 0.4f && proximity > 0.01f)
                        EnsureInkerAtmosphere(settings).Shock(shock * proximity);
                }
            }
            else
            {
                _inkerAtmosphereStep = InkerStep.None;
            }

            if (_inkerScareRemaining > 0f)
                target = 1f;
            if (_inkerAtmosphere == null && target <= 0f)
                return;
            EnsureInkerAtmosphere(settings).SetTarget(target, settings.AtmosphereEnabled ? settings.AtmosphereStrength : 0f);
        }

        /// <summary>단계별 분위기 세기(0~1) — 다가올수록 조여 오고, 귀신과 눈이 마주치는 순간이 가장 진하다. 벽 글씨는 써질수록 진해진다.</summary>
        internal static float InkerStepMood(InkerStep step, float elapsed, InkerTimings timings)
        {
            static float Ramp(float from, float to, float t, float duration)
                => Mathf.Lerp(from, to, Mathf.Clamp01(t / Mathf.Max(0.01f, duration)));

            return step switch
            {
                InkerStep.DoorPrepare => 0.3f,
                InkerStep.DoorOpening => Ramp(0.35f, 0.6f, elapsed, timings.DoorOpen),
                InkerStep.DoorLean => Ramp(0.6f, 0.8f, elapsed, timings.DoorLean),
                InkerStep.DoorWatching => 0.85f,
                InkerStep.DoorTilt => 1f,
                InkerStep.DoorSlam => 1f,
                InkerStep.DoorScratch => Ramp(0.8f, 0.45f, elapsed, timings.DoorScratch),
                InkerStep.WallWriting => Ramp(0.5f, 0.8f, elapsed, timings.WallWrite),
                _ => 0f,
            };
        }

        private void SyncInkerWritingViews()
        {
            _inkerViewsDirty = false;
            _inkerStaleViewIds.Clear();
            foreach (int id in _inkerWritingViews.Keys)
                _inkerStaleViewIds.Add(id);

            for (int i = 0; i < _inkerWritings.Count; i++)
            {
                InkerWritingState writing = _inkerWritings[i];
                _inkerStaleViewIds.Remove(writing.Id);
                if (_inkerWritingViews.TryGetValue(writing.Id, out InkerWritingView view) && view != null)
                    view.Apply(writing);
                else
                    _inkerWritingViews[writing.Id] = InkerWritingView.Create(writing, InkerSettings?.WritingMaterial);
            }

            foreach (int id in _inkerStaleViewIds)
            {
                if (_inkerWritingViews.TryGetValue(id, out InkerWritingView view) && view != null)
                    Destroy(view.gameObject);
                _inkerWritingViews.Remove(id);
            }
        }

        private void TickInkerPuppet(GhostInkerSettings settings, double now)
        {
            InkerSequenceState sequence = _inkerSequence.Value;
            if (!sequence.IsRunning)
            {
                if (_inkerPuppet != null)
                    _inkerPuppet.Hide();
                if (_inkerLoopAudio != null)
                {
                    InkerSounds.Stop(_inkerLoopAudio);
                    _inkerLoopAudio = null;
                }

                _inkerPlayedStep = InkerStep.None;
                return;
            }

            float t = Mathf.Max(0f, (float)(now - sequence.StepStartTime));
            if (sequence.Step != _inkerPlayedStep)
            {
                PlayInkerStepSound(sequence, t);
                _inkerPlayedStep = sequence.Step;
            }

            if (sequence.Kind == InkerPhenomenon.WallWriting)
            {
                // 현상 2는 귀신 모습이 없다 — 허공이 쓰듯 피 글씨만 저절로 써진다(사용자 요청 2026-11-16).
                // 젖은 문지름 소리는 지금 써지는 손끝을 따라 옮겨 간다.
                if (_inkerPuppet != null)
                    _inkerPuppet.Hide();
                if (_inkerLoopAudio != null && _inkerWritingViews.TryGetValue(sequence.WritingId, out InkerWritingView writing)
                    && writing != null && writing.TryGetTipWorld(out Vector3 tip))
                    _inkerLoopAudio.transform.position = tip;
                return;
            }

            if (_inkerPuppet == null)
                _inkerPuppet = InkerPuppetView.Create(_body, "InkerPuppet");
            if (_inkerPuppet == null)
                return;

            InkerTimings timings = settings.Timings;
            Vector3 lean = sequence.PuppetPosition + sequence.LeanOffset;
            switch (sequence.Step)
            {
                case InkerStep.DoorLean:
                {
                    float f = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.01f, timings.DoorLean));
                    _inkerPuppet.Pose(sequence.PuppetPosition + sequence.LeanOffset * f, sequence.FaceYaw, 0f, 0f, f);
                    break;
                }
                case InkerStep.DoorWatching:
                    _inkerPuppet.Pose(lean, sequence.FaceYaw, 0f, 0f, 1f);
                    break;
                case InkerStep.DoorTilt:
                {
                    float f = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.01f, timings.DoorTilt));
                    _inkerPuppet.Pose(lean, sequence.FaceYaw, 0f, settings.DoorPeekTiltDegrees * f, 1f);
                    break;
                }
                case InkerStep.DoorSlam:
                {
                    float f = Mathf.Clamp01(t / Mathf.Max(0.01f, timings.DoorSlam * 0.6f));
                    _inkerPuppet.Pose(lean - sequence.LeanOffset * f, sequence.FaceYaw, 0f, 0f, 1f - f);
                    break;
                }
                default:
                    _inkerPuppet.Hide();
                    break;
            }
        }

        private void PlayInkerStepSound(InkerSequenceState sequence, float elapsed)
        {
            if (_inkerLoopAudio != null)
            {
                InkerSounds.Stop(_inkerLoopAudio);
                _inkerLoopAudio = null;
            }

            // 늦게 들어온 피어가 지난 소리를 다시 틀지 않게, 막 시작한 단계만 소리를 낸다.
            if (elapsed > 0.5f)
                return;

            Transform door = ResolveInkerAnchor(sequence.DoorId);
            Vector3 doorPoint = door != null ? door.position + Vector3.up * 1.2f : sequence.PuppetPosition;
            Vector3 head = sequence.PuppetPosition + sequence.LeanOffset + Vector3.up * InkerFaceHeight;
            switch (sequence.Step)
            {
                case InkerStep.DoorOpening:
                    InkerSounds.PlayAt(InkerSounds.Creak, doorPoint, 0.8f);
                    break;
                case InkerStep.DoorSlam:
                    InkerSounds.PlayAt(InkerSounds.Slam, doorPoint, 1f);
                    break;
                case InkerStep.DoorScratch:
                    InkerSounds.PlayAt(InkerSounds.Scratch, doorPoint, 0.9f);
                    break;
                case InkerStep.WallWriting:
                    _inkerLoopAudio = InkerSounds.PlayAt(InkerSounds.Write, head, 0.8f, loop: true);
                    break;
            }
        }

        private void TickInkerScare()
        {
            if (_inkerScareRemaining <= 0f)
                return;

            _inkerScareRemaining -= Time.deltaTime;
            Camera viewCamera = ResolveInkerLocalCamera();
            if (_inkerScareRemaining <= 0f || viewCamera == null)
            {
                _inkerScareRemaining = 0f;
                if (_inkerScarePuppet != null)
                    _inkerScarePuppet.Hide();
                return;
            }

            GhostInkerSettings settings = InkerSettings;
            if (_inkerScarePuppet == null)
            {
                _inkerScarePuppet = InkerPuppetView.Create(_body, "InkerJumpScare");
                if (_inkerScarePuppet == null)
                    return;
                _inkerScarePuppet.MaxOpacity = 0.95f;
                // 어두운 집에서도 보이게 몸을 스스로 빛나게 하고 얼굴 앞에 점광원을 단다(사용자 요청 2026-11-16).
                if (settings != null)
                    _inkerScarePuppet.EnableGlow(settings.JumpScareEmission, settings.JumpScareLightColor,
                        settings.JumpScareLightIntensity, settings.JumpScareLightRange, InkerFaceHeight);
            }

            // 귀신이 화낸다(갑툭튀, IK-6 임시 해석): 눈앞(기본 0.7m)에 떨리는 얼굴을 띄운다. 벽·가구가 더 가까우면 그 앞에 띄워
            // 몸이 벽에 묻혀 안 보이는 일이 없게 한다.
            Transform view = viewCamera.transform;
            Vector3 flat = view.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f)
                flat = view.up;
            flat.Normalize();
            float distance = InkerJumpScareDistance(view.position, flat, settings != null ? settings.JumpScareDistance : 0.7f);
            Vector3 jitter = Random.insideUnitSphere * 0.03f;
            Vector3 position = view.position + flat * distance - Vector3.up * InkerFaceHeight + jitter;
            _inkerScarePuppet.SetGlowFlicker(0.75f + 0.5f * Mathf.PerlinNoise(Time.time * 17f, 0.37f));
            _inkerScarePuppet.Pose(position, Quaternion.LookRotation(-flat).eulerAngles.y, 0f, Random.Range(-8f, 8f), 1f);
        }

        /// <summary>갑툭튀 거리 — 눈앞 <paramref name="preferred"/>m, 그 사이에 벽·가구가 있으면 그 앞(최소 0.3m).</summary>
        private float InkerJumpScareDistance(Vector3 eye, Vector3 direction, float preferred)
        {
            const float clearance = 0.25f;
            int mask = GameLayers.NonGhostPrototypeRaycastMask & ~(GameLayers.Player >= 0 ? 1 << GameLayers.Player : 0);
            if (Physics.Raycast(eye, direction, out RaycastHit hit, preferred + clearance, mask, QueryTriggerInteraction.Ignore))
                return Mathf.Clamp(hit.distance - clearance, 0.3f, preferred);
            return preferred;
        }

        private Camera ResolveInkerLocalCamera()
        {
            if (_localCamera == null && _localViewer != null)
            {
                PlayerLook look = _localViewer.GetComponent<PlayerLook>();
                _localCamera = look != null ? look.PlayerCamera : null;
            }

            return _localCamera != null && _localCamera.isActiveAndEnabled ? _localCamera : Camera.main;
        }
    }
}
