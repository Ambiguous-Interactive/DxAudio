namespace WallstopStudios.DxAudio.Tests
{
    using System;
    using NUnit.Framework;

    /// <summary>
    /// A DSP chain runs on the audio thread, so it must not allocate per block (DxAudio#27). Runs on .NET only:
    /// <c>GC.GetAllocatedBytesForCurrentThread</c> is not something every Unity runtime offers.
    /// </summary>
    public sealed class DspChainAllocationTests
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
            chain.TryPrepare(SampleRate, BlockFrames, Channels);

            float[] block = new float[BlockFrames * Channels];
            for (int frame = 0; frame < block.Length; frame++)
            {
                block[frame] = (float)Math.Sin((2 * Math.PI * 1_000 * frame) / SampleRate);
            }

            chain.Process(block, 0, block.Length, Channels);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int pass = 0; pass < Blocks; pass++)
            {
                chain.Process(block, 0, block.Length, Channels);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero, $"{Blocks} blocks through five effects allocated {allocated} bytes");
        }
    }
}
