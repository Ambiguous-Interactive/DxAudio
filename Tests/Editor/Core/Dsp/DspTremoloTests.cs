namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using NUnit.Framework;

    public sealed class DspTremoloTests
    {
        private static float[] Signal(int frames, int channels)
        {
            float[] samples = new float[frames * channels];
            for (int frame = 0; frame < frames; ++frame)
            {
                for (int channel = 0; channel < channels; ++channel)
                {
                    samples[frame * channels + channel] = ChannelValue(channel);
                }
            }

            return samples;
        }

        private static float ChannelValue(int channel)
        {
            return (channel & 1) == 0 ? 1f : -0.5f;
        }

        [TestCase(1, 1f)]
        [TestCase(2, 0.5f)]
        [TestCase(8, 1f)]
        public void OneCycleAppliesTheSameBoundedGainToEveryChannel(int channels, float depth)
        {
            DspTremolo tremolo = new() { RateHertz = 4f, Depth = depth };
            Assert.That(tremolo.TryPrepare(16, 5, channels), Is.True);
            float[] samples = Signal(5, channels);

            tremolo.Process(samples, 0, samples.Length, channels);

            float[] gain = { 1f, 1f - 0.5f * depth, 1f - depth, 1f - 0.5f * depth, 1f };
            for (int frame = 0; frame < 5; ++frame)
            {
                for (int channel = 0; channel < channels; ++channel)
                {
                    Assert.That(samples[frame * channels + channel], Is.EqualTo(ChannelValue(channel) * gain[frame]));
                }
            }

            Assert.That(tremolo.LatencySamples, Is.Zero);
        }

        [Test]
        public void PhaseFollowsTheRateAcrossManySplitBlocksAndRestartsAtUnity()
        {
            const int Rate = 48_000;
            const int Frames = 48_017;
            const float CyclesPerSecond = 13.7f;
            DspTremolo tremolo = new() { RateHertz = CyclesPerSecond, Depth = 1f };
            Assert.That(tremolo.TryPrepare(Rate, Frames, 1), Is.True);
            float[] whole = Signal(Frames, 1);
            tremolo.Process(whole, 0, whole.Length, 1);
            float maximumError = 0f;
            for (int frame = 0; frame < Frames; ++frame)
            {
                double phase = ((double)frame * CyclesPerSecond / Rate) % 1d;
                float expected = (float)Math.Abs(1d - 2d * phase);
                maximumError = Math.Max(maximumError, Math.Abs(expected - whole[frame]));
            }

            Assert.That(maximumError, Is.LessThan(0.000001f));
            Assert.That(tremolo.GainReductionDecibels, Is.LessThan(0f));

            tremolo.Reset();
            Assert.That(tremolo.GainReductionDecibels, Is.Zero);
            float[] split = Signal(Frames, 1);
            for (int offset = 0; offset < Frames; offset += 997)
            {
                tremolo.Process(split, offset, Math.Min(997, Frames - offset), 1);
            }

            Assert.That(split, Is.EqualTo(whole));
        }

        [Test]
        public void ZeroDepthLeavesAudioUnchangedAfterPreparation()
        {
            DspTremolo tremolo = new() { RateHertz = 4f, Depth = 1f };
            Assert.That(tremolo.TryPrepare(16, 3, 1), Is.True);
            tremolo.Depth = 0f;
            float[] before = Signal(3, 1);
            tremolo.Process(before, 0, before.Length, 1);
            Assert.That(before, Is.EqualTo(new[] { 1f, 0.5f, 0f }));

            Assert.That(tremolo.TryPrepare(16, 3, 1), Is.True);
            float[] after = Signal(3, 1);
            tremolo.Process(after, 0, after.Length, 1);
            Assert.That(after, Is.EqualTo(Signal(3, 1)));
            Assert.That(tremolo.GainReductionDecibels, Is.Zero);
        }

        [Test]
        public void OnlyTheRequestedSamplesAndValidBlocksAffectTheSweep()
        {
            DspTremolo tremolo = new() { RateHertz = 4f, Depth = 1f };
            float[] samples = Signal(5, 2);
            tremolo.Process(samples, 0, samples.Length, 2);
            Assert.That(tremolo.TryPrepare(16, 5, 2), Is.True);
            tremolo.Process(samples, 0, samples.Length, 1);
            tremolo.Process(samples, 0, 3, 2);
            tremolo.Process(samples, int.MaxValue, 2, 2);
            Assert.That(samples, Is.EqualTo(Signal(5, 2)));

            tremolo.Process(samples, 2, 6, 2);
            Assert.That(samples, Is.EqualTo(new[] { 1f, -0.5f, 1f, -0.5f, 0.5f, -0.25f, 0f, 0f, 1f, -0.5f }));
        }
    }
}
