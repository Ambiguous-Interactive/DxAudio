namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// Keeps the Unity attachment honest: a chain reaches its voice, and a reused voice starts clean.
    /// </summary>
    /// <remarks>
    /// Synthetic blocks use the same processing path as Unity's audio thread. Only one caller can drive a host at a time.
    /// </remarks>
    public sealed class DspHostTests
    {
        private static bool TryReadThreadCycles(out ulong cycles)
        {
#if UNITY_EDITOR_WIN
            return QueryThreadCycleTime(GetCurrentThread(), out cycles);
#else
            cycles = 0;
            return false;
#endif
        }

#if UNITY_EDITOR_WIN
        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern uint GetCurrentProcessId();

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern void GetCurrentProcessorNumberEx(out ProcessorNumber processor);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryThreadCycleTime(IntPtr thread, out ulong cycles);
#endif

        private static DspChainAsset Chain(MadeObjects made, params DspEffectAsset[] effects)
        {
            return made.Track(DspChainAsset.Create(effects));
        }

        /// <summary>A graph that runs one branch of <paramref name="effects" /> between an input and an output junction.</summary>
        private static DspGraphAsset Graph(MadeObjects made, params DspEffectAsset[] effects)
        {
            DspChainAsset branch = Chain(made, effects);
            return made.Track(
                DspGraphAsset.Create(
                    new[] { new DspGraphNode("in", null), new DspGraphNode("branch", branch), new DspGraphNode("out", null) },
                    new[] { new DspGraphEdge("in", "branch"), new DspGraphEdge("branch", "out") },
                    "in",
                    "out"
                )
            );
        }

        private static AudioFilterHost Host(MadeObjects made, DspChainAsset chain)
        {
            AudioFilterHost host = made.Track(new GameObject("host")).AddComponent<AudioFilterHost>();
            host.Chain = chain;
            return host;
        }

        /// <summary>The rate a host prepares with: the device rate, or its 48 kHz fallback when the device reports none.</summary>
        private static int OutputSampleRate()
        {
            return 0 < AudioSettings.outputSampleRate ? AudioSettings.outputSampleRate : 48_000;
        }

        private static AudioFilterHost HostOf(MonoBehaviour player)
        {
            IReadOnlyList<AudioFilterHost> hosts = player.GetComponentsInChildren<AudioFilterHost>();
            Assert.That(hosts.Count, Is.GreaterThan(0), "a player builds one host per voice");
            return hosts[0];
        }

        private static AudioFilterHost SyntheticVoiceHost(SfxPlayer player)
        {
            AudioFilterHost host = HostOf(player);
            // Disable before Play so Unity cannot start a callback on the host the test drives.
            host.enabled = false;
            return host;
        }

        private static SfxPlayer Player(MadeObjects made, SfxLibrary library)
        {
            made.Track(library);
            SfxPlayer player = made.Track(new GameObject("sfx")).AddComponent<SfxPlayer>();
            player.Library = library;
            return player;
        }

        private static float[] FullScale(int count)
        {
            float[] block = new float[count];
            for (int index = 0; index < count; ++index)
            {
                block[index] = 1f;
            }

            return block;
        }

        private static float[] Quiet(float level, int count)
        {
            float[] block = new float[count];
            for (int index = 0; index < count; ++index)
            {
                block[index] = level;
            }

            return block;
        }

        private static float Peak(float[] samples)
        {
            float peak = 0f;
            foreach (float sample in samples)
            {
                float magnitude = Mathf.Abs(sample);
                if (peak < magnitude)
                {
                    peak = magnitude;
                }
            }

            return peak;
        }

        private static AudioClip Clip(MadeObjects made)
        {
            return made.Track(AudioClip.Create("test clip", 4_000, 1, 8_000, stream: false));
        }

        [Test]
        public void AHostRunsItsChainOnASyntheticBlockWithoutAnAudioDevice()
        {
            using MadeObjects made = new();
            AudioFilterHost host = Host(made, Chain(made, made.Track(DspLimiterAsset.Create(ceilingDecibels: -6f, lookaheadSeconds: 0f))));
            float[] block = FullScale(host.ChannelCount * 64);

            host.OnAudioFilterRead(block, host.ChannelCount);

            Assert.That(Peak(block), Is.GreaterThan(0.45f), "a limiter never lifts, and it does pass the signal");
            Assert.That(Peak(block), Is.LessThanOrEqualTo(0.51f), "the ceiling holds");
            Assert.That(host.GainReductionDecibels, Is.LessThan(-4f), "a full-scale peak sits about 6 dB over the ceiling");
        }

        [Test]
        public void AHostRunsItsChainOnANarrowerBlock()
        {
            using MadeObjects made = new();
            AudioFilterHost host = Host(made, Chain(made, made.Track(DspLimiterAsset.Create(ceilingDecibels: -6f, lookaheadSeconds: 0f))));
            float[] block = FullScale(32);

            host.OnAudioFilterRead(block, 1);

            Assert.That(Peak(block), Is.LessThanOrEqualTo(0.51f), "a mono filter block must not bypass the chain");
            Assert.That(host.SkippedBlocks, Is.Zero);
        }

        [Test]
        public void AHostSkipsAndCountsABlockWiderThanItsLayout()
        {
            using MadeObjects made = new();
            AudioFilterHost host = Host(made, Chain(made, made.Track(DspLimiterAsset.Create(ceilingDecibels: -6f, lookaheadSeconds: 0f))));
            float[] block = FullScale(12);
            int tooWide = host.ChannelCount + 1;

            host.OnAudioFilterRead(block, tooWide);

            Assert.That(block, Is.EqualTo(FullScale(12)), "a chain prepared for a narrower block would index past its state");
            Assert.That(host.GainReductionDecibels, Is.Zero);
            Assert.That(host.SkippedBlocks, Is.EqualTo(1), "a block that never reached the chain must be visible, not silent");
        }

        [Test]
        public void AHostReportsTheLatencyItsChainsAdd()
        {
            using MadeObjects made = new();
            AudioFilterHost host = Host(made, Chain(made, made.Track(DspLimiterAsset.Create(lookaheadSeconds: 0.005f))));
            int expected = Mathf.RoundToInt(0.005f * OutputSampleRate());

            Assert.That(host.LatencySamples, Is.EqualTo(expected));
        }

        [Test]
        public void ADelayAssetAddsEchoInAHostChainOrGraph()
        {
            using MadeObjects made = new();
            DspDelayAsset delay = made.Track(DspDelayAsset.Create(DspDelay.MinimumDelaySeconds, feedback: 0f, echoLevel: 0.5f));
            AudioFilterHost chainHost = Host(made, Chain(made, delay));
            AudioFilterHost graphHost = Host(made, null);
            graphHost.Graph = Graph(made, delay);
            int delayFrames = Math.Max(1, (int)Math.Round((double)DspDelay.MinimumDelaySeconds * OutputSampleRate(), MidpointRounding.AwayFromZero));
            float[] chainBlock = new float[delayFrames + 1];
            float[] graphBlock = new float[delayFrames + 1];
            chainBlock[0] = 1f;
            graphBlock[0] = 1f;

            chainHost.OnAudioFilterRead(chainBlock, 1);
            graphHost.OnAudioFilterRead(graphBlock, 1);

            Assert.That(chainBlock[0], Is.EqualTo(1f));
            Assert.That(chainBlock[delayFrames], Is.EqualTo(0.5f));
            Assert.That(graphBlock, Is.EqualTo(chainBlock));
            Assert.That(chainHost.LatencySamples, Is.Zero);
            Assert.That(graphHost.LatencySamples, Is.Zero);
            Assert.That(chainHost.SkippedBlocks, Is.Zero);
            Assert.That(graphHost.SkippedBlocks, Is.Zero);
        }

        [Test]
        public void ANoiseGateAssetClosesQuietFramesInAHostChainOrGraph()
        {
            using MadeObjects made = new();
            DspNoiseGateAsset gate = made.Track(DspNoiseGateAsset.Create(-20f, attackSeconds: 0f, releaseSeconds: 0f));
            AudioFilterHost chainHost = Host(made, Chain(made, gate));
            AudioFilterHost graphHost = Host(made, null);
            graphHost.Graph = Graph(made, gate);
            float[] chainBlock = { 0.05f, 0.2f, 0.08f, 0.04f };
            float[] graphBlock = (float[])chainBlock.Clone();

            chainHost.OnAudioFilterRead(chainBlock, 1);
            graphHost.OnAudioFilterRead(graphBlock, 1);

            Assert.That(chainBlock, Is.EqualTo(new[] { 0f, 0.2f, 0.08f, 0f }));
            Assert.That(graphBlock, Is.EqualTo(chainBlock));
            Assert.That(chainHost.LatencySamples, Is.Zero);
            Assert.That(graphHost.LatencySamples, Is.Zero);
            Assert.That(chainHost.SkippedBlocks, Is.Zero);
            Assert.That(graphHost.SkippedBlocks, Is.Zero);
        }

        [Test]
        public void AChorusAssetReachesTheHostChainAndGraph()
        {
            using MadeObjects made = new();
            DspChorusAsset chorus = made.Track(DspChorusAsset.Create(depthSeconds: 0f, mix: 0.5f));
            AudioFilterHost chainHost = Host(made, Chain(made, chorus));
            AudioFilterHost graphHost = Host(made, null);
            graphHost.Graph = Graph(made, chorus);
            float[] chainBlock = { 1f, 0f, 0f, 0f };
            float[] graphBlock = (float[])chainBlock.Clone();

            chainHost.OnAudioFilterRead(chainBlock, 1);
            graphHost.OnAudioFilterRead(graphBlock, 1);

            Assert.That(chainBlock, Is.EqualTo(new[] { 0.5f, 0f, 0f, 0f }));
            Assert.That(graphBlock, Is.EqualTo(chainBlock));
            Assert.That(chainHost.LatencySamples, Is.Zero);
            Assert.That(graphHost.LatencySamples, Is.Zero);
            Assert.That(chainHost.SkippedBlocks, Is.Zero);
            Assert.That(graphHost.SkippedBlocks, Is.Zero);
        }

        [Test]
        public void AChainOrGraphCanApplyAnAuthoredTremolo()
        {
            using MadeObjects made = new();
            const float Rate = 10f;
            const float Depth = 0.75f;
            DspTremoloAsset tremolo = made.Track(DspTremoloAsset.Create(Rate, Depth));
            AudioFilterHost chainHost = Host(made, Chain(made, tremolo));
            AudioFilterHost graphHost = Host(made, null);
            graphHost.Graph = Graph(made, tremolo);
            float[] chainBlock = FullScale(16);
            float[] graphBlock = (float[])chainBlock.Clone();

            chainHost.OnAudioFilterRead(chainBlock, 1);
            graphHost.OnAudioFilterRead(graphBlock, 1);

            Assert.That(chainBlock[0], Is.EqualTo(1f));
            Assert.That(chainBlock[15], Is.EqualTo(1f - 2f * Depth * (15f * Rate / OutputSampleRate())).Within(0.000001f));
            Assert.That(graphBlock, Is.EqualTo(chainBlock));
            Assert.That(chainHost.LatencySamples, Is.Zero);
            Assert.That(graphHost.LatencySamples, Is.Zero);
            Assert.That(chainHost.SkippedBlocks, Is.Zero);
            Assert.That(graphHost.SkippedBlocks, Is.Zero);
        }

        [Test]
        public void AChainOrGraphCanApplyAnAuthoredPhaser()
        {
            using MadeObjects made = new();
            const float Rate = 2f;
            const float Depth = 0.6f;
            DspPhaserAsset phaser = made.Track(DspPhaserAsset.Create(Rate, Depth));
            AudioFilterHost chainHost = Host(made, Chain(made, phaser));
            AudioFilterHost graphHost = Host(made, null);
            graphHost.Graph = Graph(made, phaser);
            float[] chainBlock = { 1f, 0f, 0f, 0f };
            float[] graphBlock = (float[])chainBlock.Clone();
            float[] reference = (float[])chainBlock.Clone();
            DspPhaser effect = new() { RateHertz = Rate, Depth = Depth };
            Assert.That(effect.TryPrepare(OutputSampleRate(), reference.Length, 1), Is.True);
            effect.Process(reference, 0, reference.Length, 1);

            chainHost.OnAudioFilterRead(chainBlock, 1);
            graphHost.OnAudioFilterRead(graphBlock, 1);

            Assert.That(chainBlock[0], Is.LessThan(1f));
            Assert.That(chainBlock, Is.EqualTo(reference));
            Assert.That(graphBlock, Is.EqualTo(reference));
            Assert.That(chainHost.LatencySamples, Is.Zero);
            Assert.That(graphHost.LatencySamples, Is.Zero);
            Assert.That(chainHost.SkippedBlocks, Is.Zero);
            Assert.That(graphHost.SkippedBlocks, Is.Zero);
        }

        [Test]
        public void AChainOrGraphCanApplyAndResetAnAuthoredReverb()
        {
            using MadeObjects made = new();
            const float Decay = 1.3f;
            const float Wet = 0.4f;
            const int Frames = 128;
            DspReverbAsset asset = made.Track(DspReverbAsset.Create(Decay, Wet));
            DspChainAsset chain = Chain(made, asset);
            DspGraphAsset graph = Graph(made, asset);
            AudioFilterHost chainHost = Host(made, chain);
            AudioFilterHost graphHost = Host(made, null);
            graphHost.Graph = graph;
            int rate = OutputSampleRate();
            DspReverb effect = new() { DecaySeconds = Decay, WetLevel = Wet };
            Assert.That(effect.TryPrepare(rate, Frames, 1), Is.True);
            float[] chainBlock = new float[Frames];
            float[] graphBlock = new float[Frames];
            float[] reference = new float[Frames];
            float peakReflection = 0f;
            int blocks = rate / (5 * Frames) + 1;
            for (int block = 0; block < blocks; ++block)
            {
                Array.Clear(chainBlock, 0, Frames);
                Array.Clear(graphBlock, 0, Frames);
                Array.Clear(reference, 0, Frames);
                if (block == 0)
                {
                    chainBlock[0] = 1f;
                    graphBlock[0] = 1f;
                    reference[0] = 1f;
                }

                chainHost.OnAudioFilterRead(chainBlock, 1);
                graphHost.OnAudioFilterRead(graphBlock, 1);
                effect.Process(reference, 0, Frames, 1);
                Assert.That(chainBlock, Is.EqualTo(reference));
                Assert.That(graphBlock, Is.EqualTo(reference));

                for (int frame = block == 0 ? 1 : 0; frame < Frames; ++frame)
                {
                    peakReflection = Math.Max(peakReflection, Math.Abs(chainBlock[frame]));
                }
            }

            Assert.That(peakReflection, Is.Positive);
            chainHost.Chain = chain;
            graphHost.Graph = graph;
            Array.Clear(chainBlock, 0, Frames);
            Array.Clear(graphBlock, 0, Frames);
            chainHost.OnAudioFilterRead(chainBlock, 1);
            graphHost.OnAudioFilterRead(graphBlock, 1);
            Assert.That(chainBlock, Is.All.EqualTo(0f));
            Assert.That(graphBlock, Is.All.EqualTo(0f));
            Assert.That(chainHost.LatencySamples, Is.Zero);
            Assert.That(graphHost.LatencySamples, Is.Zero);
            Assert.That(chainHost.SkippedBlocks, Is.Zero);
            Assert.That(graphHost.SkippedBlocks, Is.Zero);
        }

        [Test]
        public void AChainOrGraphCanApplyAuthoredStereoWidth()
        {
            using MadeObjects made = new();
            DspStereoWidthAsset width = made.Track(ScriptableObject.CreateInstance<DspStereoWidthAsset>());
            JsonUtility.FromJsonOverwrite("{\"_width\":2}", width);
            AudioFilterHost chainHost = Host(made, Chain(made, width));
            AudioFilterHost graphHost = Host(made, null);
            graphHost.Graph = Graph(made, width);
            Assume.That(1 < chainHost.ChannelCount, "The editor must prepare stereo callbacks for this test.");
            float[] chainBlock = { 0.5f, 0f, 0.25f, 0.25f };
            float[] graphBlock = (float[])chainBlock.Clone();

            chainHost.OnAudioFilterRead(chainBlock, 2);
            graphHost.OnAudioFilterRead(graphBlock, 2);

            Assert.That(chainBlock, Is.EqualTo(new[] { 0.75f, -0.25f, 0.25f, 0.25f }));
            Assert.That(graphBlock, Is.EqualTo(chainBlock));
            Assert.That(chainHost.LatencySamples, Is.Zero);
            Assert.That(graphHost.LatencySamples, Is.Zero);
            Assert.That(chainHost.SkippedBlocks, Is.Zero);
            Assert.That(graphHost.SkippedBlocks, Is.Zero);
        }

        [TestCase(1, "Delay", false)]
        [TestCase(8, "Delay", false)]
        [TestCase(1, "Noise Gate", false)]
        [TestCase(8, "Noise Gate", false)]
        [TestCase(8, "Noise Gate", true)]
        [TestCase(1, "Chorus", false)]
        [TestCase(8, "Chorus", false)]
        [TestCase(1, "Tremolo", false)]
        [TestCase(8, "Tremolo", false)]
        [TestCase(1, "Phaser", false)]
        [TestCase(8, "Phaser", false)]
        [TestCase(8, "Phaser", true)]
        [TestCase(1, "Reverb", false)]
        [TestCase(8, "Reverb", false)]
        [TestCase(8, "Reverb", true)]
        [TestCase(1, "Stereo Width", false)]
        [TestCase(8, "Stereo Width", false)]
        public void DspChainsStayWithinTheRenderBudget(int voices, string effect, bool bursts)
        {
            const int Rate = 48_000;
            const int Frames = 1_024;
            const int Channels = 2;
            const int Passes = 240;
            bool delay = string.Equals(effect, "Delay", StringComparison.Ordinal);
            bool decayingTail = bursts && (string.Equals(effect, "Phaser", StringComparison.Ordinal) || string.Equals(effect, "Reverb", StringComparison.Ordinal));
            bool gateBursts = bursts && string.Equals(effect, "Noise Gate", StringComparison.Ordinal);
            DspChain[] baseline = new DspChain[voices];
            DspChain[] candidate = new DspChain[voices];
            for (int voice = 0; voice < voices; ++voice)
            {
                baseline[voice] = new DspChain();
                candidate[voice] = new DspChain();
                DspEffect stage = effect switch
                {
                    "Delay" => new DspDelay { DelaySeconds = DspDelay.MaximumDelaySeconds, Feedback = DspDelay.MaximumFeedback },
                    "Chorus" => new DspChorus { DepthSeconds = DspChorus.MaximumDepthSeconds, RateHertz = DspChorus.MaximumRateHertz },
                    "Tremolo" => new DspTremolo { RateHertz = DspTremolo.MaximumRateHertz, Depth = 1f },
                    "Phaser" => new DspPhaser { RateHertz = DspPhaser.MaximumRateHertz, Depth = 1f },
                    "Reverb" => new DspReverb { DecaySeconds = DspReverb.MaximumDecaySeconds, WetLevel = 1f },
                    "Stereo Width" => new DspStereoWidth { Width = DspStereoWidth.MaximumWidth },
                    _ => new DspNoiseGate(),
                };
                candidate[voice].Add(stage);
                Assert.That(baseline[voice].TryPrepare(Rate, Frames, Channels), Is.True);
                Assert.That(candidate[voice].TryPrepare(Rate, Frames, Channels), Is.True);
            }

            float[] input = new float[Frames * Channels];
            for (int frame = 0; frame < Frames; ++frame)
            {
                float amplitude = decayingTail && 64 <= frame ? 0f : 0.1f;
                if (gateBursts && frame % 128 < 64)
                {
                    amplitude = 0.0001f;
                }

                input[frame * Channels] = amplitude * (float)Math.Sin(2d * Math.PI * frame / 64d);
                input[frame * Channels + 1] = amplitude * (float)Math.Cos(2d * Math.PI * frame / 64d);
            }

            float[] block = new float[input.Length];
            long Render(DspChain[] chains)
            {
                long ticks = 0;
                for (int voice = 0; voice < voices; ++voice)
                {
                    Array.Copy(input, block, input.Length);
                    long start = Stopwatch.GetTimestamp();
                    chains[voice].Process(block, 0, block.Length, Channels);
                    ticks += Stopwatch.GetTimestamp() - start;
                }

                return ticks;
            }

            long[] baselineTicks = new long[Passes];
            long[] candidateTicks = new long[Passes];
            RenderPass[] baselinePasses = new RenderPass[Passes];
            RenderPass[] candidatePasses = new RenderPass[Passes];
            bool haveCycles = true;
            void Measure(DspChain[] chains, long[] elapsed, RenderPass[] passes, int pass)
            {
                // Bracket the pass, including copies between voices, outside the existing DSP stopwatch windows.
                RenderPass context = default;
#if UNITY_EDITOR_WIN
                GetCurrentProcessorNumberEx(out context.StartCpu);
                context.StartTimestamp = Stopwatch.GetTimestamp();
#endif
                bool haveStart = TryReadThreadCycles(out ulong start);
                elapsed[pass] = Render(chains);
                bool haveEnd = TryReadThreadCycles(out ulong end);
#if UNITY_EDITOR_WIN
                context.EndTimestamp = Stopwatch.GetTimestamp();
                GetCurrentProcessorNumberEx(out context.EndCpu);
#endif
                haveCycles = haveCycles && haveStart && haveEnd && start <= end;
                context.Cycles = haveCycles ? end - start : 0;
                passes[pass] = context;
            }

            for (int pass = 0; pass < 64; ++pass)
            {
                Measure(baseline, baselineTicks, baselinePasses, 0);
                Measure(candidate, candidateTicks, candidatePasses, 0);
            }

            for (int pass = 0; pass < Passes; ++pass)
            {
                if (pass % 2 == 0)
                {
                    Measure(baseline, baselineTicks, baselinePasses, pass);
                    Measure(candidate, candidateTicks, candidatePasses, pass);
                }
                else
                {
                    Measure(candidate, candidateTicks, candidatePasses, pass);
                    Measure(baseline, baselineTicks, baselinePasses, pass);
                }
            }

            Array.Sort(baselineTicks, baselinePasses);
            Array.Sort(candidateTicks, candidatePasses);
            double tickMicroseconds = 1_000_000d / Stopwatch.Frequency;
            int p95 = Passes * 95 / 100 - 1;
            int p99 = (Passes * 99 + 99) / 100 - 1;
            double addedP95 = (candidateTicks[p95] - baselineTicks[p95]) * tickMicroseconds;
            int budgetPercent = delay ? 1 : 2;
            double budget = Frames * 10_000d * budgetPercent / Rate;
            string Distribution(long[] ticks)
            {
                double median = (ticks[(Passes - 1) / 2] + (double)ticks[Passes / 2]) * 0.5d * tickMicroseconds;
                return $"{median:F2}/{ticks[p95] * tickMicroseconds:F2}/{ticks[p99] * tickMicroseconds:F2}/{ticks[Passes - 1] * tickMicroseconds:F2}";
            }

            string CyclesAtElapsedRanks(RenderPass[] passes)
            {
                double median = (passes[(Passes - 1) / 2].Cycles + (double)passes[Passes / 2].Cycles) * 0.5d;
                return $"{median:0.#}/{passes[p95].Cycles}/{passes[p99].Cycles}/{passes[Passes - 1].Cycles}";
            }

            string threadCycles = haveCycles
                ? $" Pass thread cycles at elapsed ranks, baseline {CyclesAtElapsedRanks(baselinePasses)}, candidate {CyclesAtElapsedRanks(candidatePasses)}."
                : " Pass thread cycles unavailable.";
            string cpuPlacement = "";
#if UNITY_EDITOR_WIN
            string CpuSpan(RenderPass pass)
            {
                return $"{pass.StartCpu} to {pass.EndCpu}";
            }

            string TraceSpan(RenderPass pass)
            {
                return $"{pass.StartTimestamp}-{pass.EndTimestamp} ({CpuSpan(pass)})";
            }

            cpuPlacement =
                $" Candidate CPU at elapsed p50, p95, p99, max: {CpuSpan(candidatePasses[Passes / 2])}, "
                + $"{CpuSpan(candidatePasses[p95])}, {CpuSpan(candidatePasses[p99])}, {CpuSpan(candidatePasses[Passes - 1])}.";
            if (budget < addedP95)
            {
                TestContext.Out.WriteLine(
                    $"DxAudio DSP render: Windows trace for {effect} (bursts {bursts}), {voices} voices. "
                        + $"PID {GetCurrentProcessId()}, TID {GetCurrentThreadId()}, QPC frequency {Stopwatch.Frequency}. "
                        + $"Baseline p95 {TraceSpan(baselinePasses[p95])}. Candidate p50 {TraceSpan(candidatePasses[Passes / 2])}, "
                        + $"p95 {TraceSpan(candidatePasses[p95])}, p99 {TraceSpan(candidatePasses[p99])}, max {TraceSpan(candidatePasses[Passes - 1])}."
                );
            }
#endif
            TestContext.Out.WriteLine(
                $"DxAudio DSP render: {effect} (bursts {bursts}), {voices} stereo voices at {Rate} Hz, {Frames} frames, {Passes} passes on {SystemInfo.processorType}: "
                    + $"p50/p95/p99/max in us, baseline {Distribution(baselineTicks)}, candidate {Distribution(candidateTicks)}, "
                    + $"added p95 {addedP95:F2} us, budget {budget:F2} us.{threadCycles}{cpuPlacement}"
            );
            Assert.That(addedP95, Is.LessThanOrEqualTo(budget), $"Added processing must fit within {budgetPercent}% of the block window.");
        }

        [Test]
        public void AChainAssetBuildsAFilterFromItsFields()
        {
            using MadeObjects made = new();
            DspFilterAsset asset = made.Track(DspFilterAsset.Create(DspFilterMode.BandPass, frequencyHz: 1_200f, quality: 2f));

            DspFilter filter = (DspFilter)asset.CreateEffect();

            Assert.That(asset.Describe(), Does.Contain("band pass").And.Contain("1200 Hz").And.Contain("Q 2"));
            Assert.That(filter.Mode, Is.EqualTo(DspFilterMode.BandPass));
            Assert.That(filter.FrequencyHz, Is.EqualTo(1_200f));
            Assert.That(filter.Quality, Is.EqualTo(2f));

            DspFilterAsset peak = made.Track(DspFilterAsset.Create(DspFilterMode.Peak, frequencyHz: float.NaN, quality: 0f, peakDecibels: 6f));
            Assert.That(peak.Describe(), Does.Contain("+6 dB"), "a peak names its lift in the inspector");
            Assert.That(peak.FrequencyHz, Is.EqualTo(DspFilter.DefaultFrequencyHz), "a non-number falls back rather than reaching the effect");
            Assert.That(peak.Quality, Is.EqualTo(DspFilter.MinimumQuality));

            DspFilterAsset bell = made.Track(DspFilterAsset.Create(DspFilterMode.Bell, peakDecibels: -9f));
            Assert.That(bell.Describe(), Does.Contain("bell").And.Contain("-9 dB"), "a bell names its cut in the inspector");
        }

        [Test]
        public void AChainAssetBuildsAPitchShifterFromItsFields()
        {
            using MadeObjects made = new();
            DspPitchShifterAsset asset = made.Track(DspPitchShifterAsset.Create(semitones: 7f, windowSeconds: 0.01f));

            DspPitchShifter shifter = (DspPitchShifter)asset.CreateEffect();

            Assert.That(asset.Describe(), Does.Contain("7 st").And.Contain("10 ms"));
            Assert.That(shifter.Semitones, Is.EqualTo(7f));
            Assert.That(shifter.WindowSeconds, Is.EqualTo(0.01f));

            DspPitchShifterAsset fallback = made.Track(DspPitchShifterAsset.Create(semitones: float.NaN, windowSeconds: float.PositiveInfinity));
            Assert.That(fallback.Semitones, Is.Zero, "a non-number falls back to unison rather than reaching the effect");
            Assert.That(fallback.WindowSeconds, Is.EqualTo(DspPitchShifter.DefaultWindowSeconds));
        }

        [Test]
        public void AHostRunsAPitchShifterAndReportsItsDelay()
        {
            using MadeObjects made = new();
            AudioFilterHost host = Host(made, Chain(made, made.Track(DspPitchShifterAsset.Create(semitones: 0f, windowSeconds: 0.005f))));
            int window = Mathf.RoundToInt(0.005f * OutputSampleRate());
            if (window < 2)
            {
                window = 2;
            }

            if (0 < (window & 1))
            {
                ++window;
            }

            int expected = (window / 2) + 1;
            float[] block = new float[host.ChannelCount * (expected + 16)];
            for (int channel = 0; channel < host.ChannelCount; ++channel)
            {
                block[channel] = 0.5f;
            }

            host.OnAudioFilterRead(block, host.ChannelCount);

            Assert.That(host.LatencySamples, Is.EqualTo(expected));
            Assert.That(block[(expected - 1) * host.ChannelCount], Is.Zero, "the step has not arrived one frame early");
            Assert.That(block[expected * host.ChannelCount], Is.EqualTo(0.5f).Within(0.0001f), "unison delays the block by the reported figure");
        }

        [Test]
        public void AChainAssetBuildsOneEffectPerAuthoredStageInOrder()
        {
            using MadeObjects made = new();
            DspChainAsset asset = DspChainAsset.Create(new DspEffectAsset[] { made.Track(DspBitCrusherAsset.Create()), null, made.Track(DspDistortionAsset.Create()) });

            DspChain chain = asset.CreateChain();

            Assert.That(chain.Count, Is.EqualTo(2), "an empty slot is skipped, not played as silence");
            Assert.That(chain[0], Is.TypeOf<DspBitCrusher>());
            Assert.That(chain[1], Is.TypeOf<DspDistortion>());
        }

        [Test]
        public void ACueCarriesItsChainToTheVoiceThatPlaysIt()
        {
            using MadeObjects made = new();
            DspChainAsset chain = Chain(made, made.Track(DspBitCrusherAsset.Create(bits: 4)));
            SfxCue cue = made.Track(SfxCue.Create(new[] { new SfxVariant(Clip(made)) }, chain: chain));
            SfxPlayer player = Player(made, SfxLibrary.Create(new[] { cue }, voiceCount: 1));

            Assert.That(player.Play(cue), Is.True);

            AudioFilterHost host = HostOf(player);
            Assert.That(host.Chain == chain);
            Assert.That(host.LiveChain.Count, Is.EqualTo(1));
        }

        [Test]
        public void ANewPlayStartsItsChainClean()
        {
            using MadeObjects made = new();
            // A slow release leaves the last play's gain in the chain long enough to see it if it survives.
            DspChainAsset chain = Chain(made, made.Track(DspLimiterAsset.Create(ceilingDecibels: -6f, lookaheadSeconds: 0f, releaseSeconds: 10f)));
            SfxCue cue = made.Track(SfxCue.Create(new[] { new SfxVariant(Clip(made)) }, minimumIntervalSeconds: 0f, chain: chain));
            SfxPlayer player = Player(made, SfxLibrary.Create(new[] { cue }, voiceCount: 1));
            AudioFilterHost host = SyntheticVoiceHost(player);
            Assert.That(player.Play(cue), Is.True);
            host.ProcessBuffer(FullScale(host.ChannelCount * 16), host.ChannelCount);
            float[] held = Quiet(0.1f, host.ChannelCount * 16);
            host.ProcessBuffer(held, host.ChannelCount);
            Assert.That(Peak(held), Is.LessThan(0.06f), "the held gain stays down for the rest of this play");

            player.StopAll();
            Assert.That(player.Play(cue), Is.True, "the pool holds one voice, so this is the same voice");

            float[] fresh = Quiet(0.1f, host.ChannelCount * 16);
            host.ProcessBuffer(fresh, host.ChannelCount);
            Assert.That(Peak(fresh), Is.EqualTo(0.1f).Within(0.001f), "a new play must not inherit the last play's gain");
        }

        [Test]
        public void ACueWithNoChainLeavesTheVoiceUnshaped()
        {
            using MadeObjects made = new();
            DspChainAsset chain = Chain(made, made.Track(DspLimiterAsset.Create()));
            SfxCue loud = made.Track(SfxCue.Create(new[] { new SfxVariant(Clip(made)) }, priority: SfxPriority.Low, chain: chain));
            SfxCue bare = made.Track(SfxCue.Create(new[] { new SfxVariant(Clip(made)) }, priority: SfxPriority.High));
            SfxPlayer player = Player(made, SfxLibrary.Create(new[] { loud, bare }, voiceCount: 1));
            Assert.That(player.Play(loud), Is.True);
            AudioFilterHost host = HostOf(player);

            Assert.That(player.Play(bare), Is.True, "the higher cue takes the one voice");

            Assert.That(host.LiveChain == null, Is.True, "a cue with no chain must not keep the last cue's chain");
        }

        [Test]
        public void ThePlayerBusRunsAfterTheCuesOwnChain()
        {
            using MadeObjects made = new();
            DspChainAsset cueChain = Chain(made, made.Track(DspDistortionAsset.Create(drive: 30f)));
            SfxCue cue = made.Track(SfxCue.Create(new[] { new SfxVariant(Clip(made)) }, chain: cueChain));
            SfxPlayer player = Player(made, SfxLibrary.Create(new[] { cue }, voiceCount: 1));
            player.BusChain = Chain(made, made.Track(DspLimiterAsset.Create(ceilingDecibels: -8f, lookaheadSeconds: 0f)));
            AudioFilterHost host = SyntheticVoiceHost(player);
            Assert.That(player.Play(cue), Is.True);
            Assert.That(host.LiveBusChain.Count, Is.EqualTo(1), "the bus reaches the voice");

            float[] block = FullScale(host.ChannelCount * 64);
            host.ProcessBuffer(block, host.ChannelCount);

            Assert.That(Peak(block), Is.LessThanOrEqualTo(0.41f), "a bus limiter after the shaper holds the output, one before it would not");
            Assert.That(host.GainReductionDecibels, Is.LessThan(-7f), "the bus's reduction is part of the reading");
            Assert.That(host.SkippedBlocks, Is.Zero);
        }

        [Test]
        public void ThePlayerBusShapesEveryVoiceAfterItsCuesOwnChain()
        {
            using MadeObjects made = new();
            SfxCue cue = made.Track(SfxCue.Create(new[] { new SfxVariant(Clip(made)) }));
            SfxPlayer player = Player(made, SfxLibrary.Create(new[] { cue }, voiceCount: 1));
            player.BusChain = Chain(made, made.Track(DspBitCrusherAsset.Create(bits: 1)));
            AudioFilterHost host = SyntheticVoiceHost(player);
            Assert.That(player.Play(cue), Is.True);
            float[] block = new float[host.ChannelCount * 4];
            for (int index = 0, blockLength = block.Length; index < blockLength; ++index)
            {
                block[index] = 0.3f;
            }

            host.ProcessBuffer(block, host.ChannelCount);

            Assert.That(Peak(block), Is.EqualTo(1f).Within(0.0001f), "one bit keeps only the ends of the range, so 0.3 leaves as 1");
        }

        [Test]
        public void ARateChangeRepreparesTheChain()
        {
            using MadeObjects made = new();
            AudioFilterHost host = Host(made, Chain(made, made.Track(DspLimiterAsset.Create(ceilingDecibels: -6f, lookaheadSeconds: 0f))));
            host.OnAudioFilterRead(FullScale(host.ChannelCount * 64), host.ChannelCount);

            host.OnAudioConfigurationChanged(deviceChanged: true);

            Assert.That(host.LiveChain.Count, Is.EqualTo(1), "the chain is rebuilt for the new device instead of kept at the old rate");
            float[] block = FullScale(host.ChannelCount * 64);
            host.OnAudioFilterRead(block, host.ChannelCount);
            Assert.That(Peak(block), Is.LessThanOrEqualTo(0.51f), "the rebuilt chain runs");
            Assert.That(host.SkippedBlocks, Is.Zero);
        }

        [Test]
        public void EachVoiceHoldsItsOwnChainAndTheListedOnesArePrepared()
        {
            using MadeObjects made = new();
            DspChainAsset chain = Chain(made, made.Track(DspCompressorAsset.Create()));
            SfxCue cue = made.Track(SfxCue.Create(new[] { new SfxVariant(Clip(made)) }, minimumIntervalSeconds: 0f, chain: chain));
            SfxPlayer player = Player(made, SfxLibrary.Create(new[] { cue }, voiceCount: 2));
            IReadOnlyList<AudioFilterHost> hosts = player.GetComponentsInChildren<AudioFilterHost>();
            Assert.That(hosts[0].IsPrepared(chain), Is.True, "a listed cue's chain is prepared with the pool, so its first play allocates nothing");

            Assert.That(player.Play(cue), Is.True);
            Assert.That(player.Play(cue), Is.True);

            Assert.That(ReferenceEquals(hosts[0].LiveChain, hosts[1].LiveChain), Is.False, "two voices must not share delay lines");
        }

        [Test]
        public void AMusicTrackRunsItsChainAndItsPlayerBus()
        {
            using MadeObjects made = new();
            DspChainAsset chain = Chain(made, made.Track(DspCompressorAsset.Create(thresholdDecibels: -20f)));
            MusicTrack track = made.Track(MusicTrack.Create(Clip(made), chain: chain));
            MusicPlayer player = made.Track(new GameObject("music")).AddComponent<MusicPlayer>();
            player.Library = made.Track(MusicLibrary.Create(new[] { track }, fadeInSeconds: 0f, fadeOutSeconds: 0f));
            player.BusChain = Chain(made, made.Track(DspLimiterAsset.Create()));

            player.Play(track);

            AudioFilterHost host = player.GetComponentInChildren<AudioFilterHost>();
            Assert.That(host != null);
            Assert.That(host.Chain == chain);
            Assert.That(host.LiveChain.Count, Is.EqualTo(1));
            Assert.That(host.LiveBusChain.Count, Is.EqualTo(1), "the music bus reaches the voice");
        }

        [Test]
        public void AGameCanWarmAReferenceCueBeforePlayingIt()
        {
            using MadeObjects made = new();
            DspChainAsset chain = Chain(made, made.Track(DspDistortionAsset.Create(drive: 4f)));
            SfxCue cue = made.Track(SfxCue.Create(new[] { new SfxVariant(Clip(made)) }, chain: chain));
            SfxPlayer player = Player(made, SfxLibrary.Create(new SfxCue[0], voiceCount: 2));

            Assert.That(player.Warm(cue), Is.True, "the pool warms what the library lists; a reference cue needs this call");

            IReadOnlyList<AudioFilterHost> hosts = player.GetComponentsInChildren<AudioFilterHost>();
            for (int index = 0; index < hosts.Count; ++index)
            {
                Assert.That(hosts[index].IsPrepared(chain), Is.True, "every voice is ready before the first play");
            }

            Assert.That(player.Play(cue), Is.True);
        }

        [Test]
        public void ACueWithAGraphRunsItsBranchThroughItsVoice()
        {
            using MadeObjects made = new();
            DspGraphAsset graph = Graph(made, made.Track(DspBitCrusherAsset.Create(bits: 1)));
            SfxCue cue = made.Track(SfxCue.CreateWithGraph(new[] { new SfxVariant(Clip(made)) }, graph: graph));
            SfxPlayer player = Player(made, SfxLibrary.Create(new[] { cue }, voiceCount: 1));
            AudioFilterHost host = SyntheticVoiceHost(player);
            Assert.That(player.Play(cue), Is.True);
            Assert.That(host.Graph == graph);
            Assert.That(host.LiveGraph != null);
            Assert.That(host.LiveGraph.IsPrepared, Is.True, "an unprepared graph would pass the block through");

            float[] block = Quiet(0.3f, host.ChannelCount * 4);
            host.ProcessBuffer(block, host.ChannelCount);

            Assert.That(Peak(block), Is.EqualTo(1f).Within(0.0001f), "one bit keeps only the ends of the range, so 0.3 leaves as 1");
            Assert.That(host.SkippedBlocks, Is.Zero);
        }

        [Test]
        public void AHostReportsTheDelayItsGraphAdds()
        {
            using MadeObjects made = new();
            AudioFilterHost host = made.Track(new GameObject("host")).AddComponent<AudioFilterHost>();
            host.Graph = Graph(made, made.Track(DspLimiterAsset.Create(lookaheadSeconds: 0.005f)));

            Assert.That(host.LiveGraph != null);
            Assert.That(host.LiveGraph.IsPrepared, Is.True, "only a prepared graph reports a delay");
            Assert.That(host.LatencySamples, Is.EqualTo(Mathf.RoundToInt(0.005f * OutputSampleRate())));
        }

        [Test]
        public void ACueWithNoGraphLeavesTheVoiceUnbranched()
        {
            using MadeObjects made = new();
            DspGraphAsset graph = Graph(made, made.Track(DspDistortionAsset.Create()));
            SfxCue branched = made.Track(SfxCue.CreateWithGraph(new[] { new SfxVariant(Clip(made)) }, graph: graph, priority: SfxPriority.Low));
            SfxCue bare = made.Track(SfxCue.Create(new[] { new SfxVariant(Clip(made)) }, priority: SfxPriority.High));
            SfxPlayer player = Player(made, SfxLibrary.Create(new[] { branched, bare }, voiceCount: 1));
            Assert.That(player.Play(branched), Is.True);
            AudioFilterHost host = HostOf(player);

            Assert.That(player.Play(bare), Is.True, "the higher cue takes the one voice");

            Assert.That(host.LiveGraph == null, Is.True, "a cue with no graph must not keep the last cue's graph");
        }

        [Test]
        public void EachVoiceHoldsItsOwnGraphAndTheListedOnesArePrepared()
        {
            using MadeObjects made = new();
            DspGraphAsset graph = Graph(made, made.Track(DspBitCrusherAsset.Create(bits: 4)));
            SfxCue cue = made.Track(SfxCue.CreateWithGraph(new[] { new SfxVariant(Clip(made)) }, graph: graph, minimumIntervalSeconds: 0f));
            SfxPlayer player = Player(made, SfxLibrary.Create(new[] { cue }, voiceCount: 2));
            IReadOnlyList<AudioFilterHost> hosts = player.GetComponentsInChildren<AudioFilterHost>();
            Assert.That(hosts[0].IsPrepared(graph), Is.True, "a listed cue's graph is prepared with the pool, so its first play allocates nothing");

            Assert.That(player.Play(cue), Is.True);
            Assert.That(player.Play(cue), Is.True);

            Assert.That(ReferenceEquals(hosts[0].LiveGraph, hosts[1].LiveGraph), Is.False, "two voices must not share a graph's buffers");
        }

        [Test]
        public void AGameCanWarmAReferenceCuesGraphBeforePlayingIt()
        {
            using MadeObjects made = new();
            DspGraphAsset graph = Graph(made, made.Track(DspDistortionAsset.Create(drive: 4f)));
            SfxCue cue = made.Track(SfxCue.CreateWithGraph(new[] { new SfxVariant(Clip(made)) }, graph: graph));
            SfxPlayer player = Player(made, SfxLibrary.Create(new SfxCue[0], voiceCount: 2));

            Assert.That(player.Warm(cue), Is.True, "the pool warms what the library lists; a reference cue needs this call");

            IReadOnlyList<AudioFilterHost> hosts = player.GetComponentsInChildren<AudioFilterHost>();
            for (int index = 0; index < hosts.Count; ++index)
            {
                Assert.That(hosts[index].IsPrepared(graph), Is.True, "every voice is ready before the first play");
            }

            Assert.That(player.Play(cue), Is.True);
            Assert.That(HostOf(player).LiveGraph != null);
            Assert.That(HostOf(player).LiveGraph.IsPrepared, Is.True);
        }

        [Test]
        public void AMusicTrackPreparesItsGraphOnItsVoice()
        {
            using MadeObjects made = new();
            DspGraphAsset graph = Graph(made, made.Track(DspBitCrusherAsset.Create(bits: 1)));
            MusicTrack track = made.Track(MusicTrack.CreateWithGraph(Clip(made), graph: graph));
            MusicPlayer player = made.Track(new GameObject("music")).AddComponent<MusicPlayer>();
            player.Library = made.Track(MusicLibrary.Create(new[] { track }, fadeInSeconds: 0f, fadeOutSeconds: 0f));

            player.Play(track);

            AudioFilterHost host = player.GetComponentInChildren<AudioFilterHost>();
            Assert.That(host != null);
            Assert.That(host.Graph == graph);
            Assert.That(host.LiveGraph?.IsPrepared, Is.True, "the music voice prepares the selected graph");

            // Music creates its hosts as it starts playback. Render its selected graph on a host without a source.
            AudioFilterHost synthetic = Host(made, null);
            synthetic.Graph = host.Graph;
            float[] block = Quiet(0.3f, synthetic.ChannelCount * 4);
            synthetic.ProcessBuffer(block, synthetic.ChannelCount);
            Assert.That(Peak(block), Is.EqualTo(1f).Within(0.0001f), "the selected graph runs the track's effect");
        }

        [Test]
        public void AHostCountsABlockNoPreparedGraphRan()
        {
            using MadeObjects made = new();
            DspGraphAsset broken = made.Track(
                DspGraphAsset.Create(new[] { new DspGraphNode("in", null), new DspGraphNode("out", null) }, new[] { new DspGraphEdge("in", "nowhere") }, "in", "out")
            );
            AudioFilterHost host = made.Track(new GameObject("host")).AddComponent<AudioFilterHost>();
            host.Graph = broken;
            float[] block = Quiet(0.3f, host.ChannelCount * 4);

            host.OnAudioFilterRead(block, host.ChannelCount);

            Assert.That(Peak(block), Is.EqualTo(0.3f).Within(0.0001f), "a graph that cannot build never touches the block");
            Assert.That(host.SkippedBlocks, Is.EqualTo(1), "a bypass nobody counts is the failure this package rejects");
        }

        [Test]
        public void ANewPlayClearsEveryBlockShapeItPrepared()
        {
            using MadeObjects made = new();
            // A slow release keeps the last play's gain long enough to see it if the other prepared width survives.
            DspEffectAsset held = made.Track(DspLimiterAsset.Create(ceilingDecibels: -6f, lookaheadSeconds: 0f, releaseSeconds: 10f));
            DspChainAsset chain = Chain(made, held);
            DspGraphAsset graph = Graph(made, held);
            SfxCue cue = made.Track(SfxCue.CreateWithGraph(new[] { new SfxVariant(Clip(made)) }, graph: graph, minimumIntervalSeconds: 0f, chain: chain));
            SfxPlayer player = Player(made, SfxLibrary.Create(new[] { cue }, voiceCount: 1));
            AudioFilterHost host = SyntheticVoiceHost(player);
            Assert.That(player.Play(cue), Is.True);
            Assume.That(1 < host.ChannelCount, "the voice needs two prepared widths to show a stale one");

            host.ProcessBuffer(FullScale(16), 1);
            player.StopAll();
            Assert.That(player.Play(cue), Is.True, "the pool holds one voice, so this is the same voice");
            host.ProcessBuffer(Quiet(0.1f, host.ChannelCount * 16), host.ChannelCount);
            float[] mono = Quiet(0.1f, 16);
            host.ProcessBuffer(mono, 1);

            Assert.That(Peak(mono), Is.EqualTo(0.1f).Within(0.001f), "a new play clears every width, not only the one its first block used");
        }

        private struct RenderPass
        {
            internal ulong Cycles;
#if UNITY_EDITOR_WIN
            internal long StartTimestamp;
            internal long EndTimestamp;
            internal ProcessorNumber StartCpu;
            internal ProcessorNumber EndCpu;
#endif
        }

#if UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential, Size = 4)]
        private struct ProcessorNumber
        {
            internal ushort Group;
            internal byte Number;

            public override string ToString()
            {
                return $"{Group}:{Number}";
            }
        }
#endif
    }
}
