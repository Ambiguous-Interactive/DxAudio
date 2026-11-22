namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using NUnit.Framework;

    public sealed class DspPhaserTests
    {
        [TestCase(0, 200d)]
        [TestCase(1, 2_000d)]
        public void TheSweepCancelsTwoFrequenciesAndKeepsDc(int halfCycles, double cornerHertz)
        {
            const int Rate = 48_000;
            const int Window = 4_000;
            const float SweepRate = 0.05f;
            DspPhaser phaser = new() { RateHertz = SweepRate, Depth = 1f };
            Assert.That(phaser.TryPrepare(Rate, Rate, 1), Is.True);
            float[] silence = new float[Rate];
            for (int block = 0; block < 10 * halfCycles; ++block)
            {
                phaser.Process(silence, 0, silence.Length, 1);
            }

            // Four phase rotations of pi/4 or 3*pi/4 cancel the equal dry and all-pass signals.
            double tangent = Math.Tan(Math.PI * cornerHertz / Rate);
            double first = 2d * Math.Atan(tangent * Math.Tan(Math.PI / 8d));
            double second = 2d * Math.Atan(tangent * Math.Tan(3d * Math.PI / 8d));
            float[] signal = new float[Window];
            for (int frame = 0; frame < Window; ++frame)
            {
                signal[frame] = (float)(1d + Math.Sin(first * frame) + Math.Sin(second * frame));
            }

            phaser.Process(signal, 0, signal.Length, 1);

            double squaredError = 0d;
            for (int frame = Window / 2; frame < Window; ++frame)
            {
                double difference = signal[frame] - 1d;
                squaredError += difference * difference;
            }

            Assert.That(Math.Sqrt(squaredError / (Window / 2)), Is.LessThan(0.16d));
            Assert.That(phaser.LatencySamples, Is.Zero);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(8)]
        public void ResetClearsEveryChannelAndSplitBlocksKeepTheSweep(int channels)
        {
            const int Rate = 8_000;
            const int Frames = 3_811;
            const int Stride = 113;
            DspPhaser phaser = new() { RateHertz = 5f, Depth = 1f };
            Assert.That(phaser.TryPrepare(Rate, Frames, channels), Is.True);
            float[] input = new float[Frames * channels];
            for (int frame = 0; frame < Frames; ++frame)
            {
                input[frame * channels] = (float)Math.Sin(0.141d * frame);
            }

            float[] whole = (float[])input.Clone();
            phaser.Process(whole, 0, whole.Length, channels);
            Assert.That(whole[channels], Is.Not.EqualTo(input[channels]));
            for (int frame = 0; frame < Frames; ++frame)
            {
                for (int channel = 1; channel < channels; ++channel)
                {
                    Assert.That(whole[frame * channels + channel], Is.Zero);
                }
            }

            phaser.Reset();
            float[] split = (float[])input.Clone();
            phaser.Process(split, 0, split.Length, 0);
            phaser.Process(split, 0, split.Length, channels + 1);
            phaser.Process(split, -1, channels, channels);
            phaser.Process(split, int.MaxValue, channels, channels);
            phaser.Process(split, channels, split.Length, channels);
            Assert.That(split, Is.EqualTo(input));
            for (int frame = 0; frame < Frames; frame += Stride)
            {
                phaser.Process(split, frame * channels, Math.Min(Stride, Frames - frame) * channels, channels);
            }

            Assert.That(split, Is.EqualTo(whole));
        }

        [Test]
        public void HalfDepthKeepsHalfOfTheFullDepthChange()
        {
            float[] dry = { 1f, 0f, 0f, 0f };
            float[] full = (float[])dry.Clone();
            float[] half = (float[])dry.Clone();
            DspPhaser phaser = new() { Depth = 1f };
            Assert.That(phaser.TryPrepare(48_000, dry.Length, 1), Is.True);
            phaser.Process(full, 0, full.Length, 1);

            phaser.Depth = 0.5f;
            Assert.That(phaser.TryPrepare(48_000, dry.Length, 1), Is.True);
            phaser.Process(half, 0, half.Length, 1);

            Assert.That(half[0], Is.EqualTo(0.5f * (dry[0] + full[0])).Within(0.000001f));
            Assert.That(half[1], Is.EqualTo(0.5f * full[1]).Within(0.000001f));
        }

        [Test]
        public void AnImpulseSettlesToExactSilence()
        {
            const int Frames = 8_192;
            DspPhaser phaser = new() { RateHertz = 0.05f, Depth = 1f };
            Assert.That(phaser.TryPrepare(48_000, Frames, 1), Is.True);
            float[] samples = new float[Frames];
            samples[0] = 1f;

            phaser.Process(samples, 0, Frames, 1);

            Assert.That(samples[1], Is.Not.Zero);
            Assert.That(samples[Frames - 1], Is.Zero);
        }

        [Test]
        public void ZeroDepthBypassesExactlyAndSettingsApplyOnPreparation()
        {
            DspPhaser phaser = new() { Depth = 1f };
            float[] impulse = { 1f, 0f, 0f, 0f };
            float[] unprepared = (float[])impulse.Clone();
            phaser.Process(unprepared, 0, unprepared.Length, 1);
            Assert.That(unprepared, Is.EqualTo(impulse));
            Assert.That(phaser.TryPrepare(48_000, impulse.Length, 1), Is.True);
            phaser.Depth = 0f;
            float[] active = (float[])impulse.Clone();
            phaser.Process(active, 0, active.Length, 1);
            Assert.That(active, Is.Not.EqualTo(impulse));

            Assert.That(phaser.TryPrepare(48_000, impulse.Length, 1), Is.True);
            float[] bypass = (float[])impulse.Clone();
            phaser.Process(bypass, 0, bypass.Length, 1);
            Assert.That(bypass, Is.EqualTo(impulse));
        }
    }
}
