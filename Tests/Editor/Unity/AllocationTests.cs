namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// Keeps the play path honest: the package claims no managed allocation per play.
    /// </summary>
    /// <remarks>
    /// <c>GC.GetAllocatedBytesForCurrentThread</c> reports 0 for every call under Unity's Mono, so a test built on it
    /// passes for any implementation. This suite reads the heap instead: measured on the host editor, 100,000 unmodified
    /// plays moved it by 0 to 29 KB, while a planted 16-byte allocation per call moved it by 3.4 MB. The ceiling sits
    /// between those, and <see cref="TheHeapProbeSeesAPlantedAllocation"/> proves the probe can still fail. That control
    /// keeps its planted strings, because a heap counter that is handed only garbage may commit no segment and report
    /// 8 KB for 1.6 MB of churn, which is exactly what a CI run showed.
    /// </remarks>
    public sealed class AllocationTests
    {
        private const int PlayCount = 100_000;
        private const int WarmupPlays = 1_024;

        /// <summary>Bytes the heap may grow over <see cref="PlayCount"/> plays of a path that allocates nothing.</summary>
        private const long Ceiling = 524_288;

        /// <summary>Char count per planted string. 100,000 of them retain about 45 MB, which no heap counter misses.</summary>
        private const int PlantedChars = 200;

        /// <summary>Measured windows per probe. The smallest is taken, because heap segments commit in coarse steps.</summary>
        private const int Windows = 3;

        private static readonly List<string> _plants = new();
        private long _planted;

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

        /// <summary>The heap growth of <paramref name="count"/> strings that stay reachable while it measures.</summary>
        /// <remarks>
        /// One window is enough here: a collector cannot reclaim retained strings, so the reading cannot fall back to
        /// zero the way a garbage-only window did on a CI runner (8 KB for 1.6 MB of churn).
        /// </remarks>
        private static long RetainedPlantGrowth(int count)
        {
            _plants.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            long before = GC.GetTotalMemory(forceFullCollection: true);
            for (int index = 0; index < count; index++)
            {
                _plants.Add(new string('x', PlantedChars));
            }

            return GC.GetTotalMemory(forceFullCollection: true) - before;
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
            return made.Track(SfxCue.Create(new[] { new SfxVariant(Clip(made)), new SfxVariant(Clip(made)) }, key: key, loop: false, minimumIntervalSeconds: 0f));
        }

        private static AudioClip Clip(MadeObjects made)
        {
            return made.Track(AudioClip.Create("test clip", 4_000, 1, 8_000, stream: false));
        }

        [OneTimeSetUp]
        public void MeasureThePlantedControl()
        {
            _planted = RetainedPlantGrowth(PlayCount);
            _plants.Clear();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PlayingACueOnAFullPoolDoesNotAllocate(bool varyPlayback)
        {
            using MadeObjects made = new();
            SfxVariation variation = new(true, -50f, 50f, -1.5f, 0f)
            {
                UseShuffle = true,
                UseLowPass = true,
                MinimumLowPassHertz = 4_000f,
                MaximumLowPassHertz = 12_000f,
            };
            SfxCue cue = varyPlayback
                ? made.Track(
                    SfxCue.CreateWithVariation(
                        new[] { new SfxVariant(Clip(made), 0f, -3f), new SfxVariant(Clip(made)), new SfxVariant(Clip(made)) },
                        variation,
                        minimumIntervalSeconds: 0f
                    )
                )
                : Cue(made);
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { cue }, voiceCount: 4));
            for (int index = 0; index < WarmupPlays; index++)
            {
                player.Play(cue);
            }

            long growth = HeapGrowth(() => player.Play(cue), PlayCount);

            Assert.That(growth, Is.LessThan(Ceiling), $"Play grew the heap by {growth} bytes over {PlayCount:N0} plays.");
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

            Assert.That(growth, Is.LessThan(Ceiling), $"Keyed Play grew the heap by {growth} bytes over {PlayCount:N0} plays.");
        }

        [Test]
        public void PlayingACueWithAChainDoesNotAllocate()
        {
            using MadeObjects made = new();
            DspChainAsset chain = made.Track(DspChainAsset.Create(new DspEffectAsset[] { made.Track(DspLimiterAsset.Create()), made.Track(DspDistortionAsset.Create()) }));
            SfxCue cue = made.Track(SfxCue.Create(new[] { new SfxVariant(Clip(made)) }, minimumIntervalSeconds: 0f, chain: chain));
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { cue }, voiceCount: 4));
            for (int index = 0; index < WarmupPlays; index++)
            {
                player.Play(cue);
            }

            long growth = HeapGrowth(() => player.Play(cue), PlayCount);

            Assert.That(growth, Is.LessThan(Ceiling), $"Play through a chain grew the heap by {growth} bytes over {PlayCount:N0} plays.");
        }

        [Test]
        public void PlayingACueWithAGraphDoesNotAllocate()
        {
            using MadeObjects made = new();
            DspChainAsset branch = made.Track(DspChainAsset.Create(new DspEffectAsset[] { made.Track(DspLimiterAsset.Create()), made.Track(DspDistortionAsset.Create()) }));
            DspGraphAsset graph = made.Track(
                DspGraphAsset.Create(
                    new[] { new DspGraphNode("in", null), new DspGraphNode("branch", branch), new DspGraphNode("out", null) },
                    new[] { new DspGraphEdge("in", "branch"), new DspGraphEdge("branch", "out") },
                    "in",
                    "out"
                )
            );
            SfxCue cue = made.Track(SfxCue.CreateWithGraph(new[] { new SfxVariant(Clip(made)) }, graph: graph, minimumIntervalSeconds: 0f));
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { cue }, voiceCount: 4));
            for (int index = 0; index < WarmupPlays; index++)
            {
                player.Play(cue);
            }

            long growth = HeapGrowth(() => player.Play(cue), PlayCount);

            Assert.That(growth, Is.LessThan(Ceiling), $"Play through a graph grew the heap by {growth} bytes over {PlayCount:N0} plays.");
        }

        [Test]
        public void RenderingAMixBusBlockDoesNotAllocate()
        {
            using MadeObjects made = new();
            DspChainAsset chain = made.Track(DspChainAsset.Create(new DspEffectAsset[] { made.Track(DspLimiterAsset.Create()), made.Track(DspCompressorAsset.Create()) }));
            AudioMixBus bus = made.Track(new GameObject("mix bus")).AddComponent<AudioMixBus>();
            bus.Chain = chain;
            float[] block = new float[64];
            for (int index = 0, blockLength = block.Length; index < blockLength; ++index)
            {
                block[index] = 1f;
            }

            long growth = HeapGrowth(() => bus.ProcessBuffer(block, 1), PlayCount);

            Assert.That(growth, Is.LessThan(Ceiling), $"A mix bus block grew the heap by {growth} bytes over {PlayCount:N0} blocks.");
            Assert.That(bus.SkippedBlocks, Is.Zero, "a block that reached no chain would also grow nothing, so the probe must see it shape");
            Assert.That(block[0], Is.LessThan(0.95f), "the limiter held the full-scale block, so the chain really ran");
        }

        [Test]
        public void TheHeapProbeSeesAPlantedAllocation()
        {
            Assert.That(_planted, Is.GreaterThan(Ceiling), $"a probe that cannot fail is not evidence; {PlayCount:N0} retained planted strings grew it by {_planted} bytes");
        }
    }
}
