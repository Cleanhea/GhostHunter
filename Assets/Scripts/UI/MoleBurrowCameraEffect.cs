using GhostHunter.Core;
using GhostHunter.Gameplay.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace GhostHunter.UI
{
    /// <summary>
    /// 로컬 플레이어가 굴착 스킬을 쓰는 동안(시전~매몰, <see cref="MoleBurrowController.IsActive"/>)
    /// Global Volume 의 가중치를 올려 화면 가장자리 비네트를 켠다. <see cref="SanityCameraNoise"/> 와
    /// 같은 구조 — 이 Volume 은 정신력 노이즈와 별개 프로필·오브젝트다(두 효과가 동시에 걸리면
    /// 겹쳐서 더 어두워질 뿐, 서로 밀어내지 않는다).
    ///
    /// 귀신 탐지용 <see cref="MoleBurrowController.IsBurrowed"/> 와 달리 <c>IsActive</c> 는
    /// 시전을 시작한 순간부터 켜진다 — 화면 연출은 "안전하게 숨었는가"가 아니라
    /// "지금 굴착 동작 중인가"를 보여 주면 된다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MoleBurrowCameraEffect : MonoBehaviour
    {
        [SerializeField] private Volume _volume;

        [Tooltip("비네트가 켜질 때까지 걸리는 시간. 시전 시간과 맞춰 두면 카메라가 낮아지는 것과 같이 짙어진다.")]
        [SerializeField] private float _fadeInSeconds = 0.3f;

        [Tooltip("비네트가 꺼질 때까지 걸리는 시간.")]
        [SerializeField] private float _fadeOutSeconds = 0.25f;

        private ILocalPlayerContext _localPlayer;

        private void Awake()
        {
            if (_volume == null)
                _volume = GetComponent<Volume>();

            if (_volume == null)
            {
                Debug.LogError($"{nameof(MoleBurrowCameraEffect)}: Volume 배선이 필요합니다.", this);
                enabled = false;
                return;
            }

            // 씬을 열자마자 프로필이 걸려 있으면 에디터에서 화면이 늘 비네트로 보인다. 항상 0에서 시작한다.
            _volume.weight = 0f;
            Services.TryGet(out _localPlayer);
        }

        private void OnDisable()
        {
            if (_volume != null)
                _volume.weight = 0f;
        }

        private void Update()
        {
            if (_localPlayer == null && !Services.TryGet(out _localPlayer))
                return;

            MoleBurrowController burrow = _localPlayer.BurrowController;
            bool active = burrow != null && burrow.IsActive;

            float target = active ? 1f : 0f;
            float duration = active ? _fadeInSeconds : _fadeOutSeconds;

            _volume.weight = duration <= 0f
                ? target
                : Mathf.MoveTowards(_volume.weight, target, Time.deltaTime / duration);
        }
    }
}
