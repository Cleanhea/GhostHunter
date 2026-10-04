using System;
using System.Threading;
using GhostHunter.Data;
using GhostHunter.Gameplay.Voice;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    public sealed class VoiceTests
    {
        [TestCase(0f, 1f)] [TestCase(1.5f, 1f)] [TestCase(3f, 0.5358867f)] [TestCase(5f, 0.3383835f)]
        [TestCase(7f, 0.2499735f)] [TestCase(10f, 0.167903f)]
        [TestCase(15f, 0.0733941f)] [TestCase(20f, 0.0183286f)]
        [TestCase(25f, 0f)] [TestCase(27f, 0f)]
        public void Horizontal_Boundaries(float distance, float expected)
        {
            var settings = ScriptableObject.CreateInstance<VoiceChatSettings>();
            try
            {
                Assert.That(VoiceAttenuation.Horizontal(distance, settings.MinimumDistance, settings.FadeDistance,
                    settings.MaximumDistance, settings.RolloffExponent),
                    Is.EqualTo(expected).Within(0.0001f));
            }
            finally { UnityEngine.Object.DestroyImmediate(settings); }
        }
        [Test]
        public void Horizontal_ApproachesBoundaryContinuously()
        {
            var settings = ScriptableObject.CreateInstance<VoiceChatSettings>();
            try
            {
                float previous = 1f;
                for (int i = 0; i <= 2500; i++)
                {
                    float current = VoiceAttenuation.Horizontal(i / 100f, settings.MinimumDistance, settings.FadeDistance,
                        settings.MaximumDistance, settings.RolloffExponent);
                    Assert.That(current, Is.InRange(0f, previous));
                    Assert.That(previous - current, Is.LessThanOrEqualTo(0.007f));
                    previous = current;
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(settings); }
        }
        [TestCase(0f, 1f)] [TestCase(0.8f, 1f)] [TestCase(1.5f, 0.8819242f)]
        [TestCase(2.6f, 0f)] [TestCase(3f, 0f)]
        public void Vertical_FloorAndStairBoundaries(float distance, float expected)
            => Assert.That(VoiceAttenuation.Vertical(distance, 1.2f, 2.6f), Is.EqualTo(expected).Within(0.001f));
        [TestCase(0, 1f)] [TestCase(1, 0.32f)] [TestCase(2, 0.1024f)] [TestCase(3, 0.04f)] [TestCase(8, 0.04f)]
        public void Occlusion_CompoundsAndClamps(int walls, float expected)
            => Assert.That(VoiceAttenuation.Occlusion(walls, 0.32f, 3, 0.04f), Is.EqualTo(expected).Within(0.0001f));
        [Test]
        public void Smoothing_IsFrameRateIndependent()
        {
            float value = 0f;
            for (int i = 0; i < 60; i++) value = VoiceAttenuation.Smooth(value, 1f, 1f / 60f, 0.12f);
            Assert.That(value, Is.EqualTo(VoiceAttenuation.Smooth(0f, 1f, 1f, 0.12f)).Within(0.00001f));
        }
        [TestCase(true, true, 25f, 0f, true)] [TestCase(true, true, 27f, 3.6f, true)]
        [TestCase(true, true, 27.01f, 0f, false)]
        [TestCase(true, true, 0f, 3.61f, false)] [TestCase(false, false, 1000f, 1000f, true)]
        [TestCase(false, true, 0f, 0f, false)] [TestCase(true, false, 0f, 0f, false)]
        public void Relay_EnforcesChannelAndMargins(bool speaker, bool listener, float horizontal, float vertical, bool expected)
        {
            var settings = ScriptableObject.CreateInstance<VoiceChatSettings>();
            try
            {
                Assert.That(VoiceAttenuation.CanRelay(speaker, listener, horizontal, vertical, settings.MaximumDistance,
                    settings.VerticalCut, settings.ServerMarginXZ, settings.ServerMarginY), Is.EqualTo(expected));
            }
            finally { UnityEngine.Object.DestroyImmediate(settings); }
        }
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        public void ResultRelay_CombinesAllPlayersWithoutDistanceLimit(bool speaker, bool listener)
            => Assert.IsTrue(VoiceAttenuation.CanRelay(speaker, listener, 1000f, 1000f,
                25f, 2.6f, 2f, 1f, resultChannel: true));
        [Test]
        public void Gate_HysteresisAndHangoverPreserveSentence()
        {
            var gate = new VoiceActivityGate();
            Assert.IsFalse(gate.Step(-45f, 0.02f, -42f, -48f, 0.35f));
            Assert.IsTrue(gate.Step(-40f, 0.02f, -42f, -48f, 0.35f));
            for (int i = 0; i < 100; i++) Assert.IsTrue(gate.Step(-45f, 0.02f, -42f, -48f, 0.35f));
            for (int i = 0; i < 17; i++) Assert.IsTrue(gate.Step(-60f, 0.02f, -42f, -48f, 0.35f));
            Assert.IsFalse(gate.Step(-60f, 0.02f, -42f, -48f, 0.35f));
        }
        [Test]
        public void Gate_DecibelsUseRms()
            => Assert.That(VoiceActivityGate.Decibels(new[] { 0.1f, -0.1f }, 0, 2), Is.EqualTo(-20f).Within(0.001f));
        [Test]
        public void Queue_PreservesPreRollAndDropsExpired()
        {
            var queue = new VoiceFrameQueue();
            for (int i = 0; i < 5; i++) queue.Push(new[] { (byte)i }, 1, i * 0.05);
            queue.DiscardBefore(0.06);
            var packet = new byte[512];
            Assert.That(queue.Pack(packet), Is.EqualTo(9));
            Assert.That(packet[2], Is.EqualTo(2));
            Assert.That(packet[8], Is.EqualTo(4));
        }
        [Test]
        public void Queue_OverMtuKeepsCompleteNewestBlock()
        {
            var queue = new VoiceFrameQueue();
            queue.Push(new byte[600], 600, 0);
            var newest = new byte[600]; newest[0] = 9;
            queue.Push(newest, 600, 0.05);
            var packet = new byte[VoiceFrameQueue.MaximumPayload];
            Assert.That(queue.Pack(packet), Is.EqualTo(602));
            Assert.That(packet[2], Is.EqualTo(9));
        }
        [Test]
        public void Queue_KeepsBlockAccumulatedDuringFrameStall()
        {
            // 프레임이 멈춘 뒤 한 번에 읽힌 블록(실측 573~641B)이 512B 상한에 걸려 통째로 버려지던 회귀.
            var queue = new VoiceFrameQueue();
            queue.Push(new byte[700], 700, 0);
            var packet = new byte[VoiceFrameQueue.MaximumPayload];
            Assert.That(queue.Pack(packet), Is.EqualTo(702));
        }
        [Test]
        public void Buffer_UnderflowWritesSilence()
        {
            var buffer = new VoicePcmBuffer(16, 8);
            buffer.Write(new[] { 1f, 2f }, 2);
            var output = new float[4];
            buffer.Read(output);
            Assert.That(output, Is.EqualTo(new[] { 1f, 2f, 0f, 0f }));
        }
        [Test]
        public void Buffer_OverflowSkipsOldSamples()
        {
            var buffer = new VoicePcmBuffer(8, 4);
            buffer.Write(new[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f }, 8);
            var output = new float[4]; buffer.Read(output);
            Assert.That(output, Is.EqualTo(new[] { 5f, 6f, 7f, 8f }));
        }
        [Test]
        public void Buffer_ClearWhileStoppedCanRestart()
        {
            var buffer = new VoicePcmBuffer(4, 4);
            buffer.Write(new[] { 1f, 2f, 3f, 4f }, 4);
            buffer.Clear();
            buffer.Write(new[] { 5f, 6f }, 2);
            var output = new float[4]; buffer.Read(output);
            Assert.That(output, Is.EqualTo(new[] { 5f, 6f, 0f, 0f }));
        }
        [Test]
        public void Buffer_ConcurrentReaderAndWriterNeverReverseSamples()
        {
            var buffer = new VoicePcmBuffer(1024, 256);
            int done = 0;
            var writer = new Thread(() =>
            {
                var input = new float[32];
                for (int batch = 0; batch < 10000; batch++)
                {
                    for (int i = 0; i < input.Length; i++) input[i] = batch * 32 + i + 1;
                    buffer.Write(input, input.Length);
                    if (batch % 100 == 0) buffer.Clear();
                    Thread.Yield();
                }
                Volatile.Write(ref done, 1);
            });
            writer.Start();
            try
            {
                float previous = 0;
                var output = new float[64];
                while (Volatile.Read(ref done) == 0 || buffer.Count > 0)
                {
                    buffer.Read(output);
                    foreach (float sample in output)
                    {
                        if (sample == 0) continue;
                        Assert.That(sample, Is.GreaterThan(previous));
                        previous = sample;
                    }
                }
            }
            finally { Assert.IsTrue(writer.Join(5000)); }
        }
        [Test]
        public void Limiter_Rejects170Of200PacketsAndRecoversAfterWindow()
        {
            var limiter = new VoicePacketLimiter();
            int accepted = 0;
            for (int i = 0; i < 200; i++) if (limiter.Accept(i / 200d, 30)) accepted++;
            Assert.That(accepted, Is.EqualTo(30));
            Assert.IsTrue(limiter.Accept(1d, 30));
        }
        [TestCase(new byte[] { 1, 0, 42 }, true)]
        [TestCase(new byte[] { 0, 0, 42 }, false)]
        [TestCase(new byte[] { 2, 0, 42 }, false)]
        [TestCase(new byte[] { 1, 0, 42, 1 }, false)]
        public void Packet_RejectsTruncatedAndEmptyBlocks(byte[] bytes, bool expected)
        {
            using var packet = new NativeArray<byte>(bytes, Allocator.Temp);
            Assert.That(PlayerVoiceEmitter.ValidatePacket(packet), Is.EqualTo(expected));
        }
        [Test]
        public void Input_PttAndSpectatorHaveDifferentBindings()
        {
            var actions = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            Assert.That(actions.FindAction("Player/Voice").bindings[0].path, Is.EqualTo("<Keyboard>/v"));
            Assert.That(actions.FindAction("Player/SpectateToggleMode").bindings[0].path, Is.EqualTo("<Keyboard>/c"));
        }
        [Test]
        public void Transmit_IgnoresLoudnessInOpenMicAndFollowsPttKey()
        {
            // 소리 크기는 인자에 없다 — 오픈 마이크는 조용한 소리도 그대로 보낸다(2026-09-27).
            Assert.IsTrue(PlayerVoiceEmitter.ShouldTransmit(GhostHunter.Core.Voice.VoiceMode.OpenMic, false));
            Assert.IsTrue(PlayerVoiceEmitter.ShouldTransmit(GhostHunter.Core.Voice.VoiceMode.PushToTalk, true));
            Assert.IsFalse(PlayerVoiceEmitter.ShouldTransmit(GhostHunter.Core.Voice.VoiceMode.PushToTalk, false));
        }
        [Test]
        public void Resampler_DoublesRateWithLinearInterpolation()
        {
            var resampler = new VoiceResampler(24000, 48000);
            var output = new float[16];
            int written = resampler.Process(new[] { 0f, 1f, 0f }, 3, output);
            Assert.That(written, Is.EqualTo(4));
            Assert.That(new[] { output[0], output[1], output[2], output[3] }, Is.EqualTo(new[] { 0f, 0.5f, 1f, 0.5f }));
        }
        [Test]
        public void Resampler_KeepsPhaseAcrossPackets()
        {
            // 24kHz → 44.1kHz 처럼 나눠떨어지지 않는 비율에서도 패킷을 나눠 넣은 결과가 한 번에 넣은 결과와 같아야 한다.
            var input = new float[480];
            for (int i = 0; i < input.Length; i++) input[i] = (float)Math.Sin(i * 0.05);
            var whole = new VoiceResampler(24000, 44100);
            var split = new VoiceResampler(24000, 44100);
            var expected = new float[whole.MaximumOutput(input.Length)];
            int expectedCount = whole.Process(input, input.Length, expected);
            var actual = new float[expected.Length];
            var chunk = new float[160];
            int actualCount = 0;
            for (int offset = 0; offset < input.Length; offset += chunk.Length)
            {
                Array.Copy(input, offset, chunk, 0, chunk.Length);
                var part = new float[split.MaximumOutput(chunk.Length)];
                int count = split.Process(chunk, chunk.Length, part);
                Array.Copy(part, 0, actual, actualCount, count);
                actualCount += count;
            }
            Assert.That(actualCount, Is.EqualTo(expectedCount));
            for (int i = 0; i < expectedCount; i++) Assert.That(actual[i], Is.EqualTo(expected[i]).Within(1e-5f));
        }
        [Test]
        public void Buffer_ReadReportsHowManySamplesWereReal()
        {
            var buffer = new VoicePcmBuffer(16, 16);
            buffer.Write(new[] { 1f, 2f, 3f }, 3);
            var output = new float[8];
            Assert.That(buffer.Read(output, 5), Is.EqualTo(3));
            Assert.That(output, Is.EqualTo(new[] { 1f, 2f, 3f, 0f, 0f, 0f, 0f, 0f }));
        }
    }
}
