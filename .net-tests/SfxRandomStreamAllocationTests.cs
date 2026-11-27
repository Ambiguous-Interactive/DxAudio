namespace WallstopStudios.DxAudio.Tests
{
    using System;
    using NUnit.Framework;

    /// <summary>
    /// The random foundation draws on the audio thread once a graph is prepared, so warmed draws must not move the
    /// managed heap (PLAN R1, design contract 4). Runs on .NET only: <c>GC.GetAllocatedBytesForCurrentThread</c> is
    /// not something every Unity runtime offers.
    /// </summary>
    public sealed class SfxRandomStreamAllocationTests
    {
        private const int Draws = 1_000_000;

        [Test]
        public void AMillionStreamDrawsAllocateNothing()
        {
            SfxRandomStream stream = new(42UL);
            double sink = 0d;
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < Draws; ++index)
            {
                sink += stream.NextDouble();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(sink);

            Assert.That(allocated, Is.EqualTo(0), "a million stream draws allocated");
        }

        [Test]
        public void AMillionStructDrawsAllocateNothing()
        {
            SfxXoshiro256StarStar generator = new(42UL);
            ulong sink = 0UL;
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < Draws; ++index)
            {
                sink += generator.NextUInt64();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(sink);

            Assert.That(allocated, Is.EqualTo(0), "a million struct draws allocated");
        }

        [Test]
        public void DistributedDrawsThroughBothInterfacesAllocateNothing()
        {
            SfxRandomStream stream = new(7UL);
            ISfxRandom plain = stream;
            double sink = 0d;
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 200_000; ++index)
            {
                sink += stream.NextFloat(-1f, 1f);
                sink += stream.NextBoolSparing(0.3d) ? 1d : 0d;
                sink += stream.NextExponential(2d);
                sink += stream.NextGaussian(0d, 1d);
                sink += plain.NextDouble();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(sink);

            Assert.That(allocated, Is.EqualTo(0), "distributed draws through the interfaces allocated");
        }
    }
}
