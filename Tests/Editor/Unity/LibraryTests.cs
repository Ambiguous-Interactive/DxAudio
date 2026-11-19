namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class LibraryTests
    {
        [Test]
        public void AnSfxLibraryFindsPlayableCuesByKey()
        {
            using MadeObjects made = new();
            SfxCue step = made.Track(SfxCue.Create(new[] { Variant(made) }, key: "step"));
            SfxCue silent = made.Track(SfxCue.Create(new[] { new SfxVariant() }, key: "silent"));
            SfxLibrary library = made.Track(SfxLibrary.Create(new[] { step, silent, null }));

            Assert.That(library.TryGetCue("step", out SfxCue found), Is.True);
            Assert.That(found, Is.SameAs(step));
            Assert.That(library.TryGetCue("silent", out _), Is.False, "a cue with no clip is not listed");
            Assert.That(library.TryGetCue("missing", out _), Is.False);
            Assert.That(library.TryGetCue(" ", out _), Is.False);
        }

        [Test]
        public void AnSfxLibraryWarnsAboutADuplicateKey()
        {
            using MadeObjects made = new();
            SfxCue first = made.Track(SfxCue.Create(new[] { Variant(made) }, key: "step"));
            SfxCue second = made.Track(SfxCue.Create(new[] { Variant(made) }, key: "step"));
            SfxLibrary library = made.Track(SfxLibrary.Create(new[] { first, second }));

            LogAssert.Expect(LogType.Warning, new Regex("share the key 'step'"));
            Assert.That(library.TryGetCue("step", out SfxCue found), Is.True);
            Assert.That(found, Is.SameAs(first), "the first listed wins");
        }

        [Test]
        public void AMusicLibraryFindsTracksByContextAndWarnsAboutUnreachableOnes()
        {
            using MadeObjects made = new();
            MusicTrack town = made.Track(MusicTrack.Create(Clip(made), context: "town"));
            MusicTrack nowhere = made.Track(MusicTrack.Create(Clip(made)));
            MusicLibrary library = made.Track(MusicLibrary.Create(new[] { town, nowhere }));

            LogAssert.Expect(LogType.Warning, new Regex("names no context"));
            Assert.That(library.TryGetTrack("town", out MusicTrack found), Is.True);
            Assert.That(found, Is.SameAs(town));
            Assert.That(library.TryGetTrack("cave", out _), Is.False);
        }

        [Test]
        public void SameMusicComparesWhatIsHeard()
        {
            using MadeObjects made = new();
            AudioClip clip = Clip(made);
            MusicTrack town = made.Track(MusicTrack.Create(clip, context: "town"));
            MusicTrack shop = made.Track(MusicTrack.Create(clip, context: "shop"));
            MusicTrack quieter = made.Track(MusicTrack.Create(clip, context: "town", volumeScale: 0.5f));

            Assert.That(MusicTrack.SameMusic(town, shop), Is.True, "two contexts sharing a clip keep it playing");
            Assert.That(MusicTrack.SameMusic(town, quieter), Is.False);
            Assert.That(MusicTrack.SameMusic(town, null), Is.False);
            Assert.That(MusicTrack.SameMusic(null, null), Is.True);
        }

        [Test]
        public void ACueClampsItsStartAndIgnoresItForAnEngineLoop()
        {
            using MadeObjects made = new();
            AudioClip clip = Clip(made, seconds: 1f);
            SfxVariant overshoot = new(clip, startTimeSeconds: 5f);
            SfxCue oneShot = made.Track(SfxCue.Create(new[] { overshoot }));
            SfxCue loop = made.Track(SfxCue.Create(new[] { overshoot }, loop: true));

            Assert.That(oneShot.PlaybackStartSecondsFor(overshoot), Is.LessThan(clip.length), "never at or past the end");
            Assert.That(loop.PlaybackStartSecondsFor(overshoot), Is.Zero);
            Assert.That(oneShot.PitchFor(0.5f), Is.EqualTo(1f));
        }

        [Test]
        public void ARejectedPlayLeavesTheSeededTakeAndPitchSequenceIntact()
        {
            const int Seed = 81;
            const float Jitter = 0.08f;
            using MadeObjects made = new();
            SfxVariant[] variants = { Variant(made), new SfxVariant(), Variant(made), Variant(made) };
            SfxCue empty = made.Track(SfxCue.Create(new[] { new SfxVariant() }));
            SfxCue cue = made.Track(SfxCue.CreateWithVariation(variants, new SfxVariation(false, 0f, 0f, -12f, 0f), pitchJitter: Jitter, minimumIntervalSeconds: 0f));
            SfxPlayer player = made.Track(new GameObject("variation player")).AddComponent<SfxPlayer>();
            player.Library = made.Track(SfxLibrary.Create(new[] { cue }, voiceCount: 1, drawSeed: Seed));
            AudioSource voice = player.GetComponentInChildren<AudioSource>();
            SfxPlaybackResolver preview = new();
            preview.Reset(Seed);
            SfxSelection<SfxCue> legacy = new();
            System.Random originalRandom = new(Seed);
            for (int index = 0; index < 12; index++)
            {
                Assert.That(player.Play(empty), Is.False);
                Assert.That(preview.TryResolve(empty, out _), Is.False);
                Assert.That(player.Play(cue), Is.True);
                Assert.That(preview.TryResolve(cue, out SfxPlayback play), Is.True);
                Assert.That(legacy.TryNext(cue, 3, (float)originalRandom.NextDouble(), out int take), Is.True);
                Assert.That(cue.TryGetPlayableVariant(take, out SfxVariant expected), Is.True);
                float pitch = 1f + Mathf.Lerp(-Jitter, Jitter, (float)originalRandom.NextDouble());
                Assert.That(voice.clip == expected.Clip, Is.True);
                Assert.That(voice.clip == play.Variant.Clip, Is.True);
                Assert.That(voice.pitch, Is.EqualTo(pitch).Within(0.000001f));
                Assert.That(voice.pitch, Is.EqualTo(play.Pitch));
                Assert.That(voice.volume, Is.EqualTo(play.Volume));
                voice.Stop();
            }
        }

        [Test]
        public void ALoopFollowsTheLeaderWhenItsTakeIsKnown()
        {
            using MadeObjects made = new();
            SfxVariant[] variants = { new(Clip(made), 0.1f), new(Clip(made), 0.2f) };
            SfxCue lead = made.Track(SfxCue.Create(variants));
            SfxCue loop = made.Track(SfxCue.Create(variants, loop: true, followsTakeOf: lead));
            SfxPlaybackResolver resolver = new();
            resolver.Reset(81);

            Assert.That(resolver.TryResolve(loop, out SfxPlayback fallback), Is.True);
            Assert.That(fallback.FollowedTake, Is.False);
            Assert.That(resolver.TryResolve(lead, out SfxPlayback leader), Is.True);
            Assert.That(resolver.TryResolve(loop, out SfxPlayback follower), Is.True);
            Assert.That(follower.FollowedTake, Is.True);
            Assert.That(follower.Variant, Is.SameAs(leader.Variant));
            Assert.That(leader.StartSeconds, Is.EqualTo(leader.Variant.StartTimeSeconds));
            Assert.That(follower.StartSeconds, Is.Zero);
        }

        [Test]
        public void APlayerUsesIndependentInjectedStreamsForPlayback()
        {
            const int Seed = 81;
            using MadeObjects made = new();
            SfxCue cue = made.Track(SfxCue.CreateWithVariation(new[] { Variant(made), Variant(made) }, new SfxVariation(true, -1200f, 1200f, -6f, 0f)));
            SfxPlayer player = made.Track(new GameObject("variation player")).AddComponent<SfxPlayer>();
            FixedRandom takeAndPitch = new(0.75);
            FixedRandom level = new(0.0);
            player.RandomFactory = seed => seed == Seed ? takeAndPitch : level;
            player.Library = made.Track(SfxLibrary.Create(new[] { cue }, voiceCount: 1, drawSeed: Seed));

            Assert.That(player.Play(cue), Is.True);
            AudioSource voice = player.GetComponentInChildren<AudioSource>();
            Assert.That(voice.clip == cue.Variants[1].Clip, Is.True);
            Assert.That(voice.pitch, Is.EqualTo(1.414214f).Within(0.000001f));
            Assert.That(voice.volume, Is.EqualTo(0.501187f).Within(0.000001f));
            Assert.That(takeAndPitch.Draws, Is.EqualTo(2));
            Assert.That(level.Draws, Is.EqualTo(1));
            Assert.That(player.Play(cue), Is.False);
            Assert.That(takeAndPitch.Draws, Is.EqualTo(2));
            Assert.That(level.Draws, Is.EqualTo(1));
        }

        private sealed class FixedRandom : ISfxRandom
        {
            private readonly double _value;

            internal FixedRandom(double value)
            {
                _value = value;
            }

            internal int Draws { get; private set; }

            public double NextDouble()
            {
                Draws++;
                return _value;
            }
        }

        private static SfxVariant Variant(MadeObjects made)
        {
            return new SfxVariant(Clip(made));
        }

        private static AudioClip Clip(MadeObjects made, float seconds = 0.5f)
        {
            const int Frequency = 8_000;
            int samples = Mathf.Max(1, (int)(seconds * Frequency));
            return made.Track(AudioClip.Create("test clip", samples, 1, Frequency, stream: false));
        }
    }
}
