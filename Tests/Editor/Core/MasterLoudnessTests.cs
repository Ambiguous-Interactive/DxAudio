namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using NUnit.Framework;

    public sealed class MasterLoudnessTests
    {
        private static void FeedTone(MasterLoudness meter, int rate, double seconds, double frequency, params double[] levels)
        {
            int channels = levels.Length;
            float[] buffer = new float[509 * channels];
            double[] amplitudes = new double[channels];
            for (int channel = 0; channel < channels; ++channel)
            {
                amplitudes[channel] = Math.Pow(10d, levels[channel] / 20d);
            }

            int frames = (int)Math.Round(seconds * rate);
            for (int first = 0; first < frames; first += 509)
            {
                int count = Math.Min(509, frames - first);
                for (int frame = 0; frame < count; ++frame)
                {
                    double sample = Math.Sin((first + frame) * (2d * Math.PI * frequency / rate));
                    for (int channel = 0; channel < channels; ++channel)
                    {
                        buffer[frame * channels + channel] = (float)(sample * amplitudes[channel]);
                    }
                }
                meter.Observe(buffer, 0, count * channels, channels, rate);
            }
        }

        [TestCase(44_100, -23d)]
        [TestCase(48_000, -33d)]
        [TestCase(96_000, -23d)]
        public void StereoReferenceTonesMatchTheEbuLevels(int rate, double level)
        {
            MasterLoudness meter = new();
            FeedTone(meter, rate, 2.9, 1_000d, level, level);
            Assert.That(meter.ShortTerm.HasValue, Is.False);
            FeedTone(meter, rate, 0.1, 1_000d, level, level);
            Assert.That(meter.ShortTerm, Is.EqualTo(level).Within(0.1));
            Assert.That(meter.Integrated, Is.EqualTo(level).Within(0.1));
        }

        [TestCase(100d, -24.8245d)]
        [TestCase(8_000d, -19.6516d)]
        public void TheKWeightingMatchesTheItuFilterResponse(double frequency, double expected)
        {
            // The expected levels come from evaluating Annex 1 Tables 1 and 2 at 48 kHz.
            MasterLoudness meter = new();
            FeedTone(meter, 48_000, 3d, frequency, -23d, -23d);
            Assert.That(meter.ShortTerm, Is.EqualTo(expected).Within(0.1));
        }

        [Test]
        public void TheIntegratedReadingAppliesBothGates()
        {
            const int Rate = 48_000;
            MasterLoudness meter = new();
            FeedTone(meter, Rate, 10d, 1_000d, -72d, -72d);
            Assert.That(meter.Integrated.HasValue, Is.False);
            FeedTone(meter, Rate, 10d, 1_000d, -36d, -36d);
            Assert.That(meter.Integrated, Is.EqualTo(-36d).Within(0.1));
            FeedTone(meter, Rate, 60d, 1_000d, -23d, -23d);
            FeedTone(meter, Rate, 10d, 1_000d, -36d, -36d);
            FeedTone(meter, Rate, 10d, 1_000d, -72d, -72d);
            Assert.That(meter.Integrated, Is.EqualTo(-23d).Within(0.1));
            Assert.That(meter.ShortTerm, Is.EqualTo(-72d).Within(0.1));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void TheOrderedGateMatchesAFullScanAcrossInsertionOrders(int order)
        {
            const double AbsoluteGate = 0.25d;
            LoudnessGate gate = new(AbsoluteGate);
            double[] blocks = new double[1_024];
            for (int index = 0; index < 1_024; ++index)
            {
                int rank = order switch
                {
                    0 => index,
                    1 => 1_023 - index,
                    _ => index * 137 % 1_024,
                };
                double block = Math.Pow(1.06d, rank / 2) * 0.01d;
                blocks[index] = block;
                gate.Add(block);

                double absoluteSum = 0d;
                int absoluteCount = 0;
                for (int prior = 0; prior <= index; ++prior)
                {
                    if (AbsoluteGate < blocks[prior])
                    {
                        absoluteSum += blocks[prior];
                        ++absoluteCount;
                    }
                }

                double threshold = Math.Max(AbsoluteGate, 0.1d * (absoluteSum / absoluteCount));
                double relativeSum = 0d;
                int relativeCount = 0;
                for (int prior = 0; prior <= index; ++prior)
                {
                    if (threshold < blocks[prior])
                    {
                        relativeSum += blocks[prior];
                        ++relativeCount;
                    }
                }

                double? reading = gate.Mean();
                if (relativeCount == 0)
                {
                    Assert.That(reading.HasValue, Is.False);
                }
                else
                {
                    double expected = relativeSum / relativeCount;
                    Assert.That(reading, Is.EqualTo(expected).Within(1e-12d * expected));
                }
            }

            gate.Clear();
            gate.Add(AbsoluteGate);
            Assert.That(gate.Mean().HasValue, Is.False);
            gate.Add(1d);
            gate.Add(2d);
            Assert.That(gate.Mean(), Is.EqualTo(1.5d));
        }

        [Test]
        public void ShortTermRollsOverTheLastThreeSecondsWithoutGating()
        {
            const int Rate = 48_000;
            MasterLoudness meter = new();
            for (int repeat = 0; repeat < 5; ++repeat)
            {
                FeedTone(meter, Rate, 1.34, 1_000d, -20d, -20d);
                FeedTone(meter, Rate, 1.66, 1_000d, -30d, -30d);
                Assert.That(meter.ShortTerm, Is.EqualTo(-23d).Within(0.1));
            }
        }

        [TestCase(5)]
        [TestCase(6)]
        public void SurroundWeightsTheRearPairAndExcludesTheSubwoofer(int channels)
        {
            MasterLoudness meter = new();
            double[] levels = channels == 5 ? new[] { -28d, -28d, -24d, -30d, -30d } : new[] { -28d, -28d, -24d, 0d, -30d, -30d };
            FeedTone(meter, 48_000, 3d, 1_000d, levels);
            Assert.That(meter.ShortTerm, Is.EqualTo(-23d).Within(0.1));
            Assert.That(meter.Integrated, Is.EqualTo(-23d).Within(0.1));
        }

        [TestCase(4, -21.51d)]
        [TestCase(6, -23.00d)]
        public void SevenPointOneWeightsSideAndRearSpeakersByPosition(int activeChannel, double expected)
        {
            MasterLoudness meter = new();
            double[] levels = new double[8];
            Array.Fill(levels, double.NegativeInfinity);
            levels[activeChannel] = -20d;
            FeedTone(meter, 48_000, 0.4, 1_000d, levels);
            Assert.That(meter.Integrated, Is.EqualTo(expected).Within(0.1));
        }

        [Test]
        public void ANewRateOrChannelLayoutStartsNewWindows()
        {
            MasterLoudness meter = new();
            FeedTone(meter, 48_000, 3d, 1_000d, -23d, -23d);
            FeedTone(meter, 44_100, 0.399, 1_000d, -30d, -30d);
            Assert.That(meter.ShortTerm.HasValue, Is.False);
            Assert.That(meter.Integrated.HasValue, Is.False);
            FeedTone(meter, 44_100, 0.4, 1_000d, -33d);
            Assert.That(meter.Integrated, Is.EqualTo(-36d).Within(0.1));
        }

        [Test]
        public void AGapClearsLoudnessAndAQueuedDeviceChangeUsesItsOwnRate()
        {
            StereoPeakTap tap = new(48_000);
            MasterLoudness meter = new();
            float[] oldTone = new float[480 * 2];
            float[] newTone = new float[441 * 2];
            for (int frame = 0; frame < 480; ++frame)
            {
                float sample = (float)(0.1 * Math.Sin(2d * Math.PI * frame / 48d));
                oldTone[2 * frame] = sample;
                oldTone[2 * frame + 1] = sample;
                if (frame < 441)
                {
                    sample = (float)(0.1 * Math.Sin(20d * Math.PI * frame / 441d));
                    newTone[2 * frame] = sample;
                    newTone[2 * frame + 1] = sample;
                }
            }

            for (int tick = 0; tick < 4; ++tick)
            {
                for (int block = 0; block < 10; ++block)
                {
                    tap.Observe(oldTone, 2);
                }

                Assert.That(tap.TryReadTruePeak(out _, out _, meter), Is.True);
            }
            Assert.That(meter.Integrated, Is.EqualTo(-20d).Within(0.1));

            tap.Observe(oldTone, 2);
            tap.SetSampleRate(44_100);
            for (int block = 0; block < 40; ++block)
            {
                tap.Observe(newTone, 2);
            }
            Assert.That(tap.TryReadTruePeak(out _, out _, meter), Is.True);
            Assert.That(meter.Integrated, Is.EqualTo(-20d).Within(0.1));

            for (int block = 0; block < 200; ++block)
            {
                tap.Observe(newTone, 2);
            }
            Assert.That(tap.TryReadTruePeak(out _, out bool overrun, meter), Is.False);
            Assert.That(overrun, Is.True);
            Assert.That(meter.Integrated.HasValue, Is.False);

            for (int block = 0; block < 40; ++block)
            {
                tap.Observe(newTone, 2);
            }
            Assert.That(tap.TryReadTruePeak(out _, out overrun, meter), Is.True);
            Assert.That(overrun, Is.False);
            Assert.That(meter.Integrated, Is.EqualTo(-20d).Within(0.1));
        }
    }
}
