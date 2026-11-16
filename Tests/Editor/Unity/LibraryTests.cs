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

            Assert.That(
                oneShot.PlaybackStartSecondsFor(overshoot),
                Is.LessThan(clip.length),
                "never at or past the end"
            );
            Assert.That(loop.PlaybackStartSecondsFor(overshoot), Is.Zero);
            Assert.That(oneShot.PitchFor(0.5f), Is.EqualTo(1f));
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
