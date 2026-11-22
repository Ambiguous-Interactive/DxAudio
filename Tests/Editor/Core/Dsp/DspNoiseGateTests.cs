namespace WallstopStudios.DxAudio.Tests.Editor
{
    using NUnit.Framework;

    public sealed class DspNoiseGateTests
    {
        [Test]
        public void APeakOpensBothChannelsAndTheGateClosesBelowHysteresis()
        {
            DspNoiseGate gate = new()
            {
                ThresholdDecibels = -20f,
                AttackSeconds = 0f,
                ReleaseSeconds = 0f,
            };
            DspChain chain = new();
            chain.Add(gate);
            Assert.That(chain.TryPrepare(48_000, 6, 2), Is.True);
            float[] audio = { 8f, 9f, 0.08f, 0.01f, 0.01f, -0.1f, 0.08f, 0.02f, 0.04f, 0.02f, 0.08f, 0.01f };

            chain.Process(audio, 2, 4, 2);
            chain.Process(audio, 6, 6, 2);

            Assert.That(audio, Is.EqualTo(new[] { 8f, 9f, 0f, 0f, 0.01f, -0.1f, 0.08f, 0.02f, 0f, 0f, 0f, 0f }));
            Assert.That(chain.LatencySamples, Is.Zero);
        }

        [TestCase(1_000)]
        [TestCase(8_000)]
        public void AttackAndReleaseFollowTheEnvelopeAcrossBlocks(int sampleRate)
        {
            const float Time = 0.01f;
            const float Quiet = 0.025f;
            int frames = sampleRate / 100;
            DspNoiseGate gate = new()
            {
                ThresholdDecibels = -20f,
                AttackSeconds = Time,
                ReleaseSeconds = Time,
            };
            Assert.That(gate.TryPrepare(sampleRate, frames, 1), Is.True);
            float[] loud = new float[frames];
            float[] quiet = new float[frames];
            for (int index = 0; index < frames; ++index)
            {
                loud[index] = 1f;
                quiet[index] = Quiet;
            }

            gate.Process(loud, 0, loud.Length, 1);
            gate.Process(quiet, 0, quiet.Length, 1);

            Assert.That(loud[0], Is.InRange(0.01f, 0.1f));
            Assert.That(loud[frames - 1], Is.EqualTo(0.63212f).Within(0.0001f));
            Assert.That(quiet[0], Is.LessThan(Quiet * loud[frames - 1]));
            Assert.That(quiet[frames - 1], Is.EqualTo(Quiet * 0.23254f).Within(0.0001f));
        }

        [Test]
        public void ALongAttackReachesUnityAtAHighSampleRate()
        {
            const int SampleRate = 192_000;
            const int Frames = 1_024;
            DspNoiseGate gate = new() { AttackSeconds = 0.2f };
            Assert.That(gate.TryPrepare(SampleRate, Frames, 1), Is.True);
            float[] block = new float[Frames];
            for (int pass = 0; pass < 450; ++pass)
            {
                for (int index = 0; index < Frames; ++index)
                {
                    block[index] = 1f;
                }

                gate.Process(block, 0, block.Length, 1);
            }

            Assert.That(block[Frames - 1], Is.EqualTo(1f));
            Assert.That(gate.GainReductionDecibels, Is.Zero);
        }

        [Test]
        public void AnInvalidLayoutLeavesTheBlockAndGateStateAlone()
        {
            DspNoiseGate gate = new()
            {
                ThresholdDecibels = -20f,
                AttackSeconds = 0f,
                ReleaseSeconds = 0f,
            };
            Assert.That(gate.TryPrepare(48_000, 4, 2), Is.True);
            float[] block = { 1f, 1f, 0.08f, 0.02f };

            gate.Process(block, 0, block.Length, 1);
            gate.Process(block, 0, 3, 2);
            gate.Process(block, int.MaxValue, 2, 2);

            Assert.That(block, Is.EqualTo(new[] { 1f, 1f, 0.08f, 0.02f }));
            gate.Process(block, 2, 2, 2);
            Assert.That(block, Is.EqualTo(new[] { 1f, 1f, 0f, 0f }));
        }

        [Test]
        public void ResetAndPreparationClearThePreviousVoice()
        {
            DspNoiseGate gate = new()
            {
                ThresholdDecibels = -20f,
                AttackSeconds = 0f,
                ReleaseSeconds = 0.1f,
            };
            Assert.That(gate.TryPrepare(48_000, 4, 1), Is.True);
            float[] audio = { 1f, 0.08f, 1f, 0.08f };

            gate.Process(audio, 0, 1, 1);
            gate.Reset();
            gate.Process(audio, 1, 1, 1);
            gate.Process(audio, 2, 1, 1);
            Assert.That(gate.TryPrepare(44_100, 4, 1), Is.True);
            gate.Process(audio, 3, 1, 1);

            Assert.That(audio, Is.EqualTo(new[] { 1f, 0f, 1f, 0f }));
        }
    }
}
