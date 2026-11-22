namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using NUnit.Framework;
    using Unity.Profiling;
    using UnityEngine;

    /// <summary>Checks managed allocations during warmed play and render calls.</summary>
    public sealed class AllocationTests
    {
        private const int MeasuredCalls = 100_000;
        private const int WarmupCalls = 1_024;

        private static long AllocationEvents(Action run, int count)
        {
            // The process heap also includes allocations from other editor threads.
            using ProfilerRecorder recorder = new(
                ProfilerCategory.Internal,
                "GC.Alloc",
                1,
                ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread
            );
            Assert.That(recorder.Valid, Is.True, "Unity must expose the GC.Alloc marker.");
            recorder.Start();
            for (int index = 0; index < count; ++index)
            {
                run();
            }

            recorder.Stop();
            return recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
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
            Action play = () => player.Play(cue);
            for (int index = 0; index < WarmupCalls; ++index)
            {
                play();
            }

            long allocations = AllocationEvents(play, MeasuredCalls);

            Assert.That(allocations, Is.Zero, $"Play allocated {allocations} times over {MeasuredCalls:N0} plays.");
        }

        [Test]
        public void PlayingByKeyDoesNotAllocate()
        {
            using MadeObjects made = new();
            SfxCue cue = Cue(made, key: "hit");
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { cue }, voiceCount: 4));
            Action play = () => player.Play("hit");
            for (int index = 0; index < WarmupCalls; ++index)
            {
                play();
            }

            long allocations = AllocationEvents(play, MeasuredCalls);

            Assert.That(allocations, Is.Zero, $"Keyed Play allocated {allocations} times over {MeasuredCalls:N0} plays.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PlayingACueWithAChainDoesNotAllocate(bool useDelay)
        {
            using MadeObjects made = new();
            DspEffectAsset effect = useDelay ? made.Track(DspDelayAsset.Create(DspDelay.MaximumDelaySeconds)) : made.Track(DspDistortionAsset.Create());
            DspChainAsset chain = made.Track(DspChainAsset.Create(new[] { made.Track(DspLimiterAsset.Create()), effect }));
            SfxCue cue = made.Track(SfxCue.Create(new[] { new SfxVariant(Clip(made)) }, minimumIntervalSeconds: 0f, chain: chain));
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { cue }, voiceCount: 4));
            Action play = () => player.Play(cue);
            for (int index = 0; index < WarmupCalls; ++index)
            {
                play();
            }

            long allocations = AllocationEvents(play, MeasuredCalls);

            Assert.That(allocations, Is.Zero, $"Play through a chain allocated {allocations} times over {MeasuredCalls:N0} plays.");
        }

        [Test]
        public void RenderingADelayAcrossResetAndRepeatDoesNotAllocate()
        {
            const int Channels = 2;
            const int Frames = 1_024;
            const int Blocks = 128;
            DspDelay delay = new() { DelaySeconds = DspDelay.MaximumDelaySeconds };
            Assert.That(delay.TryPrepare(48_000, Frames, Channels), Is.True);
            float[] audio = new float[Frames * Channels];
            Action render = () =>
            {
                audio[0] = 0.125f;
                delay.Process(audio, 0, audio.Length, Channels);
            };

            for (int index = 0; index < Blocks; ++index)
            {
                render();
            }

            delay.Reset();
            long allocations = AllocationEvents(render, Blocks);

            Assert.That(allocations, Is.Zero, "Rendering must stay allocation-free while filling and reading delay history.");
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
            Action play = () => player.Play(cue);
            for (int index = 0; index < WarmupCalls; ++index)
            {
                play();
            }

            long allocations = AllocationEvents(play, MeasuredCalls);

            Assert.That(allocations, Is.Zero, $"Play through a graph allocated {allocations} times over {MeasuredCalls:N0} plays.");
        }

        [Test]
        public void RenderingAMixBusBlockDoesNotAllocate()
        {
            using MadeObjects made = new();
            DspChainAsset chain = made.Track(DspChainAsset.Create(new DspEffectAsset[] { made.Track(DspLimiterAsset.Create()), made.Track(DspCompressorAsset.Create()) }));
            AudioMixBus bus = made.Track(new GameObject("mix bus")).AddComponent<AudioMixBus>();
            bus.Chain = chain;
            float[] block = new float[64];
            void Render()
            {
                for (int index = 0, blockLength = block.Length; index < blockLength; ++index)
                {
                    block[index] = 1f;
                }

                bus.ProcessBuffer(block, 1);
            }

            Action render = Render;
            for (int index = 0; index < WarmupCalls; ++index)
            {
                render();
            }

            // Reselect the prepared slot so recording includes its first block and reset.
            bus.Chain = null;
            bus.Chain = chain;
            long allocations = AllocationEvents(render, MeasuredCalls);

            Assert.That(allocations, Is.Zero, $"The mix bus allocated {allocations} times over {MeasuredCalls:N0} blocks.");
            Assert.That(bus.SkippedBlocks, Is.Zero, "Every block must reach the chain.");
            Assert.That(block[0], Is.GreaterThan(0f).And.LessThan(0.95f), "The chain must attenuate the full-scale block.");
        }

        [Test]
        public void TheProfilerSeesASingleTransientAllocation()
        {
            Action allocate = static () => GC.KeepAlive(new byte[16]);
            allocate();
            long allocations = AllocationEvents(allocate, 1);

            Assert.That(allocations, Is.EqualTo(1));
        }
    }
}
