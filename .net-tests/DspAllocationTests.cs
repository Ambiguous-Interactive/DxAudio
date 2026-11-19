namespace WallstopStudios.DxAudio.Tests
{
    using System;
    using NUnit.Framework;

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
            _ = graph.TryAddNode(new DspChain(), out int input);
            _ = graph.TryAddNode(shifterBranch, out int left);
            _ = graph.TryAddNode(dynamicsBranch, out int right);
            _ = graph.TryAddNode(new DspChain(), out int output);
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
            for (int frame = 0; frame < block.Length; frame++)
            {
                block[frame] = (float)Math.Sin((2 * Math.PI * 1_000 * frame) / SampleRate);
            }

            return block;
        }
    }
}
