using GhostHunter.Core;
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

        private float _pitch;

        public Camera PlayerCamera => _playerCamera;

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
        }

        public override void OnNetworkDespawn()
        {
            if (!IsOwner)
                return;

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
