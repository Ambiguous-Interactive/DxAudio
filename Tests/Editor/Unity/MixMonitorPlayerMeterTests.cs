namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.DxAudio.Editor;

    public sealed class MixMonitorPlayerMeterTests
    {
        [Test]
        public void StereoPeaksReadTheSumOfActiveVoices()
        {
            MixMonitorPlayerMeter meter = new();
            float[] samples = new float[MixMonitorPlayerMeter.SampleCount];
            samples[23] = -0.5f;
            meter.AddChannel(samples, right: false);
            meter.AddChannel(samples, right: false);
            samples[23] = 0.25f;
            meter.AddChannel(samples, right: true);
            samples[23] = -0.125f;
            meter.AddChannel(samples, right: true);
            meter.FinishSample(0d);

            Assert.That(meter.Left.Level, Is.EqualTo(1f));
            Assert.That(meter.Right.Level, Is.EqualTo(0.125f));
            Assert.That(meter.Spectrum.Length, Is.Zero);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void ANonFiniteSampleDoesNotEraseTheOtherVoices(float invalid)
        {
            MixMonitorPlayerMeter meter = new();
            float[] samples = new float[MixMonitorPlayerMeter.SampleCount];
            samples[23] = 0.5f;
            meter.AddChannel(samples, right: false);
            samples[23] = invalid;
            meter.AddChannel(samples, right: false);
            meter.FinishSample(0d);

            Assert.That(meter.Left.Level, Is.EqualTo(0.5f));
        }

        [Test]
        public void AQuietWindowClearsTheLevelAndKeepsThePeakForOneSecond()
        {
            MixMonitorPlayerMeter meter = new();
            float[] samples = new float[MixMonitorPlayerMeter.SampleCount];
            samples[11] = 0.5f;
            meter.AddChannel(samples, right: false);
            meter.FinishSample(10d);

            meter.FinishSample(10.999d);

            Assert.That(meter.Left.Level, Is.Zero);
            Assert.That(meter.Left.Held, Is.EqualTo(0.5f));
            Assert.That(meter.Right.Held, Is.Zero);

            meter.FinishSample(11d);

            Assert.That(meter.Left.Held, Is.Zero);
        }

        [Test]
        public void InterleavedSpectrumPreservesTheEnergyOfBothChannels()
        {
            const int Bin = 48;
            const int Frames = MixMonitorPlayerMeter.SampleCount;
            float[] interleaved = new float[2 * Frames];
            for (int frame = 0; frame < Frames; ++frame)
            {
                float sample = 0.25f * (float)Math.Sin(2d * Math.PI * Bin * frame / Frames);
                interleaved[2 * frame] = sample;
                interleaved[2 * frame + 1] = -sample;
            }

            MixMonitorPlayerMeter meter = new(spectrum: true);
            meter.AddChannel(interleaved, right: false, offset: 0, stride: 2);
            meter.AddChannel(interleaved, right: true, offset: 1, stride: 2);
            meter.FinishSample(0d);

            Assert.That(meter.Spectrum[Bin], Is.EqualTo(0.25f).Within(0.00001f));
            Assert.That(meter.Spectrum[Bin / 2], Is.LessThan(0.00001f));
        }

        [Test]
        public void TheMusicSpectrumKeepsOppositeStereoSignalsAndClearsWithSilence()
        {
            const int toneBin = 48;
            const float amplitude = 0.25f;
            MixMonitorPlayerMeter meter = new(spectrum: true);
            const int sampleCount = MixMonitorPlayerMeter.SampleCount;
            float[] samples = new float[sampleCount];
            for (int index = 0; index < sampleCount; ++index)
            {
                samples[index] = amplitude * (float)Math.Sin(2d * Math.PI * toneBin * index / sampleCount);
            }
            meter.AddChannel(samples, right: false);
            for (int index = 0; index < sampleCount; ++index)
            {
                samples[index] = -samples[index];
            }
            meter.AddChannel(samples, right: true);
            meter.FinishSample(0d);

            Assert.That(meter.Spectrum[toneBin], Is.EqualTo(amplitude).Within(0.00001f));
            Assert.That(meter.Spectrum[toneBin / 2], Is.LessThan(0.00001f));

            meter.FinishSample(0.05d);

            Assert.That(meter.Spectrum[toneBin], Is.Zero);
            Assert.That(meter.Left.Held, Is.EqualTo(amplitude));
            meter.Clear();
            Assert.That(meter.Left.Held, Is.Zero);
            Assert.That(meter.Right.Held, Is.Zero);
        }
    }
}
