using GhostHunter.Gameplay.Player;
using UnityEngine;

namespace GhostHunter.Gameplay.Furniture
{
    /// <summary>
    /// 개발자 테스트용 — 혼자서도 2인 들기를 재현하는 **가상 두 번째 홀더**. 호스트가 가구(또는 시체)를 들고 있을 때
    /// 접속하지 않은 가짜 clientId 를 서버에서 홀더로 붙여 <c>ThrowReady</c> → <c>Held</c>(2인 운반)로 만든다.
    /// 파트너의 눈은 호스트 시점에서 앞뒤·좌우로 떨어진 곳이고, 조준 방향은 호스트와 같다 → 호스트가 걸으면 둘이 같이 든 것처럼 따라온다.
    ///
    /// 파트너에게는 몸(플레이어 오브젝트·콜라이더)이 없다. 그래서 파트너 몸과 가구의 충돌, 파트너 쪽 연출·입력(휠 회전, 따로 놓기)은
    /// 재현하지 못한다 — 호스트 몸 충돌과 두 손잡이 추종·문틀 통과·2인 투척 힘만 본다. 실제 2인 접속의 네트워크 지연도 덮지 않는다.
    /// 조작은 <c>DebugTools/ConnectionHud</c>(Tab HUD 버튼, F4).
    /// → docs/architecture/throw-system.md "혼자 2인 들기 테스트"
    /// </summary>
    public sealed class FurnitureVirtualPartner
    {
        /// <summary>실제 접속자의 clientId 와 겹치지 않는 값(NGO 는 0부터 순서대로 준다).</summary>
        public const ulong ClientId = 9_000_001UL;

        public const float MaxOffset = 2.5f;

        private static int _activeCount;

        private ITarget _target;
        private ulong _hostClientId;

        /// <summary>호스트 시점 기준 앞(+)·뒤(−)로 떨어진 거리(m). 앞뒤로 서면 손잡이 축이 진행 방향을 향한다.</summary>
        public float Front { get; set; }

        /// <summary>호스트 시점 기준 오른쪽(+)·왼쪽(−)으로 떨어진 거리(m). 옆에 서면 가구가 진행 방향과 직각으로 돈다.</summary>
        public float Side { get; set; } = 0.9f;

        public bool IsAttached => _target != null;

        /// <summary>마지막 붙이기·떼기 결과 한 줄(HUD 표시용).</summary>
        public string Message { get; private set; } = "";

        /// <summary>
        /// 이 clientId 가 가상 파트너인가. 서버는 홀더의 플레이어 오브젝트가 없으면 강제 해제하는데(<c>FurnitureHoverMotor</c>)
        /// 가상 파트너는 그 검사에서 빼기 위해 읽는다.
        /// </summary>
        public static bool IsVirtualHolder(ulong clientId) => _activeCount > 0 && clientId == ClientId;

        /// <summary>호스트가 들고 있는 가구에 파트너를 붙인다.</summary>
        public bool TryAttach(FurnitureGrabTarget furniture, ulong hostClientId, Vector3 hostEye, Vector3 hostForward)
            => TryAttach(furniture == null ? null : new FurnitureAdapter(furniture), hostClientId, hostEye, hostForward);

        /// <summary>호스트가 들고 있는 시체에 파트너를 붙인다.</summary>
        public bool TryAttach(PlayerVisuals corpse, ulong hostClientId, Vector3 hostEye, Vector3 hostForward)
            => TryAttach(corpse == null ? null : new CorpseAdapter(corpse), hostClientId, hostEye, hostForward);

        /// <summary>파트너를 떼고 호스트가 계속 든 채로 둔다(발사 없음).</summary>
        public void Detach(string message = "파트너를 뗐습니다.")
        {
            if (_target == null)
                return;

            if (_target.Exists && _target.ContainsHolder(ClientId))
                _target.ForceRelease(ClientId);
            Clear(message);
        }

        /// <summary>
        /// 매 프레임 호출 — 파트너 조준을 호스트 시점에 맞춘다. 가구가 발사·반출됐거나 호스트가 놓았으면 파트너를 정리한다.
        /// </summary>
        public void Tick(Vector3 hostEye, Vector3 hostForward)
        {
            if (_target == null)
                return;

            if (!_target.Exists || !_target.IsServer)
            {
                Clear("파트너 해제 — 대상이 사라졌습니다.");
                return;
            }

            if (!_target.ContainsHolder(ClientId))
            {
                Clear("파트너 해제 — 발사되었거나 대상이 풀렸습니다.");
                return;
            }

            if (!_target.ContainsHolder(_hostClientId))
            {
                // 호스트가 놓았는데 파트너만 남으면 아무도 안 든 채 허공에 붙잡혀 있게 된다.
                Detach("호스트가 놓아 파트너도 풀었습니다.");
                return;
            }

            ComputePartnerAim(hostEye, hostForward, Front, Side, out Vector3 origin, out Vector3 direction);
            _target.UpdateAim(ClientId, origin, direction);
        }

        /// <summary>파트너의 눈 위치와 조준 방향. 방향은 호스트와 같고, 위치는 호스트 눈에서 수평으로 앞뒤·좌우로 옮긴다.</summary>
        public static void ComputePartnerAim(
            Vector3 hostEye, Vector3 hostForward, float front, float side,
            out Vector3 origin, out Vector3 direction)
        {
            direction = hostForward.sqrMagnitude > 1e-6f ? hostForward.normalized : Vector3.forward;
            Vector3 flatForward = Vector3.ProjectOnPlane(direction, Vector3.up);
            flatForward = flatForward.sqrMagnitude > 1e-6f ? flatForward.normalized : Vector3.forward;
            Vector3 flatRight = Vector3.Cross(Vector3.up, flatForward);
            origin = hostEye
                + flatForward * Mathf.Clamp(front, -MaxOffset, MaxOffset)
                + flatRight * Mathf.Clamp(side, -MaxOffset, MaxOffset);
        }

        private bool TryAttach(ITarget target, ulong hostClientId, Vector3 hostEye, Vector3 hostForward)
        {
            if (_target != null)
                return Fail("이미 파트너가 붙어 있습니다.");
            if (target == null || !target.Exists)
                return Fail("들고 있는 가구가 없습니다. 가구나 시체를 먼저 드세요(F12 입력 고정이 편합니다).");
            if (!target.IsServer)
                return Fail("호스트에서만 쓸 수 있습니다.");
            if (!target.ContainsHolder(hostClientId))
                return Fail("내가 들고 있는 대상이 아닙니다.");
            if (target.HolderCount != 1)
                return Fail("이미 두 명이 들고 있습니다.");

            ComputePartnerAim(hostEye, hostForward, Front, Side, out Vector3 origin, out Vector3 direction);
            _activeCount++;
            if (!target.TryAdd(ClientId, origin, direction))
            {
                _activeCount--;
                return Fail("서버가 두 번째 홀더를 받지 않았습니다(발사 중이거나 홀더 한도).");
            }

            _target = target;
            _hostClientId = hostClientId;
            Message = "가상 파트너를 붙였습니다 — 2인 운반 중.";
            return true;
        }

        private bool Fail(string message)
        {
            Message = message;
            return false;
        }

        private void Clear(string message)
        {
            if (_target != null)
                _activeCount = Mathf.Max(0, _activeCount - 1);
            _target = null;
            Message = message;
        }

        /// <summary>가구와 시체가 같은 잡기 서버 API 모양이라 한 인터페이스로 묶는다.</summary>
        private interface ITarget
        {
            bool Exists { get; }
            bool IsServer { get; }
            int HolderCount { get; }
            bool ContainsHolder(ulong clientId);
            bool TryAdd(ulong clientId, Vector3 origin, Vector3 direction);
            void UpdateAim(ulong clientId, Vector3 origin, Vector3 direction);
            void ForceRelease(ulong clientId);
        }

        private sealed class FurnitureAdapter : ITarget
        {
            private readonly FurnitureGrabTarget _furniture;

            public FurnitureAdapter(FurnitureGrabTarget furniture) => _furniture = furniture;

            public bool Exists => _furniture != null && _furniture.IsSpawned;
            public bool IsServer => _furniture.IsServer;
            public int HolderCount => _furniture.HolderCount;
            public bool ContainsHolder(ulong clientId) => _furniture.ContainsHolder(clientId);
            public bool TryAdd(ulong clientId, Vector3 origin, Vector3 direction)
                => _furniture.ServerTryAddHolder(clientId, origin, direction);
            public void UpdateAim(ulong clientId, Vector3 origin, Vector3 direction)
                => _furniture.ServerUpdateAim(clientId, origin, direction);
            public void ForceRelease(ulong clientId) => _furniture.ServerForceRelease(clientId);
        }

        private sealed class CorpseAdapter : ITarget
        {
            private readonly PlayerVisuals _corpse;

            public CorpseAdapter(PlayerVisuals corpse) => _corpse = corpse;

            public bool Exists => _corpse != null && _corpse.IsSpawned;
            public bool IsServer => _corpse.IsServer;
            public int HolderCount => _corpse.HolderCount;
            public bool ContainsHolder(ulong clientId) => _corpse.ContainsHolder(clientId);
            public bool TryAdd(ulong clientId, Vector3 origin, Vector3 direction)
                => _corpse.ServerTryAddHolder(clientId, origin, direction);
            public void UpdateAim(ulong clientId, Vector3 origin, Vector3 direction)
                => _corpse.ServerUpdateAim(clientId, origin, direction);
            public void ForceRelease(ulong clientId) => _corpse.ServerForceRelease(clientId);
        }
    }
}
