namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// Keeps the Unity attachment honest: a chain reaches its voice, and a reused voice starts clean.
    /// </summary>
    /// <remarks>
    /// Every test drives <c>AudioFilterHost.OnAudioFilterRead</c> with a synthetic buffer, which is the call the engine
    /// makes, so no test needs an audio device or play mode.
    /// </remarks>
    public sealed class DspHostTests
    {
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
            int expected = Mathf.RoundToInt(0.005f * AudioSettings.outputSampleRate);

            Assert.That(host.LatencySamples, Is.EqualTo(expected));
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
        public void AReusedVoiceClearsTheChainItsLastPlayLeftBehind()
        {
            using MadeObjects made = new();
            DspChainAsset chain = Chain(made, made.Track(DspLimiterAsset.Create(ceilingDecibels: -6f, lookaheadSeconds: 0f)));
            DspChainAsset other = Chain(made, made.Track(DspBitCrusherAsset.Create(bits: 4)));
            SfxCue loud = made.Track(SfxCue.Create(new[] { new SfxVariant(Clip(made)) }, priority: SfxPriority.Low, chain: chain));
            SfxCue silent = made.Track(SfxCue.Create(new[] { new SfxVariant(Clip(made)) }, priority: SfxPriority.Medium));
            SfxCue again = made.Track(SfxCue.Create(new[] { new SfxVariant(Clip(made)) }, priority: SfxPriority.High, chain: other));
            SfxPlayer player = Player(made, SfxLibrary.Create(new[] { loud, silent, again }, voiceCount: 1));
            Assert.That(player.Play(loud), Is.True);
            AudioFilterHost host = HostOf(player);
            host.OnAudioFilterRead(FullScale(host.ChannelCount * 64), host.ChannelCount);
            Assert.That(host.GainReductionDecibels, Is.LessThan(-4f), "the first cue drove the limiter down");

            Assert.That(player.Play(silent), Is.True, "the pool holds one voice, so this is the same voice");
            Assert.That(host.LiveChain == null, Is.True, "a cue with no chain must not keep the last cue's chain");

            Assert.That(player.Play(again), Is.True);
            Assert.That(host.Chain == other);
            Assert.That(host.GainReductionDecibels, Is.Zero, "a reused voice must not inherit the last play's envelope");
        }

        [Test]
        public void ThePlayerBusRunsAfterTheCuesOwnChain()
        {
            using MadeObjects made = new();
            DspChainAsset cueChain = Chain(made, made.Track(DspDistortionAsset.Create(drive: 30f)));
            SfxCue cue = made.Track(SfxCue.Create(new[] { new SfxVariant(Clip(made)) }, chain: cueChain));
            SfxPlayer player = Player(made, SfxLibrary.Create(new[] { cue }, voiceCount: 1));
            player.BusChain = Chain(made, made.Track(DspLimiterAsset.Create(ceilingDecibels: -8f, lookaheadSeconds: 0f)));
            Assert.That(player.Play(cue), Is.True);
            AudioFilterHost host = HostOf(player);
            Assert.That(host.LiveBusChain.Count, Is.EqualTo(1), "the bus reaches the voice");

            float[] block = FullScale(host.ChannelCount * 64);
            host.OnAudioFilterRead(block, host.ChannelCount);

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
            Assert.That(player.Play(cue), Is.True);
            AudioFilterHost host = HostOf(player);
            float[] block = new float[host.ChannelCount * 4];
            for (int index = 0; index < block.Length; ++index)
            {
                block[index] = 0.3f;
            }

            host.OnAudioFilterRead(block, host.ChannelCount);

            Assert.That(Peak(block), Is.EqualTo(1f).Within(0.0001f), "one bit keeps only the ends of the range, so 0.3 leaves as 1");
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

        private static AudioFilterHost HostOf(MonoBehaviour player)
        {
            IReadOnlyList<AudioFilterHost> hosts = player.GetComponentsInChildren<AudioFilterHost>();
            Assert.That(hosts.Count, Is.GreaterThan(0), "a player builds one host per voice");
            return hosts[0];
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

        private static AudioClip Clip(MadeObjects made)
        {
            return made.Track(AudioClip.Create("test clip", 4_000, 1, 8_000, stream: false));
        }
    }
}
