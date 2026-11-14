namespace WallstopStudios.DxAudio.Tests
{
    using System;
    using NUnit.Framework;

    /// <summary>
    /// A game that keys cues by a struct must not allocate per sound played (DxAudio#5). Runs on .NET only:
    /// <c>GC.GetAllocatedBytesForCurrentThread</c> is not something every Unity runtime offers.
    /// </summary>
    public sealed class StructKeyAllocationTests
    {
        private const int Calls = 1000;

        private readonly struct CueKey : IEquatable<CueKey>
        {
            private readonly int _id;

            public CueKey(int id)
            {
                _id = id;
            }

            public bool Equals(CueKey other) => _id == other._id;

            public override bool Equals(object obj) => obj is CueKey other && Equals(other);

            public override int GetHashCode() => _id;
        }

        [Test]
        public void ThrottleAndSelectionDoNotAllocatePerCallForAStructKey()
        {
            SfxThrottle<CueKey> throttle = new();
            SfxSelection<CueKey> selection = new();
            CueKey leader = new(1);
            CueKey follower = new(2);
            Exercise(throttle, selection, leader, follower);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int call = 0; call < Calls; call++)
            {
                Exercise(throttle, selection, leader, follower);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero, $"{Calls} rounds on warmed struct keys allocated {allocated} bytes");
        }

        private static void Exercise(
            SfxThrottle<CueKey> throttle,
            SfxSelection<CueKey> selection,
            CueKey leader,
            CueKey follower
        )
        {
            _ = throttle.IsReady(leader, 10f, 0.05f);
            _ = throttle.TryBegin(leader, 10f, 0.05f);
            _ = selection.TryNext(leader, 4, 0.5f, out _);
            _ = selection.TryFollow(follower, leader, 4, out _);
        }
    }
}
