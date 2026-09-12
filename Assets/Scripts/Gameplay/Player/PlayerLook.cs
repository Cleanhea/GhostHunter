using GhostHunter.Core;
using GhostHunter.Gameplay.Sanity;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class PlayerLook : NetworkBehaviour
    {
        [SerializeField] private PlayerMoveSettings _settings;
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private Transform _cameraPivot;
        [SerializeField] private Camera _playerCamera;
        [SerializeField] private AudioListener _audioListener;

        // Owner 쓰기 + Everyone 읽기 — PlayerMotor의 IsCrouching/IsProne, MoleBurrowController의
        // IsBurrowed와 같은 이동 권위 예외(ADR-0008)의 연장이다. 로컬 피치는 그동안 복제되지
        // 않았지만, 관전 시스템(spectator-system.md)이 생존자의 상하 시선을 재현하려면 필요하다.
        private readonly NetworkVariable<float> _networkPitch = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private float _pitch;
        private SanityNetworkState _sanity;

        public Camera PlayerCamera => _playerCamera;

        /// <summary>
        /// 상하 시선(도, 위가 음수). 소유자는 로컬 값을 그대로, 원격에서는 복제된 값을 읽는다 —
        /// 관전 중인 다른 클라이언트의 <see cref="SpectatorController"/>가 이 값으로 카메라 회전을
        /// 재현한다.
        /// </summary>
        public float Pitch => IsOwner ? _pitch : _networkPitch.Value;

        public override void OnNetworkSpawn()
        {
            bool local = IsOwner;

            // 새 리스너를 켜기 전에 씬의 Overview 리스너부터 꺼서 한 프레임 중복을 막는다.
            if (local)
                PrototypeSceneContext.SetGameplayCameraActive(true);

            if (_playerCamera != null)
                _playerCamera.enabled = local;

            if (_audioListener != null)
                _audioListener.enabled = local;

            if (!local)
            {
                enabled = false;
                return;
            }

            SetCursorLocked(true);

            // 관전 시스템(SpectatorController)이 사망 중 이 카메라/리스너를 대신 켜므로,
            // 여기서는 생존 여부에 따라 초기·이후 상태를 맞추기만 한다.
            _sanity = GetComponent<SanityNetworkState>();
            if (_sanity != null)
            {
                _sanity.AliveStateChanged += HandleAliveStateChanged;
                if (!_sanity.HasSanity)
                    HandleAliveStateChanged(false);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (!IsOwner)
                return;

            if (_sanity != null)
                _sanity.AliveStateChanged -= HandleAliveStateChanged;
            _sanity = null;

            // Overview 리스너를 되살리기 전에 플레이어 쪽을 먼저 끈다.
            if (_playerCamera != null)
                _playerCamera.enabled = false;

            if (_audioListener != null)
                _audioListener.enabled = false;

            PrototypeSceneContext.SetGameplayCameraActive(false);
            SetCursorLocked(false);
        }

        private void Update()
        {
            if (_settings == null || _input == null || _cameraPivot == null)
                return;

            // ESC 는 일시정지 메뉴가 가져갔고, 커서 잠금도 그 메뉴가 관리한다
            // → docs/project/pause-menu-system.md §3.5 (PM-3 해소안 A).
            // 여기서는 커서가 풀린 동안 시점이 돌지 않게 막기만 한다.
            if (Cursor.lockState != CursorLockMode.Locked)
                return;

            Vector2 look = _input.Look * _settings.MouseSensitivity;
            transform.Rotate(Vector3.up, look.x, Space.World);

            _pitch = Mathf.Clamp(_pitch - look.y, -_settings.PitchLimit, _settings.PitchLimit);
            _cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            _networkPitch.Value = _pitch;
        }

        /// <summary>
        /// 사망하면 이 카메라/리스너를 끈다 — 관전 카메라(<see cref="SpectatorController"/>)가
        /// 대신 켜진다. 부활하면 되돌린다.
        /// </summary>
        private void HandleAliveStateChanged(bool alive)
        {
            if (_playerCamera != null)
                _playerCamera.enabled = alive;

            if (_audioListener != null)
                _audioListener.enabled = alive;
        }

        /// <summary>
        /// 스폰·디스폰 시의 초기 잠금·해제만 여기서 한다. 플레이 중의 커서 전환은
        /// 일시정지 메뉴가 담당한다 → docs/architecture/pause-menu.md §6.1
        /// </summary>
        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
