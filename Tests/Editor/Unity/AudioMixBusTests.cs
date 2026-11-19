namespace WallstopStudios.DxAudio.Tests.Editor
{
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Keeps the mix bus honest: it sees the sum, holds a ceiling over it, and adds only its chain's delay.
    /// </summary>
    /// <remarks>
    /// Unity hands a filter on the listener's object the mixed output of every source. No test can capture that mix, so
    /// each test sums the blocks itself and passes the sum to <c>AudioMixBus.ProcessBuffer</c>, which is the same path
    /// the engine drives.
    /// </remarks>
    public sealed class AudioMixBusTests
    {
        [Test]
        public void AMixBusHoldsACeilingThatTheSumOfTwoVoicesPasses()
        {
            using MadeObjects made = new();
            DspChainAsset ceiling = Chain(made, made.Track(DspLimiterAsset.Create(ceilingDecibels: -6f, lookaheadSeconds: 0f)));
            AudioFilterHost first = Host(made, ceiling);
            AudioFilterHost second = Host(made, ceiling);
            int channels = first.ChannelCount;
            float[] left = FullScale(channels * 64);
            float[] right = FullScale(channels * 64);

            first.OnAudioFilterRead(left, channels);
            second.OnAudioFilterRead(right, channels);
            float[] mix = Sum(left, right);

            Assert.That(Peak(left), Is.LessThanOrEqualTo(0.51f), "each voice holds the ceiling on its own");
            Assert.That(Peak(mix), Is.GreaterThan(0.95f), "two voices that each held -6 dB still sum above it, which no per-voice bus can see");

            AudioMixBus bus = Bus(made, ceiling);
            bus.ProcessBuffer(mix, channels);

            Assert.That(Peak(mix), Is.LessThanOrEqualTo(0.51f), "the mix bus holds the ceiling of the sum");
            Assert.That(bus.SkippedBlocks, Is.Zero);
        }

        [Test]
        public void AMixBusAddsOnlyItsChainsLatency()
        {
            using MadeObjects made = new();
            AudioMixBus bus = Bus(made, Chain(made, made.Track(DspLimiterAsset.Create(lookaheadSeconds: 0.005f))));
            int lookahead = Mathf.RoundToInt(0.005f * bus.PreparedSampleRate);
            int channels = bus.ChannelCount;
            float[] block = new float[channels * (lookahead + 16)];
            for (int channel = 0; channel < channels; ++channel)
            {
                block[channel] = 0.25f;
            }

            bus.ProcessBuffer(block, channels);

            Assert.That(bus.LatencySamples, Is.EqualTo(lookahead));
            Assert.That(block[(lookahead - 1) * channels], Is.Zero, "the step has not arrived one frame early");
            Assert.That(block[lookahead * channels], Is.EqualTo(0.25f).Within(0.0001f), "the bus delays by its chain's lookahead and no block of its own");
        }

        [Test]
        public void AMixBusPassesTheMixWhenNoChainRuns()
        {
            using MadeObjects made = new();
            AudioMixBus bus = Bus(made, null);
            float[] block = Quiet(0.3f, 32);

            bus.ProcessBuffer(block, 2);

            Assert.That(block, Is.EqualTo(Quiet(0.3f, 32)), "a bus with no chain leaves the mix untouched");
            Assert.That(bus.SkippedBlocks, Is.Zero, "nothing was asked to run, so nothing was skipped");
        }

        [Test]
        public void AMixBusSkipsAndCountsABlockWiderThanItsLayout()
        {
            using MadeObjects made = new();
            AudioMixBus bus = Bus(made, Chain(made, made.Track(DspLimiterAsset.Create(ceilingDecibels: -6f, lookaheadSeconds: 0f))));
            float[] block = FullScale(12);

            bus.ProcessBuffer(block, bus.ChannelCount + 1);

            Assert.That(block, Is.EqualTo(FullScale(12)), "a chain prepared for a narrower block would index past its state");
            Assert.That(bus.GainReductionDecibels, Is.Zero);
            Assert.That(bus.SkippedBlocks, Is.EqualTo(1), "a block that never reached the chain must be visible, not silent");
        }

        [Test]
        public void ClearingAndRestoringTheChainStartsItClean()
        {
            using MadeObjects made = new();
            // A slow release leaves the last block's gain in the chain long enough to see it if it survives.
            DspChainAsset chain = Chain(made, made.Track(DspLimiterAsset.Create(ceilingDecibels: -6f, lookaheadSeconds: 0f, releaseSeconds: 10f)));
            AudioMixBus bus = Bus(made, chain);
            int channels = bus.ChannelCount;

            bus.ProcessBuffer(FullScale(channels * 16), channels);
            float[] held = Quiet(0.1f, channels * 16);
            bus.ProcessBuffer(held, channels);
            Assert.That(Peak(held), Is.LessThan(0.06f), "the held gain stays down for the rest of this bus");

            bus.Chain = null;
            bus.Chain = chain;
            float[] fresh = Quiet(0.1f, channels * 16);
            bus.ProcessBuffer(fresh, channels);

            Assert.That(Peak(fresh), Is.EqualTo(0.1f).Within(0.001f), "a restored chain must not inherit the last block's gain");
        }

        [Test]
        public void SettingTheRunningChainAgainKeepsItsState()
        {
            using MadeObjects made = new();
            DspChainAsset chain = Chain(made, made.Track(DspLimiterAsset.Create(ceilingDecibels: -6f, lookaheadSeconds: 0f, releaseSeconds: 10f)));
            AudioMixBus bus = Bus(made, chain);
            int channels = bus.ChannelCount;
            bus.ProcessBuffer(FullScale(channels * 16), channels);

            bus.Chain = chain;
            float[] held = Quiet(0.1f, channels * 16);
            bus.ProcessBuffer(held, channels);

            Assert.That(Peak(held), Is.LessThan(0.06f), "a scene that sets the same bus again must not restart its release");
        }

        [Test]
        public void ARateChangeRepreparesTheChain()
        {
            using MadeObjects made = new();
            AudioMixBus bus = Bus(made, Chain(made, made.Track(DspLimiterAsset.Create(ceilingDecibels: -6f, lookaheadSeconds: 0f))));
            int channels = bus.ChannelCount;
            bus.ProcessBuffer(FullScale(channels * 64), channels);
            DspChain before = bus.LiveChain;

            bus.OnAudioConfigurationChanged(deviceChanged: true);

            Assert.That(bus.LiveChain, Is.Not.SameAs(before), "a chain sized for the old rate must not survive a device change");
            Assert.That(bus.LiveChain.Count, Is.EqualTo(1), "the chain is rebuilt for the new device instead of kept at the old rate");
            float[] block = FullScale(channels * 64);
            bus.ProcessBuffer(block, channels);
            Assert.That(Peak(block), Is.LessThanOrEqualTo(0.51f), "the rebuilt chain runs");
            Assert.That(bus.SkippedBlocks, Is.Zero);
        }

        [Test]
        public void InitializePreparesTheSavedChain()
        {
            using MadeObjects made = new();
            DspChainAsset chain = Chain(made, made.Track(DspBitCrusherAsset.Create(bits: 1)));
            AudioMixBus bus = made.Track(new GameObject("mix bus")).AddComponent<AudioMixBus>();
            SerializedObject serialized = new(bus);
            serialized.FindProperty("_chain").objectReferenceValue = chain;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            bus.Initialize();

            Assert.That(bus.LiveChain != null, Is.True, "a chain saved on the object must be prepared when it loads");
            float[] block = Quiet(0.3f, bus.ChannelCount * 4);
            bus.ProcessBuffer(block, bus.ChannelCount);
            Assert.That(Peak(block), Is.EqualTo(1f).Within(0.0001f), "one bit keeps only the ends of the range, so 0.3 leaves as 1");
        }

        [Test]
        public void AMixBusNamesTheListenerItNeeds()
        {
            using MadeObjects made = new();
            AudioMixBus bus = Bus(made, null);
            Assert.That(bus.IsOnListener, Is.False, "a bus on any other object receives no block");

            made.Track(bus.gameObject.AddComponent<AudioListener>());

            Assert.That(bus.IsOnListener, Is.True);
        }

        [Test]
        public void AMixBusPreparesAChainBeforeItRuns()
        {
            using MadeObjects made = new();
            DspChainAsset chain = Chain(made, made.Track(DspCompressorAsset.Create(thresholdDecibels: -20f)));

            AudioMixBus bus = Bus(made, null);
            bus.Warm(chain);
            Assert.That(bus.IsPrepared(chain), Is.True, "a warmed chain is ready, so switching to it on the audio path allocates nothing");

            bus.Chain = chain;

            Assert.That(bus.LiveChain.Count, Is.EqualTo(1));
            Assert.That(bus.SkippedBlocks, Is.Zero);
        }

        private static DspChainAsset Chain(MadeObjects made, params DspEffectAsset[] effects)
        {
            return made.Track(DspChainAsset.Create(effects));
        }

        private static AudioFilterHost Host(MadeObjects made, DspChainAsset chain)
        {
            AudioFilterHost host = made.Track(new GameObject("host")).AddComponent<AudioFilterHost>();
            host.Chain = chain;
            return host;
        }

        private static AudioMixBus Bus(MadeObjects made, DspChainAsset chain)
        {
            AudioMixBus bus = made.Track(new GameObject("mix bus")).AddComponent<AudioMixBus>();
            bus.Chain = chain;
            return bus;
        }

        private static float[] Sum(float[] left, float[] right)
        {
            float[] sum = new float[left.Length];
            for (int index = 0; index < left.Length; ++index)
            {
                sum[index] = left[index] + right[index];
            }

            return sum;
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
            for (int index = 0; index < samples.Length; ++index)
            {
                float magnitude = Mathf.Abs(samples[index]);
                if (peak < magnitude)
                {
                    peak = magnitude;
                }
            }

            return peak;
        }
    }
}
