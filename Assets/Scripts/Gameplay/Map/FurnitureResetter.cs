using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>
    /// 던져서 어질러진 맵 가구를 처음 놓였던 자리로 되돌린다. 플레이 모드를 다시 시작하지 않고
    /// 투척감을 반복 테스트하려는 개발용 도구다.
    ///
    /// 물리 권위는 서버에만 있으므로 호스트에서만 동작한다. 클라이언트가 R 을 눌러도 아무 일도 없다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FurnitureResetter : MonoBehaviour
    {
        [Tooltip("되돌릴 가구. 씬 생성 도구가 채운다.")]
        [SerializeField] private Rigidbody[] _furniture;

        [SerializeField] private Key _resetKey = Key.R;

        private Pose[] _initialPoses;

        private void Awake()
        {
            CapturePoses();
        }

        /// <summary>
        /// 지금 위치를 "되돌릴 위치"로 기록한다. 방 프리셋처럼 세션이 시작될 때 가구를 옮기는
        /// 쪽이 배치를 끝낸 뒤 다시 불러야, R 이 전시 자리가 아니라 배치된 자리로 되돌린다.
        /// </summary>
        public void CapturePoses()
        {
            if (_furniture == null)
                return;

            _initialPoses = new Pose[_furniture.Length];
            for (int i = 0; i < _furniture.Length; i++)
            {
                Transform body = _furniture[i] != null ? _furniture[i].transform : transform;
                _initialPoses[i] = new Pose(body.position, body.rotation);
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard[_resetKey].wasPressedThisFrame)
                return;

            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer)
                return;

            ResetAll();
        }

        private void ResetAll()
        {
            if (_furniture == null || _initialPoses == null)
                return;

            for (int i = 0; i < _furniture.Length; i++)
            {
                Rigidbody body = _furniture[i];
                if (body == null)
                    continue;

                Pose pose = _initialPoses[i];

                // NetworkTransform 은 큰 이동을 보간하려 들기 때문에 그냥 트랜스폼을 쓰면
                // 가구가 제자리로 "날아간다". Teleport 로 보간을 건너뛴다.
                if (body.TryGetComponent(out NetworkTransform networkTransform)
                    && networkTransform.IsSpawned)
                {
                    networkTransform.Teleport(pose.position, pose.rotation, body.transform.localScale);
                }
                else
                {
                    body.transform.SetPositionAndRotation(pose.position, pose.rotation);
                }

                // 트랜스폼만 옮기면 보간이 켜진 Rigidbody 가 다음 물리 스텝에 되돌려 놓는다.
                RoomPreset.TeleportBody(body, pose.position, pose.rotation);
            }

            Debug.Log($"[FurnitureResetter] 가구 {_furniture.Length}개를 초기 위치로 되돌렸습니다.", this);
        }
    }
}
