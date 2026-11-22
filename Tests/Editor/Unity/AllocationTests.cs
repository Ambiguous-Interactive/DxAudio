namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
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

        [Test]
        public void CompareMusicVolumeCost()
        {
            using MadeObjects made = new();
            MusicTrack[] tracks = new MusicTrack[3];
            for (int index = 0; index < tracks.Length; ++index)
            {
                tracks[index] = made.Track(MusicTrack.Create(Clip(made)));
            }

            MusicLibrary library = made.Track(MusicLibrary.Create(tracks, fadeInSeconds: 0f, fadeOutSeconds: 100000f));
            MusicDuck duck = made.Track(MusicDuck.Create(MusicDuckHolders.One, volumeScale: 0.5011872f, lowPassCutoffHz: 1200f, applySeconds: 0f));
            VolumeProbeBaselineMusicPlayer baseline = made.Track(new GameObject("baseline music")).AddComponent<VolumeProbeBaselineMusicPlayer>();
            MusicPlayer candidate = made.Track(new GameObject("candidate music")).AddComponent<MusicPlayer>();
            baseline.Muted = true;
            candidate.Muted = true;
            baseline.Library = library;
            candidate.Library = library;
            foreach (MusicTrack track in tracks)
            {
                baseline.Play(track);
                candidate.Play(track);
            }

            Assert.That(baseline.Duck(duck).IsValid && candidate.Duck(duck).IsValid);
            Action tickBaseline = () => baseline.Tick(1f / 60f);
            Action tickCandidate = () => candidate.Tick(1f / 60f);
            ReportVolumeProbe("tick", tickBaseline, tickCandidate);
            CheckActiveMusicVoices(baseline);
            CheckActiveMusicVoices(candidate);
            ReportVolumeProbe("apply", baseline.ApplyVolumeProbe, candidate.ApplyVolumeProbe);
        }

        private static void CheckActiveMusicVoices(MonoBehaviour player)
        {
            AudioSource[] voices = player.GetComponentsInChildren<AudioSource>();
            Assert.That(voices, Has.Length.EqualTo(3));
            foreach (AudioSource voice in voices)
            {
                Assert.That(voice.clip != null && voice.isPlaying && 0f < voice.volume);
            }
        }

        private static void ReportVolumeProbe(string path, Action baseline, Action candidate)
        {
            const int Samples = 120;
            const int Frames = 1024;
            for (int index = 0; index < 12000; ++index)
            {
                baseline();
                candidate();
            }

            double[] before = new double[Samples];
            double[] after = new double[Samples];
            GC.Collect(2, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true);
            for (int index = 0; index < Samples; ++index)
            {
                if ((index & 1) == 0)
                {
                    before[index] = TimeVolumeProbe(baseline, Frames);
                    after[index] = TimeVolumeProbe(candidate, Frames);
                }
                else
                {
                    after[index] = TimeVolumeProbe(candidate, Frames);
                    before[index] = TimeVolumeProbe(baseline, Frames);
                }
            }

            Array.Sort(before);
            Array.Sort(after);
            long baselineEvents = AllocationEvents(baseline, Frames);
            long candidateEvents = AllocationEvents(candidate, Frames);
            TestContext.Out.WriteLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "DXAUDIO_VOLUME_PROBE {0} baseline median {1:F3} p95 {2:F3} us GC.Alloc {3} candidate median {4:F3} p95 {5:F3} us GC.Alloc {6}",
                    path,
                    (before[59] + before[60]) / 2,
                    before[113],
                    baselineEvents,
                    (after[59] + after[60]) / 2,
                    after[113],
                    candidateEvents
                )
            );
        }

        private static double TimeVolumeProbe(Action run, int frames)
        {
            long start = Stopwatch.GetTimestamp();
            for (int index = 0; index < frames; ++index)
            {
                run();
            }

            return (Stopwatch.GetTimestamp() - start) * 1e6 / Stopwatch.Frequency / frames;
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

        [Test]
        public void PlayingACueWithAChainDoesNotAllocate()
        {
            using MadeObjects made = new();
            DspChainAsset chain = made.Track(DspChainAsset.Create(new DspEffectAsset[] { made.Track(DspLimiterAsset.Create()), made.Track(DspDistortionAsset.Create()) }));
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
