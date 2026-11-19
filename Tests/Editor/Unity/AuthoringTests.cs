namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
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

            List<string> findings = DxAudioValidation.Validate(library);
            string messages = Joined(findings);

            Assert.That(findings, Has.Count.EqualTo(1));
            Assert.That(messages, Does.Contain("asks for a Streaming clip"));
            Assert.That(messages, Does.Contain("imported as DecompressOnLoad"));
        }

        [Test]
        public void AMusicTrackResolvesAutoFromTheClipLength()
        {
            using MadeObjects made = new();
            AudioClip fortySeconds = Clip(made, 40f);
            MusicTrack longTrack = made.Track(MusicTrack.Create(fortySeconds));
            MusicTrack shortTrack = made.Track(MusicTrack.Create(Clip(made, 20f)));
            MusicTrack forced = made.Track(MusicTrack.Create(fortySeconds, clipLoadType: MusicClipLoadType.DecompressOnLoad));

            Assert.That(longTrack.ResolvedClipLoadType, Is.EqualTo(MusicClipLoadType.Streaming));
            Assert.That(shortTrack.ResolvedClipLoadType, Is.EqualTo(MusicClipLoadType.DecompressOnLoad));
            Assert.That(forced.ResolvedClipLoadType, Is.EqualTo(MusicClipLoadType.DecompressOnLoad), "explicit wins");
            Assert.That(MusicClipImport.Matches(forced), Is.True, "the clip holds the asked-for type");
            Assert.That(MusicClipImport.Matches(longTrack), Is.False, "a decompressed clip does not follow a track that wants streaming");
        }

        [Test]
        public void ApplyingALoadTypeWritesTheClipImportSettings()
        {
            const string SourceGuid = "5081b21fa3e1420dbfaac900a77b4b24";
            string source = AssetDatabase.GUIDToAssetPath(SourceGuid);
            string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);

            using MadeObjects made = new();
            // A name collision makes Unity create a suffixed folder, so the returned GUID is the folder this test owns.
            string folder = AssetDatabase.GUIDToAssetPath(AssetDatabase.CreateFolder("Assets", "DxAudioLoadTypeTest"));
            string destination = folder + "/TestTone.wav";
            try
            {
                Assert.That(source.Length, Is.Not.Zero, "the fixture clip is missing");
                // A copy gets its own GUID, so changing its importer cannot touch the committed fixture.
                System.IO.File.Copy(System.IO.Path.Combine(projectRoot, source), System.IO.Path.Combine(projectRoot, destination), overwrite: true);
                AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceUpdate);

                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(destination);
                Assert.That(MusicClipImport.TryApply(clip, MusicClipLoadType.Streaming), Is.True);
                AudioClip streamed = AssetDatabase.LoadAssetAtPath<AudioClip>(destination);
                Assert.That(MusicClipImport.TryGetAppliedLoadType(streamed, out MusicClipLoadType applied), Is.True);
                Assert.That(applied, Is.EqualTo(MusicClipLoadType.Streaming), "the importer took the write");
                Assert.That(MusicClipImport.Matches(made.Track(MusicTrack.Create(streamed, clipLoadType: MusicClipLoadType.Streaming))), Is.True);

                Assert.That(MusicClipImport.TryApply(streamed, MusicClipLoadType.Auto), Is.False, "Auto names no concrete type");
                AudioClip generated = made.Track(AudioClip.Create("generated", 800, 1, 8_000, false));
                Assert.That(MusicClipImport.TryApply(generated, MusicClipLoadType.Streaming), Is.False, "a generated clip has no importer");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void AnEaseStoredBeforeTheSentinelKeepsItsCurve()
        {
            using MadeObjects made = new();
            MusicLibrary library = made.Track(MusicLibrary.Create(null));
            MusicDuck duck = made.Track(MusicDuck.Create(MusicDuckHolders.One, 0.5f));

            // A 0.3.0 asset stored InOutSine as 6 and carried no version field.
            SerializedObject storedLibrary = new(library);
            storedLibrary.FindProperty("_fadeInEase").enumValueIndex = 6;
            storedLibrary.FindProperty("_fadeOutEase").enumValueIndex = 0;
            storedLibrary.FindProperty("_easeVersion").intValue = 0;
            storedLibrary.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject storedDuck = new(duck);
            storedDuck.FindProperty("_applyEase").enumValueIndex = 0;
            storedDuck.FindProperty("_releaseEase").enumValueIndex = 6;
            storedDuck.FindProperty("_easeVersion").intValue = 0;
            storedDuck.ApplyModifiedPropertiesWithoutUndo();

            library.OnAfterDeserialize();
            duck.OnAfterDeserialize();

            Assert.That(library.FadeInEase, Is.EqualTo(MusicEase.InOutSine), "the stored 6 is still InOutSine");
            Assert.That(library.FadeOutEase, Is.EqualTo(MusicEase.Linear), "the stored 0 is still Linear");
            Assert.That(duck.ApplyEase, Is.EqualTo(MusicEase.Linear), "the stored 0 is still Linear");
            Assert.That(duck.ReleaseEase, Is.EqualTo(MusicEase.InOutSine), "the stored 6 is still InOutSine");
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
            MusicDuck filtered = made.Track(MusicDuck.Create(MusicDuckHolders.Many, volumeScale: 0.8f, lowPassCutoffHz: 4_000f, highPassCutoffHz: 300f));
            MusicDuck quietest = made.Track(MusicDuck.Create(MusicDuckHolders.One, 0.4f));

            MusicAttenuation composed = MusicDuckEditor.Compose(first, new[] { permissive, filtered, quietest, null, first });

            Assert.That(composed.volumeScale, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(composed.lowPassCutoffHz, Is.EqualTo(4_000f).Within(0.5f));
            Assert.That(composed.highPassCutoffHz, Is.EqualTo(300f).Within(0.5f));
            Assert.That(MusicDuckEditor.Compose(null, null).Equals(MusicAttenuation.Unattenuated), Is.True);
        }

        [Test]
        public void ACueInspectorOffersOneAuditionForEachTake()
        {
            using MadeObjects made = new();
            SfxCue cue = made.Track(SfxCue.Create(new[] { Variant(made), Variant(made) }, key: "step"));
            SfxCue empty = made.Track(SfxCue.Create(new SfxVariant[0]));

            List<Button> auditions = SfxCueEditor.BuildTakes(cue).Query<Button>("DxAudioAudition").ToList();

            Assert.That(auditions, Has.Count.EqualTo(2));
            Assert.That(auditions[0].text, Is.EqualTo("Audition"));
            Assert.That(SfxCueEditor.BuildTakes(empty).Q<Label>().text, Is.EqualTo("No takes yet. Add one above."));
        }

        [Test]
        public void AuditioningATakePlaysItFromTheCuesStart()
        {
            using MadeObjects made = new();
            SfxVariant variant = new(Clip(made, 1f), startTimeSeconds: 0.25f);
            SfxCue cue = made.Track(SfxCue.Create(new[] { variant }));

            AudioSource source = SfxAudition.Play(cue, variant);
            try
            {
                Assert.That(source != null, Is.True);
                Assert.That(source.isPlaying, Is.True, "the preview starts");
                Assert.That(source.clip == variant.Clip, Is.True, "the preview plays the take's clip");
                Assert.That(source.loop, Is.False);
                Assert.That(source.volume, Is.EqualTo(cue.Volume).Within(0.0001f));
                Assert.That(source.time, Is.EqualTo(cue.PlaybackStartSecondsFor(variant)).Within(0.001f));

                SfxAudition.Stop();
                Assert.That(source.isPlaying, Is.False);
            }
            finally
            {
                SfxAudition.DestroyPreview();
            }

            Assert.That(SfxAudition.Play(cue, new SfxVariant()) == null, Is.True, "a take with no clip has nothing to audition");
        }

        [Test]
        public void ADuckInspectorShowsWhatItsPreviewSetComposesTo()
        {
            using MadeObjects made = new();
            MusicDuck first = made.Track(MusicDuck.Create(MusicDuckHolders.One, 0.5f));
            MusicDuck quietest = made.Track(MusicDuck.Create(MusicDuckHolders.One, 0.4f));
            List<MusicDuck> preview = new() { first, quietest };

            VisualElement view = MusicDuckEditor.BuildComposedPreview(first, preview);
            Label composed = view.Q<Label>("DxAudioComposed");

            Assert.That(composed != null, Is.True);
            Assert.That(composed.text, Does.Contain("volume x0.4"), "the most restrictive volume wins");
            Assert.That(view.Query<ObjectField>().ToList(), Has.Count.EqualTo(2), "one row per held duck");
        }

        [Test]
        public void AMusicTrackInspectorOffersTheLoadTypeWrite()
        {
            const string FixtureGuid = "5081b21fa3e1420dbfaac900a77b4b24";
            AudioClip fixture = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(FixtureGuid));
            Assert.That(fixture != null, Is.True, "the fixture clip is missing");

            using MadeObjects made = new();
            MusicTrack mismatch = made.Track(MusicTrack.Create(fixture, context: "boss", clipLoadType: MusicClipLoadType.Streaming));
            MusicTrack matched = made.Track(MusicTrack.Create(Clip(made, 20f), context: "town"));

            UnityEditor.Editor mismatchEditor = null;
            UnityEditor.Editor matchedEditor = null;
            try
            {
                mismatchEditor = UnityEditor.Editor.CreateEditor(mismatch, typeof(MusicTrackEditor));
                matchedEditor = UnityEditor.Editor.CreateEditor(matched, typeof(MusicTrackEditor));
                VisualElement view = mismatchEditor.CreateInspectorGUI();
                VisualElement settled = matchedEditor.CreateInspectorGUI();
                Button apply = view.Q<Button>("DxAudioApplyClipImport");

                Assert.That(view.Q<Label>("DxAudioClipImportStatus").text, Does.Contain("the track asks for Streaming"));
                Assert.That(apply != null, Is.True);
                Assert.That(apply.text, Does.Contain("Streaming"));
                Assert.That(settled.Q<Button>("DxAudioApplyClipImport") == null, Is.True, "a clip that follows its track needs no write");
            }
            finally
            {
                if (mismatchEditor != null)
                {
                    Object.DestroyImmediate(mismatchEditor);
                }

                if (matchedEditor != null)
                {
                    Object.DestroyImmediate(matchedEditor);
                }
            }
        }

        [Test]
        public void ALibraryWindowReportNamesEveryProblem()
        {
            using MadeObjects made = new();
            SfxCue silent = made.Track(SfxCue.Create(new[] { new SfxVariant() }, key: "step"));
            SfxLibrary library = made.Track(SfxLibrary.Create(new[] { silent, null }));
            List<Object> libraries = new() { library };
            List<List<string>> findings = new() { DxAudioValidation.Validate(library) };

            VisualElement report = DxAudioLibraryWindow.BuildReport(libraries, findings);

            Assert.That(report.Q<Label>().text, Is.EqualTo("1 library, 2 problems"));
            Assert.That(report.Query<HelpBox>().ToList(), Has.Count.EqualTo(2));

            VisualElement empty = DxAudioLibraryWindow.BuildReport(new List<Object>(), new List<List<string>>());
            Assert.That(empty.Q<Label>().text, Is.EqualTo("0 libraries, 0 problems"));
            Assert.That(empty.Q<HelpBox>() != null, Is.True, "an empty project says so");
        }

        [Test]
        public void ALibraryWindowOffersARecheckButtonBesideItsSummary()
        {
            VisualElement content = DxAudioLibraryWindow.BuildWindowContent(() => { });
            Button checkAgain = content.Q<Button>();
            Label summary = content.Q<Label>();

            Assert.That(checkAgain != null, Is.True);
            Assert.That(checkAgain.text, Is.EqualTo("Check Again"));
            Assert.That(summary != null, Is.True);
            Assert.That(summary.text, Does.Match(@"^\d+ librar(y|ies), \d+ problems?$"));
        }

        [Test]
        public void ACueInspectorListsItsChainStagesInSignalOrder()
        {
            using MadeObjects made = new();
            DspChainAsset chain = made.Track(
                DspChainAsset.Create(new DspEffectAsset[] { made.Track(DspBitCrusherAsset.Create(bits: 6)), made.Track(DspLimiterAsset.Create(ceilingDecibels: -3f)) })
            );
            SfxCue cue = made.Track(SfxCue.Create(new[] { Variant(made) }, chain: chain));

            UnityEditor.Editor cueEditor = null;
            UnityEditor.Editor chainEditor = null;
            try
            {
                cueEditor = UnityEditor.Editor.CreateEditor(cue, typeof(SfxCueEditor));
                chainEditor = UnityEditor.Editor.CreateEditor(chain, typeof(DspChainAssetEditor));
                List<string> cueRows = Labels(cueEditor.CreateInspectorGUI());
                List<string> chainRows = Labels(chainEditor.CreateInspectorGUI());

                Assert.That(Joined(cueRows), Does.Contain("1. ").And.Contain("6 bit, every 1 frame"));
                Assert.That(Joined(cueRows), Does.Contain("2. ").And.Contain("ceiling -3 dB"));
                Assert.That(Order(Joined(cueRows), "1. ", "6 bit", "2. ", "ceiling -3 dB"), Is.True, "the rows follow the authored order");
                Assert.That(Joined(chainRows), Does.Contain("ceiling -3 dB"), "the chain's own inspector draws the same rows");
                Assert.That(Joined(cueRows), Does.Contain("A running voice shows its live gain reduction"), "no voice runs in edit mode");
            }
            finally
            {
                if (cueEditor != null)
                {
                    Object.DestroyImmediate(cueEditor);
                }

                if (chainEditor != null)
                {
                    Object.DestroyImmediate(chainEditor);
                }
            }
        }

        [Test]
        public void AChainInspectorNumbersAroundAnEmptySlot()
        {
            using MadeObjects made = new();
            DspChainAsset chain = made.Track(
                DspChainAsset.Create(new DspEffectAsset[] { made.Track(DspBitCrusherAsset.Create(bits: 6)), null, made.Track(DspLimiterAsset.Create()) })
            );
            UnityEditor.Editor editor = null;
            try
            {
                editor = UnityEditor.Editor.CreateEditor(chain, typeof(DspChainAssetEditor));
                string rows = Joined(Labels(editor.CreateInspectorGUI()));

                Assert.That(Order(rows, "1. ", "6 bit", "Stage 2: empty", "3. ", "DspLimiterAsset"), Is.True, "an empty slot keeps its own row");
            }
            finally
            {
                if (editor != null)
                {
                    Object.DestroyImmediate(editor);
                }
            }
        }

        /// <summary>Whether <paramref name="parts" /> appear in <paramref name="text" /> in the order given, each once.</summary>
        private static bool Order(string text, params string[] parts)
        {
            int from = 0;
            for (int index = 0; index < parts.Length; ++index)
            {
                int at = text.IndexOf(parts[index], from, System.StringComparison.Ordinal);
                if (at < 0)
                {
                    return false;
                }

                from = at + parts[index].Length;
            }

            return true;
        }

        private static List<string> Labels(VisualElement view)
        {
            List<string> texts = new();
            foreach (Label label in view.Query<Label>().ToList())
            {
                texts.Add(label.text);
            }

            return texts;
        }

        private static string Joined(List<string> findings)
        {
            return string.Join(" | ", findings);
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
