namespace GhostHunter.Gameplay.Voice
{
    /// <summary>
    /// 오픈 마이크의 노이즈 게이트 판정. 20ms 프레임마다 크기를 넣으면 지금 보내도 되는지 답한다.
    /// 기준 이상이면 열고, <c>기준 - 여유</c> 아래로 <c>유지 시간</c> 동안 머물러야 닫는다 —
    /// 말 사이 숨·말끝이 잘리지 않게 한다. 여는 순간의 앞소리는 호출자가 앞당겨 보낸다.
    /// </summary>
    public sealed class VoiceNoiseGate
    {
        private float _belowSeconds;

        public bool IsOpen { get; private set; }

        /// <returns>이번 프레임에 막 열렸으면 true. 앞당김 프레임을 보낼 신호다.</returns>
        public bool Step(float frameDb, float frameSeconds, float thresholdDb, float hysteresisDb, float holdSeconds)
        {
            if (frameDb >= thresholdDb)
            {
                _belowSeconds = 0f;
                if (IsOpen)
                    return false;

                IsOpen = true;
                return true;
            }

            if (!IsOpen)
                return false;

            // 여유 구간(기준 - 여유 ~ 기준)은 열린 상태를 유지하되 닫힘 시계도 돌리지 않는다.
            if (frameDb >= thresholdDb - hysteresisDb)
                return false;

            _belowSeconds += frameSeconds;
            if (_belowSeconds >= holdSeconds)
            {
                IsOpen = false;
                _belowSeconds = 0f;
            }

            return false;
        }

        public void Reset()
        {
            IsOpen = false;
            _belowSeconds = 0f;
        }
    }
}
