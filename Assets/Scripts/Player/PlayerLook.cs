using GhostHunter.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.Player
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

            if (_playerCamera != null)
                _playerCamera.enabled = local;

            if (_audioListener != null)
                _audioListener.enabled = local;

            if (!local)
            {
                enabled = false;
                return;
            }

            PrototypeSceneContext.SetGameplayCameraActive(true);
            SetCursorLocked(true);
        }

        public override void OnNetworkDespawn()
        {
            if (!IsOwner)
                return;

            PrototypeSceneContext.SetGameplayCameraActive(false);
            SetCursorLocked(false);
        }

        private void Update()
        {
            if (_settings == null || _input == null || _cameraPivot == null)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                SetCursorLocked(Cursor.lockState != CursorLockMode.Locked);

            if (Cursor.lockState != CursorLockMode.Locked)
                return;

            Vector2 look = _input.Look * _settings.MouseSensitivity;
            transform.Rotate(Vector3.up, look.x, Space.World);

            _pitch = Mathf.Clamp(_pitch - look.y, -_settings.PitchLimit, _settings.PitchLimit);
            _cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
