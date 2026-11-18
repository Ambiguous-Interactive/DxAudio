namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using NUnit.Framework;

    public sealed class DspDynamicsTests
    {
        private const int SampleRate = 48_000;
        private const int BlockFrames = 256;

        [Test]
        public void AVeryLongTimeConstantStillMoves()
        {
            Assert.That(AudioMath.OnePoleCoefficient(1_000f, SampleRate), Is.GreaterThan(0f), "the subtraction happens before the cast to float");

            // Without the fix the 5 ms coefficient is still right, so the shape is checked as the physics it
            // stands for: one time constant closes about 63% of a step.
            float coefficient = AudioMath.OnePoleCoefficient(0.005f, SampleRate);
            float smoothed = 0f;
            for (int frame = 0; frame < 240; frame++)
            {
                smoothed += (1f - smoothed) * coefficient;
            }

            Assert.That(smoothed, Is.EqualTo(1f - (float)Math.Exp(-1.0)).Within(0.001f));
        }

        [TestCase(1e34f)]
        [TestCase(float.MaxValue)]
        public void AnExtremeTimeConstantStillMoves(float seconds)
        {
            float coefficient = AudioMath.OnePoleCoefficient(seconds, SampleRate);

            Assert.That(coefficient, Is.GreaterThan(0f), "a long time constant is slow, never frozen");
            Assert.That(float.IsInfinity(coefficient), Is.False);
            Assert.That(coefficient, Is.LessThan(0.001f));
        }

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
        public void TheLimiterHoldsASparsePeakUnderTheCeiling()
        {
            DspLimiter limiter = new()
            {
                Ceiling = 0.1f,
                LookaheadSeconds = 0.005f,
                ReleaseSeconds = 0.05f,
            };
            float[] source = new float[9_600];
            source[1_000] = 1f;

            Prepare(limiter);
            float[] output = Render(limiter, source);

            Assert.That(Peak(output, 0), Is.LessThanOrEqualTo(0.1f), "a peak with nothing after it is still held under the ceiling");
        }

        [Test]
        public void TheLimiterAppliesALoweredCeilingToADelayedSample()
        {
            DspLimiter limiter = new()
            {
                Ceiling = 1f,
                LookaheadSeconds = 1f / SampleRate,
                ReleaseSeconds = 0.05f,
            };
            Prepare(limiter);
            float[] loud = new float[1];
            loud[0] = 1f;
            _ = Render(limiter, loud);

            limiter.Ceiling = 0.5f;
            float[] output = Render(limiter, new float[1]);

            Assert.That(output[0], Is.EqualTo(0.5f).Within(0.001f), "the frame that leaves is measured against the ceiling of its own block");
        }

        [Test]
        public void TheLimiterKeepsOneGainAcrossTheLookahead()
        {
            DspLimiter limiter = new()
            {
                Ceiling = 0.5f,
                LookaheadSeconds = 0.005f,
                ReleaseSeconds = 0f,
            };
            float[] source = Sine(200f, 1f, 9_600);
            limiter.TryPrepare(SampleRate, 1, 1);

            // One frame at a time, so the gain is read while the audio runs rather than after it has finished.
            float smallestReduction = float.MaxValue;
            float largestReduction = 0f;
            float peak = 0f;
            float[] frame = new float[1];
            for (int index = 0; index < source.Length; index++)
            {
                frame[0] = source[index];
                limiter.Process(frame, 0, 1, 1);
                peak = Math.Max(peak, Math.Abs(frame[0]));
                if (4_800 < index)
                {
                    float reduction = -limiter.GainReductionDecibels;
                    smallestReduction = Math.Min(smallestReduction, reduction);
                    largestReduction = Math.Max(largestReduction, reduction);
                }
            }

            Assert.That(smallestReduction, Is.EqualTo(6.0206f).Within(0.01f), "the gain sits at the ceiling reduction");
            Assert.That(largestReduction - smallestReduction, Is.LessThan(0.01f), "the gain does not move while a peak is in flight");
            Assert.That(peak, Is.LessThanOrEqualTo(0.5001f));
        }

        [Test]
        public void ASustainedPassageDoesNotExtendTheHold()
        {
            DspLimiter limiter = new()
            {
                Ceiling = 0.5f,
                LookaheadSeconds = 1f,
                ReleaseSeconds = 0f,
            };

            Prepare(limiter);
            _ = Render(limiter, Constant(1f, SampleRate));
            float[] output = Render(limiter, Constant(0.1f, SampleRate + 2));

            Assert.That(output[output.Length - 1], Is.EqualTo(0.1f).Within(0.0001f), "the hold ends with the passage that caused it");
            Assert.That(limiter.GainReductionDecibels, Is.Zero.Within(0.01f));
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
        public void APeakOnItsWayOutDoesNotRenewTheHold()
        {
            DspLimiter limiter = new()
            {
                Ceiling = 0.5f,
                LookaheadSeconds = 1f,
                ReleaseSeconds = 0f,
            };
            float[] peak = new float[1];
            peak[0] = 1f;

            Prepare(limiter);
            _ = Render(limiter, peak);
            // A frame or two past the lookahead, so the impulse has left and the tail is heard after it.
            float[] tail = Constant(0.1f, SampleRate + 2);
            float[] output = Render(limiter, tail);

            Assert.That(output[output.Length - 1], Is.EqualTo(0.1f).Within(0.0001f), "the hold ends with the peak that caused it");
            Assert.That(limiter.GainReductionDecibels, Is.Zero.Within(0.01f), "a zero release lets go at once");
            Assert.That(Peak(output, 0), Is.LessThanOrEqualTo(0.5f), "the peak itself still leaves under the ceiling");
        }

        [Test]
        public void TheLimiterReleaseTakesTheTimeItsNameSays()
        {
            DspLimiter limiter = new()
            {
                Ceiling = 0.5f,
                LookaheadSeconds = 0f,
                ReleaseSeconds = 0.05f,
            };
            float[] peak = new float[1];
            peak[0] = 1f;

            Prepare(limiter);
            _ = Render(limiter, peak);
            Assert.That(limiter.GainReductionDecibels, Is.EqualTo(-6.0206f).Within(0.001f), "the peak sets the gain to half");

            _ = Render(limiter, new float[2_400]);

            // One time constant leaves 1 - 0.5 / e: 0.816 of unity, which is -1.77 dB.
            Assert.That(limiter.GainReductionDecibels, Is.EqualTo(-1.7660f).Within(0.1f));
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
        public void TheCompressorLeavesASignalBelowADeepThresholdAlone()
        {
            DspCompressor compressor = new() { ThresholdDecibels = -90f, Ratio = 4f };
            float[] source = Constant(0.00001f, 4_800);

            Prepare(compressor);
            float[] output = Render(compressor, source);

            Assert.That(compressor.GainReductionDecibels, Is.Zero.Within(0.001f), "-100 dBFS is under a -90 dB threshold");
            Assert.That(output[output.Length - 1], Is.EqualTo(0.00001f).Within(0.000001f));
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
            Assert.That(output[output.Length - 1], Is.EqualTo(AudioMath.DecibelsToLinear(-15f)).Within(0.01f), "twenty attack times finish the reduction");
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
            DspCompressor compressor = new()
            {
                ThresholdDecibels = -20f,
                Ratio = 4f,
                AttackSeconds = 0.001f,
                MakeupDecibels = 6f,
            };
            float[] source = Constant(1f, SampleRate);

            Prepare(compressor);
            float[] output = Render(compressor, source);

            // Makeup lifts the reduced signal, and it is not in the detector: 0.1778 of the input, then a 6 dB lift.
            float expected = AudioMath.DecibelsToLinear(-15f) * AudioMath.DecibelsToLinear(6f);
            Assert.That(output[output.Length - 1], Is.EqualTo(expected).Within(expected * 0.05f));
            Assert.That(compressor.GainReductionDecibels, Is.EqualTo(-15f).Within(0.5f), "makeup does not change the reduction");
        }

        [Test]
        public void TheCompressorClampsItsMakeupInsteadOfMuting()
        {
            DspCompressor compressor = new() { MakeupDecibels = -80f };

            Assert.That(compressor.MakeupDecibels, Is.EqualTo(-60f), "a gain stage never turns the signal off");
        }

        [Test]
        public void TheCompressorAttackTakesTheTimeItsNameSays()
        {
            DspCompressor compressor = new()
            {
                ThresholdDecibels = -20f,
                Ratio = 4f,
                AttackSeconds = 0.05f,
                ReleaseSeconds = 0.1f,
            };

            Prepare(compressor);
            _ = Render(compressor, Constant(1f, 2_400));

            // Fifteen decibels of target, one attack time constant in: 15 / e of it is left undone, so -9.48 dB.
            Assert.That(compressor.GainReductionDecibels, Is.EqualTo(-9.4828f).Within(0.2f));
        }

        [Test]
        public void TheCompressorReleaseTakesTheTimeItsNameSays()
        {
            DspCompressor compressor = new()
            {
                ThresholdDecibels = -20f,
                Ratio = 4f,
                AttackSeconds = 0.001f,
                ReleaseSeconds = 0.05f,
            };

            Prepare(compressor);
            _ = Render(compressor, Constant(1f, 9_600));
            Assert.That(compressor.GainReductionDecibels, Is.EqualTo(-15f).Within(0.5f), "the curve is reached first");

            _ = Render(compressor, Constant(0f, 2_400));

            // One release time constant back toward unity leaves 15 / e decibels of the reduction: -5.52 dB.
            Assert.That(compressor.GainReductionDecibels, Is.EqualTo(-5.5182f).Within(0.2f));
        }

        [Test]
        public void TheCompressorClampsItsRatio()
        {
            DspCompressor compressor = new() { Ratio = 0.5f };
            Assert.That(compressor.Ratio, Is.EqualTo(1f));

            compressor.ThresholdDecibels = -20f;
            Prepare(compressor);
            float[] output = Render(compressor, Constant(1f, 4_800));

            Assert.That(output[output.Length - 1], Is.EqualTo(1f).Within(0.0001f), "a ratio of one does not touch the signal");
        }

        [Test]
        public void ANonNumberParameterIsRefused()
        {
            DspLimiter limiter = new();
            DspCompressor compressor = new();
            float ceiling = limiter.Ceiling;
            float release = limiter.ReleaseSeconds;
            float threshold = compressor.ThresholdDecibels;
            float ratio = compressor.Ratio;

            limiter.Ceiling = float.NaN;
            limiter.LookaheadSeconds = float.PositiveInfinity;
            limiter.ReleaseSeconds = float.NaN;
            compressor.ThresholdDecibels = float.NaN;
            compressor.Ratio = float.NegativeInfinity;
            compressor.AttackSeconds = float.NaN;
            compressor.MakeupDecibels = float.NaN;

            Assert.That(limiter.Ceiling, Is.EqualTo(ceiling));
            Assert.That(limiter.LookaheadSeconds, Is.EqualTo(0.005f));
            Assert.That(limiter.ReleaseSeconds, Is.EqualTo(release));
            Assert.That(compressor.ThresholdDecibels, Is.EqualTo(threshold));
            Assert.That(compressor.Ratio, Is.EqualTo(ratio));
            Assert.That(compressor.AttackSeconds, Is.EqualTo(0.005f));
            Assert.That(compressor.MakeupDecibels, Is.Zero, "a bad knob keeps the value that was working");

            Prepare(limiter);
            float[] output = Render(limiter, Sine(1_000f, 1f, 1_024));
            Assert.That(Peak(output, 0), Is.LessThanOrEqualTo(1f), "the output stays a number");
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

            limiter.TryPrepare(SampleRate, BlockFrames, 2);
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
            effect.TryPrepare(SampleRate, BlockFrames, 1);
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
