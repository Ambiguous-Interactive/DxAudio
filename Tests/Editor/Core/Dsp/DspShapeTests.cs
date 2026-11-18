namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using NUnit.Framework;

    public sealed class DspShapeTests
    {
        private const int SampleRate = 48_000;
        private const int BlockFrames = 256;

        [TestCase(1f, TestName = "A gentle drive stays near the input")]
        [TestCase(8f, TestName = "A hard drive flattens the wave")]
        public void TheDistortionNeverLeavesFullScale(float drive)
        {
            DspDistortion distortion = new() { Drive = drive, ToneHz = DspDistortion.OpenToneHz };
            float[] source = Sine(1_000f, 4f, 4_800);

            Prepare(distortion);
            float[] output = Render(distortion, source);

            Assert.That(Peak(output), Is.LessThanOrEqualTo(1f));
            Assert.That(Peak(output), Is.GreaterThan(0.9f), "a four-times overdriven sine still reaches the top of the curve");
        }

        [TestCase(1f, 3f, TestName = "Three times the drive flattens the crest")]
        [TestCase(1f, 10f, TestName = "Ten times the drive flattens the crest")]
        public void MoreDriveSquaresTheWaveform(float gentleDrive, float hardDrive)
        {
            float[] source = Sine(1_000f, 1f, 9_600);
            DspDistortion gentle = new() { Drive = gentleDrive };
            DspDistortion hard = new() { Drive = hardDrive };

            Prepare(gentle);
            Prepare(hard);

            float gentleCrest = CrestFactor(Render(gentle, source));
            float hardCrest = CrestFactor(Render(hard, source));

            Assert.That(hardCrest, Is.LessThan(gentleCrest), "drive compresses the peaks toward the body of the wave");
        }

        [Test]
        public void TheDistortionToneControlCutsTheTop()
        {
            float[] source = Sine(6_000f, 1f, 9_600);
            DspDistortion bright = new() { Drive = 4f, ToneHz = DspDistortion.OpenToneHz };
            DspDistortion dark = new() { Drive = 4f, ToneHz = 300f };

            Prepare(bright);
            Prepare(dark);

            float brightLevel = Rms(Render(bright, source), 4_800);
            float darkLevel = Rms(Render(dark, source), 4_800);

            Assert.That(darkLevel, Is.LessThan(brightLevel * 0.25f), "a 300 Hz corner buries a 6 kHz tone");
        }

        [TestCase(1, TestName = "One bit keeps two levels")]
        [TestCase(4, TestName = "Four bits keep sixteen levels")]
        [TestCase(8, TestName = "Eight bits keep 256 levels")]
        public void TheBitCrusherQuantizesToItsStep(int bits)
        {
            DspBitCrusher crusher = new() { Bits = bits, RateReduction = 1 };
            float[] source = Ramp(-1f, 1f, 1_000);

            Prepare(crusher);
            float[] output = Render(crusher, source);

            float step = 2f / ((1 << bits) - 1);
            for (int frame = 0; frame < output.Length; frame++)
            {
                float levels = (output[frame] + 1f) / step;
                Assert.That(Math.Abs(levels - Math.Round(levels)), Is.LessThan(0.001f), $"sample {frame} is not a step of {step}");
            }

            Assert.That(Peak(output), Is.LessThanOrEqualTo(1f));
        }

        [Test]
        public void OneBitKeepsOnlyTheTwoEndsOfTheRange()
        {
            DspBitCrusher crusher = new() { Bits = 1 };
            float[] source = Ramp(-1f, 1f, 1_000);

            float[] output = Render(crusher, source, 256);

            for (int frame = 0; frame < output.Length; frame++)
            {
                Assert.That(Math.Abs(output[frame]), Is.EqualTo(1f), $"sample {frame} is one of the two levels");
            }
        }

        [Test]
        public void TheBitCrusherKeepsAWildSampleInRange()
        {
            DspBitCrusher crusher = new() { Bits = 4 };
            float[] source = { -1_000f, 0f, 1_000f, 3.4e38f };

            float[] output = Render(crusher, source, 4);

            Assert.That(Peak(output), Is.LessThanOrEqualTo(1f), "a sample outside the range is bounded before it becomes a level");
        }

        [Test]
        public void TheDistortionRampsADriveChange()
        {
            DspDistortion distortion = new() { Drive = 1f, ToneHz = DspDistortion.OpenToneHz };
            Prepare(distortion);
            float[] steady = new float[256];
            for (int frame = 0; frame < steady.Length; frame++)
            {
                steady[frame] = 0.5f;
            }

            // Process directly: Render would prepare again and snap the drive to its new value.
            float[] before = (float[])steady.Clone();
            distortion.Process(before, 0, before.Length, 1);
            distortion.Drive = 20f;
            float[] after = (float[])steady.Clone();
            distortion.Process(after, 0, after.Length, 1);

            Assert.That(after[0] - before[before.Length - 1], Is.LessThan(0.05f), "the first frame after a knob move is still near the old sound");
            Assert.That(after[after.Length - 1], Is.GreaterThan(after[0] + 0.1f), "the drive reaches its new value over the following frames");
        }

        [Test]
        public void TheDistortionToneIsCappedTheSameWayBeforeAndAfterPrepare()
        {
            float[] source = Sine(6_000f, 1f, 4_800);
            DspDistortion before = new() { Drive = 4f, ToneHz = DspDistortion.OpenToneHz };
            DspDistortion after = new() { Drive = 4f, ToneHz = DspDistortion.OpenToneHz };
            before.Prepare(16_000, BlockFrames, 1);
            after.Prepare(16_000, BlockFrames, 1);
            after.ToneHz = DspDistortion.OpenToneHz;

            Assert.That(Render(after, source), Is.EqualTo(Render(before, source)), "a 20 kHz tone is capped to the rate whichever way it was set");
        }

        [Test]
        public void TheBitCrusherHoldsAcrossABlockBoundary()
        {
            float[] source = Ramp(-0.9f, 0.9f, 4_096);
            DspBitCrusher whole = new() { Bits = 6, RateReduction = 3 };
            DspBitCrusher split = new() { Bits = 6, RateReduction = 3 };

            float[] oneBlock = Render(whole, source, source.Length);
            float[] manyBlocks = Render(split, source, 7);

            Assert.That(manyBlocks, Is.EqualTo(oneBlock), "a hold that spans a block boundary must survive it");
        }

        [Test]
        public void TheBitCrusherHoldsEachSampleForItsRateReduction()
        {
            DspBitCrusher crusher = new() { Bits = 24, RateReduction = 4 };
            float[] source = Ramp(-0.9f, 0.9f, 64);

            Prepare(crusher);
            float[] output = Render(crusher, source);

            for (int frame = 0; frame < output.Length; frame++)
            {
                int firstOfHold = frame - (frame % 4);
                Assert.That(output[frame], Is.EqualTo(output[firstOfHold]), $"frame {frame} belongs to the hold that started at {firstOfHold}");
            }

            Assert.That(output[4], Is.Not.EqualTo(output[0]), "the next hold moves with the ramp");
        }

        [TestCase(0, 1, TestName = "A bit depth below one is raised")]
        [TestCase(40, 24, TestName = "A bit depth above twenty-four is lowered")]
        public void TheBitCrusherClampsItsBitDepth(int requested, int expected)
        {
            DspBitCrusher crusher = new() { Bits = requested };

            Assert.That(crusher.Bits, Is.EqualTo(expected));
        }

        [Test]
        public void TheBitCrusherClampsItsRateReduction()
        {
            DspBitCrusher crusher = new() { RateReduction = 0 };

            Assert.That(crusher.RateReduction, Is.EqualTo(1));
        }

        [Test]
        public void ANonNumberParameterIsRefused()
        {
            DspDistortion distortion = new() { Drive = 2f, ToneHz = 500f };

            distortion.Drive = float.NaN;
            distortion.ToneHz = float.NaN;

            Assert.That(distortion.Drive, Is.EqualTo(2f));
            Assert.That(distortion.ToneHz, Is.EqualTo(500f));

            Prepare(distortion);
            float[] output = Render(distortion, Sine(1_000f, 1f, 1_024));
            for (int frame = 0; frame < output.Length; frame++)
            {
                Assert.That(float.IsNaN(output[frame]), Is.False, $"frame {frame} is not a number");
            }
        }

        [Test]
        public void AResumedVoiceDoesNotKeepTheLastCueToneState()
        {
            DspDistortion distortion = new() { Drive = 8f, ToneHz = 500f };
            float[] loud = Sine(1_000f, 1f, 4_800);

            Prepare(distortion);
            _ = Render(distortion, loud);
            distortion.Reset();

            // Process directly: Render would prepare again, which would pass even if Reset did nothing.
            float[] silence = new float[256];
            distortion.Process(silence, 0, silence.Length, 1);

            Assert.That(Peak(silence), Is.Zero, "a reset clears the tone control's memory");
        }

        private static void Prepare(DspEffect effect)
        {
            effect.Prepare(SampleRate, BlockFrames, 1);
        }

        private static float[] Render(DspEffect effect, float[] source)
        {
            return Render(effect, source, BlockFrames);
        }

        private static float[] Render(DspEffect effect, float[] source, int blockFrames)
        {
            effect.Prepare(SampleRate, blockFrames, 1);
            float[] output = new float[source.Length];
            Array.Copy(source, output, source.Length);
            for (int offset = 0; offset < output.Length; offset += blockFrames)
            {
                int count = Math.Min(blockFrames, output.Length - offset);
                effect.Process(output, offset, count, 1);
            }

            return output;
        }

        private static float Peak(float[] samples)
        {
            float peak = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float magnitude = Math.Abs(samples[i]);
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
            for (int i = from; i < samples.Length; i++)
            {
                sum += (double)samples[i] * samples[i];
            }

            return (float)Math.Sqrt(sum / (samples.Length - from));
        }

        private static float CrestFactor(float[] samples)
        {
            float rms = Rms(samples, 0);
            return rms <= 0f ? float.MaxValue : Peak(samples) / rms;
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

        private static float[] Ramp(float from, float to, int frames)
        {
            float[] block = new float[frames];
            for (int frame = 0; frame < frames; frame++)
            {
                block[frame] = from + (((to - from) * frame) / (frames - 1));
            }

            return block;
        }
    }
}
