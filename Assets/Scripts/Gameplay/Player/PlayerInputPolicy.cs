namespace GhostHunter.Gameplay.Player
{
    /// <summary>잠금 우선순위와 모드별 허용 입력. 액션 읽기와 즉시 초기화는 Reader가 맡는다.</summary>
    internal readonly struct PlayerInputPolicy
    {
        private enum Mode { Locked, Spectator, Frozen, Underground, Wheel, Gameplay }

        private readonly Mode _mode;

        public bool CanReadGameplay => _mode == Mode.Gameplay;
        public bool CanMove => _mode == Mode.Gameplay || _mode == Mode.Wheel || _mode == Mode.Underground;
        public bool CanLook => _mode == Mode.Gameplay || _mode == Mode.Underground;
        public bool CanReadStance => _mode == Mode.Gameplay || _mode == Mode.Wheel;
        public bool CanReadWheel => _mode == Mode.Gameplay || _mode == Mode.Wheel;
        public bool CanBurrow => _mode == Mode.Gameplay || _mode == Mode.Frozen || _mode == Mode.Underground;
        public bool CanUseHeadlamp => CanBurrow || _mode == Mode.Wheel;
        public bool CanSpectate => _mode == Mode.Spectator;

        private PlayerInputPolicy(Mode mode) => _mode = mode;

        public static PlayerInputPolicy Resolve(
            bool menuLocked, bool restoring, bool deathLocked, SkillInputMode skillMode, bool wheelLocked)
        {
            if (menuLocked || restoring)
                return new PlayerInputPolicy(Mode.Locked);
            if (deathLocked)
                return new PlayerInputPolicy(Mode.Spectator);
            if (skillMode != SkillInputMode.None)
                return new PlayerInputPolicy(skillMode == SkillInputMode.UndergroundMove ? Mode.Underground : Mode.Frozen);
            return new PlayerInputPolicy(wheelLocked ? Mode.Wheel : Mode.Gameplay);
        }
    }
}
