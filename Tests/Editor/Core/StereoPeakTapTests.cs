namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using NUnit.Framework;

    public sealed class StereoPeakTapTests
    {
        private const int SampleRate = 48_000;

        [TestCase(2)]
        [TestCase(8)]
        public void AuditionGainRampsAcrossBlocksThenMetersTheOutput(int channels)
        {
            StereoPeakTap tap = new(SampleRate) { AuditionGain = 0.5f };
            float[] first = new float[60 * channels];
            float[] second = new float[512 * channels];
            Array.Fill(first, 0.5f);
            Array.Fill(second, 0.5f);

            tap.Observe(first, channels);
            tap.Observe(second, channels);
            Assert.That(first[0], Is.InRange(0.49f, 0.5f));
            Assert.That(first[first.Length - 1], Is.EqualTo(0.375f).Within(0.00001f));
            Assert.That(second[0], Is.EqualTo(0.375f - 0.5f / 240f).Within(0.00001f));
            Assert.That(second[second.Length - 1], Is.EqualTo(0.25f));

            Assert.That(tap.TryRead(out _, out _), Is.True);
            Assert.That(tap.TryReadTruePeak(out _, out _), Is.True);
            Array.Fill(second, 0.5f);
            tap.Observe(second, channels);
            Assert.That(tap.TryRead(out float left, out float right), Is.True);
            Assert.That(left, Is.EqualTo(0.25f));
            Assert.That(right, Is.EqualTo(0.25f));
            Assert.That(tap.TryReadTruePeak(out float truePeak, out _), Is.True);
            StereoPeakTap reference = new(SampleRate);
            Array.Fill(second, 0.25f);
            reference.Observe(second, channels);
            Assert.That(reference.TryReadTruePeak(out _, out _), Is.True);
            reference.Observe(second, channels);
            Assert.That(reference.TryReadTruePeak(out float expectedPeak, out _), Is.True);
            Assert.That(truePeak, Is.EqualTo(expectedPeak));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(8)]
        public void TruePeakFindsAnIntersampleCrestAcrossUnevenBlocks(int channels)
        {
            StereoPeakTap tap = new(SampleRate);
            float[] cycle = { 0.75f, 0.75f, -0.75f, -0.75f };
            float[] first = new float[7 * channels];
            float[] second = new float[57 * channels];
            for (int frame = 0; frame < 64; ++frame)
            {
                float[] block = frame < 7 ? first : second;
                int index = frame < 7 ? frame : frame - 7;
                block[index * channels + channels - 1] = cycle[frame % cycle.Length];
            }

            tap.Observe(first, channels);
            Assert.That(tap.TryReadTruePeak(out _, out _), Is.True);
            tap.Observe(second, channels);

            Assert.That(tap.TryReadTruePeak(out float peak, out _), Is.True);
            double expectedDb = 20d * Math.Log10(0.75d * Math.Sqrt(2d));
            Assert.That(20d * Math.Log10(peak), Is.EqualTo(expectedDb).Within(0.25d));
            Assert.That(tap.TryReadTruePeak(out _, out _), Is.False);
        }

        [Test]
        public void SingleFrameCallbacksGiveTheSameTruePeakAsAContiguousBlock()
        {
            StereoPeakTap whole = new(SampleRate);
            StereoPeakTap split = new(SampleRate);
            float[] signal = { 0f, 0f, 0.75f, 0.75f, -0.75f, -0.75f, 0f, 0f, 0f, 0f, 0f, 0f };
            whole.Observe(signal, 1);
            foreach (float sample in signal)
            {
                split.Observe(new[] { sample }, 1);
            }

            Assert.That(whole.TryReadTruePeak(out float expected, out _), Is.True);
            Assert.That(expected, Is.GreaterThan(0.75f));
            Assert.That(split.TryReadTruePeak(out float actual, out _), Is.True);
            Assert.That(actual, Is.EqualTo(expected).Within(0.000001f));
        }

        [TestCase(1, false)]
        [TestCase(2, true)]
        public void TruePeakClearsFilterHistoryOnLayoutOrDeviceChanges(int nextChannels, bool configurationChanged)
        {
            StereoPeakTap tap = new(SampleRate);
            StereoPeakTap fresh = new(SampleRate);
            tap.Observe(new[] { 1f, 1f, -1f, -1f }, 2);
            Assert.That(tap.TryReadTruePeak(out _, out _), Is.True);
            if (configurationChanged)
            {
                tap.SetSampleRate(SampleRate);
            }

            float[] silence = new float[24];
            tap.Observe(silence, nextChannels);
            fresh.Observe(silence, nextChannels);

            Assert.That(tap.TryReadTruePeak(out float peak, out _), Is.True);
            Assert.That(fresh.TryReadTruePeak(out float expected, out _), Is.True);
            Assert.That(peak, Is.EqualTo(expected));
        }

        [Test]
        public void AFullTruePeakQueueReportsAGapAndRestartsTheFilter()
        {
            StereoPeakTap tap = new(SampleRate);
            float[] block = new float[4096];
            block[block.Length - 1] = 1f;
            for (int index = 0; index < 33; ++index)
            {
                tap.Observe(block, 1);
            }

            Assert.That(tap.TryReadTruePeak(out _, out bool overrun), Is.False);
            Assert.That(overrun, Is.True);
            Assert.That(tap.TryRead(out float sample, out _), Is.True);
            Assert.That(sample, Is.EqualTo(1f));

            // The next block wraps at the end of the queue and must start from silence.
            Array.Clear(block, 0, block.Length);
            tap.Observe(block, 1);
            Assert.That(tap.TryReadTruePeak(out float peak, out overrun), Is.True);
            Assert.That(overrun, Is.False);
            Assert.That(peak, Is.Zero);
        }

        [Test]
        public void AReadKeepsEachChannelsLoudestSampleAcrossBlocks()
        {
            StereoPeakTap tap = new(SampleRate);
            tap.Observe(new[] { 0.05f, -0.2f, -1.25f, 0.3f }, 2);
            tap.Observe(new[] { 0.3f, -0.8f }, 2);

            Assert.That(tap.TryRead(out float left, out float right), Is.True);
            Assert.That(left, Is.EqualTo(1.25f));
            Assert.That(right, Is.EqualTo(0.8f));
            Assert.That(tap.TryRead(out _, out _), Is.False);

            tap.Observe(new[] { 0f, 0f }, 2);
            Assert.That(tap.TryRead(out left, out right), Is.True);
            Assert.That(left, Is.Zero);
            Assert.That(right, Is.Zero);
        }

        [TestCase(1, 0.5f, 0.5f)]
        [TestCase(4, 0.5f, 0.25f)]
        public void ReadsMonoOrTheFrontTwoChannels(int channels, float expectedLeft, float expectedRight)
        {
            StereoPeakTap tap = new(SampleRate);
            float[] block = channels == 1 ? new[] { 0.1f, -0.5f } : new[] { 0.5f, 0f, 10f, 12f, 0.1f, -0.25f, 0f, 0f };
            tap.Observe(block, channels);

            Assert.That(tap.TryRead(out float left, out float right), Is.True);
            Assert.That(left, Is.EqualTo(expectedLeft));
            Assert.That(right, Is.EqualTo(expectedRight));
        }

        [Test]
        public void IgnoresIncompleteFramesAndNonFiniteSamples()
        {
            StereoPeakTap tap = new(SampleRate);
            tap.Observe(new[] { 1f }, 2);
            tap.Observe(new[] { 1f }, 0);
            Assert.That(tap.TryRead(out _, out _), Is.False);
            Assert.That(tap.TryReadTruePeak(out _, out _), Is.False);

            tap.Observe(new[] { float.NaN, -0.25f, -0.5f, float.PositiveInfinity, 3f }, 2);
            Assert.That(tap.TryRead(out float left, out float right), Is.True);
            Assert.That(left, Is.EqualTo(0.5f));
            Assert.That(right, Is.EqualTo(0.25f));
            Assert.That(tap.TryReadTruePeak(out float peak, out _), Is.True);
            Assert.That(peak, Is.EqualTo(0.5f));
        }
    }
}
