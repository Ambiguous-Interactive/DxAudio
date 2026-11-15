namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.DxAudio.Editor;

    public sealed class AuthoringTests
    {
        private readonly List<Object> _made = new();

        [TearDown]
        public void TearDown()
        {
            for (int index = 0; index < _made.Count; ++index)
            {
                Object.DestroyImmediate(_made[index]);
            }

            _made.Clear();
        }

        [Test]
        public void AnSfxLibraryCheckNamesEveryCueThePlayersCannotReach()
        {
            SfxCue step = Made(SfxCue.Create(new[] { Variant() }, key: "step"));
            SfxCue repeated = Made(SfxCue.Create(new[] { Variant() }, key: "step"));
            SfxCue silent = Made(SfxCue.Create(new[] { new SfxVariant() }, key: "silent"));
            SfxCue anonymous = Made(SfxCue.Create(new[] { Variant() }));
            SfxLibrary library = Made(SfxLibrary.Create(new[] { step, repeated, silent, anonymous, null }));

            string findings = Joined(DxAudioValidation.Validate(library));

            Assert.That(findings, Does.Contain("shares the key 'step'"));
            Assert.That(findings, Does.Contain("has no playable take"));
            Assert.That(findings, Does.Contain("has no key"));
            Assert.That(findings, Does.Contain("Slot 5 is empty"));
        }

        [Test]
        public void AnSfxLibraryCheckPassesALibraryEveryCueCanAnswer()
        {
            SfxCue step = Made(SfxCue.Create(new[] { Variant() }, key: "step"));
            SfxLibrary library = Made(SfxLibrary.Create(new[] { step }));

            Assert.That(DxAudioValidation.Validate(library), Is.Empty);
        }

        [Test]
        public void AMusicLibraryCheckNamesEveryTrackNoContextReaches()
        {
            MusicTrack town = Made(MusicTrack.Create(Clip(), context: "town"));
            MusicTrack alsoTown = Made(MusicTrack.Create(Clip(), context: "town"));
            MusicTrack nowhere = Made(MusicTrack.Create(Clip()));
            MusicTrack silent = Made(MusicTrack.Create(null, context: "cave"));
            MusicLibrary library = Made(MusicLibrary.Create(new[] { town, alsoTown, nowhere, silent, null }));

            string findings = Joined(DxAudioValidation.Validate(library));

            Assert.That(findings, Does.Contain("shares the context 'town'"));
            Assert.That(findings, Does.Contain("names no context"));
            Assert.That(findings, Does.Contain("has no clip"));
            Assert.That(findings, Does.Contain("Slot 5 is empty"));
        }

        [Test]
        public void ACueCheckWarnsThatALoopIgnoresItsStartOffsets()
        {
            SfxVariant variant = new(Clip(), startTimeSeconds: 0.2f);
            SfxCue loop = Made(SfxCue.Create(new[] { variant }, loop: true));
            SfxCue oneShot = Made(SfxCue.Create(new[] { variant }));

            Assert.That(Joined(DxAudioValidation.Validate(loop)), Does.Contain("start offsets are ignored"));
            Assert.That(DxAudioValidation.Validate(oneShot), Is.Empty);
        }

        [Test]
        public void ADuckPreviewComposesTheMostRestrictiveOfEveryHeldDuck()
        {
            MusicDuck first = Made(MusicDuck.Create(MusicDuckHolders.One, 0.5f));
            MusicDuck second = Made(
                MusicDuck.Create(
                    MusicDuckHolders.Many,
                    volumeScale: 0.8f,
                    lowPassCutoffHz: 4_000f,
                    highPassCutoffHz: 300f
                )
            );

            MusicAttenuation composed = MusicDuckEditor.Compose(first, new[] { second, null, first });

            Assert.That(composed.volumeScale, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(composed.lowPassCutoffHz, Is.EqualTo(4_000f).Within(0.5f));
            Assert.That(composed.highPassCutoffHz, Is.EqualTo(300f).Within(0.5f));
            Assert.That(MusicDuckEditor.Compose(null, null).Equals(MusicAttenuation.Unattenuated), Is.True);
        }

        private static string Joined(List<string> findings)
        {
            return string.Join(" | ", findings);
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
