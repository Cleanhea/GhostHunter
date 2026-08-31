using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.Interaction
{
    /// <summary>
    /// 귀신 초자연현상 '서랍 열기'(§6.5 #4)가 여닫는 서랍. 가구 프리팹은 아직 서랍 파츠가
    /// 없어서, 설치 도구가 씬에 배치된 서랍장류 인스턴스에 얇은 면 오브젝트와 이 컴포넌트를
    /// 붙인다(정신력 테스트베드와 같은 프로토타입 범위).
    ///
    /// 가구 루트가 이미 <c>NetworkObject</c> 라 여기서 <c>NetworkBehaviour</c> 를 쓰면
    /// 중첩 <c>NetworkObject</c> 가 된다(CLAUDE.md §5, 방 프리셋이 피하는 함정). 그래서
    /// 이 컴포넌트는 순수 <c>MonoBehaviour</c> 이고, 서버가 현상 RPC(§10)를 전 피어에 보내면
    /// 각 피어가 같은 위치의 가장 가까운 서랍을 로컬로 여닫는다. 판정은 서버 하나뿐이다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GhostDrawer : MonoBehaviour
    {
        private static readonly List<GhostDrawer> RegistryList = new();

        /// <summary>지금 씬에 살아 있는 서랍들.</summary>
        public static IReadOnlyList<GhostDrawer> Registry => RegistryList;

        [Tooltip("열릴 때 밀려 나오는 서랍 면. 비우면 이 오브젝트 자신을 쓴다.")]
        [SerializeField] private Transform _face;

        [Tooltip("닫힘 기준 로컬 좌표에서 열릴 때 이동하는 방향·거리(로컬).")]
        [SerializeField] private Vector3 _openLocalOffset = new(0f, 0f, 0.26f);

        [Tooltip("여닫는 속도(m/s).")]
        [SerializeField, Min(0.1f)] private float _slideSpeed = 1.3f;

        [Tooltip("열린 뒤 자동으로 닫히기까지의 시간(초).")]
        [SerializeField, Min(0.3f)] private float _autoCloseDelay = 2.4f;

        private Vector3 _closedLocalPosition;
        private bool _isOpen;
        private float _autoCloseAt;

        private void Awake()
        {
            if (_face == null)
                _face = transform;

            _closedLocalPosition = _face.localPosition;
        }

        private void OnEnable()
        {
            if (!RegistryList.Contains(this))
                RegistryList.Add(this);
        }

        private void OnDisable()
        {
            RegistryList.Remove(this);
            _isOpen = false;
            if (_face != null)
                _face.localPosition = _closedLocalPosition;
        }

        private void Update()
        {
            if (_isOpen && Time.time >= _autoCloseAt)
                _isOpen = false;

            Vector3 target = _isOpen
                ? _closedLocalPosition + _openLocalOffset
                : _closedLocalPosition;

            _face.localPosition = Vector3.MoveTowards(
                _face.localPosition,
                target,
                _slideSpeed * Time.deltaTime);
        }

        /// <summary>귀신이 서랍을 잠깐 열었다 닫는다. 모든 피어가 현상 RPC에서 로컬로 호출한다.</summary>
        public void PulseOpen()
        {
            _isOpen = true;
            _autoCloseAt = Time.time + _autoCloseDelay;
        }
    }
}
