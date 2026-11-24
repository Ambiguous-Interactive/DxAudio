namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using NUnit.Framework;
    using Unity.Profiling;
    using UnityEngine;
    using WallstopStudios.DxAudio.Editor;

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

        private static void AssertPooledPlayDoesNotAllocate(MadeObjects made, SfxCue cue)
        {
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { cue }, voiceCount: 1));
            AudioSource voice = player.GetComponentInChildren<AudioSource>();
            Assert.That(voice != null, Is.True, "The cue must use a pooled source.");
            int started = 0;
            void PlayAndRelease()
            {
                if (player.Play(cue))
                {
                    ++started;
                }

                voice.Stop();
            }

            for (int index = 0; index < WarmupCalls; ++index)
            {
                PlayAndRelease();
            }

            Assert.That(started, Is.EqualTo(WarmupCalls));
            started = 0;
            long allocations = AllocationEvents(PlayAndRelease, MeasuredCalls);

            Assert.That(started, Is.EqualTo(MeasuredCalls), "Every measured play must reach the DSP host.");
            Assert.That(allocations, Is.Zero);
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
        public void PlayingWithTheMixMonitorEnabledDoesNotAllocate()
        {
            using MadeObjects made = new();
            SfxCue cue = Cue(made, key: "hit");
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { cue }, voiceCount: 1));
            AudioSource voice = player.GetComponentInChildren<AudioSource>();
            Assert.That(voice != null, Is.True, "The cue must use a pooled source.");
            MusicPlayer music = made.Track(new GameObject("music")).AddComponent<MusicPlayer>();
            MusicTrack track = made.Track(MusicTrack.Create(cue.Variants[0].Clip));
            music.Paused = true;
            music.Play(track);
            DxAudioMixMonitorWindow monitor = made.Track(ScriptableObject.CreateInstance<DxAudioMixMonitorWindow>());
            MusicDuck duck = made.Track(MusicDuck.Create(MusicDuckHolders.Many, 0.5f, applySeconds: 0f));
            for (int hold = 0; hold < monitor.Timeline.DuckCapacity; ++hold)
            {
                music.Duck(duck);
            }

            monitor.SelectPlayers(player, music);
            if (!Application.isBatchMode)
            {
                monitor.Show();
            }

            int started = 0;
            void PlayAndDrop()
            {
                if (player.Play("hit"))
                {
                    ++started;
                }

                voice.Stop();
                player.Play("missing");
            }

            for (int index = 0; index < WarmupCalls; ++index)
            {
                PlayAndDrop();
            }

            monitor.SamplePlayer();
            int before = monitor.Capture.Counts.notPlayable;
            started = 0;
            long allocations = AllocationEvents(PlayAndDrop, MeasuredCalls);
            monitor.SamplePlayer();
            player.Paused = true;
            Assert.That(player.Play(cue), Is.True);
            for (int index = 0; index < WarmupCalls; ++index)
            {
                monitor.SamplePlayer();
            }

            Assert.That(monitor.Timeline.Clip(monitor.Timeline.Count - 1, 0).clip != null, Is.True);
            Assert.That(monitor.Timeline.VoicePeaks(monitor.Timeline.Count - 1, 0) != null, Is.True);
            Assert.That(monitor.Timeline.Deck(monitor.Timeline.Count - 1, 0).track, Is.SameAs(track));
            Assert.That(monitor.Timeline.DuckHold(monitor.Timeline.Count - 1, monitor.Timeline.DuckCapacity - 1).reason, Is.SameAs(duck));
            long samplingAllocations = AllocationEvents(monitor.SamplePlayer, MeasuredCalls);

            Assert.That(started, Is.EqualTo(MeasuredCalls));
            Assert.That(monitor.Capture.Counts.notPlayable - before, Is.EqualTo(MeasuredCalls));
            Assert.That(allocations, Is.Zero, $"Play allocated {allocations} times over {MeasuredCalls:N0} cue pairs.");
            Assert.That(samplingAllocations, Is.Zero, $"Sampling allocated {samplingAllocations} times over {MeasuredCalls:N0} reads.");
        }

        [TestCase("Distortion")]
        [TestCase("Delay")]
        [TestCase("Noise Gate")]
        [TestCase("Chorus")]
        [TestCase("Tremolo")]
        [TestCase("Phaser")]
        [TestCase("Reverb")]
        [TestCase("Stereo Width")]
        public void PlayingACueWithAChainDoesNotAllocate(string stage)
        {
            using MadeObjects made = new();
            DspEffectAsset effect = stage switch
            {
                "Distortion" => made.Track(DspDistortionAsset.Create()),
                "Delay" => made.Track(DspDelayAsset.Create(DspDelay.MaximumDelaySeconds)),
                "Chorus" => made.Track(DspChorusAsset.Create()),
                "Tremolo" => made.Track(DspTremoloAsset.Create()),
                "Phaser" => made.Track(DspPhaserAsset.Create()),
                "Reverb" => made.Track(DspReverbAsset.Create()),
                "Stereo Width" => made.Track(DspStereoWidthAsset.Create(0.5f)),
                _ => made.Track(DspNoiseGateAsset.Create()),
            };
            DspChainAsset chain = made.Track(DspChainAsset.Create(new[] { made.Track(DspLimiterAsset.Create()), effect }));
            SfxCue cue = made.Track(SfxCue.Create(new[] { new SfxVariant(Clip(made)) }, minimumIntervalSeconds: 0f, chain: chain));
            AssertPooledPlayDoesNotAllocate(made, cue);
        }

        [TestCase("Delay", TestName = "Rendering delay across reset and repeat does not allocate")]
        [TestCase("Distortion", TestName = "Rendering distortion across reset and repeat does not allocate")]
        [TestCase("Chorus", TestName = "Rendering chorus across reset and repeat does not allocate")]
        [TestCase("Tremolo", TestName = "Rendering tremolo across reset and repeat does not allocate")]
        [TestCase("Phaser", TestName = "Rendering phaser across reset and repeat does not allocate")]
        [TestCase("Reverb", TestName = "Rendering reverb across reset and repeat does not allocate")]
        [TestCase("Stereo Width", TestName = "Rendering stereo width across reset and repeat does not allocate")]
        public void RenderingAcrossResetAndRepeatDoesNotAllocate(string stage)
        {
            const int Channels = 2;
            const int Frames = 1_024;
            const int Blocks = 128;
            DspEffect effect = stage switch
            {
                "Distortion" => new DspDistortion(),
                "Chorus" => new DspChorus { DepthSeconds = DspChorus.MaximumDepthSeconds },
                "Tremolo" => new DspTremolo { RateHertz = DspTremolo.MaximumRateHertz, Depth = 1f },
                "Phaser" => new DspPhaser { RateHertz = DspPhaser.MaximumRateHertz, Depth = 1f },
                "Reverb" => new DspReverb { DecaySeconds = DspReverb.MaximumDecaySeconds, WetLevel = 1f },
                "Stereo Width" => new DspStereoWidth { Width = 0.5f },
                _ => new DspDelay { DelaySeconds = DspDelay.MaximumDelaySeconds },
            };
            Assert.That(effect.TryPrepare(48_000, Frames, Channels), Is.True);
            float[] audio = new float[Frames * Channels];
            Action render = () =>
            {
                audio[0] = 0.125f;
                effect.Process(audio, 0, audio.Length, Channels);
            };

            for (int index = 0; index < Blocks; ++index)
            {
                render();
            }

            Action resetAndRepeat = () =>
            {
                effect.Reset();
                render();
            };
            resetAndRepeat();
            long resetAllocations = AllocationEvents(resetAndRepeat, Blocks);
            long allocations = AllocationEvents(render, Blocks);

            Assert.That(resetAllocations, Is.Zero, "Reset and the first render must stay allocation-free.");

            Assert.That(allocations, Is.Zero, "Rendering must stay allocation-free after the effect resets.");
        }

        [Test]
        public void RenderingANoiseGateThroughOpenAndCloseDoesNotAllocate()
        {
            const int Frames = 1_024;
            const int Channels = 2;
            DspNoiseGate gate = new();
            Assert.That(gate.TryPrepare(48_000, Frames, Channels), Is.True);
            float[] input = new float[Frames * Channels];
            float[] audio = new float[input.Length];
            for (int frame = 0; frame < Frames; ++frame)
            {
                input[frame * Channels + 1] = frame % 128 < 64 ? 0.001f : 0.25f;
            }

            Action render = () =>
            {
                Array.Copy(input, audio, input.Length);
                gate.Process(audio, 0, audio.Length, Channels);
            };
            for (int index = 0; index < WarmupCalls; ++index)
            {
                render();
            }

            gate.Reset();
            long allocations = AllocationEvents(render, MeasuredCalls);

            Assert.That(allocations, Is.Zero);
        }

        [TestCase("Distortion", TestName = "Playing a cue with distortion in a graph does not allocate")]
        [TestCase("Chorus", TestName = "Playing a cue with chorus in a graph does not allocate")]
        [TestCase("Tremolo", TestName = "Playing a cue with tremolo in a graph does not allocate")]
        [TestCase("Phaser", TestName = "Playing a cue with phaser in a graph does not allocate")]
        [TestCase("Reverb", TestName = "Playing a cue with reverb in a graph does not allocate")]
        [TestCase("Stereo Width", TestName = "Playing a cue with stereo width in a graph does not allocate")]
        public void PlayingACueWithAGraphDoesNotAllocate(string stage)
        {
            using MadeObjects made = new();
            DspEffectAsset effect = stage switch
            {
                "Chorus" => made.Track(DspChorusAsset.Create()),
                "Tremolo" => made.Track(DspTremoloAsset.Create()),
                "Phaser" => made.Track(DspPhaserAsset.Create()),
                "Reverb" => made.Track(DspReverbAsset.Create()),
                "Stereo Width" => made.Track(DspStereoWidthAsset.Create(0.5f)),
                _ => made.Track(DspDistortionAsset.Create()),
            };
            DspChainAsset branch = made.Track(DspChainAsset.Create(new[] { made.Track(DspLimiterAsset.Create()), effect }));
            DspGraphAsset graph = made.Track(
                DspGraphAsset.Create(
                    new[] { new DspGraphNode("in", null), new DspGraphNode("branch", branch), new DspGraphNode("out", null) },
                    new[] { new DspGraphEdge("in", "branch"), new DspGraphEdge("branch", "out") },
                    "in",
                    "out"
                )
            );
            SfxCue cue = made.Track(SfxCue.CreateWithGraph(new[] { new SfxVariant(Clip(made)) }, graph: graph, minimumIntervalSeconds: 0f));
            AssertPooledPlayDoesNotAllocate(made, cue);
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
