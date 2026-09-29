using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>부활 의식 결과로 붙는 폐급 두더지 종류(revival-system.md §9). 값은 네트워크로 복제하므로 바꾸지 않는다.</summary>
    public enum DefectKind : byte
    {
        None = 0,
        Follower = 1,
        Screamer = 2,
        InvertedKeys = 3,
        VoiceModulated = 4,
        CursedRandomMove = 5,
    }

    /// <summary>
    /// 플레이어의 폐급 상태. 서버가 부활 때 정하고 모두에게 복제한다 — 스테이지가 끝나면 플레이어가 새로 스폰되므로
    /// 저절로 풀린다(§8 "스테이지 종료까지 유지"). 효과는 소유자 입력에서 낸다: 키 반대, 20초마다 3초 제멋대로 이동.
    /// 따라다니기·소리지르기·목소리 변조는 아직 표시만 한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerDefectState : NetworkBehaviour
    {
        private const float CurseInterval = 20f;
        private const float CurseDuration = 3f;
        private const float CurseDirectionChange = 0.6f;

        private readonly NetworkVariable<DefectKind> _kind = new(
            DefectKind.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private PlayerInputReader _input;
        private float _curseTimer;
        private float _curseRemaining;
        private float _directionTimer;
        private Vector2 _forcedDirection;

        public DefectKind Kind => _kind.Value;

        private void Awake() => _input = GetComponent<PlayerInputReader>();

        public void ServerApply(DefectKind kind)
        {
            if (IsServer && IsSpawned)
                _kind.Value = kind;
        }

        public override void OnNetworkDespawn()
        {
            if (_input != null)
                _input.SetMoveModifiers(false, null);
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || _input == null)
                return;

            DefectKind kind = _kind.Value;
            Vector2? forced = null;
            if (kind == DefectKind.CursedRandomMove)
                forced = TickCurse(Time.deltaTime);

            _input.SetMoveModifiers(kind == DefectKind.InvertedKeys, forced);
        }

        /// <summary>20초마다 3초 동안 이동 입력을 무작위 방향으로 덮어쓴다. 방향은 0.6초마다 바뀐다.</summary>
        private Vector2? TickCurse(float deltaTime)
        {
            if (_curseRemaining > 0f)
            {
                _curseRemaining -= deltaTime;
                _directionTimer -= deltaTime;
                if (_directionTimer <= 0f)
                {
                    _directionTimer = CurseDirectionChange;
                    _forcedDirection = Random.insideUnitCircle.normalized;
                }
                return _forcedDirection;
            }

            _curseTimer += deltaTime;
            if (_curseTimer >= CurseInterval)
            {
                _curseTimer = 0f;
                _curseRemaining = CurseDuration;
                _directionTimer = 0f;
            }
            return null;
        }

        private void OnGUI()
        {
            if (!IsOwner || _kind.Value == DefectKind.None)
                return;

            string text = _kind.Value switch
            {
                DefectKind.InvertedKeys => "폐급 두더지 — 키가 반대로 눌린다",
                DefectKind.CursedRandomMove => _curseRemaining > 0f
                    ? "저주 — 몸이 제멋대로 움직인다!"
                    : "개좃같은 폐급 두더지 — 20초마다 저주",
                DefectKind.Follower => "폐급 두더지 — 한 명을 졸졸 따라다님 (효과 준비 중)",
                DefectKind.Screamer => "폐급 두더지 — 갑자기 소리지름 (효과 준비 중)",
                DefectKind.VoiceModulated => "폐급 두더지 — 목소리 변조 (효과 준비 중)",
                _ => string.Empty,
            };
            GUI.Label(new Rect(16f, Screen.height - 60f, 480f, 24f), text);
        }
    }
}
