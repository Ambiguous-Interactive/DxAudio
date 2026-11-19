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
                panel.Query<Toggle>().Where(toggle => toggle.label == "Bypass variation on replay").First().SetValueWithoutNotify(true);
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

        private static SceneView FindOrCreateSceneView(MadeObjects made)
        {
            SceneView view = SceneView.lastActiveSceneView;
            return view != null ? view : made.Track(ScriptableObject.CreateInstance<SceneView>());
        }

        private static Button FindButton(VisualElement panel, string title)
        {
            Button button = panel.Query<Button>().Where(candidate => candidate.text == title).First();
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
                if (source.clip == clip && source.GetComponent<DspAuditionFilter>() != null)
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
    }
}
