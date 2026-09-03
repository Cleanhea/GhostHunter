namespace GhostHunter.Gameplay.Player
{
    /// <summary>플레이어의 3단 자세. 값이 클수록 낮다. 우선순위: 엎드리기 &gt; 웅크리기 &gt; 서기.</summary>
    public enum PlayerStance : byte
    {
        Standing = 0,
        Crouching = 1,
        Prone = 2,
    }

    /// <summary>
    /// 자세별 캡슐 높이·카메라 높이·이동 속도를 정하는 순수 계산. <see cref="PlayerMotor"/> 는
    /// NetworkBehaviour 라 EditMode 로 직접 돌리기 어려워서, 순수 규칙만 여기로 뺐다
    /// (<c>GhostStateMachine</c> 을 상태 기계에서 분리한 것과 같은 이유).
    /// </summary>
    public static class PlayerPosture
    {
        /// <summary>두 자세 bool 에서 실제 자세를 정한다 — 엎드리기가 웅크리기를 덮는다.</summary>
        public static PlayerStance Resolve(bool prone, bool crouching)
        {
            if (prone)
                return PlayerStance.Prone;

            return crouching ? PlayerStance.Crouching : PlayerStance.Standing;
        }

        public static float CapsuleHeight(PlayerStance stance, PlayerMoveSettings settings) => stance switch
        {
            PlayerStance.Prone => settings.ProneHeight,
            PlayerStance.Crouching => settings.CrouchHeight,
            _ => settings.StandingHeight,
        };

        public static float CameraHeight(PlayerStance stance, PlayerMoveSettings settings) => stance switch
        {
            PlayerStance.Prone => settings.ProneCameraHeight,
            PlayerStance.Crouching => settings.CrouchCameraHeight,
            _ => settings.StandingCameraHeight,
        };

        /// <summary>엎드리기 &gt; 웅크리기 &gt; 달리기 &gt; 걷기 순으로 이동 속도를 정한다.
        /// 엎드리거나 웅크리는 동안에는 달리기 입력을 무시한다.</summary>
        public static float MoveSpeed(PlayerStance stance, bool sprintHeld, PlayerMoveSettings settings) => stance switch
        {
            PlayerStance.Prone => settings.ProneMoveSpeed,
            PlayerStance.Crouching => settings.CrouchMoveSpeed,
            _ => sprintHeld ? settings.MoveSpeed * settings.SprintMultiplier : settings.MoveSpeed,
        };
    }
}
