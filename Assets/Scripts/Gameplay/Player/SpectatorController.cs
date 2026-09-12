using System.Collections.Generic;
using GhostHunter.Core;
using GhostHunter.Gameplay.Sanity;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 사망 후 관전(관전 기획서 spectator-system.md, 사용자 확정 2026-09-12: SP-1~SP-4).
    /// 로컬 소유자에서만 동작하며, 사망한 네트워크 플레이어 루트와 완전히 분리된 로컬 카메라로
    /// 자유시점(충돌·중력 없음) 또는 생존자 1인칭 추종을 보여준다.
    ///
    /// <para><b>모드</b> — 자유시점(<see cref="SpectatorMode.FreeFly"/>)과 플레이어 관전
    /// (<see cref="SpectatorMode.FollowSurvivor"/>) 사이를 <c>V</c>로 전환한다. 좌/우클릭은
    /// 이전/다음 생존자로 바로 전환한다(SP-1). 대상이 무효(사망·이탈·디스폰)가 되면 다음
    /// 생존자로, 후보가 없으면 자유시점으로 돌아간다. 자유시점 복귀 시 마지막 카메라 포즈를
    /// 이어 쓰고, 사망 직후 최초 진입은 사망 순간의 1인칭 시점에서 시작한다(SP-4).</para>
    ///
    /// <para><b>카메라 소유권</b> — 사망하면 <see cref="PlayerLook"/>이 자신의 카메라·리스너를
    /// 끄고(그쪽 파일에서 처리), 이 컴포넌트가 자신의 <see cref="_spectatorCamera"/>를 켠다.
    /// 부활하면 반대로 돌아간다. 이 클래스는 <c>transform</c>(네트워크 루트)을 절대 옮기지
    /// 않는다 — 관전 카메라의 위치·회전만 조작한다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerLook))]
    public sealed class SpectatorController : NetworkBehaviour
    {
        private const int MaxTrackedPlayers = 8;

        private enum SpectatorMode
        {
            FreeFly,
            FollowSurvivor,
        }

        [SerializeField] private SpectatorSettings _settings;
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private Camera _spectatorCamera;
        [SerializeField] private AudioListener _spectatorAudioListener;

        private readonly SanityNetworkState[] _teamStatesBuffer = new SanityNetworkState[MaxTrackedPlayers];
        private readonly List<ulong> _candidateBuffer = new(MaxTrackedPlayers);

        private PlayerLook _look;
        private SanityNetworkState _sanity;
        private ISanityTeamService _teamService;
        private SpectatorMode _mode;
        private ulong? _currentTargetClientId;
        private int _teamStateCount;
        private float _freeFlyYaw;
        private float _freeFlyPitch;
        private bool _isSpectating;

        private void Awake()
        {
            _look = GetComponent<PlayerLook>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            if (_settings == null || _input == null || _spectatorCamera == null)
            {
                Debug.LogError($"{nameof(SpectatorController)}: 필수 참조가 배선되지 않았습니다.", this);
                enabled = false;
                return;
            }

            _sanity = GetComponent<SanityNetworkState>();
            if (_sanity == null)
            {
                Debug.LogError($"{nameof(SpectatorController)}: {nameof(SanityNetworkState)}가 없습니다.", this);
                enabled = false;
                return;
            }

            _teamService = Services.Get<ISanityTeamService>();

            _spectatorCamera.enabled = false;
            if (_spectatorAudioListener != null)
                _spectatorAudioListener.enabled = false;

            _sanity.AliveStateChanged += HandleAliveStateChanged;

            // 초기 스폰 시 이미 사망 상태일 수도 있다 → spectator-system.md §3 항목 6.
            if (!_sanity.HasSanity)
                EnterSpectate();
        }

        public override void OnNetworkDespawn()
        {
            if (!IsOwner)
                return;

            if (_sanity != null)
                _sanity.AliveStateChanged -= HandleAliveStateChanged;
            _sanity = null;

            if (_isSpectating)
                ExitSpectate();
        }

        private void Update()
        {
            if (!_isSpectating || _input.IsGameplayInputLocked)
                return;

            if (_input.SpectatorToggleModePressedThisFrame)
                ToggleMode();

            if (_input.SpectatorPreviousPressedThisFrame)
                CycleTarget(previous: true);

            if (_input.SpectatorNextPressedThisFrame)
                CycleTarget(previous: false);

            if (_mode == SpectatorMode.FollowSurvivor)
                TickFollowSurvivor();
            else
                TickFreeFly(Time.deltaTime);
        }

        private void HandleAliveStateChanged(bool alive)
        {
            if (alive)
                ExitSpectate();
            else
                EnterSpectate();
        }

        private void EnterSpectate()
        {
            if (_isSpectating)
                return;

            _isSpectating = true;
            _input.SetDeathInputLocked(true);

            // 사망 순간의 1인칭 시점에서 자유시점을 시작한다(SP-4) — 아직 꺼지지 않은 생존자
            // 카메라의 월드 포즈를 그대로 스냅한다. PlayerLook이 이 프레임 안에서 자기 카메라를
            // 끄는 순서와 무관하도록, 값은 지금 읽어 둔다.
            Camera survivorCamera = _look != null ? _look.PlayerCamera : null;
            if (survivorCamera != null)
            {
                Transform survivorCameraTransform = survivorCamera.transform;
                _spectatorCamera.transform.SetPositionAndRotation(
                    survivorCameraTransform.position,
                    survivorCameraTransform.rotation);
            }

            Vector3 euler = _spectatorCamera.transform.eulerAngles;
            _freeFlyYaw = euler.y;
            _freeFlyPitch = NormalizePitch(euler.x);

            _mode = SpectatorMode.FreeFly;
            _currentTargetClientId = null;

            _spectatorCamera.enabled = true;
            if (_spectatorAudioListener != null)
                _spectatorAudioListener.enabled = true;
        }

        private void ExitSpectate()
        {
            if (!_isSpectating)
                return;

            _isSpectating = false;
            _mode = SpectatorMode.FreeFly;
            _currentTargetClientId = null;

            _spectatorCamera.enabled = false;
            if (_spectatorAudioListener != null)
                _spectatorAudioListener.enabled = false;

            _input.SetDeathInputLocked(false);
        }

        private void ToggleMode()
        {
            if (_mode == SpectatorMode.FreeFly)
            {
                RefreshCandidates();
                if (!SpectatorTargetSelector.TryGetNext(_candidateBuffer, _currentTargetClientId, out ulong next))
                    return; // SP-1: 대상이 없으면 자유시점을 유지한다.

                _currentTargetClientId = next;
                _mode = SpectatorMode.FollowSurvivor;
                return;
            }

            // 자유시점으로 복귀 — 지금 카메라가 보던 위치·방향을 그대로 이어 쓴다(SP-4).
            Vector3 euler = _spectatorCamera.transform.eulerAngles;
            _freeFlyYaw = euler.y;
            _freeFlyPitch = NormalizePitch(euler.x);
            _mode = SpectatorMode.FreeFly;
            _currentTargetClientId = null;
        }

        private void CycleTarget(bool previous)
        {
            RefreshCandidates();

            bool found;
            ulong next;
            if (previous)
                found = SpectatorTargetSelector.TryGetPrevious(_candidateBuffer, _currentTargetClientId, out next);
            else
                found = SpectatorTargetSelector.TryGetNext(_candidateBuffer, _currentTargetClientId, out next);

            if (!found)
            {
                _mode = SpectatorMode.FreeFly; // SP-1: 생존자 0명 → 자유시점.
                _currentTargetClientId = null;
                return;
            }

            _currentTargetClientId = next;
            _mode = SpectatorMode.FollowSurvivor;
        }

        private void TickFreeFly(float deltaTime)
        {
            Vector2 lookDelta = _input.RawLookDelta * _settings.LookSensitivity;
            _freeFlyYaw += lookDelta.x;
            _freeFlyPitch = Mathf.Clamp(_freeFlyPitch - lookDelta.y, -_settings.PitchLimit, _settings.PitchLimit);

            Transform cameraTransform = _spectatorCamera.transform;
            cameraTransform.rotation = Quaternion.Euler(_freeFlyPitch, _freeFlyYaw, 0f);

            Vector2 move = _input.SpectatorMove;
            Vector3 direction = cameraTransform.right * move.x + cameraTransform.forward * move.y;
            if (_input.SpectatorAscendHeld)
                direction += Vector3.up;
            if (_input.SpectatorDescendHeld)
                direction -= Vector3.up;

            // 충돌·중력 없음(AC-2) — CharacterController를 거치지 않고 로컬 카메라만 옮긴다.
            cameraTransform.position += direction.normalized * (_settings.FlySpeed * deltaTime);
        }

        private void TickFollowSurvivor()
        {
            RefreshCandidates();

            if (!SpectatorTargetSelector.IsValid(_candidateBuffer, _currentTargetClientId))
            {
                if (!SpectatorTargetSelector.TryGetNext(_candidateBuffer, _currentTargetClientId, out ulong next))
                {
                    _mode = SpectatorMode.FreeFly; // SP-1: 대상 무효 + 후보 없음 → 자유시점.
                    _currentTargetClientId = null;
                    return;
                }

                _currentTargetClientId = next;
            }

            if (!TryResolveTargetView(_currentTargetClientId.Value, out PlayerMotor motor, out PlayerLook look))
            {
                _mode = SpectatorMode.FreeFly;
                _currentTargetClientId = null;
                return;
            }

            // 확정 범위는 카메라 위치·방향 추종뿐이다(spectator-system.md §3) — 대상의 트랜스폼·
            // 입력·소유권은 건드리지 않는다.
            float eyeHeight = motor.CameraLocalHeight;
            Vector3 eyePosition = motor.transform.position + Vector3.up * eyeHeight;
            float yaw = motor.transform.eulerAngles.y;
            float pitch = look.Pitch;

            _spectatorCamera.transform.SetPositionAndRotation(eyePosition, Quaternion.Euler(pitch, yaw, 0f));
        }

        /// <summary>
        /// 생존·스폰된 다른 플레이어의 클라이언트 ID 목록을 오름차순으로 채운다(SP-1·SP-4,
        /// 생존자 목록 순서대로 순회). <see cref="_teamStatesBuffer"/>도 함께 채워 두어
        /// <see cref="TryResolveTargetView"/>가 서비스를 다시 조회하지 않게 한다.
        /// </summary>
        private void RefreshCandidates()
        {
            _candidateBuffer.Clear();
            _teamStateCount = 0;

            if (_teamService == null)
                return;

            _teamStateCount = _teamService.CopyPlayerStates(_teamStatesBuffer);
            for (int i = 0; i < _teamStateCount; i++)
            {
                SanityNetworkState state = _teamStatesBuffer[i];
                if (state == null || state == _sanity || !state.HasSanity)
                    continue;

                _candidateBuffer.Add(state.OwnerClientId);
            }
        }

        private bool TryResolveTargetView(ulong clientId, out PlayerMotor motor, out PlayerLook look)
        {
            for (int i = 0; i < _teamStateCount; i++)
            {
                SanityNetworkState state = _teamStatesBuffer[i];
                if (state == null || state.OwnerClientId != clientId || !state.HasSanity)
                    continue;

                motor = state.GetComponent<PlayerMotor>();
                look = state.GetComponent<PlayerLook>();
                return motor != null && look != null;
            }

            motor = null;
            look = null;
            return false;
        }

        private static float NormalizePitch(float eulerX)
        {
            return eulerX > 180f ? eulerX - 360f : eulerX;
        }
    }
}
