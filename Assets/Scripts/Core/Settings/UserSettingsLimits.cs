namespace GhostHunter.Core.Settings
{
    /// <summary>설정 값의 허용 범위와 기본값. 저장소는 이 범위로 자르고, 설정 창은 이 범위로 슬라이더를 만든다.</summary>
    public static class UserSettingsLimits
    {
        public const float MinMouseSensitivity = 0.1f;
        public const float MaxMouseSensitivity = 3f;
        public const float DefaultMouseSensitivity = 1f;

        public const float MinMicGainDb = -12f;
        public const float MaxMicGainDb = 12f;

        public const float MinNoiseGateThresholdDb = -70f;
        public const float MaxNoiseGateThresholdDb = -20f;
        public const float DefaultNoiseGateThresholdDb = -50f;

        /// <summary>설정 창이 고를 수 있는 프레임 상한. 0 은 무제한.</summary>
        public static readonly int[] FrameRateLimits = { 0, 30, 60, 120, 144, 240 };
    }
}
