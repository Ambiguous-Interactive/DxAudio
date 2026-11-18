namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using NUnit.Framework;

    /// <summary>Pins how the delay-line pitch shifter moves a tone, what it delays, and what a reused voice gets back.</summary>
    public sealed class DspPitchShifterTests
    {
        private const int SampleRate = 48_000;

        [Test]
        public void AnUnpreparedShifterLeavesTheBlockUntouched()
        {
            DspPitchShifter shifter = new() { Semitones = 12f };
            float[] block = Sine(440f, 0.5f, 128);
            float[] before = (float[])block.Clone();

            shifter.Process(block, 0, block.Length, 1);

            Assert.That(shifter.LatencySamples, Is.Zero, "a stage that cannot run delays nothing it can report");
            Assert.That(block, Is.EqualTo(before));
        }

        [Test]
        public void ABlockOfAnotherChannelCountIsRefused()
        {
            DspPitchShifter shifter = new() { Semitones = 12f };
            shifter.Prepare(SampleRate, 256, 2);
            float[] block = Sine(440f, 0.5f, 128);
            float[] before = (float[])block.Clone();

            shifter.Process(block, 0, block.Length, 1);

            Assert.That(block, Is.EqualTo(before), "a mono block under a stereo layout is not the prepared shape");
        }

        [TestCase(0, 256, 2, TestName = "A zero sample rate is refused")]
        [TestCase(48_000, 0, 2, TestName = "A zero block size is refused")]
        [TestCase(48_000, 256, 0, TestName = "A zero channel count is refused")]
        public void PrepareRefusesAnImpossibleLayout(int sampleRate, int maxBlockSize, int channels)
        {
            DspPitchShifter shifter = new();

            _ = Assert.Throws<ArgumentOutOfRangeException>(() => shifter.Prepare(sampleRate, maxBlockSize, channels));
        }

        [TestCase(0.05f, 1_201, TestName = "The default window delays half of it plus one frame")]
        [TestCase(0.005f, 121, TestName = "The shortest window delays its half plus one frame")]
        [TestCase(0.2f, 4_801, TestName = "The longest window delays its half plus one frame")]
        public void LatencyIsHalfTheWindowPlusOneFrame(float windowSeconds, int expected)
        {
            DspPitchShifter shifter = new() { WindowSeconds = windowSeconds };
            shifter.Prepare(SampleRate, 256, 1);

            Assert.That(shifter.LatencySamples, Is.EqualTo(expected));
        }

        [Test]
        public void ANewWindowWaitsForTheNextPrepare()
        {
            DspPitchShifter shifter = new() { WindowSeconds = 0.05f };
            shifter.Prepare(SampleRate, 256, 1);

            shifter.WindowSeconds = 0.01f;

            Assert.That(shifter.LatencySamples, Is.EqualTo(1_201), "a live stage keeps the line it prepared, so a setter never allocates under the audio thread");
            shifter.Prepare(SampleRate, 256, 1);
            Assert.That(shifter.LatencySamples, Is.EqualTo(241), "the next Prepare sizes the line for the new window");
        }

        [TestCase(1, TestName = "A mono impulse arrives once at the reported latency")]
        [TestCase(2, TestName = "A stereo impulse keeps its channels apart")]
        public void AtUnisonTheSignalArrivesAtTheReportedLatency(int channels)
        {
            DspPitchShifter shifter = new() { WindowSeconds = 0.05f };
            shifter.Prepare(SampleRate, 256, channels);
            int latency = shifter.LatencySamples;
            int frames = latency + 512;
            float[] input = new float[frames * channels];
            for (int channel = 0; channel < channels; channel++)
            {
                input[(100 * channels) + channel] = 0.25f * (channel + 1);
            }

            float[] output = (float[])input.Clone();
            ProcessInBlocks(shifter, output, channels, 256);

            for (int channel = 0; channel < channels; channel++)
            {
                int arrival = ((100 + latency) * channels) + channel;
                Assert.That(output[arrival], Is.EqualTo(input[(100 * channels) + channel]), "the impulse keeps its level and its channel");
                for (int frame = 0; frame < frames; frame++)
                {
                    int index = (frame * channels) + channel;
                    if (index != arrival)
                    {
                        Assert.That(output[index], Is.Zero, $"frame {frame} on channel {channel} is outside the delayed impulse");
                    }
                }
            }
        }

        [TestCase(12f, 880f, 440f, TestName = "An octave up doubles the frequency")]
        [TestCase(-12f, 220f, 440f, TestName = "An octave down halves the frequency")]
        [TestCase(7f, 659.26f, 440f, TestName = "A fifth up moves to the tempered fifth")]
        public void AShiftMovesTheToneToTheExpectedFrequency(float semitones, double expectedHz, double inputHz)
        {
            DspPitchShifter shifter = new() { Semitones = semitones };
            shifter.Prepare(SampleRate, 256, 1);
            int analysisStart = shifter.LatencySamples + 2_048;
            int analysisFrames = 12_000;
            float[] block = Sine((float)inputHz, 0.5f, analysisStart + analysisFrames);

            ProcessInBlocks(shifter, block, 1, 256);

            double moved = MagnitudeAt(block, analysisStart, analysisFrames, expectedHz);
            double kept = MagnitudeAt(block, analysisStart, analysisFrames, inputHz);
            Assert.That(moved, Is.GreaterThan(0.1), $"the shifted tone is present at {expectedHz:0.##} Hz");
            Assert.That(kept, Is.LessThan(moved * 0.1), $"the input tone is gone from {inputHz:0.##} Hz");
        }

        [Test]
        public void UnisonDelaysTheToneWithoutMovingIt()
        {
            DspPitchShifter shifter = new();
            shifter.Prepare(SampleRate, 256, 1);
            int analysisStart = shifter.LatencySamples + 2_048;
            int analysisFrames = 12_000;
            float[] block = Sine(440f, 0.5f, analysisStart + analysisFrames);

            ProcessInBlocks(shifter, block, 1, 256);

            double kept = MagnitudeAt(block, analysisStart, analysisFrames, 440f);
            double moved = MagnitudeAt(block, analysisStart, analysisFrames, 880f);
            Assert.That(kept, Is.EqualTo(0.5).Within(0.01), "a half-scale tone arrives at its own level");
            Assert.That(moved, Is.LessThan(0.01), "unison must not add an octave");
        }

        [TestCase(12f, TestName = "A full-scale input stays in range an octave up")]
        [TestCase(-12f, TestName = "A full-scale input stays in range an octave down")]
        [TestCase(24f, TestName = "A full-scale input stays in range at the highest shift")]
        public void AShiftKeepsTheSignalInsideItsInputRange(float semitones)
        {
            DspPitchShifter shifter = new() { Semitones = semitones };
            shifter.Prepare(SampleRate, 256, 1);
            float[] block = Sine(1_000f, 1f, 24_000);

            ProcessInBlocks(shifter, block, 1, 256);

            Assert.That(Peak(block), Is.LessThanOrEqualTo(1.0001f), "two interpolated taps at gains that sum to one cannot lift a peak");
        }

        [Test]
        public void AReusedShifterMatchesAFreshOneAfterReset()
        {
            DspPitchShifter reused = new() { Semitones = 12f };
            reused.Prepare(SampleRate, 256, 1);
            float[] noise = Sine(1_000f, 1f, 4_096);
            ProcessInBlocks(reused, noise, 1, 256);
            reused.Reset();
            float[] expected = Sine(440f, 0.5f, 8_192);
            float[] afterReset = (float[])expected.Clone();

            ProcessInBlocks(reused, afterReset, 1, 256);

            DspPitchShifter fresh = new() { Semitones = 12f };
            fresh.Prepare(SampleRate, 256, 1);
            float[] fromFresh = (float[])expected.Clone();
            ProcessInBlocks(fresh, fromFresh, 1, 256);

            Assert.That(afterReset, Is.EqualTo(fromFresh), "a reset voice carries no delay line and no sweep from the last sound");
        }

        [Test]
        public void AShiftSoundsTheSameAtAnyBlockSize()
        {
            float[] fine = Sine(440f, 0.5f, 8_192);
            float[] coarse = (float[])fine.Clone();
            DspPitchShifter fineShifter = new() { Semitones = 7f };
            DspPitchShifter coarseShifter = new() { Semitones = 7f };
            fineShifter.Prepare(SampleRate, 64, 1);
            coarseShifter.Prepare(SampleRate, 1_024, 1);

            ProcessInBlocks(fineShifter, fine, 1, 64);
            ProcessInBlocks(coarseShifter, coarse, 1, 1_024);

            Assert.That(coarse, Is.EqualTo(fine), "a block boundary must not change the sweep");
            Assert.That(fine, Is.Not.EqualTo(Sine(440f, 0.5f, 8_192)), "an unprepared stage would pass the block through unchanged");
        }

        [Test]
        public void AShiftedConstantKeepsItsLevel()
        {
            DspPitchShifter shifter = new() { Semitones = 12f };
            shifter.Prepare(SampleRate, 256, 1);
            int settled = shifter.LatencySamples * 2;
            float[] block = new float[settled + 4_096];
            for (int index = 0; index < block.Length; index++)
            {
                block[index] = 0.5f;
            }

            ProcessInBlocks(shifter, block, 1, 256);

            for (int index = settled; index < block.Length; index++)
            {
                Assert.That(block[index], Is.EqualTo(0.5f).Within(0.0001f), "gains that sum to one keep a steady level, so neither head is missing or double-counted");
            }
        }

        [Test]
        public void ANonNumberParameterIsRefused()
        {
            DspPitchShifter shifter = new() { Semitones = 7f, WindowSeconds = 0.02f };

            shifter.Semitones = float.NaN;
            shifter.WindowSeconds = float.PositiveInfinity;

            Assert.That(shifter.Semitones, Is.EqualTo(7f));
            Assert.That(shifter.WindowSeconds, Is.EqualTo(0.02f));

            shifter.Prepare(SampleRate, 256, 1);
            float[] block = Sine(1_000f, 1f, 4_096);
            ProcessInBlocks(shifter, block, 1, 256);
            foreach (float sample in block)
            {
                Assert.That(float.IsNaN(sample), Is.False, "every sample is a number");
            }
        }

        [TestCase(12f, 0f, TestName = "Returning to unison from an octave up does not splice the waveform")]
        [TestCase(-12f, 0f, TestName = "Returning to unison from an octave down does not splice the waveform")]
        [TestCase(0f, 12f, TestName = "Leaving unison upwards does not splice the waveform")]
        [TestCase(0f, -12f, TestName = "Leaving unison downwards does not splice the waveform")]
        public void CrossingUnisonDoesNotSpliceTheWaveform(float before, float after)
        {
            DspPitchShifter shifter = new() { Semitones = before };
            shifter.Prepare(SampleRate, 256, 1);
            float[] block = Sine(997f, 0.5f, 16_384);
            int half = block.Length / 2;

            ProcessInBlocks(shifter, block, 1, 256, 0, half);
            shifter.Semitones = after;
            ProcessInBlocks(shifter, block, 1, 256, half, block.Length);

            float largest = 0f;
            for (int frame = 1; frame < block.Length; frame++)
            {
                float step = Math.Abs(block[frame] - block[frame - 1]);
                if (largest < step)
                {
                    largest = step;
                }
            }

            // The loudest tone here is 1,994 Hz at half scale, whose own slope is 0.13 per sample at 48 kHz.
            Assert.That(largest, Is.LessThan(0.15f), "a switch at the boundary is as smooth as the tone itself, so neither direction clicks");
        }

        [TestCase(12f, TestName = "An octave up returns to a pure delay")]
        [TestCase(-12f, TestName = "An octave down returns to a pure delay")]
        public void UnisonAfterAShiftIsAPureDelayAgain(float semitones)
        {
            DspPitchShifter shifter = new() { Semitones = semitones };
            shifter.Prepare(SampleRate, 256, 1);
            int latency = shifter.LatencySamples;
            int frames = 16_384;
            float[] input = Sine(440f, 0.5f, frames);
            float[] block = (float[])input.Clone();
            int half = frames / 2;

            ProcessInBlocks(shifter, block, 1, 256, 0, half);
            shifter.Semitones = 0f;
            ProcessInBlocks(shifter, block, 1, 256, half, frames);

            // The mix crossfades into the settled read over ten milliseconds, then the delay is fixed.
            int fade = (int)(0.01f * SampleRate);
            for (int frame = half + fade; frame < frames; frame++)
            {
                Assert.That(block[frame], Is.EqualTo(input[frame - latency]), $"frame {frame} is a copy of the input, not a comb of two heads");
            }
        }

        private static void ProcessInBlocks(DspEffect effect, float[] buffer, int channels, int blockFrames)
        {
            ProcessInBlocks(effect, buffer, channels, blockFrames, 0, buffer.Length / channels);
        }

        private static void ProcessInBlocks(DspEffect effect, float[] buffer, int channels, int blockFrames, int startFrame, int endFrame)
        {
            for (int frame = startFrame; frame < endFrame; frame += blockFrames)
            {
                int count = Math.Min(blockFrames, endFrame - frame) * channels;
                effect.Process(buffer, frame * channels, count, channels);
            }
        }

        /// <summary>The amplitude of one frequency over a window, by projection, so a warble cannot fool a crossing count.</summary>
        private static double MagnitudeAt(float[] samples, int startFrame, int frames, double hertz)
        {
            double real = 0;
            double imaginary = 0;
            double step = (2 * Math.PI * hertz) / SampleRate;
            for (int frame = 0; frame < frames; frame++)
            {
                double sample = samples[startFrame + frame];
                real += sample * Math.Cos(step * frame);
                imaginary += sample * Math.Sin(step * frame);
            }

            return (2 * Math.Sqrt((real * real) + (imaginary * imaginary))) / frames;
        }

        private static float Peak(float[] samples)
        {
            float peak = 0f;
            foreach (float sample in samples)
            {
                float magnitude = Math.Abs(sample);
                if (peak < magnitude)
                {
                    peak = magnitude;
                }
            }

            return peak;
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
