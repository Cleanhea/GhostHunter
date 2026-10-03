using System;
using Concentus.Enums;
using Concentus.Structs;
using GhostHunter.Core.Voice;

namespace GhostHunter.Systems.Voice
{
    /// <summary>
    /// 캡처 블록 형식과 Opus 인코딩·디코딩. 블록은 <c>[프레임 길이 1B][Opus 프레임]</c> 의 반복이다.
    /// 프레임은 48kHz mono 20ms(960 샘플)이고 한 프레임은 255B 를 넘지 않는다(24kbps 에서 약 60B).
    /// 48kHz 인 이유: Concentus 1.1.7 은 24kHz VOIP(SILK) 에서 무음을 풀어 냈다 — 16·48kHz 는 정상(2026-10-03 측정).
    /// </summary>
    public static class OpusVoiceBlock
    {
        public const int SampleRate = 48000;
        public const int FrameSamples = SampleRate / 50;
        public const int MaximumFrameBytes = 255;
        /// <summary>디코더 한 번이 낼 수 있는 최대 샘플 수(120ms). Opus 프레임 상한이다.</summary>
        private const int MaximumDecodedSamples = SampleRate * 120 / 1000;

        public static OpusEncoder CreateEncoder(int bitrate, int complexity)
        {
            OpusEncoder encoder = OpusEncoder.Create(SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
            encoder.Bitrate = bitrate;
            encoder.Complexity = complexity;
            encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
            encoder.UseVBR = true;
            return encoder;
        }

        /// <summary>
        /// 20ms 프레임 하나를 압축해 <paramref name="output"/> 에 쓰고 길이를 돌려준다. 실패하면 0.
        /// Concentus 1.1.7 의 float 경로는 ±1.0 입력에서 무음을 냈다(2026-10-03 테스트) — 16bit 로 바꿔 넣는다.
        /// </summary>
        /// <param name="scratch">FrameSamples 이상의 임시 버퍼. 호출자가 소유한다.</param>
        public static int EncodeFrame(OpusEncoder encoder, float[] frame, short[] scratch, byte[] output)
        {
            for (int i = 0; i < FrameSamples; i++)
                scratch[i] = ToShort(frame[i]);

            try
            {
                int length = encoder.Encode(scratch, 0, FrameSamples, output, 0, MaximumFrameBytes);
                return length > 0 && length <= MaximumFrameBytes ? length : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>블록 안의 프레임을 차례로 풀어 이어 붙인다. 형식이 깨졌으면 그때까지 푼 것만 돌려준다.</summary>
        /// <param name="scratch">MaximumDecodedSamples 이상의 임시 버퍼.</param>
        public static int DecodeBlock(OpusDecoder decoder, byte[] block, int count, short[] scratch, float[] samples)
        {
            int written = 0;
            int offset = 0;
            while (offset < count)
            {
                int length = block[offset++];
                if (length == 0 || length > count - offset)
                    break;

                int room = Math.Min(MaximumDecodedSamples, samples.Length - written);
                if (room < FrameSamples)
                    break;

                try
                {
                    int decoded = decoder.Decode(block, offset, length, scratch, 0, room, false);
                    for (int i = 0; i < decoded; i++)
                        samples[written + i] = scratch[i] / 32768f;
                    written += Math.Max(0, decoded);
                }
                catch (Exception)
                {
                    // 손상된 원격 프레임 하나 때문에 나머지를 버리지 않는다.
                }

                offset += length;
            }

            return written;
        }

        private static short ToShort(float sample)
        {
            float scaled = sample * 32767f;
            return (short)(scaled > 32767f ? 32767f : scaled < -32768f ? -32768f : scaled);
        }

        /// <summary>화자 한 명의 Opus 디코더. 상태(예측·손실 은닉)를 가지므로 화자마다 따로 쓴다.</summary>
        public sealed class Decoder : IVoiceDecoder
        {
            private readonly OpusDecoder _decoder = OpusDecoder.Create(SampleRate, 1);
            private readonly short[] _scratch = new short[MaximumDecodedSamples];

            public int Decode(byte[] compressed, int count, float[] samples)
                => DecodeBlock(_decoder, compressed, count, _scratch, samples);
        }
    }
}
