using System;
using System.Threading;
using GhostHunter.Gameplay.Voice;
using NUnit.Framework;
using Unity.Collections;

namespace GhostHunter.Tests.EditMode
{
    public sealed class VoiceTests
    {
        [TestCase(0f, 1f)] [TestCase(1.5f, 1f)] [TestCase(10f, 0f)] [TestCase(12f, 0f)]
        public void Horizontal_Boundaries(float distance, float expected)
            => Assert.That(VoiceAttenuation.Horizontal(distance, 1.5f, 7f, 10f, 0.9f), Is.EqualTo(expected).Within(0.0001f));
        [Test]
        public void Horizontal_ApproachesBoundaryContinuously()
        {
            float previous = 1f;
            for (int i = 0; i <= 1000; i++)
            {
                float current = VoiceAttenuation.Horizontal(i / 100f, 1.5f, 7f, 10f, 0.9f);
                Assert.That(current, Is.InRange(0f, previous));
                previous = current;
            }
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
        [TestCase(true, true, 12f, 3.6f, true)] [TestCase(true, true, 12.01f, 0f, false)]
        [TestCase(true, true, 0f, 3.61f, false)] [TestCase(false, false, 1000f, 1000f, true)]
        [TestCase(false, true, 0f, 0f, false)] [TestCase(true, false, 0f, 0f, false)]
        public void Relay_EnforcesChannelAndMargins(bool speaker, bool listener, float horizontal, float vertical, bool expected)
            => Assert.That(VoiceAttenuation.CanRelay(speaker, listener, horizontal, vertical, 10f, 2.6f, 2f, 1f), Is.EqualTo(expected));
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
            queue.Push(new byte[300], 300, 0);
            var newest = new byte[300]; newest[0] = 9;
            queue.Push(newest, 300, 0.05);
            var packet = new byte[512];
            Assert.That(queue.Pack(packet), Is.EqualTo(302));
            Assert.That(packet[2], Is.EqualTo(9));
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
        public void Installation_HasRequiredReferences() => GhostHunter.EditorTools.VoiceChatSetup.ValidateInstallation();
    }
}
