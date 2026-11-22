namespace WallstopStudios.DxAudio.Tests
{
    using System;
    using NUnit.Framework;

    [TestFixture]
    public sealed class DspReverbTests
    {
        private const int Rate = 48_000;

        private static DspReverb Prepare(int channels, float decay = 0.9f, float wetLevel = 1f, int rate = Rate)
        {
            DspReverb effect = new() { DecaySeconds = decay, WetLevel = wetLevel };
            Assert.That(effect.TryPrepare(rate, 1_024, channels), Is.True);
            return effect;
        }

        private static float[] Impulse(int length)
        {
            float[] samples = new float[length];
            samples[0] = 1f;
            return samples;
        }

        private static double Energy(float[] block, int start, int length)
        {
            double sum = 0d;
            for (int i = start; i < start + length; ++i)
            {
                sum += (double)block[i] * block[i];
            }

            return sum;
        }

        [TestCase(48_000, 0.3f)]
        [TestCase(48_000, 0.9f)]
        [TestCase(48_000, 1.8f)]
        [TestCase(44_100, 0.9f)]
        [TestCase(96_000, 0.9f)]
        public void AnImpulseDevelopsADenseTailWithTheRequestedDecay(int rate, float decay)
        {
            float[] block = Impulse(rate * 3);
            DspReverb effect = Prepare(1, decay, 1f, rate);
            effect.Process(block, 0, block.Length, 1);

            Assert.That(effect.LatencySamples, Is.Zero);
            Assert.That(block[0], Is.EqualTo(1f));
            Assert.That(block[1], Is.Zero);
            int early = rate / 10;
            int window = rate / 10;
            int late = early + (int)Math.Round(decay * rate);
            double earlyEnergy = Energy(block, early, window);
            Assert.That(earlyEnergy, Is.GreaterThan(0.00001d));
            double drop = 10d * Math.Log10(Energy(block, late, window) / earlyEnergy);
            Assert.That(drop, Is.EqualTo(-60d).Within(5d));

            int reflectionCount = 0;
            for (int i = early; i < early + window; ++i)
            {
                if (0.000001f < Math.Abs(block[i]))
                {
                    ++reflectionCount;
                }
            }

            Assert.That(reflectionCount, Is.GreaterThan(24));
        }

        [Test]
        public void WetLevelScalesTheTailWhileDryAudioPassesUnchanged()
        {
            float[] dry = Impulse(Rate / 2);
            float[] silentWet = (float[])dry.Clone();
            float[] fullWet = (float[])dry.Clone();
            float[] halfWet = (float[])dry.Clone();
            Prepare(1, 0.9f, 0f).Process(silentWet, 0, silentWet.Length, 1);
            Prepare(1, 0.9f, 1f).Process(fullWet, 0, fullWet.Length, 1);
            Prepare(1, 0.9f, 0.5f).Process(halfWet, 0, halfWet.Length, 1);

            Assert.That(silentWet, Is.EqualTo(dry));
            Assert.That(Energy(fullWet, 1, fullWet.Length - 1), Is.Positive);
            int length = dry.Length;
            for (int i = 0; i < length; ++i)
            {
                fullWet[i] = dry[i] + 0.5f * (fullWet[i] - dry[i]);
            }

            Assert.That(halfWet, Is.EqualTo(fullWet));
        }

        [Test]
        public void RepreparingAppliesNewSettingsAndDiscardsTheOldTail()
        {
            DspReverb effect = Prepare(1);
            float[] first = Impulse(Rate / 5);
            effect.Process(first, 0, first.Length, 1);
            Assert.That(Energy(first, 1, first.Length - 1), Is.Positive);

            effect.WetLevel = 0f;
            float[] continuation = new float[Rate / 5];
            effect.Process(continuation, 0, continuation.Length, 1);
            Assert.That(Energy(continuation, 0, continuation.Length), Is.Positive);

            Assert.That(effect.TryPrepare(Rate, 1_024, 1), Is.True);
            float[] bypassed = Impulse(Rate / 5);
            effect.Process(bypassed, 0, bypassed.Length, 1);
            Assert.That(bypassed, Is.EqualTo(Impulse(Rate / 5)));

            effect.WetLevel = 1f;
            Assert.That(effect.TryPrepare(Rate, 1_024, 1), Is.True);
            float[] silence = new float[Rate / 5];
            effect.Process(silence, 0, silence.Length, 1);
            Assert.That(silence, Is.All.EqualTo(0f));
        }

        [Test]
        public void TheWetPathRejectsTheHighestFrequencyWhileTheDryPathKeepsIt()
        {
            // At Nyquist, each pair contains equal and opposite samples.
            const int Length = Rate / 5;
            float[] input = new float[Length];
            for (int frame = 0; frame < Length; ++frame)
            {
                input[frame] = frame % 2 == 0 ? 1f : -1f;
            }

            float[] output = (float[])input.Clone();
            Prepare(1).Process(output, 0, Length, 1);
            Assert.That(output, Is.EqualTo(input));
        }

        [TestCase(2)]
        [TestCase(8)]
        public void ChannelsMatchMonoAndSplitBlocksMatchWholeBlocks(int channels)
        {
            const int Frames = 9923;
            float[] whole = new float[Frames * channels];
            float[] mono = Impulse(Frames);
            float[] split = new float[whole.Length + 2];
            whole[channels - 1] = 1f;
            Array.Copy(whole, 0, split, 1, whole.Length);
            split[0] = -1f;
            split[split.Length - 1] = -1f;

            Prepare(1).Process(mono, 0, mono.Length, 1);
            Prepare(channels).Process(whole, 0, whole.Length, channels);
            DspReverb effect = Prepare(channels);
            for (int frame = 0; frame < Frames; frame += 257)
            {
                int length = Math.Min(257, Frames - frame);
                effect.Process(split, 1 + frame * channels, length * channels, channels);
            }

            Assert.That(split[0], Is.EqualTo(-1f));
            Assert.That(split[split.Length - 1], Is.EqualTo(-1f));
            for (int frame = 0; frame < Frames; ++frame)
            {
                int start = frame * channels;
                for (int channel = 0; channel < channels; ++channel)
                {
                    float expected = channel == channels - 1 ? mono[frame] : 0f;
                    Assert.That(whole[start + channel], Is.EqualTo(expected));
                    Assert.That(split[start + channel + 1], Is.EqualTo(expected));
                }
            }
        }

        [Test]
        public void InvalidBuffersLeaveTheTailIntactAndResetClearsIt()
        {
            float[] impulse = Impulse(4001);
            DspReverb effect = Prepare(1);
            DspReverb reference = Prepare(1);
            float[] first = (float[])impulse.Clone();
            effect.Process(first, 0, first.Length, 1);
            reference.Process(impulse, 0, impulse.Length, 1);

            float[] rejected = { 1f, 2f, 3f };
            effect.Process(null, 0, 3, 1);
            effect.Process(rejected, 0, 2, 2);
            effect.Process(rejected, -1, 3, 1);
            effect.Process(rejected, 1, int.MaxValue, 1);
            effect.Process(rejected, 0, -1, 1);
            Assert.That(rejected, Is.EqualTo(new[] { 1f, 2f, 3f }));

            float[] tail = new float[3_000];
            float[] expected = new float[tail.Length];
            effect.Process(tail, 0, tail.Length, 1);
            reference.Process(expected, 0, expected.Length, 1);
            Assert.That(Energy(expected, 0, expected.Length), Is.Positive);
            Assert.That(tail, Is.EqualTo(expected));

            effect.Reset();
            Array.Clear(tail, 0, tail.Length);
            effect.Process(tail, 0, tail.Length, 1);
            Assert.That(tail, Is.All.EqualTo(0f));
        }
    }
}
