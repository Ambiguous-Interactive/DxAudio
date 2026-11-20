namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using NUnit.Framework;

    public sealed class DspFilterTests
    {
        private const int SampleRate = 48_000;
        private const int BlockFrames = 256;

        /// <summary>The amplitude a tone leaves at, after the filter has settled on it.</summary>
        private static float Amplitude(DspFilter filter, float hertz)
        {
            float[] output = Render(filter, Sine(hertz, 1f, 9_600));
            return Rms(output, 4_800) * (float)Math.Sqrt(2.0);
        }

        /// <summary>The response at one tone, for a filter at a 1 kHz corner and the quality its mode documents.</summary>
        private static float Decibels(DspFilterMode mode, float hertz, float peakDecibels)
        {
            DspFilter filter = new()
            {
                Mode = mode,
                FrequencyHz = 1_000f,
                Quality = mode == DspFilterMode.LowPass || mode == DspFilterMode.HighPass ? DspFilter.DefaultQuality : 2f,
                PeakDecibels = peakDecibels,
            };

            return 20f * (float)Math.Log10(Amplitude(filter, hertz));
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
            for (int offset = 0, outputLength = output.Length; offset < outputLength; offset += BlockFrames)
            {
                int count = Math.Min(BlockFrames, output.Length - offset);
                effect.Process(output, offset, count, 1);
            }

            return output;
        }

        private static float Peak(float[] samples, int from)
        {
            return Peak(samples, from, samples.Length - from);
        }

        private static float Peak(float[] samples, int from, int count)
        {
            float peak = 0f;
            for (int index = from; index < from + count; ++index)
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
            for (int index = from, samplesLength = samples.Length; index < samplesLength; ++index)
            {
                sum += (double)samples[index] * samples[index];
            }

            return (float)Math.Sqrt(sum / (samples.Length - from));
        }

        private static float[] Sine(float hertz, float amplitude, int frames)
        {
            float[] block = new float[frames];
            double step = (2 * Math.PI * hertz) / SampleRate;
            for (int frame = 0; frame < frames; ++frame)
            {
                block[frame] = amplitude * (float)Math.Sin(step * frame);
            }

            return block;
        }

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

        [TestCase(DspFilterMode.Peak, 6f, 2f)]
        [TestCase(DspFilterMode.Peak, -9f, 0.355f)]
        [TestCase(DspFilterMode.Bell, 6f, 2f)]
        [TestCase(DspFilterMode.Bell, -9f, 0.355f)]
        public void AGainBandMovesItsCentreByItsGainAndLeavesTheRest(DspFilterMode mode, float decibels, float expectedGain)
        {
            DspFilter filter = new()
            {
                Mode = mode,
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

        [TestCase(DspFilterMode.Peak)]
        [TestCase(DspFilterMode.Bell)]
        public void AGainBandWithNoGainPassesTheBlockThroughExactly(DspFilterMode mode)
        {
            DspFilter filter = new()
            {
                Mode = mode,
                FrequencyHz = 1_000f,
                PeakDecibels = 0f,
            };
            float[] source = Sine(1_000f, 1f, 4_800);

            Prepare(filter);
            float[] output = Render(filter, source);

            Assert.That(output, Is.EqualTo(source), "a gain of zero decibels is a gain of one, which changes no sample");
        }

        [TestCase(24f, 700f)]
        [TestCase(24f, 2_000f)]
        [TestCase(12f, 700f)]
        [TestCase(6f, 2_000f)]
        public void ABellMirrorsItsBoostAndCut(float decibels, float hertz)
        {
            float boost = Decibels(DspFilterMode.Bell, hertz, decibels);
            float cut = Decibels(DspFilterMode.Bell, hertz, -decibels);

            Assert.That(boost, Is.EqualTo(-cut).Within(0.2f), "an equally sized boost and cut have the same width");
        }

        [TestCase(24f, 19.1f, -1.7f)]
        [TestCase(12f, 7.6f, -1.5f)]
        public void APeakKeepsItsWiderBoostSkirt(float decibels, float boost, float cut)
        {
            Assert.That(Decibels(DspFilterMode.Peak, 700f, decibels), Is.EqualTo(boost).Within(0.2f), "the published boost skirt is unchanged");
            Assert.That(Decibels(DspFilterMode.Peak, 700f, -decibels), Is.EqualTo(cut).Within(0.2f), "the published cut skirt is unchanged");
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
            filter.FrequencyHz = 100f;
            filter.Process(whole, 48_000, 48_000, 1);

            /*
                Two milliseconds after the move the old passband is still there; a coefficient step would have closed it
                in the first frames, which is what this reads.
            */
            Assert.That(Peak(whole, 48_096, 96), Is.GreaterThan(0.5f), "the corner ramps rather than jumping");
            Assert.That(Rms(whole, 91_200), Is.LessThan(0.05f), "a tenth of a second later the new corner has taken effect");
        }

        [Test]
        public void TheFilterRunsEachChannelOnItsOwnState()
        {
            float[] left = Sine(1_000f, 0.5f, 4_608);
            float[] right = Sine(3_000f, 0.25f, 4_608);
            DspFilter monoLeft = new() { Mode = DspFilterMode.LowPass, FrequencyHz = 1_500f };
            DspFilter monoRight = new() { Mode = DspFilterMode.LowPass, FrequencyHz = 1_500f };
            float[] expectedLeft = Render(monoLeft, left);
            float[] expectedRight = Render(monoRight, right);

            DspFilter stereo = new() { Mode = DspFilterMode.LowPass, FrequencyHz = 1_500f };
            stereo.TryPrepare(SampleRate, BlockFrames, 2);
            float[] interleaved = new float[left.Length * 2];
            for (int frame = 0, leftLength = left.Length; frame < leftLength; ++frame)
            {
                interleaved[frame * 2] = left[frame];
                interleaved[frame * 2 + 1] = right[frame];
            }

            for (int offset = 0, interleavedLength = interleaved.Length; offset < interleavedLength; offset += BlockFrames * 2)
            {
                int count = Math.Min(BlockFrames * 2, interleaved.Length - offset);
                stereo.Process(interleaved, offset, count, 2);
            }

            for (int frame = 0, leftLength = left.Length; frame < leftLength; ++frame)
            {
                Assert.That(interleaved[frame * 2], Is.EqualTo(expectedLeft[frame]).Within(0.000001f), "the left channel keeps its own state");
                Assert.That(interleaved[frame * 2 + 1], Is.EqualTo(expectedRight[frame]).Within(0.000001f), "the right channel keeps its own state");
            }
        }

        [Test]
        public void TheFilterDoesNotDependOnTheBlockSize()
        {
            float[] source = Sine(1_000f, 1f, 8_192);
            DspFilter narrow = new() { FrequencyHz = 1_000f, Quality = 2f };
            DspFilter wide = new() { FrequencyHz = 1_000f, Quality = 2f };
            narrow.TryPrepare(SampleRate, 1_024, 1);
            wide.TryPrepare(SampleRate, 1_024, 1);

            float[] small = (float[])source.Clone();
            float[] large = (float[])source.Clone();
            for (int offset = 0, smallLength = small.Length; offset < smallLength; offset += 64)
            {
                narrow.Process(small, offset, 64, 1);
            }

            for (int offset = 0, largeLength = large.Length; offset < largeLength; offset += 1_024)
            {
                wide.Process(large, offset, 1_024, 1);
            }

            Assert.That(small, Is.EqualTo(large), "the ramp moves once per frame, not once per call");
        }

        [TestCase(1_000f, DspFilterMode.LowPass, 0f, -3.00f)]
        [TestCase(2_000f, DspFilterMode.LowPass, 0f, -12.37f)]
        [TestCase(8_000f, DspFilterMode.LowPass, 0f, -37.80f)]
        [TestCase(125f, DspFilterMode.HighPass, 0f, -36.11f)]
        [TestCase(1_000f, DspFilterMode.BandPass, 0f, 0.00f)]
        [TestCase(500f, DspFilterMode.BandPass, 0f, -10.02f)]
        [TestCase(1_000f, DspFilterMode.Peak, 6f, 5.99f)]
        [TestCase(700f, DspFilterMode.Bell, 6f, 2.00f)]
        [TestCase(1_000f, DspFilterMode.Bell, 6f, 6.00f)]
        [TestCase(2_000f, DspFilterMode.Bell, 6f, 0.60f)]
        public void TheResponseMatchesTheDesignRecord(float hertz, DspFilterMode mode, float peakDecibels, float expectedDecibels)
        {
            float decibels = Decibels(mode, hertz, peakDecibels);

            Assert.That(decibels, Is.EqualTo(expectedDecibels).Within(0.5f), "the documented response is the measured one");
        }

        [TestCase(DspFilterMode.LowPass)]
        [TestCase(DspFilterMode.HighPass)]
        [TestCase(DspFilterMode.BandPass)]
        [TestCase(DspFilterMode.Notch)]
        [TestCase(DspFilterMode.Peak)]
        [TestCase(DspFilterMode.Bell)]
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
                Assert.That(Math.Abs(sample), Is.LessThan(40f), "a quality of twenty lifts the corner by at most 26 dB");
            }

            foreach (float sample in high)
            {
                Assert.That(AudioMath.IsFinite(sample), Is.True, "a corner above Nyquist is capped, not a tangent to infinity");
                Assert.That(Math.Abs(sample), Is.LessThan(40f));
            }
        }

        [Test]
        public void AMovingCornerAtMaximumQualityStaysStable()
        {
            DspFilter filter = new()
            {
                Mode = DspFilterMode.LowPass,
                FrequencyHz = DspFilter.MinimumFrequencyHz,
                Quality = DspFilter.MaximumQuality,
                PeakDecibels = DspFilter.MaximumPeakDecibels,
            };
            filter.TryPrepare(SampleRate, BlockFrames, 1);
            float[] source = Sine(1_000f, 1f, BlockFrames);

            for (int pass = 0; pass < 200; ++pass)
            {
                // A fresh copy every pass: the block is not fed back, so only the filter can make it grow.
                float[] block = (float[])source.Clone();
                filter.FrequencyHz = DspFilter.MinimumFrequencyHz + (pass * 100f);
                filter.Process(block, 0, block.Length, 1);

                foreach (float sample in block)
                {
                    Assert.That(AudioMath.IsFinite(sample), Is.True, $"a sweeping corner never produces a non-number at pass {pass}");
                    Assert.That(Math.Abs(sample), Is.LessThan(40f), $"a sweeping corner never runs away at pass {pass}");
                }
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
            for (int frame = 0, sourceLength = source.Length; frame < sourceLength; ++frame)
            {
                stereo[frame * 2] = source[frame];
                stereo[frame * 2 + 1] = source[frame];
            }

            mono.Process(stereo, 0, stereo.Length, 2);

            for (int frame = 0, sourceLength = source.Length; frame < sourceLength; ++frame)
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

            Assert.That(Peak(silence, 0), Is.Zero, "a reset clears both integrators");
        }

        [Test]
        public void TheCornerIsClampedToItsRange()
        {
            DspFilter low = new() { FrequencyHz = 1f };
            DspFilter high = new() { FrequencyHz = 30_000f };

            Assert.That(low.FrequencyHz, Is.EqualTo(DspFilter.MinimumFrequencyHz));
            Assert.That(high.FrequencyHz, Is.EqualTo(DspFilter.MaximumFrequencyHz), "a burst of speed does not give a corner the inspector cannot hold");
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
    }
}
