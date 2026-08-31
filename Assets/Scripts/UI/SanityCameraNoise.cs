using GhostHunter.Core;
using GhostHunter.Gameplay.Sanity;
using UnityEngine;
using UnityEngine.Rendering;

namespace GhostHunter.UI
{
    /// <summary>
    /// 로컬 플레이어의 정신력이 카메라 노이즈 임계값 이하로 내려가면 Global Volume 의 가중치를
    /// 올려 화면 테두리 연출을 켠다. 기획서 §5의 `n ≤ 20` 규칙을 그대로 따르며 세기는 단계가
    /// 없다 — 조건을 만족하면 켜고, 벗어나거나 사망하면 끈다.
    ///
    /// 임계값 판정 자체는 <see cref="SanityNetworkState.CurrentDebuffs"/> 가 갖고 있다.
    /// 이 컴포넌트는 그 결과를 화면에 옮기기만 한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SanityCameraNoise : MonoBehaviour
    {
        private const float ResolveInterval = 0.5f;

        [SerializeField] private Volume _volume;

        [Tooltip("노이즈가 켜질 때까지 걸리는 시간. 0이면 즉시 켠다.")]
        [SerializeField] private float _fadeInSeconds = 0.35f;

        [Tooltip("노이즈가 꺼질 때까지 걸리는 시간.")]
        [SerializeField] private float _fadeOutSeconds = 0.8f;

        private ISanityTeamService _teamService;
        private SanityNetworkState _localState;
        private float _resolveRemaining;

        private void Awake()
        {
            if (_volume == null)
                _volume = GetComponent<Volume>();

            if (_volume == null)
            {
                Debug.LogError($"{nameof(SanityCameraNoise)}: Volume 배선이 필요합니다.", this);
                enabled = false;
                return;
            }

            // 씬을 열자마자 프로필이 걸려 있으면 에디터에서 화면이 늘 노이즈로 보인다. 항상 0에서 시작한다.
            _volume.weight = 0f;
            Services.TryGet(out _teamService);
        }

        private void OnDisable()
        {
            if (_volume != null)
                _volume.weight = 0f;

            _localState = null;
        }

        private void Update()
        {
            ResolveLocalState();

            bool noiseActive = _localState != null
                && _localState.IsSpawned
                && (_localState.CurrentDebuffs & SanityDebuffFlags.CameraNoise) != 0;

            float target = noiseActive ? 1f : 0f;
            float duration = noiseActive ? _fadeInSeconds : _fadeOutSeconds;

            _volume.weight = duration <= 0f
                ? target
                : Mathf.MoveTowards(_volume.weight, target, Time.deltaTime / duration);
        }

        /// <summary>
        /// 로컬 플레이어는 접속·리스폰마다 새 <c>NetworkObject</c> 로 바뀐다. 매 프레임 서비스를
        /// 뒤지지 않도록 주기적으로만 다시 찾고, 들고 있던 참조가 살아 있으면 그대로 쓴다.
        /// </summary>
        private void ResolveLocalState()
        {
            if (_localState != null && _localState.IsSpawned)
                return;

            _resolveRemaining -= Time.deltaTime;
            if (_resolveRemaining > 0f)
                return;

            _resolveRemaining = ResolveInterval;

            if (_teamService == null && !Services.TryGet(out _teamService))
                return;

            if (!_teamService.TryGetLocalState(out _localState))
                _localState = null;
        }
    }
}
