namespace WallstopStudios.DxAudio.Tests.Editor
{
    using NUnit.Framework;

    public sealed class DspDelayTests
    {
        [TestCase(8_000, 2, TestName = "Delay repeats every two frames at 8 kHz")]
        [TestCase(16_000, 4, TestName = "Delay repeats every four frames at 16 kHz")]
        public void TheEchoRepeatsAcrossBlocksInItsOwnChannel(int sampleRate, int delayFrames)
        {
            DspDelay delay = new()
            {
                DelaySeconds = 0.00025f,
                Feedback = 0.5f,
                EchoLevel = 0.5f,
            };
            DspChain chain = new();
            chain.Add(delay);
            Assert.That(chain.TryPrepare(sampleRate, 3, 2), Is.True);

            float[] audio = new float[(delayFrames * 2 + 3) * 2];
            audio[0] = 7f;
            audio[2] = 1f;
            audio[5] = -0.5f;
            float[] expected = (float[])audio.Clone();
            expected[delayFrames * 2 + 2] = 0.5f;
            expected[delayFrames * 2 + 5] = -0.25f;
            expected[delayFrames * 4 + 2] = 0.25f;
            expected[delayFrames * 4 + 5] = -0.125f;
            for (int offset = 2, audioLength = audio.Length; offset < audioLength; offset += 2)
            {
                chain.Process(audio, offset, 2, 2);
            }

            Assert.That(audio, Is.EqualTo(expected));
            Assert.That(chain.LatencySamples, Is.Zero);
        }

        [Test]
        public void ResetDiscardsPendingEchoesAndStartsTheNextCue()
        {
            DspDelay delay = new()
            {
                DelaySeconds = 0.00025f,
                Feedback = 0.5f,
                EchoLevel = 0.5f,
            };
            Assert.That(delay.TryPrepare(8_000, 6, 1), Is.True);
            float[] firstCue = { 1f, 0f, 0f };
            delay.Process(firstCue, 0, firstCue.Length, 1);
            Assert.That(firstCue[2], Is.EqualTo(0.5f));

            delay.Reset();
            float[] secondCue = { 0f, -1f, 0f, 0f, 0f, 0f };
            delay.Process(secondCue, 0, secondCue.Length, 1);

            Assert.That(secondCue, Is.EqualTo(new[] { 0f, -1f, 0f, -0.5f, 0f, -0.25f }));
        }

        [Test]
        public void ANewDelayTimeAppliesOnPreparation()
        {
            DspDelay delay = new()
            {
                DelaySeconds = 0.00025f,
                Feedback = 0f,
                EchoLevel = 1f,
            };
            Assert.That(delay.TryPrepare(8_000, 6, 1), Is.True);
            delay.DelaySeconds = 0.0005f;
            float[] before = { 1f, 0f, 0f, 0f, 0f, 0f };
            delay.Process(before, 0, before.Length, 1);

            Assert.That(delay.TryPrepare(8_000, 6, 1), Is.True);
            float[] after = { -1f, 0f, 0f, 0f, 0f, 0f };
            delay.Process(after, 0, after.Length, 1);

            Assert.That(before, Is.EqualTo(new[] { 1f, 0f, 1f, 0f, 0f, 0f }));
            Assert.That(after, Is.EqualTo(new[] { -1f, 0f, 0f, 0f, -1f, 0f }));
        }

        [Test]
        public void ARefusedBlockDoesNotMoveTheEcho()
        {
            DspDelay delay = new() { DelaySeconds = 0.00025f, EchoLevel = 0.5f };
            Assert.That(delay.TryPrepare(8_000, 4, 2), Is.True);
            float[] wrongShape = { 1f, 2f, 3f, 4f };
            delay.Process(wrongShape, 0, wrongShape.Length, 2);

            delay.Process(wrongShape, 0, wrongShape.Length, 1);
            delay.Process(wrongShape, 0, 3, 2);
            delay.Process(wrongShape, int.MaxValue, 2, 2);
            float[] silence = new float[4];
            delay.Process(silence, 0, silence.Length, 2);

            Assert.That(wrongShape, Is.EqualTo(new[] { 1f, 2f, 3f, 4f }));
            Assert.That(silence, Is.EqualTo(new[] { 0.5f, 1f, 1.5f, 2f }));
        }
    }
}
