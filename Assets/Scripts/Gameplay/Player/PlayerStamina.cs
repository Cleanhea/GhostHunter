using GhostHunter.Core;
using GhostHunter.Core.Audio;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 달리기 스태미나. 소모·회복·지침 규칙은 <see cref="StaminaModel"/>, 이 컴포넌트는 소유자 쪽에서 그 모델을
    /// 들고 있다가 <see cref="PlayerMotor"/> 가 매 프레임 <see cref="Tick"/> 으로 진행시키고, 헐떡이는 소리를 건다.
    ///
    /// <para><b>복제하지 않는다.</b> 스태미나는 소유자 화면의 바와 소유자 자신의 이동 속도에만 쓰이고 서버는 읽지 않는다 —
    /// 이동이 소유자 권위인 것(ADR-0008)의 연장이라 새 NetworkVariable·RPC 가 없다. 원격 사본은 모델이 없어
    /// 항상 가득 찬 것으로 읽힌다. 다른 사람에게도 헐떡임이 들려야 하면 <see cref="Panting"/> 만 복제하면 된다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerStamina : NetworkBehaviour
    {
        [SerializeField] private StaminaSettings _settings;

        private StaminaModel _model;
        private PlayerInputReader _input;
        private IAudioService _audio;
        private SoundHandle _breathHandle;

        /// <summary>HUD 가 바 모양과 사라지는 시간을 읽는다.</summary>
        public StaminaSettings Settings => _settings;

        /// <summary>0~1. 소유자가 아니면 1.</summary>
        public float Ratio => _model?.Ratio ?? 1f;

        public float Current => _model?.Current ?? (_settings != null ? _settings.MaxStamina : 0f);

        public float Max => _settings != null ? _settings.MaxStamina : 0f;

        public bool Exhausted => _model != null && _model.Exhausted;

        public bool Panting => _model != null && _model.Panting;

        /// <summary>지금 달릴 수 있는가. 탈진했으면 거짓이다. 모델이 없으면(원격·설정 없음) 막지 않는다.</summary>
        public bool CanSprint => _model == null || _model.CanSprint;

        /// <summary>지쳐서 줄어든 이동 속도 배율(1이면 그대로).</summary>
        public float SpeedMultiplier => _model?.SpeedMultiplier ?? 1f;

        /// <summary>HUD 를 이 클라이언트가 그려야 하는가.</summary>
        public bool IsLocalOwner => IsSpawned && IsOwner && _model != null;

        private void Awake()
        {
            if (_settings == null)
                Debug.LogError($"{nameof(PlayerStamina)}: StaminaSettings 가 Player 프리팹에 배선되지 않았습니다.", this);

            TryGetComponent(out _input);
        }

        public override void OnNetworkSpawn()
        {
            Services.TryGet(out _audio);

            if (IsOwner && _settings != null)
                _model = new StaminaModel(_settings);
        }

        public override void OnNetworkDespawn()
        {
            StopBreathing();
            _model = null;
            _audio = null;
        }

        /// <summary>
        /// 한 프레임을 진행한다. <see cref="PlayerMotor"/> 가 소유자 쪽에서만 부른다 — 이동이 잠겨 있어도(달리지 않으므로
        /// <paramref name="sprinting"/> 이 거짓) 회복은 계속되게 매 프레임 부른다.
        /// </summary>
        public void Tick(float deltaTime, bool sprinting)
        {
            if (_model == null)
                return;

            _model.Tick(deltaTime, sprinting);
            UpdateBreathing();
        }

        /// <summary>스태미나를 가득 채운다(부활·재시작 등).</summary>
        public void Refill()
        {
            _model?.Refill();
            StopBreathing();
        }

        /// <summary>
        /// 헐떡이는 소리를 멈춘다. 모델은 그대로다 — 로딩·정산처럼 <see cref="Tick"/> 이 멈추는 구간에서
        /// 반복 소리만 끊기 위해 <see cref="PlayerMotor"/> 가 부른다.
        /// </summary>
        public void StopBreathing()
        {
            if (!_breathHandle.IsValid)
                return;

            _audio?.Stop(_breathHandle, _settings != null ? _settings.BreathFadeSeconds : 0f);
            _breathHandle = SoundHandle.None;
        }

        private void UpdateBreathing()
        {
            SoundCue cue = _settings != null ? _settings.BreathCue : null;
            // 사망하면 몸은 남아도 관전 입력으로 바뀐다 — 죽은 사람이 헐떡이지 않게 한다.
            bool dead = _input != null && _input.IsDeathInputLocked;
            if (cue == null || _audio == null || dead || !_model.Panting)
            {
                StopBreathing();
                return;
            }

            if (_breathHandle.IsValid && _audio.IsPlaying(_breathHandle))
                return;

            // 반복 큐면 계속 돌고, 한 번짜리 큐면 끝난 뒤 다시 건다(큐의 쿨다운이 간격을 준다).
            _breathHandle = _audio.PlayAttached(cue, transform, _settings.BreathVolume);
        }
    }
}
