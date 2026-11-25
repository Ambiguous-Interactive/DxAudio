namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using NUnit.Framework;

    public sealed class DspGraphTests
    {
        private const int SampleRate = 48_000;

        /// <summary>A fan-out into two branches that fan back in to one output: nodes 0, 1, 2, then the output at 3.</summary>
        private static DspGraph FanIn(DspChain first, DspChain second)
        {
            DspGraph graph = new();
            int input = AddNode(graph, new DspChain());
            int left = AddNode(graph, first);
            int right = AddNode(graph, second);
            int output = AddNode(graph, new DspChain());
            Assert.That(graph.TryAddEdge(input, left), Is.True);
            Assert.That(graph.TryAddEdge(input, right), Is.True);
            Assert.That(graph.TryAddEdge(left, output), Is.True);
            Assert.That(graph.TryAddEdge(right, output), Is.True);
            return graph;
        }

        private static int AddNode(DspGraph graph, DspChain chain)
        {
            if (!graph.TryAddNode(chain, out int index))
            {
                throw new AssertionException("The graph fixture could not add a node.");
            }

            return index;
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
            for (int channel = 0; channel < channels; ++channel)
            {
                block[(frame * channels) + channel] = value;
            }

            return block;
        }

        private static int NonZeroCount(float[] block)
        {
            int count = 0;
            foreach (float sample in block)
            {
                if (sample != 0f)
                {
                    ++count;
                }
            }

            return count;
        }

        [Test]
        public void AFanOutFeedsEveryBranch()
        {
            DspGraph graph = new();
            int input = AddNode(graph, new DspChain());
            int quiet = AddNode(graph, ChainOf(new DelayEffect(3) { Gain = 0.25f }));
            int loud = AddNode(graph, ChainOf(new DelayEffect(7) { Gain = 0.75f }));
            int output = AddNode(graph, new DspChain());
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
            int input = AddNode(graph, new DspChain());
            int first = AddNode(graph, ChainOf(new DelayEffect(0) { Gain = 2f }));
            int second = AddNode(graph, ChainOf(new DelayEffect(0) { Gain = 3f }));
            int third = AddNode(graph, ChainOf(new DelayEffect(0) { Gain = 5f }));
            int output = AddNode(graph, new DspChain());
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

        [Test]
        public void SoloKeepsAllPathsThroughTheSelectedNodeAndDropsShortcuts()
        {
            DspGraph graph = new();
            int output = AddNode(graph, new DspChain());
            int side = AddNode(graph, ChainOf(new DelayEffect(20)));
            int upstream = AddNode(graph, ChainOf(new DelayEffect(1) { Gain = 2f }));
            int solo = AddNode(graph, ChainOf(new DelayEffect(2) { Gain = 3f }));
            int input = AddNode(graph, new DspChain());
            int downstream = AddNode(graph, ChainOf(new DelayEffect(4) { Gain = 5f }));
            Assert.That(graph.TryAddEdge(input, upstream), Is.True);
            Assert.That(graph.TryAddEdge(input, solo), Is.True);
            Assert.That(graph.TryAddEdge(upstream, solo), Is.True);
            Assert.That(graph.TryAddEdge(solo, downstream), Is.True);
            Assert.That(graph.TryAddEdge(solo, output), Is.True);
            Assert.That(graph.TryAddEdge(downstream, output), Is.True);
            Assert.That(graph.TryAddEdge(upstream, downstream), Is.True, "the shortcut joins two nodes on the solo path");
            Assert.That(graph.TryAddEdge(input, side), Is.True);
            Assert.That(graph.TryAddEdge(side, output), Is.True);

            Assert.That(graph.TrySolo(input, output, solo, out int soloInput, out int soloOutput), Is.True);
            Assert.That(graph.TryPrepare(SampleRate, 64, 1, soloInput, soloOutput), Is.True);
            float[] block = Impulse(64, 1, 0, 1f);
            graph.Process(block, 0, block.Length, 1);

            Assert.That(graph.LatencySamples, Is.EqualTo(7));
            Assert.That(block[7], Is.EqualTo(54f), "both inputs reach Solo and both outputs leave it");
            Assert.That(NonZeroCount(block), Is.EqualTo(1));
            Assert.That(graph.NodeCount, Is.EqualTo(5));
            Assert.That(graph.EdgeCount, Is.EqualTo(6));
        }

        [Test]
        public void GraphMetersShowStereoNodeOutputAndTheSignalAnAlignedEdgeCarries()
        {
            DspGraph graph = FanIn(ChainOf(new DelayEffect(0) { Gain = 2f }), ChainOf(new DelayEffect(5) { Gain = 0.5f }));
            DspGraphMeterTap meters = new(8);
            Assert.That(graph.TryPrepare(8, 4, 2, 0, 3), Is.True);
            graph.Process(new[] { -0.75f, 0.25f, 0f, 0f, 0f, 0f, 0f, 0f }, 0, 8, 2, meters);
            Assert.That(meters.ReadNode(1).Left, Is.EqualTo(1.5f));
            Assert.That(meters.ReadNode(1).Right, Is.EqualTo(0.5f));
            Assert.That(meters.ReadNode(1).Clipped, Is.True);
            Assert.That(meters.ReadEdge(2).Left, Is.Zero, "the short branch has yet to arrive");

            float[] silence = new float[8];
            graph.Process(silence, 0, silence.Length, 2, meters);
            Assert.That(meters.ReadEdge(2).Left, Is.EqualTo(1.5f));
            Assert.That(meters.ReadEdge(2).Right, Is.EqualTo(0.5f));
            Assert.That(meters.ReadEdge(3).Left, Is.EqualTo(0.375f));
            Assert.That(meters.ReadNode(3).Left, Is.EqualTo(1.875f));
            Assert.That(silence[2], Is.EqualTo(-1.875f), "metering preserves the audible sum");
            Assert.That(meters.ReadNode(1).Left, Is.Zero);
            Assert.That(meters.ReadNode(1).HeldLeft, Is.EqualTo(1.5f));

            Array.Clear(silence, 0, silence.Length);
            for (int block = 0; block < 3; ++block)
            {
                graph.Process(silence, 0, silence.Length, 2, meters);
            }
            Assert.That(meters.ReadNode(1).HeldLeft, Is.Zero, "the peak expires after two seconds of audio");
            Assert.That(meters.ReadNode(1).Clipped, Is.False);
        }

        [Test]
        public void SpectrumPairsTheSelectedNodesInputAndOutputAcrossBlocks()
        {
            DspGraph graph = FanIn(ChainOf(new DelayEffect(0) { Gain = 2f }), ChainOf(new DelayEffect(0) { Gain = 0.5f }));
            DspGraphSpectrumTap spectrum = new(SampleRate, 4);
            DspGraphMeterTap meters = new(SampleRate, spectrum);
            Assert.That(graph.TryPrepare(SampleRate, 4, 2, 0, 3), Is.True);
            float[] first = { 0.125f, -0.25f, 0.25f, 0.125f };
            float[] second = { 0.25f, 0.125f, 0.125f, -0.25f };
            float[] input = new float[8];
            float[] output = new float[8];

            spectrum.Select(1);
            graph.Process(first, 0, first.Length, 2, meters);
            Assert.That(spectrum.TryRead(input, output), Is.False);
            graph.Process(second, 0, second.Length, 2, meters);
            Assert.That(spectrum.TryRead(input, output), Is.True);
            Assert.That(input, Is.EqualTo(new[] { 0.125f, -0.25f, 0.25f, 0.125f, 0.25f, 0.125f, 0.125f, -0.25f }));
            Assert.That(output, Is.EqualTo(new[] { 0.25f, -0.5f, 0.5f, 0.25f, 0.5f, 0.25f, 0.25f, -0.5f }));
            Assert.That(first, Is.EqualTo(new[] { 0.3125f, -0.625f, 0.625f, 0.3125f }), "the audible sum is unchanged");
        }

        [Test]
        public void SpectrumDiscardsACompletedWindowWhenSelectionChanges()
        {
            DspGraph graph = FanIn(new DspChain(), ChainOf(new DelayEffect(0) { Gain = 0.25f }));
            DspGraphSpectrumTap spectrum = new(SampleRate, 2);
            DspGraphMeterTap meters = new(SampleRate, spectrum);
            Assert.That(graph.TrySolo(0, 3, 2, out int start, out int end, meters), Is.True);
            Assert.That(graph.TryPrepare(SampleRate, 2, 1, start, end), Is.True);
            float[] input = new float[4];
            float[] output = new float[4];

            spectrum.Select(2);
            graph.Process(new[] { 0.5f, 0.25f }, 0, 2, 1, meters);
            spectrum.Select(1);
            Assert.That(spectrum.TryRead(input, output), Is.False, "discard the old selection");
            graph.Process(new[] { 0.5f, 0.25f }, 0, 2, 1, meters);
            Assert.That(spectrum.TryRead(input, output), Is.False, "the pruned node has no audition signal");

            spectrum.Select(2);
            graph.Process(new[] { 0.5f, 0.25f }, 0, 2, 1, meters);
            Assert.That(spectrum.TryRead(input, output), Is.True);
            Assert.That(input, Is.EqualTo(new[] { 0.5f, 0.5f, 0.25f, 0.25f }));
            Assert.That(output, Is.EqualTo(new[] { 0.125f, 0.125f, 0.0625f, 0.0625f }));

            graph.Process(new[] { 0.5f, 0.25f }, 0, 2, 1, meters);
            spectrum.Select(1);
            spectrum.Select(2);
            Assert.That(spectrum.TryRead(input, output), Is.False, "reselecting a node discards its old window");
        }

        [Test]
        public void SoloMetersRetainAuthoredIndicesAfterCompaction()
        {
            DspGraph graph = FanIn(new DspChain(), ChainOf(new DelayEffect(0) { Gain = 0.25f }));
            DspGraphMeterTap meters = new(SampleRate);
            Assert.That(graph.TrySolo(0, 3, 2, out int input, out int output, meters), Is.True);
            Assert.That(graph.TryPrepare(SampleRate, 4, 1, input, output), Is.True);

            graph.Process(new[] { 0f, 0.5f, 0f, 0f }, 0, 4, 1, meters);
            Assert.That(meters.ReadNode(1).Active, Is.False);
            Assert.That(meters.ReadNode(2).Active, Is.True);
            Assert.That(meters.ReadNode(2).Right, Is.EqualTo(0.125f), "mono feeds both display channels");
            Assert.That(meters.ReadEdge(0).Active, Is.False);
            Assert.That(meters.ReadEdge(1).Left, Is.EqualTo(0.5f));
            Assert.That(meters.ReadEdge(2).Active, Is.False);
            Assert.That(meters.ReadEdge(3).Left, Is.EqualTo(0.125f));
        }

        [Test]
        public void SoloRefusesAnAuthoredGraphWithANodeOffThePath()
        {
            DspGraph graph = FanIn(new DspChain(), new DspChain());
            AddNode(graph, new DspChain());

            Assert.That(graph.TrySolo(0, 3, 1, out _, out _), Is.False);
            Assert.That(graph.NodeCount, Is.EqualTo(5));
            Assert.That(graph.EdgeCount, Is.EqualTo(4));
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
            int first = AddNode(graph, new DspChain());
            int second = AddNode(graph, new DspChain());
            int third = AddNode(graph, new DspChain());

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
            int input = AddNode(stranded, new DspChain());
            int middle = AddNode(stranded, new DspChain());
            int output = AddNode(stranded, new DspChain());
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
            int input = AddNode(graph, ChainOf(new DelayEffect(0) { Gain = 0f }));

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
        public void ABlockLargerThanThePreparedShapePassesThrough()
        {
            DspGraph graph = FanIn(ChainOf(new DelayEffect(0) { Gain = 0f }), ChainOf(new DelayEffect(0) { Gain = 0f }));
            graph.TryPrepare(SampleRate, 64, 2, 0, 3);

            float[] block = Impulse(128, 2, 0, 1f);
            float[] before = (float[])block.Clone();
            graph.Process(block, 0, block.Length, 2);

            Assert.That(block, Is.EqualTo(before), "a block wider than the prepared one is not the shape the graph holds");
        }

        [Test]
        public void AnEdgeDelayLongerThanTheBlockStillArrivesOnTime()
        {
            const int offset = 8;
            DspGraph graph = FanIn(ChainOf(new DelayEffect(300)), ChainOf(new DelayEffect(10)));
            graph.TryPrepare(SampleRate, 64, 1, 0, 3);

            float[] gathered = new float[64 * 8];
            float[] block = new float[64 + offset];
            for (int pass = 0; pass < 8; ++pass)
            {
                Array.Clear(block, 0, block.Length);
                if (pass == 0)
                {
                    block[offset] = 0.5f;
                }

                graph.Process(block, offset, 64, 1);
                Array.Copy(block, offset, gathered, pass * 64, 64);
            }

            Assert.That(gathered[300], Is.EqualTo(1f).Within(1e-5f), "a 290-frame edge delay spans four blocks");
            Assert.That(NonZeroCount(gathered), Is.EqualTo(1), "the short branch waits across every block");
        }

        [Test]
        public void ResetClearsEveryBranchAndLine()
        {
            DspGraph graph = FanIn(ChainOf(new DelayEffect(100)), ChainOf(new DelayEffect(300)));
            graph.TryPrepare(SampleRate, 512, 1, 0, 3);
            float[] block = Impulse(200, 1, 0, 1f);
            graph.Process(block, 0, block.Length, 1);
            graph.Reset();

            float[] silence = new float[512];
            graph.Process(silence, 0, silence.Length, 1);
            graph.Process(silence, 0, silence.Length, 1);

            Assert.That(NonZeroCount(silence), Is.Zero, "the impulse was still in a delay line when the reset ran");
        }

        [Test]
        public void TheSameChainIsNotTwoNodes()
        {
            DspGraph graph = new();
            DspChain shared = ChainOf(new DelayEffect(10));
            Assert.That(graph.TryAddNode(shared, out int first), Is.True);

            Assert.That(graph.TryAddNode(shared, out int second), Is.False, "a stateful chain belongs to one node");
            Assert.That(second, Is.EqualTo(-1));
            Assert.That(graph.NodeCount, Is.EqualTo(1));
            Assert.That(first, Is.Zero);
        }

        [Test]
        public void AnOversizedLayoutIsRefusedBeforeAChainPrepares()
        {
            DspLimiter limiter = new();
            DspGraph graph = FanIn(ChainOf(limiter), ChainOf(new DelayEffect(0)));

            Assert.That(graph.TryPrepare(SampleRate, 64, 20_000, 0, 3), Is.False);
            Assert.That(limiter.LatencySamples, Is.Zero, "the graph refused the layout before a stage spent its memory");
        }

        [Test]
        public void AnOverflowingLayoutIsRefusedAndTheGraphCanPrepareAgain()
        {
            DspGraph graph = new();
            DspChain chain = ChainOf(new DelayEffect(0) { Gain = 0f });
            int node = AddNode(graph, chain);
            float[] block = { 1f };
            Assert.That(graph.TryPrepare(SampleRate, 1, 1, node, node), Is.True);

            Assert.That(graph.TryPrepare(SampleRate, int.MaxValue, int.MaxValue, node, node), Is.False);
            Assert.That(graph.IsPrepared, Is.False);
            Assert.That(graph.PreparedBytes, Is.Zero);
            graph.Process(block, 0, block.Length, 1);
            Assert.That(block[0], Is.EqualTo(1f));

            Assert.That(graph.TryPrepare(SampleRate, 1, 1, node, node), Is.True);
            graph.Process(block, 0, block.Length, 1);
            Assert.That(block[0], Is.Zero);
        }

        [TestCase(0, true, TestName = "Graph buffers exactly at the budget are accepted")]
        [TestCase(1, false, TestName = "Graph buffers one stereo frame over the budget are refused")]
        public void NodesAndDelayedEdgesShareThePreparationBudget(int extraFrames, bool accepted)
        {
            const int frames = 16;
            const int channels = 2;
            const int budgetFrames = DspGraph.MaximumPreparedBytes / (channels * sizeof(float));
            // Four node buffers and one delayed edge also hold one block each.
            int delay = budgetFrames - (5 * frames) + extraFrames;
            DspGraph graph = FanIn(ChainOf(new LatencyClaimEffect(delay)), new DspChain());

            Assert.That(graph.TryPrepare(SampleRate, frames, channels, 0, 3), Is.EqualTo(accepted));
            Assert.That(graph.IsPrepared, Is.EqualTo(accepted));
            Assert.That(graph.PreparedBytes, Is.EqualTo(accepted ? DspGraph.MaximumPreparedBytes : 0));
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
        public void EachNodeReportsTheDelayItWaitsFor()
        {
            DspGraph graph = FanIn(ChainOf(new DelayEffect(100)), ChainOf(new DelayEffect(300)));
            graph.TryPrepare(SampleRate, 64, 1, 0, 3);

            Assert.That(graph.TryGetNodeLatency(0, out int input), Is.True);
            Assert.That(graph.TryGetNodeLatency(1, out int first), Is.True);
            Assert.That(graph.TryGetNodeLatency(2, out int second), Is.True);
            Assert.That(graph.TryGetNodeLatency(3, out int output), Is.True);
            Assert.That(input, Is.Zero, "the input arrives with the block");
            Assert.That(first, Is.EqualTo(100));
            Assert.That(second, Is.EqualTo(300));
            Assert.That(output, Is.EqualTo(graph.LatencySamples), "the output node holds the longest path");
        }

        [Test]
        public void ANodeLatencyIsRefusedBeforePrepareAndOutsideTheGraph()
        {
            DspGraph graph = FanIn(ChainOf(new DelayEffect(100)), ChainOf(new DelayEffect(300)));
            Assert.That(graph.TryGetNodeLatency(0, out int refused), Is.False, "an unprepared graph has no arrival");
            Assert.That(refused, Is.Zero);

            graph.TryPrepare(SampleRate, 64, 1, 0, 3);

            Assert.That(graph.TryGetNodeLatency(-1, out int before), Is.False);
            Assert.That(graph.TryGetNodeLatency(graph.NodeCount, out int after), Is.False);
            Assert.That(before, Is.Zero);
            Assert.That(after, Is.Zero);
        }

        [Test]
        public void TheNodeCountIsBounded()
        {
            DspGraph graph = new();
            for (int node = 0; node < DspGraph.MaximumNodes; ++node)
            {
                Assert.That(graph.TryAddNode(new DspChain(), out _), Is.True);
            }

            Assert.That(graph.TryAddNode(new DspChain(), out int extra), Is.False);
            Assert.That(extra, Is.EqualTo(-1));
            Assert.That(graph.NodeCount, Is.EqualTo(DspGraph.MaximumNodes));
            Assert.That(graph.TryAddNode(null, out _), Is.False, "a null chain is not a node");
        }

        /// <summary>Delays the signal by a whole number of frames, so a branch arrives late for real.</summary>
        private sealed class DelayEffect : DspEffect
        {
            public float Gain { get; set; } = 1f;

            public override string DisplayName => "Delay";

            public override int LatencySamples => _frames;
            private readonly int _frames;
            private float[] _line = Array.Empty<float>();
            private int _position;

            public DelayEffect(int frames)
            {
                _frames = frames;
            }

            public override void Process(float[] buffer, int offset, int count, int channels)
            {
                if (_frames == 0)
                {
                    for (int sample = 0; sample < count; ++sample)
                    {
                        buffer[offset + sample] *= Gain;
                    }

                    return;
                }

                int capacity = _line.Length;
                for (int sample = 0; sample < count; ++sample)
                {
                    float held = _line[_position];
                    _line[_position] = buffer[offset + sample];
                    buffer[offset + sample] = held * Gain;
                    ++_position;
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
            public override string DisplayName => "Claim";

            public override int LatencySamples => _frames;
            private readonly int _frames;

            public LatencyClaimEffect(int frames)
            {
                _frames = frames;
            }

            public override void Process(float[] buffer, int offset, int count, int channels) { }

            protected override void PrepareCore(int sampleRate, int maxBlockSize, int channels) { }
        }
    }
}
