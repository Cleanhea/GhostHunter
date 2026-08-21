using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>
    /// 방 하나 분량의 가구 배치. 집 바깥 전시 자리에 만들어 두고, 슬롯에 뽑히면 가구만 옮긴다.
    ///
    /// 프리팹으로 스폰하지 않는 이유: 맵 가구는 전부 씬에 놓인 <see cref="NetworkObject"/>라
    /// 호스트가 시작될 때 한꺼번에 스폰된다. 프리셋을 프리팹으로 만들면 가구가 중첩
    /// NetworkObject 가 되고 스폰 경로가 둘로 갈린다. 이미 스폰된 가구를 서버가 옮기기만 하면
    /// 클라이언트는 <see cref="NetworkTransform"/> 복제로 같은 결과를 본다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomPreset : MonoBehaviour
    {
        [Tooltip("로그에 찍히는 이름. A / B / C.")]
        [SerializeField] private string _presetId = "A";

        [Tooltip("방 중심(바닥면) 기준점. 가구 위치를 이 트랜스폼 기준 로컬로 기억한다.")]
        [SerializeField] private Transform _anchor;

        [Tooltip("이 프리셋에 속한 가구. 씬 생성 도구가 채운다.")]
        [SerializeField] private NetworkObject[] _furniture;

        private Pose[] _localPoses;

        public string PresetId => _presetId;

        /// <summary>전시 자리. 프리셋을 여기에 다시 적용하면 원래 배치로 돌아간다.</summary>
        public Transform Anchor => _anchor != null ? _anchor : transform;

        private void Awake()
        {
            CacheLocalPoses();
        }

        /// <summary>
        /// 지금 가구 위치를 앵커 기준 로컬 포즈로 기억한다. 반드시 전시 자리에 있을 때 불러야 한다.
        /// 에디터 검증도 이걸 부른 뒤 <see cref="ApplyTo"/>로 슬롯에 시험 배치한다.
        /// </summary>
        public void CacheLocalPoses()
        {
            if (_furniture == null)
            {
                _localPoses = System.Array.Empty<Pose>();
                return;
            }

            Transform anchor = Anchor;
            _localPoses = new Pose[_furniture.Length];
            for (int i = 0; i < _furniture.Length; i++)
            {
                Transform item = _furniture[i] != null ? _furniture[i].transform : anchor;
                _localPoses[i] = new Pose(
                    anchor.InverseTransformPoint(item.position),
                    Quaternion.Inverse(anchor.rotation) * item.rotation);
            }
        }

        /// <summary>가구 개수. 배치 결과를 로그로 확인할 때 쓴다.</summary>
        public int FurnitureCount => _furniture != null ? _furniture.Length : 0;

        /// <summary>대표 가구의 현재 월드 좌표. 배치가 실제로 먹었는지 로그로 보기 위한 것이다.</summary>
        public Vector3 FirstFurniturePosition =>
            _furniture != null && _furniture.Length > 0 && _furniture[0] != null
                ? _furniture[0].transform.position
                : transform.position;

        /// <summary>
        /// 가구를 슬롯 기준 같은 상대 위치로 옮긴다. 물리 권위가 서버에 있으므로 서버에서만 부른다.
        ///
        /// <see cref="NetworkTransform.Teleport"/>를 쓰는 이유는 보간 때문이다 — 그냥 트랜스폼을
        /// 옮기면 클라이언트에서 가구가 전시 자리부터 방까지 날아가는 게 보인다.
        /// 아직 스폰 전이면 트랜스폼을 직접 옮긴다. 그 위치가 스폰 메시지에 실려 나가므로
        /// 클라이언트는 처음부터 제자리에 놓인 가구를 본다.
        /// </summary>
        public void ApplyTo(Transform slot)
        {
            if (_furniture == null || slot == null)
                return;

            if (_localPoses == null || _localPoses.Length != _furniture.Length)
                CacheLocalPoses();

            for (int i = 0; i < _furniture.Length; i++)
            {
                NetworkObject item = _furniture[i];
                if (item == null)
                    continue;

                Pose local = _localPoses[i];
                Vector3 position = slot.TransformPoint(local.position);
                Quaternion rotation = slot.rotation * local.rotation;

                if (item.IsSpawned && item.TryGetComponent(out NetworkTransform networkTransform))
                    networkTransform.Teleport(position, rotation, item.transform.localScale);
                else
                    item.transform.SetPositionAndRotation(position, rotation);

                if (Application.isPlaying && item.TryGetComponent(out Rigidbody body))
                    TeleportBody(body, position, rotation);
            }
        }

        /// <summary>
        /// Rigidbody 를 옮긴 자리에 실제로 앉힌다.
        ///
        /// 트랜스폼만 옮기면 안 되는 이유: 가구는 <see cref="RigidbodyInterpolation.Interpolate"/>
        /// 로 돌아간다. 보간이 켜진 바디는 다음 물리 스텝에 <b>자기가 들고 있던 이전 자세</b>로
        /// 트랜스폼을 다시 덮어쓴다 — 눈에는 "옮겼는데 도로 돌아간다"로 보인다.
        /// 그래서 트랜스폼과 별개로 바디 자세까지 같이 옮긴다.
        /// </summary>
        internal static void TeleportBody(Rigidbody body, Vector3 position, Quaternion rotation)
        {
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            body.position = position;
            body.rotation = rotation;
        }
    }
}
