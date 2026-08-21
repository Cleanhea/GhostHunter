using GhostHunter.Core.Player;
using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>
    /// 게임 씬의 플레이어 시작 위치를 고른다.
    ///
    /// 단순히 <c>clientId % 지점수</c>로 고르면 안 되는 이유: 클라이언트 ID는 접속할 때마다
    /// 증가한다. 로비를 나갔다 들어오거나 진행 중인 판에 난입하면 이미 다른 플레이어가 서 있는
    /// 지점이 뽑혀 <b>서로 겹친 채로 스폰</b>된다. CharacterController 는 겹친 상태에서
    /// 서로를 밀어내므로 눈에는 "스폰하자마자 튕겨 나간다"로 보인다
    /// → [ADR-0012 §4](../../../../docs/architecture/decisions/ADR-0012-room-code-and-lobby-visibility.md)
    ///
    /// 그래서 선호 지점부터 순서대로 <b>비어 있는지 확인</b>하고, 전부 막혀 있으면 선호 지점
    /// 주변으로 밀어내며 빈 자리를 찾는다. 끝까지 못 찾아도 스폰 자체를 실패시키지는 않는다 —
    /// 겹쳐 나오는 것이 아예 안 나오는 것보다 낫다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSpawnRegistry : MonoBehaviour, IPlayerSpawnRegistry
    {
        [Tooltip("시작 위치. 씬 생성 도구가 채운다.")]
        [SerializeField] private Transform[] _spawnPoints;

        [Tooltip("자리가 비었는지 재는 캡슐 반지름. CharacterController(0.35) 보다 조금 작게 둔다.")]
        [SerializeField, Min(0.05f)] private float _clearanceRadius = 0.34f;

        [Tooltip("자리가 비었는지 재는 캡슐 높이. CharacterController 와 같게 둔다.")]
        [SerializeField, Min(0.2f)] private float _clearanceHeight = 1.8f;

        [Tooltip("전 지점이 막혔을 때 선호 지점 주변을 훑어볼 반경.")]
        [SerializeField, Min(0f)] private float _fallbackNudgeDistance = 0.9f;

        /// <summary>선호 지점이 막혔을 때 훑어볼 방향. 8방위면 좁은 방에서도 한 자리는 나온다.</summary>
        private static readonly Vector3[] NudgeDirections =
        {
            new(1f, 0f, 0f),
            new(-1f, 0f, 0f),
            new(0f, 0f, 1f),
            new(0f, 0f, -1f),
            new(0.7071f, 0f, 0.7071f),
            new(-0.7071f, 0f, 0.7071f),
            new(0.7071f, 0f, -0.7071f),
            new(-0.7071f, 0f, -0.7071f),
        };

        private readonly Collider[] _overlapBuffer = new Collider[8];

        public bool TryGetSpawn(
            ulong clientId,
            Transform spawning,
            out Vector3 position,
            out Quaternion rotation)
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0)
            {
                position = default;
                rotation = default;
                return false;
            }

            int preferred = (int)(clientId % (ulong)_spawnPoints.Length);
            Transform preferredPoint = _spawnPoints[preferred];

            // 1) 선호 지점부터 한 바퀴 돌며 비어 있는 곳을 찾는다.
            for (int offset = 0; offset < _spawnPoints.Length; offset++)
            {
                Transform candidate = _spawnPoints[(preferred + offset) % _spawnPoints.Length];
                if (candidate == null)
                    continue;

                if (IsClear(candidate.position, spawning))
                {
                    position = candidate.position;
                    rotation = candidate.rotation;
                    return true;
                }
            }

            // 2) 전부 막혔다. 선호 지점 주변으로 밀어내 본다.
            if (preferredPoint != null && _fallbackNudgeDistance > 0f)
            {
                foreach (Vector3 direction in NudgeDirections)
                {
                    Vector3 nudged = preferredPoint.position + direction * _fallbackNudgeDistance;
                    if (!IsClear(nudged, spawning))
                        continue;

                    position = nudged;
                    rotation = preferredPoint.rotation;
                    return true;
                }
            }

            // 3) 그래도 없으면 선호 지점 그대로. 겹쳐 나오더라도 스폰은 시킨다.
            if (preferredPoint == null)
            {
                position = default;
                rotation = default;
                return false;
            }

            Debug.LogWarning(
                $"[PlayerSpawnRegistry] clientId {clientId} 가 설 빈 자리를 찾지 못했습니다. " +
                $"'{preferredPoint.name}' 에 겹쳐 스폰합니다.",
                this);

            position = preferredPoint.position;
            rotation = preferredPoint.rotation;
            return true;
        }

        /// <summary>
        /// 그 자리에 플레이어 캡슐이 들어가는지 본다. 스폰하려는 본인의 콜라이더는 세지 않는다 —
        /// 플레이어 오브젝트는 자리를 정하기 <b>전에</b> 이미 씬에 만들어져 있다.
        /// </summary>
        private bool IsClear(Vector3 footPosition, Transform spawning)
        {
            float radius = _clearanceRadius;
            Vector3 bottom = footPosition + Vector3.up * radius;
            Vector3 top = footPosition + Vector3.up * Mathf.Max(_clearanceHeight - radius, radius);

            int count = Physics.OverlapCapsuleNonAlloc(
                bottom,
                top,
                radius,
                _overlapBuffer,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider hit = _overlapBuffer[i];
                if (hit == null)
                    continue;

                if (spawning != null && hit.transform.IsChildOf(spawning))
                    continue;

                return false;
            }

            return true;
        }
    }
}
