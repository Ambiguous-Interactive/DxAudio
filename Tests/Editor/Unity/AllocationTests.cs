namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Keeps the play path honest: the package claims no managed allocation per play.
    /// </summary>
    /// <remarks>
    /// <c>GC.GetAllocatedBytesForCurrentThread</c> reports 0 for every call under Unity's Mono, so a test built on it
    /// passes for any implementation. This suite reads the heap instead and calibrates the ceiling: measured on the
    /// host editor, 100,000 unmodified plays moved it by 0 to 24 KB, while a planted 16-byte allocation per call moved
    /// it by 3.4 MB. The ceiling sits between those, and <see cref="TheHeapProbeSeesAPlantedAllocation"/> proves the
    /// probe can still fail.
    /// </remarks>
    public sealed class AllocationTests
    {
        private const int PlayCount = 100_000;
        private const int WarmupPlays = 1_024;

        /// <summary>Bytes the heap may grow over <see cref="PlayCount"/> plays of a path that allocates nothing.</summary>
        private const long Ceiling = 262_144;

        /// <summary>Measured windows per probe. The smallest is taken, because heap segments commit in coarse steps.</summary>
        private const int Windows = 3;

        [Test]
        public void PlayingACueOnAFullPoolDoesNotAllocate()
        {
            using MadeObjects made = new();
            SfxCue cue = Cue(made);
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { cue }, voiceCount: 4));
            for (int index = 0; index < WarmupPlays; index++)
            {
                player.Play(cue);
            }

            long growth = HeapGrowth(() => player.Play(cue), PlayCount);

            Assert.That(
                growth,
                Is.LessThan(Ceiling),
                $"Play grew the heap by {growth} bytes over {PlayCount:N0} plays."
            );
        }

        [Test]
        public void PlayingByKeyDoesNotAllocate()
        {
            using MadeObjects made = new();
            SfxCue cue = Cue(made, key: "hit");
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { cue }, voiceCount: 4));
            for (int index = 0; index < WarmupPlays; index++)
            {
                player.Play("hit");
            }

            long growth = HeapGrowth(() => player.Play("hit"), PlayCount);

            Assert.That(
                growth,
                Is.LessThan(Ceiling),
                $"Keyed Play grew the heap by {growth} bytes over {PlayCount:N0} plays."
            );
        }

        [Test]
        public void TheHeapProbeSeesAPlantedAllocation()
        {
            long growth = HeapGrowth(PlantSixteenBytes, PlayCount);

            Assert.That(
                growth,
                Is.GreaterThan(Ceiling),
                $"a probe that cannot fail is not evidence; {PlayCount:N0} planted 16-byte arrays grew it by {growth} bytes"
            );
        }

        /// <summary>The smallest heap growth of <paramref name="Windows"/> runs of <paramref name="count"/> calls.</summary>
        /// <remarks>
        /// A collection at the end would reclaim exactly the churn the test looks for, so the second reading is the raw
        /// heap. Unity's Mono counts in coarse segments and occasionally commits one inside a window, which made a
        /// single window fail in a full-suite run; a real regression grows every window, so the smallest one is taken.
        /// </remarks>
        private static long HeapGrowth(Action play, int count)
        {
            // The first call through the delegate compiles its invoke stub, so it runs outside the measured windows.
            play();
            long smallest = long.MaxValue;
            for (int window = 0; window < Windows; window++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                long before = GC.GetTotalMemory(forceFullCollection: false);
                for (int index = 0; index < count; index++)
                {
                    play();
                }

                long growth = GC.GetTotalMemory(forceFullCollection: false) - before;
                if (growth < smallest)
                {
                    smallest = growth;
                }
            }

            return smallest;
        }

        private static void PlantSixteenBytes()
        {
            GC.KeepAlive(new byte[16]);
        }

        private static SfxPlayer SfxPlayer(MadeObjects made, SfxLibrary library)
        {
            made.Track(library);
            SfxPlayer player = made.Track(new GameObject("sfx")).AddComponent<SfxPlayer>();
            player.Library = library;
            return player;
        }

        private static SfxCue Cue(MadeObjects made, string key = null)
        {
            return made.Track(
                SfxCue.Create(
                    new[] { new SfxVariant(Clip(made)), new SfxVariant(Clip(made)) },
                    key: key,
                    loop: false,
                    minimumIntervalSeconds: 0f
                )
            );
        }

        private static AudioClip Clip(MadeObjects made)
        {
            return made.Track(AudioClip.Create("test clip", 4_000, 1, 8_000, stream: false));
        }

        private sealed class MadeObjects : IDisposable
        {
            private readonly List<Object> _made = new();

            public T Track<T>(T made)
                where T : Object
            {
                _made.Add(made);
                return made;
            }

            public void Dispose()
            {
                for (int index = 0; index < _made.Count; ++index)
                {
                    if (_made[index] != null)
                    {
                        Object.DestroyImmediate(_made[index]);
                    }
                }

                _made.Clear();
            }
        }
    }
}
