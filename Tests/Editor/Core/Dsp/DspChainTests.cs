namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using NUnit.Framework;

    public sealed class DspChainTests
    {
        private const int SampleRate = 48_000;

        [Test]
        public void AnEmptyChainPassesTheBlockUntouched()
        {
            float[] block = Sine(440f, 0.5f, 128);
            float[] before = (float[])block.Clone();

            DspChain chain = new();
            chain.Prepare(SampleRate, block.Length, 1);
            chain.Process(block, 0, block.Length, 1);

            Assert.That(chain.Count, Is.Zero);
            Assert.That(chain.LatencySamples, Is.Zero);
            Assert.That(block, Is.EqualTo(before));
        }

        [Test]
        public void TheChainRunsEffectsInTheOrderItHoldsThem()
        {
            DspDistortion distortion = new() { Drive = 8f };
            DspBitCrusher crusher = new() { Bits = 3 };

            float[] shapeThenCrush = Run(new DspEffect[] { distortion, crusher });
            float[] crushThenShape = Run(new DspEffect[] { crusher, distortion });

            Assert.That(DifferingSamples(shapeThenCrush, crushThenShape), Is.GreaterThan(100), "an order swap has to change the sound");
        }

        [Test]
        public void AddingAStageAppendsItAndRemovingTakesItOut()
        {
            DspCompressor compressor = new();
            DspLimiter limiter = new();
            DspChain chain = new();

            chain.Add(compressor);
            chain.Add(limiter);

            Assert.That(chain.Count, Is.EqualTo(2));
            Assert.That(chain[0], Is.SameAs(compressor));
            Assert.That(chain[1], Is.SameAs(limiter));
            Assert.That(chain.Remove(compressor), Is.True);
            Assert.That(chain.Count, Is.EqualTo(1));
            Assert.That(chain[0], Is.SameAs(limiter), "the rest keeps its order");
            Assert.That(chain.Remove(compressor), Is.False, "a second removal finds nothing");
            Assert.That(chain.Remove(null), Is.False, "a null stage is not held");
            chain.Clear();
            Assert.That(chain.Count, Is.Zero);
        }

        [Test]
        public void AddRefusesANullStage()
        {
            DspChain chain = new();

            _ = Assert.Throws<ArgumentNullException>(() => chain.Add(null));
        }

        [TestCase(0, 256, 2, TestName = "A zero sample rate is refused")]
        [TestCase(48_000, 0, 2, TestName = "A zero block size is refused")]
        [TestCase(48_000, 256, 0, TestName = "A zero channel count is refused")]
        public void PrepareRefusesAnImpossibleLayout(int sampleRate, int maxBlockSize, int channels)
        {
            DspChain chain = new();
            chain.Add(new DspLimiter());

            _ = Assert.Throws<ArgumentOutOfRangeException>(() => chain.Prepare(sampleRate, maxBlockSize, channels));
        }

        [Test]
        public void PrepareAndResetReachEveryStage()
        {
            RecordingEffect first = new();
            RecordingEffect second = new();
            DspChain chain = new();
            chain.Add(first);
            chain.Add(second);

            chain.Prepare(SampleRate, 512, 2);
            chain.Reset();

            Assert.That(first.PreparedAs, Is.EqualTo((SampleRate, 512, 2)));
            Assert.That(second.PreparedAs, Is.EqualTo((SampleRate, 512, 2)));
            Assert.That(first.Resets, Is.EqualTo(1));
            Assert.That(second.Resets, Is.EqualTo(1));
        }

        [Test]
        public void LatencyIsTheSumOfItsStages()
        {
            DspLimiter limiter = new() { LookaheadSeconds = 0.005f };
            DspChain chain = new();
            chain.Add(new DspCompressor());
            chain.Add(limiter);
            chain.Add(new DspBitCrusher());

            chain.Prepare(SampleRate, 512, 1);

            Assert.That(limiter.LatencySamples, Is.EqualTo(240));
            Assert.That(chain.LatencySamples, Is.EqualTo(240), "only the limiter delays the signal");
        }

        [Test]
        public void AnUnpreparedChainPassesTheBlockUntouched()
        {
            float[] block = Sine(440f, 0.5f, 128);
            float[] before = (float[])block.Clone();
            DspChain chain = new();
            chain.Add(new DspLimiter());
            chain.Add(new DspCompressor());
            chain.Add(new DspDistortion());
            chain.Add(new DspBitCrusher());

            chain.Process(block, 0, block.Length, 1);

            Assert.That(block, Is.EqualTo(before), "a stage that was never prepared must not touch the block");
        }

        [Test]
        public void ABlockWithTheWrongChannelCountIsRefused()
        {
            float[] block = Sine(440f, 0.5f, 128);
            float[] before = (float[])block.Clone();
            DspBitCrusher crusher = new();
            crusher.Prepare(SampleRate, 128, 2);

            crusher.Process(block, 0, block.Length, 1);

            Assert.That(block, Is.EqualTo(before), "a mono block under a stereo layout is not the prepared shape");
        }

        [Test]
        public void AChainSoundsTheSameAtAnyBlockSize()
        {
            float[] fine = Sine(1_000f, 1f, 8_192);
            float[] coarse = (float[])fine.Clone();

            RunInBlocks(fine, 64);
            RunInBlocks(coarse, 1_024);

            Assert.That(coarse, Is.EqualTo(fine), "a block boundary must not change what the effects do");
        }

        [Test]
        public void EveryEffectRefusesAZeroRateOrChannelCount()
        {
            DspEffect[] effects = { new DspLimiter(), new DspCompressor(), new DspDistortion(), new DspBitCrusher() };

            for (int i = 0; i < effects.Length; i++)
            {
                DspEffect effect = effects[i];
                _ = Assert.Throws<ArgumentOutOfRangeException>(() => effect.Prepare(0, 256, 2), effect.DisplayName);
                _ = Assert.Throws<ArgumentOutOfRangeException>(() => effect.Prepare(SampleRate, 256, 0), effect.DisplayName);
            }
        }

        [Test]
        public void AStageAddedAfterPrepareRunsAndCountsItsLatency()
        {
            DspChain chain = new();
            chain.Add(new DspCompressor());
            chain.Prepare(SampleRate, 256, 1);

            DspLimiter limiter = new() { LookaheadSeconds = 0.005f, Ceiling = 0.5f };
            chain.Add(limiter);

            Assert.That(chain.LatencySamples, Is.EqualTo(240), "a late stage still reports the delay it adds");

            float[] block = Sine(1_000f, 1f, 1_024);
            chain.Process(block, 0, block.Length, 1);

            Assert.That(Peak(block), Is.LessThanOrEqualTo(0.5001f), "the late stage is prepared, so it actually runs");
        }

        [TestCase(0, TestName = "Zero channels is refused")]
        [TestCase(-2, TestName = "Negative channels is refused")]
        public void ABlockOfAnImpossibleShapeIsRefused(int channels)
        {
            DspLimiter unprepared = new();
            DspLimiter prepared = new();
            prepared.Prepare(SampleRate, 8, 1);
            float[] block = new float[8];

            unprepared.Process(block, 0, block.Length, channels);
            prepared.Process(block, int.MaxValue, 10, 1);

            Assert.That(block, Is.All.Zero, "a nonsensical range is refused, not indexed or divided by");
        }

        [Test]
        public void APartialFrameIsRefused()
        {
            DspLimiter limiter = new() { Ceiling = 0.5f };
            limiter.Prepare(SampleRate, 16, 2);
            float[] block = new float[10];
            block[8] = 1f;
            block[9] = 1f;

            limiter.Process(block, 0, 9, 2);

            Assert.That(block[8], Is.EqualTo(1f), "an odd sample count is not a whole stereo frame, so the block is refused");
        }

        private static void RunInBlocks(float[] block, int blockFrames)
        {
            DspChain chain = new();
            chain.Add(new DspLimiter());
            chain.Add(new DspCompressor());
            chain.Add(new DspDistortion { Drive = 6f });
            chain.Add(new DspBitCrusher { Bits = 8, RateReduction = 2 });
            chain.Prepare(SampleRate, blockFrames, 1);
            for (int offset = 0; offset < block.Length; offset += blockFrames)
            {
                int count = Math.Min(blockFrames, block.Length - offset);
                chain.Process(block, offset, count, 1);
            }
        }

        private static float[] Run(DspEffect[] effects)
        {
            float[] block = Sine(2_000f, 0.8f, 4_096);
            DspChain chain = new();
            for (int i = 0; i < effects.Length; i++)
            {
                chain.Add(effects[i]);
            }

            chain.Prepare(SampleRate, 256, 1);
            chain.Process(block, 0, block.Length, 1);
            return block;
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

        private static int DifferingSamples(float[] left, float[] right)
        {
            int differences = 0;
            for (int i = 0; i < left.Length; i++)
            {
                if (0.0001f < Math.Abs(left[i] - right[i]))
                {
                    differences++;
                }
            }

            return differences;
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

        private sealed class RecordingEffect : DspEffect
        {
            public override string DisplayName => "Recording";

            public (int sampleRate, int maxBlockSize, int channels) PreparedAs { get; private set; }

            public int Resets { get; private set; }

            public override void Prepare(int sampleRate, int maxBlockSize, int channels)
            {
                PreparedAs = (sampleRate, maxBlockSize, channels);
            }

            public override void Process(float[] buffer, int offset, int count, int channels) { }

            public override void Reset()
            {
                Resets++;
            }
        }
    }
}
