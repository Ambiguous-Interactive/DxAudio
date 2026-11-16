namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DxAudio.Editor;

    public sealed class AuthoringTests
    {
        [Test]
        public void AnSfxLibraryCheckNamesEveryCueThePlayersCannotReach()
        {
            using MadeObjects made = new();
            SfxCue step = made.Track(SfxCue.Create(new[] { Variant(made) }, key: "step"));
            SfxCue repeated = made.Track(SfxCue.Create(new[] { Variant(made) }, key: "step"));
            SfxCue silent = made.Track(SfxCue.Create(new[] { new SfxVariant() }, key: "silent"));
            SfxCue anonymous = made.Track(SfxCue.Create(new[] { Variant(made) }, key: "  "));
            SfxLibrary library = made.Track(SfxLibrary.Create(new[] { step, repeated, silent, anonymous, null }));

            List<string> findings = DxAudioValidation.Validate(library);
            string messages = Joined(findings);

            Assert.That(findings, Has.Count.EqualTo(4));
            Assert.That(messages, Does.Contain("shares the key 'step'"));
            Assert.That(messages, Does.Contain("has no playable take"));
            Assert.That(messages, Does.Contain("has no key"));
            Assert.That(messages, Does.Contain("Slot 5 is empty"));
        }

        [Test]
        public void AnSfxLibraryCheckFollowsTheRuntimePastASilentDuplicateKey()
        {
            using MadeObjects made = new();
            SfxCue silent = made.Track(SfxCue.Create(new[] { new SfxVariant() }, key: "step"));
            SfxCue step = made.Track(SfxCue.Create(new[] { Variant(made) }, key: "step"));
            SfxLibrary library = made.Track(SfxLibrary.Create(new[] { silent, step }));

            List<string> findings = DxAudioValidation.Validate(library);

            Assert.That(Joined(findings), Does.Not.Contain("shares the key"));
            Assert.That(findings, Has.Count.EqualTo(1), "only the silent cue is a problem");
        }

        [Test]
        public void AnSfxLibraryCheckPassesALibraryEveryCueCanAnswer()
        {
            using MadeObjects made = new();
            SfxCue step = made.Track(SfxCue.Create(new[] { Variant(made) }, key: "step"));
            SfxLibrary library = made.Track(SfxLibrary.Create(new[] { step }));

            Assert.That(DxAudioValidation.Validate(library), Is.Empty);
        }

        [Test]
        public void AMusicLibraryCheckNamesEveryTrackNoContextReaches()
        {
            using MadeObjects made = new();
            MusicTrack town = made.Track(MusicTrack.Create(Clip(made), context: "town"));
            MusicTrack alsoTown = made.Track(MusicTrack.Create(Clip(made), context: "town"));
            MusicTrack nowhere = made.Track(MusicTrack.Create(Clip(made)));
            MusicTrack silent = made.Track(MusicTrack.Create(null, context: "cave"));
            MusicLibrary library = made.Track(MusicLibrary.Create(new[] { town, alsoTown, nowhere, silent, null }));

            List<string> findings = DxAudioValidation.Validate(library);
            string messages = Joined(findings);

            Assert.That(findings, Has.Count.EqualTo(4));
            Assert.That(messages, Does.Contain("shares the context 'town'"));
            Assert.That(messages, Does.Contain("names no context"));
            Assert.That(messages, Does.Contain("has no clip"));
            Assert.That(messages, Does.Contain("Slot 5 is empty"));
        }

        [Test]
        public void AMusicLibraryCheckFollowsTheRuntimePastASilentDuplicateContext()
        {
            using MadeObjects made = new();
            MusicTrack silent = made.Track(MusicTrack.Create(null, context: "town"));
            MusicTrack town = made.Track(MusicTrack.Create(Clip(made), context: "town"));
            MusicLibrary library = made.Track(MusicLibrary.Create(new[] { silent, town }));

            List<string> findings = DxAudioValidation.Validate(library);

            Assert.That(Joined(findings), Does.Not.Contain("shares the context"));
            Assert.That(findings, Has.Count.EqualTo(1), "only the track with no clip is a problem");
        }

        [Test]
        public void AMusicLibraryCheckPassesALibraryEveryContextReaches()
        {
            using MadeObjects made = new();
            MusicTrack town = made.Track(MusicTrack.Create(Clip(made), context: "town"));
            MusicLibrary library = made.Track(MusicLibrary.Create(new[] { town }));

            Assert.That(DxAudioValidation.Validate(library), Is.Empty);
        }

        [Test]
        public void AMusicLibraryCheckReportsAClipThatIgnoresTheTrackLoadType()
        {
            using MadeObjects made = new();
            MusicTrack boss = made.Track(MusicTrack.Create(Clip(made, 40f), context: "boss"));
            MusicLibrary library = made.Track(MusicLibrary.Create(new[] { boss }));

            string messages = Joined(DxAudioValidation.Validate(library));

            Assert.That(messages, Does.Contain("asks for a Streaming clip"));
            Assert.That(messages, Does.Contain("imported as DecompressOnLoad"));
        }

        [Test]
        public void AMusicTrackResolvesAutoFromTheClipLength()
        {
            using MadeObjects made = new();
            MusicTrack longTrack = made.Track(MusicTrack.Create(Clip(made, 40f)));
            MusicTrack shortTrack = made.Track(MusicTrack.Create(Clip(made, 20f)));
            MusicTrack forced = made.Track(
                MusicTrack.Create(Clip(made, 40f), clipLoadType: MusicClipLoadType.DecompressOnLoad)
            );
            MusicTrack streaming = made.Track(MusicTrack.Create(Clip(made, 5f, stream: true)));

            Assert.That(longTrack.ResolvedClipLoadType, Is.EqualTo(MusicClipLoadType.Streaming));
            Assert.That(shortTrack.ResolvedClipLoadType, Is.EqualTo(MusicClipLoadType.DecompressOnLoad));
            Assert.That(forced.ResolvedClipLoadType, Is.EqualTo(MusicClipLoadType.DecompressOnLoad), "explicit wins");
            Assert.That(MusicClipImport.Matches(forced), Is.True, "the clip already holds the asked-for type");
            Assert.That(MusicClipImport.AppliedLoadType(streaming.Clip), Is.EqualTo(MusicClipLoadType.Streaming));
            Assert.That(
                MusicClipImport.Matches(streaming),
                Is.False,
                "a streaming clip does not follow a track asking for decompression"
            );
        }

        [Test]
        public void ACueCheckWarnsThatALoopIgnoresItsStartOffsets()
        {
            using MadeObjects made = new();
            SfxVariant variant = new(Clip(made), startTimeSeconds: 0.2f);
            SfxCue loop = made.Track(SfxCue.Create(new[] { variant }, loop: true));
            SfxCue oneShot = made.Track(SfxCue.Create(new[] { variant }));

            Assert.That(Joined(DxAudioValidation.Validate(loop)), Does.Contain("start offsets are ignored"));
            Assert.That(DxAudioValidation.Validate(oneShot), Is.Empty);
        }

        [Test]
        public void ACueCheckWarnsWhenItFollowsItsOwnTakeNumber()
        {
            using MadeObjects made = new();
            SfxCue cue = made.Track(SfxCue.Create(new[] { Variant(made) }, key: "step"));
            SerializedObject serialized = new(cue);
            serialized.FindProperty("_followsTakeOf").objectReferenceValue = cue;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            List<string> findings = DxAudioValidation.Validate(cue);

            Assert.That(findings, Has.Count.EqualTo(1));
            Assert.That(Joined(findings), Does.Contain("follows its own take number"));
        }

        [Test]
        public void ADuckPreviewComposesTheMostRestrictiveOfEveryHeldDuck()
        {
            using MadeObjects made = new();
            MusicDuck first = made.Track(MusicDuck.Create(MusicDuckHolders.One, 0.5f));
            MusicDuck permissive = made.Track(MusicDuck.Create(MusicDuckHolders.One, 1f));
            MusicDuck filtered = made.Track(
                MusicDuck.Create(
                    MusicDuckHolders.Many,
                    volumeScale: 0.8f,
                    lowPassCutoffHz: 4_000f,
                    highPassCutoffHz: 300f
                )
            );
            MusicDuck quietest = made.Track(MusicDuck.Create(MusicDuckHolders.One, 0.4f));

            MusicAttenuation composed = MusicDuckEditor.Compose(
                first,
                new[] { permissive, filtered, quietest, null, first }
            );

            Assert.That(composed.volumeScale, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(composed.lowPassCutoffHz, Is.EqualTo(4_000f).Within(0.5f));
            Assert.That(composed.highPassCutoffHz, Is.EqualTo(300f).Within(0.5f));
            Assert.That(MusicDuckEditor.Compose(null, null).Equals(MusicAttenuation.Unattenuated), Is.True);
        }

        private static string Joined(List<string> findings)
        {
            return string.Join(" | ", findings);
        }

        private static SfxVariant Variant(MadeObjects made)
        {
            return new SfxVariant(Clip(made));
        }

        private static AudioClip Clip(MadeObjects made, float seconds = 0.5f, bool stream = false)
        {
            const int Frequency = 8_000;
            int samples = Mathf.Max(1, (int)(seconds * Frequency));
            return made.Track(AudioClip.Create("test clip", samples, 1, Frequency, stream));
        }
    }
}
