using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Sanity;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// '박치기 공룡' 귀신(project/ghost-dino.md)의 서버 이동·충돌·피해와 예고 연출. 사이클 시간 계산은
    /// <see cref="GhostDinoCharge"/>(순수 C#)가 하고, 여기서는 그 단계에 맞춰 귀신을 움직인다.
    /// 어택 중에만 돈다 — <see cref="ServerTickAttack"/> 맨 앞에서 <see cref="ServerTickDino"/> 가 준비·돌진·스턴 단계를
    /// 맡으면 기존 추격·포획 틱은 건너뛴다(추격 단계에서는 기존 어택 규칙 그대로).
    /// </summary>
    public sealed partial class GhostPrototypeController
    {
        // 한 번에 이만큼(초)보다 길게 이동하지 않는다 — 프레임이 길어져도 플레이어·얇은 벽을 건너뛰지 않게 쪼갠다.
        private const float DinoMaxSubStep = 0.05f;
        // 부딪힘이 기록됐고 돌진 방향으로 의도한 이동의 이 비율보다 못 갔으면 막힌 것이다(계단 모서리를 올라타는 경우는 거의 다 간다).
        private const float DinoBlockedRatio = 0.8f;
        // 부딪힘이 기록됐고 돌진 방향 옆으로 의도한 이동의 이 비율보다 밀렸으면 벽을 스친 것이다 — 미끄러져 꺾이지 않고 거기서 끝낸다
        // (2026-11-09 사용자 보고 "직선이 아니다": 25° 벽을 스치면 앞으로는 82% 가서 위 비율을 통과하고 벽을 타고 휘었다).
        private const float DinoDeflectRatio = 0.15f;
        // 부딪힘 보고 없이도 의도한 이동의 이 비율보다 못 가는 상태가 이 시간(초) 이어지면 알 수 없는 장애물에 막힌 것이다.
        private const float DinoStuckRatio = 0.2f;
        private const float DinoStuckSeconds = 0.3f;
        // 법선 y 가 이 값 이내인 접촉만 '옆으로 부딪힘'이다(바닥·경사로·천장은 제외).
        private const float DinoSideNormalLimit = 0.5f;

        private readonly NetworkVariable<byte> _dinoStage = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private GhostDinoCharge _dino;
        private SanityNetworkState _dinoTarget;
        private Vector3 _dinoTargetPosition;
        private bool _dinoHasTarget;
        private Vector3 _dinoDirection;
        private bool _dinoChargeBegan;
        private float _dinoChargeDistance;
        private float _dinoChargeElapsed;
        private float _dinoStuckElapsed;
        private bool _dinoHitRecorded;
        private Collider _dinoHitCollider;
        private int _dinoPlayerCount;

        /// <summary>박치기 귀신 설정 — 설정이 <see cref="GhostDinoSettings"/>(기본 귀신 설정을 상속)일 때만 박치기를 돈다(ADR-0024).</summary>
        private GhostDinoSettings DinoSettings => _settings as GhostDinoSettings;

        /// <summary>준비·돌진 중이면 어택 시간이 끝나도 어택을 끝내지 않는다(돌진까지 마친 뒤 종료).</summary>
        private bool DinoHoldsAttackEnd => _dino != null && _dino.IsCommitted;

        /// <summary>F1 접속 HUD(서버) — 박치기 단계와 경과. 기믹이 꺼졌거나 어택이 아니면 "-".</summary>
        public string DinoSummary
        {
            get
            {
                if (_dino == null || _phase.Value != GhostPhase.Attack)
                    return "-";
                return _dino.Current switch
                {
                    GhostDinoCharge.Stage.Chase => $"추격 {_dino.Elapsed:F1}/{_dino.ChaseSeconds:F0}s"
                        + (_dinoHasTarget ? string.Empty : " · 타깃 없음(찾을 때까지 준비 대기)"),
                    GhostDinoCharge.Stage.Prepare => $"준비 {_dino.Elapsed:F1}/{_dino.PrepareSeconds:F0}s"
                        + (_dino.IsDirectionLocked ? " · 방향 고정" : " · 방향 조정"),
                    GhostDinoCharge.Stage.Charge => $"돌진 {_dinoChargeDistance:F1}m",
                    GhostDinoCharge.Stage.Stun => $"스턴 {_dino.Elapsed:F1}/{_dino.StunSeconds:F0}s",
                    _ => "-",
                };
            }
        }

        private void DinoSpawn()
        {
            _dinoStage.OnValueChanged += HandleDinoStageChanged;
        }

        private void DinoDespawn()
        {
            _dinoStage.OnValueChanged -= HandleDinoStageChanged;
            _dino = null;
            _dinoTarget = null;
            _dinoHasTarget = false;
            _dinoChargeBegan = false;
            _dinoHitCollider = null;
            _dinoHitRecorded = false;
        }

        /// <summary>어택에 들어갈 때와 나갈 때(그리고 기믹을 끌 때) 사이클을 처음으로 되돌린다. 서버 전용.</summary>
        private void DinoReset()
        {
            _dino?.Reset();
            _dinoTarget = null;
            _dinoHasTarget = false;
            _dinoDirection = Vector3.zero;
            _dinoChargeBegan = false;
            _dinoChargeDistance = 0f;
            _dinoChargeElapsed = 0f;
            _dinoStuckElapsed = 0f;
            _dinoHitRecorded = false;
            _dinoHitCollider = null;
            if (_machine != null)
                _machine.HoldAttackEnd = false;
            if (IsServer && IsSpawned)
                SetDinoStage(GhostDinoCharge.Stage.Chase);
        }

        /// <returns>준비·돌진·스턴 단계가 이번 틱의 움직임을 맡았으면 true — 기존 어택 틱을 건너뛴다.</returns>
        private bool ServerTickDino(float deltaTime, int playerCount)
        {
            GhostDinoSettings settings = DinoSettings;
            if (settings == null)
            {
                if (_dino != null)
                {
                    DinoReset();
                    _dino = null;
                }

                return false;
            }

            _dinoPlayerCount = playerCount;
            _dino ??= new GhostDinoCharge();
            _dino.ChaseSeconds = settings.DinoChaseSeconds;
            _dino.PrepareSeconds = settings.DinoPrepareSeconds;
            _dino.DirectionLockSeconds = settings.DinoDirectionLockSeconds;
            _dino.StunSeconds = settings.DinoStunSeconds;

            RememberDinoTarget();
            GhostDinoCharge.Stage before = _dino.Current;
            _dino.Tick(deltaTime, _dinoHasTarget);
            if (_dino.Current != before)
                EnterDinoStage(_dino.Current);
            SetDinoStage(_dino.Current);

            switch (_dino.Current)
            {
                case GhostDinoCharge.Stage.Prepare:
                    TickDinoPrepare(deltaTime);
                    return true;

                case GhostDinoCharge.Stage.Charge:
                    TickDinoCharge(deltaTime);
                    return true;

                case GhostDinoCharge.Stage.Stun:
                    DinoGroundStep(deltaTime);
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// 추격 동안 마지막으로 타깃팅한 플레이어의 위치를 기억한다. 그 플레이어가 죽거나 은신해도, 심지어 접속이 끊겨도
        /// 마지막 위치를 그대로 쓴다(기획 §2.1 "기존 타깃 및 방향 결정 규칙 유지").
        /// </summary>
        private void RememberDinoTarget()
        {
            if (_target != null)
                _dinoTarget = _target;

            if (_dinoTarget != null)
            {
                _dinoTargetPosition = _dinoTarget.transform.position;
                _dinoHasTarget = true;
            }
        }

        private void EnterDinoStage(GhostDinoCharge.Stage stage)
        {
            switch (stage)
            {
                case GhostDinoCharge.Stage.Prepare:
                    _dinoDirection = Vector3.zero;
                    break;

                case GhostDinoCharge.Stage.Charge:
                    _dinoChargeBegan = false;
                    break;
            }
        }

        /// <summary>
        /// 제자리에 서서 앞 구간(기본 1.3초)에는 타깃 위치를 따라 방향을 조정하고, 뒤 구간(0.7초)에는 방향을 고정한다.
        /// 고정된 뒤에도 몸은 그 방향으로 돌고 있을 수 있다(회전 속도) — 돌진 방향은 고정 시점의 값이다.
        /// </summary>
        private void TickDinoPrepare(float deltaTime)
        {
            if (!_dino.IsDirectionLocked || _dinoDirection.sqrMagnitude < 1e-4f)
            {
                Vector3 toTarget = _dinoTargetPosition - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 1e-4f)
                    _dinoDirection = toTarget.normalized;
            }

            if (_dinoDirection.sqrMagnitude < 1e-4f)
            {
                _dinoDirection = transform.forward;
                _dinoDirection.y = 0f;
                _dinoDirection = _dinoDirection.sqrMagnitude < 1e-4f ? Vector3.forward : _dinoDirection.normalized;
            }

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(_dinoDirection, Vector3.up),
                DinoSettings.DinoTurnSpeedDegrees * deltaTime);
            DinoGroundStep(deltaTime);
        }

        /// <summary>정지 상태에서도 바닥에 붙어 있게 중력만 적용한다.</summary>
        private void DinoGroundStep(float deltaTime)
        {
            if (deltaTime > 0f)
                _controller.Move(Vector3.down * _settings.Gravity * deltaTime);
        }

        private void TickDinoCharge(float deltaTime)
        {
            if (!_dinoChargeBegan)
            {
                _dinoChargeBegan = true;
                _dinoChargeDistance = 0f;
                _dinoChargeElapsed = 0f;
                _dinoStuckElapsed = 0f;
                if (_dinoDirection.sqrMagnitude < 1e-4f)
                    _dinoDirection = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            }

            float remaining = Mathf.Max(0f, deltaTime);
            while (remaining > 0f && _dino.Current == GhostDinoCharge.Stage.Charge)
            {
                float step = Mathf.Min(remaining, DinoMaxSubStep);
                remaining -= step;
                if (DinoChargeStep(step))
                    return;
            }
        }

        /// <returns>이번 스텝에서 충돌로 돌진이 끝났으면 true.</returns>
        private bool DinoChargeStep(float step)
        {
            _dinoChargeElapsed += step;
            Vector3 before = transform.position;
            float intended = DinoSettings.DinoChargeSpeed * step;

            _dinoHitRecorded = false;
            _dinoHitCollider = null;
            Vector3 move = _dinoDirection * intended;
            move.y = -_settings.Gravity * step;
            _controller.Move(move);

            Vector3 delta = transform.position - before;
            delta.y = 0f;
            _dinoChargeDistance += delta.magnitude;
            // 앞으로 간 양과 옆으로 밀린 양을 나눠 본다 — 벽을 따라 미끄러지면 전체 이동은 커도 옆으로 밀린다.
            float moved = Vector3.Dot(delta, _dinoDirection);
            float deflected = (delta - _dinoDirection * moved).magnitude;

            // 플레이어는 귀신과 충돌 레이어가 꺼져 있으므로 거리로 판정한다. 가구·벽보다 먼저 본다.
            if (TryDinoHitPlayers())
            {
                EndDinoCharge(null);
                return true;
            }

            bool blocked = _dinoHitRecorded
                && (moved < intended * DinoBlockedRatio || deflected > intended * DinoDeflectRatio);
            if (!blocked)
            {
                if (moved < intended * DinoStuckRatio)
                {
                    _dinoStuckElapsed += step;
                    blocked = _dinoStuckElapsed >= DinoStuckSeconds;
                }
                else
                {
                    _dinoStuckElapsed = 0f;
                }
            }

            // 집 경계는 벽이다. 밖으로 나가면 되돌리고 끝낸다.
            if (!IsInsideHouseBounds(transform.position))
            {
                ClampToHouseBounds();
                blocked = true;
            }

            if (blocked)
            {
                FurnitureNetworkPhysics furniture = _dinoHitCollider != null
                    ? _dinoHitCollider.GetComponentInParent<FurnitureNetworkPhysics>()
                    : null;
                EndDinoCharge(furniture);
                return true;
            }

            if (_dinoChargeElapsed >= DinoSettings.DinoMaxChargeSeconds)
            {
                EndDinoCharge(null);
                return true;
            }

            return false;
        }

        /// <summary>돌진 중 귀신에 닿은 플레이어를 즉사시킨다(기획 §2.2). 은신·굴착·세이프 존의 플레이어는 닿지 않는다.</summary>
        private bool TryDinoHitPlayers()
        {
            bool hit = false;
            float radius = DinoSettings.DinoHitRadius;
            for (int i = 0; i < _dinoPlayerCount; i++)
            {
                SanityNetworkState player = _players[i];
                if (!CanChasePlayer(player))
                    continue;

                Vector3 offset = player.transform.position - transform.position;
                float vertical = Mathf.Abs(offset.y);
                offset.y = 0f;
                if (offset.magnitude > radius || vertical > GhostNavigation.ArrivalVerticalTolerance)
                    continue;

                hit = true;
                if (player.ServerMarkDead())
                {
                    Debug.Log(
                        $"[GhostPrototype] 박치기 돌진에 Client {player.OwnerClientId} 가 충돌해 사망했습니다 " +
                        $"(돌진 {_dinoChargeDistance:F1}m).",
                        this);
                }
            }

            return hit;
        }

        /// <summary>첫 충돌로 돌진을 끝낸다. 가구와 부딪혔으면 ⌊거리×5⌋(최소 1) 내구도를 깎고 스턴으로 들어간다.</summary>
        private void EndDinoCharge(FurnitureNetworkPhysics furniture)
        {
            if (furniture != null)
            {
                int damage = GhostDinoCharge.FurnitureDamage(_dinoChargeDistance, DinoSettings.DinoDamagePerMeter);
                int applied = furniture.ServerApplyDirectDamage(damage);
                Debug.Log(
                    $"[GhostPrototype] 박치기 돌진이 {furniture.name} 와 충돌 — {_dinoChargeDistance:F1}m, 내구도 -{applied}.",
                    this);
            }

            _dino.EndCharge();
            _dinoChargeBegan = false;
            _dinoHitRecorded = false;
            _dinoHitCollider = null;
            SetDinoStage(_dino.Current);
        }

        /// <summary>
        /// 캐릭터 컨트롤러가 이동 중 부딪힌 콜라이더를 기록한다(돌진 중에만). 플레이어 레이어는 충돌이 꺼져 있고
        /// 열린 문짝도 귀신과 무시 상태라 여기에 들어오지 않는다.
        /// </summary>
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (_dino == null || _dino.Current != GhostDinoCharge.Stage.Charge || _dinoHitRecorded
                || hit.collider == null || Mathf.Abs(hit.normal.y) > DinoSideNormalLimit)
            {
                return;
            }

            _dinoHitRecorded = true;
            _dinoHitCollider = hit.collider;
        }

        private void SetDinoStage(GhostDinoCharge.Stage stage)
        {
            if (!IsServer || !IsSpawned)
                return;

            byte value = (byte)stage;
            if (_dinoStage.Value != value)
                _dinoStage.Value = value;
        }

        // ---- 예고 연출(모든 피어) — 모델·소리가 없어 몸 조명으로만 알린다. 최종 연출은 아트 리소스 후 교체(BD-2). ----

        private void HandleDinoStageChanged(byte previous, byte current)
        {
            ApplyDinoVisual();
        }

        private void ApplyDinoVisual()
        {
            // 먼저 어택의 기본 연출(빨강)로 되돌린 뒤 단계별로 덮는다.
            ApplyPhaseVisual(_phase.Value);
            if (_phase.Value != GhostPhase.Attack || _stateLight == null || _settings == null)
                return;

            switch ((GhostDinoCharge.Stage)_dinoStage.Value)
            {
                case GhostDinoCharge.Stage.Prepare:
                    _stateLight.color = new Color(1f, 0.85f, 0.2f);
                    break;

                case GhostDinoCharge.Stage.Charge:
                    _stateLight.color = Color.white;
                    _stateLight.intensity = _settings.AttackLightIntensity * 3f;
                    break;

                case GhostDinoCharge.Stage.Stun:
                    _stateLight.color = new Color(0.3f, 0.45f, 1f);
                    _stateLight.intensity = _settings.AttackLightIntensity * 0.5f;
                    break;
            }
        }

        /// <summary>돌진 준비 중에는 몸 조명이 빠르게 깜빡여 '곧 돌진한다'를 알린다.</summary>
        private void TickDinoVisual()
        {
            if (!IsSpawned || _settings == null || _stateLight == null || _phase.Value != GhostPhase.Attack
                || (GhostDinoCharge.Stage)_dinoStage.Value != GhostDinoCharge.Stage.Prepare)
            {
                return;
            }

            _stateLight.intensity = Mathf.Lerp(
                _settings.AttackLightIntensity,
                _settings.AttackLightIntensity * 4f,
                Mathf.PingPong(Time.time * 10f, 1f));
        }
    }
}
