namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class LibraryTests
    {
        private readonly List<Object> _made = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object made in _made)
            {
                Object.DestroyImmediate(made);
            }

            _made.Clear();
        }

        [Test]
        public void AnSfxLibraryFindsPlayableCuesByKey()
        {
            SfxCue step = Made(SfxCue.Create(new[] { Variant() }, key: "step"));
            SfxCue silent = Made(SfxCue.Create(new[] { new SfxVariant() }, key: "silent"));
            SfxLibrary library = Made(SfxLibrary.Create(new[] { step, silent, null }));

            Assert.That(library.TryGetCue("step", out SfxCue found), Is.True);
            Assert.That(found, Is.SameAs(step));
            Assert.That(library.TryGetCue("silent", out _), Is.False, "a cue with no clip is not listed");
            Assert.That(library.TryGetCue("missing", out _), Is.False);
            Assert.That(library.TryGetCue(" ", out _), Is.False);
        }

        [Test]
        public void AnSfxLibraryWarnsAboutADuplicateKey()
        {
            SfxCue first = Made(SfxCue.Create(new[] { Variant() }, key: "step"));
            SfxCue second = Made(SfxCue.Create(new[] { Variant() }, key: "step"));
            SfxLibrary library = Made(SfxLibrary.Create(new[] { first, second }));

            LogAssert.Expect(LogType.Warning, new Regex("share the key 'step'"));
            Assert.That(library.TryGetCue("step", out SfxCue found), Is.True);
            Assert.That(found, Is.SameAs(first), "the first listed wins");
        }

        [Test]
        public void AMusicLibraryFindsTracksByContextAndWarnsAboutUnreachableOnes()
        {
            MusicTrack town = Made(MusicTrack.Create(Clip(), context: "town"));
            MusicTrack nowhere = Made(MusicTrack.Create(Clip()));
            MusicLibrary library = Made(MusicLibrary.Create(new[] { town, nowhere }));

            LogAssert.Expect(LogType.Warning, new Regex("names no context"));
            Assert.That(library.TryGetTrack("town", out MusicTrack found), Is.True);
            Assert.That(found, Is.SameAs(town));
            Assert.That(library.TryGetTrack("cave", out _), Is.False);
        }

        [Test]
        public void SameMusicComparesWhatIsHeard()
        {
            AudioClip clip = Clip();
            MusicTrack town = Made(MusicTrack.Create(clip, context: "town"));
            MusicTrack shop = Made(MusicTrack.Create(clip, context: "shop"));
            MusicTrack quieter = Made(MusicTrack.Create(clip, context: "town", volumeScale: 0.5f));

            Assert.That(MusicTrack.SameMusic(town, shop), Is.True, "two contexts sharing a clip keep it playing");
            Assert.That(MusicTrack.SameMusic(town, quieter), Is.False);
            Assert.That(MusicTrack.SameMusic(town, null), Is.False);
            Assert.That(MusicTrack.SameMusic(null, null), Is.True);
        }

        [Test]
        public void ACueClampsItsStartAndIgnoresItForAnEngineLoop()
        {
            AudioClip clip = Clip(seconds: 1f);
            SfxVariant overshoot = new(clip, startTimeSeconds: 5f);
            SfxCue oneShot = Made(SfxCue.Create(new[] { overshoot }));
            SfxCue loop = Made(SfxCue.Create(new[] { overshoot }, loop: true));

            Assert.That(
                oneShot.PlaybackStartSecondsFor(overshoot),
                Is.LessThan(clip.length),
                "never at or past the end"
            );
            Assert.That(loop.PlaybackStartSecondsFor(overshoot), Is.Zero);
            Assert.That(oneShot.PitchFor(0.5f), Is.EqualTo(1f));
        }

        private T Made<T>(T made)
            where T : Object
        {
            _made.Add(made);
            return made;
        }

        private SfxVariant Variant()
        {
            return new SfxVariant(Clip());
        }

        private AudioClip Clip(float seconds = 0.5f)
        {
            const int Frequency = 8_000;
            int samples = Mathf.Max(1, (int)(seconds * Frequency));
            return Made(AudioClip.Create("test clip", samples, 1, Frequency, stream: false));
        }
    }
}
