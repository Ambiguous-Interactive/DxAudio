namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using NUnit.Framework;

    public sealed class DspDynamicsTests
    {
        private const int SampleRate = 48_000;
        private const int BlockFrames = 256;

        [Test]
        public void TheLimiterPassesASignalBelowItsCeilingUntouched()
        {
            DspLimiter limiter = new() { Ceiling = 0.5f, LookaheadSeconds = 0.0015f };
            float[] source = Sine(440f, 0.1f, 4_800);

            Prepare(limiter);
            float[] output = Render(limiter, source);

            int latency = limiter.LatencySamples;
            Assert.That(latency, Is.EqualTo(72));
            for (int frame = 0; frame < latency; frame++)
            {
                Assert.That(output[frame], Is.Zero, "the lookahead delay is silent");
            }

            for (int frame = latency; frame < source.Length; frame++)
            {
                Assert.That(output[frame], Is.EqualTo(source[frame - latency]), "a quiet signal is only delayed, never turned down");
            }
        }

        [Test]
        public void TheLimiterHoldsThePeakUnderItsCeiling()
        {
            DspLimiter limiter = new()
            {
                Ceiling = 0.5f,
                LookaheadSeconds = 0.0015f,
                ReleaseSeconds = 0.05f,
            };
            float[] source = Sine(1_000f, 1f, 9_600);

            Prepare(limiter);
            float[] output = Render(limiter, source);

            Assert.That(Peak(output, 2_400), Is.LessThanOrEqualTo(0.5001f), "a full-scale sine leaves at the ceiling");
            Assert.That(Peak(output, 2_400), Is.GreaterThan(0.45f), "the limiter turns the signal down, not off");
            Assert.That(limiter.GainReductionDecibels, Is.LessThan(-5f), "the reduction is reported for a meter");
        }

        [Test]
        public void TheLimiterWithoutLookaheadStillHoldsTheCeiling()
        {
            DspLimiter limiter = new() { Ceiling = 0.25f, LookaheadSeconds = 0f };
            float[] source = Sine(1_000f, 1f, 4_800);

            Prepare(limiter);
            float[] output = Render(limiter, source);

            Assert.That(limiter.LatencySamples, Is.Zero);
            Assert.That(Peak(output, 0), Is.LessThanOrEqualTo(0.2501f));
        }

        [Test]
        public void TheLimiterReleasesOnceThePeakIsGone()
        {
            DspLimiter limiter = new()
            {
                Ceiling = 0.5f,
                LookaheadSeconds = 0.0015f,
                ReleaseSeconds = 0.05f,
            };
            float[] loud = Sine(1_000f, 1f, 4_800);
            float[] quiet = new float[9_600];

            Prepare(limiter);
            _ = Render(limiter, loud);
            _ = Render(limiter, quiet);

            Assert.That(limiter.GainReductionDecibels, Is.GreaterThan(-1f), "four release times bring the gain back to unity");
        }

        [Test]
        public void TheCompressorReachesItsStaticCurve()
        {
            DspCompressor compressor = new()
            {
                ThresholdDecibels = -20f,
                Ratio = 4f,
                AttackSeconds = 0.001f,
                ReleaseSeconds = 0.1f,
            };
            float[] source = Constant(1f, SampleRate);

            Prepare(compressor);
            float[] output = Render(compressor, source);

            // Fifteen decibels of reduction: fifteen decibels over the threshold leave as a quarter of that.
            float expected = AudioMath.DecibelsToLinear(-15f);
            Assert.That(output[output.Length - 1], Is.EqualTo(expected).Within(expected * 0.05f));
            Assert.That(compressor.GainReductionDecibels, Is.EqualTo(-15f).Within(0.5f));
        }

        [TestCase(-40f, TestName = "A signal under the threshold passes")]
        [TestCase(-24f, TestName = "A signal at the threshold passes")]
        public void TheCompressorLeavesQuietSignalsAlone(float decibels)
        {
            DspCompressor compressor = new() { ThresholdDecibels = -24f, Ratio = 4f };
            float level = AudioMath.DecibelsToLinear(decibels);
            float[] source = Constant(level, 4_800);

            Prepare(compressor);
            float[] output = Render(compressor, source);

            Assert.That(output[output.Length - 1], Is.EqualTo(level).Within(0.0001f));
            Assert.That(compressor.GainReductionDecibels, Is.Zero.Within(0.001f));
        }

        [Test]
        public void TheCompressorAttackSpreadsTheReductionOverTime()
        {
            DspCompressor compressor = new()
            {
                ThresholdDecibels = -20f,
                Ratio = 4f,
                AttackSeconds = 0.05f,
                ReleaseSeconds = 0.1f,
            };
            float[] source = Constant(1f, SampleRate);

            Prepare(compressor);
            float[] output = Render(compressor, source);

            Assert.That(output[0], Is.GreaterThan(0.9f), "the first sample has not been turned down yet");
            Assert.That(output[output.Length - 1], Is.EqualTo(AudioMath.DecibelsToLinear(-15f)).Within(0.01f), "ten attack times finish the reduction");
        }

        [Test]
        public void TheCompressorReleaseRecoversAfterAQuietPassage()
        {
            DspCompressor compressor = new()
            {
                ThresholdDecibels = -20f,
                Ratio = 4f,
                AttackSeconds = 0.001f,
                ReleaseSeconds = 0.05f,
            };
            float[] loud = Constant(1f, 9_600);
            float[] quiet = Constant(0.001f, 14_400);

            Prepare(compressor);
            _ = Render(compressor, loud);
            float[] output = Render(compressor, quiet);

            Assert.That(compressor.GainReductionDecibels, Is.GreaterThan(-1f));
            Assert.That(output[output.Length - 1], Is.EqualTo(0.001f).Within(0.0001f));
        }

        [Test]
        public void TheCompressorAppliesMakeupGainAfterTheReduction()
        {
            DspCompressor compressor = new() { ThresholdDecibels = -24f, MakeupDecibels = 6f };
            float[] source = Constant(0.01f, 4_800);

            Prepare(compressor);
            float[] output = Render(compressor, source);

            Assert.That(output[output.Length - 1], Is.EqualTo(0.01f * AudioMath.DecibelsToLinear(6f)).Within(0.0001f));
        }

        [Test]
        public void AResumedVoiceDoesNotKeepTheLastCueEnvelope()
        {
            DspCompressor compressor = new()
            {
                ThresholdDecibels = -20f,
                Ratio = 4f,
                AttackSeconds = 0f,
            };
            float[] loud = Constant(1f, 4_800);

            Prepare(compressor);
            _ = Render(compressor, loud);
            compressor.Reset();

            Assert.That(compressor.GainReductionDecibels, Is.Zero.Within(0.001f));
        }

        [Test]
        public void TheLimiterTurnsBothChannelsDownTogether()
        {
            DspLimiter limiter = new()
            {
                Ceiling = 0.5f,
                LookaheadSeconds = 0.0015f,
                ReleaseSeconds = 0.05f,
            };
            float[] source = Sine(1_000f, 1f, 4_800);
            float[] interleaved = new float[source.Length * 2];
            for (int frame = 0; frame < source.Length; frame++)
            {
                interleaved[frame * 2] = source[frame];
                interleaved[frame * 2 + 1] = source[frame] * 0.1f;
            }

            limiter.Prepare(SampleRate, BlockFrames, 2);
            for (int offset = 0; offset < interleaved.Length; offset += BlockFrames * 2)
            {
                int count = Math.Min(BlockFrames * 2, interleaved.Length - offset);
                limiter.Process(interleaved, offset, count, 2);
            }

            float left = 0f;
            float right = 0f;
            for (int frame = 1_200; frame < source.Length; frame++)
            {
                left = Math.Max(left, Math.Abs(interleaved[frame * 2]));
                right = Math.Max(right, Math.Abs(interleaved[frame * 2 + 1]));
            }

            Assert.That(left, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(right, Is.EqualTo(0.05f).Within(0.001f), "one gain for every channel keeps the image still");
        }

        private static void Prepare(DspEffect effect)
        {
            effect.Prepare(SampleRate, BlockFrames, 1);
        }

        private static float[] Render(DspEffect effect, float[] source)
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

        private static float Peak(float[] samples, int from)
        {
            float peak = 0f;
            for (int i = from; i < samples.Length; i++)
            {
                float magnitude = Math.Abs(samples[i]);
                if (peak < magnitude)
                {
                    peak = magnitude;
                }
            }

            return peak;
        }

        private static float[] Constant(float value, int frames)
        {
            float[] block = new float[frames];
            for (int frame = 0; frame < frames; frame++)
            {
                block[frame] = value;
            }

            return block;
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
