namespace WallstopStudios.DxAudio.Tests
{
    using System;
    using System.Diagnostics;
    using NUnit.Framework;
    using WallstopStudios.DxAudio.Editor;

    /// <summary>
    /// A DSP chain and a DSP graph run on the audio thread, so neither may allocate per block (DxAudio#27, DxAudio#69,
    /// DxAudio#78). Runs on .NET only: <c>GC.GetAllocatedBytesForCurrentThread</c> is not something every Unity runtime
    /// offers.
    /// </summary>
    public sealed class DspAllocationTests
    {
        private const int SampleRate = 48_000;
        private const int BlockFrames = 256;
        private const int Channels = 2;
        private const int Blocks = 10_000;

        private static int AddNode(DspGraph graph, DspChain chain)
        {
            if (!graph.TryAddNode(chain, out int index))
            {
                throw new AssertionException("The graph fixture could not add a node.");
            }

            return index;
        }

        private static DspAuditionProcessor.Revision FullGraphRevision(int number, int blockFrames)
        {
            DspGraph graph = new();
            for (int index = 0; index < DspGraph.MaximumNodes; ++index)
            {
                DspChain chain = new();
                chain.Add(
                    new DspFilter
                    {
                        Mode = DspFilterMode.Bell,
                        FrequencyHz = 1_200f,
                        PeakDecibels = number % 2 == 0 ? 4f : 5f,
                    }
                );
                chain.Add(new DspCompressor());
                chain.Add(new DspLimiter { LookaheadSeconds = 0f });
                Assert.That(graph.TryAddNode(chain, out _), Is.True);
            }

            for (int distance = 1; graph.EdgeCount < DspGraph.MaximumEdges; ++distance)
            {
                for (int index = 0; index + distance < graph.NodeCount && graph.EdgeCount < DspGraph.MaximumEdges; ++index)
                {
                    Assert.That(graph.TryAddEdge(index, index + distance), Is.True);
                }
            }

            Assert.That(graph.TryPrepare(SampleRate, blockFrames, Channels, 0, graph.NodeCount - 1), Is.True);
            DspAuditionProcessor.Path[] shapes = { null, new DspAuditionProcessor.Path(blockFrames, Channels, null, graph) };
            return new DspAuditionProcessor.Revision(number, shapes);
        }

        private static long AllocatedBy(DspChain chain, float[] block)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int pass = 0; pass < Blocks; pass++)
            {
                chain.Process(block, 0, block.Length, Channels);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static float[] Sine()
        {
            float[] block = new float[BlockFrames * Channels];
            for (int frame = 0, blockLength = block.Length; frame < blockLength; frame++)
            {
                block[frame] = (float)Math.Sin((2 * Math.PI * 1_000 * frame) / SampleRate);
            }

            return block;
        }

        [Test]
        public void AChainOfEveryEffectDoesNotAllocatePerBlock()
        {
            DspChain chain = new();
            chain.Add(new DspLimiter());
            chain.Add(new DspCompressor());
            chain.Add(new DspDistortion { Drive = 6f });
            chain.Add(new DspBitCrusher { Bits = 6, RateReduction = 2 });
            chain.Add(new DspPitchShifter { Semitones = 7f });
            chain.Add(
                new DspFilter
                {
                    // The bell is the one mode that takes a square root each frame, so the probe must run it.
                    Mode = DspFilterMode.Bell,
                    FrequencyHz = 1_200f,
                    Quality = 2f,
                    PeakDecibels = 12f,
                }
            );
            chain.TryPrepare(SampleRate, BlockFrames, Channels);

            float[] block = Sine();
            chain.Process(block, 0, block.Length, Channels);

            long allocated = AllocatedBy(chain, block);
            Assert.That(allocated, Is.Zero, $"{Blocks} blocks through six effects allocated {allocated} bytes");
        }

        [Test]
        public void AGraphOfEveryEffectDoesNotAllocatePerBlock()
        {
            DspChain shifterBranch = new();
            shifterBranch.Add(new DspPitchShifter { Semitones = 7f });
            DspChain dynamicsBranch = new();
            dynamicsBranch.Add(new DspCompressor());
            dynamicsBranch.Add(new DspLimiter());
            dynamicsBranch.Add(new DspBitCrusher { Bits = 6, RateReduction = 2 });

            DspGraph graph = new();
            int input = AddNode(graph, new DspChain());
            int left = AddNode(graph, shifterBranch);
            int right = AddNode(graph, dynamicsBranch);
            int output = AddNode(graph, new DspChain());
            _ = graph.TryAddEdge(input, left);
            _ = graph.TryAddEdge(input, right);
            _ = graph.TryAddEdge(left, output);
            _ = graph.TryAddEdge(right, output);
            Assert.That(graph.TryPrepare(SampleRate, BlockFrames, Channels, input, output), Is.True);

            float[] block = Sine();
            graph.Process(block, 0, block.Length, Channels);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int pass = 0; pass < Blocks; pass++)
            {
                graph.Process(block, 0, block.Length, Channels);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero, $"{Blocks} blocks through two branches allocated {allocated} bytes");
        }

        [Test]
        public void FullGraphAuditionSwapsWithoutAllocatingOnTheCallback()
        {
            const int PreviewFrames = 1_024;
            const int Warmup = 32;
            const int Samples = 120;
            DspAuditionProcessor processor = new(SampleRate, PreviewFrames, Channels);
            float[] input = new float[PreviewFrames * Channels];
            float[] block = new float[input.Length];
            for (int index = 0, inputLength = input.Length; index < inputLength; ++index)
            {
                input[index] = 0.05f * (float)Math.Sin(2d * Math.PI * 1_000 * (index / Channels) / SampleRate);
            }

            for (int index = 0; index < Warmup; ++index)
            {
                processor.Publish(FullGraphRevision(index + 1, PreviewFrames));
                Array.Copy(input, block, input.Length);
                processor.Process(block, Channels);
            }

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            long[] times = new long[Samples];
            long allocated = 0;
            for (int index = 0; index < Samples; ++index)
            {
                processor.Publish(FullGraphRevision(Warmup + index + 1, PreviewFrames));
                Array.Copy(input, block, input.Length);
                long start = Stopwatch.GetTimestamp();
                long before = GC.GetAllocatedBytesForCurrentThread();
                processor.Process(block, Channels);
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                times[index] = Stopwatch.GetTimestamp() - start;
            }

            long controlStart = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(new byte[32]);
            long control = GC.GetAllocatedBytesForCurrentThread() - controlStart;
            Assert.That(control, Is.GreaterThanOrEqualTo(32), "the allocation probe must detect a planted array");
            Assert.That(allocated, Is.Zero, "the audition callback must allocate nothing during a graph swap");
            Assert.That(processor.ActiveRevision, Is.EqualTo(Warmup + Samples));

            Array.Sort(times);
            double tickMilliseconds = 1_000d / Stopwatch.Frequency;
            double p50 = times[Samples / 2] * tickMilliseconds;
            double p95 = times[(int)Math.Ceiling(Samples * 0.95d) - 1] * tickMilliseconds;
            TestContext.Out.WriteLine(
                $"{Samples} warmed swaps at {SampleRate} Hz, {Channels} channels, {PreviewFrames} frames: "
                    + $"p50 {p50:F3} ms, p95 {p95:F3} ms, callback allocations {allocated} bytes, planted allocation {control} bytes."
            );
        }
    }
}
