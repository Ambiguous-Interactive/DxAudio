namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using NUnit.Framework;

    public sealed class DspChainTests
    {
        private const int SampleRate = 48_000;

        private static void RunInBlocks(float[] block, int blockFrames)
        {
            DspChain chain = new();
            chain.Add(new DspLimiter());
            chain.Add(new DspCompressor());
            chain.Add(new DspDistortion { Drive = 6f });
            chain.Add(new DspBitCrusher { Bits = 8, RateReduction = 2 });
            chain.Add(new DspPitchShifter { Semitones = 7f });
            chain.TryPrepare(SampleRate, blockFrames, 1);
            for (int offset = 0, blockLength = block.Length; offset < blockLength; offset += blockFrames)
            {
                int count = Math.Min(blockFrames, block.Length - offset);
                chain.Process(block, offset, count, 1);
            }
        }

        private static float[] Run(DspEffect[] effects)
        {
            float[] block = Sine(2_000f, 0.8f, 4_096);
            DspChain chain = new();
            foreach (DspEffect effect in effects)
            {
                chain.Add(effect);
            }

            chain.TryPrepare(SampleRate, 256, 1);
            chain.Process(block, 0, block.Length, 1);
            return block;
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

        private static int DifferingSamples(float[] left, float[] right)
        {
            int differences = 0;
            for (int i = 0, leftLength = left.Length; i < leftLength; ++i)
            {
                if (0.0001f < Math.Abs(left[i] - right[i]))
                {
                    ++differences;
                }
            }

            return differences;
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

        [Test]
        public void AnEmptyChainPassesTheBlockUntouched()
        {
            float[] block = Sine(440f, 0.5f, 128);
            float[] before = (float[])block.Clone();

            DspChain chain = new();
            chain.TryPrepare(SampleRate, block.Length, 1);
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
            chain.Add(new DspBitCrusher { Bits = 1 });
            Assert.That(chain.TryPrepare(SampleRate, 256, 1), Is.True);
            float[] block = Sine(440f, 0.5f, 256);
            float[] before = (float[])block.Clone();

            Assert.That(chain.TryPrepare(sampleRate, maxBlockSize, channels), Is.False);
            chain.Process(block, 0, block.Length, 1);

            Assert.That(block, Is.EqualTo(before), "a refused chain keeps the buffers its stages hold and must not run them");
        }

        [Test]
        public void PrepareAndResetReachEveryStage()
        {
            RecordingEffect first = new();
            RecordingEffect second = new();
            DspChain chain = new();
            chain.Add(first);
            chain.Add(second);

            chain.TryPrepare(SampleRate, 512, 2);
            chain.Reset();

            Assert.That(first.PreparedAs, Is.EqualTo((SampleRate, 512, 2)));
            Assert.That(second.PreparedAs, Is.EqualTo((SampleRate, 512, 2)));
            Assert.That(first.Resets, Is.EqualTo(1));
            Assert.That(second.Resets, Is.EqualTo(1));
        }

        [Test]
        public void AStageMayAddToTheChainFromItsPrepare()
        {
            DspChain chain = new();
            chain.Add(new AddingEffect(chain, new DspBitCrusher()));

            chain.TryPrepare(SampleRate, 256, 1);

            Assert.That(chain.Count, Is.EqualTo(2), "the stage added itself without breaking the pass");
        }

        [Test]
        public void AStageMayAddToTheChainFromItsProcess()
        {
            DspChain chain = new();
            RecordingEffect appended = new();
            AddingEffect adder = new(chain, appended, addWhileProcessing: true);
            chain.Add(adder);
            chain.TryPrepare(SampleRate, 256, 1);
            float[] block = Sine(440f, 0.5f, 256);

            chain.Process(block, 0, block.Length, 1);

            Assert.That(chain.Count, Is.EqualTo(2), "the block completed and the stage joined the chain");
            Assert.That(appended.PreparedAs, Is.EqualTo((SampleRate, 256, 1)), "the appended stage takes the chain's layout");
            Assert.That(appended.Processed, Is.EqualTo(1), "the appended stage runs in the same block");
        }

        [Test]
        public void AStageMayAddToTheChainWhileReportingGainReduction()
        {
            DspChain chain = new();
            chain.Add(new RecordingEffect { ReadingReduction = () => chain.Add(new RecordingEffect { Reduction = 3f }) });

            Assert.That(chain.GainReductionDecibels, Is.EqualTo(3f), "the appended stage contributes to the same reading");
        }

        [Test]
        public void AStageMayAddToTheChainFromItsReset()
        {
            DspChain chain = new();
            RecordingEffect appended = new();
            chain.Add(new RecordingEffect { Resetting = () => chain.Add(appended) });

            chain.Reset();

            Assert.That(appended.Resets, Is.EqualTo(1), "the appended stage resets in the same pass");
        }

        [Test]
        public void LatencyIsTheSumOfItsStages()
        {
            DspLimiter limiter = new() { LookaheadSeconds = 0.005f };
            DspPitchShifter shifter = new() { WindowSeconds = 0.005f };
            DspChain chain = new();
            chain.Add(new DspCompressor());
            chain.Add(limiter);
            chain.Add(new DspBitCrusher());
            chain.Add(shifter);

            chain.TryPrepare(SampleRate, 512, 1);

            Assert.That(limiter.LatencySamples, Is.EqualTo(240));
            Assert.That(shifter.LatencySamples, Is.EqualTo(121));
            Assert.That(chain.LatencySamples, Is.EqualTo(361), "the delay of every stage adds up");
        }

        [Test]
        public void TheChainAddsUpItsStagesReductions()
        {
            DspChain chain = new();
            chain.Add(new DspLimiter { Ceiling = 0.25f, LookaheadSeconds = 0f });
            chain.Add(
                new DspCompressor
                {
                    ThresholdDecibels = -20f,
                    Ratio = 4f,
                    AttackSeconds = 0f,
                    ReleaseSeconds = 0f,
                }
            );
            chain.TryPrepare(SampleRate, 256, 1);
            float[] block = Sine(1_000f, 1f, 256);

            Assert.That(chain.GainReductionDecibels, Is.Zero, "nothing has run yet, so nothing holds the signal down");

            chain.Process(block, 0, block.Length, 1);

            float first = chain[0].GainReductionDecibels;
            float second = chain[1].GainReductionDecibels;
            Assert.That(first, Is.LessThan(-10f), "a quarter ceiling is about 12 dB down");
            Assert.That(second, Is.LessThan(-4f), "the compressor sees a quarter-scale signal over its threshold");
            Assert.That(second, Is.Not.EqualTo(first), "a reading that keeps only one stage cannot pass this");
            Assert.That(chain.GainReductionDecibels, Is.EqualTo(first + second), "stages in series add their reductions");
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
            chain.Add(new DspPitchShifter());

            chain.Process(block, 0, block.Length, 1);

            Assert.That(block, Is.EqualTo(before), "a stage that was never prepared must not touch the block");
        }

        [Test]
        public void ABlockWithTheWrongChannelCountIsRefused()
        {
            float[] block = Sine(440f, 0.5f, 128);
            float[] before = (float[])block.Clone();
            DspBitCrusher crusher = new();
            crusher.TryPrepare(SampleRate, 128, 2);

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
        public void EveryEffectRefusesAZeroRateOrAChannelCount()
        {
            DspEffect[] effects = { new DspLimiter(), new DspCompressor(), new DspDistortion(), new DspBitCrusher(), new DspPitchShifter() };

            foreach (DspEffect effect in effects)
            {
                Assert.That(effect.TryPrepare(0, 256, 2), Is.False, effect.DisplayName);
                Assert.That(effect.TryPrepare(SampleRate, 256, 0), Is.False, effect.DisplayName);
                Assert.That(effect.TryPrepare(SampleRate, 0, 2), Is.False, effect.DisplayName);
            }
        }

        [Test]
        public void AStageAddedAfterPrepareRunsAndCountsItsLatency()
        {
            DspChain chain = new();
            chain.Add(new DspCompressor());
            chain.TryPrepare(SampleRate, 256, 1);

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
            prepared.TryPrepare(SampleRate, 8, 1);
            float[] block = new float[8];

            unprepared.Process(block, 0, block.Length, channels);
            prepared.Process(block, int.MaxValue, 10, 1);

            Assert.That(block, Is.All.Zero, "a nonsensical range is refused, not indexed or divided by");
        }

        [Test]
        public void APartialFrameIsRefused()
        {
            DspLimiter limiter = new() { Ceiling = 0.5f };
            limiter.TryPrepare(SampleRate, 16, 2);
            float[] block = new float[10];
            block[8] = 1f;
            block[9] = 1f;

            limiter.Process(block, 0, 9, 2);

            Assert.That(block[8], Is.EqualTo(1f), "an odd sample count is not a whole stereo frame, so the block is refused");
        }

        private sealed class AddingEffect : DspEffect
        {
            public override string DisplayName => "Adding";
            private readonly DspChain _chain;
            private readonly DspEffect _stage;
            private readonly bool _addWhileProcessing;
            private bool _added;

            public AddingEffect(DspChain chain, DspEffect stage, bool addWhileProcessing = false)
            {
                _chain = chain;
                _stage = stage;
                _addWhileProcessing = addWhileProcessing;
            }

            public override void Process(float[] buffer, int offset, int count, int channels)
            {
                if (_addWhileProcessing && !_added)
                {
                    _added = true;
                    _chain.Add(_stage);
                }
            }

            protected override void PrepareCore(int sampleRate, int maxBlockSize, int channels)
            {
                if (!_addWhileProcessing && !_added)
                {
                    _added = true;
                    _chain.Add(_stage);
                }
            }
        }

        private sealed class RecordingEffect : DspEffect
        {
            public override string DisplayName => "Recording";

            public (int sampleRate, int maxBlockSize, int channels) PreparedAs { get; private set; }

            public int Resets { get; private set; }

            public int Processed { get; private set; }

            public Action ReadingReduction { get; set; }

            public Action Resetting { get; set; }

            public float Reduction { get; set; }

            public override float GainReductionDecibels
            {
                get
                {
                    ReadingReduction?.Invoke();
                    return Reduction;
                }
            }

            public override void Process(float[] buffer, int offset, int count, int channels)
            {
                ++Processed;
            }

            public override void Reset()
            {
                ++Resets;
                Resetting?.Invoke();
            }

            protected override void PrepareCore(int sampleRate, int maxBlockSize, int channels)
            {
                PreparedAs = (sampleRate, maxBlockSize, channels);
            }
        }
    }
}
