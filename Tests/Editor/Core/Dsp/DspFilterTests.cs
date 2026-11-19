namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using NUnit.Framework;

    public sealed class DspFilterTests
    {
        private const int SampleRate = 48_000;
        private const int BlockFrames = 256;

        [TestCase(DspFilterMode.LowPass, 100f, 0.99f, 8_000f, 0.02f, TestName = "A low pass keeps its passband and cuts its stopband")]
        [TestCase(DspFilterMode.HighPass, 5_000f, 0.99f, 125f, 0.02f, TestName = "A high pass keeps its passband and cuts its stopband")]
        public void APassFilterKeepsOneSideOfItsCorner(DspFilterMode mode, float passedHz, float leastGain, float cutHz, float mostGain)
        {
            DspFilter filter = new() { Mode = mode, FrequencyHz = 1_000f };

            float passed = Amplitude(filter, passedHz);
            float cut = Amplitude(filter, cutHz);

            Assert.That(passed, Is.GreaterThan(leastGain), $"{passedHz} Hz is in the passband");
            Assert.That(cut, Is.LessThan(mostGain), $"{cutHz} Hz is in the stopband");
        }

        [Test]
        public void ABandPassKeepsItsCentreAtUnityAndCutsBothSides()
        {
            DspFilter filter = new()
            {
                Mode = DspFilterMode.BandPass,
                FrequencyHz = 1_000f,
                Quality = 2f,
            };

            float centre = Amplitude(filter, 1_000f);
            float below = Amplitude(filter, 50f);
            float above = Amplitude(filter, 20_000f);

            Assert.That(centre, Is.EqualTo(1f).Within(0.03f), "the damping normalises the centre to unity");
            Assert.That(below, Is.LessThan(0.1f));
            Assert.That(above, Is.LessThan(0.1f));
        }

        [Test]
        public void ANotchRemovesItsCentreAndKeepsTheRest()
        {
            DspFilter filter = new()
            {
                Mode = DspFilterMode.Notch,
                FrequencyHz = 1_000f,
                Quality = 2f,
            };

            float centre = Amplitude(filter, 1_000f);
            float away = Amplitude(filter, 5_000f);

            Assert.That(centre, Is.LessThan(0.05f), "a tone at the notch is removed");
            Assert.That(away, Is.GreaterThan(0.8f), "a tone away from the notch stays");
        }

        [TestCase(6f, 2f)]
        [TestCase(-9f, 0.355f)]
        public void APeakMovesItsCentreByItsGainAndLeavesTheRest(float decibels, float expectedGain)
        {
            DspFilter filter = new()
            {
                Mode = DspFilterMode.Peak,
                FrequencyHz = 1_000f,
                Quality = 2f,
                PeakDecibels = decibels,
            };

            Prepare(filter);
            float centre = Amplitude(filter, 1_000f);
            float away = Amplitude(filter, 50f);

            Assert.That(centre, Is.EqualTo(expectedGain).Within(expectedGain * 0.05f), "the band moves by the gain");
            Assert.That(away, Is.EqualTo(1f).Within(0.05f), "a frequency away from the peak is untouched");
        }

        [Test]
        public void APeakWithNoGainPassesTheBlockThroughExactly()
        {
            DspFilter filter = new()
            {
                Mode = DspFilterMode.Peak,
                FrequencyHz = 1_000f,
                PeakDecibels = 0f,
            };
            float[] source = Sine(1_000f, 1f, 4_800);

            Prepare(filter);
            float[] output = Render(filter, source);

            Assert.That(output, Is.EqualTo(source), "a peak of zero decibels is a gain of one, which changes no sample");
        }

        [Test]
        public void TheFilterRampsAKnobMoveInsteadOfStepping()
        {
            DspFilter filter = new() { Mode = DspFilterMode.LowPass, FrequencyHz = 20_000f };
            float[] source = Sine(1_000f, 1f, 96_000);

            filter.TryPrepare(SampleRate, BlockFrames, 1);
            float[] whole = new float[source.Length];
            Array.Copy(source, whole, source.Length);

            // Half at a corner that passes the tone, then the same filter at a corner far below it.
            filter.Process(whole, 0, 48_000, 1);
            float lastBefore = whole[47_999];
            filter.FrequencyHz = 100f;
            filter.Process(whole, 48_000, 48_000, 1);

            Assert.That(Math.Abs(whole[48_000] - lastBefore), Is.LessThan(0.2f), "the first frame after the move is still near the old sound");
            Assert.That(Rms(whole, 91_200), Is.LessThan(0.05f), "a tenth of a second later the new corner has taken effect");
        }

        [TestCase(DspFilterMode.LowPass)]
        [TestCase(DspFilterMode.HighPass)]
        [TestCase(DspFilterMode.BandPass)]
        [TestCase(DspFilterMode.Notch)]
        [TestCase(DspFilterMode.Peak)]
        public void ACrowdedOrEmptyBandStaysStableAndFinite(DspFilterMode mode)
        {
            float[] source = Sine(100f, 1f, 9_600);
            float[] crowded = new float[source.Length];
            float[] empty = new float[source.Length];
            Array.Copy(source, crowded, source.Length);
            Array.Copy(source, empty, source.Length);

            DspFilter lowCorner = new()
            {
                Mode = mode,
                FrequencyHz = DspFilter.MinimumFrequencyHz,
                Quality = DspFilter.MaximumQuality,
                PeakDecibels = DspFilter.MaximumPeakDecibels,
            };
            DspFilter highCorner = new()
            {
                Mode = mode,
                FrequencyHz = 1e9f,
                Quality = DspFilter.MaximumQuality,
                PeakDecibels = DspFilter.MaximumPeakDecibels,
            };
            lowCorner.TryPrepare(SampleRate, BlockFrames, 1);
            highCorner.TryPrepare(SampleRate, BlockFrames, 1);

            float[] low = RenderPrepared(lowCorner, crowded);
            float[] high = RenderPrepared(highCorner, empty);

            foreach (float sample in low)
            {
                Assert.That(AudioMath.IsFinite(sample), Is.True, "a corner at the floor cannot poison the state");
                Assert.That(Math.Abs(sample), Is.LessThan(100f));
            }

            foreach (float sample in high)
            {
                Assert.That(AudioMath.IsFinite(sample), Is.True, "a corner above Nyquist is capped, not a tangent to infinity");
                Assert.That(Math.Abs(sample), Is.LessThan(100f));
            }
        }

        [Test]
        public void AnUnpreparedOrMismatchedBlockPassesThroughUntouched()
        {
            float[] source = Sine(1_000f, 0.5f, 512);
            DspFilter unprepared = new() { FrequencyHz = 100f };
            float[] untouched = new float[source.Length];
            Array.Copy(source, untouched, source.Length);

            unprepared.Process(untouched, 0, untouched.Length, 1);
            Assert.That(untouched, Is.EqualTo(source), "a stage that was never prepared has no state to run on");

            DspFilter mono = new() { FrequencyHz = 100f };
            mono.TryPrepare(SampleRate, BlockFrames, 1);
            float[] stereo = new float[source.Length * 2];
            for (int frame = 0; frame < source.Length; frame++)
            {
                stereo[frame * 2] = source[frame];
                stereo[frame * 2 + 1] = source[frame];
            }

            mono.Process(stereo, 0, stereo.Length, 2);

            for (int frame = 0; frame < source.Length; frame++)
            {
                Assert.That(stereo[frame * 2], Is.EqualTo(source[frame]), "a wider block must not index past the state");
            }
        }

        [TestCase(-5, TestName = "A negative offset is refused")]
        [TestCase(4_097, TestName = "A count past the end is refused")]
        public void ABlockOutsideTheBufferIsRefused(int offset)
        {
            DspFilter filter = new() { FrequencyHz = 100f };
            filter.TryPrepare(SampleRate, BlockFrames, 1);
            float[] block = new float[4_096];
            float[] unchanged = new float[4_096];

            filter.Process(block, offset, 2, 1);

            Assert.That(block, Is.EqualTo(unchanged));
        }

        [Test]
        public void AResumedVoiceDoesNotKeepTheLastCueFilterState()
        {
            DspFilter filter = new() { Mode = DspFilterMode.LowPass, FrequencyHz = 100f };

            Prepare(filter);
            _ = Render(filter, Sine(100f, 1f, 4_800));
            filter.Reset();

            float[] silence = new float[256];
            filter.Process(silence, 0, silence.Length, 1);

            Assert.That(Peak(silence), Is.Zero, "a reset clears both integrators");
        }

        [Test]
        public void TheCornerIsRaisedToItsFloor()
        {
            DspFilter filter = new() { FrequencyHz = 1f };

            Assert.That(filter.FrequencyHz, Is.EqualTo(DspFilter.MinimumFrequencyHz));
        }

        [TestCase(0f, DspFilter.MinimumQuality)]
        [TestCase(1_000f, DspFilter.MaximumQuality)]
        public void TheQualityIsClampedToItsRange(float requested, float expected)
        {
            DspFilter filter = new() { Quality = requested };

            Assert.That(filter.Quality, Is.EqualTo(expected));
        }

        [TestCase(-100f, DspFilter.MinimumPeakDecibels)]
        [TestCase(100f, DspFilter.MaximumPeakDecibels)]
        public void ThePeakGainIsClampedToItsRange(float requested, float expected)
        {
            DspFilter filter = new() { PeakDecibels = requested };

            Assert.That(filter.PeakDecibels, Is.EqualTo(expected));
        }

        [Test]
        public void ANonNumberParameterIsRefused()
        {
            DspFilter filter = new()
            {
                FrequencyHz = 500f,
                Quality = 2f,
                PeakDecibels = 3f,
            };

            filter.FrequencyHz = float.NaN;
            filter.Quality = float.PositiveInfinity;
            filter.PeakDecibels = float.NegativeInfinity;

            Assert.That(filter.FrequencyHz, Is.EqualTo(500f), "a bad knob keeps the value that was working");
            Assert.That(filter.Quality, Is.EqualTo(2f));
            Assert.That(filter.PeakDecibels, Is.EqualTo(3f));
        }

        [Test]
        public void TheFilterAddsNoDelay()
        {
            DspFilter filter = new();

            Assert.That(filter.LatencySamples, Is.Zero, "a state-variable filter answers the frame it is given");
            Assert.That(filter.GainReductionDecibels, Is.Zero, "a band shape is not a reduction");
        }

        /// <summary>The amplitude a tone leaves at, after the filter has settled on it.</summary>
        private static float Amplitude(DspFilter filter, float hertz)
        {
            float[] output = Render(filter, Sine(hertz, 1f, 9_600));
            return Rms(output, 4_800) * (float)Math.Sqrt(2.0);
        }

        private static void Prepare(DspEffect effect)
        {
            effect.TryPrepare(SampleRate, BlockFrames, 1);
        }

        private static float[] Render(DspEffect effect, float[] source)
        {
            effect.TryPrepare(SampleRate, BlockFrames, 1);
            return RenderPrepared(effect, source);
        }

        private static float[] RenderPrepared(DspEffect effect, float[] source)
        {
            float[] output = new float[source.Length];
            Array.Copy(source, output, source.Length);
            for (int offset = 0; offset < output.Length; offset += BlockFrames)
            {
                int count = Math.Min(BlockFrames, output.Length - offset);
                effect.Process(output, offset, count, 1);
            }

            return output;
        }

        private static float Peak(float[] samples)
        {
            float peak = 0f;
            for (int index = 0; index < samples.Length; index++)
            {
                float magnitude = Math.Abs(samples[index]);
                if (peak < magnitude)
                {
                    peak = magnitude;
                }
            }

            return peak;
        }

        private static float Rms(float[] samples, int from)
        {
            double sum = 0;
            for (int index = from; index < samples.Length; index++)
            {
                sum += (double)samples[index] * samples[index];
            }

            return (float)Math.Sqrt(sum / (samples.Length - from));
        }

        private static float[] Sine(float hertz, float amplitude, int frames)
        {
            float[] block = new float[frames];
            double step = (2 * Math.PI * hertz) / SampleRate;
            for (int frame = 0; frame < frames; frame++)
            {
                block[frame] = amplitude * (float)Math.Sin(step * frame);
            }

            return block;
        }
    }
}
