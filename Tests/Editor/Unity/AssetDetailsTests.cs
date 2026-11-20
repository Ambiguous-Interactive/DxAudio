namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System.Collections.Generic;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.DxAudio.Editor;

    public sealed class AssetDetailsTests
    {
        private static AudioClip ConstantTake(MadeObjects made, float amplitude, int leadIn = 0)
        {
            AudioClip clip = made.Track(AudioClip.Create("Level fixture", 1_600, 1, 8_000, false));
            float[] samples = new float[clip.samples];
            for (int index = leadIn, samplesLength = samples.Length; index < samplesLength; ++index)
            {
                samples[index] = amplitude;
            }
            Assert.That(clip.SetData(samples, 0), Is.True);
            return clip;
        }

        private static SceneView FindOrCreateSceneView(MadeObjects made)
        {
            SceneView view = SceneView.lastActiveSceneView;
            return view != null ? view : made.Track(ScriptableObject.CreateInstance<SceneView>());
        }

        private static Button FindButton(VisualElement panel, string title)
        {
            Button button = panel.Query<Button>().Where(candidate => string.Equals(candidate.text, title, System.StringComparison.Ordinal)).First();
            Assert.That(button != null, Is.True, title);
            return button;
        }

        private static void Press(VisualElement panel, string title)
        {
            Button button = FindButton(panel, title);
            Assert.That(button.enabledSelf, Is.True, title);

            // Use the registered click handler without an EditorWindow or event loop.
            MethodInfo invoke = typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(invoke != null, Is.True);
            invoke.Invoke(button.clickable, new object[] { null });
        }

        private static AudioSource FindPreviewSource(AudioClip clip)
        {
            AudioSource[] sources = Resources.FindObjectsOfTypeAll<AudioSource>();
            foreach (AudioSource source in sources)
            {
                if (source.clip == clip && source.TryGetComponent<DspAuditionFilter>(out _))
                {
                    return source;
                }
            }

            Assert.Fail($"No cue audition source was configured for {clip.name}.");
            return null;
        }

        private static void AssertPreview(SfxPlayback draw)
        {
            AudioSource source = FindPreviewSource(draw.Variant.Clip);
            Assert.That(source.pitch, Is.EqualTo(draw.Pitch).Within(0.0001f));
            Assert.That(source.volume, Is.EqualTo(draw.Volume).Within(0.0001f));
        }

        [Test]
        public void LibrarySearchMatchesKeysAndNamesAndKeepsMembershipOrder()
        {
            using MadeObjects made = new();
            SfxCue first = made.Track(SfxCue.Create(null, key: "impact.iron"));
            first.name = "Hit";
            SfxCue second = made.Track(SfxCue.Create(null, key: "footstep"));
            second.name = "Iron Boot";
            SfxLibrary library = made.Track(SfxLibrary.Create(new[] { first, null, second }));

            List<DxAudioAssetDetails.Member> members = new();
            DxAudioAssetDetails.CollectMembers(library, " iron ", members);
            Assert.That(members.Count, Is.EqualTo(2));
            Assert.That(members[0].Asset, Is.SameAs(first));
            Assert.That(members[1].Asset, Is.SameAs(second));
            Assert.That(members[1].Slot, Is.EqualTo(3));
            DxAudioAssetDetails.CollectMembers(library, string.Empty, members);
            Assert.That(members.Count, Is.EqualTo(3));
            Assert.That(members[1].Asset == null, Is.True);
        }

        [Test]
        public void LibrarySearchMatchesMusicContexts()
        {
            using MadeObjects made = new();
            MusicTrack track = made.Track(MusicTrack.Create(null, context: "Battle"));
            MusicLibrary library = made.Track(MusicLibrary.Create(new[] { track }));
            List<DxAudioAssetDetails.Member> members = new();

            DxAudioAssetDetails.CollectMembers(library, "battle", members);

            Assert.That(members.Count, Is.EqualTo(1));
            Assert.That(members[0].Asset, Is.SameAs(track));
        }

        [Test]
        public void TrackAuditionUsesTheAuthoredOffsetAndTrim()
        {
            using MadeObjects made = new();
            AudioClip clip = made.Track(AudioClip.Create("Track", 48_000, 1, 48_000, false));
            MusicTrack track = made.Track(MusicTrack.Create(clip, startTimeSeconds: 0.25f, volumeScale: 0.4f));
            try
            {
                AudioSource source = SfxAudition.Play(track);
                Assert.That(source.clip, Is.SameAs(clip));
                Assert.That(source.volume, Is.EqualTo(0.4f));
                Assert.That(source.loop, Is.False);
                Assert.That(source.time, Is.EqualTo(track.StartTimeSeconds).Within(0.001f));
            }
            finally
            {
                SfxAudition.DestroyPreview();
            }
        }

        [TestCase(false, TestName = "Stop releases a cue audition source")]
        [TestCase(true, TestName = "Switching assets releases a cue series source")]
        public void CueTransportReturnsTheSourceAndSceneAudioToTheirIdleState(bool switchAsset)
        {
            using MadeObjects made = new();
            SceneView view = FindOrCreateSceneView(made);
            bool sceneAudio = view.audioPlay;
            AudioClip clip = made.Track(AudioClip.Create("Audition take", 48_000, 1, 48_000, false));
            SfxCue cue = made.Track(SfxCue.Create(new[] { new SfxVariant(clip) }));
            DspAuditionPanel panel = new(_ => { }, () => { });
            panel.SetTarget(cue);

            try
            {
                Press(panel, switchAsset ? "Play Series" : "Play Cue");
                AudioSource source = FindPreviewSource(clip);

                if (switchAsset)
                {
                    panel.SetTarget(made.Track(SfxCue.Create(null)));
                    Assert.That(FindButton(panel, "Replay Last").enabledSelf, Is.False);
                }
                else
                {
                    Press(panel, "Stop");
                    Assert.That(FindButton(panel, "Replay Last").enabledSelf, Is.True);
                }

                Assert.That(source == null, Is.True);
                Assert.That(view.audioPlay, Is.EqualTo(sceneAudio));
            }
            finally
            {
                panel.Stop();
                view.audioPlay = sceneAudio;
            }
        }

        [Test]
        public void CueReplayCanBypassVariationAndLeavesTheSeededSequenceInPlace()
        {
            using MadeObjects made = new();
            SceneView view = FindOrCreateSceneView(made);
            bool sceneAudio = view.audioPlay;
            AudioClip first = made.Track(AudioClip.Create("First take", 48_000, 1, 48_000, false));
            AudioClip second = made.Track(AudioClip.Create("Second take", 48_000, 1, 48_000, false));
            SfxVariation variation = new(true, 600f, 1_200f, -6f, -3f);
            SfxCue cue = made.Track(SfxCue.CreateWithVariation(new[] { new SfxVariant(first), new SfxVariant(second) }, variation, volume: 0.5f));
            DspAuditionPanel panel = new(_ => { }, () => { });
            panel.SetTarget(cue);
            SfxPlaybackResolver expected = new();
            expected.Reset(panel.Q<IntegerField>().value);
            Assert.That(expected.TryResolve(cue, out SfxPlayback firstDraw), Is.True);
            Assert.That(expected.TryResolve(cue, out SfxPlayback secondDraw), Is.True);

            try
            {
                Press(panel, "Play Cue");
                AssertPreview(firstDraw);
                Press(panel, "Stop");
                panel
                    .Query<Toggle>()
                    .Where(toggle => string.Equals(toggle.label, "Bypass variation on replay", System.StringComparison.Ordinal))
                    .First()
                    .SetValueWithoutNotify(true);
                Press(panel, "Replay Last");
                AudioSource replay = FindPreviewSource(firstDraw.Variant.Clip);
                Assert.That(replay.pitch, Is.EqualTo(1f));
                Assert.That(replay.volume, Is.EqualTo(cue.Volume));
                Press(panel, "Stop");

                Press(panel, "Play Cue");
                AssertPreview(secondDraw);
                Press(panel, "Reset Seed");
                Assert.That(FindButton(panel, "Replay Last").enabledSelf, Is.False);
                Press(panel, "Play Cue");
                AssertPreview(firstDraw);
            }
            finally
            {
                panel.Stop();
                view.audioPlay = sceneAudio;
            }
        }

        [Test]
        public void ComparisonReplaysTheCapturedSettingsAndKeepsTheLiveSequenceInPlace()
        {
            using MadeObjects made = new();
            SceneView view = FindOrCreateSceneView(made);
            bool sceneAudio = view.audioPlay;
            AudioClip first = made.Track(AudioClip.Create("First comparison take", 48_000, 1, 48_000, false));
            AudioClip second = made.Track(AudioClip.Create("Second comparison take", 48_000, 1, 48_000, false));
            SfxCue cue = made.Track(SfxCue.Create(new[] { new SfxVariant(first), new SfxVariant(second) }));
            DspAuditionPanel panel = new(_ => { }, () => { });
            panel.SetTarget(cue);
            SfxPlaybackResolver expected = new();
            expected.Reset(panel.Q<IntegerField>().value);
            Assert.That(expected.TryResolve(cue, out SfxPlayback firstDraw), Is.True);
            Assert.That(expected.TryResolve(cue, out SfxPlayback secondDraw), Is.True);

            try
            {
                Press(panel, "Play Cue");
                Press(panel, "Stop");
                Press(panel, "Capture A");
                SerializedObject edit = new(cue);
                SerializedProperty variation = edit.FindProperty("_variation");
                variation.FindPropertyRelative(nameof(SfxVariation.UsePitchCents)).boolValue = true;
                variation.FindPropertyRelative(nameof(SfxVariation.MinimumPitchCents)).floatValue = 1200f;
                variation.FindPropertyRelative(nameof(SfxVariation.MaximumPitchCents)).floatValue = 1200f;
                edit.ApplyModifiedPropertiesWithoutUndo();

                Press(panel, "Play A");
                AssertPreview(firstDraw);
                Press(panel, "Stop");
                Press(panel, "Play B");
                Assert.That(FindPreviewSource(firstDraw.Variant.Clip).pitch, Is.EqualTo(2f));
                Press(panel, "Stop");
                Press(panel, "Replay Last");
                AssertPreview(firstDraw);
                Press(panel, "Stop");
                Press(panel, "Play Cue");
                Assert.That(FindPreviewSource(secondDraw.Variant.Clip).pitch, Is.EqualTo(2f));
            }
            finally
            {
                panel.Stop();
                view.audioPlay = sceneAudio;
            }
        }

        [Test]
        public void EditingVolumeDuringAComparisonDoesNotRestartTheShuffleCycle()
        {
            using MadeObjects made = new();
            SceneView view = FindOrCreateSceneView(made);
            bool sceneAudio = view.audioPlay;
            AudioClip[] clips = { ConstantTake(made, 0.2f), ConstantTake(made, 0.3f), ConstantTake(made, 0.4f) };
            SfxCue cue = made.Track(SfxCueCreation.Build(clips));
            DspAuditionPanel panel = new(_ => { }, () => { });
            panel.SetTarget(cue);
            const int Seed = 2;
            panel.Q<IntegerField>().value = Seed;
            SfxPlaybackResolver expected = new();
            expected.Reset(Seed);
            Assert.That(expected.TryResolve(cue, out _), Is.True);
            Assert.That(expected.TryResolve(cue, out _), Is.True);
            Assert.That(expected.TryResolve(cue, out SfxPlayback third), Is.True);

            MethodInfo next = typeof(DspAuditionPanel).GetMethod("PlayNextTake", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                Press(panel, "Capture A");
                Press(panel, "Play A");
                next.Invoke(panel, null);

                int revision = cue.TakeRevision;
                using SerializedObject edit = new(cue);
                edit.FindProperty("_volume").floatValue = 0.5f;
                edit.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(cue.TakeRevision, Is.Not.EqualTo(revision));

                next.Invoke(panel, null);
                AssertPreview(third);
            }
            finally
            {
                panel.Stop();
                view.audioPlay = sceneAudio;
            }
        }

        [Test]
        public void AddingSeveralClipsStartsEachTakeWithNeutralSettings()
        {
            using MadeObjects made = new();
            AudioClip existing = made.Track(AudioClip.Create("Existing", 48_000, 1, 48_000, false));
            AudioClip first = made.Track(AudioClip.Create("First addition", 48_000, 1, 48_000, false));
            AudioClip second = made.Track(AudioClip.Create("Second addition", 48_000, 1, 48_000, false));
            SfxCue cue = made.Track(SfxCue.Create(new[] { new SfxVariant(existing, 0.2f, -6f) }));

            Assert.That(SfxCueEditor.AppendTakes(new SerializedObject(cue), new Object[] { first, cue, second }), Is.True);
            Assert.That(cue.Variants.Count, Is.EqualTo(3));
            Assert.That(cue.Variants[0].AttenuationDecibels, Is.EqualTo(-6f));
            Assert.That(cue.Variants[1].Clip == first, Is.True);
            Assert.That(cue.Variants[2].Clip == second, Is.True);
            for (int index = 1; index < cue.Variants.Count; ++index)
            {
                Assert.That(cue.Variants[index].StartTimeSeconds, Is.Zero);
                Assert.That(cue.Variants[index].AttenuationDecibels, Is.Zero);
            }
        }

        [TestCase("Pitch Stable", 0f, 0f)]
        [TestCase("Subtle", 50f, -1.5f)]
        public void ApplyingAPresetCanBeUndoneAndLeavesShuffleAndCutoffAlone(string preset, float pitch, float level)
        {
            using MadeObjects made = new();
            SfxVariation original = new(false, 12f, 24f, -7f, -3f)
            {
                UseShuffle = true,
                UseLowPass = true,
                MinimumLowPassHertz = 4_000f,
                MaximumLowPassHertz = 8_000f,
            };
            SfxCue cue = made.Track(SfxCue.CreateWithVariation(null, original));
            SerializedObject asset = new(cue);
            VisualElement view = new SfxVariationDrawer().CreatePropertyGUI(asset.FindProperty("_variation"));
            Undo.IncrementCurrentGroup();
            Press(view, preset);
            Undo.FlushUndoRecordObjects();

            Assert.That(cue.Variation.UsePitchCents, Is.True);
            Assert.That(cue.Variation.MinimumPitchCents, Is.EqualTo(-pitch));
            Assert.That(cue.Variation.MaximumPitchCents, Is.EqualTo(pitch));
            Assert.That(cue.Variation.MinimumAttenuationDecibels, Is.EqualTo(level));
            Assert.That(cue.Variation.MaximumAttenuationDecibels, Is.Zero);
            Assert.That(cue.Variation.UseShuffle, Is.True);
            Assert.That(cue.Variation.UseLowPass, Is.True);
            Assert.That(cue.Variation.MinimumLowPassHertz, Is.EqualTo(4_000f));
            Assert.That(cue.Variation.MaximumLowPassHertz, Is.EqualTo(8_000f));

            Undo.PerformUndo();
            Assert.That(cue.Variation, Is.EqualTo(original));
        }

        [Test]
        public void EnablingLowPassOnSeveralCuesKeepsAuthoredCutoffs()
        {
            using MadeObjects made = new();
            SfxVariation variation = new() { MinimumLowPassHertz = 4_000f, MaximumLowPassHertz = 12_000f };
            SfxCue authored = made.Track(SfxCue.CreateWithVariation(null, variation));
            SfxCue untouched = made.Track(SfxCue.Create(null));
            SerializedObject edit = new(new Object[] { authored, untouched });
            Undo.IncrementCurrentGroup();
            SfxVariationDrawer.SetLowPass(edit, "_variation", true);

            Assert.That(authored.Variation.UseLowPass, Is.True);
            Assert.That(authored.Variation.MinimumLowPassHertz, Is.EqualTo(4_000f));
            Assert.That(authored.Variation.MaximumLowPassHertz, Is.EqualTo(12_000f));
            Assert.That(untouched.Variation.UseLowPass, Is.True);
            Assert.That(untouched.Variation.MinimumLowPassHertz, Is.EqualTo(8_000f));
            Assert.That(untouched.Variation.MaximumLowPassHertz, Is.EqualTo(16_000f));

            Undo.PerformUndo();
            Assert.That(authored.Variation, Is.EqualTo(variation));
            Assert.That(untouched.Variation, Is.EqualTo(default(SfxVariation)));
        }

        [Test]
        public void ClipDefaultsBalanceReadableTakesAndIgnoreSilence()
        {
            using MadeObjects made = new();
            AudioClip loud = ConstantTake(made, 0.5f);
            AudioClip quiet = ConstantTake(made, 0.25f);
            AudioClip quietWithLeadIn = ConstantTake(made, 0.25f, leadIn: 800);
            AudioClip silent = ConstantTake(made, 0f);
            float[] levels = SfxCueClipDefaults.AttenuationsFor(new[] { loud, quiet, quietWithLeadIn, silent });

            Assert.That(levels, Is.EqualTo(new[] { -6f, 0f, 0f, 0f }).Within(0.001f));
        }

        [Test]
        public void ClipDefaultsLeaveHeadroomWithoutRequiringEveryClipToBeReadable()
        {
            using MadeObjects made = new();
            AudioClip loud = ConstantTake(made, 0.9f);
            AudioClip longClip = made.Track(AudioClip.Create("Long take", 88_000, 1, 8_000, false));
            float[] levels = SfxCueClipDefaults.AttenuationsFor(new[] { loud, longClip });

            Assert.That(levels[0], Is.EqualTo(-2.08485f).Within(0.001f));
            Assert.That(levels[1], Is.Zero);
        }

        [Test]
        public void ClipDefaultsBalanceAgainstTheTakeAfterPeakReduction()
        {
            using MadeObjects made = new();
            AudioClip transient = ConstantTake(made, 0.2f);
            Assert.That(transient.SetData(new[] { 1f }, 0), Is.True);
            AudioClip steady = ConstantTake(made, 0.25f);
            float[] levels = SfxCueClipDefaults.AttenuationsFor(new[] { transient, steady });
            Assert.That(levels[0], Is.EqualTo(-3f).Within(0.001f));
            Assert.That(levels[1], Is.EqualTo(-4.685f).Within(0.001f));
        }

        [Test]
        public void CreatingACueFromClipsUsesStablePitchAndUniqueOrderedTakes()
        {
            using MadeObjects made = new();
            AudioClip loud = ConstantTake(made, 0.5f);
            AudioClip quiet = ConstantTake(made, 0.25f);
            quiet.name = "A";
            loud.name = "B";
            SfxCue cue = made.Track(SfxCueCreation.Build(new[] { loud, quiet, loud, null }));
            Assert.That(cue.Variants.Count, Is.EqualTo(2));
            Assert.That(cue.Variants[0].Clip == quiet, Is.True);
            Assert.That(cue.Variants[1].AttenuationDecibels, Is.EqualTo(-6f).Within(0.001f));
            Assert.That(cue.Variation.UseShuffle, Is.True);
            Assert.That(cue.Variation.UseLowPass, Is.False);
            Assert.That(cue.PitchFor(0f), Is.EqualTo(1f));
            Assert.That(cue.PitchFor(1f), Is.EqualTo(1f));

            SfxCue one = made.Track(SfxCueCreation.Build(new[] { quiet }));
            Assert.That(one.Variation.UseShuffle, Is.False);
        }

        [Test]
        public void EditingATrimInvalidatesTheCapturedComparison()
        {
            using MadeObjects made = new();
            AudioClip clip = made.Track(AudioClip.Create("Comparison trim", 48_000, 1, 48_000, false));
            SfxCue cue = made.Track(SfxCue.Create(new[] { new SfxVariant(clip) }));
            DspAuditionPanel panel = new(_ => { }, () => { });
            panel.SetTarget(cue);
            Press(panel, "Capture A");

            SerializedObject edit = new(cue);
            edit.FindProperty("_variants").GetArrayElementAtIndex(0).FindPropertyRelative("_startTimeSeconds").floatValue = 0.1f;
            edit.ApplyModifiedPropertiesWithoutUndo();
            Press(panel, "Play A");

            Assert.That(FindButton(panel, "Play A").enabledSelf, Is.False);
            Assert.That(FindButton(panel, "Play B").enabledSelf, Is.False);
            Assert.That(FindButton(panel, "Capture A").enabledSelf, Is.True);
        }

        [Test]
        public void RawTakeAuditionKeepsSceneAudioThroughACuePreviewHandoff()
        {
            using MadeObjects made = new();
            SceneView active = FindOrCreateSceneView(made);
            List<SceneView> enabledViews = new();
            foreach (SceneView view in SceneView.sceneViews)
            {
                if (view != null && view.audioPlay)
                {
                    enabledViews.Add(view);
                    view.audioPlay = false;
                }
            }

            AudioClip clip = made.Track(AudioClip.Create("Audition take", 48_000, 1, 48_000, false));
            SfxVariant take = new(clip);
            SfxCue cue = made.Track(SfxCue.Create(new[] { take }));
            DspAuditionPanel panel = new(_ => { }, () => { });
            panel.SetTarget(cue);
            try
            {
                Press(panel, "Play Cue");
                Assert.That(active.audioPlay, Is.True);
                AudioSource cuePreview = FindPreviewSource(clip);

                AudioSource quickPreview = SfxAudition.Play(cue, take);
                Assert.That(cuePreview == null, Is.True);
                Assert.That(quickPreview != null, Is.True);
                Assert.That(active.audioPlay, Is.True);
                SfxAudition.Stop();
                Assert.That(active.audioPlay, Is.False);
            }
            finally
            {
                panel.Stop();
                SfxAudition.DestroyPreview();
                foreach (SceneView view in enabledViews)
                {
                    view.audioPlay = true;
                }
            }
        }
    }
}
