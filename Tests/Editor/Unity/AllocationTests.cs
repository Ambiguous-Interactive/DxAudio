namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using NUnit.Framework;
    using Unity.Profiling;
    using UnityEngine;
    using WallstopStudios.DxAudio.Editor;
    using Stopwatch = System.Diagnostics.Stopwatch;

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

        private static SfxCue Cue(MadeObjects made, string key = null, SfxPriority priority = SfxPriority.Medium)
        {
            return made.Track(
                SfxCue.Create(new[] { new SfxVariant(Clip(made)), new SfxVariant(Clip(made)) }, key: key, loop: false, minimumIntervalSeconds: 0f, priority: priority)
            );
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
            SfxCue cue = Cue(made, key: "hit", priority: SfxPriority.Low);
            SfxCue high = Cue(made, key: "high", priority: SfxPriority.High);
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { cue, high }, voiceCount: 1));
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
            monitor.SetPlayerAudition(forSfx: true, -6f, muted: false, solo: true);
            if (!Application.isBatchMode)
            {
                monitor.Show();
            }

            int steals = 0;
            void PlayStealAndDrop()
            {
                if (
                    player.Play("hit")
                    && player.TryObserveVoiceState(0, out _, out _, out _, out int victim, out _)
                    && player.Play("high")
                    && player.TryObserveVoiceState(0, out _, out _, out _, out _, out int stolenPlaybackId)
                    && victim != 0
                    && stolenPlaybackId == victim
                )
                {
                    ++steals;
                }

                voice.Stop();
                player.Play("missing");
            }

            for (int index = 0; index < WarmupCalls; ++index)
            {
                PlayStealAndDrop();
            }

            monitor.SamplePlayer();
            int before = monitor.Capture.Counts.notPlayable;
            steals = 0;
            long allocations = AllocationEvents(PlayStealAndDrop, MeasuredCalls);
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

            Assert.That(steals, Is.EqualTo(MeasuredCalls));
            Assert.That(monitor.Capture.Counts.notPlayable - before, Is.EqualTo(MeasuredCalls));
            Assert.That(allocations, Is.Zero, $"Play and steal allocated {allocations} times over {MeasuredCalls:N0} cue pairs.");
            Assert.That(samplingAllocations, Is.Zero, $"Sampling allocated {samplingAllocations} times over {MeasuredCalls:N0} reads.");
        }

        [Test]
        public void MixMonitorMeasuresOnlyTheSelectedPlayersPlayCalls()
        {
            using MadeObjects made = new();
            SfxCue cue = Cue(made, "hit");
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { cue }, voiceCount: 1));
            SfxPlayer other = SfxPlayer(made, SfxLibrary.Create(new[] { cue }, voiceCount: 1));
            DxAudioMixMonitorWindow monitor = made.Track(ScriptableObject.CreateInstance<DxAudioMixMonitorWindow>());
            monitor.SelectPlayers(player, null);
            AudioSource source = player.GetMonitorSource(0);
            Assert.That(source != null, Is.True);
            int started = 0;
            void PlayAndStop()
            {
                if (player.Play("hit"))
                {
                    ++started;
                }
                source.Stop();
            }

            _ = player.Play("missing");
            _ = player.Play((SfxCue)null, Vector3.zero);
            for (int index = 0; index < WarmupCalls; ++index)
            {
                PlayAndStop();
            }

            Action play = PlayAndStop;
            started = 0;
            Stopwatch elapsed = Stopwatch.StartNew();
            long unmeasured = AllocationEvents(play, MeasuredCalls);
            double unmeasuredMilliseconds = elapsed.Elapsed.TotalMilliseconds;
            Assert.That(started, Is.EqualTo(MeasuredCalls));
            monitor.SetPerformanceCapture(true);
            Assert.That(monitor.PlayAllocationBytes.HasValue, Is.False);
            for (int index = 0; index < WarmupCalls; ++index)
            {
                PlayAndStop();
            }
            started = 0;
            elapsed.Restart();
            long measured = AllocationEvents(play, MeasuredCalls);
            elapsed.Stop();

            monitor.SamplePlayer();
            Debug.Log($"Mix Monitor Play probe over {MeasuredCalls} single-voice plays. Off {unmeasuredMilliseconds:F3} ms. On {elapsed.Elapsed.TotalMilliseconds:F3} ms.");
            Assert.That(started, Is.EqualTo(MeasuredCalls));
            Assert.That(unmeasured, Is.Zero);
            Assert.That(measured, Is.Zero);
            Assert.That(monitor.PlayAllocationBytes, Is.Zero);

            const int PayloadBytes = 4_096;
            Action<SfxCue, SfxDropReason> allocate = static (_, _) => GC.KeepAlive(new byte[PayloadBytes]);
            allocate(null, default);
            other.CueDropped += allocate;
            Assert.That(other.Play("missing"), Is.False);
            GC.KeepAlive(new byte[PayloadBytes]);
            monitor.SamplePlayer();
            Assert.That(monitor.PlayAllocationBytes, Is.Zero);

            player.CueDropped += allocate;
            Assert.That(player.Play("missing"), Is.False);
            monitor.SamplePlayer();
            long bytes = monitor.PlayAllocationBytes.Value;
            Assert.That(bytes, Is.InRange(PayloadBytes, PayloadBytes + 128L));
            Assert.That(player.Play((SfxCue)null, Vector3.zero), Is.False);
            monitor.SamplePlayer();
            Assert.That(monitor.PlayAllocationBytes, Is.EqualTo(2L * bytes));

            player.CueDropped -= allocate;
            PlayAndStop();
            monitor.SamplePlayer();
            Assert.That(monitor.PlayAllocationBytes, Is.EqualTo(2L * bytes));

            monitor.ToggleCapture();
            player.CueDropped += allocate;
            Assert.That(player.Play("missing"), Is.False);
            monitor.SamplePlayer();
            Assert.That(monitor.PlayAllocationBytes, Is.EqualTo(2L * bytes));
            monitor.ToggleCapture();
            Assert.That(monitor.PlayAllocationBytes.HasValue, Is.False);
            PlayAndStop();
            monitor.SamplePlayer();
            Assert.That(monitor.PlayAllocationBytes, Is.Zero);

            made.DestroyNow(monitor);
            DxAudioMixMonitorWindow next = made.Track(ScriptableObject.CreateInstance<DxAudioMixMonitorWindow>());
            next.SelectPlayers(player, null);
            next.SetPerformanceCapture(true);
            PlayAndStop();
            next.SamplePlayer();
            Assert.That(next.PlayAllocationBytes, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MixMonitorTimesManagedDspInTheCurrentSampleInterval(bool master)
        {
            using MadeObjects made = new();
            GameObject owner = made.Track(new GameObject("DSP timing probe"));
            owner.SetActive(false);
            DspChainAsset chain = made.Track(DspChainAsset.Create(new[] { made.Track(DspDistortionAsset.Create()) }));
            Action<float[], int> process;
            if (master)
            {
                AudioMixBus bus = owner.AddComponent<AudioMixBus>();
                bus.Chain = chain;
                process = bus.ProcessBuffer;
            }
            else
            {
                AudioFilterHost host = owner.AddComponent<AudioFilterHost>();
                host.BusChain = chain;
                process = host.ProcessBuffer;
            }

            DxAudioMixMonitorWindow monitor = made.Track(ScriptableObject.CreateInstance<DxAudioMixMonitorWindow>());
            const int Channels = 2;
            float[] block = new float[512 * Channels];
            void ProcessSignal()
            {
                Array.Fill(block, 1f);
                process(block, Channels);
            }

            Action run = ProcessSignal;
            run();
            Assert.That(block[0], Is.GreaterThan(0f).And.LessThan(1f));
            for (int index = 0; index < WarmupCalls; ++index)
            {
                run();
            }

            Stopwatch elapsed = Stopwatch.StartNew();
            long unmeasured = AllocationEvents(run, WarmupCalls);
            double unmeasuredMilliseconds = elapsed.Elapsed.TotalMilliseconds;
            monitor.SetPerformanceCapture(true);
            for (int index = 0; index < WarmupCalls; ++index)
            {
                run();
            }
            monitor.SamplePlayer();
            elapsed.Restart();
            long allocations = AllocationEvents(run, WarmupCalls);
            elapsed.Stop();
            monitor.SamplePlayer();
            double dspMilliseconds = monitor.DspMilliseconds;
            Debug.Log(
                $"Mix Monitor DSP probe over {WarmupCalls} stereo blocks of 512 frames. Master {master}. Off {unmeasuredMilliseconds:F3} ms. "
                    + $"On {elapsed.Elapsed.TotalMilliseconds:F3} ms. DSP {dspMilliseconds:F3} ms."
            );
            Assert.That(unmeasured, Is.Zero);
            Assert.That(allocations, Is.Zero);
            Assert.That(dspMilliseconds, Is.GreaterThan(0d).And.LessThanOrEqualTo(elapsed.Elapsed.TotalMilliseconds));

            monitor.SamplePlayer();
            Assert.That(monitor.DspMilliseconds, Is.Zero);
            run();
            monitor.SamplePlayer();
            double frozenMilliseconds = monitor.DspMilliseconds;
            monitor.ToggleCapture();
            run();
            monitor.SamplePlayer();
            Assert.That(monitor.DspMilliseconds, Is.EqualTo(frozenMilliseconds));
            monitor.ToggleCapture();
            monitor.SamplePlayer();
            Assert.That(monitor.DspMilliseconds, Is.Zero);
        }

        [TestCase(2, 1f)]
        [TestCase(8, 1f)]
        [TestCase(2, 0.5f)]
        [TestCase(8, 0.5f)]
        public void AListenerPeakTapDoesNotAllocate(int channels, float gain)
        {
            using MadeObjects made = new();
            GameObject host = made.Track(new GameObject("listener tap probe"));
            host.SetActive(false);
            AudioMixBus bus = host.AddComponent<AudioMixBus>();
            Assert.That(bus.TryAttachPeakTap(out StereoPeakTap tap), Is.True);
            tap.AuditionGain = gain;
            float[] block = new float[512 * channels];
            int queuedBlocks = 0;
            bool lostAudio = false;
            float peak = 0f;
            void ProcessBlock()
            {
                block[0] = 0.5f;
                block[1] = -0.25f;
                bus.ProcessBuffer(block, channels);
                if (++queuedBlocks == 16)
                {
                    queuedBlocks = 0;
                    if (tap.TryReadTruePeak(out float value, out bool overrun))
                    {
                        peak = Math.Max(peak, value);
                    }

                    lostAudio = lostAudio || overrun;
                }
            }

            for (int index = 0; index < WarmupCalls; ++index)
            {
                ProcessBlock();
            }

            _ = tap.TryRead(out _, out _);
            peak = 0f;
            Debug.Log($"Measure listener tap allocations over {MeasuredCalls} blocks of 512 frames and {channels} channels.");
            long allocations = AllocationEvents(ProcessBlock, MeasuredCalls);
            Debug.Log($"Listener tap recorded {allocations} allocation events.");
            Assert.That(tap.TryRead(out float left, out float right), Is.True);
            Assert.That(left, Is.EqualTo(0.5f * gain));
            Assert.That(right, Is.EqualTo(0.25f * gain));
            Assert.That(peak, Is.EqualTo(0.5f * gain));
            Assert.That(lostAudio, Is.False);
            Assert.That(allocations, Is.Zero);
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
