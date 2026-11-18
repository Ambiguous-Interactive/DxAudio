namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using NUnit.Framework;

    public sealed class DspGraphTests
    {
        private const int SampleRate = 48_000;

        [Test]
        public void AFanOutFeedsEveryBranch()
        {
            DspGraph graph = new();
            _ = graph.TryAddNode(new DspChain(), out int input);
            _ = graph.TryAddNode(ChainOf(new DelayEffect(3) { Gain = 0.25f }), out int quiet);
            _ = graph.TryAddNode(ChainOf(new DelayEffect(7) { Gain = 0.75f }), out int loud);
            _ = graph.TryAddNode(new DspChain(), out int output);
            Assert.That(graph.TryAddEdge(input, quiet), Is.True);
            Assert.That(graph.TryAddEdge(input, loud), Is.True);
            Assert.That(graph.TryAddEdge(quiet, output), Is.True);
            Assert.That(graph.TryAddEdge(loud, output), Is.True);
            graph.TryPrepare(SampleRate, 64, 1, input, output);

            float[] block = Impulse(64, 1, 0, 1f);
            graph.Process(block, 0, block.Length, 1);

            Assert.That(block[7], Is.EqualTo(1f).Within(1e-6f), "both branches arrive together and sum");
            Assert.That(NonZeroCount(block), Is.EqualTo(1), "neither branch arrives anywhere else");
        }

        [Test]
        public void ANodeFansOutAgainAfterAFanIn()
        {
            DspGraph graph = new();
            _ = graph.TryAddNode(new DspChain(), out int input);
            _ = graph.TryAddNode(ChainOf(new DelayEffect(0) { Gain = 2f }), out int first);
            _ = graph.TryAddNode(ChainOf(new DelayEffect(0) { Gain = 3f }), out int second);
            _ = graph.TryAddNode(ChainOf(new DelayEffect(0) { Gain = 5f }), out int third);
            _ = graph.TryAddNode(new DspChain(), out int output);
            Assert.That(graph.TryAddEdge(input, first), Is.True);
            Assert.That(graph.TryAddEdge(first, second), Is.True);
            Assert.That(graph.TryAddEdge(first, third), Is.True);
            Assert.That(graph.TryAddEdge(second, output), Is.True);
            Assert.That(graph.TryAddEdge(third, output), Is.True);
            graph.TryPrepare(SampleRate, 64, 1, input, output);

            float[] block = Impulse(64, 1, 0, 1f);
            graph.Process(block, 0, block.Length, 1);

            Assert.That(block[0], Is.EqualTo(16f).Within(1e-5f), "one branch feeds two, and both of those sum");
        }

        [TestCase(3, 7, 7, TestName = "The later branch sets the delay")]
        [TestCase(300, 100, 300, TestName = "The order nodes were added in does not set the delay")]
        public void TheLongestBranchSetsTheReportedLatency(int firstDelay, int secondDelay, int expected)
        {
            DspGraph graph = FanIn(ChainOf(new DelayEffect(firstDelay)), ChainOf(new DelayEffect(secondDelay)));
            graph.TryPrepare(SampleRate, 64, 1, 0, 3);

            Assert.That(graph.LatencySamples, Is.EqualTo(expected));
        }

        [Test]
        public void AShortBranchWaitsForTheLongOne()
        {
            DspGraph graph = FanIn(ChainOf(new DelayEffect(100)), ChainOf(new DelayEffect(300)));
            graph.TryPrepare(SampleRate, 512, 1, 0, 3);

            float[] block = Impulse(512, 1, 0, 0.5f);
            graph.Process(block, 0, block.Length, 1);

            Assert.That(block[300], Is.EqualTo(1f).Within(1e-5f), "the shorter branch is delayed to the longer one");
            Assert.That(NonZeroCount(block), Is.EqualTo(1), "no branch arrives early");
        }

        [Test]
        public void AShifterBranchAndALimiterBranchStayAligned()
        {
            DspPitchShifter shifter = new() { Semitones = 0f };
            DspLimiter limiter = new();
            DspGraph graph = FanIn(ChainOf(shifter), ChainOf(limiter));
            graph.TryPrepare(SampleRate, 2_048, 1, 0, 3);
            int shifterLatency = shifter.LatencySamples;

            float[] block = Impulse(2_048, 1, 0, 0.25f);
            graph.Process(block, 0, block.Length, 1);

            Assert.That(graph.LatencySamples, Is.EqualTo(shifterLatency));
            Assert.That(graph.PreparedBytes, Is.EqualTo(44_804), "four node buffers plus the 961-frame line the limiter branch waits on, in mono");
            Assert.That(limiter.LatencySamples, Is.LessThan(shifterLatency), "the branches report different delays");
            Assert.That(block[shifterLatency], Is.EqualTo(0.5f).Within(1e-5f), "both arrive at the same frame, so they sum without a comb");
            Assert.That(NonZeroCount(block), Is.EqualTo(1));
        }

        [Test]
        public void ACycleIsRefusedAtTheEdgeThatClosesIt()
        {
            DspGraph graph = new();
            _ = graph.TryAddNode(new DspChain(), out int first);
            _ = graph.TryAddNode(new DspChain(), out int second);
            _ = graph.TryAddNode(new DspChain(), out int third);

            Assert.That(graph.TryAddEdge(first, second), Is.True);
            Assert.That(graph.TryAddEdge(second, third), Is.True);
            Assert.That(graph.TryAddEdge(third, first), Is.False, "the edge would close a cycle");
            Assert.That(graph.TryAddEdge(second, first), Is.False, "a shorter cycle is refused the same way");
            Assert.That(graph.TryAddEdge(first, first), Is.False, "a node joined to itself is a cycle");
            Assert.That(graph.TryAddEdge(first, second), Is.False, "the one-directional pair is held once");
            Assert.That(graph.TryAddEdge(first, third), Is.True, "a forward edge across two nodes is not a cycle");
            Assert.That(graph.EdgeCount, Is.EqualTo(3));
        }

        [Test]
        public void ANodeOffThePathIsRefused()
        {
            DspGraph stranded = new();
            _ = stranded.TryAddNode(new DspChain(), out int input);
            _ = stranded.TryAddNode(new DspChain(), out int middle);
            _ = stranded.TryAddNode(new DspChain(), out int output);
            _ = stranded.TryAddNode(new DspChain(), out _);
            _ = stranded.TryAddEdge(input, middle);
            _ = stranded.TryAddEdge(middle, output);

            Assert.That(stranded.TryPrepare(SampleRate, 128, 1, input, output), Is.False, "a fourth node reaches nothing");

            _ = stranded.TryAddEdge(middle, 3);
            Assert.That(stranded.TryPrepare(SampleRate, 128, 1, input, 3), Is.False, "the middle node now reaches the output, and the old one is stranded");
        }

        [TestCase(-1, 1, TestName = "A node index below the graph is refused")]
        [TestCase(0, 4, TestName = "A node index past the graph is refused")]
        public void ANamedNodeHasToExist(int input, int output)
        {
            DspGraph graph = FanIn(ChainOf(new DelayEffect(0)), ChainOf(new DelayEffect(0)));

            Assert.That(graph.TryPrepare(SampleRate, 128, 2, input, output), Is.False);
            Assert.That(graph.IsPrepared, Is.False);
        }

        [Test]
        public void AnOversizedGraphIsRefusedBeforeItAllocates()
        {
            DspGraph graph = FanIn(ChainOf(new LatencyClaimEffect(400_000)), ChainOf(new DelayEffect(0)));

            Assert.That(graph.TryPrepare(SampleRate, 256, 2, 0, 3), Is.False);
            Assert.That(graph.IsPrepared, Is.False);
            Assert.That(graph.PreparedBytes, Is.Zero);

            float[] block = Impulse(512, 2, 0, 1f);
            float[] before = (float[])block.Clone();
            graph.Process(block, 0, block.Length, 2);
            Assert.That(block, Is.EqualTo(before), "a refused graph passes the block through");
        }

        [TestCase(0, 256, 2, TestName = "A zero sample rate is refused")]
        [TestCase(48_000, 0, 2, TestName = "A zero block size is refused")]
        [TestCase(48_000, 256, 0, TestName = "A zero channel count is refused")]
        public void PrepareRefusesAnImpossibleLayout(int sampleRate, int maxBlockSize, int channels)
        {
            DspGraph graph = FanIn(ChainOf(new DelayEffect(0)), ChainOf(new DelayEffect(0)));

            Assert.That(graph.TryPrepare(sampleRate, maxBlockSize, channels, 0, 3), Is.False);
        }

        [Test]
        public void AnUnpreparedGraphPassesTheBlockThrough()
        {
            DspGraph graph = new();
            _ = graph.TryAddNode(ChainOf(new DelayEffect(0) { Gain = 0f }), out int input);

            float[] block = Impulse(256, 1, 0, 1f);
            float[] before = (float[])block.Clone();
            graph.Process(block, 0, block.Length, 1);

            Assert.That(block, Is.EqualTo(before));
        }

        [Test]
        public void ABlockOfAnotherShapePassesThrough()
        {
            DspGraph graph = FanIn(ChainOf(new DelayEffect(0) { Gain = 0f }), ChainOf(new DelayEffect(0) { Gain = 0f }));
            graph.TryPrepare(SampleRate, 256, 2, 0, 3);

            float[] block = Impulse(256, 1, 0, 1f);
            float[] before = (float[])block.Clone();
            graph.Process(block, 0, block.Length, 1);

            Assert.That(block, Is.EqualTo(before), "a mono block is not the prepared stereo shape");
        }

        [Test]
        public void ResetClearsEveryBranchAndLine()
        {
            DspGraph graph = FanIn(ChainOf(new DelayEffect(100)), ChainOf(new DelayEffect(300)));
            graph.TryPrepare(SampleRate, 512, 1, 0, 3);
            float[] block = Impulse(512, 1, 0, 1f);
            graph.Process(block, 0, block.Length, 1);
            graph.Reset();

            float[] silence = new float[512];
            graph.Process(silence, 0, silence.Length, 1);

            Assert.That(NonZeroCount(silence), Is.Zero, "the delay lines hold nothing from the last block");
        }

        [Test]
        public void PreparationReservesOneBufferPerNodeAndOneLinePerDelayedEdge()
        {
            DspGraph graph = FanIn(ChainOf(new DelayEffect(100)), ChainOf(new DelayEffect(300)));
            graph.TryPrepare(SampleRate, 256, 2, 0, 3);

            // Four node buffers of 256 stereo frames, plus the 200-frame edge line the short branch needs.
            int expected = ((4 * 256 * 2) + ((200 + 256) * 2)) * sizeof(float);
            Assert.That(graph.PreparedBytes, Is.EqualTo(expected));
            Assert.That(graph.PreparedBytes, Is.LessThan(DspGraph.MaximumPreparedBytes));
        }

        [Test]
        public void AddingANodeAfterPrepareDropsThePreparedGraph()
        {
            DspGraph graph = FanIn(ChainOf(new DelayEffect(0) { Gain = 0f }), ChainOf(new DelayEffect(0) { Gain = 0f }));
            graph.TryPrepare(SampleRate, 256, 1, 0, 3);

            _ = graph.TryAddNode(new DspChain(), out _);

            Assert.That(graph.IsPrepared, Is.False);
            Assert.That(graph.LatencySamples, Is.Zero);
        }

        [Test]
        public void TheNodeCountIsBounded()
        {
            DspGraph graph = new();
            for (int node = 0; node < DspGraph.MaximumNodes; node++)
            {
                Assert.That(graph.TryAddNode(new DspChain(), out _), Is.True);
            }

            Assert.That(graph.TryAddNode(new DspChain(), out int extra), Is.False);
            Assert.That(extra, Is.EqualTo(-1));
            Assert.That(graph.NodeCount, Is.EqualTo(DspGraph.MaximumNodes));
            Assert.That(graph.TryAddNode(null, out _), Is.False, "a null chain is not a node");
        }

        /// <summary>A fan-out into two branches that fan back in to one output: nodes 0, 1, 2, then the output at 3.</summary>
        private static DspGraph FanIn(DspChain first, DspChain second)
        {
            DspGraph graph = new();
            _ = graph.TryAddNode(new DspChain(), out int input);
            _ = graph.TryAddNode(first, out int left);
            _ = graph.TryAddNode(second, out int right);
            _ = graph.TryAddNode(new DspChain(), out int output);
            Assert.That(graph.TryAddEdge(input, left), Is.True);
            Assert.That(graph.TryAddEdge(input, right), Is.True);
            Assert.That(graph.TryAddEdge(left, output), Is.True);
            Assert.That(graph.TryAddEdge(right, output), Is.True);
            return graph;
        }

        private static DspChain ChainOf(DspEffect effect)
        {
            DspChain chain = new();
            chain.Add(effect);
            return chain;
        }

        private static float[] Impulse(int frames, int channels, int frame, float value)
        {
            float[] block = new float[frames * channels];
            for (int channel = 0; channel < channels; channel++)
            {
                block[(frame * channels) + channel] = value;
            }

            return block;
        }

        private static int NonZeroCount(float[] block)
        {
            int count = 0;
            for (int sample = 0; sample < block.Length; sample++)
            {
                if (block[sample] != 0f)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Delays the signal by a whole number of frames, so a branch arrives late for real.</summary>
        private sealed class DelayEffect : DspEffect
        {
            private readonly int _frames;
            private float[] _line = Array.Empty<float>();
            private int _position;

            public DelayEffect(int frames)
            {
                _frames = frames;
            }

            public float Gain { get; set; } = 1f;

            public override string DisplayName => "Delay";

            public override int LatencySamples => _frames;

            public override void Process(float[] buffer, int offset, int count, int channels)
            {
                if (_frames == 0)
                {
                    for (int sample = 0; sample < count; sample++)
                    {
                        buffer[offset + sample] *= Gain;
                    }

                    return;
                }

                int capacity = _line.Length;
                for (int sample = 0; sample < count; sample++)
                {
                    float held = _line[_position];
                    _line[_position] = buffer[offset + sample];
                    buffer[offset + sample] = held * Gain;
                    _position++;
                    if (_position == capacity)
                    {
                        _position = 0;
                    }
                }
            }

            public override void Reset()
            {
                if (_line != null)
                {
                    Array.Clear(_line, 0, _line.Length);
                }

                _position = 0;
            }

            protected override void PrepareCore(int sampleRate, int maxBlockSize, int channels)
            {
                _line = 0 < _frames ? new float[_frames * channels] : Array.Empty<float>();
                _position = 0;
            }
        }

        /// <summary>Reports a delay it does not hold, so a graph can be made to reserve more than it may.</summary>
        private sealed class LatencyClaimEffect : DspEffect
        {
            private readonly int _frames;

            public LatencyClaimEffect(int frames)
            {
                _frames = frames;
            }

            public override string DisplayName => "Claim";

            public override int LatencySamples => _frames;

            public override void Process(float[] buffer, int offset, int count, int channels) { }

            protected override void PrepareCore(int sampleRate, int maxBlockSize, int channels) { }
        }
    }
}
