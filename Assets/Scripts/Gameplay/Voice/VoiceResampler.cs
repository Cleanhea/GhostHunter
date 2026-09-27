using System;

namespace GhostHunter.Gameplay.Voice
{
    /// <summary>
    /// 디코딩한 음성(24kHz)을 오디오 출력 샘플레이트로 바꾸는 선형 보간 리샘플러.
    /// 패킷 경계를 넘어 위상을 이어 가므로 블록마다 딸깍 소리가 나지 않는다. 메인 스레드(수신 RPC)에서만 쓴다.
    /// </summary>
    public sealed class VoiceResampler
    {
        private readonly double _step;
        private double _position;
        private float _previous;
        private bool _hasPrevious;

        public VoiceResampler(int inputRate, int outputRate)
        {
            _step = inputRate / (double)Math.Max(1, outputRate);
        }

        /// <summary>입력 <paramref name="count"/>개가 만들 수 있는 출력 샘플 수의 상한.</summary>
        public int MaximumOutput(int count) => (int)Math.Ceiling(count / _step) + 2;

        /// <summary>입력을 이어 붙여 변환하고 쓴 출력 샘플 수를 돌려준다. 첫 샘플은 다음 샘플과의 보간 기준으로만 쓴다.</summary>
        public int Process(float[] input, int count, float[] output)
        {
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                float current = input[i];
                if (!_hasPrevious)
                {
                    _previous = current;
                    _hasPrevious = true;
                    continue;
                }

                while (_position < 1d && written < output.Length)
                {
                    output[written++] = _previous + (current - _previous) * (float)_position;
                    _position += _step;
                }

                _position -= 1d;
                _previous = current;
            }

            return written;
        }

        public void Reset()
        {
            _hasPrevious = false;
            _position = 0d;
        }
    }
}
