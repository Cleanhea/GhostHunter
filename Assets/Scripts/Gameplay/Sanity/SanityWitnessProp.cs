using System.Collections.Generic;
using GhostHunter.Core;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Sanity
{
    /// <summary>테스트베드 소품이 어떤 정신력 감소 조건을 흉내 내는지 정한다.</summary>
    public enum SanityWitnessKind : byte
    {
        /// <summary>동료 시체. 최초 목격만 20 감소하고 같은 시체는 다시 줄지 않는다.</summary>
        Corpse,

        /// <summary>귀신 이벤트. 한 번의 발생당 플레이어마다 한 번 10 감소한다.</summary>
        GhostEvent,
    }

    /// <summary>
    /// 프로토타입 테스트베드 전용 소품. 서버가 플레이어의 가시 판정을 직접 계산해서
    /// <see cref="SanityNetworkState"/> 의 감소 API를 호출한다.
    ///
    /// **정식 시체·귀신 이벤트 시스템이 아니다.** 어떤 현상을 "귀신 이벤트 목격"으로 칠지는
    /// 아직 기획 결정 대기(roadmap M8-GS-2)라, 여기서는 눈에 보이는 소품 하나를 발생 한 건으로
    /// 취급한다. 정식 시스템이 생기면 이 컴포넌트와 테스트베드는 통째로 삭제한다.
    ///
    /// 서버는 플레이어의 카메라 피치를 모른다 — 피치는 소유자 로컬 값이고 복제하지 않는다.
    /// 그래서 수평 방향(요)만으로 판정한다. 테스트에는 충분하고, 정식 가시 판정 규칙은
    /// 귀신 이벤트 문서에서 정의한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SanityWitnessProp : MonoBehaviour
    {
        private const int MaxPlayerCount = 8;

        [SerializeField] private SanityWitnessKind _kind = SanityWitnessKind.Corpse;

        [Tooltip("Corpse 모드에서 시체를 구분하는 값. 시체마다 달라야 중복 방지를 확인할 수 있다.")]
        [SerializeField] private ulong _corpseId = 1;

        [Header("Witness check")]
        [Tooltip("플레이어가 이 지점을 볼 수 있어야 목격으로 친다. 비우면 소품의 원점을 쓴다.")]
        [SerializeField] private Transform _sightPoint;
        [SerializeField] private float _witnessDistance = 12f;
        [SerializeField, Range(1f, 180f)] private float _witnessAngle = 70f;
        [SerializeField] private float _checkInterval = 0.2f;
        [SerializeField] private float _playerEyeHeight = 1.5f;

        [Header("Ghost event occurrence")]
        [SerializeField] private float _activeDuration = 5f;
        [SerializeField] private float _dormantDuration = 5f;
        [SerializeField] private Renderer[] _occurrenceRenderers;
        [SerializeField] private Light _occurrenceLight;

        private readonly SanityNetworkState[] _states = new SanityNetworkState[MaxPlayerCount];
        private readonly HashSet<ulong> _witnessedThisOccurrence = new();

        private ISanityTeamService _teamService;
        private float _checkRemaining;
        private bool _isOccurrenceActive;

        /// <summary>귀신 이벤트가 지금 발생 중인지. 시체 모드에서는 항상 true다.</summary>
        public bool IsOccurrenceActive =>
            _kind != SanityWitnessKind.GhostEvent || _isOccurrenceActive;

        private void Awake()
        {
            if (_kind == SanityWitnessKind.GhostEvent)
                ApplyOccurrenceVisual(false);
        }

        private void Update()
        {
            if (_kind == SanityWitnessKind.GhostEvent)
                UpdateOccurrence();

            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer)
                return;

            _checkRemaining -= Time.deltaTime;
            if (_checkRemaining > 0f)
                return;

            _checkRemaining = Mathf.Max(0.05f, _checkInterval);
            ApplyWitness();
        }

        /// <summary>
        /// 발생 위상을 서버 시계로 계산한다. 복제 없이도 모든 피어가 같은 순간에 켜고 끈다 —
        /// 소품이 <c>NetworkObject</c> 가 아니라서 씬 배치 해시 함정(CLAUDE.md §5)을 피할 수 있다.
        /// </summary>
        private void UpdateOccurrence()
        {
            double cycle = _activeDuration + _dormantDuration;
            if (cycle <= 0d)
                return;

            NetworkManager network = NetworkManager.Singleton;
            double clock = network != null && network.IsListening
                ? network.ServerTime.Time
                : Time.timeAsDouble;

            bool active = clock % cycle < _activeDuration;
            if (active == _isOccurrenceActive)
                return;

            _isOccurrenceActive = active;

            // 새 발생이 시작되면 지난 발생의 목격 기록을 버린다. 같은 발생 안에서는 한 번만 줄인다.
            if (active)
                _witnessedThisOccurrence.Clear();

            ApplyOccurrenceVisual(active);
        }

        private void ApplyOccurrenceVisual(bool active)
        {
            if (_occurrenceRenderers != null)
            {
                for (int i = 0; i < _occurrenceRenderers.Length; i++)
                {
                    if (_occurrenceRenderers[i] != null)
                        _occurrenceRenderers[i].enabled = active;
                }
            }

            if (_occurrenceLight != null)
                _occurrenceLight.enabled = active;
        }

        private void ApplyWitness()
        {
            if (_teamService == null && !Services.TryGet(out _teamService))
                return;

            if (!IsOccurrenceActive)
                return;

            int count = _teamService.CopyPlayerStates(_states);
            for (int i = 0; i < count; i++)
            {
                SanityNetworkState state = _states[i];
                if (state == null || !state.IsSpawned || !state.HasSanity)
                    continue;

                if (!CanWitness(state.transform))
                    continue;

                if (_kind == SanityWitnessKind.Corpse)
                {
                    if (state.ServerApplyCorpseWitnessed(_corpseId))
                        LogWitness(state, $"시체({_corpseId}) 최초 목격");
                    continue;
                }

                if (!_witnessedThisOccurrence.Add(state.OwnerClientId))
                    continue;

                if (state.ServerApplyGhostEventWitnessed())
                    LogWitness(state, "귀신 이벤트 목격");
            }
        }

        private bool CanWitness(Transform player)
        {
            Vector3 sight = _sightPoint != null ? _sightPoint.position : transform.position;
            Vector3 eye = player.position + Vector3.up * _playerEyeHeight;

            Vector3 toProp = sight - eye;
            toProp.y = 0f;
            float distanceSquared = toProp.sqrMagnitude;
            if (distanceSquared > _witnessDistance * _witnessDistance || distanceSquared <= 0.0001f)
                return false;

            Vector3 direction = toProp / Mathf.Sqrt(distanceSquared);
            float minimumDot = Mathf.Cos(_witnessAngle * 0.5f * Mathf.Deg2Rad);
            if (Vector3.Dot(player.forward, direction) < minimumDot)
                return false;

            if (!Physics.Linecast(
                    eye,
                    sight,
                    out RaycastHit hit,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            return hit.transform == transform || hit.transform.IsChildOf(transform);
        }

        private void LogWitness(SanityNetworkState state, string reason)
        {
            Debug.Log(
                $"[SanityTestbed] Client {state.OwnerClientId} {reason} → 정신력 {state.Sanity}%",
                this);
        }
    }
}
