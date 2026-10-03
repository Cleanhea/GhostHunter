using System;
using Concentus.Structs;
using GhostHunter.Core.Voice;
using GhostHunter.Gameplay.Voice;
using GhostHunter.Systems.Voice;
using NUnit.Framework;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>마이크 노이즈 게이트와 Opus 블록(docs/architecture/voice-chat.md "캡처").</summary>
    public sealed class MicrophoneVoiceTests
    {
        private const float Frame = 0.02f;
        private const float Threshold = -50f;
        private const float Hysteresis = 6f;
        private const float Hold = 0.5f;

        [Test]
        public void 게이트는_기준_이상에서_열리고_여는_순간만_알린다()
        {
            var gate = new VoiceNoiseGate();

            Assert.IsFalse(gate.Step(-60f, Frame, Threshold, Hysteresis, Hold));
            Assert.IsFalse(gate.IsOpen);
            Assert.IsTrue(gate.Step(-40f, Frame, Threshold, Hysteresis, Hold), "여는 프레임은 앞당김 신호를 준다");
            Assert.IsTrue(gate.IsOpen);
            Assert.IsFalse(gate.Step(-40f, Frame, Threshold, Hysteresis, Hold), "이미 열려 있으면 다시 알리지 않는다");
        }

        [Test]
        public void 게이트는_유지_시간이_지나야_닫힌다()
        {
            var gate = new VoiceNoiseGate();
            gate.Step(-40f, Frame, Threshold, Hysteresis, Hold);

            // 0.48초 — 아직 열려 있다(말끝·숨).
            for (int i = 0; i < 24; i++)
                gate.Step(-80f, Frame, Threshold, Hysteresis, Hold);
            Assert.IsTrue(gate.IsOpen);

            gate.Step(-80f, Frame, Threshold, Hysteresis, Hold);
            Assert.IsFalse(gate.IsOpen);
        }

        [Test]
        public void 여유_구간에서는_열린_채로_닫힘_시계도_멈춘다()
        {
            var gate = new VoiceNoiseGate();
            gate.Step(-40f, Frame, Threshold, Hysteresis, Hold);

            for (int i = 0; i < 100; i++)
                gate.Step(-53f, Frame, Threshold, Hysteresis, Hold);

            Assert.IsTrue(gate.IsOpen, "기준-여유(-56) 위의 조용한 말소리로는 닫히지 않는다");
        }

        [Test]
        public void 말_사이에_다시_커지면_닫힘_시계가_처음부터_돈다()
        {
            var gate = new VoiceNoiseGate();
            gate.Step(-40f, Frame, Threshold, Hysteresis, Hold);
            for (int i = 0; i < 20; i++)
                gate.Step(-80f, Frame, Threshold, Hysteresis, Hold);
            gate.Step(-40f, Frame, Threshold, Hysteresis, Hold);
            for (int i = 0; i < 20; i++)
                gate.Step(-80f, Frame, Threshold, Hysteresis, Hold);

            Assert.IsTrue(gate.IsOpen);
        }

        [Test]
        public void Opus_블록은_압축했다_풀면_같은_길이와_비슷한_크기로_돌아온다()
        {
            OpusEncoder encoder = OpusVoiceBlock.CreateEncoder(24000, 5);
            IVoiceDecoder decoder = new OpusVoiceBlock.Decoder();
            var frame = new float[OpusVoiceBlock.FrameSamples];
            var encoded = new byte[OpusVoiceBlock.MaximumFrameBytes];
            var scratch = new short[OpusVoiceBlock.FrameSamples];
            var block = new byte[1024];
            var decoded = new float[48000];

            // 인코더·디코더가 자리를 잡도록 440Hz 사인파 10프레임을 이어서 보낸다.
            double totalIn = 0, totalOut = 0;
            for (int f = 0; f < 10; f++)
            {
                for (int i = 0; i < frame.Length; i++)
                    frame[i] = 0.3f * (float)Math.Sin(2 * Math.PI * 440 * (f * frame.Length + i) / OpusVoiceBlock.SampleRate);

                int length = OpusVoiceBlock.EncodeFrame(encoder, frame, scratch, encoded);
                Assert.That(length, Is.InRange(1, OpusVoiceBlock.MaximumFrameBytes));

                block[0] = (byte)length;
                Buffer.BlockCopy(encoded, 0, block, 1, length);
                int samples = decoder.Decode(block, length + 1, decoded);
                Assert.AreEqual(OpusVoiceBlock.FrameSamples, samples);

                if (f < 5)
                    continue;
                for (int i = 0; i < samples; i++)
                {
                    totalIn += frame[i] * frame[i];
                    totalOut += decoded[i] * decoded[i];
                }
            }

            double ratio = Math.Sqrt(totalOut / totalIn);
            Assert.That(ratio, Is.InRange(0.7, 1.3), $"복원된 소리 크기가 원본과 비슷해야 한다 (비율 {ratio:F3})");
        }

        [Test]
        public void 블록_하나에_여러_프레임을_이어_담을_수_있다()
        {
            OpusEncoder encoder = OpusVoiceBlock.CreateEncoder(24000, 5);
            var frame = new float[OpusVoiceBlock.FrameSamples];
            var encoded = new byte[OpusVoiceBlock.MaximumFrameBytes];
            var scratch = new short[OpusVoiceBlock.FrameSamples];
            var block = new byte[1024];
            int offset = 0;
            for (int f = 0; f < 3; f++)
            {
                int length = OpusVoiceBlock.EncodeFrame(encoder, frame, scratch, encoded);
                block[offset++] = (byte)length;
                Buffer.BlockCopy(encoded, 0, block, offset, length);
                offset += length;
            }

            int samples = new OpusVoiceBlock.Decoder().Decode(block, offset, new float[48000]);

            Assert.AreEqual(3 * OpusVoiceBlock.FrameSamples, samples);
        }

        [Test]
        public void 깨진_블록은_예외_없이_거기까지만_푼다()
        {
            var block = new byte[] { 200, 1, 2, 3 };

            Assert.AreEqual(0, new OpusVoiceBlock.Decoder().Decode(block, block.Length, new float[48000]));
        }
    }
}
